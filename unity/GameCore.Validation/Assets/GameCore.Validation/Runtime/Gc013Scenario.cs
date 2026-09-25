// GameCore.Validation.ProbeHost — the GC-013 qualification scenario (incremental invalidation, reparenting and live
// mode switching).
//
// The gate sentence this file implements, from `docs/game-core/09-implementation-guide.md` (Wave 4, GC-013):
//
//   "Reparent and both mode-switch directions pass in both early genres, with no per-instance imports added in
//    Automatic." — and, from the same task's acceptance: "Existing/future targets follow the published mode;
//    isolated branches remain unchanged; a conflict preserves old membership/mode."
//
// One runner, two family adapters. The world plumbing below is exactly what the GC-010 and GC-011 scenarios build,
// because it is the same chain of real modules in both genres:
//
//   * `UnityWorldRegistry.TryCreate` + the family's generated registration  → a real owned world with real ECS
//     storage and the compiled schedule of the family's declarations (GC-005, GC-009);
//   * `TargetRegistry` + `LiveTargetIndex` + `LiveTargetSeeder`            → the live targets of the family, each
//     seeded as real ECS storage with its own state slots (GC-008, P-010, P-032);
//   * `CompositionHost` + the family's declared lane seed                  → the real control lane, opened at the
//     world definition's declared tree, wired to `DerivationModeSwitchValidator` so a mode switch whose proposed
//     closure the kernel refuses keeps the old mode and the old assembly (GC-004, P-014);
//   * `DerivedAssemblyPipeline`                                            → the real derivation (GC-006, over the
//     incremental engine of GC-013), proposal translation, plan and publication into the world (GC-008).
//
// Nothing here re-implements a kernel module and nothing is discovered reflectively. The scripted sequence is A-E of
// the task's own acceptance, in the order the specification names them, and every observation is a named
// `Gc013Step` carrying the values it was computed from.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named GC-013 observation: what was checked and the values it was computed from.</summary>
    public sealed class Gc013Step
    {
        public Gc013Step(string name, bool passed, string detail)
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
    /// Full result of one GC-013 run: the named observations plus one digest over them. The digest is computed over
    /// the canonical `name=pass|fail` lines with the same digest function the narrative trace uses, so the standalone
    /// probe can archive a single stable literal per family and a run that records a different set of observations (or
    /// a failing one) cannot report the expected digest.
    /// </summary>
    public sealed class Gc013ScenarioResult
    {
        public Gc013ScenarioResult(string label, IReadOnlyList<Gc013Step> steps)
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
        public IReadOnlyList<Gc013Step> Steps { get; }

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

    /// <summary>The real world pieces one family seeds its declared targets through.</summary>
    public sealed class Gc013WorldContext
    {
        public Gc013WorldContext(UnityWorldHost host, LiveTargetIndex targets, LiveTargetSeeder seeder)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Targets = targets ?? throw new ArgumentNullException(nameof(targets));
            Seeder = seeder ?? throw new ArgumentNullException(nameof(seeder));
        }

        public UnityWorldHost Host { get; }

        public LiveTargetIndex Targets { get; }

        public LiveTargetSeeder Seeder { get; }
    }

    /// <summary>
    /// One family's declared facts: the catalog data its lane mounts, the scope tree it lives in, the live targets it
    /// seeds, and the composition edits the scripted sequence submits. A family method that cannot do what it says
    /// throws with the diagnostic code and detail it received, so a refusal is never silent.
    /// </summary>
    public interface IGc013Family
    {
        /// <summary>Family label every observation name is qualified with, e.g. <c>narrative</c>.</summary>
        string Label { get; }

        /// <summary>Fingerprint of the catalog this run uses, recorded in the details (P-028).</summary>
        string CatalogFingerprint { get; }

        /// <summary>Deterministic salt of the session id sequence, so a run is reproducible (P-008).</summary>
        ulong SessionSalt { get; }

        /// <summary>Stable issuer of every operation identity this run mints (P-050).</summary>
        Id128 Issuer { get; }

        /// <summary>The validated catalog the lane's manifest source resolves through (P-009).</summary>
        ICatalog Catalog { get; }

        /// <summary>The family's generated-style declarations, mounted through the real lane.</summary>
        IReadOnlyList<CatalogPluginDeclaration> Declarations { get; }

        /// <summary>The world root scope: the only scope without a parent (P-010).</summary>
        ScopeId WorldRootScope { get; }

        /// <summary>The publication the lane joins the world's initial assembly at, with its declared tree.</summary>
        CompositionLaneSeed LaneSeed { get; }

        /// <summary>Edits the world definition needs before its targets can be derived for.</summary>
        IReadOnlyList<CompositionEditPayload> SetupEdits { get; }

        /// <summary>Two scope creations under the world root that no live target lives in (P-010).</summary>
        IReadOnlyList<CompositionEditPayload> SpareScopeEdits { get; }

        /// <summary>Compiles the family's declared ownership surface and schedule (GC-007, GC-009).</summary>
        PipelineDescriptorReport CompilePipeline();

        /// <summary>Creation request of one command-driven world of this family (O-01).</summary>
        WorldCreateRequest CreateRequest(WorldId world, OperationId operation);

        /// <summary>The world's composition root over the compiled schedule (GC-008, GC-009).</summary>
        UnityWorldRegistration CreateRegistration(ScheduleAdaptation adaptation);

        /// <summary>The family's closed recipe catalog, including its explicitly opted-in recipe (P-015).</summary>
        SpawnRecipeCatalog CreateRecipes();

        /// <summary>The slot migrations the family registers (P-029).</summary>
        MigrationRegistry CreateMigrations();

        /// <summary>The registered reducer(s) and predicate(s) the family's rules resolve (P-009, P-019).</summary>
        IDerivationValueSource CreateValues();

        /// <summary>Seeds every declared target as real ECS storage and registers it in the index.</summary>
        bool SeedTargets(Gc013WorldContext context);

        /// <summary>
        /// Seeds the one target whose descriptor declares a complete explicit opt-in for the provider, which is what
        /// keeps its binding in Conservative mode while every automatically eligible target loses it (P-013).
        /// </summary>
        bool SeedOptedInTarget(Gc013WorldContext context);

        /// <summary>Prepares the family's spawn appliers for the future target's spawn.</summary>
        void PrepareSpawn();

        /// <summary>O-03 mount of the family's provider at its own scope (P-013).</summary>
        CompositionEditPayload MountProvider();

        /// <summary>O-03 mount of the family's second provider at its sibling branch (07 section 3.1 / 2.1).</summary>
        CompositionEditPayload MountSecondProvider();

        /// <summary>O-02 move of the branch the sequence reparents (P-025).</summary>
        CompositionEditPayload ScopeReparent();

        /// <summary>O-08 world propagation mode edit (P-013, P-014).</summary>
        CompositionEditPayload ModeSet(PropagationMode mode);

        /// <summary>O-03 mount of the first provider of the family's overlapping exclusive pair (P-014).</summary>
        CompositionEditPayload MountConflictProvider();

        /// <summary>O-03 mount of the second provider of that pair.</summary>
        CompositionEditPayload MountConflictSecondProvider();

        /// <summary>Scope the provider is mounted at.</summary>
        ScopeId ProviderScope { get; }

        /// <summary>Scope the sequence moves.</summary>
        ScopeId MovedScope { get; }

        /// <summary>The new parent of <see cref="MovedScope"/>.</summary>
        ScopeId MoveDestination { get; }

        /// <summary>Installation of the second provider: the provenance the moved binding must name afterwards.</summary>
        PluginInstanceId SecondProviderInstance { get; }

        /// <summary>Installation of the first exclusive-conflict provider.</summary>
        PluginInstanceId ConflictProviderInstance { get; }

        /// <summary>Installation of the second exclusive-conflict provider.</summary>
        PluginInstanceId ConflictSecondProviderInstance { get; }

        /// <summary>Scope the first conflict provider is mounted at.</summary>
        ScopeId ConflictProviderScope { get; }

        /// <summary>Scope the second conflict provider is mounted at.</summary>
        ScopeId ConflictSecondProviderScope { get; }

        /// <summary>The capability the family's provider derives (P-013).</summary>
        CapabilityId DerivedCapability { get; }

        /// <summary>The exclusive capability the conflict pair declares (P-019).</summary>
        CapabilityId ConflictCapability { get; }

        /// <summary>The value the provider's payload carries.</summary>
        int ProviderValue { get; }

        /// <summary>The value the second provider's payload carries.</summary>
        int SecondProviderValue { get; }

        /// <summary>
        /// The targets that must receive the derived capability from Automatic propagation alone, with no import and
        /// no opt-in of their own (P-013's eligible existing descendants).
        /// </summary>
        IReadOnlyList<TargetId> AutomaticTargets { get; }

        /// <summary>The target whose branch the sequence moves (P-025).</summary>
        TargetId MovedTarget { get; }

        /// <summary>The target inside the family's capability boundary, which receives nothing in either mode (P-016).</summary>
        TargetId IsolatedTarget { get; }

        /// <summary>The target whose recipe no rule selects, which stays on its base recipe (P-015).</summary>
        TargetId IneligibleTarget { get; }

        /// <summary>The target whose descriptor declares the complete explicit opt-in (P-013).</summary>
        TargetId OptedInTarget { get; }

        /// <summary>The target that does not exist yet: one spawn publishes it fully assembled (P-024).</summary>
        TargetId FutureTarget { get; }

        /// <summary>The future target's recipe.</summary>
        DefinitionRef FutureRecipe { get; }

        /// <summary>The future target's owner scope: beneath the provider, so the mode decides its binding.</summary>
        ScopeId FutureScope { get; }

        /// <summary>Owner of the mutable live state slot the sequence writes before the move (P-032).</summary>
        OwnerId MutableOwner { get; }

        /// <summary>That slot.</summary>
        SlotId MutableSlot { get; }

        /// <summary>The value written into it; the move must not change it (P-025).</summary>
        int MutableValue { get; }
    }

    /// <summary>Runs the GC-013 sequence over one family and one catalog.</summary>
    public static class Gc013Scenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>Staged lease ceiling of the scenario's plan resource gate, in bytes (P-022).</summary>
        private const ulong StagedByteCeiling = 1024UL * 1024UL;

        private const ulong ScratchCapacityBytes = 4096UL;

        private const ulong ScratchBytesPerSlot = 64UL;

        private const ulong PrepareBytesLimit = 1024UL * 1024UL;

        /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
        private const ulong IdlePumpTicks = 1000000UL;

        private const int IdlePumpFrames = 4;

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. Both families record
        /// exactly these names, so a renamed or dropped observation fails the EditMode suite and the player probe
        /// instead of shrinking them silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc013-world-and-live-targets",
            "gc013-mount-inherits-to-every-eligible-target",
            "gc013-reparent-preserves-target-state-and-inheritance",
            "gc013-isolated-branch-unchanged-across-the-reparent",
            "gc013-opt-in-target-declared-and-derived",
            "gc013-isolated-branch-unchanged-across-the-opt-in-target",
            "gc013-mode-switch-automatic-to-conservative",
            "gc013-isolated-branch-unchanged-across-the-conservative-switch",
            "gc013-future-target-in-conservative-derives-nothing",
            "gc013-isolated-branch-unchanged-across-the-future-spawn",
            "gc013-mode-switch-conservative-to-automatic",
            "gc013-isolated-branch-unchanged-across-the-automatic-switch",
            "gc013-exclusive-conflict-preserves-mode-and-membership",
            "gc013-isolated-branch-unchanged-across-the-conflict",
            "gc013-teardown-settles-and-disposes",
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
        public static Gc013ScenarioResult Run(IGc013Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        private sealed class Executor
        {
            private readonly IGc013Family family;
            private readonly List<Gc013Step> steps = new List<Gc013Step>();
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

            private ulong operationSequence;
            private int registryBeforeCreate;
            private string lastFailure = string.Empty;
            private string isolatedBaseline = string.Empty;
            private string isolatedPrevious = string.Empty;

            public Executor(IGc013Family family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public Gc013ScenarioResult Run()
            {
                CreateWorldAndTargets();
                ProveAutomaticInheritance();
                ReparentAndProveInheritance();
                ProveIsolatedUnchanged("gc013-isolated-branch-unchanged-across-the-reparent");
                SeedOptedInTargetAndPublish();
                ProveIsolatedUnchanged("gc013-isolated-branch-unchanged-across-the-opt-in-target");
                SwitchMode(PropagationMode.Conservative, "gc013-mode-switch-automatic-to-conservative");
                ProveIsolatedUnchanged("gc013-isolated-branch-unchanged-across-the-conservative-switch");
                SpawnFutureTargetInConservative();
                ProveIsolatedUnchanged("gc013-isolated-branch-unchanged-across-the-future-spawn");
                SwitchMode(PropagationMode.Automatic, "gc013-mode-switch-conservative-to-automatic");
                ProveIsolatedUnchanged("gc013-isolated-branch-unchanged-across-the-automatic-switch");
                ProveExclusiveConflictPreservesOldMode();
                ProveIsolatedUnchanged("gc013-isolated-branch-unchanged-across-the-conflict");
                TearDownSafely();
                return new Gc013ScenarioResult(family.Label, steps);
            }

            // ------------------------------------------------------------------ 1. the real world and its targets

            private void CreateWorldAndTargets()
            {
                const string name = "gc013-world-and-live-targets";
                try
                {
                    PipelineDescriptorReport descriptorReport = family.CompilePipeline();
                    if (!descriptorReport.Succeeded
                        || descriptorReport.Descriptor == null
                        || descriptorReport.Adaptation == null
                        || descriptorReport.Compilation == null)
                    {
                        Add(name, false, "the ownership and schedule pipeline refused: " + descriptorReport.Describe());
                        return;
                    }

                    registryBeforeCreate = UnityWorldRegistry.Count;
                    WorldId world = new WorldId(sessionSequence.Next());
                    WorldCreateRequest request = family.CreateRequest(world, NextOperation(world));
                    UnityWorldRegistration registration = family.CreateRegistration(descriptorReport.Adaptation);

                    bool created = UnityWorldRegistry.TryCreate(
                        request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        Add(name, false, "world creation failed: " + result.Code + ": " + result.Detail);
                        return;
                    }

                    registry = new TargetRegistry(world, 16);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        family.CreateRecipes(),
                        family.CreateMigrations(),
                        descriptorReport.Descriptor);

                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    // One registered value source serves both the mode-switch validator and the derivation pipeline, so
                    // the reducer and predicate identities the lane checks are exactly the ones the world derives with.
                    IDerivationValueSource valueSource = family.CreateValues();

                    // P-014: the lane consults this validator while planning, before anything is staged, so a mode
                    // switch whose proposed closure the kernel refuses keeps the old mode and the old assembly.
                    validator = new DerivationModeSwitchValidator(valueSource, TargetView);
                    lane = CompositionHost.CreateDefault(
                        world,
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
                        new StagedResourceGate(StagedByteCeiling, family.Issuer),
                        new PlanBudget(
                            PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    bool setupEdits = PublishEdits(family.SetupEdits);
                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));
                    ulong idleSteps = PumpIdleFrames();

                    bool registered = true;
                    for (int i = 0; i < family.AutomaticTargets.Count; i++)
                    {
                        registered &= targets.Contains(family.AutomaticTargets[i]);
                    }

                    registered &= targets.Contains(family.MovedTarget)
                        && targets.Contains(family.IsolatedTarget)
                        && targets.Contains(family.IneligibleTarget);

                    bool joined = MatchesPublishedAssembly();

                    bool pass = setupEdits
                        && seeded
                        && registered
                        && family.SpareScopeEdits.Count == 2
                        && idleSteps == 0UL
                        && joined
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && host.Lifecycle == WorldLifecycleState.Running;

                    Add(name, pass,
                        "session=" + world.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; laneSeed=" + family.LaneSeed.ToString()
                        + "; setupEdits=" + setupEdits
                        + "; targetsSeeded=" + seeded
                        + "; targetsRegistered=" + registered
                        + "; idleFrames=" + IdlePumpFrames.ToString(CultureInfo.InvariantCulture)
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + lane.Committed.Mode
                        + "; joined=" + joined
                        + "; lifecycle=" + host.Lifecycle
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 2. A: automatic inheritance

            /// <summary>
            /// A. Both providers are mounted in Automatic (07 section 2.1 / 3.1). Every automatically eligible
            /// existing target carries the derived capability, no target descriptor gained an import or an opt-in,
            /// and the isolated and ineligible targets carry nothing (P-013, P-015, P-016).
            /// </summary>
            private void ProveAutomaticInheritance()
            {
                const string name = "gc013-mount-inherits-to-every-eligible-target";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || seeder == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    bool providerEdit = PublishEdit(family.MountProvider(), "mount-provider");
                    bool secondEdit = PublishEdit(family.MountSecondProvider(), "mount-second-provider");
                    DerivationResult? derivation = LastDerivation;

                    int derivedTargets = 0;
                    bool valuesAgree = true;
                    var missing = new List<string>();
                    for (int i = 0; i < family.AutomaticTargets.Count; i++)
                    {
                        TargetId target = family.AutomaticTargets[i];
                        bool has = HasCapability(derivation, target, family.DerivedCapability);
                        int derived = EffectiveValueOf(derivation, target, family.DerivedCapability);
                        int published = RowValueOf(target, family.DerivedCapability);
                        if (has)
                        {
                            derivedTargets++;
                        }
                        else
                        {
                            missing.Add(target.ToString());
                        }

                        valuesAgree &= has
                            && derived == published
                            && derived > 0
                            && publisher.Published.HasBinding(target, family.DerivedCapability, 0U);
                    }

                    int isolatedRows = publisher.ReadBindingRows(family.IsolatedTarget).Count;
                    int ineligibleRows = publisher.ReadBindingRows(family.IneligibleTarget).Count;
                    bool isolatedClear = isolatedRows == 0
                        && !publisher.Published.HasBinding(family.IsolatedTarget, family.DerivedCapability, 0U)
                        && !HasCapability(derivation, family.IsolatedTarget, family.DerivedCapability);
                    bool ineligibleClear = ineligibleRows == 0
                        && !HasCapability(derivation, family.IneligibleTarget, family.DerivedCapability);
                    bool factsRead = CountDescriptorFacts(derivation, out int imports, out int optIns);

                    isolatedBaseline = IsolatedFingerprint(derivation);
                    isolatedPrevious = isolatedBaseline;

                    bool joined = MatchesPublishedAssembly();
                    bool pass = providerEdit
                        && secondEdit
                        && LastOutcome == DerivedAssemblyOutcome.Published
                        && joined
                        && derivedTargets == family.AutomaticTargets.Count
                        && valuesAgree
                        && isolatedClear
                        && ineligibleClear
                        && factsRead
                        && imports == 0
                        && optIns == 0
                        && lane.Committed.Mode == PropagationMode.Automatic;

                    Add(name, pass,
                        "providerEdit=" + providerEdit
                        + "; secondEdit=" + secondEdit
                        + "; outcome=" + LastOutcome
                        + "; eligibleTargets=" + family.AutomaticTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; derivedTargets=" + derivedTargets.ToString(CultureInfo.InvariantCulture)
                        + "; missing=" + Join(missing)
                        + "; isolatedRows=" + isolatedRows.ToString(CultureInfo.InvariantCulture)
                        + "; ineligibleRows=" + ineligibleRows.ToString(CultureInfo.InvariantCulture)
                        + "; descriptorImports=" + imports.ToString(CultureInfo.InvariantCulture)
                        + "; descriptorOptIns=" + optIns.ToString(CultureInfo.InvariantCulture)
                        + "; isolatedBaseline=" + isolatedBaseline
                        + "; joined=" + joined
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 3. B: the move

            /// <summary>
            /// B. The branch moves under the sibling provider (P-025, 07 s2.4 / s3.4). The target keeps its identity
            /// and its owner scope and its seeded live state value, its inherited binding becomes the new provider's
            /// payload named by that provider's installation, and the moved scope's parent is the new one.
            /// </summary>
            private void ReparentAndProveInheritance()
            {
                const string name = "gc013-reparent-preserves-target-state-and-inheritance";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || seeder == null
                        || targets == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    if (!targets.TryGet(family.MovedTarget, out LiveTarget live))
                    {
                        Add(name, false, "the moved target is not a live target of this world (P-005)");
                        return;
                    }

                    ScopeId scopeBefore = live.Scope;
                    int stateBefore = LiveStateValue(family.MovedTarget);
                    int valueBefore = EffectiveValueOf(LastDerivation, family.MovedTarget, family.DerivedCapability);

                    bool published = PublishEdit(family.ScopeReparent(), "scope-reparent");
                    DerivationResult? derivation = LastDerivation;

                    TargetAssembly? assembly = AssemblyOf(derivation, family.MovedTarget);
                    bool identityHeld = assembly != null
                        && assembly.Target.Equals(family.MovedTarget)
                        && assembly.Scope.Equals(scopeBefore);
                    int valueAfter = EffectiveValueOf(derivation, family.MovedTarget, family.DerivedCapability);
                    bool providerIsSecond = TryEffectiveProvider(
                        derivation, family.MovedTarget, family.DerivedCapability, out ProviderInstallationId provider)
                        && provider.Value.Equals(family.SecondProviderInstance.Value);
                    int stateAfter = LiveStateValue(family.MovedTarget);

                    ScopeRecord? moved = null;
                    bool parentMoved = lane.Committed.Scopes.TryGet(family.MovedScope, out moved)
                        && moved != null
                        && moved.Parent.Equals(family.MoveDestination);

                    bool pass = published
                        && LastOutcome == DerivedAssemblyOutcome.Published
                        && identityHeld
                        && valueBefore == family.ProviderValue
                        && valueAfter == family.SecondProviderValue
                        && providerIsSecond
                        && parentMoved
                        && stateBefore == family.MutableValue
                        && stateAfter == family.MutableValue
                        && RowValueOf(family.MovedTarget, family.DerivedCapability) == family.SecondProviderValue
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "published=" + published
                        + "; outcome=" + LastOutcome
                        + "; scope=" + scopeBefore
                        + "->stillOwned=" + (assembly != null && assembly.Scope.Equals(scopeBefore))
                        + "; effectiveValue=" + valueBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + valueAfter.ToString(CultureInfo.InvariantCulture)
                        + "; expected=" + family.ProviderValue.ToString(CultureInfo.InvariantCulture)
                        + "->" + family.SecondProviderValue.ToString(CultureInfo.InvariantCulture)
                        + "; supportProviderIsSecond=" + providerIsSecond
                        + "; movedScope=" + family.MovedScope
                        + "; parent=" + family.MoveDestination
                        + "; liveState=" + stateBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + stateAfter.ToString(CultureInfo.InvariantCulture)
                        + "; expectedState=" + family.MutableValue.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 4. C (setup): the declared opt-in

            /// <summary>
            /// C. The one target whose descriptor declares a complete explicit opt-in is seeded mid-run and published
            /// through an ordinary composition publication, so the later Conservative switch has a target that must
            /// keep its binding while every automatically eligible target loses it (P-013).
            /// </summary>
            private void SeedOptedInTargetAndPublish()
            {
                const string name = "gc013-opt-in-target-declared-and-derived";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || seeder == null
                        || targets == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    // P-006 has one publication series: a target that appears between two publications needs a lane
                    // publication to ride on, so the run submits one scope creation that no target lives in and lets
                    // the seeded target be the publication's only derivable change.
                    bool seeded = family.SeedOptedInTarget(new Gc013WorldContext(host, targets, seeder));
                    bool published = PublishEdit(family.SpareScopeEdits[0], "spare-scope-a");
                    DerivationResult? derivation = LastDerivation;

                    bool has = HasCapability(derivation, family.OptedInTarget, family.DerivedCapability);
                    int derived = EffectiveValueOf(derivation, family.OptedInTarget, family.DerivedCapability);
                    int publishedRow = RowValueOf(family.OptedInTarget, family.DerivedCapability);

                    bool optedIn = false;
                    int descriptorImports = -1;
                    DerivationTarget? descriptorTarget = null;
                    if (derivation != null
                        && derivation.Snapshot.TryGetTarget(family.OptedInTarget, out descriptorTarget)
                        && descriptorTarget != null)
                    {
                        descriptorImports = descriptorTarget.Descriptor.Imports.Count;
                        optedIn = descriptorTarget.Descriptor.OptIns.Count == 1 && descriptorImports == 0;
                    }

                    bool pass = seeded
                        && published
                        && LastOutcome == DerivedAssemblyOutcome.Published
                        && has
                        && derived == family.ProviderValue
                        && publishedRow == family.ProviderValue
                        && optedIn
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "seeded=" + seeded
                        + "; published=" + published
                        + "; outcome=" + LastOutcome
                        + "; derivedValue=" + derived.ToString(CultureInfo.InvariantCulture)
                        + "; expectedValue=" + family.ProviderValue.ToString(CultureInfo.InvariantCulture)
                        + "; publishedRow=" + publishedRow.ToString(CultureInfo.InvariantCulture)
                        + "; declaredOptIn=" + optedIn
                        + "; descriptorImports=" + descriptorImports.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 5. C: both mode directions

            /// <summary>
            /// C. One mode direction. In Conservative the only target that keeps the binding is the one whose
            /// descriptor declares the complete explicit opt-in; in Automatic every eligible target carries it again,
            /// the future target included, and no import was added anywhere (P-013, P-014).
            /// </summary>
            private void SwitchMode(PropagationMode destination, string name)
            {
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || targets == null
                        || validator == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    PropagationMode before = lane.Committed.Mode;
                    bool published = PublishEdit(family.ModeSet(destination), "mode-set-" + destination);
                    DerivationResult? derivation = LastDerivation;
                    bool automatic = destination == PropagationMode.Automatic;

                    var dissenting = new List<string>();
                    for (int i = 0; i < family.AutomaticTargets.Count; i++)
                    {
                        TargetId target = family.AutomaticTargets[i];
                        bool has = HasCapability(derivation, target, family.DerivedCapability);
                        int rows = publisher.ReadBindingRows(target).Count;
                        if (automatic ? !has || rows == 0 : has || rows != 0)
                        {
                            dissenting.Add(target.ToString() + "(has=" + has + ",rows=" + rows + ")");
                        }
                    }

                    bool optedInKept = HasCapability(derivation, family.OptedInTarget, family.DerivedCapability)
                        && EffectiveValueOf(derivation, family.OptedInTarget, family.DerivedCapability)
                            == family.ProviderValue
                        && RowValueOf(family.OptedInTarget, family.DerivedCapability) == family.ProviderValue;

                    bool futureClear = true;
                    string futureDetail = "not-registered";
                    if (targets.Contains(family.FutureTarget))
                    {
                        bool has = HasCapability(derivation, family.FutureTarget, family.DerivedCapability);
                        int rows = publisher.ReadBindingRows(family.FutureTarget).Count;
                        int value = EffectiveValueOf(derivation, family.FutureTarget, family.DerivedCapability);
                        futureClear = automatic
                            ? has && rows > 0 && value == family.ProviderValue
                            : !has && rows == 0;
                        futureDetail = "has=" + has + ",rows=" + rows + ",value=" + value;
                    }

                    bool isolatedClear = !HasCapability(derivation, family.IsolatedTarget, family.DerivedCapability)
                        && publisher.ReadBindingRows(family.IsolatedTarget).Count == 0;
                    bool ineligibleClear = !HasCapability(derivation, family.IneligibleTarget, family.DerivedCapability)
                        && publisher.ReadBindingRows(family.IneligibleTarget).Count == 0;
                    bool factsRead = CountDescriptorFacts(derivation, out int imports, out int optIns);
                    bool scopeImportsRead = CountScopeImports(out int scopeImports);
                    bool importsHeld = factsRead
                        && imports == 0
                        && optIns == 1
                        && scopeImportsRead
                        && scopeImports == 0;

                    bool pass = published
                        && LastOutcome == DerivedAssemblyOutcome.Published
                        && lane.Committed.Mode == destination
                        && dissenting.Count == 0
                        && optedInKept
                        && futureClear
                        && isolatedClear
                        && ineligibleClear
                        && importsHeld
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "mode=" + before
                        + "->" + lane.Committed.Mode
                        + "; destination=" + destination
                        + "; outcome=" + LastOutcome
                        + "; eligibleTargets=" + family.AutomaticTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; dissenting=" + Join(dissenting)
                        + "; optInKept=" + optedInKept
                        + "; futureTarget=" + family.FutureTarget + "(" + futureDetail + ")"
                        + "; isolatedRows="
                        + publisher.ReadBindingRows(family.IsolatedTarget).Count.ToString(CultureInfo.InvariantCulture)
                        + "; ineligibleRows="
                        + publisher.ReadBindingRows(family.IneligibleTarget).Count.ToString(CultureInfo.InvariantCulture)
                        + "; descriptorImports=" + imports.ToString(CultureInfo.InvariantCulture)
                        + "; descriptorOptIns=" + optIns.ToString(CultureInfo.InvariantCulture)
                        + "; scopeImports=" + scopeImports.ToString(CultureInfo.InvariantCulture)
                        + "; validator=" + validator.Describe()
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 6. C: a future target in Conservative

            /// <summary>
            /// C. A target created in Conservative derives nothing and becomes visible with no row at all, because a
            /// descendant-reach rule needs an export plus an import or a complete opt-in in that mode (P-013, P-024).
            /// </summary>
            private void SpawnFutureTargetInConservative()
            {
                const string name = "gc013-future-target-in-conservative-derives-nothing";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || targets == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    // The publication the spawn rides on must carry no derivable target change: a scope no target
                    // lives in is exactly that (P-006, P-024).
                    bool neutralEdit = ApplyEdit(
                        family.SpareScopeEdits[1], "spare-scope-b", out DerivedAssemblyReport forward);
                    bool forwardClear = forward.Outcome == DerivedAssemblyOutcome.NoTargetChange;

                    family.PrepareSpawn();
                    DerivedAssemblyReport spawn = pipeline.PublishSpawn(
                        NextOperation(host.World), family.FutureTarget, family.FutureRecipe, family.FutureScope);

                    bool registered = targets.TryRegister(
                        family.FutureTarget,
                        family.FutureScope,
                        family.FutureRecipe,
                        out DiagnosticCode registerCode,
                        out string registerDetail);

                    int rows = publisher.ReadBindingRows(family.FutureTarget).Count;
                    bool inView = publisher.Published.Bindings.HasTarget(family.FutureTarget);
                    bool pass = neutralEdit
                        && forwardClear
                        && spawn.Outcome == DerivedAssemblyOutcome.Published
                        && spawn.IsSpawn
                        && spawn.CountersJoined
                        && registered
                        && rows == 0
                        && inView
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "neutralEdit=" + neutralEdit
                        + "; forwardOutcome=" + forward.Outcome
                        + "; spawnOutcome=" + spawn.Outcome
                        + "; isSpawn=" + spawn.IsSpawn
                        + "; joined=" + spawn.CountersJoined
                        + "; registered=" + registered
                        + (registered ? string.Empty : "(" + registerCode + ": " + registerDetail + ")")
                        + "; bindingRows=" + rows.ToString(CultureInfo.InvariantCulture)
                        + "; inPublishedView=" + inView
                        + "; mode=" + lane.Committed.Mode
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7. D: the conflict keeps the mode

            /// <summary>
            /// D. P-014 and 02 section 5: a proposed Conservative to Automatic switch may reveal an exclusive
            /// conflict, and the result is a diagnostic with the old mode intact. Two providers whose exclusive rules
            /// both reach the same recipes are mounted while Conservative (where both are denied, so the composition
            /// derives cleanly), and the switch that would expose the conflict is then refused by the lane's own
            /// published-consequence validator with the kernel's `CapabilityConflict`.
            /// </summary>
            private void ProveExclusiveConflictPreservesOldMode()
            {
                const string name = "gc013-exclusive-conflict-preserves-mode-and-membership";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || validator == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    // The pair is mounted in Conservative, where a descendant-reach rule without an export/import or
                    // an opt-in is not granted, so the mount itself derives cleanly and changes no target.
                    bool toConservative = PublishEdit(
                        family.ModeSet(PropagationMode.Conservative), "mode-set-conflict");
                    bool conservativeHeld = lane.Committed.Mode == PropagationMode.Conservative;
                    bool firstMount = PublishEdit(family.MountConflictProvider(), "mount-conflict-provider");
                    DerivedAssemblyOutcome firstOutcome = LastOutcome;
                    bool secondMount = PublishEdit(family.MountConflictSecondProvider(), "mount-conflict-second");
                    DerivedAssemblyOutcome secondOutcome = LastOutcome;

                    string publishedBefore = PublishedStateText();
                    int refusalsBefore = validator.Refusals;
                    bool capabilityAbsent = !publisher.Published.HasBinding(
                        family.MovedTarget, family.ConflictCapability, 0U);

                    EditAdmission admission = lane.SubmitEdit(
                        family.ModeSet(PropagationMode.Automatic),
                        NextOperation(host.World),
                        lane.Committed.Revision);
                    IReadOnlyList<PublishedOperation> drained = lane.Drain();
                    string publishedAfter = PublishedStateText();

                    bool refused = !admission.Staged && admission.Code == DiagnosticCode.CapabilityConflict;
                    bool modeHeld = lane.Committed.Mode == PropagationMode.Conservative;
                    bool nothingPublished = drained.Count == 0;
                    bool assemblyHeld = string.Equals(publishedBefore, publishedAfter, StringComparison.Ordinal);
                    bool validatorRefused = validator.Refusals == refusalsBefore + 1
                        && validator.LastRefusalCode == DiagnosticCode.CapabilityConflict;

                    bool membershipHeld = true;
                    if (!lane.Committed.TryGetInstall(family.ConflictProviderInstance, out InstallEntry? first)
                        || first == null
                        || !first.Scope.Equals(family.ConflictProviderScope))
                    {
                        membershipHeld = false;
                    }

                    if (!lane.Committed.TryGetInstall(family.ConflictSecondProviderInstance, out InstallEntry? second)
                        || second == null
                        || !second.Scope.Equals(family.ConflictSecondProviderScope))
                    {
                        membershipHeld = false;
                    }

                    ScopeRecord? moved = null;
                    bool branchHeld = lane.Committed.Scopes.TryGet(family.MovedScope, out moved)
                        && moved != null
                        && moved.Parent.Equals(family.MoveDestination);

                    bool pass = toConservative
                        && conservativeHeld
                        && firstMount
                        && secondMount
                        && firstOutcome == DerivedAssemblyOutcome.NoTargetChange
                        && secondOutcome == DerivedAssemblyOutcome.NoTargetChange
                        && capabilityAbsent
                        && refused
                        && modeHeld
                        && nothingPublished
                        && assemblyHeld
                        && validatorRefused
                        && membershipHeld
                        && branchHeld
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "reading=the pair's exclusive rules are denied in Conservative and granted in Automatic, so the"
                        + " Conservative->Automatic switch is the attempt that would expose the conflict (02 s5, P-014)"
                        + "; toConservative=" + toConservative
                        + "; mounts=" + firstOutcome + "/" + secondOutcome
                        + "; conflictCapabilityPublished=" + !capabilityAbsent
                        + "; admission.staged=" + admission.Staged
                        + "; admission.code=" + admission.Code
                        + "; admission.kind=" + admission.Kind
                        + "; laneMode=" + lane.Committed.Mode
                        + "; drained=" + drained.Count.ToString(CultureInfo.InvariantCulture)
                        + "; publishedBefore=" + publishedBefore
                        + "; publishedAfter=" + publishedAfter
                        + "; membershipHeld=" + membershipHeld
                        + "; branchHeld=" + branchHeld
                        + "; validator=" + validator.Describe()
                        + "; refusalDetail=" + validator.LastRefusalDetail
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 8. E: isolated branches unchanged

            /// <summary>
            /// E. Before and after every published edit of B and C the isolated target's assembly - its recipe hash
            /// and its effective capability set - is identical, it still has no row at all, and no candidate this run
            /// evaluated for it emitted anything: the boundary denied every one (P-016, P-023). A provider mounted
            /// behind the boundary may legitimately re-derive the target - the closure names it dirty and the
            /// decision set gains its `BlockedByBoundary` denial, which the oracle-equal path requires (P-026) - so
            /// locality here is the target's unchanged effective assembly, not its absence from the dirty set.
            /// </summary>
            private void ProveIsolatedUnchanged(string bareName)
            {
                try
                {
                    if (publisher == null)
                    {
                        Add(bareName, false, "no publisher");
                        return;
                    }

                    string current = IsolatedFingerprint(LastDerivation);
                    int rows = publisher.ReadBindingRows(family.IsolatedTarget).Count;
                    bool hasBinding = publisher.Published.HasBinding(
                        family.IsolatedTarget, family.DerivedCapability, 0U);

                    // P-016's invariant for the isolated branch is behavioural, not dirty-set membership: every
                    // candidate the composition reaches it with is denied by the boundary in this snapshot too, so
                    // its effective assembly cannot move. The invalidation closure may still name it dirty — a newly
                    // mounted provider behind the boundary owes the target a `BlockedByBoundary` decision, and the
                    // oracle-equal decision set is exactly that evidence (P-023, P-026) — which is why locality is
                    // asserted as "every decision this run recorded for the isolated target was denied" rather than
                    // "the closure skipped it".
                    int denied = 0;
                    int emitted = 0;
                    if (LastDerivation != null)
                    {
                        IReadOnlyList<CandidateDecision> decisions = LastDerivation.Decisions;
                        for (int i = 0; i < decisions.Count; i++)
                        {
                            if (!decisions[i].Target.Equals(family.IsolatedTarget))
                            {
                                continue;
                            }

                            if (decisions[i].Status == CandidateStatus.Emitted)
                            {
                                emitted++;
                            }
                            else
                            {
                                denied++;
                            }
                        }
                    }

                    bool boundaryHeld = emitted == 0;

                    string carriedDetail = "not-observed";
                    InvalidationClosureResult? invalidation = LastInvalidation;
                    if (invalidation != null)
                    {
                        carriedDetail = (invalidation.WholeWorld ? "whole-world" : "local")
                            + ";dirty="
                            + Contains(invalidation.DirtyTargets, family.IsolatedTarget)
                            + ";carried="
                            + invalidation.Counters.CarriedTargets.ToString(CultureInfo.InvariantCulture);
                    }

                    bool pass = current != "<no-assembly>"
                        && string.Equals(current, isolatedPrevious, StringComparison.Ordinal)
                        && string.Equals(current, isolatedBaseline, StringComparison.Ordinal)
                        && rows == 0
                        && !hasBinding
                        && boundaryHeld;

                    Add(bareName, pass,
                        "fingerprint=" + current
                        + "; baseline=" + isolatedBaseline
                        + "; previous=" + isolatedPrevious
                        + "; rows=" + rows.ToString(CultureInfo.InvariantCulture)
                        + "; hasBinding=" + hasBinding
                        + "; decisionsDenied=" + denied.ToString(CultureInfo.InvariantCulture)
                        + "; decisionsEmitted=" + emitted.ToString(CultureInfo.InvariantCulture)
                        + "; " + carriedDetail);
                    isolatedPrevious = current;
                }
                catch (Exception exception)
                {
                    Add(bareName, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 9. teardown

            private void TearDownSafely()
            {
                const string name = "gc013-teardown-settles-and-disposes";
                try
                {
                    if (host == null)
                    {
                        Add(name, false, "no world");
                        return;
                    }

                    ulong idleSteps = PumpIdleFrames();
                    if (time != null)
                    {
                        time.Clear(out int discardedCommands, out int pendingWakes);
                        _ = discardedCommands;
                        _ = pendingWakes;
                    }

                    int registryBeforeStop = UnityWorldRegistry.Count;
                    OperationResult stop = host.Stop(
                        NextOperation(host.World), "gc-013 incremental invalidation teardown");
                    UnityWorldHost stopped = host;
                    stopped.Dispose();

                    int outstanding = stopped.Ledger.OutstandingJobCount;
                    int retained = stopped.Ledger.RetainedResourceCount;
                    int registryAfter = UnityWorldRegistry.Count;

                    bool pass = idleSteps == 0UL
                        && (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && outstanding == 0
                        && retained == 0
                        && registryAfter == registryBeforeCreate
                        && registryBeforeStop == registryAfter + 1;

                    Add(name, pass,
                        "idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; stop=" + stop.Outcome + "(" + stop.Code + ")"
                        + "; outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; registryBeforeCreate=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryBeforeStop=" + registryBeforeStop.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture));
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ the control lane

            private DerivedAssemblyOutcome LastOutcome { get; set; } = DerivedAssemblyOutcome.Refused;

            private DerivationResult? LastDerivation { get; set; }

            private InvalidationClosureResult? LastInvalidation { get; set; }

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has one
            /// publication series, so an edit the world does not answer leaves the lane one publication ahead and every
            /// later adoption is refused as stale: the two halves are always done together, and a `NoTargetChange`
            /// derivation is answered with the unchanged assembly so the counters stay joined.
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string label)
            {
                if (!ApplyEdit(payload, label, out DerivedAssemblyReport report))
                {
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange
                    && publisher != null && host != null && lane != null)
                {
                    AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                        NextOperation(host.World), lane.Committed.Revision, lane.Committed.Epoch);
                    if (!unchanged.Published)
                    {
                        lastFailure = "the unchanged assembly publication was refused: " + unchanged.Detail;
                        return false;
                    }
                }

                return MatchesPublishedAssembly();
            }

            /// <summary>
            /// Applies one edit and derives for it without answering a `NoTargetChange` publication, so the next call
            /// may consume that publication number (a spawn rides it, P-024).
            /// </summary>
            private bool ApplyEdit(CompositionEditPayload payload, string label, out DerivedAssemblyReport report)
            {
                report = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.Refused };
                LastOutcome = DerivedAssemblyOutcome.Refused;
                if (lane == null || pipeline == null || host == null || publisher == null)
                {
                    lastFailure = "the world or its pipeline is missing";
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
                        + (published.Count > 0 ? published[0].Outcome.ToString() + "/" + published[0].Code : "none")
                        + ")";
                    return false;
                }

                report = pipeline.PublishDerived(NextOperation(host.World));
                LastOutcome = report.Outcome;
                LastDerivation = report.Derivation;
                LastInvalidation = report.Invalidation;
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = label + ": the world refused the assembly: " + report.Describe();
                    return false;
                }

                return true;
            }

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

            // ------------------------------------------------------------------ readings

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

            private string PublishedStateText()
            {
                if (lane == null || publisher == null || host == null)
                {
                    return "<no-world>";
                }

                return "lane=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + ",assembly=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                    + "/" + publisher.Published.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                    + ",rows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                    + ",worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture);
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

            private int LiveStateValue(TargetId target)
            {
                if (seeder == null)
                {
                    return int.MinValue;
                }

                IReadOnlyList<LiveSlotState> slots = seeder.ReadLiveSlots(new[] { target });
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot.Owner.Equals(family.MutableOwner)
                        && slots[i].Slot.Slot.Equals(family.MutableSlot))
                    {
                        return slots[i].Value;
                    }
                }

                return int.MinValue;
            }

            private int RowValueOf(TargetId target, CapabilityId capability)
            {
                if (publisher == null)
                {
                    return int.MinValue;
                }

                IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Capability.Equals(capability))
                    {
                        return rows[i].Value;
                    }
                }

                return int.MinValue;
            }

            private static TargetAssembly? AssemblyOf(DerivationResult? derivation, TargetId target)
                => derivation == null ? null : derivation.AssemblyOf(target);

            private static bool HasCapability(DerivationResult? derivation, TargetId target, CapabilityId capability)
            {
                TargetAssembly? assembly = AssemblyOf(derivation, target);
                return assembly != null && assembly.HasCapability(capability);
            }

            private static bool TryEffectiveValue(
                DerivationResult? derivation,
                TargetId target,
                CapabilityId capability,
                out int value)
            {
                value = int.MinValue;
                TargetAssembly? assembly = AssemblyOf(derivation, target);
                if (assembly == null)
                {
                    return false;
                }

                for (int i = 0; i < assembly.Slots.Count; i++)
                {
                    EffectiveSlot slot = assembly.Slots[i];
                    if (!slot.Capability.Equals(capability) || slot.IsEmpty || slot.Values.Count == 0)
                    {
                        continue;
                    }

                    return TryReadInt32(slot.Values[0], out value);
                }

                return false;
            }

            private static int EffectiveValueOf(
                DerivationResult? derivation,
                TargetId target,
                CapabilityId capability)
                => TryEffectiveValue(derivation, target, capability, out int value) ? value : int.MinValue;

            private static bool TryEffectiveProvider(
                DerivationResult? derivation,
                TargetId target,
                CapabilityId capability,
                out ProviderInstallationId provider)
            {
                provider = default(ProviderInstallationId);
                TargetAssembly? assembly = AssemblyOf(derivation, target);
                if (assembly == null)
                {
                    return false;
                }

                for (int i = 0; i < assembly.Slots.Count; i++)
                {
                    EffectiveSlot slot = assembly.Slots[i];
                    if (!slot.Capability.Equals(capability) || slot.Support.Count == 0)
                    {
                        continue;
                    }

                    provider = slot.Support[0].Provider;
                    return true;
                }

                return false;
            }

            /// <summary>
            /// Counts descriptor imports and opt-ins over the whole snapshot, so "no per-instance import was added in
            /// Automatic" is a claim about every live target rather than about the one that changed (P-013).
            /// </summary>
            private static bool CountDescriptorFacts(DerivationResult? derivation, out int imports, out int optIns)
            {
                imports = 0;
                optIns = 0;
                if (derivation == null)
                {
                    return false;
                }

                IReadOnlyList<DerivationTarget> live = derivation.Snapshot.Targets;
                for (int i = 0; i < live.Count; i++)
                {
                    imports += live[i].Descriptor.Imports.Count;
                    optIns += live[i].Descriptor.OptIns.Count;
                }

                return true;
            }

            /// <summary>Counts scope-level Conservative imports in the committed composition (P-013).</summary>
            private bool CountScopeImports(out int scopeImports)
            {
                scopeImports = 0;
                if (lane == null)
                {
                    return false;
                }

                IReadOnlyList<ScopeRecord> scopes = lane.Committed.Scopes.Scopes;
                for (int i = 0; i < scopes.Count; i++)
                {
                    if (scopes[i].Grants != null)
                    {
                        scopeImports += scopes[i].Grants.Imports.Count;
                    }
                }

                return true;
            }

            /// <summary>The isolated target's assembly identity: recipe hash plus its effective capability set.</summary>
            private string IsolatedFingerprint(DerivationResult? derivation)
            {
                TargetAssembly? assembly = AssemblyOf(derivation, family.IsolatedTarget);
                if (assembly == null)
                {
                    return "<no-assembly>";
                }

                var text = new StringBuilder();
                text.Append(assembly.RecipeHash.ToHex());
                text.Append("|capabilities=")
                    .Append(assembly.EffectiveCapabilities.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < assembly.EffectiveCapabilities.Count; i++)
                {
                    text.Append('|').Append(assembly.EffectiveCapabilities[i].Value.ToString());
                }

                text.Append("|slots=").Append(assembly.Slots.Count.ToString(CultureInfo.InvariantCulture));
                return text.ToString();
            }

            private static bool Contains(IReadOnlyList<TargetId> targets, TargetId candidate)
            {
                for (int i = 0; i < targets.Count; i++)
                {
                    if (targets[i].Equals(candidate))
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Reads one canonical big-endian int32 payload (05 section 6), the only value a derived binding row
            /// carries. A payload of another length is a miss, never a truncation.
            /// </summary>
            private static bool TryReadInt32(FrozenPayload payload, out int value)
            {
                value = 0;
                IReadOnlyList<byte> bytes = payload.Bytes;
                if (bytes.Count != 4)
                {
                    return false;
                }

                uint raw = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
                value = unchecked((int)raw);
                return true;
            }

            // ------------------------------------------------------------------ helpers

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
                steps.Add(new Gc013Step(family.Label + "/" + bareName, passed, detail ?? string.Empty));
            }

            private string DescribeFailure()
                => lastFailure.Length == 0 ? string.Empty : "; failure=" + lastFailure;

            private static string Join(IReadOnlyList<string> values)
            {
                if (values.Count == 0)
                {
                    return "<none>";
                }

                var array = new string[values.Count];
                for (int i = 0; i < values.Count; i++)
                {
                    array[i] = values[i];
                }

                return string.Join(",", array);
            }

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }
    }
}
