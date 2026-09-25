// GameCore.Validation.ProbeHost — the Wave 4 integration gate scenario.
//
// The gate sentence this file implements, verbatim from `docs/game-core/09-implementation-guide.md`:
//
//   "Run both families in IL2CPP, then integrate indexed move/mode changes, service-closure lifecycle and slot
//    policies. Demonstrate both mode directions, a subtree move preserving state, suspend/resume and provider
//    loss/unload. Provisional generic execution freeze requires GC-012; dynamic composition is still awaiting later
//    stress/fault completion."
//
// One runner, two family adapters (`IW4GateFamily`), exactly as GC-013's sequence has one runner and two adapters.
// The adapters own only what a genre declares; this file owns the one scripted sequence both genres run, so a unity
// of four Wave 4 tasks is demonstrated on one revision rather than four times in isolation:
//
//   * GC-013's indexed move/mode changes: the branch reparent and both propagation-mode directions, over the real
//     incremental engine inside `DerivedAssemblyPipeline`, with the invalidation closure reported per publication and
//     the isolated branch unchanged (P-013, P-014, P-016, P-023, P-025);
//   * GC-014's service-closure lifecycle: `LifecycleController` drives suspend, resume, the removal of a required
//     provider in the same publication that makes its consumer wait, the compatible provider's return that resumes
//     it, and an unload that runs the P-048 order and reuses the tracked `DerivationProposalBridge` publication
//     (P-011, P-012, P-046, P-047, P-048);
//   * GC-015's slot policies: `StateMigrationPipeline` executes the declared policies against copies of the live
//     slots of the same world, and the plan it returns is the plan the real publisher applies, so `Preserve`,
//     `PreserveDormant`, `RemoveDerived`, `TransferTo` and the manifest-supported `Reset` are observed in actual ECS
//     storage (P-020, P-025, P-029, P-032, P-033, P-034);
//   * GC-012's provisional generic-execution freeze: the whole sequence runs in both families from one compile, with
//     no genre type in the kernel, and reports into `-probeW4Gate` (task `W4-GATE`).
//
// The standing invariant P-006 asks for — one publication series, so the composition lane's committed
// (revision, epoch) and the world's published pair never diverge — is checked after *every* publication of this run
// and counted, and the final step asserts the count of mismatches is zero with at least one publication observed.
//
// Nothing here re-implements a kernel module and nothing is discovered reflectively. `GC-013`'s own sequence remains
// the authority on the indexed move/mode clauses and `GC-015`'s on the pure slot policies; this runner asserts the
// same clauses *again* through one integrated world because that integration is what Wave 4's exit gate is.
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
using GameCore.Planning.StatePolicies;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Lifecycle;
using GameCore.Unity.Runtime.StateMigration;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named W4-gate observation: what was checked and the values it was computed from.</summary>
    public sealed class W4GateStep
    {
        public W4GateStep(string name, bool passed, string detail)
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
    /// Full result of one W4-gate run: the named observations plus one digest over them, computed over the canonical
    /// `name=pass|fail` lines with the same digest function the narrative trace and the GC-013 result use, so the
    /// standalone probe can archive one stable literal per family and a run that records a different set of
    /// observations (or a failing one) cannot report the expected digest.
    /// </summary>
    public sealed class W4GateScenarioResult
    {
        public W4GateScenarioResult(string label, IReadOnlyList<W4GateStep> steps)
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
        public IReadOnlyList<W4GateStep> Steps { get; }

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

    /// <summary>Runs the Wave 4 integration-gate sequence over one family and one catalog.</summary>
    public static class W4GateScenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. Both families record
        /// exactly these names, so a renamed or dropped observation fails the EditMode suite and the player probe
        /// instead of shrinking them silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "w4-world-lane-and-extra-manifests",
            "w4-extra-providers-mount-onto-the-same-revision",
            "w4-automatic-inheritance-and-the-future-target",
            "w4-subtree-move-preserves-state-and-switches-binding",
            "w4-mode-automatic-to-conservative-retracts-existing-and-future",
            "w4-mode-conservative-to-automatic-restores-existing-and-future",
            "w4-suspend-retracts-and-resume-restores",
            "w4-required-provider-loss-makes-consumers-wait",
            "w4-required-provider-return-resumes-consumers",
            "w4-unload-disposes-in-reverse-acquisition-order",
            "w4-slot-preserve-keeps-the-non-default-value",
            "w4-slot-preserve-dormant-retains-without-an-active-writer",
            "w4-slot-remove-derived-drops-the-row",
            "w4-slot-transfer-to-moves-the-value-to-the-named-owner",
            "w4-slot-reset-uses-the-manifest-permission",
            "w4-lane-epoch-equals-world-epoch-throughout",
            "w4-teardown-settles-and-disposes",
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
        public static W4GateScenarioResult Run(IW4GateFamily family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        private sealed class Executor
        {
            /// <summary>Bounded temporary storage the plans of this scenario may reserve, in bytes.</summary>
            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            /// <summary>Staged lease ceiling of the scenario's plan resource gate, in bytes (P-022).</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            /// <summary>Frames a command-driven world is pumped while it must perform no simulation step (P-036).</summary>
            private const ulong IdlePumpTicks = 1000000UL;

            private const int IdlePumpFrames = 4;

            /// <summary>Leases the unload step stages on one installation, so reverse-order disposal is observable.</summary>
            private const int UnloadLeaseCount = 2;

            private readonly IW4GateFamily family;
            private readonly List<W4GateStep> steps = new List<W4GateStep>();
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
            private LifecycleController? controller;
            private StatePolicyCatalog? policyCatalog;
            private StateMigrationPipeline? policies;
            private W4GateResourceFactory? resources;

            private ulong operationSequence;
            private int registryBeforeCreate;
            private string lastFailure = string.Empty;
            private string isolatedBaseline = string.Empty;
            private string isolatedPrevious = string.Empty;

            /// <summary>Every publication this run made, counted against mismatch checks (P-006).</summary>
            private int publications;

            /// <summary>Publications after which the lane pair and the world pair disagreed; zero is the gate.</summary>
            private int counterMismatches;

            /// <summary>First mismatch, verbatim, so a failure names the publication that broke the series.</summary>
            private string firstMismatch = "<none>";


            public Executor(IW4GateFamily family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public W4GateScenarioResult Run()
            {
                CreateWorldAndLane();
                MountTheW4Providers();
                MountSecondProviderAndSpawnFutureTarget();
                MoveTheSubtree();
                SwitchMode(PropagationMode.Conservative, "w4-mode-automatic-to-conservative-retracts-existing-and-future");
                SwitchMode(PropagationMode.Automatic, "w4-mode-conservative-to-automatic-restores-existing-and-future");
                SuspendAndResume();
                LoseAndReturnTheRequiredProvider();
                UnloadInReverseOrder();
                RunEverySlotPolicy();
                CheckTheEpochInvariant();
                TearDownSafely();
                return new W4GateScenarioResult(family.Label, steps);
            }
            // ------------------------------------------------------------------ 3. inheritance and the future target

            /// <summary>
            /// The second provider is mounted at its sibling branch and one future target is spawned, both in
            /// Automatic (P-013, P-024). This is what gives the two later clauses their subject matter: the move needs
            /// a second provider for the moved branch's inherited binding to switch to, and both mode directions are
            /// claims about *existing* and *future* targets, so the future target must exist before the first switch
            /// and must be spawned in Automatic.
            /// </summary>
            private void MountSecondProviderAndSpawnFutureTarget()
            {
                const string name = "w4-automatic-inheritance-and-the-future-target";
                try
                {
                    if (host == null || lane == null || publisher == null || pipeline == null || seeder == null
                        || targets == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    bool secondProvider = PublishEdit(family.MountSecondProvider(), "mount-second-provider");

                    var inherited = new List<string>();
                    for (int i = 0; i < family.AutomaticTargets.Count; i++)
                    {
                        TargetId target = family.AutomaticTargets[i];
                        bool has = HasCapability(LastDerivation, target, family.DerivedCapability);
                        int value = EffectiveValueOf(LastDerivation, target, family.DerivedCapability);
                        if (!has || RowValueOf(target, family.DerivedCapability) != value)
                        {
                            inherited.Add(target.ToString() + "(has=" + has + ",value=" + value + ")");
                        }
                    }

                    // P-006/P-024: the spawn rides a publication whose derivation changed no target assembly, so the
                    // neutral scope creation is that publication and the spawn consumes its number.
                    bool neutralEdit = ApplyEdit(
                        family.SpareScopeEdits[0], "spare-scope-before-spawn", out DerivedAssemblyReport forward);
                    bool forwardClear = forward.Outcome == DerivedAssemblyOutcome.NoTargetChange;

                    family.PrepareSpawn();
                    DerivedAssemblyReport spawn = pipeline.PublishSpawn(
                        NextOperation(host.World), family.FutureTarget, family.FutureRecipe, family.FutureScope);
                    bool futureRegistered = targets.TryRegister(
                        family.FutureTarget,
                        family.FutureScope,
                        family.FutureRecipe,
                        out DiagnosticCode registerCode,
                        out string registerDetail);
                    int futureRows = publisher.ReadBindingRows(family.FutureTarget).Count;
                    int futureValue = EffectiveValueOf(spawn.Derivation, family.FutureTarget, family.DerivedCapability);
                    bool futurePresent = publisher.Published.Bindings.HasTarget(family.FutureTarget);
                    InvalidationClosureResult? invalidation = spawn.Invalidation;
                    bool joined = MatchesPublishedAssembly();
                    NotePublication("spawn-future-target");

                    bool pass = secondProvider
                        && inherited.Count == 0
                        && neutralEdit
                        && forwardClear
                        && futureRegistered
                        && futurePresent
                        && spawn.Outcome == DerivedAssemblyOutcome.Published
                        && spawn.IsSpawn
                        && spawn.CountersJoined
                        && futureRows > 0
                        && futureValue == family.ProviderValue
                        && joined;

                    Add(name, pass,
                        "secondProviderEdit=" + secondProvider
                        + "; eligibleTargets=" + family.AutomaticTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; dissenting=" + Join(inherited)
                        + "; neutralEdit=" + neutralEdit
                        + "; forwardOutcome=" + forward.Outcome
                        + "; spawnOutcome=" + spawn.Outcome
                        + "; isSpawn=" + spawn.IsSpawn
                        + "; joined=" + spawn.CountersJoined
                        + "; registered=" + futureRegistered
                        + (futureRegistered ? string.Empty : "(" + registerCode + ": " + registerDetail + ")")
                        + "; futureTarget=" + family.FutureTarget
                        + "(rows=" + futureRows.ToString(CultureInfo.InvariantCulture)
                        + ",value=" + futureValue.ToString(CultureInfo.InvariantCulture)
                        + ",inView=" + futurePresent + ")"
                        + "; closure=" + DescribeInvalidation(invalidation)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 5. both mode directions

            /// <summary>
            /// One mode direction (P-013, P-014). In Conservative the only target that keeps the binding is the one
            /// whose descriptor declares the complete explicit opt-in; in Automatic every automatically eligible
            /// target carries it again, the future target included, and no import was added anywhere. Both directions
            /// are asserted over the *same* targets: the existing automatically eligible set and the future target
            /// spawned before the first switch, so neither direction can pass by having no subject.
            /// </summary>
            private void SwitchMode(PropagationMode destination, string name)
            {
                try
                {
                    if (host == null || lane == null || publisher == null || targets == null || validator == null)
                    {
                        Add(name, false, "the world or its lane is missing");
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

                    // The future target follows the published mode exactly like an existing one, which is why it was
                    // spawned before this switch (P-013, P-024).
                    bool futureRegistered = targets.Contains(family.FutureTarget);
                    bool futureFollows = false;
                    string futureDetail = "not-registered";
                    if (futureRegistered)
                    {
                        bool has = HasCapability(derivation, family.FutureTarget, family.DerivedCapability);
                        int rows = publisher.ReadBindingRows(family.FutureTarget).Count;
                        int value = EffectiveValueOf(derivation, family.FutureTarget, family.DerivedCapability);
                        futureFollows = automatic
                            ? has && rows > 0 && value == family.ProviderValue
                            : !has && rows == 0;
                        futureDetail = "has=" + has + ",rows=" + rows + ",value=" + value;
                    }

                    bool optInKept = HasCapability(derivation, family.OptedInTarget, family.DerivedCapability)
                        && EffectiveValueOf(derivation, family.OptedInTarget, family.DerivedCapability)
                            == family.ProviderValue
                        && RowValueOf(family.OptedInTarget, family.DerivedCapability) == family.ProviderValue;
                    bool isolatedClear = !HasCapability(derivation, family.IsolatedTarget, family.DerivedCapability)
                        && publisher.ReadBindingRows(family.IsolatedTarget).Count == 0;
                    bool ineligibleClear = !HasCapability(derivation, family.IneligibleTarget, family.DerivedCapability)
                        && publisher.ReadBindingRows(family.IneligibleTarget).Count == 0;
                    bool factsRead = CountDescriptorFacts(derivation, out int imports, out int optIns);
                    bool scopeImportsRead = CountScopeImports(out int scopeImports);

                    bool pass = published
                        && LastOutcome == DerivedAssemblyOutcome.Published
                        && lane.Committed.Mode == destination
                        && before != destination
                        && dissenting.Count == 0
                        && futureRegistered
                        && futureFollows
                        && optInKept
                        && isolatedClear
                        && ineligibleClear
                        && factsRead
                        && imports == 0
                        && optIns == 1
                        && scopeImportsRead
                        && scopeImports == 0
                        && MatchesPublishedAssembly();

                    // The isolated branch's assembly identity is captured before the first switch and re-checked
                    // after every publication of both directions (P-016, P-023).
                    string isolatedNow = IsolatedFingerprint(derivation);
                    bool isolatedHeld = string.Equals(isolatedNow, isolatedBaseline, StringComparison.Ordinal);
                    isolatedPrevious = isolatedNow;

                    Add(name, pass && isolatedHeld,
                        "mode=" + before + "->" + lane.Committed.Mode
                        + "; destination=" + destination
                        + "; outcome=" + LastOutcome
                        + "; eligibleTargets=" + family.AutomaticTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; dissenting=" + Join(dissenting)
                        + "; futureTarget=" + family.FutureTarget + "(" + futureDetail + ")"
                        + "; optInKept=" + optInKept
                        + "; isolatedHeld=" + isolatedHeld
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


            // ------------------------------------------------------------------ 1. the integrated world

            /// <summary>
            /// Builds the one real world of this run: the family's generated catalog and declarations, the live
            /// targets, the control lane over a manifest source that also resolves the four extra lifecycle manifests
            /// and the state-policy provider, the world composition bridge, the incremental derived-assembly chain
            /// (GC-013), the compiled schedule with its time driver (GC-009), and GC-014's `LifecycleController` over
            /// the same three seams. This is the integration the wave exit gate is about: every Wave 4 module is
            /// present in one process on one revision.
            /// </summary>
            private void CreateWorldAndLane()
            {
                const string name = "w4-world-lane-and-extra-manifests";
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

                    registry = new TargetRegistry(world, 32);
                    resources = new W4GateResourceFactory(new IdSequence(family.SessionSalt ^ 0x573447415445UL));
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        family.CreateRecipes(),
                        family.CreateMigrations(),
                        descriptorReport.Descriptor);

                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    // The lane's manifest source resolves the catalog's declarations *and* this gate's four lifecycle
                    // manifests and state-policy provider: the pool is the union of both, so an installation the
                    // sequence mounts late resolves its own declaration at the publication that mounts it (P-009).
                    var catalogSource = new CatalogManifestSource(family.Catalog, family.Declarations);
                    var manifestSource = new W4GateManifestSource(catalogSource, family.ExtraManifests);

                    // One registered value source serves the mode-switch validator, the derivation pipeline and the
                    // state-policy pass, so every identity the lane checks is the one the world derives with.
                    IDerivationValueSource valueSource = family.CreateValues();
                    validator = new DerivationModeSwitchValidator(valueSource, TargetView);

                    lane = CompositionHost.CreateDefault(
                        world,
                        family.WorldRootScope,
                        manifestSource,
                        resources,
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
                        DefaultBudget());

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    // GC-015's policy surface of this revision, built from the very manifests the lane resolves, so a
                    // declared reset permission is the manifest's field and not an override (P-032, GC-012).
                    policyCatalog = StatePolicyCatalog.Build(
                        ManifestSet(),
                        family.PolicyMigrations.Migrations,
                        family.InitialValues);
                    policies = new StateMigrationPipeline(host, publisher, seeder, policyCatalog, DefaultBudget());

                    // The controller owns the one lifecycle binding of this world and must exist before the first
                    // publication, because a lane that has already published cannot swap its binding (P-030).
                    controller = new LifecycleController(host, lane, publisher, pipeline);

                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));

                    // The policy cases' live rows are seeded before anything publishes, so the very first policy pass
                    // acts on state the world owns rather than on a value an assertion invented (P-032).
                    bool casesSeeded = true;
                    for (int i = 0; i < family.SlotCases.Count; i++)
                    {
                        casesSeeded &= family.SeedSlotCase(family.SlotCases[i], seeder);
                    }

                    ulong idleSteps = PumpIdleFrames();
                    bool joined = MatchesPublishedAssembly();

                    bool pass = seeded
                        && casesSeeded
                        && family.SlotCases.Count == 5
                        && policyCatalog.Succeeded
                        && policyCatalog.Policies != null
                        && policyCatalog.Policies.Count >= 5
                        && policyCatalog.SlotPolicyResults.Count == policyCatalog.Policies.Count
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentEpoch.Equals(AssemblyEpoch.First)
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && host.Lifecycle == WorldLifecycleState.Running
                        && idleSteps == 0UL
                        && joined;

                    Add(name, pass,
                        "session=" + world.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; declaredSlots=" + policyCatalog.Policies!.Count.ToString(CultureInfo.InvariantCulture)
                        + "; slotCases=" + family.SlotCases.Count.ToString(CultureInfo.InvariantCulture)
                        + "; extraManifests=" + family.ExtraManifests.Count.ToString(CultureInfo.InvariantCulture)
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + lane.Committed.Mode
                        + "; lifecycle=" + host.Lifecycle
                        + "; idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joined
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 2. the Wave 4 providers

            /// <summary>
            /// Mounts every installation this gate's own half needs, each through the family's own payload builder:
            /// the family's lifecycle provider (GC-014's suspend/resume subject and the derivation source of the move
            /// and mode clauses), the state-policy host, the required-service consumer and its provider, and the
            /// unload installation. One publication per mount, because P-006 has one publication series and every
            /// mount is a real composition revision (O-03).
            /// </summary>
            private void MountTheW4Providers()
            {
                const string name = "w4-extra-providers-mount-onto-the-same-revision";
                try
                {
                    if (host == null || lane == null || pipeline == null || publisher == null || controller == null)
                    {
                        Add(name, false, "the world or its pipeline is missing");
                        return;
                    }

                    bool provider = PublishEdit(
                        family.MountInstall(
                            family.LifecycleProviderManifest,
                            family.LifecycleProviderInstall,
                            family.LifecycleProviderScope),
                        "mount-lifecycle-provider");
                    bool policyHost = PublishEdit(family.MountStatePolicyHost(), "mount-state-policy-host");
                    bool consumer = PublishEdit(
                        family.MountInstall(
                            family.RequiredConsumerManifest,
                            family.RequiredConsumerInstall,
                            family.RequiredConsumerScope),
                        "mount-required-consumer");
                    bool requiredProvider = PublishEdit(
                        family.MountInstall(
                            family.RequiredProviderManifest,
                            family.RequiredProviderInstall,
                            family.RequiredProviderScope),
                        "mount-required-provider");
                    bool unload = PublishEdit(
                        family.MountInstall(family.UnloadManifest, family.UnloadInstall, family.UnloadScope),
                        "mount-unload-installation");

                    int providerRows = AttributedRows(family.RequiredProviderInstall);
                    int consumerRows = AttributedRows(family.RequiredConsumerInstall);
                    bool lifecycleProviderRows = AttributedRows(family.LifecycleProviderInstall) > 0;

                    // The consumer really declares the contract as a REQUIRED dependency and really resolved it: its
                    // manifest names a ServiceDependency on the pair's contract, and the publication gave it a
                    // binding to the provider's export (P-011, P-012). Neither is assumed from the edit's success.
                    bool consumerResolved = false;
                    if (lane.Committed.TryGetInstall(family.RequiredConsumerInstall, out InstallEntry? consumerEntry)
                        && consumerEntry != null)
                    {
                        IReadOnlyList<ServiceDependency> declared = consumerEntry.Manifest.ServiceDependencies;
                        for (int i = 0; i < declared.Count; i++)
                        {
                            if (declared[i].Required && declared[i].Contract.ContractId.Equals(family.RequiredService.ContractId))
                            {
                                consumerResolved = true;
                                break;
                            }
                        }

                        consumerResolved &= consumerEntry.Bindings.Count > 0;
                    }

                    // The policy surface is a second real installation of this revision, mounted at its declared
                    // scope, so the gate's five policy slots belong to a mounted provider rather than to a manifest
                    // the lane never resolved (P-009).
                    bool policyHostActive = StateOf(family.StatePolicyInstall) == InstallationState.Active;
                    bool policyHostScoped = lane.Committed.TryGetInstall(
                            family.StatePolicyInstall, out InstallEntry? policyEntry)
                        && policyEntry != null
                        && policyEntry.Scope.Equals(family.StatePolicyScope);

                    bool pass = provider
                        && policyHost
                        && consumer
                        && requiredProvider
                        && unload
                        && consumerActive
                        && providerActive
                        && consumerResolved
                        && providerRows > 0
                        && consumerRows > 0
                        && lifecycleProviderRows
                        && policyHostActive
                        && policyHostScoped
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "providerEdit=" + provider
                        + "; policyHost=" + policyHost
                        + "; consumerEdit=" + consumer
                        + "; requiredProviderEdit=" + requiredProvider
                        + "; unloadEdit=" + unload
                        + "; consumerState=" + StateOf(family.RequiredConsumerInstall)
                        + "; providerState=" + StateOf(family.RequiredProviderInstall)
                        + "; consumerResolved=" + consumerResolved
                        + "; providerRows=" + providerRows.ToString(CultureInfo.InvariantCulture)
                        + "; consumerRows=" + consumerRows.ToString(CultureInfo.InvariantCulture)
                        + "; policyHostState=" + StateOf(family.StatePolicyInstall)
                        + "; policyHostScoped=" + policyHostScoped
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }


            // ------------------------------------------------------------------ 4. the move

            /// <summary>
            /// The subtree move (P-010, P-025). A branch's scope moves under another branch while the run is in
            /// Conservative, and the moved target keeps its identity, its owner scope, its seeded live value and the
            /// complete inherited binding set it had before the move. The isolated branch is compared byte-for-byte
            /// against the baseline captured before the switch, which is the locality half of P-023/P-016.
            /// </summary>
            private void MoveTheSubtree()
            {
                const string name = "w4-subtree-move-preserves-state-and-switches-binding";
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
                    string shapeBefore = BindingShape(family.MovedTarget);
                    int valueBefore = EffectiveValueOf(LastDerivation, family.MovedTarget, family.DerivedCapability);
                    string isolatedBefore = IsolatedFingerprint(LastDerivation);
                    ulong epochBefore = host.CurrentEpoch.Value;

                    bool published = PublishEdit(family.ScopeReparent(), "scope-reparent");
                    DerivationResult? derivation = LastDerivation;

                    TargetAssembly? assembly = AssemblyOf(derivation, family.MovedTarget);
                    bool identityHeld = assembly != null
                        && assembly.Target.Equals(family.MovedTarget)
                        && assembly.Scope.Equals(scopeBefore);
                    int stateAfter = LiveStateValue(family.MovedTarget);
                    string shapeAfter = BindingShape(family.MovedTarget);
                    int valueAfter = EffectiveValueOf(derivation, family.MovedTarget, family.DerivedCapability);

                    // The switched binding: the moved target's derived capability is now supported by the second
                    // provider, named by that provider's own installation (P-017, P-025).
                    bool providerIsSecond = TryEffectiveProvider(
                            derivation, family.MovedTarget, family.DerivedCapability, out ProviderInstallationId provider)
                        && provider.Value.Equals(family.SecondProviderInstance.Value);
                    string isolatedAfter = IsolatedFingerprint(derivation);

                    ScopeRecord? moved = null;
                    bool parentMoved = lane.Committed.Scopes.TryGet(family.MovedScope, out moved)
                        && moved != null
                        && moved.Parent.Equals(family.MoveDestination);

                    // P-025's closure diff: the move is reported as a change of scope facts, and the moved targets
                    // really were re-derived rather than carried, because their reaching provider changed.
                    InvalidationClosureResult? invalidation = LastInvalidation;
                    bool closureReported = invalidation != null
                        && (invalidation.WholeWorld || invalidation.DirtyTargets.Count > 0);
                    bool movedTargetDirty = invalidation != null && Contains(invalidation.DirtyTargets, family.MovedTarget);

                    bool pass = published
                        && LastOutcome == DerivedAssemblyOutcome.Published
                        && identityHeld
                        && parentMoved
                        && stateBefore == family.MutableValue
                        && stateAfter == family.MutableValue
                        && string.Equals(shapeBefore, shapeAfter, StringComparison.Ordinal)
                        && valueBefore > 0
                        && valueAfter > 0
                        && providerIsSecond
                        && string.Equals(isolatedBefore, isolatedAfter, StringComparison.Ordinal)
                        && string.Equals(isolatedAfter, isolatedBaseline, StringComparison.Ordinal)
                        && closureReported
                        && movedTargetDirty
                        && host.CurrentEpoch.Value > epochBefore
                        && MatchesPublishedAssembly();

                    isolatedPrevious = isolatedAfter;

                    Add(name, pass,
                        "published=" + published
                        + "; outcome=" + LastOutcome
                        + "; movedTarget=" + family.MovedTarget
                        + "; scope=" + scopeBefore + "->parent=" + family.MoveDestination
                        + "; identityHeld=" + identityHeld
                        + "; liveState=" + stateBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + stateAfter.ToString(CultureInfo.InvariantCulture)
                        + "; expectedState=" + family.MutableValue.ToString(CultureInfo.InvariantCulture)
                        + "; shape=" + shapeAfter
                        + "; shapeHeld=" + string.Equals(shapeBefore, shapeAfter, StringComparison.Ordinal)
                        + "; effectiveValue=" + valueBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + valueAfter.ToString(CultureInfo.InvariantCulture)
                        + "; supportProviderIsSecond=" + providerIsSecond
                        + "; closure=" + DescribeInvalidation(invalidation)
                        + "; epoch=" + epochBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 6. suspend and resume

            /// <summary>
            /// GC-014's suspend and resume (O-06, O-04). A suspend retracts the installation's attributed rows,
            /// retires its activation so a token minted before the suspend is discarded at completion, and closes its
            /// ingress; the resume re-derives against the current ancestry, restores exactly the rows the suspend
            /// retracted, and a fresh token dispatches again. The same publication carries both halves of the commit,
            /// so the observation is about one publication rather than about two (P-006, P-046, P-047).
            /// </summary>
            private void SuspendAndResume()
            {
                const string name = "w4-suspend-retracts-and-resume-restores";
                try
                {
                    if (lane == null || publisher == null || controller == null || host == null)
                    {
                        Add(name, false, "the world or its controller is missing");
                        return;
                    }

                    PluginInstanceId provider = family.LifecycleProviderInstall;
                    int rowsBefore = AttributedRows(provider);
                    bool epochKnown = lane.Committed.TryGetInstall(provider, out InstallEntry? entry) && entry != null;
                    ulong epochBefore = epochKnown ? entry!.Record.ActivationEpoch.Value : 0UL;
                    InstallationGeneration generationBefore =
                        epochKnown ? entry!.Record.Generation : default(InstallationGeneration);
                    int liveActivationsBefore = lane.Callbacks.LiveActivationCount;

                    OperationId suspendOperation = NextOperation(host.World);
                    LifecycleRequestReport suspend = controller.Submit(
                        family.SuspendInstall(provider), suspendOperation);
                    int rowsAfterSuspend = AttributedRows(provider);
                    string suspendState = StateOf(provider);
                    bool holdsAuthorityAfterSuspend = lane.Lifecycle.HoldsAuthority(provider);
                    int liveActivationsAfter = lane.Callbacks.LiveActivationCount;

                    // P-047: a completion that was in flight when the suspend happened is discarded, not delivered
                    // into an installation without authority. Evaluated with the pre-suspend token.
                    CallbackGateDecision lateDecision = CallbackGateDecision.Dispatch;
                    if (epochBefore != 0UL)
                    {
                        lateDecision = lane.Lifecycle.EvaluateCompletion(
                            new AsyncWorkToken(
                                suspendOperation, provider, generationBefore, new ActivationEpoch(epochBefore), 1U));
                    }

                    bool lateDiscarded = lateDecision == CallbackGateDecision.DiscardRetiredRoute
                        || lateDecision == CallbackGateDecision.DiscardStaleActivation;
                    bool retracted = suspend.Lifecycle != null
                        && rowsAfterSuspend == 0
                        && rowsBefore > 0;

                    OperationId resumeOperation = NextOperation(host.World);
                    LifecycleRequestReport resume = controller.Submit(
                        family.ResumeInstall(provider), resumeOperation);
                    int rowsAfterResume = AttributedRows(provider);
                    string resumeState = StateOf(provider);
                    bool holdsAuthorityAfterResume = lane.Lifecycle.HoldsAuthority(provider);
                    bool rowsRestored = rowsAfterResume == rowsBefore && rowsBefore > 0;

                    bool freshDispatches = false;
                    if (lane.Committed.TryGetInstall(provider, out InstallEntry? resumed) && resumed != null)
                    {
                        freshDispatches = lane.Lifecycle.EvaluateCompletion(
                            new AsyncWorkToken(
                                resumeOperation,
                                provider,
                                resumed.Record.Generation,
                                resumed.Record.ActivationEpoch,
                                1U)) == CallbackGateDecision.Dispatch;
                    }

                    bool pass = suspend.Succeeded
                        && resume.Succeeded
                        && suspendState == InstallationState.Suspended.ToString()
                        && !holdsAuthorityAfterSuspend
                        && liveActivationsAfter < liveActivationsBefore
                        && retracted
                        && lateDiscarded
                        && resumeState == InstallationState.Active.ToString()
                        && holdsAuthorityAfterResume
                        && rowsRestored
                        && freshDispatches
                        && suspend.Derived != null
                        && resume.Derived != null
                        && suspend.Derived.Outcome != DerivedAssemblyOutcome.Refused
                        && resume.Derived.Outcome != DerivedAssemblyOutcome.Refused
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "suspendState=" + suspendState
                        + "; suspendCode=" + suspend.Code
                        + "; holdsAuthority=" + holdsAuthorityAfterSuspend
                        + "; liveActivations=" + liveActivationsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + liveActivationsAfter.ToString(CultureInfo.InvariantCulture)
                        + "; rows=" + rowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + rowsAfterSuspend.ToString(CultureInfo.InvariantCulture)
                        + "; lateCompletion=" + lateDecision
                        + "; resumeState=" + resumeState
                        + "; resumeCode=" + resume.Code
                        + "; rowsAfterResume=" + rowsAfterResume.ToString(CultureInfo.InvariantCulture)
                        + "; freshToken=" + (freshDispatches ? CallbackGateDecision.Dispatch.ToString() : "<discarded>")
                        + "; suspendDerived=" + Describe(suspend.Derived)
                        + "; resumeDerived=" + Describe(resume.Derived)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7. provider loss and return

            /// <summary>
            /// GC-014's service closure (O-07 then O-03, P-011, P-012). Removing the required provider and making its
            /// consumer wait happen in the *same* publication: the consumer lands in `WaitingForDependencies` with
            /// its bindings retracted and is named by that publication's `WaitingConsumers`. A compatible provider's
            /// return puts it back to `Active` with its bindings restored and names it in `ResumedConsumers`.
            /// </summary>
            private void LoseAndReturnTheRequiredProvider()
            {
                const string lossName = "w4-required-provider-loss-makes-consumers-wait";
                const string returnName = "w4-required-provider-return-resumes-consumers";
                try
                {
                    if (lane == null || publisher == null || controller == null || host == null)
                    {
                        Add(lossName, false, "the world or its controller is missing");
                        Add(returnName, false, "the world or its controller is missing");
                        return;
                    }

                    int consumerRowsBefore = AttributedRows(family.RequiredConsumerInstall);
                    int consumerBindingsBefore = BindingCount(family.RequiredConsumerInstall);

                    OperationId lossOperation = NextOperation(host.World);
                    LifecycleRequestReport loss = controller.Submit(
                        family.UnmountInstall(family.RequiredProviderInstall), lossOperation);
                    string consumerStateAfterLoss = StateOf(family.RequiredConsumerInstall);
                    int consumerBindingsAfterLoss = BindingCount(family.RequiredConsumerInstall);
                    int consumerRowsAfterLoss = AttributedRows(family.RequiredConsumerInstall);
                    PublishedOperation? lossPublication = FindPublished(loss, lossOperation);
                    bool namedWaiting = lossPublication != null
                        && NamesExactly(lossPublication.WaitingConsumers, family.RequiredConsumerInstall);

                    bool lossPass = loss.Succeeded
                        && consumerStateAfterLoss == InstallationState.WaitingForDependencies.ToString()
                        && namedWaiting
                        && consumerBindingsAfterLoss == 0
                        && consumerBindingsBefore > 0
                        && consumerRowsBefore > 0
                        && consumerRowsAfterLoss == 0
                        && loss.Derived != null
                        && loss.Derived.Outcome != DerivedAssemblyOutcome.Refused
                        && MatchesPublishedAssembly();

                    Add(lossName, lossPass,
                        "consumerState=" + consumerStateAfterLoss
                        + "; waitingConsumers=" + (lossPublication != null
                            ? lossPublication.WaitingConsumers.Count.ToString(CultureInfo.InvariantCulture)
                            : "<no-publication>")
                        + "; namedExactly=" + namedWaiting
                        + "; consumerBindings=" + consumerBindingsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + consumerBindingsAfterLoss.ToString(CultureInfo.InvariantCulture)
                        + "; consumerRows=" + consumerRowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + consumerRowsAfterLoss.ToString(CultureInfo.InvariantCulture)
                        + "; providerState=" + StateOf(family.RequiredProviderInstall)
                        + "; derived=" + Describe(loss.Derived)
                        + DescribeFailure());

                    OperationId returnOperation = NextOperation(host.World);
                    LifecycleRequestReport returned = controller.Submit(
                        family.MountInstall(
                            family.RequiredProviderReplacementManifest,
                            family.RequiredProviderReplacementInstall,
                            family.RequiredProviderScope),
                        returnOperation);
                    string consumerStateAfterReturn = StateOf(family.RequiredConsumerInstall);
                    int consumerBindingsAfterReturn = BindingCount(family.RequiredConsumerInstall);
                    int consumerRowsAfterReturn = AttributedRows(family.RequiredConsumerInstall);
                    PublishedOperation? returnPublication = FindPublished(returned, returnOperation);
                    bool namedResumed = returnPublication != null
                        && NamesExactly(returnPublication.ResumedConsumers, family.RequiredConsumerInstall);

                    bool returnPass = returned.Succeeded
                        && consumerStateAfterReturn == InstallationState.Active.ToString()
                        && namedResumed
                        && consumerBindingsAfterReturn == consumerBindingsBefore
                        && consumerRowsAfterReturn == consumerRowsBefore
                        && returned.Derived != null
                        && returned.Derived.Outcome != DerivedAssemblyOutcome.Refused
                        && MatchesPublishedAssembly();

                    Add(returnName, returnPass,
                        "consumerState=" + consumerStateAfterReturn
                        + "; resumedConsumers=" + (returnPublication != null
                            ? returnPublication.ResumedConsumers.Count.ToString(CultureInfo.InvariantCulture)
                            : "<no-publication>")
                        + "; namedExactly=" + namedResumed
                        + "; consumerBindings=" + consumerBindingsAfterLoss.ToString(CultureInfo.InvariantCulture)
                        + "->" + consumerBindingsAfterReturn.ToString(CultureInfo.InvariantCulture)
                        + "; consumerRows=" + consumerRowsAfterLoss.ToString(CultureInfo.InvariantCulture)
                        + "->" + consumerRowsAfterReturn.ToString(CultureInfo.InvariantCulture)
                        + "; expectedRows=" + consumerRowsBefore.ToString(CultureInfo.InvariantCulture)
                        + "; replacementState=" + StateOf(family.RequiredProviderReplacementInstall)
                        + "; derived=" + Describe(returned.Derived)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(lossName, false, DescribeException(exception));
                    Add(returnName, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 8. unload with reverse disposal

            /// <summary>
            /// GC-014's unload (O-07). Two leases are staged on the gate's unload installation in acquisition order,
            /// then the P-048 order runs: ingress closes, the step settles, work is fenced, contributions are
            /// retracted, the leases are retired in reverse acquisition order, and a token minted before the unload
            /// is discarded. The installation ends `Disposed` with no retained reference, and the order the factory
            /// observed is asserted to be the exact reverse of the acquisition order (P-007, P-047, P-048).
            /// </summary>
            private void UnloadInReverseOrder()
            {
                const string name = "w4-unload-disposes-in-reverse-acquisition-order";
                try
                {
                    if (host == null || lane == null || controller == null || resources == null)
                    {
                        Add(name, false, "the world or its controller is missing");
                        return;
                    }

                    PluginInstanceId instance = family.UnloadInstall;
                    OperationId mountOperation = NextOperation(host.World);
                    EditAdmission admission = lane.SubmitEdit(
                        family.MountInstall(family.UnloadManifest, instance, family.UnloadScope),
                        mountOperation,
                        lane.Committed.Revision);
                    if (!admission.Staged)
                    {
                        Add(name, false, "the unload installation's mount was refused by the lane: " + admission.Kind);
                        return;
                    }

                    var leaseIds = new List<Id128>(UnloadLeaseCount);
                    bool stagedAll = true;
                    for (int i = 0; i < UnloadLeaseCount; i++)
                    {
                        var key = new ResourceKey(new Id128(family.SessionSalt, (ulong)(i + 1)));
                        bool staged = lane.StageResource(
                            mountOperation,
                            instance,
                            key,
                            new FrozenPayload(new byte[] { (byte)i }),
                            null,
                            out DiagnosticCode leaseCode);
                        if (!staged)
                        {
                            lastFailure = "lease " + i.ToString(CultureInfo.InvariantCulture)
                                + " was refused: " + DiagnosticCodeText.Of(leaseCode);
                            stagedAll = false;
                            break;
                        }

                    }

                    IReadOnlyList<StagedLease> stagedLeases = lane.StagedLeases(mountOperation);
                    for (int i = 0; i < stagedLeases.Count; i++)
                    {
                        leaseIds.Add(stagedLeases[i].LeaseId);
                    }

                    bool joined = PublishDrainAndDerive(mountOperation, out DerivedAssemblyReport mountDerived);
                    bool published = mountDerived.Outcome == DerivedAssemblyOutcome.Published
                        || mountDerived.Outcome == DerivedAssemblyOutcome.NoTargetChange;

                    int acquired = leaseIds.Count;
                    int disposalCursor = resources.DisposedSequence.Cursor;
                    int disposedBefore = resources.DisposeCount;

                    TeardownReport teardown = controller.Unload(instance, NextOperation(host.World));

                    List<Id128> disposed = resources.DisposedSequence.Since(disposalCursor);
                    bool reverseOrder = disposed.Count == acquired && IsReverseOf(disposed, leaseIds);
                    bool tokenDiscarded = false;
                    if (lane.Committed.TryGetInstall(instance, out InstallEntry? entry) && entry != null)
                    {
                        tokenDiscarded = lane.Lifecycle.EvaluateCompletion(
                            new AsyncWorkToken(
                                teardown.Operation,
                                instance,
                                entry.Record.Generation,
                                entry.Record.ActivationEpoch,
                                1U)) != CallbackGateDecision.Dispatch;
                    }

                    bool sixSteps = teardown.Steps.Count == 6;
                    bool pass = joined
                        && published
                        && stagedAll
                        && acquired == UnloadLeaseCount
                        && teardown.Code == DiagnosticCode.None
                        && teardown.DisposeSettled
                        && teardown.Quarantined.Count == 0
                        && teardown.FailedReleases.Count == 0
                        && sixSteps
                        && resources.DisposeCount == disposedBefore + acquired
                        && reverseOrder
                        && tokenDiscarded
                        && StateOf(instance) == InstallationState.Disposed.ToString()
                        && lane.Lifecycle.RetainedCountFor(instance) == 0
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "joined=" + joined
                        + "; mountOutcome=" + mountDerived.Outcome
                        + "; stagedLeases=" + acquired.ToString(CultureInfo.InvariantCulture)
                        + "; teardownCode=" + teardown.Code
                        + "; disposeSettled=" + teardown.DisposeSettled
                        + "; steps=" + teardown.Steps.Count.ToString(CultureInfo.InvariantCulture)
                        + "; quarantined=" + teardown.Quarantined.Count.ToString(CultureInfo.InvariantCulture)
                        + "; failedReleases=" + teardown.FailedReleases.Count.ToString(CultureInfo.InvariantCulture)
                        + "; acquired=" + DescribeIds(leaseIds)
                        + "; disposed=" + DescribeIds(disposed)
                        + "; reverseOrder=" + reverseOrder
                        + "; lateCompletion=" + (tokenDiscarded ? "discarded" : "dispatched")
                        + "; state=" + StateOf(instance)
                        + "; retained=" + lane.Lifecycle.RetainedCountFor(instance).ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 9. every slot policy

            /// <summary>
            /// GC-015's slot policies in the live world (P-020, P-025, P-029, P-032, P-033). One policy pass runs
            /// over copies of the seeded live slots and its plan is the plan the real publisher applies, so each
            /// disposition is observed in actual ECS storage: `Preserve` leaves the non-default value, `PreserveDormant`
            /// retains it with no active writer, `RemoveDerived` drops the row, `TransferTo` moves the value to the
            /// declared destination owner and retires the source, and `Reset` writes the declared initialization value
            /// *because the manifest declares reset support* — the GC-012 field, not a test-declared override.
            /// </summary>
            private void RunEverySlotPolicy()
            {
                try
                {
                    if (host == null || lane == null || publisher == null || policies == null || seeder == null)
                    {
                        for (int i = 0; i < family.SlotCases.Count; i++)
                        {
                            Add(PolicyStepName(family.SlotCases[i]), false, "the world or its policy pipeline is missing");
                        }

                        return;
                    }

                    // One policy pass per case, each published by its own composition publication: the lane advances on
                    // a scope no target lives in, and the plan the world applies carries the case's disposition. A
                    // separate publication per case is what makes "this disposition caused this storage change"
                    // checkable (P-006, P-029).
                    for (int i = 0; i < family.SlotCases.Count; i++)
                    {
                        RunOneSlotCase(i, family.SlotCases[i]);
                    }
                }
                catch (Exception exception)
                {
                    for (int i = 0; i < family.SlotCases.Count; i++)
                    {
                        Add(PolicyStepName(family.SlotCases[i]), false, DescribeException(exception));
                    }
                }
            }

            private void RunOneSlotCase(int index, W4GateSlotCase slotCase)
            {
                try
                {
                    StateSlotKey slot = slotCase.Slot;
                    bool beforeRead = ReadSlot(slot, out int valueBefore, out uint versionBefore);
                    bool writerBefore = policies!.HasActiveWriter(slot);
                    StateSlotKey destination = slotCase.Destination;

                    // P-004/P-032: the falsification half runs first, against the identical slot declaration *without*
                    // the manifest's reset support (see UndeclaredResetIsRefused), so "the manifest field is what
                    // authorizes this reset" cannot be mistaken for "any reset succeeds".
                    {
                        bool refused = UndeclaredResetIsRefused(slot, out DiagnosticCode refusedCode);
                        if (!refused)
                        {
                            Add(name, false,
                                "an undeclared reset of " + slot.ToString() + " was not refused (code="
                                + refusedCode + "); P-032 makes an unpermitted reset an OwnershipConflict.");
                            return;
                        }
                    }

                    bool published = PublishPolicyCase(index, slotCase, out DerivedAssemblyReport derived, out StatePolicyPlan? plan);
                    bool afterRead = ReadSlot(slot, out int valueAfter, out uint versionAfter);
                    bool destinationRead = ReadSlot(destination, out int destinationValue, out uint destinationVersion);
                    bool writerAfter = policies.HasActiveWriter(slot);
                    bool dormantRecorded = policies.Dormant.TryGet(slot, out DormantSlotRecord dormant)
                        && dormant.RetainedValue == valueBefore
                        && dormant.SchemaVersion == versionBefore;

                    bool pass;
                    string expectation;
                    switch (slotCase.Policy)
                    {
                        case W4GateSlotPolicy.Preserve:
                            // P-020/P-032: the value and its version are exactly what they were.
                            expectation = "preserved";
                            pass = published
                                && beforeRead
                                && afterRead
                                && valueBefore == slotCase.Value
                                && valueAfter == slotCase.Value
                                && versionAfter == versionBefore
                                && writerBefore
                                && writerAfter;
                            break;

                        case W4GateSlotPolicy.PreserveDormant:
                            // P-032: the value is retained, the row stops writing, and no writer remains.
                            expectation = "dormant";
                            pass = published
                                && beforeRead
                                && afterRead
                                && valueBefore == slotCase.Value
                                && valueAfter == slotCase.Value
                                && writerBefore
                                && !writerAfter
                                && dormantRecorded;
                            break;

                        case W4GateSlotPolicy.RemoveDerived:
                            // P-032/P-033: disposable derived data loses its row entirely.
                            expectation = "removed";
                            pass = published
                                && beforeRead
                                && valueBefore == slotCase.Value
                                && !afterRead;
                            break;

                        case W4GateSlotPolicy.TransferTo:
                            // P-025/P-032/P-034: exactly one owner holds the value afterwards, at the destination.
                            expectation = "transferred";
                            pass = published
                                && beforeRead
                                && valueBefore == slotCase.Value
                                && !afterRead
                                && destinationRead
                                && destinationValue == slotCase.Value
                                && destinationVersion == versionBefore
                                && destination.Owner.Equals(slotCase.DestinationOwner)
                                && !destination.Equals(slot);
                            break;

                        default:
                            // P-032: the declared initialization value replaced the live value.
                            expectation = "reset";
                            pass = published
                                && beforeRead
                                && afterRead
                                && valueBefore == slotCase.Value
                                && valueAfter != slotCase.Value
                                && versionAfter == versionBefore
                                && writerAfter
                                && plan != null
                                && plan.Succeeded;
                            break;
                    }

                    Add(name, pass,
                        "policy=" + slotCase.Policy
                        + "; expected=" + expectation
                        + "; slot=" + slot.ToString()
                        + "; published=" + published
                        + "; derived=" + derived.Outcome
                        + "; live=" + Value(beforeRead, valueBefore, versionBefore)
                        + "->" + Value(afterRead, valueAfter, versionAfter)
                        + "; seeded=" + slotCase.Value.ToString(CultureInfo.InvariantCulture)
                        + "; hasWriter=" + writerBefore + "->" + writerAfter
                        + "; dormant=" + dormantRecorded
                        + "; destination=" + destination.ToString()
                        + "=" + Value(destinationRead, destinationValue, destinationVersion)
                        + "; plan=" + (plan == null
                            ? "<none>"
                            : (plan.Succeeded
                                ? "succeeded"
                                : plan.Code.ToString() + ": " + plan.Detail))
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 10. the standing invariant

            /// <summary>
            /// P-006's one publication series, read at the end of the run: every publication of this run left the
            /// composition lane's committed `(revision, epoch)` equal to the world's published pair, and at least one
            /// publication was observed. The count is accumulated by <see cref="NotePublication"/> at every
            /// publication the run makes, so this step cannot pass by observing nothing.
            /// </summary>
            private void CheckTheEpochInvariant()
            {
                const string name = "w4-lane-epoch-equals-world-epoch-throughout";
                try
                {
                    if (host == null || lane == null || publisher == null)
                    {
                        Add(name, false, "the world or its lane is missing");
                        return;
                    }

                    bool joinedNow = MatchesPublishedAssembly();
                    bool pass = counterMismatches == 0
                        && publications > 0
                        && joinedNow
                        && lane.Committed.Revision.Equals(publisher.PublishedRevision)
                        && lane.Committed.Epoch.Equals(host.CurrentEpoch);

                    Add(name, pass,
                        "publications=" + publications.ToString(CultureInfo.InvariantCulture)
                        + "; mismatches=" + counterMismatches.ToString(CultureInfo.InvariantCulture)
                        + "; firstMismatch=" + firstMismatch
                        + "; lane=" + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; assembly=" + publisher.PublishedRevision.Value.ToString(CultureInfo.InvariantCulture)
                        + "; worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture)
                        + "; joined=" + joinedNow
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 11. teardown

            private void TearDownSafely()
            {
                const string name = "w4-teardown-settles-and-disposes";
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
                        NextOperation(host.World), "wave 4 integration gate teardown");
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

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange)
                {
                    if (!PublishUnchangedAssembly(NextOperation(host!.World)))
                    {
                        return false;
                    }
                }

                return NotePublication(label);
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

            /// <summary>
            /// Drains one already-admitted operation, publishes the assembly that belongs to it and records the
            /// publication. A derivation with no target change is answered with the unchanged assembly, so the lane's
            /// pair and the world's pair stay on the one series P-006 requires even when the edit moved no binding.
            /// </summary>
            private bool PublishDrainAndDerive(OperationId operation, out DerivedAssemblyReport report)
            {
                report = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.Refused };
                if (lane == null || pipeline == null || host == null)
                {
                    lastFailure = "the world or its pipeline is missing";
                    return false;
                }

                IReadOnlyList<PublishedOperation> published = lane.Drain();
                if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                {
                    lastFailure = "the publication was refused ("
                        + (published.Count > 0 ? published[0].Outcome.ToString() + "/" + published[0].Code : "none")
                        + ")";
                    return false;
                }

                report = pipeline.PublishDerived(operation);
                LastOutcome = report.Outcome;
                LastDerivation = report.Derivation;
                LastInvalidation = report.Invalidation;
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = "the world refused the assembly: " + report.Describe();
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange
                    && !PublishUnchangedAssembly(NextOperation(host.World)))
                {
                    return false;
                }

                return NotePublication("stage-resource");
            }

            /// <summary>
            /// Publishes one slot case's policy pass through the real publisher. The pass runs first and a refused pass
            /// returns without touching the lane, so a refusal leaves no publication debt (P-029). The lane then
            /// advances on the case's neutral edit — a scope no target lives in, so the publication's derivation
            /// changes no target assembly — and the world publishes the plan `AssemblyPlanner.Build` produced from the
            /// pass's own dispositions. A pass whose only disposition is a plain `Retain` is a real no-op, so the world
            /// publishes the unchanged assembly for that pair, which is what the publisher's own effective-change rule
            /// decides (P-006, GC-015).
            /// </summary>
            private bool PublishPolicyCase(
                int index,
                W4GateSlotCase slotCase,
                out DerivedAssemblyReport derived,
                out StatePolicyPlan? plan)
            {
                derived = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.Refused };
                plan = null;
                if (lane == null || publisher == null || host == null || targets == null || seeder == null
                    || policies == null)
                {
                    lastFailure = "the world or its policy pipeline is missing";
                    return false;
                }

                if (index < 0 || index >= family.NeutralEdits.Count)
                {
                    lastFailure = "the family declares no neutral edit for policy case " + index.ToString(CultureInfo.InvariantCulture);
                    return false;
                }

                // 1. The pass itself. It reads copies of the live slots, so it can run before the lane advances and a
                //    refusal cannot leave an unpublished composition publication behind (P-029).
                var requests = new List<StatePolicyRequest> { slotCase.ToRequest() };
                plan = policies.Execute(family.PolicyTargets, requests);
                if (!plan.Succeeded)
                {
                    lastFailure = "the state-policy pass was refused: " + plan.Code + ": " + plan.Detail;
                    return false;
                }

                // 2. The publication the plan rides on: the lane advances, and the derivation belongs to the same
                //    operation so nothing is left unpublished (P-006).
                var empty = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.NoTargetChange };
                if (!ApplyEdit(family.NeutralEdits[index], "policy-" + slotCase.Name, out empty))
                {
                    return false;
                }

                if (empty.Outcome != DerivedAssemblyOutcome.NoTargetChange)
                {
                    lastFailure = "the neutral edit of policy case " + slotCase.Name
                        + " changed a target assembly (" + empty.Outcome + "); a scope no target lives in must not";
                    derived = empty;
                    return false;
                }

                PlanningCompositionProposal proposal = EmptyPolicyProposal(NextOperation(host.World), empty);
                PlannedPublication planned = AssemblyPlanner.Build(
                    proposal,
                    publisher.Descriptor,
                    publisher.PublishedRevision,
                    host.CurrentEpoch,
                    publisher.Published.Bindings,
                    publisher.Published.Rules,
                    targets.PlannerTargets(),
                    seeder.ReadLiveSlots(family.PolicyTargets),
                    publisher.Migrations,
                    new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot),
                    new InertAcquisitionSet(
                        new StagedResourceGate(StagedByteCeiling, family.Issuer),
                        NextOperation(host.World)),
                    DefaultBudget(),
                    plan);
                derived = empty;

                if (planned.IsRejected)
                {
                    lastFailure = "the plan was rejected: " + planned.State.Code + ": " + planned.State.Detail;
                    return false;
                }

                if (!HasEffectiveChange(planned))
                {
                    // The pass decided nothing but `Retain` for this revision, so this publication's assembly is the
                    // unchanged one — still a real publication of the lane's own numbers (P-006, GC-015).
                    return PublishUnchangedAssembly(NextOperation(host.World));
                }

                if (!publisher.TryAdoptLanePublication(
                        lane.Committed.Revision,
                        lane.Committed.Epoch,
                        out AssemblyEpoch _,
                        out DiagnosticCode adoptCode))
                {
                    lastFailure = "the assembly publisher refused to adopt composition publication "
                        + lane.Committed.Revision.Value.ToString(CultureInfo.InvariantCulture)
                        + "/" + lane.Committed.Epoch.Value.ToString(CultureInfo.InvariantCulture)
                        + " (" + adoptCode + ")";
                    return false;
                }

                AssemblyPublicationReport publication = publisher.Publish(planned);
                if (!publication.Published)
                {
                    lastFailure = "the policy publication was refused: " + publication.Code + ": " + publication.Detail;
                    return false;
                }

                return NotePublication("policy-" + slotCase.Name);
            }

            /// <summary>
            /// The publisher's own effective-change rule, read from the plan it is about to publish: a binding row that
            /// moves, or any disposition that is not a plain `Retain` (P-006, GC-015).
            /// </summary>
            private static bool HasEffectiveChange(PlannedPublication plan)
            {
                if (plan.Installs.Count != 0 || plan.Removals.Count != 0)
                {
                    return true;
                }

                for (int i = 0; i < plan.Dispositions.Count; i++)
                {
                    if (plan.Dispositions[i].Kind != StateDispositionKind.Retain)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <summary>
            /// Runs an explicit reset against the *same slot declarations without the manifest's reset support* and
            /// requires the refusal P-032 mandates. This is the falsification half of the reset case: the reset slot the
            /// manifest declares through the GC-012 field must be permitted, and the identical declaration without that
            /// field must be refused, so a gate whose reset succeeded for any other reason cannot pass. The alternative
            /// reading — probing the live catalog — would ask the same policy set to both permit and refuse one reset.
            ///
            /// The probe set is built from *every* manifest of this revision with the options derived from each slot's
            /// last-support policy alone, so no live row of a policy target can fail the pass for an unrelated reason
            /// (an undeclared target-resident slot would otherwise report `MissingDependency` first and hide the reset
            /// refusal this step exists to prove).
            /// </summary>
            private bool UndeclaredResetIsRefused(StateSlotKey slot, out DiagnosticCode code)
            {
                code = DiagnosticCode.None;
                if (seeder == null || publisher == null)
                {
                    return false;
                }

                var policies = new List<SlotStatePolicy>();
                IReadOnlyList<PluginManifest> manifests = ManifestSet();
                for (int m = 0; m < manifests.Count; m++)
                {
                    PluginManifest manifest = manifests[m];
                    if (manifest == null)
                    {
                        continue;
                    }

                    IReadOnlyList<StateSlotSpec> specs = manifest.StateSlots;
                    for (int i = 0; i < specs.Count; i++)
                    {
                        StateSlotSpec spec = specs[i];
                        if (spec == null)
                        {
                            continue;
                        }

                        // Exactly the pre-GC-012 declaration of the same slot: every declared fact kept, the options
                        // derived from the last-support policy alone, so `ResetPermitted` is false (P-032).
                        policies.Add(new SlotStatePolicy(
                            SlotAuthorityDeclaration.FromSpec(
                                spec, SlotAuthorityOptionsFactory.ForLastSupport(spec.LastSupport)),
                            spec.InitPolicy,
                            spec.ConfigChangePolicy,
                            FirstMigrationKeyOf(spec)));
                    }
                }

                var undeclared = new SlotStatePolicySet(policies);
                var scratch = new MigrationScratch(ScratchCapacityBytes, ScratchBytesPerSlot);
                StatePolicyPlan refused = StatePolicyExecutor.Execute(
                    undeclared,
                    seeder.ReadLiveSlots(family.PolicyTargets),
                    new List<StatePolicyRequest> { StatePolicyRequest.Reset(slot, "undeclared reset") },
                    publisher.Migrations,
                    new DeclaredSlotMigrationRegistry(undeclared),
                    family.InitialValues,
                    scratch);
                code = refused.Code;
                return !refused.Succeeded
                    && refused.Code == DiagnosticCode.OwnershipConflict
                    && refused.Dispositions.Count == 0;
            }

            /// <summary>The declared migration key of one spec, mirroring <c>SlotStatePolicy.FirstMigrationKeyOf</c>.</summary>
            private static FactoryKey FirstMigrationKeyOf(StateSlotSpec spec)
            {
                if (!spec.VersionChangePolicy.RegistrationKey.IsDefault)
                {
                    return spec.VersionChangePolicy;
                }

                for (int i = 0; i < spec.MigrationKeys.Count; i++)
                {
                    if (!spec.MigrationKeys[i].RegistrationKey.IsDefault)
                    {
                        return spec.MigrationKeys[i];
                    }
                }

                return default(FactoryKey);
            }

            /// <summary>The proposal a state-only publication carries: no mount and no unmount (GC-015, P-006).</summary>
            private PlanningCompositionProposal EmptyPolicyProposal(OperationId operation, DerivedAssemblyReport report)
            {
                ContentHash inputHash = report.Derivation != null
                    ? DerivedCompositionProposal.InputHashOf(report.Derivation)
                    : ContentHash.Empty;
                ContentHash catalogHash = report.Input != null && report.Input.Snapshot != null
                    ? report.Input.Snapshot.SnapshotHash
                    : ContentHash.Empty;

                return new PlanningCompositionProposal(
                    operation,
                    inputHash,
                    publisher!.PublishedRevision,
                    host!.CurrentEpoch,
                    catalogHash,
                    lane!.Committed.Mode,
                    null,
                    null);
            }

            private bool PublishUnchangedAssembly(OperationId operation)
            {
                if (publisher == null || lane == null)
                {
                    lastFailure = "no publisher";
                    return false;
                }

                AssemblyPublicationReport unchanged = publisher.PublishUnchangedAssembly(
                    operation, lane.Committed.Revision, lane.Committed.Epoch);
                if (!unchanged.Published)
                {
                    lastFailure = "the unchanged assembly publication was refused: " + unchanged.Detail;
                    return false;
                }

                return NotePublication("unchanged");
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

            /// <summary>Counts one publication and immediately checks P-006's one-series invariant (P-006).</summary>
            private bool NotePublication(string label)
            {
                publications++;
                bool joined = MatchesPublishedAssembly();
                if (!joined)
                {
                    counterMismatches++;
                    if (counterMismatches == 1)
                    {
                        firstMismatch = label + ": " + PublishedStateText();
                    }
                }

                return true;
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

            private string StateOf(PluginInstanceId instance)
            {
                if (lane == null
                    || !lane.Committed.TryGetInstall(instance, out InstallEntry? entry)
                    || entry == null)
                {
                    return "<not-installed>";
                }

                return entry.State.ToString();
            }

            /// <summary>Rows of the published assembly attributed to one installation (P-017, P-033).</summary>
            private int AttributedRows(PluginInstanceId instance)
            {
                if (controller == null)
                {
                    return -1;
                }

                return controller.Binding.CountAttributedRows(instance);
            }

            /// <summary>Live binding rows currently stored for an installation (P-033).</summary>
            private int BindingCount(PluginInstanceId instance)
            {
                if (publisher == null)
                {
                    return -1;
                }

                TargetBindingTable table = publisher.Published.Bindings;
                int count = 0;
                for (int i = 0; i < table.Rows.Count; i++)
                {
                    if (table.Rows[i].Provider.Value.Equals(instance.Value))
                    {
                        count++;
                    }
                }

                return count;
            }

            /// <summary>
            /// The publication a lifecycle request produced, found by the operation identity it was submitted with, so
            /// a step never assumes its operation was the only one in that publication (P-050).
            /// </summary>
            private static PublishedOperation? FindPublished(LifecycleRequestReport report, OperationId operation)
            {
                for (int i = 0; i < report.Published.Count; i++)
                {
                    if (report.Published[i].Operation.Equals(operation))
                    {
                        return report.Published[i];
                    }
                }

                return null;
            }

            private static bool NamesExactly(IReadOnlyList<PluginInstanceId> instances, PluginInstanceId expected)
            {
                if (instances.Count != 1)
                {
                    return false;
                }

                return instances[0].Value.Equals(expected.Value);
            }

            private int LiveStateValue(TargetId target)
            {
                if (seeder == null)
                {
                    return int.MinValue;
                }

                return ReadSlotValue(seeder.ReadLiveSlots(new[] { target }), family.MutableOwner, family.MutableSlot);
            }

            private bool ReadSlot(StateSlotKey slot, out int value, out uint version)
            {
                value = 0;
                version = 0;
                if (publisher == null)
                {
                    return false;
                }

                IReadOnlyList<TargetSlotState> rows = publisher.ReadSlotStates(slot.Target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].Owner.Equals(slot.Owner) && rows[i].Slot.Equals(slot.Slot))
                    {
                        value = rows[i].Value;
                        version = rows[i].SchemaVersion;
                        return true;
                    }
                }

                return false;
            }

            private static int ReadSlotValue(IReadOnlyList<LiveSlotState> slots, OwnerId owner, SlotId slot)
            {
                for (int i = 0; i < slots.Count; i++)
                {
                    if (slots[i].Slot.Owner.Equals(owner) && slots[i].Slot.Slot.Equals(slot))
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

            /// <summary>
            /// A target's complete inherited binding set, canonically ordered: what "the move preserves the target's
            /// state and inheritance" means as data rather than as a count (P-025).
            /// </summary>
            private string BindingFingerprint(TargetId target)
            {
                if (publisher == null)
                {
                    return "<no-publisher>";
                }

                IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(target);
                var text = new StringBuilder();
                text.Append("rows=").Append(rows.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < rows.Count; i++)
                {
                    text.Append('|')
                        .Append(rows[i].Capability.Value.ToString())
                        .Append(':')
                        .Append(rows[i].OutputSlot.ToString(CultureInfo.InvariantCulture))
                        .Append('=')
                        .Append(rows[i].Value.ToString(CultureInfo.InvariantCulture))
                        .Append('@')
                        .Append(rows[i].Provider.Value.ToString())
                        .Append('#')
                        .Append(rows[i].ProviderGeneration.Value.ToString(CultureInfo.InvariantCulture));
                }

                IReadOnlyList<CapabilitySupportRow> supports = publisher.ReadSupportRows(target);
                text.Append(";supports=").Append(supports.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < supports.Count; i++)
                {
                    text.Append('|')
                        .Append(supports[i].Capability.Value.ToString())
                        .Append(':')
                        .Append(supports[i].OutputSlot.ToString(CultureInfo.InvariantCulture))
                        .Append('@')
                        .Append(supports[i].Provider.Value.ToString());
                }

                return text.ToString();
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

            /// <summary>
            /// A target's inherited binding *shape*, canonically ordered: the set of `(capability, output slot)` identities
            /// the published assembly really carries, plus its support count per identity. This is what "the move
            /// preserves the target's inheritance" means as data: the identities survive the move while the supporting
            /// provider legitimately changes (P-017, P-025), so provider identity and generation are deliberately not
            /// part of the shape.
            /// </summary>
            private string BindingShape(TargetId target)
            {
                if (publisher == null)
                {
                    return "<no-publisher>";
                }

                IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(target);
                var text = new StringBuilder();
                text.Append("rows=").Append(rows.Count.ToString(CultureInfo.InvariantCulture));
                for (int i = 0; i < rows.Count; i++)
                {
                    text.Append('|')
                        .Append(rows[i].Capability.Value.ToString())
                        .Append(':')
                        .Append(rows[i].OutputSlot.ToString(CultureInfo.InvariantCulture))
                        .Append('/')
                        .Append(rows[i].SupporterCount.ToString(CultureInfo.InvariantCulture));
                }

                return text.ToString();
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

            /// <summary>The manifests this revision's policy surface is built from: declarations plus extras (P-009).</summary>
            private IReadOnlyList<PluginManifest> ManifestSet()
            {
                var manifests = new List<PluginManifest>(family.Declarations.Count + family.ExtraManifests.Count);
                for (int i = 0; i < family.Declarations.Count; i++)
                {
                    manifests.Add(family.Declarations[i].Manifest);
                }

                if (!ContainsManifest(manifests, family.StatePolicyManifest))
                {
                    manifests.Add(family.StatePolicyManifest);
                }

                for (int i = 0; i < family.ExtraManifests.Count; i++)
                {
                    if (!ContainsManifest(manifests, family.ExtraManifests[i]))
                    {
                        manifests.Add(family.ExtraManifests[i]);
                    }
                }

                return manifests;
            }

            private static bool ContainsManifest(List<PluginManifest> manifests, PluginManifest candidate)
            {
                if (candidate == null)
                {
                    return true;
                }

                for (int i = 0; i < manifests.Count; i++)
                {
                    if (manifests[i].PluginTypeId.Value.Equals(candidate.PluginTypeId.Value))
                    {
                        return true;
                    }
                }

                return false;
            }

            private string DescribeInvalidation(InvalidationClosureResult? invalidation)
            {
                if (invalidation == null)
                {
                    return "<none>";
                }

                return (invalidation.WholeWorld ? "whole-world" : "local")
                    + ",dirty=" + invalidation.DirtyTargets.Count.ToString(CultureInfo.InvariantCulture)
                    + ",carried=" + invalidation.Counters.CarriedTargets.ToString(CultureInfo.InvariantCulture);
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

            /// <summary>The observation name of one slot case: `w4-slot-&lt;policy&gt;-…` (P-060).</summary>
            private static string PolicyStepName(W4GateSlotCase slotCase)
            {
                switch (slotCase.Policy)
                {
                    case W4GateSlotPolicy.Preserve:
                        return "w4-slot-preserve-keeps-the-non-default-value";
                    case W4GateSlotPolicy.PreserveDormant:
                        return "w4-slot-preserve-dormant-retains-without-an-active-writer";
                    case W4GateSlotPolicy.RemoveDerived:
                        return "w4-slot-remove-derived-drops-the-row";
                    case W4GateSlotPolicy.TransferTo:
                        return "w4-slot-transfer-to-moves-the-value-to-the-named-owner";
                    default:
                        return "w4-slot-reset-uses-the-manifest-permission";
                }
            }

            // ------------------------------------------------------------------ helpers

            private OperationId NextOperation(WorldId world)
            {
                operationSequence++;
                return new OperationId(world, family.Issuer, operationSequence);
            }

            private PlanBudget DefaultBudget() =>
                new PlanBudget(1024UL * 1024UL, 1024UL * 1024UL, ScratchCapacityBytes, ScratchBytesPerSlot);

            /// <summary>
            /// Records one observation. The family qualification is applied here, at the single recording point, so
            /// every step method passes the bare name from <see cref="ObservationNames"/> and the recorded sequence is
            /// exactly the qualified list: a step that qualified its own name (or forgot to) would change the digest,
            /// which is what the EditMode suite and the player probe assert on.
            /// </summary>
            private void Add(string bareName, bool passed, string detail)
            {
                lastFailure = string.Empty;
                steps.Add(new W4GateStep(family.Label + "/" + bareName, passed, detail ?? string.Empty));
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

            private static bool IsReverseOf(IReadOnlyList<Id128> observed, IReadOnlyList<Id128> acquired)
            {
                if (observed.Count != acquired.Count)
                {
                    return false;
                }

                for (int i = 0; i < observed.Count; i++)
                {
                    if (!observed[i].Equals(acquired[acquired.Count - 1 - i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            private static string DescribeIds(IReadOnlyList<Id128> ids)
            {
                if (ids.Count == 0)
                {
                    return "<none>";
                }

                var text = new StringBuilder();
                for (int i = 0; i < ids.Count; i++)
                {
                    if (i != 0)
                    {
                        text.Append(',');
                    }

                    text.Append(ids[i].ToString());
                }

                return text.ToString();
            }

            private static string Value(bool present, int value, uint version)
                => present
                    ? value.ToString(CultureInfo.InvariantCulture) + "@" + version.ToString(CultureInfo.InvariantCulture)
                    : "<missing>";

            private static string Describe(DerivedAssemblyReport? report)
                => report == null
                    ? "<none>"
                    : report.Outcome.ToString()
                        + (report.Code == DiagnosticCode.None
                            ? string.Empty
                            : "(" + report.Code.ToString() + ")")
                        + " " + report.Detail;

            private static string DescribeException(Exception exception)
                => "unhandled " + exception.GetType().FullName + ": " + exception.Message;
        }

        /// <summary>
        /// The lane's manifest source: the catalog's own declarations plus this gate's extra manifests, which are the
        /// four lifecycle installations and the state-policy provider. A manifest added here resolves for a mount of
        /// its plugin type exactly as a catalog declaration does (P-009).
        /// </summary>
        private sealed class W4GateManifestSource : IPluginManifestSource
        {
            private readonly CatalogManifestSource catalog;
            private readonly Dictionary<Id128, PluginManifest> added = new Dictionary<Id128, PluginManifest>();

            public W4GateManifestSource(CatalogManifestSource catalog, IReadOnlyList<PluginManifest> extra)
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                if (extra == null)
                {
                    throw new ArgumentNullException(nameof(extra));
                }

                for (int i = 0; i < extra.Count; i++)
                {
                    if (extra[i] != null)
                    {
                        added[extra[i].PluginTypeId.Value] = extra[i];
                    }
                }
            }

            public bool TryGetManifest(PluginTypeId pluginType, out PluginManifest? manifest)
            {
                if (added.TryGetValue(pluginType.Value, out PluginManifest? found) && found != null)
                {
                    manifest = found;
                    return true;
                }

                return catalog.TryGetManifest(pluginType, out manifest);
            }

            public bool TryGetConfigDefaults(SchemaRef schema, out ConfigDocument? defaults) =>
                catalog.TryGetConfigDefaults(schema, out defaults);
        }

        /// <summary>
        /// The scenario's managed-resource factory: counted preparations, deterministic lease identities and a
        /// recorded disposal order, so "each lease was prepared once and disposed at most once, in reverse acquisition
        /// order" is data rather than an intention (P-007, P-048).
        /// </summary>
        private sealed class W4GateResourceFactory : IManagedResourceFactory
        {
            private readonly IdSequence ids;

            public W4GateResourceFactory(IdSequence ids)
            {
                this.ids = ids ?? throw new ArgumentNullException(nameof(ids));
            }

            public int PrepareCount { get; private set; }

            public int DisposeCount { get; private set; }

            /// <summary>Lease identities in disposal order, so teardown ordering is observable (P-048).</summary>
            public DisposalLog DisposedSequence { get; } = new DisposalLog();

            public FactoryKey DisposerKey { get; } =
                new FactoryKey(new Id128(0x573447415445554CUL, 1UL), 1U);

            public IManagedResourceLease Prepare(ManagedResourceRequest request)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                PrepareCount++;
                return new ManagedResourceLease(
                    request.Resource,
                    ids.Next(),
                    request.Token,
                    DisposerKey,
                    new ManagedResourceGate(),
                    OnDisposed);
            }

            private void OnDisposed(Id128 leaseId)
            {
                DisposedSequence.Append(leaseId);
                DisposeCount++;
            }
        }

        /// <summary>
        /// The disposal order of one run, with a monotonic cursor so a step can ask for exactly the disposals that
        /// happened during it instead of diffing a count against a count (P-048).
        /// </summary>
        private sealed class DisposalLog
        {
            private readonly List<Id128> entries = new List<Id128>();

            /// <summary>How many disposals have been recorded; a caller captures this before an operation (P-048).</summary>
            public int Cursor => entries.Count;

            public void Append(Id128 leaseId) => entries.Add(leaseId);

            /// <summary>The disposals recorded since a cursor, in the order the factory observed them.</summary>
            public List<Id128> Since(int cursor)
            {
                var slice = new List<Id128>();
                for (int i = cursor; i < entries.Count; i++)
                {
                    slice.Add(entries[i]);
                }

                return slice;
            }
        }
    }
}
