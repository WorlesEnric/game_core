// GameCore.Validation.ProbeHost — the live ECS world half of the GC-026 benchmark.
//
// Two things the pure fixture cannot measure, and this file exists for exactly those:
//
//   1. **The fenced apply pause of a real plan.** 08's row "Valid plan affecting 100 existing targets: apply pause p95
//      at most 2 ms" is a statement about the interval during which the world is not simulating, and only a real world
//      has one. The publisher measures it itself (`TelemetryCounter.ApplyDurationMicroseconds`, through the clock the
//      caller supplies), so the number in the report is the production measurement rather than a runner's estimate.
//   2. **The memory categories of a real world's owners.** Leases, retained events, caches, quarantine and native
//      containers belong to owners that exist only with a world; 08 says an aggregate process number cannot attribute
//      them, so they are read from the owners through GC-023's schema.
//
// The world is a real narrative-family world built through the production path — the committed generated catalog, the
// real composition lane, the real publisher and the real derived-assembly pipeline — so nothing here is a model of the
// kernel. What this file adds is a generated scope tree and a generated target population:
//
//   * `liveScopes` scopes (including the world root) form a ten-ary tree declared in the lane seed, so the lane opens
//     its composition with the world definition's own tree rather than building it through scope edits (P-006, P-010);
//   * the first `applyTargets` targets are villagers, which the narrative chapter rule selects; every other target is
//     a decorative prop, which no rule selects. Mounting the chapter provider therefore publishes a valid plan
//     affecting EXACTLY `applyTargets` existing targets — the doc's hundred-target row — while the world itself is at
//     the recorded live scale. The ineligible targets are the "unrecognized targets remain unchanged" case P-015 asks
//     for, and they are what makes the apply count exact rather than approximate.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Benchmarks;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using GameCore.Execution.Time;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Narrative;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;
using GameCore.Validation.Generated;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Why one live world could not be built; never empty on a refusal.</summary>
    public sealed class LiveWorldFailure
    {
        public LiveWorldFailure(string stage, DiagnosticCode code, string detail)
        {
            Stage = stage ?? string.Empty;
            Code = code;
            Detail = detail ?? string.Empty;
        }

        public string Stage { get; }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public override string ToString() => Stage + ": " + Code + ": " + Detail;
    }

    /// <summary>
    /// One live world of the benchmark: a real narrative world with a generated scope tree and a generated target
    /// population, plus the operations the benchmark's live workloads perform on it.
    /// </summary>
    public sealed class BenchmarkLiveWorld : IDisposable
    {
        /// <summary>Fan-out of the generated scope tree; the same ten-ary shape the pure fixture uses.</summary>
        public const int ScopeFanOut = 10;

        /// <summary>Ceiling of one plan's staged leases, in bytes (P-022); generous because the fixture is large.</summary>
        private const ulong StagedByteCeiling = 64UL * 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 1024UL * 1024UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        /// <summary>Host ticks per idle pump: a command-driven world ignores them, which is the point.</summary>
        private const ulong IdlePumpTicks = 1000000UL;

        private readonly List<TargetId> villagers = new List<TargetId>();
        private readonly List<TargetId> ineligible = new List<TargetId>();
        private readonly List<ScopeId> scopes = new List<ScopeId>();

        private ulong operationSequence;
        private BenchmarkLiveWorld()
        {
        }

        /// <summary>The real owned world; every lifecycle assertion is read from it.</summary>
        public UnityWorldHost Host { get; private set; } = null!;

        /// <summary>The serialized control lane of that world.</summary>
        public CompositionHost Lane { get; private set; } = null!;

        /// <summary>The assembly publisher: the owner of the fenced apply measurement (P-030).</summary>
        public AssemblyPublisher Publisher { get; private set; } = null!;

        public LiveTargetIndex Targets { get; private set; } = null!;

        public LiveTargetSeeder Seeder { get; private set; } = null!;

        public DerivedAssemblyPipeline Pipeline { get; private set; } = null!;

        public WorldTimeDriver Time { get; private set; } = null!;

        public DerivationModeSwitchValidator Validator { get; private set; } = null!;

        /// <summary>The last refusal text of this world; empty when nothing was refused.</summary>
        public string LastFailure { get; private set; } = string.Empty;

        /// <summary>The world root scope; the lane opens with this as its only parentless scope (P-010).</summary>
        public ScopeId RootScope { get; private set; }

        /// <summary>Scopes created, including the world root; the live scale of the fixture.</summary>
        public int ScopeCount => scopes.Count + 1;

        /// <summary>Eligible targets seeded: the villagers the chapter rule selects.</summary>
        public int VillagerCount => villagers.Count;

        /// <summary>Targets no rule selects; the ignored population a hidden propagation scan would still visit.</summary>
        public int IneligibleCount => ineligible.Count;

        public int TargetCount => villagers.Count + ineligible.Count;

        /// <summary>The most recent accepted derivation of this world.</summary>
        public DerivationResult? LastDerivation => Pipeline.PreviousDerivation;

        /// <summary>The most recent invalidation of this world (P-023).</summary>
        public InvalidationClosureResult? LastInvalidation => Pipeline.PreviousInvalidation;

        /// <summary>Host ticks of one fixed step: 1/60 s expressed in the tick domain the world is given.</summary>
        private const ulong FixedStepDurationTicks = 166_667UL;

        /// <summary>Tick domain of the fixed-step world; the accumulator converts host ticks with it (P-036).</summary>
        private const ulong FixedStepTicksPerSecond = 10_000_000UL;

        /// <summary>Catch-up limit per pump; the accumulator retains the remaining time as debt.</summary>
        private const uint FixedStepMaxStepsPerPump = 1000U;

        /// <summary>
        /// Builds one live world: the real catalog and declarations, a generated scope tree open in the lane seed, the
        /// generated target population seeded as real ECS storage, and the pipeline ready to publish.
        /// </summary>
        public static bool TryCreate(
            int scopeCount,
            int targetCount,
            int applyTargets,
            uint seed,
            bool fixedStep,
            out BenchmarkLiveWorld? world,
            out LiveWorldFailure? failure)
        {
            world = null;
            failure = null;

            if (scopeCount < 2)
            {
                failure = new LiveWorldFailure(
                    "config", DiagnosticCode.MissingDependency, "a live world needs a root and at least one child scope.");
                return false;
            }

            if (targetCount < 1 || applyTargets < 1 || applyTargets > targetCount)
            {
                failure = new LiveWorldFailure(
                    "config",
                    DiagnosticCode.MissingDependency,
                    "the live world needs targetCount >= applyTargets >= 1, got targetCount="
                    + targetCount.ToString(CultureInfo.InvariantCulture)
                    + " applyTargets=" + applyTargets.ToString(CultureInfo.InvariantCulture) + ".");
                return false;
            }

            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash observedFingerprint);
            if (build.Catalog == null
                || !string.Equals(observedFingerprint.ToHex(), ProbeCatalog.CatalogFingerprint, StringComparison.Ordinal))
            {
                failure = new LiveWorldFailure(
                    "catalog",
                    DiagnosticCode.UnsupportedVersion,
                    "the committed generated catalog was not the one this run derived over: " + build.Describe());
                return false;
            }

            IReadOnlyList<CatalogPluginDeclaration> declarations =
                Gc013NarrativeHost.Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema);

            var family = new BenchmarkLiveFamily(build.Catalog, declarations, scopeCount);
            PipelineDescriptorReport descriptor = family.CompilePipeline();
            if (!descriptor.Succeeded || descriptor.Descriptor == null || descriptor.Adaptation == null)
            {
                failure = new LiveWorldFailure(
                    "pipeline",
                    DiagnosticCode.AmbiguousOrder,
                    "the ownership/schedule pipeline refused: " + descriptor.Describe());
                return false;
            }

            var created = new BenchmarkLiveWorld();
            WorldId session = new WorldId(new Id128(0x42454E43484C4956UL, 0x0000000000000001UL + seed));

            try
            {
                // A fixed-step world commits steps from elapsed host time, which is what the unchanged-composition
                // window needs; a command-driven world commits one step per admitted command or wake (P-036). Both
                // models are created here so the two live workloads each run on the model their claim is about.
                WorldCreateRequest request = fixedStep
                    ? new WorldCreateRequest(
                        session,
                        NarrativeKeys.WorldDefinition,
                        TemporalModel.FixedStep,
                        PropagationMode.Automatic,
                        ContentHash.Empty,
                        created.NextOperation(session),
                        new FixedStepSettings(
                            FixedStepDurationTicks,
                            FixedStepTicksPerSecond,
                            FixedStepMaxStepsPerPump,
                            true))
                    : NarrativeRegistration.CommandDrivenRequest(
                        session, created.NextOperation(session), ContentHash.Empty);

                bool opened = UnityWorldRegistry.TryCreate(
                    request,
                    NarrativeRegistration.Create(descriptor.Adaptation, NarrativeRegistration.Systems()),
                    out UnityWorldHost? host,
                    out WorldCreateResult createResult);

                if (!opened || host == null)
                {
                    failure = new LiveWorldFailure(
                        "create", createResult.Code, "world creation refused: " + createResult.Detail);
                    return false;
                }

                created.Host = host;
                var registry = new TargetRegistry(session, checked((uint)(targetCount + 4096)));
                created.Publisher = new AssemblyPublisher(
                    host,
                    registry,
                    family.CreateRecipes(),
                    family.CreateMigrations(),
                    descriptor.Descriptor);
                created.Targets = new LiveTargetIndex(created.Publisher.Recipes);
                created.Seeder = new LiveTargetSeeder(host, registry, created.Targets);

                IDerivationValueSource values = family.CreateValues();
                created.RootScope = family.RootScope;
                created.Validator = new DerivationModeSwitchValidator(values, created.TargetView);
                created.Lane = CompositionHost.CreateDefault(
                    session,
                    family.RootScope,
                    new CatalogManifestSource(family.Catalog, declarations),
                    null,
                    CompositionLaneSeed.InitialAssembly.WithScopes(family.DeclareScopes(created.scopes)),
                    created.Validator);

                created.Pipeline = new DerivedAssemblyPipeline(
                    host,
                    created.Lane,
                    created.Publisher,
                    created.Targets,
                    created.Seeder,
                    values,
                    null,
                    null,
                    created.Publisher.Migrations,
                    new StagedResourceGate(StagedByteCeiling, family.Issuer),
                    new PlanBudget(
                        StagedByteCeiling, StagedByteCeiling, ScratchCapacityBytes, ScratchBytesPerSlot));

                created.Time = new WorldTimeDriver(host, new StepInputCutoff(64, 256), new PluginClockRegistry(64), 1U);
                created.Time.AdoptResourceTable(descriptor.Adaptation.NativeTable!);

                if (!created.SeedTargets(targetCount, applyTargets))
                {
                    failure = new LiveWorldFailure(
                        "seed",
                        DiagnosticCode.ResourceUnavailable,
                        created.LastFailure);
                    created.Dispose();
                    return false;
                }


                world = created;
                return true;
            }
            catch (Exception exception)
            {
                failure = new LiveWorldFailure(
                    "exception", DiagnosticCode.ApplyFault, exception.GetType().FullName + ": " + exception.Message);
                created.Dispose();
                return false;
            }
        }

        /// <summary>Pumps one frame of the world's own temporal driver at the supplied host tick.</summary>
        public TimeFrameReport Pump(ulong hostTicksNow) => Time.PumpFrame(hostTicksNow);

        /// <summary>Pumps <paramref name="frames"/> idle frames and returns the steps they committed.</summary>
        public ulong PumpIdle(int frames)
        {
            ulong committed = 0UL;
            for (int i = 0; i < frames; i++)
            {
                committed += Time.PumpFrame(IdlePumpTicks * (ulong)(i + 2)).StepsCommitted;
            }

            return committed;
        }

        /// <summary>
        /// Mounts the narrative chapter provider at the world root, which is the composition edit the live apply
        /// workload measures: a valid plan affecting exactly <see cref="VillagerCount"/> existing targets.
        /// </summary>
        public DerivedAssemblyReport MountChapterProvider()
        {
            IReadOnlyList<CatalogPluginDeclaration> declarations = Gc013NarrativeHost.Declarations(
                ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema);

            CompositionEditPayload payload = NarrativeMounts.Mount(
                declarations[0].Manifest,
                NarrativeKeys.ChapterOneInstall,
                RootScope,
                declarations[0].SchemaDefaults);

            return SubmitAndDerive(payload, "mount-chapter-one");
        }

        /// <summary>
        /// Answers the lane's current publication with an unchanged assembly, so the two halves of the one series stay
        /// joined (P-006). A caller that has published nothing else calls this after a `NoTargetChange` derivation.
        /// </summary>
        public bool PublishUnchanged(string label)
        {
            OperationId operation = NextOperation(Host.World);
            AssemblyPublicationReport report = Publisher.PublishUnchangedAssembly(
                operation, Lane.Committed.Revision, Lane.Committed.Epoch);
            if (!report.Published)
            {
                LastFailure = label + ": the unchanged assembly publication was refused: " + report.Detail;
                return false;
            }

            return MatchesPublishedAssembly();
        }

        /// <summary>
        /// Seeds live targets as real ECS storage without publishing them, so the caller can measure the single
        /// publication that installs their rows: this is the "1,000-target spawn under an already-active provider" case,
        /// where one publication must give every spawned target its complete effective assembly (P-024).
        /// </summary>
        public bool TrySeedTargets(int count, out string detail)
        {
            detail = string.Empty;
            for (int i = 0; i < count; i++)
            {
                TargetId target = BenchmarkFixtureVariants.LiveStateTarget(villagers.Count + i);
                ScopeId scope = scopes.Count == 0 ? RootScope : scopes[i % scopes.Count];
                if (!Seeder.TrySeed(
                        target,
                        scope,
                        NarrativeKeys.VillagerRecipe,
                        out TargetHandle _,
                        out DiagnosticCode code,
                        out string seedDetail))
                {
                    detail = "spawn seed " + i.ToString(CultureInfo.InvariantCulture) + " refused: " + code + ": "
                        + seedDetail;
                    return false;
                }

                villagers.Add(target);
            }

            detail = "seededSpawnTargets=" + count.ToString(CultureInfo.InvariantCulture)
                + ";liveTargets=" + TargetCount.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>
        /// Publishes the world's assembly for the lane's committed composition: the real derived-assembly publication.
        /// Seeding storage or mounting a provider is followed by exactly this call, and the publisher's own fenced
        /// apply window is what the apply measurement reads (P-029, P-030).
        /// </summary>
        public DerivedAssemblyReport PublishDerivedNow(string label)
        {
            DerivedAssemblyReport report = Pipeline.PublishDerived(NextOperation(Host.World));
            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange && !PublishUnchanged(label + "/unchanged"))
            {
                report.Detail = LastFailure;
            }
            else if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                LastFailure = label + ": the world refused the assembly: " + report.Describe();
            }

            return report;
        }

        /// <summary>Copies one target's authoritative state slot as the world's own reader sees it (P-032).</summary>
        public bool TryReadSlot(TargetId target, OwnerId owner, SlotId slot, out int value)
        {
            IReadOnlyList<LiveSlotState> slots = Seeder.ReadLiveSlots(new[] { target });
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i].Slot.Owner.Equals(owner) && slots[i].Slot.Slot.Equals(slot))
                {
                    value = slots[i].Value;
                    return true;
                }
            }

            value = int.MinValue;
            return false;
        }

        /// <summary>
        /// Writes one authoritative state slot through the world's own owner path (P-032). The write goes into ECS
        /// storage; there is no managed mirror to keep in step, which is what the authority fixture asserts.
        /// </summary>
        public bool TryWriteSlot(
            TargetId target,
            OwnerId owner,
            SlotId slot,
            uint schemaVersion,
            int value,
            out string detail)
        {
            if (Seeder.TrySeedSlot(target, owner, slot, schemaVersion, value, out DiagnosticCode code, out string refusal))
            {
                detail = string.Empty;
                return true;
            }

            detail = "the owner write was refused: " + code + ": " + refusal;
            return false;
        }

        /// <summary>Copies one target's published derived binding rows (P-017).</summary>
        public IReadOnlyList<CapabilityBinding> ReadBindingRows(TargetId target) => Publisher.ReadBindingRows(target);

        /// <summary>True when the lane and the world are on the same publication of the one series (P-006).</summary>
        public bool MatchesPublishedAssembly() =>
            AssemblyPublisher.MatchesPublishedAssembly(
                Lane.Committed.Revision,
                Lane.Committed.Epoch,
                Publisher.PublishedRevision,
                Host.CurrentEpoch);

        /// <summary>The first target the apply workload mounts the provider for; null when there is none.</summary>
        public TargetId FirstVillager => villagers.Count == 0 ? default(TargetId) : villagers[0];

        /// <summary>Stops and disposes the world, returning the registry to its pre-creation size.</summary>
        public void Stop(string reason)
        {
            if (Host == null)
            {
                return;
            }

            try
            {
                Time.Clear(out int discardedCommands, out int pendingWakes);
                _ = discardedCommands;
                _ = pendingWakes;
            }
            catch (Exception exception)
            {
                LastFailure = "the time driver refused to clear: " + exception.Message;
            }

            try
            {
                Host.Stop(NextOperation(Host.World), reason);
            }
            catch (Exception exception)
            {
                LastFailure = "the world refused to stop: " + exception.Message;
            }

            Host.Dispose();
        }

        public void Dispose() => Stop("gc-026 benchmark teardown");

        /// <summary>One composition edit and the world's assembly for that same publication (P-006).</summary>
        private DerivedAssemblyReport SubmitAndDerive(CompositionEditPayload payload, string label)
        {
            var refused = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.Refused };
            EditAdmission admission = Lane.SubmitEdit(payload, NextOperation(Host.World), Lane.Committed.Revision);
            if (!admission.Staged)
            {
                LastFailure = label + ": the lane refused the edit (" + admission.Kind + "/" + admission.Code + ")";
                refused.Detail = LastFailure;
                return refused;
            }

            IReadOnlyList<PublishedOperation> published = Lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                LastFailure = label + ": the composition publication was refused ("
                    + (published.Count > 0 ? published[0].Outcome + "/" + published[0].Code : "none") + ")";
                refused.Detail = LastFailure;
                return refused;
            }

            DerivedAssemblyReport report = Pipeline.PublishDerived(NextOperation(Host.World));
            if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
            {
                if (!PublishUnchanged(label + "/unchanged"))
                {
                    refused.Detail = LastFailure;
                    return refused;
                }
            }
            else if (report.Outcome == DerivedAssemblyOutcome.Refused)
            {
                LastFailure = label + ": the world refused the assembly: " + report.Describe();
            }

            return report;
        }

        private bool SeedTargets(int targetCount, int applyTargets)
        {
            for (int i = 0; i < targetCount; i++)
            {
                TargetId target = BenchmarkFixtureVariants.LiveStateTarget(i);
                ScopeId scope = scopes[i % scopes.Count];
                bool eligible = i < applyTargets;
                DefinitionRef recipe = eligible ? NarrativeKeys.VillagerRecipe : NarrativeKeys.DecorativeCrowdRecipe;

                if (!Seeder.TrySeed(target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                {
                    LastFailure = "target " + i.ToString(CultureInfo.InvariantCulture) + " refused: " + code + ": "
                        + detail;
                    return false;
                }

                if (eligible)
                {
                    villagers.Add(target);
                }
                else
                {
                    ineligible.Add(target);
                }
            }

            return true;
        }

        private IReadOnlyList<DerivationTarget> TargetView()
        {
            DerivationInputTargets view = Targets.BuildDerivationTargets();
            return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
        }

        private OperationId NextOperation(WorldId world)
        {
            operationSequence++;
            return new OperationId(world, NarrativeKeys.Issuer, operationSequence);
        }
    }

    /// <summary>
    /// The declared facts of the benchmark's live world: the narrative family's own catalog and declarations plus a
    /// generated scope tree. The declarations are the narrative host's, so the live world is a real narrative world
    /// and this type authors no catalog content of its own (P-009's generated-registration rule).
    /// </summary>
    internal sealed class BenchmarkLiveFamily
    {
        private readonly SpawnRecipeCatalog recipes;
        private readonly MigrationRegistry migrations;
        private readonly IDerivationValueSource values;

        public BenchmarkLiveFamily(
            ImmutableCatalog catalog,
            IReadOnlyList<CatalogPluginDeclaration> declarations,
            int scopeCount)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
            ScopeCount = scopeCount;
            RootScope = NarrativeKeys.RootScope;

            var applier = new NarrativeRecipeApplier();
            recipes = new SpawnRecipeCatalog(new List<SpawnRecipe>(NarrativeRecipes.Catalog(applier).Recipes));
            migrations = new MigrationRegistry(new List<ISlotMigration>
            {
                new NarrativeConversationNodeMigration(),
                new NarrativeConversationStatusMigration(),
            });

            values = new FixtureValueSource().RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);
        }

        public ICatalog Catalog { get; }

        public IReadOnlyList<CatalogPluginDeclaration> Declarations { get; }

        public int ScopeCount { get; }

        public ScopeId RootScope { get; }

        public Id128 Issuer => NarrativeKeys.Issuer;

        public SpawnRecipeCatalog CreateRecipes() => recipes;

        public MigrationRegistry CreateMigrations() => migrations;

        public IDerivationValueSource CreateValues() => values;

        public PipelineDescriptorReport CompilePipeline()
        {
            var kinds = new ScheduleDispatchKindTable()
                .Add(NarrativeKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.DialogueSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.QuestSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.GateSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.EncounterSystem, SystemDispatchKind.ManagedSystem)
                .Add(NarrativeKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

            var manifests = new List<PluginManifest>(Declarations.Count);
            for (int i = 0; i < Declarations.Count; i++)
            {
                manifests.Add(Declarations[i].Manifest);
            }

            return OwnershipSchedulePipeline.Build(manifests, kinds, new NarrativeSlotMigrations());
        }

        /// <summary>
        /// The generated scope tree of the world definition: the root's children first, then each child's own
        /// children, with the depth computed from the parent chain because the lane validates it when it opens the
        /// composition (P-010). <paramref name="into"/> receives the generated identities in tree order, which is the
        /// order the live targets are distributed over.
        /// </summary>
        public IReadOnlyList<ScopeRecord> DeclareScopes(List<ScopeId> into)
        {
            into.Clear();
            int generated = ScopeCount - 1;
            var records = new List<ScopeRecord>(generated);
            var identities = new List<ScopeId>(generated);
            var depths = new List<int>(generated);

            for (int i = 0; i < generated; i++)
            {
                ScopeId scope = BenchmarkIds.GeneratedScope(0x1000 + i);
                ScopeId parent;
                int depth;
                if (i == 0)
                {
                    parent = RootScope;
                    depth = 1;
                }
                else
                {
                    int parentIndex = (i - 1) / BenchmarkLiveWorld.ScopeFanOut;
                    parent = identities[parentIndex];
                    depth = depths[parentIndex] + 1;
                }

                identities.Add(scope);
                depths.Add(depth);
                into.Add(scope);
                records.Add(new ScopeRecord(
                    scope,
                    parent,
                    depth,
                    new IsolationSet(false, null),
                    new IsolationSet(false, null),
                    null,
                    null));
            }

            return records;
        }
    }
}
