// GameCore.Validation.ProbeHost — the GC-018 checkpoint/restore qualification scenario.
//
// The gate sentence this file implements, from `docs/game-core/09-implementation-guide.md` (GC-018), is the
// task's own probe sentence: "active+dormant state round-trips without handles" (P-053, P-054), with O-20's and
// O-21's acceptance beside it:
//
//   O-20 "snapshot boundary, command inclusion option → versioned blob and checksum ... Copy/serialization
//    errors produce no checkpoint; cancel before completed file publication, remove temp artifact";
//   O-21 "verified blob → new WorldId and fully published restored world ... Unsupported schema/migration
//    rejects without affecting existing world; cancel destroys staged world; duplicate returns same restored
//    session. Probe: old callback cannot target restored entity."
//
// One runner, two family adapters (`IGc018Family`), exactly as GC-013's and the Wave 4 gate's sequences have one
// runner and two adapters. The world plumbing is the same chain of real modules those scenarios build, because it
// is the same chain in both genres:
//
//   * `UnityWorldRegistry.TryCreate` + the family's generated registration  → a real owned world with real ECS
//     storage (GC-005, GC-009);
//   * `TargetRegistry` + `LiveTargetIndex` + `LiveTargetSeeder`            → the live targets of the family, each
//     seeded as real ECS storage with its own active and dormant state slots (GC-008, P-010, P-032);
//   * `CompositionHost` + the family's declared lane seed                  → the real control lane, so the captured
//     composition (scope tree, boundaries, mode) is committed state and not a test-local copy (GC-004, P-006);
//   * `DerivedAssemblyPipeline`                                            → the real derivation, so the restored
//     world's capabilities are rederived over the seeded targets rather than asserted (GC-006, 06 s7);
//   * `WorldTimeDriver` + a real `PluginClockRegistry`                      → a persistent clock and a pending wake,
//     which are the clock facts P-053 requires a checkpoint to carry (GC-009, P-038);
//   * `RngStreamTable`                                                      → the deterministic stream positions P-008
//     and P-053 require, seeded and drawn from before the capture;
//   * `UnityCommittedBoundaryReader` + `CheckpointCodecSet` over the generated `CheckpointCatalog` serializers +
//     `CheckpointCapture`                                                    → the real capture half (O-20);
//   * `CheckpointRestorePlanner` + `CheckpointRestoreExecutor` + a real `IRestoreTargetBuilder` → the real restore
//     half into an **unexposed** world created by `UnityWorldHost.TryCreateUnexposed` (O-21).
//
// Nothing here re-implements a kernel module and nothing is discovered reflectively. The scripted sequence is the
// task's own A-E in the order the specification names it, and every observation is a named `Gc018Step` carrying
// the values it was computed from: a step never records a claim it did not read, and no helper returns a silent
// false — a method that cannot do what its name says throws with the code and detail it received.
//
// The captured bytes are compared with a *second* capture taken of the restored world through the same reader, so
// every "the same ... survives" claim is a comparison of two documents rather than a claim about the plan. The
// only fields excluded from those comparisons are the ones the protocol says must differ: a native
// `TargetRecordValue.SourceSlot`/`SourceGeneration` (P-005 stale evidence, never a mapping) and the session a
// cursor was captured in (re-stamped by the restored world, P-004).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Persistence;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Time;
using GameCore.Validation.GeneratedCheckpoint;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named GC-018 observation: what was checked and the values it was computed from.</summary>
    public sealed class Gc018Step
    {
        public Gc018Step(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        public string Name { get; }

        public bool Passed { get; }

        public string Detail { get; }

        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Full result of one GC-018 run: the named observations plus one digest over them, computed with the same
    /// digest function the narrative trace uses. A run that records a different set of observations, or a failing
    /// one, cannot report the expected digest.
    /// </summary>
    public sealed class Gc018ScenarioResult
    {
        public Gc018ScenarioResult(string label, IReadOnlyList<Gc018Step> steps)
        {
            Label = label;
            Steps = steps;
            var lines = new List<string>(steps.Count);
            bool allPassed = steps.Count > 0;
            for (int i = 0; i < steps.Count; i++)
            {
                lines.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
                allPassed &= steps[i].Passed;
            }

            AllPassed = allPassed;
            Digest = NarrativeDigest.OfLines(lines);
        }

        /// <summary>The family label every observation name of this run is qualified with.</summary>
        public string Label { get; }

        /// <summary>The named observations, in execution order, qualified with <see cref="Label"/>.</summary>
        public IReadOnlyList<Gc018Step> Steps { get; }

        /// <summary>Canonical digest over this run's observation names and pass flags.</summary>
        public string Digest { get; }

        public bool AllPassed { get; }

        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].ToString());
                }
            }

            return "digest=" + Digest
                + "; label=" + Label
                + "; steps=" + Steps.Count.ToString(CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }
    }

    /// <summary>
    /// One decoded checkpoint document: the header plus every record category, so two documents can be compared
    /// field by field instead of by byte equality (a restored world legitimately differs in its cursor session
    /// stamps and in its per-target source-slot evidence, and nowhere else).
    /// </summary>
    public sealed class Gc018Image
    {
        public HeaderRecordValue Header;
        public byte[] Bytes = Array.Empty<byte>();
        public IReadOnlyList<ScopeRecordValue> Scopes = Array.Empty<ScopeRecordValue>();
        public IReadOnlyList<InstallRecordValue> Installs = Array.Empty<InstallRecordValue>();
        public IReadOnlyList<SelectionRecordValue> Selections = Array.Empty<SelectionRecordValue>();
        public IReadOnlyList<TargetRecordValue> Targets = Array.Empty<TargetRecordValue>();
        public IReadOnlyList<SlotRecordValue> Slots = Array.Empty<SlotRecordValue>();
        public IReadOnlyList<GrantRecordValue> Grants = Array.Empty<GrantRecordValue>();
        public IReadOnlyList<ClockRecordValue> Clocks = Array.Empty<ClockRecordValue>();
        public IReadOnlyList<CommandRecordValue> Commands = Array.Empty<CommandRecordValue>();
        public IReadOnlyList<MessageRecordValue> Messages = Array.Empty<MessageRecordValue>();
        public IReadOnlyList<RngRecordValue> RngStreams = Array.Empty<RngRecordValue>();
        public IReadOnlyList<CursorRecordValue> Cursors = Array.Empty<CursorRecordValue>();
    }

    /// <summary>
    /// The real O-21 rebuilder: it creates the reserved session's world **unexposed**, reconstructs the captured
    /// composition (scope tree, boundaries, mode) through the real control lane, reinstalls the captured clocks and
    /// random streams, seeds every captured target and its owner state, re-admits the captured external commands,
    /// and finally re-derives the assembly so the restored world's capability rows exist (06 s7: "Build an
    /// unexposed world with a fresh WorldId, reconstruct scope/target stable IDs, rederive capabilities, allocate
    /// recipes, restore owner slots, repair stable references, restore clocks/RNG/outbox cursors, then publish").
    ///
    /// A refusal is always a coded value with a detail the caller can act on: nothing here returns a silent false,
    /// and nothing here is reached at all when the planner has already refused (the executor calls a builder only
    /// for a plan).
    /// </summary>
    public sealed class Gc018FamilyRestoreBuilder : IRestoreTargetBuilder
    {
        private const ulong StagedByteCeiling = 1024UL * 1024UL;
        private const ulong ScratchCapacityBytes = 4096UL;
        private const ulong ScratchBytesPerSlot = 64UL;
        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        private readonly IGc018Family family;
        private readonly PipelineDescriptorReport descriptor;
        private readonly ContentHash catalogFingerprint;
        private readonly int registryBaseline;
        private readonly Func<WorldId, OperationId> nextOperation;

        private UnityWorldHost? staging;
        private TargetRegistry? registry;
        private LiveTargetIndex? targets;
        private LiveTargetSeeder? seeder;
        private AssemblyPublisher? publisher;
        private CompositionHost? lane;
        private WorldCompositionBridge? bridge;
        private DerivedAssemblyPipeline? pipeline;
        private WorldTimeDriver? time;
        private RngStreamTable? rng;
        private Gc018RuntimeWorld? runtime;

        public Gc018FamilyRestoreBuilder(
            IGc018Family family,
            PipelineDescriptorReport descriptor,
            ContentHash catalogFingerprint,
            int registryBaseline,
            Func<WorldId, OperationId> nextOperation)
        {
            this.family = family ?? throw new ArgumentNullException(nameof(family));
            this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            this.catalogFingerprint = catalogFingerprint;
            this.registryBaseline = registryBaseline;
            this.nextOperation = nextOperation ?? throw new ArgumentNullException(nameof(nextOperation));
        }

        /// <summary>Attempts this builder was asked to perform; zero proves a refused restore never staged one.</summary>
        public int BuildCount { get; private set; }

        /// <summary>Registry size observed right after the unexposed world was created (P-049).</summary>
        public int RegistryCountAfterStaging { get; private set; } = -1;

        /// <summary>Targets seeded from the plan.</summary>
        public int RebuiltTargetCount { get; private set; }

        /// <summary>Restored targets whose recipe base layout was installed from the family's own applier (P-024).</summary>
        public int BaseLayoutCount { get; private set; }

        /// <summary>Owner state rows seeded from the plan, active and dormant.</summary>
        public int RebuiltSlotCount { get; private set; }

        /// <summary>Dormant rows among them (P-032).</summary>
        public int RebuiltDormantSlotCount { get; private set; }

        /// <summary>Captured scopes the declared tree did not already carry and which were created (P-010).</summary>
        public int ReplayedScopeCount { get; private set; }

        /// <summary>Captured scope boundaries (isolation set or exclusion) replayed as real edits (P-016).</summary>
        public int ReplayedBoundaryCount { get; private set; }

        /// <summary>Captured provider installations replayed as real mount edits before scope imports can name them (P-013).</summary>
        public int ReplayedInstallCount { get; private set; }

        /// <summary>Captured Conservative-mode scope imports replayed as real grant edits (P-013).</summary>
        public int ReplayedImportCount { get; private set; }

        /// <summary>Captured mode edits replayed, zero when the declared mode is already the captured one (P-013).</summary>
        public int ReplayedModeCount { get; private set; }

        /// <summary>Captured persistent clocks re-registered (P-038, P-053).</summary>
        public int RebuiltClockCount { get; private set; }

        /// <summary>Captured pending wakes re-scheduled with their remaining delay (P-038, P-053).</summary>
        public int RebuiltWakeCount { get; private set; }

        /// <summary>Captured external commands re-admitted on the restored session (P-037, P-053).</summary>
        public int ReadmittedCommandCount { get; private set; }

        /// <summary>Assembly publications the rebuild performed, the last one being the rederivation (06 s7).</summary>
        public int PublishedAssemblyCount { get; private set; }

        public UnityWorldHost? Staging => staging;

        /// <summary>Owner-state rows removed because the plan names no such slot: the restored world carries the
        /// captured state and nothing else (P-032, P-053).</summary>
        public int PrunedSlotCount { get; private set; }

        public TargetRegistry? Registry => registry;

        public LiveTargetIndex? Targets => targets;

        public LiveTargetSeeder? Seeder => seeder;

        public CompositionHost? Lane => lane;

        public AssemblyPublisher? Publisher => publisher;

        public WorldTimeDriver? Time => time;

        public RngStreamTable? Rng => rng;

        /// <summary>The restored world's family runtime, attached by the build and released by
        /// <see cref="DetachRuntime"/> at teardown (P-042, P-048).</summary>
        public Gc018RuntimeWorld? Runtime => runtime;

        /// <summary>
        /// Releases the family runtime module the build attached to the restored world. Called by the scenario's
        /// teardown whether the world passed or faulted, because the module outlives no world (P-048).
        /// </summary>
        public void DetachRuntime()
        {
            if (runtime == null)
            {
                return;
            }

            family.DetachRuntime(runtime);
            runtime = null;
        }

        /// <summary>Registry size the restore started from; the unexposed world must not change it (P-049).</summary>
        public int RegistryBaseline => registryBaseline;

        public bool TryBuild(
            WorldId session,
            RestorePlan plan,
            int migrationCount,
            out UnityWorldHost? built,
            out DiagnosticCode code,
            out string detail)
        {
            BuildCount++;
            built = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (plan == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the restore builder was called without a plan.";
                return false;
            }

            if (migrationCount != 0 || plan.Migrations.Count != 0)
            {
                code = DiagnosticCode.MigrationRequired;
                detail = "this builder restores direct plans only; the plan carries "
                    + plan.Migrations.Count.ToString(CultureInfo.InvariantCulture)
                    + " migration(s), and running a migration is a separate rebuild path.";
                return false;
            }

            // O-21's "C/unexposed new world→B": the reserved session's world exists and is fully applied, and no
            // caller can route to it until the executor exposes it (P-049).
            WorldCreateRequest request = family.CreateRequest(session, nextOperation(session));
            WorldCreateResult created = UnityWorldHost.TryCreateUnexposed(
                request, family.CreateRegistration(descriptor.Adaptation!), out UnityWorldHost? createdHost);
            staging = createdHost;
            if (!created.Created || staging == null)
            {
                code = created.Code;
                detail = "creating the unexposed world for session " + session.Session.ToString() + " failed: "
                    + created.Code + ": " + created.Detail;
                return false;
            }

            RegistryCountAfterStaging = UnityWorldRegistry.Count;

            registry = new TargetRegistry(session, 16);
            publisher = new AssemblyPublisher(
                staging, registry, family.CreateRecipes(), family.CreateMigrations(), descriptor.Descriptor!);
            targets = new LiveTargetIndex(publisher.Recipes);
            seeder = new LiveTargetSeeder(staging, registry, targets);

            IDerivationValueSource values = family.CreateValues();
            lane = CompositionHost.CreateDefault(
                session,
                family.WorldRootScope,
                new CatalogManifestSource(family.Catalog, family.Declarations),
                null,
                family.LaneSeed,
                new DerivationModeSwitchValidator(values, TargetView));
            bridge = new WorldCompositionBridge(staging, lane, publisher);
            pipeline = new DerivedAssemblyPipeline(
                staging,
                lane,
                publisher,
                targets,
                seeder,
                values,
                null,
                null,
                publisher.Migrations,
                new StagedResourceGate(StagedByteCeiling, family.Issuer),
                new PlanBudget(PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));
            time = new WorldTimeDriver(staging, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);

            if (!RebuildClocks(plan, out code, out detail))
            {
                return false;
            }

            if (!RngStreamTable.TryRestore(plan.RngStreams, out RngStreamTable? restoredRng, out string rngDetail)
                || restoredRng == null)
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "rebuilding the captured random streams was refused: " + rngDetail;
                return false;
            }

            rng = restoredRng;

            if (!SeedTargets(plan, out code, out detail))
            {
                return false;
            }

            if (!ReplayComposition(plan, out code, out detail))
            {
                return false;
            }

            for (int i = 0; i < plan.Commands.Count; i++)
            {
                CommandRecordValue command = plan.Commands[i];
                var envelope = new CommandEnvelope(
                    command.RequestIdIn(session),
                    command.Route,
                    command.Target,
                    command.Schema,
                    null,
                    command.Frozen);
                CommandAdmissionReceipt receipt = staging.Submit(envelope);
                if (!receipt.Admitted)
                {
                    code = receipt.Result.Reason;
                    detail = "re-admitting captured command " + command.RequestIdIn(session).ToString()
                        + " on the restored session was refused: " + receipt.Result.Kind + "/"
                        + receipt.Result.Reason + ".";
                    return false;
                }

                ReadmittedCommandCount++;
            }

            // The restored world is exposed as a running world, and its first pump executes the re-admitted
            // command: the family's own runtime module — the one its generated step systems resolve their world
            // through — must be attached with every live target mapped, or the input stage would no-op and leave
            // the ingress lane unconsumed at commit (P-042, P-043).
            var runtimeWorld = new Gc018RuntimeWorld(
                staging, targets!, seeder!, descriptor.Compilation!.Schedule!);
            if (!family.TryAttachRuntime(runtimeWorld, out detail))
            {
                code = DiagnosticCode.MissingDependency;
                return false;
            }

            runtime = runtimeWorld;
            built = staging;
            return true;
        }

        public bool TryCreateCaptureContext(out CaptureContext? context, out string detail)
        {
            context = null;
            if (staging == null || registry == null || targets == null || lane == null || publisher == null
                || time == null || rng == null)
            {
                detail = "the restored world is not built yet, so it has no capture context.";
                return false;
            }

            context = new CaptureContext(
                staging.World,
                staging.Request.Definition,
                catalogFingerprint,
                targets,
                registry,
                time.Clocks.Clocks,
                time.Clocks,
                rng,
                lane.Committed.Mode,
                Gc018Scenario.StepDurationTicks,
                Gc018Scenario.TicksPerSecond,
                Gc018Scenario.MaxStepsPerPump,
                false,
                lane,
                publisher,
                Array.Empty<BufferId>(),
                null);
            detail = string.Empty;
            return true;
        }

        /// <summary>Native ECS index of one restored target, so the round trip's indices can be compared (P-005).</summary>
        public bool TryEntityIndexOf(TargetId target, out int index)
        {
            index = -1;
            if (registry == null)
            {
                return false;
            }

            if (!registry.TryResolveTarget(target, out TargetHandle _, out Entity entity))
            {
                return false;
            }

            index = entity.Index;
            return true;
        }

        /// <summary>Current handle of one restored target: the identity is restored, the handle is a new one (P-005).</summary>
        public bool TryHandleOf(TargetId target, out TargetHandle handle)
        {
            handle = default(TargetHandle);
            return registry != null && registry.TryGetHandle(target, out handle);
        }

        /// <summary>
        /// The join this rebuild opened between the restored lane and the restored world. It exists as a property
        /// because the join is a real validation (P-006) and not merely a constructed object: a lane that was not
        /// seeded from the world it joins is refused while the world is still unexposed.
        /// </summary>
        public WorldCompositionBridge? Composition => bridge;

        private IReadOnlyList<DerivationTarget> TargetView()
        {
            if (targets == null)
            {
                return Array.Empty<DerivationTarget>();
            }

            DerivationInputTargets view = targets.BuildDerivationTargets();
            return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
        }

        /// <summary>
        /// Re-registers the captured persistent clocks and re-schedules their pending wakes with the remaining
        /// delay the document recorded, so a restored world resumes a deferred timer instead of restarting it
        /// (P-038, P-053).
        /// </summary>
        private bool RebuildClocks(RestorePlan plan, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (time == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the restored world has no time driver.";
                return false;
            }

            for (int i = 0; i < plan.Clocks.Count; i++)
            {
                ClockRecordValue row = plan.Clocks[i];
                if (row.IsDeclaration)
                {
                    var spec = new PluginClockSpec(
                        row.ClockId,
                        "gc018-restored-clock",
                        (PluginClockKind)row.ClockKind,
                        (WakePausePolicy)row.PausePolicy,
                        row.Persists);
                    if (!time.Clocks.TryRegister(spec, out code))
                    {
                        detail = "re-registering captured clock " + row.ClockId.ToString() + " was refused: " + code;
                        return false;
                    }

                    RebuiltClockCount++;
                    continue;
                }

                if (!time.Clocks.TryScheduleWake(
                        row.ClockId,
                        row.WakeId,
                        row.PayloadSchema,
                        row.RemainingSteps,
                        row.RemainingTicks,
                        row.ScheduledAtSequence,
                        out WakeRecord? wake,
                        out code)
                    || wake == null)
                {
                    detail = "re-scheduling captured wake " + row.WakeId.ToString() + " on clock "
                        + row.ClockId.ToString() + " was refused: " + code;
                    return false;
                }

                RebuiltWakeCount++;
            }

            return true;
        }

        private bool SeedTargets(RestorePlan plan, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (seeder == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the restored world has no target seeder.";
                return false;
            }

            EntityManager entityManager = staging!.EntityWorld.EntityManager;
            int baseLayouts = 0;
            for (int i = 0; i < plan.Targets.Count; i++)
            {
                TargetRecordValue row = plan.Targets[i];
                if (!seeder.TrySeed(
                        row.Target, row.Scope, row.Recipe, out TargetHandle _, out code, out detail))
                {
                    detail = "seeding captured target " + row.Target.ToString() + " at scope "
                        + row.Scope.ToString() + " with recipe " + row.Recipe.ToString()
                        + " was refused: " + code + ": " + detail;
                    return false;
                }

                // 06 s7's "allocate recipes": the restored target gets the same base layout a spawn installs, from
                // the family's own registered applier, so the restored world's targets are complete rather than bare
                // identity rows (P-024, 04 s6).
                if (publisher == null || !publisher.Recipes.TryResolve(row.Recipe, out SpawnRecipe? recipe, out code)
                    || recipe == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "the captured recipe " + row.Recipe.ToString()
                        + " is not registered in this build's recipe catalog, so its base layout cannot be allocated"
                        + " (P-015, P-024).";
                    return false;
                }

                if (!seeder.TryGetEntity(row.Target, out Entity entity))
                {
                    code = DiagnosticCode.StaleHandle;
                    detail = "target " + row.Target.ToString()
                        + " was seeded but the registry cannot resolve it (P-005).";
                    return false;
                }

                recipe.Applier.ApplyBaseLayout(entityManager, entity, recipe);
                baseLayouts++;
                RebuiltTargetCount++;
            }

            for (int i = 0; i < plan.Slots.Count; i++)
            {
                SlotRecordValue row = plan.Slots[i];
                StateSlotKey key = row.Key;
                if (!seeder.TrySeedSlot(
                        key.Target, key.Owner, key.Slot, row.SchemaVersion, row.Value, row.Active, out code, out detail))
                {
                    detail = "seeding captured state slot " + key.ToString() + " was refused: " + code + ": " + detail;
                    return false;
                }

                RebuiltSlotCount++;
                if (!row.Active)
                {
                    RebuiltDormantSlotCount++;
                }
            }

            // A capture saves exactly the rows the source world held, so the restored world must carry exactly the
            // planned rows: a recipe base layout that declares owner slots the capture never carried (the ledger's
            // facts, a gate's decision) must not leave them behind as fabricated active state. The base layout
            // above still installs every component and buffer the family's runtime needs; the prune removes only
            // slot rows the plan does not name (P-032, P-053).
            if (!PruneBeyondPlanSlots(plan, out code, out detail))
            {
                return false;
            }

            BaseLayoutCount = baseLayouts;

            return true;
        }

        /// <summary>
        /// Removes every owner-state slot row the plan does not name, so the restored world's authoritative state
        /// is the captured state and nothing else. Rows are removed back-to-front from each target's buffer; a
        /// target without slot storage carries nothing to prune (P-032, P-053).
        /// </summary>
        private bool PruneBeyondPlanSlots(RestorePlan plan, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (staging == null || seeder == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the restored world is not built, so its state cannot be pruned.";
                return false;
            }

            var planned = new HashSet<(Id128 target, Id128 owner, Id128 slot)>();
            for (int i = 0; i < plan.Slots.Count; i++)
            {
                StateSlotKey key = plan.Slots[i].Key;
                planned.Add((key.Target.Value, key.Owner.Value, key.Slot.Value));
            }

            EntityManager entityManager = staging.EntityWorld.EntityManager;
            for (int i = 0; i < plan.Targets.Count; i++)
            {
                TargetRecordValue row = plan.Targets[i];
                if (!seeder.TryGetEntity(row.Target, out Entity entity)
                    || !entityManager.HasBuffer<TargetSlotState>(entity))
                {
                    continue;
                }

                DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
                for (int s = slots.Length - 1; s >= 0; s--)
                {
                    TargetSlotState slot = slots[s];
                    if (planned.Contains((row.Target.Value, slot.Owner.Value, slot.Slot.Value)))
                    {
                        continue;
                    }

                    slots.RemoveAt(s);
                    PrunedSlotCount++;
                }
            }

            return true;
        }

        /// <summary>
        /// Reconstructs the captured composition through the real control lane: the scopes the declared tree does
        /// not already carry are created, every captured isolation set, exclusion, installation and scope import is
        /// replayed as a real edit, and a captured world mode that differs from the declared one is replayed as an
        /// O-08 edit (P-010, P-013, P-016). Each accepted edit is answered with the world's assembly for that same
        /// publication, so the one publication series P-006 requires holds throughout — and the last one is the
        /// rederivation 06 s7 asks for, because it derives over the targets this builder just seeded.
        /// </summary>
        private bool ReplayComposition(RestorePlan plan, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (lane == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the restored world has no control lane.";
                return false;
            }

            // The lane opens with the family's declared scope seed. Only captured scopes absent from that seed are
            // pending replay; declared scopes already present are not unresolved work.
            int pending = 0;
            for (int i = 0; i < plan.Scopes.Count; i++)
            {
                ScopeRecordValue row = plan.Scopes[i];
                if (!row.IsRoot && !lane.Committed.Scopes.TryGet(row.Scope, out ScopeRecord? _))
                {
                    pending++;
                }
            }

            // Parents before children: the captured tree is a rooted tree (the planner proved it), so repeatedly
            // creating the scopes whose parent the lane already carries terminates.
            bool progress = pending > 0;
            while (progress)
            {
                progress = false;
                for (int i = 0; i < plan.Scopes.Count; i++)
                {
                    ScopeRecordValue row = plan.Scopes[i];
                    if (row.IsRoot || lane.Committed.Scopes.TryGet(row.Scope, out ScopeRecord? _))
                    {
                        continue;
                    }

                    if (!lane.Committed.Scopes.TryGet(row.Parent, out ScopeRecord? _))
                    {
                        continue;
                    }

                    if (!ApplyEdit(Gc018Scenario.ScopeCreate(row.Scope, row.Parent), out code, out detail))
                    {
                        return false;
                    }

                    ReplayedScopeCount++;
                    pending--;
                    progress = true;
                }
            }

            if (pending != 0)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the captured scope tree names " + pending.ToString(CultureInfo.InvariantCulture)
                    + " scope(s) whose parent the declared tree never carries, so the tree cannot be rebuilt.";
                return false;
            }

            for (int i = 0; i < plan.Scopes.Count; i++)
            {
                ScopeRecordValue row = plan.Scopes[i];
                if (!lane.Committed.Scopes.TryGet(row.Scope, out ScopeRecord? current) || current == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "scope " + row.Scope.ToString() + " is absent from the restored composition.";
                    return false;
                }

                IsolationSet service = CapturedIsolation(plan.Grants, row.Scope, GrantKind.ServiceIsolationMember, row.ServiceIsolationAll);
                IsolationSet capability = CapturedIsolation(plan.Grants, row.Scope, GrantKind.CapabilityIsolationMember, row.CapabilityIsolationAll);
                IReadOnlyList<ExclusionRule> exclusions = CapturedExclusions(plan.Grants, row.Scope);

                string currentText = BoundaryText(current.ServiceIsolation, current.CapabilityIsolation, current.Exclusions);
                string capturedText = BoundaryText(service, capability, exclusions);
                if (string.Equals(currentText, capturedText, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ApplyEdit(
                        Gc018Scenario.ScopeBoundaries(row.Scope, service, capability, exclusions),
                        out code,
                        out detail))
                {
                    return false;
                }

                ReplayedBoundaryCount++;
            }

            if (!ReplayInstalls(plan, out code, out detail))
            {
                return false;
            }

            for (int i = 0; i < plan.Scopes.Count; i++)
            {
                ScopeRecordValue row = plan.Scopes[i];
                if (!lane.Committed.Scopes.TryGet(row.Scope, out ScopeRecord? current) || current == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "scope " + row.Scope.ToString() + " is absent from the restored composition.";
                    return false;
                }

                IReadOnlyList<CapabilityImport> imports = CapturedImports(plan.Grants, row.Scope);
                IReadOnlyList<CapabilityImport> currentImports = current.Grants == null
                    ? Array.Empty<CapabilityImport>()
                    : current.Grants.Imports;
                if (string.Equals(ImportsText(currentImports), ImportsText(imports), StringComparison.Ordinal))
                {
                    continue;
                }

                if (!ApplyEdit(Gc018Scenario.ScopeImports(row.Scope, imports), out code, out detail))
                {
                    return false;
                }

                ReplayedImportCount += imports.Count;
            }

            if (plan.Header.Propagation != lane.Committed.Mode)
            {
                if (!ApplyEdit(family.ModeSet(plan.Header.Propagation), out code, out detail))
                {
                    return false;
                }

                ReplayedModeCount++;
            }

            return true;
        }

        private bool ReplayInstalls(RestorePlan plan, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            for (int i = 0; i < plan.Installs.Count; i++)
            {
                InstallRecordValue row = plan.Installs[i];
                if (row.Lifecycle == InstallationState.Disposed)
                {
                    continue;
                }

                if (!TryMountPayload(row, plan.Selections, out CompositionEditPayload? payload, out code, out detail)
                    || payload == null)
                {
                    return false;
                }

                if (!ApplyEdit(payload, out code, out detail))
                {
                    detail = "replaying captured install " + row.Instance.ToString()
                        + " at scope " + row.Scope.ToString() + " was refused: " + detail;
                    return false;
                }

                ReplayedInstallCount++;
            }

            return true;
        }

        private static bool TryMountPayload(
            InstallRecordValue row,
            IReadOnlyList<SelectionRecordValue> selections,
            out CompositionEditPayload? payload,
            out DiagnosticCode code,
            out string detail)
        {
            payload = null;
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!TryInstallConfig(row, out ConfigDocument? config, out detail) || config == null)
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            payload = new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                row.Scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                row.PluginType,
                row.Instance,
                row.Revision,
                row.ConfigHash,
                config,
                row.Priority,
                CapturedSelections(selections, row.Instance),
                PropagationMode.Automatic);
            return true;
        }

        private static bool TryInstallConfig(
            InstallRecordValue row,
            out ConfigDocument? config,
            out string detail)
        {
            config = ConfigDocument.Empty;
            detail = string.Empty;
            if (!row.HasConfigDocument)
            {
                return true;
            }

            byte[]? configBytes = row.ConfigBytes;
            if (configBytes == null)
            {
                config = null;
                detail = "install " + row.Instance.ToString() + " declared configuration bytes, but none were captured.";
                return false;
            }

            if (!ConfigDocumentCodec.TryDecode(new FrozenPayload(configBytes), out config) || config == null)
            {
                detail = "install " + row.Instance.ToString() + " carried a configuration document that would not decode.";
                return false;
            }

            return true;
        }

        private static IReadOnlyList<ServiceSelection> CapturedSelections(
            IReadOnlyList<SelectionRecordValue> selections,
            PluginInstanceId instance)
        {
            var selected = new List<ServiceSelection>();
            for (int i = 0; i < selections.Count; i++)
            {
                if (selections[i].Instance.Equals(instance))
                {
                    selected.Add(selections[i].ToSelection());
                }
            }

            return selected;
        }

        /// <summary>
        /// Submits one composition edit and answers the world for the publication it produced, exactly as the
        /// GC-013 sequence does: a `NoTargetChange` derivation is answered with the unchanged assembly so the
        /// lane's pair and the world's pair stay one series (P-006, P-030).
        /// </summary>
        private bool ApplyEdit(CompositionEditPayload payload, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (lane == null || pipeline == null || publisher == null || staging == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the restored world's control lane or pipeline is missing.";
                return false;
            }

            EditAdmission admission = lane.SubmitEdit(payload, nextOperation(staging.World), lane.Committed.Revision);
            if (!admission.Staged)
            {
                code = admission.Code;
                detail = "the composition edit was refused by the restored lane (" + admission.Kind + "/"
                    + admission.Code + ").";
                return false;
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                code = published.Count == 0 ? DiagnosticCode.ApplyFault : published[0].Code;
                detail = "the restored lane answered the edit with "
                    + (published.Count == 0
                        ? "no publication"
                        : published[0].Outcome.ToString() + "/" + published[0].Code)
                    + ".";
                return false;
            }

            DerivedAssemblyReport report = pipeline.PublishDerived(nextOperation(staging.World));
            if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                code = report.Code;
                detail = "the restored world refused the assembly for the edit: " + report.Describe();
                return false;
            }

            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                    nextOperation(staging.World), lane.Committed.Revision, lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    code = unchanged.Code;
                    detail = "the unchanged restored assembly was refused: " + unchanged.Detail;
                    return false;
                }
            }

            PublishedAssemblyCount++;
            return true;
        }

        private static IsolationSet CapturedIsolation(
            IReadOnlyList<GrantRecordValue> grants,
            ScopeId scope,
            GrantKind kind,
            bool allContracts)
        {
            var members = new List<Id128>();
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                if (row.Grant == kind && row.Scope.Equals(scope))
                {
                    members.Add(row.Subject);
                }
            }

            return new IsolationSet(allContracts, members);
        }

        private static IReadOnlyList<CapabilityImport> CapturedImports(
            IReadOnlyList<GrantRecordValue> grants,
            ScopeId scope)
        {
            var imports = new List<CapabilityImport>();
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                if (row.Grant == GrantKind.ScopeImport && row.Scope.Equals(scope))
                {
                    imports.Add(row.ToImport());
                }
            }

            return imports;
        }

        private static IReadOnlyList<ExclusionRule> CapturedExclusions(
            IReadOnlyList<GrantRecordValue> grants,
            ScopeId scope)
        {
            var rules = new List<ExclusionRule>();
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                if (row.Grant == GrantKind.Exclusion && row.Scope.Equals(scope))
                {
                    rules.Add(row.ToExclusion());
                }
            }

            return rules;
        }

        private static string BoundaryText(
            IsolationSet service,
            IsolationSet capability,
            IReadOnlyList<ExclusionRule> exclusions)
        {
            var lines = new List<string>
            {
                "service=" + IsolationText(service),
                "capability=" + IsolationText(capability),
            };

            for (int i = 0; i < exclusions.Count; i++)
            {
                ExclusionRule rule = exclusions[i];
                lines.Add("exclusion=" + rule.Kind + ";" + rule.TargetId.ToString() + ";"
                    + rule.AtScope.ToString() + ";" + rule.AtTarget.ToString() + ";"
                    + rule.AppliesToSubtree.ToString());
            }

            return Gc018Scenario.Canonical(lines);
        }

        private static string IsolationText(IsolationSet set)
        {
            var lines = new List<string> { set.AllContracts.ToString() };
            for (int i = 0; i < set.Contracts.Count; i++)
            {
                lines.Add(set.Contracts[i].ToString());
            }

            lines.Sort(StringComparer.Ordinal);
            return string.Join(",", lines.ToArray());
        }

        private static string ImportsText(IReadOnlyList<CapabilityImport> imports)
        {
            var lines = new List<string>(imports.Count);
            for (int i = 0; i < imports.Count; i++)
            {
                CapabilityImport import = imports[i];
                lines.Add(import.CapabilityId.ToString() + "@" + import.ProviderInstallationId.ToString());
            }

            return Gc018Scenario.Canonical(lines);
        }
    }


    /// <summary>Runs the GC-018 sequence over one family and one catalog.</summary>
    public static class Gc018Scenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// Declared temporal settings of the command-driven world this scenario captures: a world with no fixed
        /// step publishes no simulation duration, and one admitted command is one step (P-036, P-037).
        /// </summary>
        public const ulong StepDurationTicks = 0UL;

        public const ulong TicksPerSecond = 0UL;

        public const uint MaxStepsPerPump = 1U;

        /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
        private const ulong IdlePumpTicks = 1000000UL;

        private const int IdlePumpFrames = 4;

        /// <summary>Remaining delay the one scheduled wake carries across the boundary (P-038, P-053).</summary>
        private const ulong WakeDelaySteps = 5UL;

        /// <summary>
        /// Auxiliary entities the source world creates before it seeds its targets. They are not targets, they
        /// belong to no identity and nothing reads them: they exist so the source world's native `Entity.Index`
        /// block and the restored world's block are disjoint, which is what makes "the same state at different
        /// native indices" an observation rather than a coincidence (P-005's "an index is never an identity").
        /// </summary>
        private const int AuxiliaryEntityCount = 4;

        /// <summary>Random streams the source world declares; their positions are part of a checkpoint (P-008).</summary>
        private const int RngStreamCount = 2;

        /// <summary>Values drawn from each stream before the capture, so the saved position is non-trivial.</summary>
        private const int RngDrawsPerStream = 3;

        /// <summary>Schema version of the hand-made document that no serializer of this build accepts (P-055).</summary>
        private const uint UnknownSchemaVersion = 9U;

        /// <summary>Version two registered migration chains reach, which is the ambiguity P-054 rejects.</summary>
        private const uint AmbiguousTargetVersion = 3U;

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. Both families record
        /// exactly these names, so a renamed or dropped observation fails the EditMode suite and the player probe
        /// instead of shrinking them silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc018-world-lane-and-live-state",
            "gc018-committed-boundary-capture",
            "gc018-queued-commands-are-dispositioned-not-omitted",
            "gc018-capture-refuses-outside-a-boundary",
            "gc018-corrupt-and-truncated-documents-reject",
            "gc018-unknown-required-schema-rejects-restore",
            "gc018-ambiguous-migration-rejects-restore",
            "gc018-corrupt-reference-rejects-restore",
            "gc018-restore-happens-into-a-new-unexposed-world",
            "gc018-restore-recreates-state-at-different-native-indices",
            "gc018-restore-preserves-dormant-slots",
            "gc018-restore-preserves-mode-imports-and-exclusions",
            "gc018-restore-continues-clocks-rng-and-cursors",
            "gc018-old-callbacks-cannot-target-the-new-session",
            "gc018-restored-world-publishes-and-advances",
            "gc018-teardown-disposes-both-worlds",
        };

        /// <summary>The observation names one family's run records: <c>&lt;label&gt;/&lt;name&gt;</c>.</summary>
        public static string[] QualifiedNames(string label)
        {
            var names = new string[ObservationNames.Length];
            for (int i = 0; i < ObservationNames.Length; i++)
            {
                names[i] = label + "/" + ObservationNames[i];
            }

            return names;
        }

        /// <summary>Runs the whole sequence for one family and one catalog.</summary>
        public static Gc018ScenarioResult Run(IGc018Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        /// <summary>O-02: one scope creation, in the shape the family lane seeds declare (P-010).</summary>
        internal static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// P-016: one scope's replacement isolation sets and exclusions, which is the shape the capture records a
        /// boundary in and the shape a restore replays it with.
        /// </summary>
        internal static CompositionEditPayload ScopeBoundaries(
            ScopeId scope,
            IsolationSet service,
            IsolationSet capability,
            IReadOnlyList<ExclusionRule> exclusions)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeIsolation,
                scope,
                default(ScopeId),
                false,
                service,
                capability,
                exclusions,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>
        /// P-013: one scope's explicit Conservative-mode imports, naming providers already registered in the lane.
        /// A checkpoint restore replays these separately from isolation because <see cref="CompositionEditSubject.ScopeIsolation"/>
        /// deliberately preserves whatever grants the scope already carried.
        /// </summary>
        internal static CompositionEditPayload ScopeImports(
            ScopeId scope,
            IReadOnlyList<CapabilityImport> imports)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeGrants,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                imports,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>Sorts and joins canonical lines, so two record sets compare independent of discovery order (P-008).</summary>
        internal static string Canonical(List<string> lines)
        {
            lines.Sort(StringComparer.Ordinal);
            return string.Join("\n", lines.ToArray());
        }

        internal static string Clip(string text, int limit)
            => text.Length <= limit
                ? text
                : text.Substring(0, limit) + "...(+" + (text.Length - limit).ToString(CultureInfo.InvariantCulture)
                    + " chars)";

        internal static List<string> TargetLines(IReadOnlyList<TargetRecordValue> targets)
        {
            var lines = new List<string>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                TargetRecordValue row = targets[i];
                lines.Add("target=" + row.Target.ToString()
                    + ";scope=" + row.Scope.ToString()
                    + ";recipe=" + row.Recipe.Id.ToString()
                    + ";schema=" + row.Recipe.Schema.ToString()
                    + ";revision=" + row.Recipe.Revision.Value.ToString(CultureInfo.InvariantCulture));
            }

            return lines;
        }

        internal static List<string> SlotLines(IReadOnlyList<SlotRecordValue> slots)
        {
            var lines = new List<string>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                SlotRecordValue row = slots[i];
                lines.Add("slot=" + row.Key.ToString()
                    + ";version=" + row.SchemaVersion.ToString(CultureInfo.InvariantCulture)
                    + ";value=" + row.Value.ToString(CultureInfo.InvariantCulture)
                    + ";active=" + row.Active.ToString());
            }

            return lines;
        }

        internal static List<string> ScopeLines(IReadOnlyList<ScopeRecordValue> scopes)
        {
            var lines = new List<string>(scopes.Count);
            for (int i = 0; i < scopes.Count; i++)
            {
                ScopeRecordValue row = scopes[i];
                lines.Add("scope=" + row.Scope.ToString()
                    + ";parent=" + row.Parent.ToString()
                    + ";depth=" + row.Depth.ToString(CultureInfo.InvariantCulture)
                    + ";mode=" + row.Propagation.ToString()
                    + ";installs=" + row.InstallCount.ToString(CultureInfo.InvariantCulture)
                    + ";serviceAll=" + row.ServiceIsolationAll.ToString()
                    + ";capAll=" + row.CapabilityIsolationAll.ToString()
                    + ";serviceMembers=" + row.ServiceIsolationCount.ToString(CultureInfo.InvariantCulture)
                    + ";capMembers=" + row.CapabilityIsolationCount.ToString(CultureInfo.InvariantCulture));
            }

            return lines;
        }


        internal static List<string> InstallLines(IReadOnlyList<InstallRecordValue> installs)
        {
            var lines = new List<string>(installs.Count);
            for (int i = 0; i < installs.Count; i++)
            {
                InstallRecordValue row = installs[i];
                lines.Add("install=" + row.Instance.ToString()
                    + ";type=" + row.PluginType.ToString()
                    + ";scope=" + row.Scope.ToString()
                    + ";revision=" + row.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + ";config=" + row.ConfigHash.ToHex()
                    + ";priority=" + row.Priority.ToString(CultureInfo.InvariantCulture)
                    + ";generation=" + row.Generation.ToString(CultureInfo.InvariantCulture)
                    + ";activation=" + row.ActivationEpoch.ToString(CultureInfo.InvariantCulture)
                    + ";state=" + row.Lifecycle.ToString()
                    + ";fields=" + row.ConfigFieldCount.ToString(CultureInfo.InvariantCulture)
                    + ";hasConfig=" + row.HasConfigDocument.ToString());
            }

            return lines;
        }

        internal static List<string> GrantLines(IReadOnlyList<GrantRecordValue> grants)
        {
            var lines = new List<string>(grants.Count);
            for (int i = 0; i < grants.Count; i++)
            {
                GrantRecordValue row = grants[i];
                lines.Add("grant=" + row.Grant.ToString()
                    + ";scope=" + row.Scope.ToString()
                    + ";target=" + row.Target.ToString()
                    + ";capability=" + row.Capability.ToString()
                    + ";provider=" + row.Provider.ToString()
                    + ";subject=" + row.Subject.ToString()
                    + ";subtree=" + row.AppliesToSubtree.ToString()
                    + ";allContracts=" + row.AllContracts.ToString()
                    + ";exclusion=" + row.Exclusion.ToString()
                    + ";order=" + row.Order.ToString(CultureInfo.InvariantCulture));
            }

            return lines;
        }

        internal static List<string> ClockLines(IReadOnlyList<ClockRecordValue> clocks)
        {
            var lines = new List<string>(clocks.Count);
            for (int i = 0; i < clocks.Count; i++)
            {
                ClockRecordValue row = clocks[i];
                lines.Add("clock=" + row.Row.ToString()
                    + ";id=" + row.ClockId.ToString()
                    + ";kind=" + row.ClockKind.ToString(CultureInfo.InvariantCulture)
                    + ";pause=" + row.PausePolicy.ToString(CultureInfo.InvariantCulture)
                    + ";persists=" + row.Persists.ToString()
                    + ";wake=" + row.WakeId.ToString()
                    + ";state=" + row.State.ToString()
                    + ";steps=" + row.RemainingSteps.ToString(CultureInfo.InvariantCulture)
                    + ";ticks=" + row.RemainingTicks.ToString(CultureInfo.InvariantCulture)
                    + ";at=" + row.ScheduledAtSequence.ToString(CultureInfo.InvariantCulture)
                    + ";payload=" + row.PayloadSchema.ToString());
            }

            return lines;
        }

        internal static List<string> RngLines(IReadOnlyList<RngRecordValue> streams)
        {
            var lines = new List<string>(streams.Count);
            for (int i = 0; i < streams.Count; i++)
            {
                RngRecordValue row = streams[i];
                lines.Add("rng=" + row.StreamId.ToString()
                    + ";key=" + row.StreamKey.ToString(CultureInfo.InvariantCulture)
                    + ";state=" + row.State.ToString(CultureInfo.InvariantCulture)
                    + ";draws=" + row.DrawCount.ToString(CultureInfo.InvariantCulture));
            }

            return lines;
        }

        /// <summary>
        /// Cursor rows without the session stamp: a restored world re-stamps every cursor with its own fresh
        /// session id, which is exactly what P-004 requires, so comparing the stamps would assert the wrong thing.
        /// </summary>
        internal static List<string> CursorLines(IReadOnlyList<CursorRecordValue> cursors)
        {
            var lines = new List<string>(cursors.Count);
            for (int i = 0; i < cursors.Count; i++)
            {
                CursorRecordValue row = cursors[i];
                lines.Add("cursor=" + row.Row.ToString()
                    + ";issuer=" + row.IssuerId.ToString()
                    + ";sequence=" + row.Sequence.ToString(CultureInfo.InvariantCulture));
            }

            return lines;
        }

        internal static int CountGrants(IReadOnlyList<GrantRecordValue> grants, GrantKind kind)
        {
            int count = 0;
            for (int i = 0; i < grants.Count; i++)
            {
                if (grants[i].Grant == kind)
                {
                    count++;
                }
            }

            return count;
        }

        internal static int CountSlots(IReadOnlyList<SlotRecordValue> slots, bool active)
        {
            int count = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Active == active)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// One registered directed schema migration step, as the ambiguity observation needs it. The step carries
        /// only its registration key and the version pair it moves between (P-054): it exists so two distinct chains
        /// into one destination version are registered, which is the shape a restore must refuse rather than pick
        /// from.
        /// </summary>
        private sealed class Gc018MigrationStep : ISchemaMigrationStep
        {
            private readonly SchemaRef from;
            private readonly SchemaRef to;

            public Gc018MigrationStep(ulong registrationOrdinal, SchemaId schema, uint fromVersion, uint toVersion)
            {
                from = new SchemaRef(schema, fromVersion);
                to = new SchemaRef(schema, toVersion);
                Key = new FactoryKey(new Id128(0x47433031_38UL, registrationOrdinal), 1U);
            }

            public FactoryKey Key { get; }

            public SchemaRef From => from;

            public SchemaRef To => to;
        }

        private sealed class Executor
        {
            private readonly IGc018Family family;
            private readonly List<Gc018Step> steps = new List<Gc018Step>();
            private readonly IdSequence sessionSequence;

            private UnityWorldHost? host;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private WorldCompositionBridge? bridge;
            private DerivationModeSwitchValidator? validator;
            private WorldTimeDriver? time;
            private RngStreamTable? rng;
            private PipelineDescriptorReport? descriptor;
            private CheckpointSerializerBindings? bindings;
            private CheckpointCodecSet? codecs;
            private CaptureContext? sourceContext;
            private WorldId sourceWorld;
            private WorldCreateRequest sourceRequest;
            private ContentHash catalogFingerprint;
            private CheckpointMigrationRegistry? directMigrations;
            private string codecEvidence = string.Empty;

            private readonly Dictionary<Id128, int> sourceIndices = new Dictionary<Id128, int>();
            private readonly Dictionary<Id128, TargetHandle> sourceHandles = new Dictionary<Id128, TargetHandle>();
            private readonly Dictionary<OperationId, FrozenPayload> commandPayloads =
                new Dictionary<OperationId, FrozenPayload>();

            private CommandEnvelope? queuedEnvelope;
            private OperationId queuedOperation;
            private CheckpointCaptureResult? rejectedCapture;
            private CheckpointCaptureResult? includedCapture;
            private Gc018Image? capturedImage;
            private Gc018Image? restoredImage;
            private Gc018FamilyRestoreBuilder? builder;
            private RestoreOutcome? restoreOutcome;
            private UnityWorldHost? restoredHost;
            private WorldId restoredSession;
            private int auxiliaryFirstIndex;
            private int auxiliaryLastIndex;
            private ulong operationSequence;
            private int registryBeforeCreate;
            private string lastFailure = string.Empty;

            public Executor(IGc018Family family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public Gc018ScenarioResult Run()
            {
                CreateWorldAndLiveState();
                RequireBuilder();
                CaptureAtCommittedBoundary();
                ProveQueueDispositions();
                ProveCaptureRefusesOutsideABoundary();
                ProveCorruptAndTruncatedDocumentsReject();
                ProveUnknownRequiredSchemaRejectsRestore();
                ProveAmbiguousMigrationRejectsRestore();
                ProveCorruptReferenceRejectsRestore();
                RestoreIntoANewUnexposedWorld();
                ProveRestoredIndicesDiffer();
                ProveDormantSlotsSurvive();
                ProveModeImportsAndExclusionsSurvive();
                ProveClocksRngAndCursorsContinue();
                ProveOldCallbacksCannotTargetTheNewSession();
                ProveRestoredWorldPublishesAndAdvances();
                TearDownSafely();
                return new Gc018ScenarioResult(family.Label, steps);
            }

            // ------------------------------------------------------------------ 1. the source world and its live state

            private void CreateWorldAndLiveState()
            {
                const string name = "gc018-world-lane-and-live-state";
                try
                {
                    descriptor = family.CompilePipeline();
                    if (!descriptor.Succeeded || descriptor.Descriptor == null || descriptor.Adaptation == null
                        || descriptor.Compilation == null)
                    {
                        Add(name, false, "the ownership and schedule pipeline refused: " + descriptor.Describe());
                        return;
                    }

                    if (!ContentHash.TryParseHex(family.CatalogFingerprint, out catalogFingerprint))
                    {
                        Add(name, false, "the family's catalog fingerprint literal is not 64 lowercase hex characters: "
                            + family.CatalogFingerprint);
                        return;
                    }

                    if (!TryBuildCodecs(out string codecDetail))
                    {
                        Add(name, false, codecDetail);
                        return;
                    }

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    sourceWorld = new WorldId(sessionSequence.Next());
                    sourceRequest = family.CreateRequest(sourceWorld, NextOperation(sourceWorld));
                    host = null;
                    bool created = UnityWorldRegistry.TryCreate(
                        sourceRequest,
                        family.CreateRegistration(descriptor.Adaptation),
                        out UnityWorldHost? createdHost,
                        out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        Add(name, false, "world creation failed: " + result.Code + ": " + result.Detail);
                        return;
                    }

                    registry = new TargetRegistry(sourceWorld, 16);
                    publisher = new AssemblyPublisher(
                        host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptor.Descriptor);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    IDerivationValueSource valueSource = family.CreateValues();
                    validator = new DerivationModeSwitchValidator(valueSource, TargetView);
                    lane = CompositionHost.CreateDefault(
                        sourceWorld,
                        family.WorldRootScope,
                        new CatalogManifestSource(family.Catalog, family.Declarations),
                        null,
                        family.LaneSeed,
                        validator);
                    bridge = new WorldCompositionBridge(host, lane, publisher);
                    pipeline = new DerivedAssemblyPipeline(
                        host,
                        lane,
                        publisher,
                        targets,
                        seeder,
                        valueSource,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(1024UL * 1024UL, family.Issuer),
                        new PlanBudget(
                            1024UL * 1024UL, 1024UL * 1024UL, 4096UL, 64UL));
                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptor.Adaptation.NativeTable!);

                    // The declared clock: one persistent logical-step clock with one pending wake, so the capture
                    // carries a clock fact rather than an empty list (P-038, P-053).
                    PluginClockSpec clockSpec = new PluginClockSpec(
                        family.WakeClockId,
                        "gc018-" + family.Label,
                        PluginClockKind.LogicalStep,
                        WakePausePolicy.Defer,
                        true);
                    if (!time.Clocks.TryRegister(clockSpec, out DiagnosticCode clockCode))
                    {
                        Add(name, false, "registering the declared plugin clock " + family.WakeClockId.ToString()
                            + " was refused: " + clockCode);
                        return;
                    }

                    Id128 wakeId = new Id128(family.WakeClockId.High, family.WakeClockId.Low ^ 0x00000000000000FFUL);
                    if (!time.TryScheduleWake(
                            family.WakeClockId,
                            wakeId,
                            family.WakePayloadSchema,
                            WakeDelaySteps,
                            0UL,
                            out WakeRecord? wake,
                            out DiagnosticCode wakeCode)
                        || wake == null)
                    {
                        Add(name, false, "scheduling the declared wake was refused: " + wakeCode);
                        return;
                    }

                    // The random streams whose positions the checkpoint saves (P-008, P-053).
                    rng = new RngStreamTable();
                    var drawTotals = new List<string>();
                    for (int i = 0; i < RngStreamCount; i++)
                    {
                        var streamId = new Id128(family.WakeClockId.High ^ 0x5253UL, 100UL + (ulong)i);
                        if (!rng.TryDeclare(
                                streamId, 7UL + (ulong)i, 0x1234UL + (ulong)i, out RngStream? stream, out string declareDetail)
                            || stream == null)
                        {
                            Add(name, false, "declaring random stream " + streamId.ToString()
                                + " was refused: " + declareDetail);
                            return;
                        }

                        for (int d = 0; d < RngDrawsPerStream; d++)
                        {
                            stream.Next();
                        }

                        drawTotals.Add(streamId.ToString() + "=" + stream.DrawCount.ToString(CultureInfo.InvariantCulture));
                    }

                    // Auxiliary entities before the targets, so this world's native index block and the restored
                    // world's cannot overlap (P-005: an index is never an identity).
                    EntityManager entityManager = host.EntityWorld.EntityManager;
                    auxiliaryFirstIndex = int.MaxValue;
                    auxiliaryLastIndex = int.MinValue;
                    for (int i = 0; i < AuxiliaryEntityCount; i++)
                    {
                        Entity auxiliary = entityManager.CreateEntity();
                        auxiliaryFirstIndex = Math.Min(auxiliaryFirstIndex, auxiliary.Index);
                        auxiliaryLastIndex = Math.Max(auxiliaryLastIndex, auxiliary.Index);
                    }

                    bool setupEdits = PublishEdits(family.SetupEdits);
                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));

                    // The active row the family declared, and the dormant row this scenario declares: one target
                    // carries both, which is what makes "active+dormant round-trips" a comparison of two rows
                    // rather than of two worlds (P-032).
                    bool activeSeeded = TryReadActiveSlot(family.MovedTarget, out uint activeVersion, out int activeValue);
                    if (!seeder.TrySeedSlot(
                            family.DormantTarget,
                            family.DormantOwner,
                            family.DormantSlot,
                            family.DormantVersion,
                            family.DormantValue,
                            false,
                            out DiagnosticCode dormantCode,
                            out string dormantDetail))
                    {
                        Add(name, false, "seeding the declared dormant slot was refused: " + dormantCode + ": "
                            + dormantDetail);
                        return;
                    }

                    // The captured composition boundaries, which the restore must replay and not reopen (P-016).
                    bool spareScope = PublishEdit(family.SpareScopeEdits[0], "spare-scope");
                    bool enrichment = PublishEdit(family.BoundaryEnrichment(), "boundary-enrichment");

                    CompositionEditPayload providerMount = family.MountProvider();
                    bool conservativeMode = PublishEdit(
                        family.ModeSet(PropagationMode.Conservative), "mode-set-conservative");
                    bool providerMounted = PublishEdit(providerMount, "provider-mount");
                    var scopeImports = new List<CapabilityImport>
                    {
                        new CapabilityImport(
                            family.DerivedCapability,
                            new ProviderInstallationId(providerMount.Instance.Value)),
                    };
                    bool importGranted = PublishEdit(
                        Gc018Scenario.ScopeImports(family.EnrichedScope, scopeImports), "scope-import");

                    IReadOnlyList<LiveTarget> live = targets.Targets;
                    for (int i = 0; i < live.Count; i++)
                    {
                        LiveTarget liveTarget = live[i];
                        if (!registry.TryResolveTarget(liveTarget.Target, out TargetHandle handle, out Entity entity))
                        {
                            Add(name, false, "live target " + liveTarget.Target.ToString()
                                + " has no handle in this world's target registry (P-005)");
                            return;
                        }

                        sourceIndices[liveTarget.Target.Value] = entity.Index;
                        sourceHandles[liveTarget.Target.Value] = handle;
                    }

                    ulong idleSteps = PumpIdleFrames();
                    bool joined = MatchesPublishedAssembly();
                    int exclusions = SourceGrantCount(GrantKind.Exclusion);
                    int isolationMembers = SourceGrantCount(GrantKind.CapabilityIsolationMember);
                    int imports = SourceGrantCount(GrantKind.ScopeImport);
                    bool spareScopePresent = lane.Committed.Scopes.TryGet(family.SpareScopeEdits[0].Scope, out ScopeRecord? _);
                    bool enrichedPresent = lane.Committed.Scopes.TryGet(family.EnrichedScope, out ScopeRecord? enriched)
                        && enriched != null
                        && enriched.CapabilityIsolation.Contracts.Count == 1
                        && enriched.Exclusions.Count == 1;
                    bool providerPresent = lane.Committed.TryGetInstall(
                            providerMount.Instance, out InstallEntry? providerEntry)
                        && providerEntry != null
                        && providerEntry.Scope.Equals(providerMount.Scope);
                    bool laneJoined = lane.Validator != null
                        && ReferenceEquals(lane.Validator, validator)
                        && bridge != null
                        && ReferenceEquals(bridge.Composition, lane)
                        && ReferenceEquals(bridge.Publisher, publisher);

                    bool pass = setupEdits
                        && seeded
                        && activeSeeded
                        && activeVersion == family.ActiveSlotVersion
                        && activeValue == family.MutableValue
                        && live.Count > 0
                        && sourceIndices.Count == live.Count
                        && dormantSeededPresent()
                        && spareScope
                        && enrichment
                        && conservativeMode
                        && providerMounted
                        && providerPresent
                        && importGranted
                        && imports == 1
                        && exclusions == 1
                        && isolationMembers == 1
                        && spareScopePresent
                        && enrichedPresent
                        && rng.Count == RngStreamCount
                        && time.Clocks.IsRegistered(family.WakeClockId)
                        && time.Clocks.PendingWakeCount == 1
                        && idleSteps == 0UL
                        && joined
                        && laneJoined
                        && sourceRequest.Mode == PropagationMode.Automatic
                        && lane.Committed.Mode == PropagationMode.Conservative
                        && host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "session=" + sourceWorld.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + live.Count.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; setupEdits=" + setupEdits
                        + "; targetsSeeded=" + seeded
                        + "; activeSlot=" + activeVersion.ToString(CultureInfo.InvariantCulture) + "/"
                        + activeValue.ToString(CultureInfo.InvariantCulture)
                        + "; dormantSlot=" + family.DormantVersion.ToString(CultureInfo.InvariantCulture) + "/"
                        + family.DormantValue.ToString(CultureInfo.InvariantCulture) + " active=False"
                        + "; spareScope=" + spareScopePresent
                        + "; enrichedScope=" + family.EnrichedScope.ToString()
                        + " exclusions=" + exclusions.ToString(CultureInfo.InvariantCulture)
                        + " capIsolationMembers=" + isolationMembers.ToString(CultureInfo.InvariantCulture)
                        + " imports=" + imports.ToString(CultureInfo.InvariantCulture)
                        + "; importProvider=" + providerMount.Instance.ToString()
                        + "@" + providerMount.Scope.ToString()
                        + " capability=" + family.DerivedCapability.ToString()
                        + " importScope=" + family.EnrichedScope.ToString()
                        + "; rngStreams=" + rng.Count.ToString(CultureInfo.InvariantCulture)
                        + " draws=" + string.Join(",", drawTotals.ToArray())
                        + "; clock=" + family.WakeClockId.ToString()
                        + " wakes=" + time.Clocks.PendingWakeCount.ToString(CultureInfo.InvariantCulture)
                        + "; auxiliaryIndices=" + auxiliaryFirstIndex.ToString(CultureInfo.InvariantCulture) + ".."
                        + auxiliaryLastIndex.ToString(CultureInfo.InvariantCulture)
                        + "; idleFrames=" + IdlePumpFrames.ToString(CultureInfo.InvariantCulture)
                        + " idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + sourceRequest.Mode + "->" + lane.Committed.Mode
                        + "; joined=" + joined
                        + "; sourceHandles=" + Gc018Scenario.Clip(SourceHandleText(live), 360)
                        + "; laneJoined=" + laneJoined
                        + "; lifecycle=" + host.Lifecycle
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>The source world's recorded handle and native index evidence, per live target (P-005).</summary>
            private string SourceHandleText(IReadOnlyList<LiveTarget> live)
            {
                var pairs = new List<string>(live.Count);
                for (int i = 0; i < live.Count; i++)
                {
                    TargetId target = live[i].Target;
                    if (sourceHandles.TryGetValue(target.Value, out TargetHandle handle)
                        && sourceIndices.TryGetValue(target.Value, out int index))
                    {
                        pairs.Add(target.ToString() + "@idx" + index.ToString(CultureInfo.InvariantCulture)
                            + "=slot" + handle.Slot.ToString(CultureInfo.InvariantCulture) + "/"
                            + handle.Generation.ToString(CultureInfo.InvariantCulture));
                    }
                }

                return string.Join(",", pairs.ToArray());
            }


            private bool dormantSeededPresent()
            {
                if (seeder == null)
                {
                    return false;
                }

                IReadOnlyList<LiveSlotState> slots = seeder.ReadLiveSlots(new[] { family.DormantTarget });
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot.Owner.Equals(family.DormantOwner)
                        && slots[i].Slot.Slot.Equals(family.DormantSlot)
                        && slots[i].SchemaVersion == family.DormantVersion
                        && slots[i].Value == family.DormantValue)
                    {
                        return true;
                    }
                }

                return false;
            }

            private bool TryReadActiveSlot(TargetId target, out uint version, out int value)
            {
                version = 0U;
                value = int.MinValue;
                if (seeder == null)
                {
                    return false;
                }

                if (!seeder.TryGetEntity(target, out Entity _))
                {
                    return false;
                }

                IReadOnlyList<LiveSlotState> slots = seeder.ReadLiveSlots(new[] { target });
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot.Owner.Equals(family.MutableOwner) && slots[i].Slot.Slot.Equals(family.MutableSlot))
                    {
                        version = slots[i].SchemaVersion;
                        value = slots[i].Value;
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Counts one grant category in the committed composition. The captured document's equivalent is
            /// `CountGrants` over the decoded grant rows, so a boundary that was lost or reopened shows up as a
            /// difference between these two counts and the rows they came from (P-013, P-016).
            /// </summary>
            private int SourceGrantCount(GrantKind kind)
            {
                if (lane == null)
                {
                    return -1;
                }

                int count = 0;
                IReadOnlyList<ScopeRecord> scopes = lane.Committed.Scopes.Scopes;
                for (int i = 0; i < scopes.Count; i++)
                {
                    ScopeRecord scope = scopes[i];
                    switch (kind)
                    {
                        case GrantKind.ScopeImport:
                            count += scope.Grants == null ? 0 : scope.Grants.Imports.Count;
                            break;
                        case GrantKind.Exclusion:
                            count += scope.Exclusions.Count;
                            break;
                        case GrantKind.CapabilityIsolationMember:
                            count += scope.CapabilityIsolation.Contracts.Count;
                            break;
                        case GrantKind.ServiceIsolationMember:
                            count += scope.ServiceIsolation.Contracts.Count;
                            break;
                        default:
                            break;
                    }
                }

                return count;
            }

            // ------------------------------------------------------------------ 2. the captured boundary

            /// <summary>
            /// Queues one real command through the family's own route and captures the world at its committed
            /// boundary, asserting that every header field echoes the world it was read from: the session, the
            /// definition, the step, the retained debt, the mode, the catalog fingerprint and the counts (P-053).
            /// </summary>
            private void CaptureAtCommittedBoundary()
            {
                const string name = "gc018-committed-boundary-capture";
                try
                {
                    if (host == null || lane == null || publisher == null || targets == null || registry == null
                        || rng == null || time == null || codecs == null)
                    {
                        Add(name, false, "the source world or its pipeline is missing");
                        return;
                    }

                    queuedOperation = NextOperation(sourceWorld);
                    queuedEnvelope = family.QueuedCommand(sourceWorld, queuedOperation);
                    CommandAdmissionReceipt receipt = host.Submit(queuedEnvelope);
                    if (!receipt.Admitted)
                    {
                        Add(name, false, "the family's own command was not admitted: " + receipt.Result.Kind + "/"
                            + receipt.Result.Reason);
                        return;
                    }

                    commandPayloads[queuedOperation] = queuedEnvelope.Payload;
                    sourceContext = new CaptureContext(
                        sourceWorld,
                        sourceRequest.Definition,
                        catalogFingerprint,
                        targets,
                        registry,
                        time.Clocks.Clocks,
                        time.Clocks,
                        rng,
                        lane.Committed.Mode,
                        StepDurationTicks,
                        TicksPerSecond,
                        MaxStepsPerPump,
                        false,
                        lane,
                        publisher,
                        Array.Empty<BufferId>(),
                        commandPayloads);

                    var reader = new UnityCommittedBoundaryReader(host, sourceContext);
                    bool atBoundary = reader.IsAtCommittedBoundary;
                    rejectedCapture = CheckpointCapture.Capture(
                        reader,
                        new CheckpointCaptureRequest(
                            sourceWorld, codecs, CheckpointQueuePolicy.RejectQueued, catalogFingerprint));
                    if (rejectedCapture == null || !rejectedCapture.Captured)
                    {
                        Add(name, false, "the boundary capture was refused: "
                            + (rejectedCapture == null
                                ? "the capture returned no result"
                                : rejectedCapture.Code + ": " + rejectedCapture.Detail));
                        return;
                    }

                    if (!TryDecode(rejectedCapture.Document, out Gc018Image? image, out string decodeDetail)
                        || image == null)
                    {
                        Add(name, false, decodeDetail);
                        return;
                    }

                    capturedImage = image;
                    HeaderRecordValue header = rejectedCapture.Header;
                    ulong admissionCutoff = host.Messages == null
                        ? 0UL
                        : host.Messages.Requests.LastAdmissionSequence.Value;
                    int activeSlots = CountSlots(image.Slots, true);
                    int dormantSlots = CountSlots(image.Slots, false);

                    bool pass = atBoundary
                        && codecEvidence.Length != 0
                        && rejectedCapture.Code == DiagnosticCode.None
                        && rejectedCapture.Document.Length > 0
                        && !rejectedCapture.DocumentHash.IsEmpty
                        && header.SourceSession.Session.Equals(sourceWorld.Session)
                        && header.WorldDefinition.Equals(sourceRequest.Definition)
                        && header.Temporal == sourceRequest.TemporalModel
                        && header.LogicalStep == host.CurrentStep.Value
                        && header.TimeDebtTicks == host.RetainedDebt.Ticks
                        && header.DomainSeconds == host.DomainSeconds
                        && header.PendingDemand == host.PendingDemand
                        && header.PendingDemand == 1UL
                        && header.ProtocolMajor == CheckpointFormat.ProtocolMajor
                        && header.ProtocolMinor == CheckpointFormat.ProtocolMinor
                        && header.IsSupportedProtocol
                        && header.StepDurationTicks == StepDurationTicks
                        && header.TicksPerSecond == TicksPerSecond
                        && header.MaxStepsPerPump == MaxStepsPerPump
                        && !header.UsesUnscaledHostClock
                        && header.Propagation == lane.Committed.Mode
                        && header.CatalogFingerprint.Equals(catalogFingerprint)
                        && header.SourcePublishedRevision == lane.Committed.Revision.Value
                        && header.SourcePublishedEpoch == lane.Committed.Epoch.Value
                        && header.SourcePublishedEpoch == host.CurrentEpoch.Value
                        && header.SourceHostTicksPerSecond == host.HostTicksPerSecond
                        && header.AdmissionCutoff == admissionCutoff
                        && header.QueuePolicy == (uint)CheckpointQueuePolicy.RejectQueued
                        && header.TargetCount == (uint)targets.Count
                        && header.SlotCount == (uint)image.Slots.Count
                        && header.ScopeCount == (uint)lane.Committed.Scopes.Count
                        && header.RngStreamCount == (uint)rng.Count
                        && header.ClockCount == 2U
                        && header.ContentRevisionCount == 0U
                        && image.Targets.Count == targets.Count
                        && image.Scopes.Count == lane.Committed.Scopes.Count
                        && image.Slots.Count == 2
                        && activeSlots == 1
                        && dormantSlots == 1
                        && image.RngStreams.Count == RngStreamCount;

                    Add(name, pass,
                        "session=" + sourceWorld.Session.ToString()
                        + "; atBoundary=" + atBoundary
                        + "; bytes=" + rejectedCapture.Document.Length.ToString(CultureInfo.InvariantCulture)
                        + "; documentHash=" + rejectedCapture.DocumentHash.ToHex()
                        + "; counts=" + rejectedCapture.Counts.ToString()
                        + "; header=" + header.ToString()
                        + "; definition=" + header.WorldDefinition.ToString()
                        + "; step=" + header.LogicalStep.ToString(CultureInfo.InvariantCulture)
                        + "; debtTicks=" + header.TimeDebtTicks.ToString(CultureInfo.InvariantCulture)
                        + "; pendingDemand=" + header.PendingDemand.ToString(CultureInfo.InvariantCulture)
                        + "; protocol=" + header.ProtocolMajor.ToString(CultureInfo.InvariantCulture) + "."
                        + header.ProtocolMinor.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + header.Propagation
                        + "; fingerprint=" + header.CatalogFingerprint.ToHex()
                        + "; published=" + header.SourcePublishedRevision.ToString(CultureInfo.InvariantCulture)
                        + "/" + header.SourcePublishedEpoch.ToString(CultureInfo.InvariantCulture)
                        + "; admissionCutoff=" + header.AdmissionCutoff.ToString(CultureInfo.InvariantCulture)
                        + "; activeSlots=" + activeSlots.ToString(CultureInfo.InvariantCulture)
                        + "; dormantSlots=" + dormantSlots.ToString(CultureInfo.InvariantCulture)
                        + "; rngStreams=" + image.RngStreams.Count.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Captures the same committed boundary again with the other queue policy, so the two dispositions are
            /// read from two documents over one identical world state: `RejectQueued` records the queued command as
            /// explicitly rejected and carries none, `IncludeQueued` carries it with its payload. Neither omits it
            /// ambiguously, which is P-053's whole point (and 06 s7's "command inclusion is explicit").
            /// </summary>
            private void ProveQueueDispositions()
            {
                const string name = "gc018-queued-commands-are-dispositioned-not-omitted";
                try
                {
                    if (host == null || sourceContext == null || codecs == null || rejectedCapture == null
                        || queuedEnvelope == null)
                    {
                        Add(name, false, "the world, its capture context or the first capture is missing");
                        return;
                    }

                    var reader = new UnityCommittedBoundaryReader(host, sourceContext);
                    includedCapture = CheckpointCapture.Capture(
                        reader,
                        new CheckpointCaptureRequest(
                            sourceWorld, codecs, CheckpointQueuePolicy.IncludeQueued, catalogFingerprint));
                    if (includedCapture == null || !includedCapture.Captured)
                    {
                        Add(name, false, "the include-queued capture was refused: "
                            + (includedCapture == null
                                ? "the capture returned no result"
                                : includedCapture.Code + ": " + includedCapture.Detail));
                        return;
                    }

                    if (!TryDecode(includedCapture.Document, out Gc018Image? image, out string decodeDetail)
                        || image == null)
                    {
                        Add(name, false, decodeDetail);
                        return;
                    }

                    QueueDisposition quiet = rejectedCapture.Queue;
                    QueueDisposition loud = includedCapture.Queue;
                    bool quietHeld = quiet.Policy == CheckpointQueuePolicy.RejectQueued
                        && quiet.Offered == 1
                        && quiet.Included == 0
                        && quiet.Rejected == 1
                        && quiet.IsAccountedFor
                        && rejectedCapture.Header.QueuePolicy == (uint)CheckpointQueuePolicy.RejectQueued
                        && rejectedCapture.Header.RejectedQueuedCount == 1U
                        && rejectedCapture.Header.CommandCount == 0U
                        && rejectedCapture.Counts.Commands == 0;
                    bool loudHeld = loud.Policy == CheckpointQueuePolicy.IncludeQueued
                        && loud.Offered == 1
                        && loud.Included == 1
                        && loud.Rejected == 0
                        && loud.IsAccountedFor
                        && includedCapture.Header.QueuePolicy == (uint)CheckpointQueuePolicy.IncludeQueued
                        && includedCapture.Header.RejectedQueuedCount == 0U
                        && includedCapture.Header.CommandCount == 1U
                        && includedCapture.Counts.Commands == 1;
                    bool cutoffsAgree = quiet.Cutoff.Value == loud.Cutoff.Value;
                    bool commandHeld = image.Commands.Count == 1
                        && image.Commands[0].IssuerId.Equals(family.Issuer)
                        && image.Commands[0].IssuerSequence == queuedOperation.IssuerSequence
                        && image.Commands[0].Route.Equals(queuedEnvelope.RouteId)
                        && image.Commands[0].Target.Equals(queuedEnvelope.TargetId)
                        && image.Commands[0].Schema.Equals(queuedEnvelope.Schema)
                        && image.Commands[0].Payload != null
                        && image.Commands[0].Payload!.Length == queuedEnvelope.Payload.Length;

                    Add(name, quietHeld && loudHeld && cutoffsAgree && commandHeld,
                        "queued=" + queuedOperation.ToString()
                        + "; route=" + queuedEnvelope.RouteId.ToString()
                        + "; target=" + queuedEnvelope.TargetId.ToString()
                        + "; schema=" + queuedEnvelope.Schema.ToString()
                        + "; payloadBytes=" + queuedEnvelope.Payload.Length.ToString(CultureInfo.InvariantCulture)
                        + "; rejectQueued(" + quiet.ToString() + " accounted=" + quiet.IsAccountedFor
                        + " headerCommands=" + rejectedCapture.Header.CommandCount.ToString(CultureInfo.InvariantCulture)
                        + " headerRejected=" + rejectedCapture.Header.RejectedQueuedCount.ToString(CultureInfo.InvariantCulture)
                        + ")"
                        + "; includeQueued(" + loud.ToString() + " accounted=" + loud.IsAccountedFor
                        + " headerCommands=" + includedCapture.Header.CommandCount.ToString(CultureInfo.InvariantCulture)
                        + " headerRejected=" + includedCapture.Header.RejectedQueuedCount.ToString(CultureInfo.InvariantCulture)
                        + ")"
                        + "; cutoff=" + quiet.Cutoff.Value.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// A capture belongs at a committed boundary (P-030, P-053): while a composition publication is in
            /// flight the lane's committed pair and the world's published pair disagree, and the reader refuses
            /// before it copies anything. The refusal is `TooLate` with no document at all — a capture that refuses
            /// leaves no artifact behind (O-20). The world is then healed by answering the lane publication, so the
            /// rest of the run continues on one publication series (P-006).
            /// </summary>
            private void ProveCaptureRefusesOutsideABoundary()
            {
                const string name = "gc018-capture-refuses-outside-a-boundary";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null
                        || sourceContext == null || codecs == null)
                    {
                        Add(name, false, "the world, its lane or its capture context is missing");
                        return;
                    }

                    CompositionRevision laneRevisionBefore = lane.Committed.Revision;
                    AssemblyEpoch worldEpochBefore = host.CurrentEpoch;

                    // The lane publishes the next composition revision; the world has not published its assembly
                    // for it yet, so a publication is in flight (P-030).
                    EditAdmission admission = lane.SubmitEdit(
                        family.SpareScopeEdits[1], NextOperation(sourceWorld), lane.Committed.Revision);
                    bool staged = admission.Staged;
                    IReadOnlyList<PublishedOperation> published = lane.Drain();
                    bool laneAhead = published.Count > 0 && published[0].Outcome != Outcome.Rejected;

                    var reader = new UnityCommittedBoundaryReader(host, sourceContext);
                    bool atBoundary = reader.IsAtCommittedBoundary;
                    bool readRefused = !reader.TryRead(
                        sourceWorld,
                        out CommittedBoundarySnapshot? snapshot,
                        out BoundaryRefusal refusal,
                        out DiagnosticCode refusalCode,
                        out string refusalDetail);
                    CheckpointCaptureResult refused = CheckpointCapture.Capture(
                        reader,
                        new CheckpointCaptureRequest(
                            sourceWorld, codecs, CheckpointQueuePolicy.RejectQueued, catalogFingerprint));

                    bool healed = PublishPendingAssembly();
                    bool joined = MatchesPublishedAssembly();

                    bool pass = staged
                        && laneAhead
                        && !atBoundary
                        && readRefused
                        && snapshot == null
                        && refusal == BoundaryRefusal.NotAtBoundary
                        && refusalCode == DiagnosticCode.TooLate
                        && !refused.Captured
                        && refused.Code == DiagnosticCode.TooLate
                        && refused.Document.Length == 0
                        && refused.Header.SourceSessionHigh == 0UL
                        && refused.Header.SourceSessionLow == 0UL
                        && healed
                        && joined;

                    Add(name, pass,
                        "laneRevision=" + laneRevisionBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneEpoch=" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + worldEpochBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "; laneAhead=" + laneAhead
                        + "; atCommittedBoundary=" + atBoundary
                        + "; readRefusal=" + refusal + "/" + refusalCode
                        + "; captureRefusal=" + refused.Code
                        + "; documentBytes=" + refused.Document.Length.ToString(CultureInfo.InvariantCulture)
                        + "; healed=" + healed
                        + "; joined=" + joined
                        + "; refusalDetail=" + Gc018Scenario.Clip(refusalDetail, 160)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 3. the refusals that expose nothing

            /// <summary>
            /// A tampered, a truncated and a padded document are all refused by the envelope checks, and the restore
            /// half refuses each of them at its own read stage without ever asking a builder for a world (O-20's
            /// "copy/serialization errors produce no checkpoint", O-21's "rejects without affecting existing
            /// world").
            /// </summary>
            private void ProveCorruptAndTruncatedDocumentsReject()
            {
                const string name = "gc018-corrupt-and-truncated-documents-reject";
                try
                {
                    if (includedCapture == null || codecs == null || directMigrations == null)
                    {
                        Add(name, false, "the captured document or the codec set is missing");
                        return;
                    }

                    int registryBefore = UnityWorldRegistry.Count;
                    byte[] original = includedCapture.Document;
                    byte[] tampered = (byte[])original.Clone();
                    int middle = tampered.Length / 2;
                    tampered[middle] = (byte)(tampered[middle] ^ 0xFF);
                    byte[] truncated = new byte[original.Length - 8];
                    Array.Copy(original, truncated, truncated.Length);
                    byte[] padded = new byte[original.Length + 1];
                    Array.Copy(original, padded, original.Length);
                    padded[original.Length] = 0x42;

                    bool tamperRefused = RefusesRestore(tampered, out DiagnosticCode tamperCode, out string tamperDetail);
                    bool truncateRefused = RefusesRestore(truncated, out DiagnosticCode truncateCode, out string truncateDetail);
                    bool paddedRefused = RefusesRestore(padded, out DiagnosticCode paddedCode, out string paddedDetail);
                    bool registryUnchanged = UnityWorldRegistry.Count == registryBefore;

                    Add(name, tamperRefused && truncateRefused && paddedRefused && registryUnchanged,
                        "bytes=" + original.Length.ToString(CultureInfo.InvariantCulture)
                        + "; tampered=" + tamperCode + " (" + Gc018Scenario.Clip(tamperDetail, 120) + ")"
                        + "; truncated=" + truncateCode + " (" + Gc018Scenario.Clip(truncateDetail, 120) + ")"
                        + "; padded=" + paddedCode + " (" + Gc018Scenario.Clip(paddedDetail, 120) + ")"
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; builderBuilds=" + (builder == null ? 0 : builder.BuildCount).ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// A document whose required schema version this build has no path to is refused as a migration
            /// rejection: "unknown required fields/schema versions reject" (P-054, P-055). The planner refuses and
            /// the executor refuses at its plan stage, and no world is staged by either (O-21).
            /// </summary>
            private void ProveUnknownRequiredSchemaRejectsRestore()
            {
                const string name = "gc018-unknown-required-schema-rejects-restore";
                try
                {
                    if (codecs == null || directMigrations == null || builder == null)
                    {
                        Add(name, false, "the codec set, the migration registry or the restore builder is missing");
                        return;
                    }

                    SchemaRef unknown = new SchemaRef(family.WakePayloadSchema.Id, UnknownSchemaVersion);
                    if (!TryWriteDocument(unknown, false, out byte[] document, out string writeDetail))
                    {
                        Add(name, false, writeDetail);
                        return;
                    }

                    if (!CheckpointDocument.TryRead(
                            document, codecs, out CheckpointDocument? parsed, out DiagnosticCode readCode, out string readDetail)
                        || parsed == null)
                    {
                        Add(name, false, "the hand-made document is not readable: " + readCode + ": " + readDetail);
                        return;
                    }

                    SchemaRef[] allocated = { CheckpointFormat.DocumentSchema, family.WakePayloadSchema };
                    WorldId planSession = new WorldId(sessionSequence.Next());
                    RestorePlanResult planned = CheckpointRestorePlanner.Plan(
                        new CheckpointRestoreRequest(
                            planSession, parsed, codecs, directMigrations, catalogFingerprint, allocated, true));
                    bool refusedAtPlan = !planned.Succeeded
                        && planned.Plan == null
                        && planned.Refusal == RestoreRefusal.MigrationRejected
                        && planned.Code == DiagnosticCode.MigrationRequired;

                    int buildsBefore = builder.BuildCount;
                    int registryBefore = UnityWorldRegistry.Count;
                    WorldId session = new WorldId(sessionSequence.Next());
                    var executor = new CheckpointRestoreExecutor(new RestoreReservationLedger(8), codecs);
                    RestoreOutcome outcome = executor.Restore(
                        document,
                        session,
                        NextOperation(session),
                        builder!,
                        directMigrations,
                        catalogFingerprint,
                        allocated,
                        true);
                    bool refusedAtExecutor = !outcome.Restored
                        && outcome.Stage == RestoreStage.Plan
                        && outcome.Plan == null
                        && builder.BuildCount == buildsBefore
                        && !UnityWorldRegistry.TryGet(session, out UnityWorldHost? _)
                        && UnityWorldRegistry.Count == registryBefore;

                    Add(name, refusedAtPlan && refusedAtExecutor,
                        "declaredSchemaVersion=" + UnknownSchemaVersion.ToString(CultureInfo.InvariantCulture)
                        + "; allocatedSchemaVersion=" + family.WakePayloadSchema.Version.ToString(CultureInfo.InvariantCulture)
                        + "; schema=" + family.WakePayloadSchema.Id.ToString()
                        + "; plannerRefusal=" + planned.Refusal + "/" + planned.Code
                        + "; executorStage=" + outcome.Stage + "/" + outcome.Code
                        + "; restored=" + outcome.Restored
                        + "; builderBuilds=" + builder.BuildCount.ToString(CultureInfo.InvariantCulture)
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; plannerDetail=" + Gc018Scenario.Clip(planned.Detail, 160)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Two registered chains from the requested source into one destination are ambiguous; P-054 refuses
            /// the pair rather than silently picking a path: the planner rejects the restore at its plan stage,
            /// and the reservation's session never becomes a world (O-21).
            /// </summary>
            private void ProveAmbiguousMigrationRejectsRestore()
            {
                const string name = "gc018-ambiguous-migration-rejects-restore";
                try
                {
                    if (codecs == null || builder == null)
                    {
                        Add(name, false, "the codec set or the restore builder is missing");
                        return;
                    }

                    SchemaId schema = family.WakePayloadSchema.Id;
                    var steps = new List<ISchemaMigrationStep>
                    {
                        new Gc018MigrationStep(1UL, schema, 1U, 2U),
                        new Gc018MigrationStep(2UL, schema, 2U, 3U),
                        new Gc018MigrationStep(3UL, schema, 2U, AmbiguousTargetVersion),
                    };
                    var ambiguous = new CheckpointMigrationRegistry(steps);
                    SchemaRef destination = new SchemaRef(schema, AmbiguousTargetVersion);
                    MigrationPlan direct = ambiguous.Plan(family.WakePayloadSchema, destination);
                    bool registryWellFormed = ambiguous.IsWellFormed;

                    if (!TryWriteDocument(family.WakePayloadSchema, false, out byte[] document, out string writeDetail))
                    {
                        Add(name, false, writeDetail);
                        return;
                    }

                    if (!CheckpointDocument.TryRead(
                            document, codecs, out CheckpointDocument? parsed, out DiagnosticCode readCode, out string readDetail)
                        || parsed == null)
                    {
                        Add(name, false, "the hand-made document is not readable: " + readCode + ": " + readDetail);
                        return;
                    }

                    SchemaRef[] allocated = { CheckpointFormat.DocumentSchema, destination };
                    WorldId planSession = new WorldId(sessionSequence.Next());
                    RestorePlanResult planned = CheckpointRestorePlanner.Plan(
                        new CheckpointRestoreRequest(
                            planSession, parsed, codecs, ambiguous, catalogFingerprint, allocated, true));
                    bool refusedAtPlan = !planned.Succeeded
                        && planned.Plan == null
                        && planned.Refusal == RestoreRefusal.MigrationRejected
                        && planned.Code == DiagnosticCode.OwnershipConflict;

                    int buildsBefore = builder.BuildCount;
                    int registryBefore = UnityWorldRegistry.Count;
                    WorldId session = new WorldId(sessionSequence.Next());
                    var executor = new CheckpointRestoreExecutor(new RestoreReservationLedger(8), codecs);
                    RestoreOutcome outcome = executor.Restore(
                        document,
                        session,
                        NextOperation(session),
                        builder!,
                        ambiguous,
                        catalogFingerprint,
                        allocated,
                        true);
                    bool refusedAtExecutor = !outcome.Restored
                        && outcome.Stage == RestoreStage.Plan
                        && outcome.Plan == null
                        && builder.BuildCount == buildsBefore
                        && !UnityWorldRegistry.TryGet(session, out UnityWorldHost? _)
                        && UnityWorldRegistry.Count == registryBefore;

                    Add(name,
                        refusedAtPlan
                        && refusedAtExecutor
                        && registryWellFormed
                        && direct.Outcome == MigrationPlanOutcome.Ambiguous
                        && direct.PathCount == 2,
                        "steps=" + ambiguous.Steps.Count.ToString(CultureInfo.InvariantCulture)
                        + "; registryWellFormed=" + registryWellFormed
                        + "; directPlan=" + direct.ToString()
                        + "; plannerRefusal=" + planned.Refusal + "/" + planned.Code
                        + "; executorStage=" + outcome.Stage + "/" + outcome.Code
                        + "; builderBuilds=" + builder.BuildCount.ToString(CultureInfo.InvariantCulture)
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + "; plannerDetail=" + Gc018Scenario.Clip(planned.Detail, 160)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// A document whose state slot names a target it does not declare is a corrupt reference, not a
            /// precedence question: "missing required targets reject" (05 s6), so the restore refuses with the
            /// identity table's own diagnostic and exposes nothing (O-21).
            /// </summary>
            private void ProveCorruptReferenceRejectsRestore()
            {
                const string name = "gc018-corrupt-reference-rejects-restore";
                try
                {
                    if (codecs == null || directMigrations == null || builder == null)
                    {
                        Add(name, false, "the codec set, the migration registry or the restore builder is missing");
                        return;
                    }

                    if (!TryWriteDocument(family.WakePayloadSchema, true, out byte[] document, out string writeDetail))
                    {
                        Add(name, false, writeDetail);
                        return;
                    }

                    if (!CheckpointDocument.TryRead(
                            document, codecs, out CheckpointDocument? parsed, out DiagnosticCode readCode, out string readDetail)
                        || parsed == null)
                    {
                        Add(name, false, "the hand-made document is not readable: " + readCode + ": " + readDetail);
                        return;
                    }

                    SchemaRef[] allocated = { CheckpointFormat.DocumentSchema, family.WakePayloadSchema };
                    WorldId planSession = new WorldId(sessionSequence.Next());
                    RestorePlanResult planned = CheckpointRestorePlanner.Plan(
                        new CheckpointRestoreRequest(
                            planSession, parsed, codecs, directMigrations, catalogFingerprint, allocated, true));
                    bool refusedAtPlan = !planned.Succeeded
                        && planned.Plan == null
                        && planned.Refusal == RestoreRefusal.CorruptReference
                        && planned.Diagnostics.Count == 1;

                    int buildsBefore = builder.BuildCount;
                    int registryBefore = UnityWorldRegistry.Count;
                    WorldId session = new WorldId(sessionSequence.Next());
                    var executor = new CheckpointRestoreExecutor(new RestoreReservationLedger(8), codecs);
                    RestoreOutcome outcome = executor.Restore(
                        document,
                        session,
                        NextOperation(session),
                        builder!,
                        directMigrations,
                        catalogFingerprint,
                        allocated,
                        true);
                    bool refusedAtExecutor = !outcome.Restored
                        && outcome.Stage == RestoreStage.Plan
                        && outcome.Plan == null
                        && builder.BuildCount == buildsBefore
                        && !UnityWorldRegistry.TryGet(session, out UnityWorldHost? _)
                        && UnityWorldRegistry.Count == registryBefore;

                    Add(name, refusedAtPlan && refusedAtExecutor,
                        "plannerRefusal=" + planned.Refusal + "/" + planned.Code
                        + "; diagnostics=" + planned.Diagnostics.Count.ToString(CultureInfo.InvariantCulture)
                        + "; firstDiagnostic="
                        + (planned.Diagnostics.Count == 0 ? "<none>" : planned.Diagnostics[0].Summary)
                        + "; executorStage=" + outcome.Stage + "/" + outcome.Code
                        + "; builderBuilds=" + builder.BuildCount.ToString(CultureInfo.InvariantCulture)
                        + "; registry=" + registryBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + UnityWorldRegistry.Count.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 4. the round trip

            /// <summary>
            /// The O-21 sequence over the captured document: reserve a fresh session, read, plan, build an
            /// **unexposed** world, validate and expose. The registry is provably unchanged while the world exists
            /// unexposed and provably one larger once it is exposed, so "unexposed then exposed" is an observation
            /// about the registry rather than a claim about the builder (P-049, P-050).
            /// </summary>
            private void RestoreIntoANewUnexposedWorld()
            {
                const string name = "gc018-restore-happens-into-a-new-unexposed-world";
                try
                {
                    if (includedCapture == null || codecs == null || builder == null || directMigrations == null
                        || targets == null)
                    {
                        Add(name, false, "the captured document, the codecs or the restore builder is missing");
                        return;
                    }

                    restoredSession = new WorldId(sessionSequence.Next());
                    var executor = new CheckpointRestoreExecutor(new RestoreReservationLedger(8), codecs);
                    restoreOutcome = executor.Restore(
                        includedCapture.Document,
                        restoredSession,
                        NextOperation(restoredSession),
                        builder!,
                        directMigrations,
                        catalogFingerprint,
                        AllocatedSchemas(),
                        true);

                    RestoreOutcome outcome = restoreOutcome;
                    restoredHost = null;
                    bool exposed = UnityWorldRegistry.TryGet(restoredSession, out UnityWorldHost? restored);
                    restoredHost = restored;
                    int registryAfter = UnityWorldRegistry.Count;
                    RestorePlan? plan = outcome.Plan;
                    IReadOnlyList<SlotRecordValue> capturedSlots = capturedImage == null
                        ? Array.Empty<SlotRecordValue>()
                        : capturedImage.Slots;
                    int plannedInstalls = plan == null ? -1 : plan.Installs.Count;
                    int plannedImports = plan == null ? -1 : CountGrants(plan.Grants, GrantKind.ScopeImport);

                    bool pass = outcome.Restored
                        && outcome.Stage == RestoreStage.Expose
                        && outcome.Code == DiagnosticCode.None
                        && outcome.PublishedToken.HasValue
                        && !outcome.Session.Session.Equals(sourceWorld.Session)
                        && outcome.SourceWorld.Session.Equals(sourceWorld.Session)
                        && plan != null
                        && plan.TargetSession.Session.Equals(restoredSession.Session)
                        && plan.Targets.Count == targets.Count
                        && plan.Slots.Count == capturedSlots.Count
                        && plan.DormantSlotCount == CountSlots(capturedSlots, false)
                        && plan.IsDirect
                        && outcome.RestoredTargets == plan.Targets.Count
                        && outcome.RestoredSlots == plan.Slots.Count
                        && outcome.DormantSlots == plan.DormantSlotCount
                        && builder.BuildCount == 1
                        && builder.RegistryCountAfterStaging == builder.RegistryBaseline
                        && builder.RegistryBaseline + 1 == registryAfter
                        && exposed
                        && ReferenceEquals(restoredHost, builder.Staging)
                        && builder.RebuiltTargetCount == plan.Targets.Count
                        && builder.BaseLayoutCount == plan.Targets.Count
                        && builder.RebuiltSlotCount == plan.Slots.Count
                        && builder.RebuiltDormantSlotCount == plan.DormantSlotCount
                        && builder.ReplayedScopeCount >= 1
                        && builder.ReplayedBoundaryCount >= 1
                        && plannedInstalls >= 1
                        && plannedImports >= 1
                        && builder.ReplayedInstallCount == plannedInstalls
                        && builder.ReplayedImportCount == plannedImports
                        && builder.ReplayedModeCount == 1
                        && builder.ReadmittedCommandCount == plan.Commands.Count
                        && builder.RebuiltClockCount == 1
                        && builder.RebuiltWakeCount == 1
                        && builder.PublishedAssemblyCount >= 1
                        && builder.Lane != null
                        && builder.Publisher != null
                        && restoredHost != null
                        && restoredHost.CurrentEpoch.Value >= 2UL
                        && AssemblyPublisher.MatchesPublishedAssembly(
                            builder.Lane.Committed.Revision,
                            builder.Lane.Committed.Epoch,
                            builder.Publisher.PublishedRevision,
                            restoredHost.CurrentEpoch);

                    Add(name, pass,
                        "sourceSession=" + sourceWorld.Session.ToString()
                        + "; restoredSession=" + restoredSession.Session.ToString()
                        + "; registryBaseline=" + builder.RegistryBaseline.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfterStaging=" + builder.RegistryCountAfterStaging.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfterExpose=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; stage=" + outcome.Stage
                        + "; code=" + outcome.Code
                        + "; restoredTargets=" + outcome.RestoredTargets.ToString(CultureInfo.InvariantCulture)
                        + "; restoredSlots=" + outcome.RestoredSlots.ToString(CultureInfo.InvariantCulture)
                        + "; dormantSlots=" + outcome.DormantSlots.ToString(CultureInfo.InvariantCulture)
                        + "; migrations=" + (plan == null ? -1 : plan.Migrations.Count)
                        + "; replayedScopes=" + builder.ReplayedScopeCount.ToString(CultureInfo.InvariantCulture)
                        + "; replayedBoundaries=" + builder.ReplayedBoundaryCount.ToString(CultureInfo.InvariantCulture)
                        + "; replayedInstalls=" + builder.ReplayedInstallCount.ToString(CultureInfo.InvariantCulture)
                        + "/" + plannedInstalls.ToString(CultureInfo.InvariantCulture)
                        + "; replayedImports=" + builder.ReplayedImportCount.ToString(CultureInfo.InvariantCulture)
                        + "/" + plannedImports.ToString(CultureInfo.InvariantCulture)
                        + "; replayedModes=" + builder.ReplayedModeCount.ToString(CultureInfo.InvariantCulture)
                        + "; readmittedCommands=" + builder.ReadmittedCommandCount.ToString(CultureInfo.InvariantCulture)
                        + "; clocks=" + builder.RebuiltClockCount.ToString(CultureInfo.InvariantCulture)
                        + " wakes=" + builder.RebuiltWakeCount.ToString(CultureInfo.InvariantCulture)
                        + "; assembliesPublished=" + builder.PublishedAssemblyCount.ToString(CultureInfo.InvariantCulture)
                        + "; restoredEpoch=" + (restoredHost == null ? 0UL : restoredHost.CurrentEpoch.Value)
                        + "; exposureDetail=" + Gc018Scenario.Clip(outcome.Detail, 160)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The point of a round trip through stable identities: the restored world carries the same target
            /// identity set, at native `Entity.Index` values that are provably not the source world's, with new
            /// handles — because a checkpoint records no native index at all (P-005, 04 s5).
            /// </summary>
            private void ProveRestoredIndicesDiffer()
            {
                const string name = "gc018-restore-recreates-state-at-different-native-indices";
                try
                {
                    if (restoreOutcome?.Plan == null || builder == null)
                    {
                        Add(name, false, "the restore did not produce a plan");
                        return;
                    }

                    if (!EnsureRestoredImage(out string imageDetail))
                    {
                        Add(name, false, imageDetail);
                        return;
                    }

                    RestorePlan plan = restoreOutcome.Plan;
                    var pairs = new List<string>();
                    var restoredIndices = new List<int>();
                    int resolved = 0;
                    int differ = 0;
                    int allocatedHandles = 0;
                    for (int i = 0; i < plan.Targets.Count; i++)
                    {
                        TargetId target = plan.Targets[i].Target;
                        bool hasSource = sourceIndices.TryGetValue(target.Value, out int sourceIndex);
                        bool hasRestored = builder.TryEntityIndexOf(target, out int restoredIndex);
                        bool hasHandle = builder.TryHandleOf(target, out TargetHandle restoredHandle)
                            && restoredHandle.IsAllocated;
                        bool hasSourceHandle = sourceHandles.TryGetValue(target.Value, out TargetHandle sourceHandle);
                        if (hasSource && hasRestored && hasHandle && hasSourceHandle)
                        {
                            resolved++;
                            allocatedHandles++;
                            restoredIndices.Add(restoredIndex);
                            if (sourceIndex != restoredIndex)
                            {
                                differ++;
                            }

                            pairs.Add(target.ToString() + ":" + sourceIndex.ToString(CultureInfo.InvariantCulture)
                                + "->" + restoredIndex.ToString(CultureInfo.InvariantCulture)
                                + "(slot" + sourceHandle.Slot.ToString(CultureInfo.InvariantCulture) + "/"
                                + sourceHandle.Generation.ToString(CultureInfo.InvariantCulture) + "->slot"
                                + restoredHandle.Slot.ToString(CultureInfo.InvariantCulture) + "/"
                                + restoredHandle.Generation.ToString(CultureInfo.InvariantCulture) + ")");
                        }
                        else
                        {
                            pairs.Add(target.ToString() + ":unresolved");
                        }
                    }

                    bool identitiesEqual = string.Equals(
                        Canonical(TargetLines(plan.Targets)),
                        Canonical(TargetLines(restoredImage!.Targets)),
                        StringComparison.Ordinal);
                    bool blocksDisjoint = true;
                    foreach (KeyValuePair<Id128, int> pair in sourceIndices)
                    {
                        if (restoredIndices.Contains(pair.Value))
                        {
                            blocksDisjoint = false;
                        }
                    }

                    Add(name,
                        resolved == plan.Targets.Count
                        && differ == plan.Targets.Count
                        && allocatedHandles == plan.Targets.Count
                        && identitiesEqual
                        && blocksDisjoint
                        && restoredImage.Targets.Count == plan.Targets.Count,
                        "targets=" + plan.Targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; sourceAuxiliaryIndices=" + auxiliaryFirstIndex.ToString(CultureInfo.InvariantCulture)
                        + ".." + auxiliaryLastIndex.ToString(CultureInfo.InvariantCulture)
                        + "; differ=" + differ.ToString(CultureInfo.InvariantCulture)
                        + "; identitySetEqual=" + identitiesEqual
                        + "; indexBlocksDisjoint=" + blocksDisjoint
                        + "; pairs=" + Gc018Scenario.Clip(string.Join(",", pairs.ToArray()), 480)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Dormant state is authoritative state (P-032): the restored world carries the same `(owner, slot)`
            /// rows with the same version, the same value and the same active flag, read back from the restored
            /// world's own storage through the same boundary reader that produced the capture.
            /// </summary>
            private void ProveDormantSlotsSurvive()
            {
                const string name = "gc018-restore-preserves-dormant-slots";
                try
                {
                    if (restoreOutcome?.Plan == null || capturedImage == null || builder == null)
                    {
                        Add(name, false, "the source capture or the restore plan is missing");
                        return;
                    }

                    if (!EnsureRestoredImage(out string imageDetail))
                    {
                        Add(name, false, imageDetail);
                        return;
                    }

                    IReadOnlyList<SlotRecordValue> captured = capturedImage.Slots;
                    IReadOnlyList<SlotRecordValue> restored = restoredImage!.Slots;
                    bool rowsEqual = string.Equals(
                        Canonical(SlotLines(captured)), Canonical(SlotLines(restored)), StringComparison.Ordinal);
                    int capturedDormant = CountSlots(captured, false);
                    int restoredDormant = CountSlots(restored, false);
                    int capturedActive = CountSlots(captured, true);
                    int restoredActive = CountSlots(restored, true);

                    bool dormantSurvived = TryFindSlot(
                            captured, family.DormantTarget, family.DormantOwner, family.DormantSlot, out SlotRecordValue capturedDormantRow)
                        && TryFindSlot(
                            restored, family.DormantTarget, family.DormantOwner, family.DormantSlot, out SlotRecordValue restoredDormantRow)
                        && !capturedDormantRow.Active
                        && !restoredDormantRow.Active
                        && capturedDormantRow.SchemaVersion == family.DormantVersion
                        && restoredDormantRow.SchemaVersion == family.DormantVersion
                        && capturedDormantRow.Value == family.DormantValue
                        && restoredDormantRow.Value == family.DormantValue;
                    bool activeSurvived = TryFindSlot(
                            restored, family.MovedTarget, family.MutableOwner, family.MutableSlot, out SlotRecordValue restoredActiveRow)
                        && restoredActiveRow.Active
                        && restoredActiveRow.Value == family.MutableValue
                        && restoredActiveRow.SchemaVersion == family.ActiveSlotVersion;
                    bool liveStorage = builder.Registry != null && builder.Registry.IsLive(family.DormantTarget);

                    Add(name,
                        rowsEqual
                        && capturedDormant == 1
                        && restoredDormant == 1
                        && capturedActive == 1
                        && restoredActive == 1
                        && dormantSurvived
                        && activeSurvived
                        && liveStorage
                        && restoreOutcome.Plan.DormantSlotCount == 1
                        && restoredImage.Header.SlotCount == (uint)captured.Count,
                        "slots=" + captured.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + restored.Count.ToString(CultureInfo.InvariantCulture)
                        + "; dormant=" + capturedDormant.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredDormant.ToString(CultureInfo.InvariantCulture)
                        + "; active=" + capturedActive.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredActive.ToString(CultureInfo.InvariantCulture)
                        + "; dormantRow=" + family.DormantTarget.ToString() + "/"
                        + family.DormantValue.ToString(CultureInfo.InvariantCulture) + "@"
                        + family.DormantVersion.ToString(CultureInfo.InvariantCulture) + " active=False"
                        + "; activeRow=" + family.MovedTarget.ToString() + "/"
                        + family.MutableValue.ToString(CultureInfo.InvariantCulture) + "@"
                        + family.ActiveSlotVersion.ToString(CultureInfo.InvariantCulture) + " active=True"
                        + "; rowsEqual=" + rowsEqual
                        + "; liveStorage=" + liveStorage
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// The composition half of the definition: the same world mode, the same scope tree with the same
            /// per-scope isolation sets, the same Conservative-mode imports and the same explicit exclusions. The
            /// source world carries at least one real provider-backed scope import, one exclusion and one named
            /// capability-isolation member, so a restore that lost grants or reopened a boundary cannot pass this
            /// (P-013, P-016).
            /// </summary>
            private void ProveModeImportsAndExclusionsSurvive()
            {
                const string name = "gc018-restore-preserves-mode-imports-and-exclusions";
                try
                {
                    if (restoreOutcome?.Plan == null || capturedImage == null || builder?.Lane == null || lane == null)
                    {
                        Add(name, false, "the source capture, the restore plan or a lane is missing");
                        return;
                    }

                    if (!EnsureRestoredImage(out string imageDetail))
                    {
                        Add(name, false, imageDetail);
                        return;
                    }

                    PropagationMode captured = capturedImage.Header.Propagation;
                    PropagationMode planned = restoreOutcome.Plan.Header.Propagation;
                    PropagationMode restored = restoredImage!.Header.Propagation;
                    PropagationMode sourceLaneMode = lane.Committed.Mode;
                    PropagationMode restoredLaneMode = builder.Lane.Committed.Mode;

                    bool scopesEqual = string.Equals(
                        Canonical(ScopeLines(capturedImage.Scopes)),
                        Canonical(ScopeLines(restoredImage.Scopes)),
                        StringComparison.Ordinal);
                    bool grantsEqual = string.Equals(
                        Canonical(GrantLines(capturedImage.Grants)),
                        Canonical(GrantLines(restoredImage.Grants)),
                        StringComparison.Ordinal);
                    bool installsEqual = string.Equals(
                        Canonical(InstallLines(capturedImage.Installs)),
                        Canonical(InstallLines(restoredImage.Installs)),
                        StringComparison.Ordinal);


                    int installs = capturedImage.Installs.Count;
                    int restoredInstalls = restoredImage.Installs.Count;
                    int imports = CountGrants(capturedImage.Grants, GrantKind.ScopeImport);
                    int restoredImports = CountGrants(restoredImage.Grants, GrantKind.ScopeImport);
                    int exclusions = CountGrants(capturedImage.Grants, GrantKind.Exclusion);
                    int restoredExclusions = CountGrants(restoredImage.Grants, GrantKind.Exclusion);
                    int capabilityMembers = CountGrants(capturedImage.Grants, GrantKind.CapabilityIsolationMember);
                    int restoredCapabilityMembers = CountGrants(restoredImage.Grants, GrantKind.CapabilityIsolationMember);
                    int serviceMembers = CountGrants(capturedImage.Grants, GrantKind.ServiceIsolationMember);
                    int restoredServiceMembers = CountGrants(restoredImage.Grants, GrantKind.ServiceIsolationMember);

                    Add(name,
                        captured == planned
                        && restored == planned
                        && planned == sourceLaneMode
                        && restoredLaneMode == planned
                        && scopesEqual
                        && grantsEqual
                        && captured == PropagationMode.Conservative
                        && installsEqual
                        && installs >= 1
                        && installs == restoredInstalls
                        && ScopeCountsAgree(capturedImage, restoredImage)
                        && imports == restoredImports
                        && imports >= 1
                        && exclusions == restoredExclusions
                        && exclusions >= 1
                        && capabilityMembers == restoredCapabilityMembers
                        && capabilityMembers >= 1
                        && serviceMembers == restoredServiceMembers
                        && builder.ReplayedBoundaryCount >= 1
                        && builder.ReplayedInstallCount >= 1
                        && builder.ReplayedImportCount >= 1
                        && builder.ReplayedModeCount == 1,
                        "mode=" + sourceLaneMode
                        + "->" + restoredLaneMode
                        + " (captured=" + captured + " planned=" + planned + ")"
                        + "; scopes=" + capturedImage.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredImage.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + " treeEqual=" + scopesEqual
                        + "; grants=" + capturedImage.Grants.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredImage.Grants.Count.ToString(CultureInfo.InvariantCulture)
                        + " rowsEqual=" + grantsEqual
                        + "; installs=" + installs.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredInstalls.ToString(CultureInfo.InvariantCulture)
                        + " rowsEqual=" + installsEqual
                        + "; imports=" + imports.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredImports.ToString(CultureInfo.InvariantCulture)
                        + "; exclusions=" + exclusions.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredExclusions.ToString(CultureInfo.InvariantCulture)
                        + "; capabilityIsolationMembers=" + capabilityMembers.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredCapabilityMembers.ToString(CultureInfo.InvariantCulture)
                        + "; serviceIsolationMembers=" + serviceMembers.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredServiceMembers.ToString(CultureInfo.InvariantCulture)
                        + "; replayedBoundaries=" + builder.ReplayedBoundaryCount.ToString(CultureInfo.InvariantCulture)
                        + "; replayedInstalls=" + builder.ReplayedInstallCount.ToString(CultureInfo.InvariantCulture)
                        + "; replayedImports=" + builder.ReplayedImportCount.ToString(CultureInfo.InvariantCulture)
                        + "; replayedModes=" + builder.ReplayedModeCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// Deterministic state continues rather than restarting: the same RNG stream states and draw counts,
            /// the same clock declarations and pending wakes with their remaining delay, the same committed-event
            /// cursor and the same per-issuer high-water mark — the last two observed over the restored world's own
            /// re-admitted command, so duplicate suppression still holds for the issuer (P-008, P-038, P-045,
            /// P-050, P-053).
            /// </summary>
            private void ProveClocksRngAndCursorsContinue()
            {
                const string name = "gc018-restore-continues-clocks-rng-and-cursors";
                try
                {
                    if (capturedImage == null || builder?.Rng == null || restoredHost == null
                        || restoredHost.Messages == null)
                    {
                        Add(name, false, "the source capture or the restored world is missing");
                        return;
                    }

                    if (!EnsureRestoredImage(out string imageDetail))
                    {
                        Add(name, false, imageDetail);
                        return;
                    }

                    bool rngEqual = string.Equals(
                        Canonical(RngLines(capturedImage.RngStreams)),
                        Canonical(RngLines(restoredImage!.RngStreams)),
                        StringComparison.Ordinal);
                    bool clocksEqual = string.Equals(
                        Canonical(ClockLines(capturedImage.Clocks)),
                        Canonical(ClockLines(restoredImage.Clocks)),
                        StringComparison.Ordinal);
                    bool cursorsEqual = string.Equals(
                        Canonical(CursorLines(capturedImage.Cursors)),
                        Canonical(CursorLines(restoredImage.Cursors)),
                        StringComparison.Ordinal);

                    ulong sourceWatermark = 0UL;
                    ulong restoredWatermark = 0UL;
                    bool issuerHeld = TryIssuerWatermark(capturedImage.Cursors, family.Issuer, out sourceWatermark)
                        && TryIssuerWatermark(restoredImage.Cursors, family.Issuer, out restoredWatermark)
                        && sourceWatermark == restoredWatermark
                        && sourceWatermark == queuedOperation.IssuerSequence;
                    ulong sourceEvent = 0UL;
                    ulong restoredEvent = 0UL;
                    bool eventCursorHeld = TryEventCursor(capturedImage.Cursors, out sourceEvent)
                        && TryEventCursor(restoredImage.Cursors, out restoredEvent)
                        && sourceEvent == restoredEvent
                        && sourceEvent == restoredHost.Messages.LastEventSequence.Value;

                    // A restored cursor is re-stamped with the restored session: the stamps must differ, and each
                    // must name its own world (P-004).
                    bool stampsRestamped = CursorsBelongTo(capturedImage.Cursors, sourceWorld.Session)
                        && CursorsBelongTo(restoredImage.Cursors, restoredSession.Session)
                        && !sourceWorld.Session.Equals(restoredSession.Session);

                    bool rngAndClocksNonTrivial = builder.Rng.Count == RngStreamCount
                        && RngStreamsAreDrawn(capturedImage.RngStreams)
                        && RngStreamsAreDrawn(restoredImage.RngStreams)
                        && ClockRowsAreNonTrivial(capturedImage.Clocks)
                        && ClockRowsAreNonTrivial(restoredImage.Clocks);
                    bool readmittedCommandWaits = restoredHost.Messages.Requests.PendingCount == 1
                        && restoredHost.Messages.Requests.RowCount == 1;

                    Add(name,
                        rngEqual
                        && clocksEqual
                        && cursorsEqual
                        && issuerHeld
                        && eventCursorHeld
                        && stampsRestamped
                        && rngAndClocksNonTrivial
                        && readmittedCommandWaits,
                        "rngStreams=" + capturedImage.RngStreams.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredImage.RngStreams.Count.ToString(CultureInfo.InvariantCulture)
                        + " positionsEqual=" + rngEqual
                        + " draws=" + string.Join(",", DrawCounts(capturedImage.RngStreams).ToArray())
                        + "; clocks=" + capturedImage.Clocks.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredImage.Clocks.Count.ToString(CultureInfo.InvariantCulture)
                        + " rowsEqual=" + clocksEqual
                        + " wakes=" + builder.RebuiltWakeCount.ToString(CultureInfo.InvariantCulture)
                        + "; cursors=" + capturedImage.Cursors.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredImage.Cursors.Count.ToString(CultureInfo.InvariantCulture)
                        + " rowsEqual=" + cursorsEqual
                        + "; lastEventSequence=" + sourceEvent.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredEvent.ToString(CultureInfo.InvariantCulture)
                        + "; issuerHighWater=" + sourceWatermark.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredWatermark.ToString(CultureInfo.InvariantCulture)
                        + "; pendingCommands=" + restoredHost.Messages.Requests.PendingCount.ToString(CultureInfo.InvariantCulture)
                        + "; stampsRestamped=" + stampsRestamped
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// O-21's own probe: "old callback cannot target restored entity". Neither an old `TargetHandle` nor an
            /// old `OperationId` is valid in the restored session, while the stable target identity still resolves
            /// to a new handle — and the refusal of a re-submitted old command creates no ledger row at all
            /// (P-004, P-005, P-049, P-050).
            /// </summary>
            private void ProveOldCallbacksCannotTargetTheNewSession()
            {
                const string name = "gc018-old-callbacks-cannot-target-the-new-session";
                try
                {
                    if (host?.Messages == null || restoredHost?.Messages == null || builder?.Registry == null
                        || registry == null || queuedEnvelope == null)
                    {
                        Add(name, false, "the source world, the restored world or a registry is missing");
                        return;
                    }

                    bool sourceRequestKnown = host.Messages.Requests.TryGet(queuedOperation, out RequestRow? sourceRow)
                        && sourceRow != null;
                    bool oldHandleWasLive = sourceHandles.TryGetValue(
                            family.MovedTarget.Value, out TargetHandle oldHandle)
                        && registry.TryResolve(oldHandle, out TargetId resolvedTarget, out Entity _, out DiagnosticCode liveCode)
                        && resolvedTarget.Equals(family.MovedTarget);
                    bool oldHandleRefused = !builder.Registry.TryResolve(
                        oldHandle, out TargetId _, out Entity _, out DiagnosticCode staleCode);
                    bool identityStillResolves = builder.Registry.TryResolveTarget(
                            family.MovedTarget, out TargetHandle newHandle, out Entity _)
                        && newHandle.IsAllocated
                        && !newHandle.Equals(oldHandle);
                    bool oldRequestUnknown = !restoredHost.Messages.Requests.TryGet(queuedOperation, out RequestRow? _);
                    // The capture carried this command as a record, and the restore re-admitted it at the request
                    // identity that record names inside the restored session: same issuer and sequence, fresh world
                    // (P-004, P-050). `RequestIdIn` is the record's method, so the id is rebuilt from the same three
                    // parts here rather than called on the operation itself.
                    var readmittedRequest = new OperationId(
                        restoredSession, queuedOperation.IssuerId, queuedOperation.IssuerSequence);
                    bool newRequestKnown = restoredHost.Messages.Requests.TryGet(readmittedRequest, out RequestRow? newRow)
                        && newRow != null;

                    int rowsBefore = restoredHost.Messages.Requests.RowCount;
                    var resubmitted = new CommandEnvelope(
                        queuedOperation,
                        queuedEnvelope.RouteId,
                        queuedEnvelope.TargetId,
                        queuedEnvelope.Schema,
                        null,
                        queuedEnvelope.Payload);
                    CommandAdmissionReceipt refused = restoredHost.Submit(resubmitted);
                    bool resubmitRefused = !refused.Admitted
                        && refused.Result.Kind == RequestResultKind.Rejected
                        && refused.Result.Reason == DiagnosticCode.StaleHandle;
                    bool rowsUnchanged = restoredHost.Messages.Requests.RowCount == rowsBefore;

                    Add(name,
                        sourceRequestKnown
                        && oldHandleWasLive
                        && oldHandleRefused
                        && identityStillResolves
                        && oldRequestUnknown
                        && newRequestKnown
                        && resubmitRefused
                        && rowsUnchanged,
                        "sourceRequestKnown=" + sourceRequestKnown
                        + "; oldHandleWasLive=" + oldHandleWasLive
                        + " (slot" + oldHandle.Slot.ToString(CultureInfo.InvariantCulture) + "/"
                        + oldHandle.Generation.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; oldHandleRefused=" + oldHandleRefused + " (" + staleCode + ")"
                        + "; restoredHandle=slot" + newHandle.Slot.ToString(CultureInfo.InvariantCulture) + "/"
                        + newHandle.Generation.ToString(CultureInfo.InvariantCulture)
                        + "; identityStillResolves=" + identityStillResolves
                        + "; oldRequestKnownInRestored=" + (!oldRequestUnknown)
                        + "; newRequestKnown=" + newRequestKnown
                        + "; resubmitAdmitted=" + refused.Admitted
                        + " reason=" + refused.Result.Reason
                        + "; ledgerRows=" + rowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredHost.Messages.Requests.RowCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            /// <summary>
            /// A restored world is a running world, not a frozen image: it routes the command the capture carried
            /// and commits at least one admitted logical step, with no fault and a joined publication series
            /// (O-21's "then publish", P-035, P-037).
            /// </summary>
            private void ProveRestoredWorldPublishesAndAdvances()
            {
                const string name = "gc018-restored-world-publishes-and-advances";
                try
                {
                    if (restoredHost == null || builder?.Time == null || builder.Lane == null
                        || builder.Publisher == null)
                    {
                        Add(name, false, "the restored world or its time driver is missing");
                        return;
                    }

                    LogicalStepId stepBefore = restoredHost.CurrentStep;
                    ulong demandBefore = restoredHost.PendingDemand;
                    TimeFrameReport frame = builder.Time.PumpFrame(IdlePumpTicks);
                    ulong steps = frame.StepsCommitted;
                    bool alive = restoredHost.Lifecycle == WorldLifecycleState.Running
                        && !restoredHost.Driver.IsFaulted
                        && restoredHost.FaultCount == 0;
                    bool advanced = restoredHost.CurrentStep.Value > stepBefore.Value;
                    bool joined = AssemblyPublisher.MatchesPublishedAssembly(
                        builder.Lane.Committed.Revision,
                        builder.Lane.Committed.Epoch,
                        builder.Publisher.PublishedRevision,
                        restoredHost.CurrentEpoch);
                    int pendingAfter = restoredHost.Messages == null ? -1 : restoredHost.Messages.Requests.PendingCount;

                    Add(name,
                        alive
                        && frame.Pump.Pumped
                        && demandBefore == 1UL
                        && steps >= 1UL
                        && advanced
                        && steps == restoredHost.CurrentStep.Value - stepBefore.Value
                        && joined,
                        "branch=admitted-step"
                        + "; demandBefore=" + demandBefore.ToString(CultureInfo.InvariantCulture)
                        + "; stepsCommitted=" + steps.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + restoredHost.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; pump=" + frame.Pump.Code
                        + "; pumped=" + frame.Pump.Pumped
                        + "; restored=" + frame.RestoredInput.ToString(CultureInfo.InvariantCulture)
                        + "; dueWakes=" + frame.DueWakes.ToString(CultureInfo.InvariantCulture)
                        + "; lifecycle=" + restoredHost.Lifecycle
                        + "; faults=" + restoredHost.FaultCount.ToString(CultureInfo.InvariantCulture)
                        + "; pendingCommands=" + pendingAfter.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joined
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 5. teardown

            /// <summary>
            /// Both worlds are settled and disposed and the registry returns to its pre-run baseline, so the run
            /// leaves neither a live world nor a registry entry behind (P-035, P-048).
            /// </summary>
            private void TearDownSafely()
            {
                const string name = "gc018-teardown-disposes-both-worlds";
                try
                {
                    if (host == null)
                    {
                        Add(name, false, "no source world");
                        return;
                    }

                    int registryBeforeTeardown = UnityWorldRegistry.Count;
                    int discardedCommands = 0;
                    int pendingWakes = 0;
                    if (time != null)
                    {
                        time.Clear(out discardedCommands, out pendingWakes);
                    }

                    OperationResult sourceStop = host.Stop(
                        NextOperation(sourceWorld), "gc-018 checkpoint qualification teardown");
                    UnityWorldHost source = host;
                    source.Dispose();
                    int sourceOutstanding = source.Ledger.OutstandingJobCount;
                    int sourceRetained = source.Ledger.RetainedResourceCount;

                    int restoredOutstanding = -1;
                    int restoredRetained = -1;
                    Outcome restoredOutcome = Outcome.Faulted;
                    bool restoredDisposed = false;
                    if (restoredHost != null)
                    {
                        // The family's runtime module is released before its world, so no family counter can be
                        // incremented after the storage it reads is gone (P-048).
                        builder?.DetachRuntime();
                        OperationResult restoredStop = restoredHost.Stop(
                            NextOperation(restoredSession), "gc-018 restored world teardown");
                        restoredOutcome = restoredStop.Outcome;
                        UnityWorldHost restored = restoredHost;
                        restored.Dispose();
                        restoredDisposed = restored.Lifecycle == WorldLifecycleState.Disposed;
                        restoredOutstanding = restored.Ledger.OutstandingJobCount;
                        restoredRetained = restored.Ledger.RetainedResourceCount;
                    }

                    int registryAfter = UnityWorldRegistry.Count;

                    Add(name,
                        (sourceStop.Outcome == Outcome.Published || sourceStop.Outcome == Outcome.NoChange)
                        && (restoredOutcome == Outcome.Published || restoredOutcome == Outcome.NoChange)
                        && sourceOutstanding == 0
                        && sourceRetained == 0
                        && restoredOutstanding == 0
                        && restoredRetained == 0
                        && restoredDisposed
                        && registryAfter == registryBeforeCreate
                        && !UnityWorldRegistry.TryGet(sourceWorld, out UnityWorldHost? _)
                        && !UnityWorldRegistry.TryGet(restoredSession, out UnityWorldHost? _),
                        "sourceStop=" + sourceStop.Outcome + "(" + sourceStop.Code + ")"
                        + "; restoredStop=" + restoredOutcome
                        + "; sourceOutstandingJobs=" + sourceOutstanding.ToString(CultureInfo.InvariantCulture)
                        + "; sourceRetainedResources=" + sourceRetained.ToString(CultureInfo.InvariantCulture)
                        + "; restoredOutstandingJobs=" + restoredOutstanding.ToString(CultureInfo.InvariantCulture)
                        + "; restoredRetainedResources=" + restoredRetained.ToString(CultureInfo.InvariantCulture)
                        + "; discardedCommands=" + discardedCommands.ToString(CultureInfo.InvariantCulture)
                        + "; pendingWakes=" + pendingWakes.ToString(CultureInfo.InvariantCulture)
                        + "; registryBefore=" + registryBeforeTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; registryBaseline=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; sourceSessionRegistered=" + UnityWorldRegistry.TryGet(sourceWorld, out UnityWorldHost? _)
                        + "; restoredSessionRegistered=" + UnityWorldRegistry.TryGet(restoredSession, out UnityWorldHost? _)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ helpers

            /// <summary>
            /// Binds the twelve generated serializers of the committed checkpoint catalog to the engine-free codec
            /// seam. The generated serializers speak their own nested value structs, while the capture, document and
            /// restore pipeline speaks the contract record values, so each binding carries the two field-exact
            /// conversions of its kind beside the generated `Serialize`/`TryDeserialize` method groups (05 s6,
            /// P-053, P-054). A missing serializer is reported here, before any capture, because a capture without
            /// every required serializer must be refused rather than write a partial document.
            /// </summary>
            private bool TryBuildCodecs(out string detail)
            {
                var header = new CheckpointCatalog.HeaderRecordSerializer();
                var scope = new CheckpointCatalog.ScopeRecordSerializer();
                var install = new CheckpointCatalog.InstallRecordSerializer();
                var selection = new CheckpointCatalog.SelectionRecordSerializer();
                var target = new CheckpointCatalog.TargetRecordSerializer();
                var slot = new CheckpointCatalog.SlotRecordSerializer();
                var grant = new CheckpointCatalog.GrantRecordSerializer();
                var clock = new CheckpointCatalog.ClockRecordSerializer();
                var command = new CheckpointCatalog.CommandRecordSerializer();
                var message = new CheckpointCatalog.MessageRecordSerializer();
                var rngSerializer = new CheckpointCatalog.RngRecordSerializer();
                var cursor = new CheckpointCatalog.CursorRecordSerializer();

                bindings = new CheckpointSerializerBindings(
                    new CheckpointRecordSerializer<HeaderRecordValue, CheckpointCatalog.HeaderRecordValue>(
                        header.Schema, header.Serialize, header.TryDeserialize,
                        HeaderToGenerated, HeaderFromGenerated),
                    new CheckpointRecordSerializer<ScopeRecordValue, CheckpointCatalog.ScopeRecordValue>(
                        scope.Schema, scope.Serialize, scope.TryDeserialize,
                        ScopeToGenerated, ScopeFromGenerated),
                    new CheckpointRecordSerializer<InstallRecordValue, CheckpointCatalog.InstallRecordValue>(
                        install.Schema, install.Serialize, install.TryDeserialize,
                        InstallToGenerated, InstallFromGenerated),
                    new CheckpointRecordSerializer<SelectionRecordValue, CheckpointCatalog.SelectionRecordValue>(
                        selection.Schema, selection.Serialize, selection.TryDeserialize,
                        SelectionToGenerated, SelectionFromGenerated),
                    new CheckpointRecordSerializer<TargetRecordValue, CheckpointCatalog.TargetRecordValue>(
                        target.Schema, target.Serialize, target.TryDeserialize,
                        TargetToGenerated, TargetFromGenerated),
                    new CheckpointRecordSerializer<SlotRecordValue, CheckpointCatalog.SlotRecordValue>(
                        slot.Schema, slot.Serialize, slot.TryDeserialize,
                        SlotToGenerated, SlotFromGenerated),
                    new CheckpointRecordSerializer<GrantRecordValue, CheckpointCatalog.GrantRecordValue>(
                        grant.Schema, grant.Serialize, grant.TryDeserialize,
                        GrantToGenerated, GrantFromGenerated),
                    new CheckpointRecordSerializer<ClockRecordValue, CheckpointCatalog.ClockRecordValue>(
                        clock.Schema, clock.Serialize, clock.TryDeserialize,
                        ClockToGenerated, ClockFromGenerated),
                    new CheckpointRecordSerializer<CommandRecordValue, CheckpointCatalog.CommandRecordValue>(
                        command.Schema, command.Serialize, command.TryDeserialize,
                        CommandToGenerated, CommandFromGenerated),
                    new CheckpointRecordSerializer<MessageRecordValue, CheckpointCatalog.MessageRecordValue>(
                        message.Schema, message.Serialize, message.TryDeserialize,
                        MessageToGenerated, MessageFromGenerated),
                    new CheckpointRecordSerializer<RngRecordValue, CheckpointCatalog.RngRecordValue>(
                        rngSerializer.Schema, rngSerializer.Serialize, rngSerializer.TryDeserialize,
                        RngToGenerated, RngFromGenerated),
                    new CheckpointRecordSerializer<CursorRecordValue, CheckpointCatalog.CursorRecordValue>(
                        cursor.Schema, cursor.Serialize, cursor.TryDeserialize,
                        CursorToGenerated, CursorFromGenerated));

                codecs = bindings.ToCodecSet();
                if (!codecs.IsComplete)
                {
                    detail = "the generated checkpoint catalog is incomplete: " + DescribeKinds(codecs.MissingKinds());
                    return false;
                }

                codecEvidence = "kinds=" + codecs.CompleteKindCount.ToString(CultureInfo.InvariantCulture)
                    + " schemas=" + codecs.Schemas.Count.ToString(CultureInfo.InvariantCulture);
                detail = codecEvidence;
                return true;
            }

            // The twelve record kinds' contract value and generated value are field-for-field the same type with
            // two names, so each kind converts by copying the whole field list through the constructor — never by
            // re-encoding, defaulting or dropping a field. The two directions of one kind sit beside each other so
            // a field added to one struct and not the other is a compile error in this block, not a silent truncate.

            private static CheckpointCatalog.HeaderRecordValue HeaderToGenerated(HeaderRecordValue value)
                => new CheckpointCatalog.HeaderRecordValue(
                    value.WorldDefinitionHigh, value.WorldDefinitionLow, value.SourceSessionHigh, value.SourceSessionLow,
                    value.ProtocolMajor, value.ProtocolMinor, value.TemporalModel, value.StepDurationTicks,
                    value.TicksPerSecond, value.MaxStepsPerPump, value.UsesUnscaledHostClock, value.LogicalStep,
                    value.TimeDebtTicks, value.DomainSeconds, value.PendingDemand, value.PropagationMode,
                    value.CatalogFingerprintA, value.CatalogFingerprintB, value.CatalogFingerprintC, value.CatalogFingerprintD,
                    value.QueuePolicy, value.AdmissionCutoff, value.RejectedQueuedCount, value.LastEventSequence,
                    value.ScopeCount, value.InstallCount, value.SelectionCount, value.TargetCount,
                    value.SlotCount, value.GrantCount, value.ClockCount, value.CommandCount,
                    value.MessageCount, value.RngStreamCount, value.CursorCount, value.SourcePublishedRevision,
                    value.SourcePublishedEpoch, value.SourceHostTicksPerSecond, value.ContentRevisionCount);

            private static HeaderRecordValue HeaderFromGenerated(CheckpointCatalog.HeaderRecordValue value)
                => new HeaderRecordValue(
                    value.WorldDefinitionHigh, value.WorldDefinitionLow, value.SourceSessionHigh, value.SourceSessionLow,
                    value.ProtocolMajor, value.ProtocolMinor, value.TemporalModel, value.StepDurationTicks,
                    value.TicksPerSecond, value.MaxStepsPerPump, value.UsesUnscaledHostClock, value.LogicalStep,
                    value.TimeDebtTicks, value.DomainSeconds, value.PendingDemand, value.PropagationMode,
                    value.CatalogFingerprintA, value.CatalogFingerprintB, value.CatalogFingerprintC, value.CatalogFingerprintD,
                    value.QueuePolicy, value.AdmissionCutoff, value.RejectedQueuedCount, value.LastEventSequence,
                    value.ScopeCount, value.InstallCount, value.SelectionCount, value.TargetCount,
                    value.SlotCount, value.GrantCount, value.ClockCount, value.CommandCount,
                    value.MessageCount, value.RngStreamCount, value.CursorCount, value.SourcePublishedRevision,
                    value.SourcePublishedEpoch, value.SourceHostTicksPerSecond, value.ContentRevisionCount);

            private static CheckpointCatalog.ScopeRecordValue ScopeToGenerated(ScopeRecordValue value)
                => new CheckpointCatalog.ScopeRecordValue(
                    value.ScopeHigh, value.ScopeLow, value.ParentHigh, value.ParentLow,
                    value.Depth, value.Mode, value.InstallCount, value.GrantCount,
                    value.ServiceIsolationAll, value.CapabilityIsolationAll,
                    value.ServiceIsolationCount, value.CapabilityIsolationCount);

            private static ScopeRecordValue ScopeFromGenerated(CheckpointCatalog.ScopeRecordValue value)
                => new ScopeRecordValue(
                    value.ScopeHigh, value.ScopeLow, value.ParentHigh, value.ParentLow,
                    value.Depth, value.Mode, value.InstallCount, value.GrantCount,
                    value.ServiceIsolationAll, value.CapabilityIsolationAll,
                    value.ServiceIsolationCount, value.CapabilityIsolationCount);

            private static CheckpointCatalog.InstallRecordValue InstallToGenerated(InstallRecordValue value)
                => new CheckpointCatalog.InstallRecordValue(
                    value.InstanceHigh, value.InstanceLow, value.PluginTypeHigh, value.PluginTypeLow,
                    value.ScopeHigh, value.ScopeLow, value.ConfigRevision, value.ConfigHashA,
                    value.ConfigHashB, value.ConfigHashC, value.ConfigHashD, value.Priority,
                    value.Generation, value.ActivationEpoch, value.State, value.ConfigFieldCount,
                    value.ConfigBytes, value.SelectionCount, value.HasConfigDocument);

            private static InstallRecordValue InstallFromGenerated(CheckpointCatalog.InstallRecordValue value)
                => new InstallRecordValue(
                    value.InstanceHigh, value.InstanceLow, value.PluginTypeHigh, value.PluginTypeLow,
                    value.ScopeHigh, value.ScopeLow, value.ConfigRevision, value.ConfigHashA,
                    value.ConfigHashB, value.ConfigHashC, value.ConfigHashD, value.Priority,
                    value.Generation, value.ActivationEpoch, value.State, value.ConfigFieldCount,
                    value.ConfigBytes, value.SelectionCount, value.HasConfigDocument);

            private static CheckpointCatalog.SelectionRecordValue SelectionToGenerated(SelectionRecordValue value)
                => new CheckpointCatalog.SelectionRecordValue(
                    value.InstanceHigh, value.InstanceLow, value.ContractHigh, value.ContractLow,
                    value.ContractVersion, value.ProviderHigh, value.ProviderLow, value.Order);

            private static SelectionRecordValue SelectionFromGenerated(CheckpointCatalog.SelectionRecordValue value)
                => new SelectionRecordValue(
                    value.InstanceHigh, value.InstanceLow, value.ContractHigh, value.ContractLow,
                    value.ContractVersion, value.ProviderHigh, value.ProviderLow, value.Order);

            private static CheckpointCatalog.TargetRecordValue TargetToGenerated(TargetRecordValue value)
                => new CheckpointCatalog.TargetRecordValue(
                    value.TargetHigh, value.TargetLow, value.ScopeHigh, value.ScopeLow,
                    value.DefinitionHigh, value.DefinitionLow, value.SchemaHigh, value.SchemaLow,
                    value.SchemaVersion, value.ContentRevision, value.SourceSlot, value.SourceGeneration);

            private static TargetRecordValue TargetFromGenerated(CheckpointCatalog.TargetRecordValue value)
                => new TargetRecordValue(
                    value.TargetHigh, value.TargetLow, value.ScopeHigh, value.ScopeLow,
                    value.DefinitionHigh, value.DefinitionLow, value.SchemaHigh, value.SchemaLow,
                    value.SchemaVersion, value.ContentRevision, value.SourceSlot, value.SourceGeneration);

            private static CheckpointCatalog.SlotRecordValue SlotToGenerated(SlotRecordValue value)
                => new CheckpointCatalog.SlotRecordValue(
                    value.TargetHigh, value.TargetLow, value.OwnerHigh, value.OwnerLow,
                    value.SlotHigh, value.SlotLow, value.SchemaVersion, value.Value, value.Active);

            private static SlotRecordValue SlotFromGenerated(CheckpointCatalog.SlotRecordValue value)
                => new SlotRecordValue(
                    value.TargetHigh, value.TargetLow, value.OwnerHigh, value.OwnerLow,
                    value.SlotHigh, value.SlotLow, value.SchemaVersion, value.Value, value.Active);

            private static CheckpointCatalog.GrantRecordValue GrantToGenerated(GrantRecordValue value)
                => new CheckpointCatalog.GrantRecordValue(
                    value.Kind, value.ScopeHigh, value.ScopeLow, value.TargetHigh, value.TargetLow,
                    value.CapabilityHigh, value.CapabilityLow, value.CapabilityVersion,
                    value.ProviderHigh, value.ProviderLow, value.RuleHigh, value.RuleLow,
                    value.ContractHigh, value.ContractLow, value.ContractVersion,
                    value.SubjectHigh, value.SubjectLow, value.AppliesToSubtree, value.AllContracts,
                    value.ExclusionKind, value.Order);

            private static GrantRecordValue GrantFromGenerated(CheckpointCatalog.GrantRecordValue value)
                => new GrantRecordValue(
                    value.Kind, value.ScopeHigh, value.ScopeLow, value.TargetHigh, value.TargetLow,
                    value.CapabilityHigh, value.CapabilityLow, value.CapabilityVersion,
                    value.ProviderHigh, value.ProviderLow, value.RuleHigh, value.RuleLow,
                    value.ContractHigh, value.ContractLow, value.ContractVersion,
                    value.SubjectHigh, value.SubjectLow, value.AppliesToSubtree, value.AllContracts,
                    value.ExclusionKind, value.Order);

            private static CheckpointCatalog.ClockRecordValue ClockToGenerated(ClockRecordValue value)
                => new CheckpointCatalog.ClockRecordValue(
                    value.RowKind, value.ClockHigh, value.ClockLow, value.ClockKind,
                    value.PausePolicy, value.Persists, value.WakeHigh, value.WakeLow,
                    value.PayloadSchemaHigh, value.PayloadSchemaLow, value.PayloadSchemaVersion,
                    value.ScheduledAtSequence, value.RemainingSteps, value.RemainingTicks,
                    value.WakeState, value.Order);

            private static ClockRecordValue ClockFromGenerated(CheckpointCatalog.ClockRecordValue value)
                => new ClockRecordValue(
                    value.RowKind, value.ClockHigh, value.ClockLow, value.ClockKind,
                    value.PausePolicy, value.Persists, value.WakeHigh, value.WakeLow,
                    value.PayloadSchemaHigh, value.PayloadSchemaLow, value.PayloadSchemaVersion,
                    value.ScheduledAtSequence, value.RemainingSteps, value.RemainingTicks,
                    value.WakeState, value.Order);

            private static CheckpointCatalog.CommandRecordValue CommandToGenerated(CommandRecordValue value)
                => new CheckpointCatalog.CommandRecordValue(
                    value.IssuerHigh, value.IssuerLow, value.IssuerSequence, value.RouteHigh, value.RouteLow,
                    value.TargetHigh, value.TargetLow, value.SchemaHigh, value.SchemaLow,
                    value.SchemaVersion, value.AdmittedStep, value.AdmittedEpoch,
                    value.AdmissionSequence, value.OrderOrdinal, value.OriginKind,
                    value.InputHashA, value.InputHashB, value.InputHashC, value.InputHashD,
                    value.Payload);

            private static CommandRecordValue CommandFromGenerated(CheckpointCatalog.CommandRecordValue value)
                => new CommandRecordValue(
                    value.IssuerHigh, value.IssuerLow, value.IssuerSequence, value.RouteHigh, value.RouteLow,
                    value.TargetHigh, value.TargetLow, value.SchemaHigh, value.SchemaLow,
                    value.SchemaVersion, value.AdmittedStep, value.AdmittedEpoch,
                    value.AdmissionSequence, value.OrderOrdinal, value.OriginKind,
                    value.InputHashA, value.InputHashB, value.InputHashC, value.InputHashD,
                    value.Payload);

            private static CheckpointCatalog.MessageRecordValue MessageToGenerated(MessageRecordValue value)
                => new CheckpointCatalog.MessageRecordValue(
                    value.Step, value.Epoch, value.RequestIssuerHigh, value.RequestIssuerLow,
                    value.RequestSequence, value.RouteHigh, value.RouteLow, value.OwnerHigh,
                    value.OwnerLow, value.TargetHigh, value.TargetLow, value.PayloadSchemaHigh,
                    value.PayloadSchemaLow, value.PayloadSchemaVersion, value.MessageKind,
                    value.OrderAdmitted, value.OrderOrdinal, value.OriginKeyHigh, value.OriginKeyLow,
                    value.ProducerKeyHigh, value.ProducerKeyLow, value.ProducerKeyVersion,
                    value.BufferHigh, value.BufferLow, value.HasPayload, value.HasRequest,
                    value.IsOutcome, value.Payload);

            private static MessageRecordValue MessageFromGenerated(CheckpointCatalog.MessageRecordValue value)
                => new MessageRecordValue(
                    value.Step, value.Epoch, value.RequestIssuerHigh, value.RequestIssuerLow,
                    value.RequestSequence, value.RouteHigh, value.RouteLow, value.OwnerHigh,
                    value.OwnerLow, value.TargetHigh, value.TargetLow, value.PayloadSchemaHigh,
                    value.PayloadSchemaLow, value.PayloadSchemaVersion, value.MessageKind,
                    value.OrderAdmitted, value.OrderOrdinal, value.OriginKeyHigh, value.OriginKeyLow,
                    value.ProducerKeyHigh, value.ProducerKeyLow, value.ProducerKeyVersion,
                    value.BufferHigh, value.BufferLow, value.HasPayload, value.HasRequest,
                    value.IsOutcome, value.Payload);

            private static CheckpointCatalog.RngRecordValue RngToGenerated(RngRecordValue value)
                => new CheckpointCatalog.RngRecordValue(
                    value.StreamHigh, value.StreamLow, value.State, value.StreamKey, value.DrawCount);

            private static RngRecordValue RngFromGenerated(CheckpointCatalog.RngRecordValue value)
                => new RngRecordValue(
                    value.StreamHigh, value.StreamLow, value.State, value.StreamKey, value.DrawCount);

            private static CheckpointCatalog.CursorRecordValue CursorToGenerated(CursorRecordValue value)
                => new CheckpointCatalog.CursorRecordValue(
                    value.RowKind, value.IssuerHigh, value.IssuerLow, value.Sequence,
                    value.SessionHigh, value.SessionLow);

            private static CursorRecordValue CursorFromGenerated(CheckpointCatalog.CursorRecordValue value)
                => new CursorRecordValue(
                    value.RowKind, value.IssuerHigh, value.IssuerLow, value.Sequence,
                    value.SessionHigh, value.SessionLow);


            private static string DescribeKinds(IReadOnlyList<CheckpointRecordKind> kinds)
            {
                if (kinds.Count == 0)
                {
                    return "no record kind";
                }

                var names = new List<string>(kinds.Count);
                for (int i = 0; i < kinds.Count; i++)
                {
                    names.Add(kinds[i].ToString());
                }

                return string.Join(",", names.ToArray());
            }

            /// <summary>
            /// Reads and decodes one document into every record category, so the two sides of the round trip can be
            /// compared row by row. A document that does not decode reports the code and detail it received.
            /// </summary>
            private bool TryDecode(byte[] document, out Gc018Image? image, out string detail)
            {
                image = null;
                detail = string.Empty;
                if (codecs == null)
                {
                    detail = "the codec set is not built";
                    return false;
                }

                if (!CheckpointDocument.TryRead(
                        document, codecs, out CheckpointDocument? parsed, out DiagnosticCode code, out string readDetail)
                    || parsed == null)
                {
                    detail = "the document was refused: " + code + ": " + readDetail;
                    return false;
                }

                if (!parsed.TryReadRecords(CheckpointRecordKind.Scope, out IReadOnlyList<ScopeRecordValue> scopes, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Install, out IReadOnlyList<InstallRecordValue> installs, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Selection, out IReadOnlyList<SelectionRecordValue> selections, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Target, out IReadOnlyList<TargetRecordValue> targetsRows, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Grant, out IReadOnlyList<GrantRecordValue> grants, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Clock, out IReadOnlyList<ClockRecordValue> clocks, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Command, out IReadOnlyList<CommandRecordValue> commands, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Message, out IReadOnlyList<MessageRecordValue> messages, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Rng, out IReadOnlyList<RngRecordValue> rngRows, out code, out detail)
                    || !parsed.TryReadRecords(CheckpointRecordKind.Cursor, out IReadOnlyList<CursorRecordValue> cursors, out code, out detail))
                {
                    detail = "decoding the document failed: " + code + ": " + detail;
                    return false;
                }

                image = new Gc018Image
                {
                    Header = parsed.Header,
                    Bytes = parsed.RawBytes,
                    Scopes = scopes,
                    Installs = installs,
                    Selections = selections,
                    Targets = targetsRows,
                    Slots = slots,
                    Grants = grants,
                    Clocks = clocks,
                    Commands = commands,
                    Messages = messages,
                    RngStreams = rngRows,
                    Cursors = cursors,
                };
                return true;
            }

            /// <summary>
            /// Writes a structurally valid document by hand — one root scope, one leaf scope, one target whose
            /// recipe declares <paramref name="targetSchema"/>, and one owner state slot — with an optional second
            /// slot that names a target the document never declares. The phantom row carries its own owner and slot
            /// identities so the corrupt-reference observation has exactly one fault instead of duplicate identity
            /// noise, and it is built with the production serializer, so every framing rule (header first,
            /// ascending fields, trailing checksum, matching counts) is the real one (P-054).
            /// </summary>
            private bool TryWriteDocument(
                SchemaRef targetSchema,
                bool phantomSlot,
                out byte[] document,
                out string detail)
            {
                document = Array.Empty<byte>();
                detail = string.Empty;
                if (codecs == null)
                {
                    detail = "the codec set is not built";
                    return false;
                }

                ScopeId root = new ScopeId(StableNameKeyDerivation.Derive("gc018.handmade.root"));
                ScopeId leaf = new ScopeId(StableNameKeyDerivation.Derive("gc018.handmade.leaf"));
                TargetId target = new TargetId(StableNameKeyDerivation.Derive("gc018.handmade.target"));
                TargetId phantom = new TargetId(StableNameKeyDerivation.Derive("gc018.handmade.phantom"));
                OwnerId owner = new OwnerId(StableNameKeyDerivation.Derive("gc018.handmade.owner"));
                SlotId slot = new SlotId(StableNameKeyDerivation.Derive("gc018.handmade.slot"));
                OwnerId phantomOwner = new OwnerId(StableNameKeyDerivation.Derive("gc018.handmade.phantom-owner"));
                SlotId phantomSlotId = new SlotId(StableNameKeyDerivation.Derive("gc018.handmade.phantom-slot"));
                DefinitionId recipe = new DefinitionId(StableNameKeyDerivation.Derive("gc018.handmade.recipe"));

                var serializer = new CheckpointSerializer(codecs);
                if (!serializer.TryAdd(
                        CheckpointRecordKind.Scope,
                        new ScopeRecordValue(
                            root.Value.High, root.Value.Low, 0UL, 0UL, 0U, (uint)PropagationMode.Automatic, 0U, 0U,
                            false, false, 0U, 0U),
                        out DiagnosticCode code,
                        out detail)
                    || !serializer.TryAdd(
                        CheckpointRecordKind.Scope,
                        new ScopeRecordValue(
                            leaf.Value.High, leaf.Value.Low, root.Value.High, root.Value.Low, 1U,
                            (uint)PropagationMode.Automatic, 0U, 0U, false, false, 0U, 0U),
                        out code,
                        out detail)
                    || !serializer.TryAdd(
                        CheckpointRecordKind.Target,
                        new TargetRecordValue(
                            target.Value.High, target.Value.Low,
                            leaf.Value.High, leaf.Value.Low,
                            recipe.Value.High, recipe.Value.Low,
                            targetSchema.Id.Value.High, targetSchema.Id.Value.Low,
                            targetSchema.Version, 1UL, 0U, 1UL),
                        out code,
                        out detail)
                    || !serializer.TryAdd(
                        CheckpointRecordKind.Slot,
                        new SlotRecordValue(
                            target.Value.High, target.Value.Low,
                            owner.Value.High, owner.Value.Low,
                            slot.Value.High, slot.Value.Low,
                            1U, 4242, true),
                        out code,
                        out detail))
                {
                    detail = "writing the hand-made document was refused: " + code + ": " + detail;
                    return false;
                }

                if (phantomSlot
                    && !serializer.TryAdd(
                        CheckpointRecordKind.Slot,
                        new SlotRecordValue(
                            phantom.Value.High, phantom.Value.Low,
                            phantomOwner.Value.High, phantomOwner.Value.Low,
                            phantomSlotId.Value.High, phantomSlotId.Value.Low,
                            1U, 7, true),
                        out code,
                        out detail))
                {
                    detail = "writing the phantom slot was refused: " + code + ": " + detail;
                    return false;
                }

                CanonicalId32.Split(catalogFingerprint, out ulong f0, out ulong f1, out ulong f2, out ulong f3);
                CheckpointCounts counts = serializer.Counts;
                var header = new HeaderRecordValue(
                    sourceRequest.Definition.Value.High,
                    sourceRequest.Definition.Value.Low,
                    sourceWorld.Session.High,
                    sourceWorld.Session.Low,
                    CheckpointFormat.ProtocolMajor,
                    CheckpointFormat.ProtocolMinor,
                    (uint)sourceRequest.TemporalModel,
                    StepDurationTicks,
                    TicksPerSecond,
                    MaxStepsPerPump,
                    false,
                    0UL,
                    0UL,
                    0.0,
                    0UL,
                    (uint)PropagationMode.Automatic,
                    f0,
                    f1,
                    f2,
                    f3,
                    (uint)CheckpointQueuePolicy.RejectQueued,
                    0UL,
                    0U,
                    0UL,
                    (uint)counts.Scopes,
                    (uint)counts.Installs,
                    (uint)counts.Selections,
                    (uint)counts.Targets,
                    (uint)counts.Slots,
                    (uint)counts.Grants,
                    (uint)counts.Clocks,
                    (uint)counts.Commands,
                    (uint)counts.Messages,
                    (uint)counts.RngStreams,
                    (uint)counts.Cursors,
                    1UL,
                    1UL,
                    UnityWorldHost.DefaultHostTicksPerSecond,
                    0U);

                if (!serializer.TrySerialize(header, out document, out code, out detail))
                {
                    document = Array.Empty<byte>();
                    detail = "serializing the hand-made document was refused: " + code + ": " + detail;
                    return false;
                }

                detail = "bytes=" + document.Length.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            /// <summary>
            /// Proves one document is refused by the envelope checks *and* by the restore sequence, and that the
            /// refusal never reaches a builder or creates a world (O-20, O-21).
            /// </summary>
            private bool RefusesRestore(byte[] document, out DiagnosticCode code, out string detail)
            {
                code = DiagnosticCode.None;
                detail = string.Empty;
                if (codecs == null || directMigrations == null)
                {
                    detail = "the codec set or the migration registry is missing";
                    return false;
                }

                if (CheckpointDocument.TryRead(
                        document, codecs, out CheckpointDocument? parsed, out DiagnosticCode readCode, out string readDetail)
                    || parsed != null)
                {
                    code = DiagnosticCode.None;
                    detail = "the document was accepted: " + readDetail;
                    return false;
                }

                RequireBuilder();
                int buildsBefore = builder == null ? 0 : builder.BuildCount;
                int registryBefore = UnityWorldRegistry.Count;
                WorldId session = new WorldId(sessionSequence.Next());
                var executor = new CheckpointRestoreExecutor(new RestoreReservationLedger(8), codecs);
                RestoreOutcome outcome = executor.Restore(
                    document,
                    session,
                    NextOperation(session),
                    builder!,
                    directMigrations,
                    catalogFingerprint,
                    null,
                    true);
                bool refused = !outcome.Restored
                    && outcome.Stage == RestoreStage.Read
                    && outcome.Plan == null
                    && (builder == null || builder.BuildCount == buildsBefore)
                    && !UnityWorldRegistry.TryGet(session, out UnityWorldHost? _)
                    && UnityWorldRegistry.Count == registryBefore;
                code = readCode;
                detail = "envelope=" + readCode + "(" + readDetail + "); restoreStage=" + outcome.Stage
                    + "/" + outcome.Code;
                return refused;
            }

            /// <summary>Creates the one restore builder lazily, so every refusal can prove it was never asked.</summary>
            private void RequireBuilder()
            {
                if (builder != null || descriptor == null)
                {
                    return;
                }

                directMigrations = new CheckpointMigrationRegistry(new List<ISchemaMigrationStep>());
                builder = new Gc018FamilyRestoreBuilder(
                    family,
                    descriptor,
                    catalogFingerprint,
                    UnityWorldRegistry.Count,
                    NextOperation);
            }

            /// <summary>
            /// The schemas this build can allocate at the version it carries: the checkpoint document container,
            /// every recipe the family's catalog registers, and the payload schema the declared wake names. A
            /// captured schema version that is not in this set has no path to the build's version, which is what
            /// P-054 requires a restore to refuse (and what the two refusal steps above prove).
            /// </summary>
            private IReadOnlyList<SchemaRef> AllocatedSchemas()
            {
                var schemas = new List<SchemaRef> { CheckpointFormat.DocumentSchema };
                SpawnRecipeCatalog recipes = family.CreateRecipes();
                for (int i = 0; i < recipes.Recipes.Count; i++)
                {
                    AddDistinct(schemas, recipes.Recipes[i].Recipe.Schema);
                }

                AddDistinct(schemas, family.WakePayloadSchema);
                return schemas;
            }

            private static void AddDistinct(List<SchemaRef> schemas, SchemaRef candidate)
            {
                for (int i = 0; i < schemas.Count; i++)
                {
                    if (schemas[i].Equals(candidate))
                    {
                        return;
                    }
                }

                schemas.Add(candidate);
            }

            /// <summary>Captures the restored world once through the same reader, and caches the decoded image.</summary>
            private bool EnsureRestoredImage(out string detail)
            {
                detail = string.Empty;
                if (restoredImage != null)
                {
                    return true;
                }

                if (builder == null || restoredHost == null || codecs == null)
                {
                    detail = "the restored world is not built";
                    return false;
                }

                if (!builder.TryCreateCaptureContext(out CaptureContext? context, out string contextDetail)
                    || context == null)
                {
                    detail = contextDetail;
                    return false;
                }

                var reader = new UnityCommittedBoundaryReader(restoredHost, context);
                if (!reader.IsAtCommittedBoundary)
                {
                    detail = "the restored world is not at a committed boundary (lifecycle "
                        + restoredHost.Lifecycle + ", pumping " + restoredHost.IsPumping + ")";
                    return false;
                }

                CheckpointCaptureResult result = CheckpointCapture.Capture(
                    reader,
                    new CheckpointCaptureRequest(
                        restoredHost.World, codecs, CheckpointQueuePolicy.RejectQueued, catalogFingerprint));
                if (!result.Captured)
                {
                    detail = "capturing the restored world was refused: " + result.Code + ": " + result.Detail;
                    return false;
                }

                if (!TryDecode(result.Document, out Gc018Image? image, out string decodeDetail) || image == null)
                {
                    detail = decodeDetail;
                    return false;
                }

                restoredImage = image;
                return true;
            }

            private static bool TryFindSlot(
                IReadOnlyList<SlotRecordValue> slots,
                TargetId target,
                OwnerId owner,
                SlotId slot,
                out SlotRecordValue row)
            {
                row = default(SlotRecordValue);
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Key.Target.Equals(target)
                        && slots[i].Key.Owner.Equals(owner)
                        && slots[i].Key.Slot.Equals(slot))
                    {
                        row = slots[i];
                        return true;
                    }
                }

                return false;
            }

            private static bool TryEventCursor(IReadOnlyList<CursorRecordValue> cursors, out ulong sequence)
            {
                sequence = 0UL;
                for (int i = 0; i < cursors.Count; i++)
                {
                    if (cursors[i].Row == CursorRowKind.EventCursor)
                    {
                        sequence = cursors[i].Sequence;
                        return true;
                    }
                }

                return false;
            }

            private static bool TryIssuerWatermark(
                IReadOnlyList<CursorRecordValue> cursors,
                Id128 issuer,
                out ulong watermark)
            {
                watermark = 0UL;
                for (int i = 0; i < cursors.Count; i++)
                {
                    if (cursors[i].Row == CursorRowKind.IssuerHighWater && cursors[i].IssuerId.Equals(issuer))
                    {
                        watermark = cursors[i].Sequence;
                        return true;
                    }
                }

                return false;
            }

            private static bool CursorsBelongTo(IReadOnlyList<CursorRecordValue> cursors, Id128 session)
            {
                if (cursors.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < cursors.Count; i++)
                {
                    if (!cursors[i].SourceSession.Session.Equals(session))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool RngStreamsAreDrawn(IReadOnlyList<RngRecordValue> streams)
            {
                if (streams.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < streams.Count; i++)
                {
                    if (streams[i].DrawCount != (ulong)RngDrawsPerStream || streams[i].State == 0UL)
                    {
                        return false;
                    }
                }

                return true;
            }

            private static List<string> DrawCounts(IReadOnlyList<RngRecordValue> streams)
            {
                var counts = new List<string>(streams.Count);
                for (int i = 0; i < streams.Count; i++)
                {
                    counts.Add(streams[i].DrawCount.ToString(CultureInfo.InvariantCulture));
                }

                return counts;
            }

            private static bool ClockRowsAreNonTrivial(IReadOnlyList<ClockRecordValue> clocks)
            {
                int declarations = 0;
                int wakes = 0;
                for (int i = 0; i < clocks.Count; i++)
                {
                    if (clocks[i].IsDeclaration)
                    {
                        declarations++;
                    }
                    else
                    {
                        wakes++;
                        if (clocks[i].RemainingSteps != WakeDelaySteps || clocks[i].State != ClockWakeState.Pending)
                        {
                            return false;
                        }
                    }
                }

                return declarations == 1 && wakes == 1;
            }

            private static bool ScopeCountsAgree(Gc018Image captured, Gc018Image restored)
                => captured.Scopes.Count == restored.Scopes.Count;

            private bool PublishEdits(IReadOnlyList<CompositionEditPayload> payloads)
            {
                for (int i = 0; i < payloads.Count; i++)
                {
                    if (!PublishEdit(payloads[i], "setup-edit-" + i.ToString(CultureInfo.InvariantCulture)))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has
            /// one publication series, so an edit the world does not answer leaves the lane one publication ahead
            /// and every later adoption is refused as stale: the two halves are always done together.
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string label)
            {
                if (!ApplyEdit(payload, label))
                {
                    return false;
                }

                return PublishPendingAssembly();
            }

            /// <summary>Applies one edit and drains the lane's publication without answering it with an assembly.</summary>
            private bool ApplyEdit(CompositionEditPayload payload, string label)
            {
                if (lane == null || pipeline == null || host == null || publisher == null)
                {
                    lastFailure = label + ": the world or its pipeline is missing";
                    return false;
                }

                EditAdmission admission = lane.SubmitEdit(
                    payload, NextOperation(host.World), lane.Committed.Revision);
                if (!admission.Staged)
                {
                    lastFailure = label + ": the edit was refused by the lane (" + admission.Kind + "/"
                        + admission.Code + ")";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    lastFailure = label + ": the publication was refused ("
                        + (published.Count > 0
                            ? published[0].Outcome.ToString() + "/" + published[0].Code
                            : "none")
                        + ")";
                    return false;
                }

                return true;
            }

            /// <summary>
            /// Publishes the world's assembly for the lane's committed publication: the derived assembly when the
            /// derivation changed target bindings, and the unchanged assembly when it did not (P-006, P-030).
            /// </summary>
            private bool PublishPendingAssembly()
            {
                if (pipeline == null || publisher == null || lane == null || host == null)
                {
                    lastFailure = "the world or its pipeline is missing";
                    return false;
                }

                DerivedAssemblyReport report = pipeline.PublishDerived(NextOperation(host.World));
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = "the world refused the assembly: " + report.Describe();
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        NextOperation(host.World), lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        lastFailure = "the unchanged assembly was refused: " + unchanged.Detail;
                        return false;
                    }
                }

                return MatchesPublishedAssembly();
            }

            private IReadOnlyList<DerivationTarget> TargetView()
            {
                if (targets == null)
                {
                    return Array.Empty<DerivationTarget>();
                }

                DerivationInputTargets view = targets.BuildDerivationTargets();
                return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
            }

            private bool MatchesPublishedAssembly()
            {
                if (lane == null || publisher == null || host == null)
                {
                    return false;
                }

                return AssemblyPublisher.MatchesPublishedAssembly(
                    lane.Committed.Revision,
                    lane.Committed.Epoch,
                    publisher.PublishedRevision,
                    host.CurrentEpoch);
            }

            private ulong PumpIdleFrames()
            {
                if (time == null)
                {
                    return 0UL;
                }

                ulong committed = 0UL;
                for (int i = 0; i < IdlePumpFrames; i++)
                {
                    committed += time.PumpFrame(IdlePumpTicks).StepsCommitted;
                }

                return committed;
            }

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            /// <summary>
            /// Records one observation. The family qualification is applied here, at the single recording point, so
            /// every step method passes the bare name from <see cref="ObservationNames"/> and the recorded sequence
            /// is exactly the qualified list: a step that qualified its own name (or forgot to) would change the
            /// digest, which is what the EditMode suite and the player probe assert on.
            /// </summary>
            private void Add(string bareName, bool passed, string detail)
            {
                lastFailure = string.Empty;
                steps.Add(new Gc018Step(family.Label + "/" + bareName, passed, detail ?? string.Empty));
            }

            private string DescribeFailure()
                => lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
