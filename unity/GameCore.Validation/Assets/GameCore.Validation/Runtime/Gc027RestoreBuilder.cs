// GameCore.Validation.ProbeHost — the GC-027 recovery rebuilder.
//
// The gate sentence this file serves, from `docs/game-core/09-implementation-guide.md` (GC-027):
//
//   "Compose `RecoverWorld` (O-22) over the existing capture/restore path, with P-049 host-configured bounded
//    retries. Inject faults at capture copy, file publication, restore reference repair, postwrite apply, outbox
//    append, delivery, ack and restart. For each: the permitted observable result, the failed old world never
//    resumes, no hidden external replay. Restore cards, narrative and traversal-compatible checkpoint data into a
//    NEW world with different native handles; verify active/dormant state, pending-command disposition and delivery
//    cursor intact; replayed external delivery does not duplicate the test destination effect; incompatible content
//    leaves the new world unexposed."
//
// This type is `Gc018FamilyRestoreBuilder` adapted to a recovery, because a recovery is a restore and there is one
// way to rebuild a world from a verified plan: the unexposed world, the target registry, the control lane, the
// derived-assembly pipeline, the clock and random-stream rebuild, the target and slot seeding, the recipe base
// layouts, the composition replay, the command re-admission and the family runtime attach are GC-018's own bodies
// (P-002, P-030, P-053). What a *recovery* adds is the delivery state, which is what makes "the delivery cursor
// came across intact" and "the replayed external delivery does not duplicate the destination effect" checks rather
// than claims (P-045):
//
//   * the recovered session owns the captured obligations, so the build creates its `WorldDeliveryOwner` and
//     registers the run's recording destination port on it; a destination that cannot be registered is a coded
//     refusal, because a recovered world that silently lost its destination is not the world the document described
//     (P-034);
//   * the plan's outbox section is reinstated through that owner and then proved against the live outbox before the
//     executor exposes the world (P-045, P-053);
//   * nothing here delivers: reinstatement is state and never a replay, so the destination's attempt count stays
//     zero until the recovered world itself dispatches (P-049);
//   * the plan's fault latch (`restore-apply` or `recovery-publication`) is armed on the staging world once its
//     state is applied, so the executor's own reach fires on a world that really was written to (TEST-016 row 5,
//     P-031).
//
// A refusal is always a coded value with a detail the caller can act on: nothing here returns a silent false.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Delivery;
using GameCore.Execution.Persistence;
using GameCore.Execution.Recovery;
using GameCore.Execution.Messages;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Delivery;
using GameCore.Unity.Runtime.Faults;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The real O-21 rebuilder of one GC-027 recovery: it creates the reserved session's world **unexposed**,
    /// reconstructs the captured composition, clocks, random streams, live targets, owner slots, recipe base layouts
    /// and commands exactly as <see cref="Gc018FamilyRestoreBuilder"/> does, rebuilds the recovered session's delivery
    /// state (its owner and the run's recording destination port), arms the plan's fault latch on the staging world
    /// once its state is applied, and exposes nothing itself: the executor owns the reach and the exposure (P-030,
    /// P-049).
    ///
    /// A refusal is always a coded value with a detail the caller can act on, and the counting properties are what
    /// the scenario observes: a refused restore that never staged a world reports a <see cref="BuildCount"/> of zero
    /// and a staging host of null (P-052).
    /// </summary>
    public sealed class Gc027RestoreBuilder : IRestoreTargetBuilder, IRestoreOutboxBuilder
    {
        private const ulong StagedByteCeiling = 1024UL * 1024UL;
        private const ulong ScratchCapacityBytes = 4096UL;
        private const ulong ScratchBytesPerSlot = 64UL;
        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        /// <summary>Boundary name the executor reaches after the plan's state has been applied (TEST-016 row 5).</summary>
        private const string RestoreApplyBoundaryName = "restore-apply";

        /// <summary>Boundary name the executor reaches when the validated world is about to be published (P-030).</summary>
        private const string RecoveryPublicationBoundaryName = "recovery-publication";

        private readonly IGc027Family family;
        private readonly PipelineDescriptorReport descriptor;
        private readonly ContentHash catalogFingerprint;
        private readonly Func<WorldId, OperationId> nextOperation;

        /// <summary>The declared boundary name, empty when this build arms nothing.</summary>
        private readonly string armBoundaryName;

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
        private WorldDeliveryOwner? delivery;
        private Gc027RecordingDestination? destination;

        public Gc027RestoreBuilder(
            IGc027Family family,
            PipelineDescriptorReport descriptor,
            ContentHash catalogFingerprint,
            Func<WorldId, OperationId> nextOperation,
            string? armBoundaryName)
        {
            this.family = family ?? throw new ArgumentNullException(nameof(family));
            this.descriptor = descriptor ?? throw new ArgumentNullException(nameof(descriptor));
            this.catalogFingerprint = catalogFingerprint;
            this.nextOperation = nextOperation ?? throw new ArgumentNullException(nameof(nextOperation));
            this.armBoundaryName = armBoundaryName ?? string.Empty;
            ArmedBoundaryName = this.armBoundaryName;
        }

        /// <summary>Attempts this builder was asked to perform; zero proves a refused recovery never staged one.</summary>
        public int BuildCount { get; private set; }

        /// <summary>The unexposed staging world of the last successful build, or null when none was created.</summary>
        public UnityWorldHost? Staging => staging;

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

        /// <summary>Captured mode edits replayed, zero when the declared mode is already the captured one (P-013).</summary>
        public int ReplayedModeCount { get; private set; }

        /// <summary>Captured persistent clocks re-registered (P-038, P-053).</summary>
        public int RebuiltClockCount { get; private set; }

        /// <summary>Captured external commands re-admitted on the recovered session (P-037, P-053).</summary>
        public int ReadmittedCommandCount { get; private set; }

        /// <summary>The declared boundary name this builder was constructed with, or empty when it arms nothing.</summary>
        public string ArmedBoundaryName { get; }

        /// <summary>
        /// True when the declared boundary was observed armed on the staging world right after the state was applied,
        /// so the executor's own reach really fires on this world and not on a latch the run merely intended (P-031).
        /// </summary>
        public bool ArmedBoundaryWasSetOnStaging { get; private set; }

        /// <summary>The recovered session's delivery owner: the outbox, its journal, its cursor and its ports (GC-021).</summary>
        public WorldDeliveryOwner? Delivery { get; private set; }

        /// <summary>The recording destination port the recovered session delivers to; the "test destination effect".</summary>
        public Gc027RecordingDestination? Destination { get; private set; }

        /// <summary>The recovered session's live outbox, or null when no delivery owner was built.</summary>
        public DurableOutbox? Outbox => Delivery == null ? null : Delivery.Outbox;

        /// <summary>
        /// Native ECS index of every restored live target, resolved after the state was applied, so the scenario can
        /// compare the recovered world's native block with the source world's: the identities round trip and the
        /// handles are new (P-005).
        /// </summary>
        public IReadOnlyList<int> RestoredTargetIndices { get; private set; } = Array.Empty<int>();

        /// <summary>Outbox reinstatements that were made against the recovered session's delivery owner (P-053).</summary>
        public int ReinstateAttemptCount { get; private set; }

        /// <summary>Outbox rows the recovered session's live outbox was proved to carry (P-045, P-053).</summary>
        public int ProvedRowCount { get; private set; }

        /// <summary>
        /// Registry size observed right after the unexposed world was created, so the scenario can show a recovery
        /// adds one session and nothing else (P-049).
        /// </summary>
        private int RegistryCountAfterStaging { get; set; } = -1;

        /// <summary>Owner-state rows removed because the plan names no such slot (P-032, P-053).</summary>
        private int PrunedSlotCount { get; set; }

        /// <summary>Captured scope boundaries replayed as real edits (P-016).</summary>
        private int ReplayedBoundaryCount { get; set; }

        /// <summary>Captured provider installations replayed as real mount edits before imports can name them (P-013).</summary>
        private int ReplayedInstallCount { get; set; }

        /// <summary>Captured Conservative-mode scope imports replayed as real grant edits (P-013).</summary>
        private int ReplayedImportCount { get; set; }

        /// <summary>Captured pending wakes re-scheduled with their remaining delay (P-038, P-053).</summary>
        private int RebuiltWakeCount { get; set; }

        /// <summary>Assembly publications the rebuild performed, the last one being the rederivation (06 s7).</summary>
        private int PublishedAssemblyCount { get; set; }

        /// <summary>The restored world's family runtime, attached by the build and released by
        /// <see cref="DetachRuntime"/> at teardown (P-042, P-048).</summary>
        public Gc018RuntimeWorld? Runtime => runtime;

        /// <summary>
        /// Releases the family runtime module the build attached to the recovered world. Called by the run's teardown
        /// whether the world passed or faulted, because the module outlives no world (P-048). Safe to call twice.
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

        /// <summary>
        /// Builds the reserved session's world unexposed and applies the plan to it: every GC-018 restore step, plus
        /// the recovered session's delivery state and the declared fault latch. A false result destroys nothing
        /// itself; the executor discards the staging world it was handed and never exposes one (O-21).
        /// </summary>
        public bool TryBuild(
            WorldId session,
            RestorePlan plan,
            int migrationCount,
            out UnityWorldHost? staging,
            out DiagnosticCode code,
            out string detail)
        {
            BuildCount++;
            staging = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            // This attempt's state, not a previous attempt's: one instance serves every attempt of one recovery, so a
            // build that failed after it created a world must not leave that discarded world (or its delivery state)
            // standing for the next attempt the executor makes (P-049, P-050).
            this.staging = null;
            delivery = null;
            destination = null;

            if (plan == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the recovery builder was called without a plan.";
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

            // The declared latch is resolved before any world exists, so an unknown name costs no staging world and
            // cannot be mistaken for a latch that was meant to fire (TEST-016 row 5).
            bool armBoundary = armBoundaryName.Length != 0;
            FaultBoundary boundaryToArm = default(FaultBoundary);
            if (armBoundary && !TryResolveBoundaryName(armBoundaryName, out boundaryToArm, out detail))
            {
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            // O-21's "C/unexposed new world→B": the reserved session's world exists and is fully applied, and no
            // caller can route to it until the executor exposes it (P-049).
            WorldCreateRequest request = family.CreateRequest(session, nextOperation(session));
            WorldCreateResult created = UnityWorldHost.TryCreateUnexposed(
                request, family.CreateRegistration(descriptor.Adaptation!), out UnityWorldHost? createdHost);
            this.staging = createdHost;
            if (!created.Created || createdHost == null)
            {
                code = created.Code;
                detail = "creating the unexposed world for session " + session.Session.ToString()
                    + " under catalog " + catalogFingerprint.ToHex() + " failed: "
                    + created.Code + ": " + created.Detail;
                return false;
            }

            UnityWorldHost host = createdHost;
            RegistryCountAfterStaging = UnityWorldRegistry.Count;

            registry = new TargetRegistry(session, 16);
            publisher = new AssemblyPublisher(
                host, registry, family.CreateRecipes(), family.CreateMigrations(), descriptor.Descriptor!);
            targets = new LiveTargetIndex(publisher.Recipes);
            seeder = new LiveTargetSeeder(host, registry, targets);

            IDerivationValueSource values = family.CreateValues();
            lane = CompositionHost.CreateDefault(
                session,
                family.WorldRootScope,
                new CatalogManifestSource(family.Catalog, family.Declarations),
                null,
                family.LaneSeed,
                new DerivationModeSwitchValidator(values, TargetView));
            bridge = new WorldCompositionBridge(host, lane, publisher);
            pipeline = new DerivedAssemblyPipeline(
                host,
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
            time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);

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

            // The recovered session's delivery state: the ownership the checkpoint's outbox rows are reinstated into
            // and the port the recovered world will deliver to. A destination this build cannot register is a coded
            // refusal rather than a world that silently cannot deliver (P-034, P-045).
            if (!BuildDelivery(session, out code, out detail))
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
                CommandAdmissionReceipt receipt = host.Submit(envelope);
                if (!receipt.Admitted)
                {
                    code = receipt.Result.Reason;
                    detail = "re-admitting captured command " + command.RequestIdIn(session).ToString()
                        + " on the recovered session was refused: " + receipt.Result.Kind + "/"
                        + receipt.Result.Reason + ".";
                    return false;
                }

                ReadmittedCommandCount++;
            }

            if (!CaptureRestoredTargetIndices(out code, out detail))
            {
                return false;
            }

            // The recovered world is exposed as a running world, and its first pump executes the re-admitted
            // command: the family's own runtime module — the one its generated step systems resolve their world
            // through — must be attached with every live target mapped, or the input stage would no-op and leave
            // the ingress lane unconsumed at commit (P-042, P-043).
            var runtimeWorld = new Gc018RuntimeWorld(
                host, targets!, seeder!, descriptor.Compilation!.Schedule!);
            if (!family.TryAttachRuntime(runtimeWorld, out detail))
            {
                code = DiagnosticCode.MissingDependency;
                return false;
            }

            runtime = runtimeWorld;

            // The state is applied and the world still unexposed, so this is the moment the plan's latch is armed:
            // the executor's reach then fires on this staging world (TEST-016 row 5, P-031).
            if (armBoundary)
            {
                host.Faults.Arm(boundaryToArm);
                ArmedBoundaryWasSetOnStaging = host.Faults.IsArmed(boundaryToArm);
            }

            staging = host;
            return true;
        }

        /// <summary>
        /// Reinstates the plan's outbox rows into the recovered session's delivery owner, before the world is
        /// exposed: a recovered session is never observable with a dropped committed obligation (P-045, P-049,
        /// P-053). Nothing is delivered here — reinstatement is state, and dispatching is the recovered world's own
        /// work — so the destination's attempt count stays at zero until that world runs.
        /// </summary>
        public bool TryReinstateOutbox(
            WorldId session,
            RestorePlan plan,
            out int reinstatedRows,
            out DiagnosticCode code,
            out string detail)
        {
            reinstatedRows = 0;
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (plan == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the outbox of session " + session.Session.ToString()
                    + " cannot be reinstated because no plan was supplied.";
                return false;
            }

            WorldDeliveryOwner? owner = Delivery;
            if (owner == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the recovered session " + session.Session.ToString()
                    + " has no delivery owner, so the plan's "
                    + plan.Outbox.Count.ToString(CultureInfo.InvariantCulture)
                    + " outbox row(s) have nowhere to be reinstated (P-045, P-053).";
                return false;
            }

            if (!owner.TryReinstate(plan.Outbox, out code, out detail))
            {
                return false;
            }

            reinstatedRows = plan.Outbox.Count;
            ReinstateAttemptCount++;
            return true;
        }

        /// <summary>
        /// Proves that the recovered session's live outbox carries exactly the plan's rows and the same delivery
        /// cursors, before the executor exposes the world: "the delivery cursor came across intact" is a census of
        /// two real outboxes, and a disagreement refuses the restore instead of publishing a world that owes the
        /// wrong obligations (P-045, P-053).
        /// </summary>
        public bool TryProveOutbox(
            WorldId session,
            IReadOnlyList<OutboxRecordValue> expected,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (expected == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the outbox proof for session " + session.Session.ToString()
                    + " was called without the plan's rows.";
                return false;
            }

            WorldDeliveryOwner? owner = Delivery;
            if (owner == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the recovered session " + session.Session.ToString()
                    + " has no delivery owner, so its outbox cannot be proved against the plan's "
                    + expected.Count.ToString(CultureInfo.InvariantCulture) + " row(s) (P-045, P-053).";
                return false;
            }

            OutboxConsistencyReport report = OutboxConsistency.Verify(
                expected, owner.Outbox, "staging:" + session.Session.ToString());
            detail = report.Describe().Replace('\n', ' ');
            if (!report.Consistent)
            {
                code = DiagnosticCode.MissingDependency;
                return false;
            }

            ProvedRowCount = expected.Count;
            return true;
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

        /// <summary>
        /// The recovered session's delivery state: its owner over the family's declared outbox parameters and a
        /// memory journal of its own (so two sessions never share a journal), plus the run's recording destination
        /// port. Registering the port is part of the build: a refusal here refuses the recovery, because a world
        /// whose obligation could never reach its destination is not the state the document described (P-034).
        /// </summary>
        private bool BuildDelivery(WorldId session, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (staging == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the recovered world is not built, so it has no delivery state.";
                return false;
            }

            var owner = new WorldDeliveryOwner(
                staging,
                family.Issuer,
                family.OutboxCapacity,
                family.OutboxTerminalRetention,
                family.OutboxDurabilityClass,
                new MemoryDeliveryJournal("memory://gc027/" + family.Label + "/" + session.Session.ToString()),
                null);
            var port = new Gc027RecordingDestination(
                family.DeliveryDestinationId, family.DeliveryCommandSchema);
            if (!owner.TryRegisterDestination(port, out string registerDetail))
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "registering the recording destination " + family.DeliveryDestinationId.ToString()
                    + " on the recovered session " + session.Session.ToString()
                    + " was refused (P-034): " + registerDetail;
                return false;
            }

            delivery = owner;
            destination = port;
            return true;
        }

        /// <summary>
        /// Resolves the declared boundary name to the boundary the executor reaches. Only the two recovery
        /// boundaries of a GC-027 run are accepted: any other name is a plan this build does not implement, and it is
        /// refused rather than mapped to the nearest latch (TEST-016 row 5).
        /// </summary>
        private static bool TryResolveBoundaryName(string name, out FaultBoundary boundary, out string detail)
        {
            if (string.Equals(name, RestoreApplyBoundaryName, StringComparison.Ordinal))
            {
                boundary = FaultBoundary.RestoreApply;
                detail = string.Empty;
                return true;
            }

            if (string.Equals(name, RecoveryPublicationBoundaryName, StringComparison.Ordinal))
            {
                boundary = FaultBoundary.RecoveryPublication;
                detail = string.Empty;
                return true;
            }

            boundary = default(FaultBoundary);
            detail = "the builder was asked to arm boundary \"" + name
                + "\", which names no recovery boundary of this run; it accepts \""
                + RestoreApplyBoundaryName + "\" (" + FaultBoundaryText.Of(FaultBoundary.RestoreApply)
                + ") and \"" + RecoveryPublicationBoundaryName + "\" ("
                + FaultBoundaryText.Of(FaultBoundary.RecoveryPublication) + ") only.";
            return false;
        }

        /// <summary>
        /// Resolves every restored live target to its native entity and records the index, so the recovered world's
        /// native block is evidence the scenario can compare with the source world's (P-005). A live target the
        /// registry cannot resolve is refused: the build would otherwise report handles it cannot prove.
        /// </summary>
        private bool CaptureRestoredTargetIndices(out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (registry == null || targets == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the recovered world is not built, so its targets have no native indices.";
                return false;
            }

            IReadOnlyList<LiveTarget> live = targets.Targets;
            var indices = new List<int>(live.Count);
            for (int i = 0; i < live.Count; i++)
            {
                if (!registry.TryResolveTarget(live[i].Target, out TargetHandle _, out Entity entity))
                {
                    code = DiagnosticCode.StaleHandle;
                    detail = "restored target " + live[i].Target.ToString()
                        + " is live in the recovered world and the registry cannot resolve it to a native entity"
                        + " (P-005).";
                    return false;
                }

                indices.Add(entity.Index);
            }

            RestoredTargetIndices = indices;
            return true;
        }

        /// <summary>
        /// Re-registers the captured persistent clocks and re-schedules their pending wakes with the remaining
        /// delay the document recorded, so a recovered world resumes a deferred timer instead of restarting it
        /// (P-038, P-053). Copied from GC-018's restore builder; only the restored clock's plugin-type label differs.
        /// </summary>
        private bool RebuildClocks(RestorePlan plan, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (time == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the recovered world has no time driver.";
                return false;
            }

            for (int i = 0; i < plan.Clocks.Count; i++)
            {
                ClockRecordValue row = plan.Clocks[i];
                if (row.IsDeclaration)
                {
                    var spec = new PluginClockSpec(
                        row.ClockId,
                        "gc027-restored-clock",
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
                detail = "the recovered world has no target seeder.";
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
                // the family's own registered applier, so the recovered world's targets are complete rather than bare
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

            // A capture saves exactly the rows the source world held, so the recovered world must carry exactly the
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
        /// Removes every owner-state slot row the plan does not name, so the recovered world's authoritative state
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
                detail = "the recovered world is not built, so its state cannot be pruned.";
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
                detail = "the recovered world has no control lane.";
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
                    detail = "scope " + row.Scope.ToString() + " is absent from the recovered composition.";
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
                    detail = "scope " + row.Scope.ToString() + " is absent from the recovered composition.";
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
                detail = "the recovered world's control lane or pipeline is missing.";
                return false;
            }

            EditAdmission admission = lane.SubmitEdit(payload, nextOperation(staging.World), lane.Committed.Revision);
            if (!admission.Staged)
            {
                code = admission.Code;
                detail = "the composition edit was refused by the recovered lane (" + admission.Kind + "/"
                    + admission.Code + ").";
                return false;
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                code = published.Count == 0 ? DiagnosticCode.ApplyFault : published[0].Code;
                detail = "the recovered lane answered the edit with "
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
                detail = "the recovered world refused the assembly for the edit: " + report.Describe();
                return false;
            }

            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                    nextOperation(staging.World), lane.Committed.Revision, lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    code = unchanged.Code;
                    detail = "the unchanged recovered assembly was refused: " + unchanged.Detail;
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
}
