// GameCore.Validation.ProbeHost — the GC-020 fixed-step qualification scenario.
//
// The gate sentences this file implements, from `docs/game-core/09-implementation-guide.md` (Wave 6, GC-020) and
// `docs/game-core/07-reference-compositions.md` s4 (plus REF-A01..A06):
//
//   "Integrate a fixed-step real-time slice with one authority per physical domain." — with the acceptance
//   "one admitted step integrates exactly once; a presentation rate never advances simulation twice; an engine-owned
//    pose is never integrated by gameplay; observation replay separates rule repeatability from native physics."
//
// One runner, one traversal family (`IGc020Family`), the same shape `Gc019Scenario` has for its two adapters: the
// family owns only what the traversal genre declares (its movement identity and payload, its targets, its declared
// numeric facts, its stage runtime); this file owns the one scripted sequence the genre runs, so the genre is
// demonstrated inside the *same* real world the traversal package's own stages run in:
//
//   * the world and its committed assembly are the GC-013 construction (`UnityWorldRegistry` + `AssemblyPublisher` +
//     `LiveTargetIndex`/`LiveTargetSeeder` over the family's declarations, the real `CompositionHost` control lane
//     with the production `DerivationModeSwitchValidator`, and `DerivedAssemblyPipeline` for the derivation), with
//     the compiled schedule's time driver (`WorldTimeDriver`) pumped through the family's own FIXED-STEP world
//     (P-030, P-036, P-042). Host time is never invented: every admitted step in this scenario is one real
//     `WorldTimeDriver.PumpFrame(ulong)` call over host ticks the world itself accumulated;
//   * input is the real `TypedInputIngress` over the world's own `ICommandIngress` (`UnityWorldHost`), so admission
//     keeps the one path the kernel already owns and a sampled movement event is not itself a committed game event
//     (P-007, P-037, P-042, 04 s7);
//   * every composition edit goes through `PublishEdit` (lane submit -> drain -> `pipeline.PublishDerived`), so one
//     publication series holds and a later adoption is never refused as stale (P-006);
//   * the observation trace is the package's own bounded `TraversalTraceRecorder`, so "rule repeatability" and
//     "native physics observation" are compared as recorded values with a declared tolerance, never as a bitwise
//     cross-platform claim (P-008, TEST-022);
//   * the physics half is the REAL `UnityPhysicsSceneBackend` behind the engine-free `PhysicsAuthorityGate`, so
//     "exactly one engine simulation per admitted step" is a property of a live world, not of a convention (04 s7).
//
// ONE CATALOG.  There is no committed generated traversal catalog (`Assets/GameCore.Validation/GeneratedTraversal`
// does not exist), so this gate runs the traversal fixture catalog once. `Gc020TraversalHost.RunFixtureCatalog` is
// the entry point and `Gc020TraversalHost.RunBoth` hands back that single run; the first observation reports
// `generatedCatalog=absent` rather than inventing a second catalog's worth of evidence.
//
// Each observation records the values it was computed from, and a failure carries the world's own diagnostic text.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.Planning;
using GameCore.Rules.Cards;
using GameCore.Rules.Narrative;
using GameCore.Rules.Traversal;
using GameCore.Unity.Adapters.Authority;
using GameCore.Unity.Adapters.Animation;
using GameCore.Unity.Adapters.Audio;
using GameCore.Unity.Adapters.Physics;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using GameCore.Validation.Generated;
using GameCore.Validation.GeneratedCards;
using GameCore.Validation.Probe;
using Unity.Entities;
using UnityEngine;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Runs the GC-020 fixed-step sequence over one family and one catalog.</summary>
    public static class Gc020Scenario
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family scenarios use.</summary>
        public const string FixtureRunPrefix = "fixture:";

        /// <summary>
        /// The scenario's observations, in execution order, without the family qualification. The family records
        /// exactly these names, so a renamed or dropped observation fails the caller instead of shrinking the gate
        /// silently.
        /// </summary>
        public static readonly string[] ObservationNames =
        {
            "gc020-fixed-step-world-and-runners",
            "gc020-existing-runners-derive-the-modifier",
            "gc020-one-admitted-step-integrates-once",
            "gc020-replay-separates-pure-motion-from-engine-observation",
            "gc020-presentation-rate-does-not-double-advance",
            "gc020-reparent-changes-the-contribution-and-keeps-state",
            "gc020-unmount-keeps-pose-progress-and-receipts",
            "gc020-mode-switch-both-directions",
            "gc020-future-descendant-derives-before-execution",
            "gc020-one-simulation-per-admitted-step",
            "gc020-externally-owned-pose-is-not-integrated",
            "gc020-committed-animation-and-audio-output",
            "gc020-cards-and-narrative-declare-no-action-phase",
            "gc020-teardown-settles-and-disposes",
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
        public static Gc020ScenarioResult Run(IGc020Family family)
        {
            if (family == null)
            {
                throw new ArgumentNullException(nameof(family));
            }

            return new Executor(family).Run();
        }

        private sealed class Executor
        {
            /// <summary>Bounded temporary storage the plans of this scenario may reserve, in bytes (P-022).</summary>
            private const ulong ScratchCapacityBytes = 4096UL;

            private const ulong ScratchBytesPerSlot = 64UL;

            private const ulong PrepareBytesLimit = 1024UL * 1024UL;

            /// <summary>Staged lease ceiling of the scenario's plan resource gate, in bytes (P-022).</summary>
            private const ulong StagedByteCeiling = 1024UL * 1024UL;

            /// <summary>Frames a fixed-step world is pumped while it must commit no step (no elapsed host time).</summary>
            private const int IdlePumpFrames = 4;

            /// <summary>Admitted steps one replay run records, so the two traces hold comparable step records.</summary>
            private const int ReplaySteps = 5;

            /// <summary>Admitted steps the physics observation drives the local scene through (04 s7).</summary>
            private const int PhysicsSteps = 3;

            /// <summary>Declared live-target capacity of the scenario's target registry.</summary>
            private const int TargetCapacity = 32;

            /// <summary>Low half of the input-source identity: this scenario's own headless sampling source (P-008).</summary>
            private const ulong InputSourceLow = 0x6763303230696E70UL;

            /// <summary>The seven presentation rates REF-A06 names, in the order the observation reports them.</summary>
            private static readonly int[] PresentationRates = { 30, 60, 144 };

            private readonly IGc020Family family;
            private readonly List<Gc020Step> steps = new List<Gc020Step>();
            private readonly IdSequence sessionSequence;

            private UnityWorldHost? host;
            private UnityWorldRegistration? registration;
            private CompositionHost? lane;
            private AssemblyPublisher? publisher;
            private TargetRegistry? registry;
            private LiveTargetIndex? targets;
            private LiveTargetSeeder? seeder;
            private DerivedAssemblyPipeline? pipeline;
            private WorldCompositionBridge? bridge;
            private DerivationModeSwitchValidator? validator;
            private WorldTimeDriver? time;

            private TypedInputIngress? ingress;

            private Gc020StageRuntime? stageRuntime;

            private ulong operationSequence;

            /// <summary>Host ticks handed to the world's last pump; every step is one step's worth of them (P-036).</summary>
            private ulong hostTicksNow;

            /// <summary>Global physics simulation mode observed just before the local scene was created (04 s7).</summary>
            private SimulationMode globalModeBeforeAttach = SimulationMode.FixedUpdate;

            private int registryBeforeCreate;
            private string lastFailure = string.Empty;

            /// <summary>Every publication this run made, counted against the P-006 series check.</summary>
            private int publications;

            /// <summary>Publications after which the lane pair and the world pair disagreed; zero is the gate.</summary>
            private int counterMismatches;

            /// <summary>First mismatch, verbatim, so a failure names the publication that broke the series.</summary>
            private string firstMismatch = "<none>";

            /// <summary>The publications this run made, so the teardown can report the P-006 series pressure.</summary>

            public Executor(IGc020Family family)
            {
                this.family = family;
                sessionSequence = new IdSequence(family.SessionSalt);
            }

            public Gc020ScenarioResult Run()
            {
                CreateWorldAndLiveTargets();
                ExistingRunnersDeriveTheModifier();
                OneAdmittedStepIntegratesOnce();
                ReplaySeparatesPureMotionFromEngineObservation();
                PresentationRateDoesNotDoubleAdvance();
                ReparentChangesTheContributionAndKeepsState();
                UnmountKeepsPoseProgressAndReceipts();
                ModeSwitchBothDirections();
                FutureDescendantDerivesBeforeExecution();
                OneSimulationPerAdmittedStep();
                ExternallyOwnedPoseIsNotIntegrated();
                CommittedAnimationAndAudioOutput();
                CardsAndNarrativeDeclareNoActionPhase();
                TeardownSettlesAndDisposes();

                return new Gc020ScenarioResult(family.Label, steps);
            }

            // ------------------------------------------------------------------ 1. the fixed-step world

            /// <summary>
            /// Builds the one real fixed-step world of this run — the family's compiled five-stage schedule, its live
            /// targets, the control lane over the family's own manifest source, the composition bridge, the derived
            /// assembly pipeline and the fixed-step time driver — then attaches the genre's own stage runtime and the
            /// REAL local physics scene, and checks that the world definition really carries the reference's
            /// configured 20 ms / four-step catch-up. A world with no elapsed host time must commit no step, which is
            /// what "advances only by its admitted steps" means for a fixed-step world (P-030, P-036, 07 s4.1).
            /// </summary>
            private void CreateWorldAndLiveTargets()
            {
                const string name = "gc020-fixed-step-world-and-runners";
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
                    registration = family.CreateRegistration(descriptorReport.Adaptation);

                    bool created = UnityWorldRegistry.TryCreate(
                        request, registration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        Add(name, false, "world creation failed: " + result.Code + ": " + result.Detail);
                        return;
                    }

                    registry = new TargetRegistry(world, TargetCapacity);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        family.CreateRecipes(),
                        family.CreateMigrations(),
                        descriptorReport.Descriptor);

                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    IDerivationValueSource valueSource = family.CreateValues();
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

                    bool seeded = family.SeedTargets(new Gc013WorldContext(host, targets, seeder));

                    // The genre's own stage runtime, attached exactly where its own scenario attaches it: after the
                    // world exists and its targets are seeded, before any step is pumped. Without it the genre's five
                    // systems resolve no module, the movement route stays unconsumed and the step commit faults the
                    // world (P-043) — this gate would then observe a faulted world rather than the course it qualifies.
                    globalModeBeforeAttach = Physics.simulationMode;
                    stageRuntime = family.AttachStageRuntime(host, descriptorReport, targets, seeder);
                    family.ConfigurePhysics(world);

                    // The live-target half: the course, every runner and every volume the family declared, each with
                    // the ECS storage its own recipe installs, and each bound in the module by its declared recipe.
                    var unstored = new List<string>();
                    var unbound = new List<string>();
                    EntityManager entityManager = host.EntityWorld.EntityManager;
                    if (!targets.Contains(family.CourseTarget)
                        || !HasCourseStorage(entityManager, EntityOf(family.CourseTarget)))
                    {
                        unstored.Add(family.CourseTarget.ToString());
                    }

                    // The future runner (`runner-c`) is not spawned until observation 9, so only the live runners are
                    // required to carry storage here; the count of them is what this observation asserts beside it.
                    int liveRunners = 0;
                    for (int i = 0; i < family.CourseTargets.Count; i++)
                    {
                        TargetId runner = family.CourseTargets[i];
                        if (!targets.Contains(runner))
                        {
                            continue;
                        }

                        liveRunners++;
                        if (!HasRunnerStorage(entityManager, EntityOf(runner)))
                        {
                            unstored.Add(runner.ToString());
                        }
                    }

                    if (!targets.Contains(family.SecondCheckpoint)
                        || !HasVolumeStorage(entityManager, EntityOf(family.SecondCheckpoint)))
                    {
                        unstored.Add(family.SecondCheckpoint.ToString());
                    }

                    TraversalModule? module = stageRuntime.Module;
                    if (module != null)
                    {
                        IReadOnlyList<TraversalRunnerRef> boundRunners = module.Runners();
                        for (int i = 0; i < family.CourseTargets.Count; i++)
                        {
                            TargetId runner = family.CourseTargets[i];
                            bool live = false;
                            for (int r = 0; r < boundRunners.Count; r++)
                            {
                                live |= boundRunners[r].Target.Equals(runner);
                            }

                            if (!live)
                            {
                                unbound.Add(runner.ToString());
                            }
                        }
                    }

                    ulong idleSteps = PumpIdleFrames();
                    bool joined = MatchesPublishedAssembly();

                    // The declared identities this gate *and* the fixture catalog use for the same stable names must
                    // be the same identities, or a mount the gate declares would resolve a different registration
                    // than the one the catalog holds (P-028, P-009).
                    bool identitiesAgree = TraversalCatalogTable.DerivationHolds()
                        && TraversalKeys.PluginFactoryKey.Equals(TraversalCatalogTable.PluginFactoryKey)
                        && TraversalKeys.ConfigSchema.Equals(TraversalCatalogTable.ConfigSchema);

                    FixedStepSettings? fixedStep = host.Request.FixedStep;
                    bool declaredStep = host.TemporalModel == TemporalModel.FixedStep
                        && fixedStep != null
                        && fixedStep.IsValid
                        && fixedStep.StepDurationTicks == family.StepDurationTicks
                        && fixedStep.MaxStepsPerPump == family.MaxStepsPerPump
                        && fixedStep.TicksPerSecond == TraversalRegistration.TicksPerSecond
                        && family.StepMilliseconds == TraversalVocabulary.StepMilliseconds
                        && family.StepMilliseconds > 0;

                    bool pass = seeded
                        && declaredStep
                        && identitiesAgree
                        && registry != null
                        && bridge != null
                        && validator != null
                        && publisher.PublishedRevision.Equals(lane.Committed.Revision)
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && host.CurrentEpoch.Equals(lane.Committed.Epoch)
                        && host.Lifecycle == WorldLifecycleState.Running
                        && host.CurrentStep.Equals(LogicalStepId.Zero)
                        && UnityWorldRegistry.Count == registryBeforeCreate + 1
                        && module != null
                        && module.CourseEntity != Entity.Null
                        && module.RunnerCount == liveRunners
                        && module.VolumeCount == 2
                        && liveRunners == 3
                        && unstored.Count == 0
                        && unbound.Count == 0
                        && stageRuntime.Physics != null
                        && stageRuntime.PhysicsGate != null
                        && idleSteps == 0UL
                        && joined;

                    Add(name, pass,
                        "session=" + world.Session.ToString()
                        + "; catalogFingerprint=" + family.CatalogFingerprint
                        + "; generatedCatalog=" + (Gc020TraversalHost.GeneratedCatalogPresent ? "present" : "absent")
                        + "(" + Gc020TraversalHost.GeneratedCatalogDirectory + ")"
                        + "; registryBefore=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; liveTargets=" + targets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; courseTargets=" + family.CourseTargets.Count.ToString(CultureInfo.InvariantCulture)
                        + "; moduleRunners=" + (module != null ? module.RunnerCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "(live=" + liveRunners.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; moduleVolumes=" + (module != null ? module.VolumeCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; targetsWithoutStorage=" + Join(unstored)
                        + "; targetsUnbound=" + Join(unbound)
                        + "; temporalModel=" + host.TemporalModel
                        + "; stepMillis=" + family.StepMilliseconds.ToString(CultureInfo.InvariantCulture)
                        + "; stepDurationTicks=" + family.StepDurationTicks.ToString(CultureInfo.InvariantCulture)
                        + "; maxStepsPerPump=" + family.MaxStepsPerPump.ToString(CultureInfo.InvariantCulture)
                        + "; declaredStep=" + declaredStep
                        + "; identitiesAgree=" + identitiesAgree
                        + "; descriptorStages=" + stageRuntime.DescriptorStageCount.ToString(CultureInfo.InvariantCulture)
                        + "; registeredStages=" + registration.Stages.Count.ToString(CultureInfo.InvariantCulture)
                        + "; registeredSystems=" + registration.Systems.Count.ToString(CultureInfo.InvariantCulture)
                        + "; physicsScene=" + (stageRuntime.Physics != null ? stageRuntime.Physics.SceneName : "<none>")
                        + "; scopes=" + lane.Committed.Scopes.Count.ToString(CultureInfo.InvariantCulture)
                        + "; mode=" + lane.Committed.Mode
                        + "; bridge=" + (bridge != null)
                        + "; modeValidator=" + (validator != null)
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

            // ------------------------------------------------------------------ 2. the mounted modifier

            /// <summary>
            /// Mounting `Tailwind` in the valley makes every runner the provider reaches carry an ACTIVE
            /// `traversal.acceleration` binding row equal to `+2 m/s²` before any step runs, while a checkpoint (its
            /// recipe declares only the sensor contract) and the isolated showcase runner carry none. The publication
            /// itself integrates nothing: it changes configuration, not motion (07 s4.1, P-013, P-015, P-016).
            /// </summary>
            private void ExistingRunnersDeriveTheModifier()
            {
                const string name = "gc020-existing-runners-derive-the-modifier";
                try
                {
                    if (host == null || lane == null || publisher == null || targets == null || stageRuntime == null)
                    {
                        Add(name, false, "the world or its stage runtime is missing");
                        return;
                    }

                    TraversalVector3i velocityBefore = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseBefore = PoseOf(family.VelocityAssertedTarget);

                    bool mounted = PublishEdit(family.MountProvider(), "mount-tailwind");

                    var reached = new List<string>();
                    var unreached = new List<string>();
                    var isolated = new List<string>();
                    for (int i = 0; i < family.CourseTargets.Count; i++)
                    {
                        TargetId runner = family.CourseTargets[i];
                        if (!targets.Contains(runner))
                        {
                            continue;
                        }

                        bool active = TryActiveBindingValue(runner, out int value);
                        if (runner.Equals(family.IsolatedTarget))
                        {
                            isolated.Add(runner.ToString() + "=" + (active ? value.ToString(CultureInfo.InvariantCulture) : "<none>"));
                            continue;
                        }

                        bool inValley = IsUnderScope(runner, family.ProviderScope);
                        if (inValley)
                        {
                            reached.Add(runner.ToString() + "=" + (active ? value.ToString(CultureInfo.InvariantCulture) : "<none>"));
                        }
                        else
                        {
                            unreached.Add(runner.ToString() + "=" + (active ? value.ToString(CultureInfo.InvariantCulture) : "<none>"));
                        }
                    }

                    bool checkpointSilent = !TryActiveBindingValue(family.IneligibleTarget, out int checkpointValue);
                    if (!checkpointSilent)
                    {
                        unreached.Add(family.IneligibleTarget.ToString() + "=" + checkpointValue.ToString(CultureInfo.InvariantCulture));
                    }

                    bool asserted = TryActiveBindingValue(family.VelocityAssertedTarget, out int assertedValue)
                        && assertedValue == family.ProviderValue
                        && assertedValue == TraversalVocabulary.TailwindMilli;

                    // Every runner the provider reaches must carry the row; a runner outside its scope must not.
                    bool reachedAll = true;
                    for (int i = 0; i < family.CourseTargets.Count; i++)
                    {
                        TargetId runner = family.CourseTargets[i];
                        if (!targets.Contains(runner) || runner.Equals(family.IsolatedTarget))
                        {
                            continue;
                        }

                        if (IsUnderScope(runner, family.ProviderScope)
                            && (!TryActiveBindingValue(runner, out int value) || value != family.ProviderValue))
                        {
                            reachedAll = false;
                        }
                    }

                    bool isolatedSilent = true;
                    for (int i = 0; i < family.CourseTargets.Count; i++)
                    {
                        TargetId runner = family.CourseTargets[i];
                        if (targets.Contains(runner)
                            && runner.Equals(family.IsolatedTarget)
                            && TryActiveBindingValue(runner, out int _))
                        {
                            isolatedSilent = false;
                        }
                    }

                    TraversalVector3i velocityAfter = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseAfter = PoseOf(family.VelocityAssertedTarget);
                    bool publicationMovedNothing = velocityAfter.Equals(velocityBefore) && poseAfter.Equals(poseBefore);

                    bool pass = mounted
                        && asserted
                        && reachedAll
                        && reached.Count >= 1
                        && isolatedSilent
                        && checkpointSilent
                        && publicationMovedNothing
                        && stageRuntime.Module != null
                        && stageRuntime.Module.SteppedStepCount == 0
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "mounted=" + mounted
                        + "; providerValue=" + family.ProviderValue.ToString(CultureInfo.InvariantCulture)
                        + "; reached=" + Join(reached)
                        + "; notReached=" + Join(unreached)
                        + "; isolated=" + Join(isolated)
                        + "; checkpointRows=" + (checkpointSilent ? "<none>" : "present")
                        + "; velocity=" + velocityBefore.ToString() + "->" + velocityAfter.ToString()
                        + "; pose=" + poseBefore.ToString() + "->" + poseAfter.ToString()
                        + "; steppedSteps=" + (stageRuntime.Module != null ? stageRuntime.Module.SteppedStepCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; bindingRows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 3. one admitted step

            /// <summary>
            /// One admitted movement envelope, one fixed step: the tailwind modifier is applied exactly once
            /// (`1.00 -> 1.04`), the pose advances by exactly one step of the velocity the step began with (20 mm),
            /// `traversal.output` writes exactly one snapshot row for that step, and the integrator advances exactly
            /// the runners this course owns — no more and no fewer (07 s4.3 REF-A01, P-034, P-044).
            /// </summary>
            private void OneAdmittedStepIntegratesOnce()
            {
                const string name = "gc020-one-admitted-step-integrates-once";
                try
                {
                    if (host == null || time == null || publisher == null || targets == null || stageRuntime == null)
                    {
                        Add(name, false, "the world or its stage runtime is missing");
                        return;
                    }

                    TraversalModule? module = stageRuntime.Module;
                    if (module == null)
                    {
                        Add(name, false, "the world has no traversal module");
                        return;
                    }

                    // One envelope for the asserted runner: zero additional horizontal input and no jump, so the
                    // only acceleration in play is the derived configuration the assembly published (P-042).
                    InputAdmissionResult admission = SubmitMovement(family.VelocityAssertedTarget, 0, 0);
                    if (admission.Outcome != InputAdmissionOutcome.Admitted)
                    {
                        Add(name, false, "the movement envelope was not admitted: " + admission.ToString());
                        return;
                    }

                    TraversalVector3i velocityBefore = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseBefore = PoseOf(family.VelocityAssertedTarget);
                    int integratedBefore = module.IntegratedRunnerStepCount;
                    int outputBefore = module.OutputRowCount;
                    LogicalStepId stepBefore = host.CurrentStep;

                    ulong committed = StepOnce();

                    TraversalVector3i velocityAfter = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseAfter = PoseOf(family.VelocityAssertedTarget);
                    int integratedDelta = module.IntegratedRunnerStepCount - integratedBefore;
                    int outputDelta = module.OutputRowCount - outputBefore;
                    int poseAdvance = poseAfter.X - poseBefore.X;

                    // The step the trace recorded belongs to the step that just executed: the host publishes the
                    // step's image and only then advances `CurrentStep` (P-044), so the record's own step number is
                    // the one the world ran, not the number it reached afterwards.
                    bool bodyRecorded = TryLastBodyOf(
                        family.VelocityAssertedTarget, out TraversalStepTrace recordedStep, out TraversalBodyTrace body);
                    bool recordedStepIsTheOneRan = bodyRecorded && recordedStep.Step == stepBefore.Value;
                    int appliedAccelerationX = bodyRecorded ? body.AppliedAcceleration.X : int.MinValue;

                    bool pass = committed == 1UL
                        && host.CurrentStep.Value == stepBefore.Value + 1UL
                        && velocityBefore.X == family.SeededVelocityMilli
                        && velocityAfter.X == family.ExpectedAcceleratedVelocityMilli
                        && velocityAfter.Equals(new TraversalVector3i(family.ExpectedAcceleratedVelocityMilli, 0, 0))
                        && poseAdvance == family.SeededVelocityMilli * family.StepMilliseconds / TraversalVocabulary.MilliScale
                        && integratedDelta == module.RunnerCount
                        && module.LastIntegratedCount == module.RunnerCount
                        && outputDelta == 1
                        && module.DecodedInputCount == 1
                        && module.InputRejectionCount == 0
                        && recordedStepIsTheOneRan
                        && appliedAccelerationX == family.ProviderValue
                        && appliedAccelerationX == TraversalVocabulary.TailwindMilli
                        && bodyRecorded
                        && body.Velocity.X == family.ExpectedAcceleratedVelocityMilli
                        && module.SteppedStepCount >= 1
                        && module.CourseEntity != Entity.Null
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "committedSteps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + "; step=" + stepBefore.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; admission=" + admission.Outcome
                        + "; velocity=" + velocityBefore.ToString() + "->" + velocityAfter.ToString()
                        + "; expectedVelocity=" + family.ExpectedAcceleratedVelocityMilli.ToString(CultureInfo.InvariantCulture)
                        + "; poseAdvanceX=" + poseAdvance.ToString(CultureInfo.InvariantCulture)
                        + "; pose=" + poseBefore.ToString() + "->" + poseAfter.ToString()
                        + "; integratedDelta=" + integratedDelta.ToString(CultureInfo.InvariantCulture)
                        + "; lastIntegrated=" + module.LastIntegratedCount.ToString(CultureInfo.InvariantCulture)
                        + "; ownedRunners=" + module.RunnerCount.ToString(CultureInfo.InvariantCulture)
                        + "; outputRows=" + outputDelta.ToString(CultureInfo.InvariantCulture)
                        + "; decodedInputs=" + module.DecodedInputCount.ToString(CultureInfo.InvariantCulture)
                        + "; refusedInputs=" + module.InputRejectionCount.ToString(CultureInfo.InvariantCulture)
                        + "; recordedStep=" + (bodyRecorded ? recordedStep.Step.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; recordedAppliedAccelerationX=" + (bodyRecorded
                            ? appliedAccelerationX.ToString(CultureInfo.InvariantCulture) : "<none>")
                        + "; recordedVelocity=" + (bodyRecorded ? body.Velocity.ToString() : "<none>")
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 4. replay vs engine observation

            /// <summary>
            /// Two independent worlds integrate the SAME admitted input through the SAME declared step, and their
            /// recorded observations agree exactly (tolerance 0): the pure integer motion of this package is
            /// repeatable. The same comparison against a trace whose one body is off by a single milli-unit
            /// MISMATCHES at tolerance 0 and MATCHES at the package's declared comparison tolerance — which is what a
            /// recorded-observation comparison, rather than a bitwise claim, means for a physics-adjacent slice
            /// (P-008, TEST-022, REF-A06).
            /// </summary>
            private void ReplaySeparatesPureMotionFromEngineObservation()
            {
                const string name = "gc020-replay-separates-pure-motion-from-engine-observation";
                SecondaryFixture? first = null;
                SecondaryFixture? second = null;
                try
                {
                    first = SecondaryFixture.Build(family, sessionSequence, "traversal-replay-a", hostTicksNow);
                    second = SecondaryFixture.Build(family, sessionSequence, "traversal-replay-b", hostTicksNow);
                    if (!first.Ready || !second.Ready)
                    {
                        Add(name, false, "a replay world could not be built: " + first.Failure + " | " + second.Failure);
                        return;
                    }

                    bool ranFirst = first.SubmitMovement(0, 0) == InputAdmissionOutcome.Admitted
                        && first.PumpSteps(ReplaySteps) == (ulong)ReplaySteps;
                    bool ranSecond = second.SubmitMovement(0, 0) == InputAdmissionOutcome.Admitted
                        && second.PumpSteps(ReplaySteps) == (ulong)ReplaySteps;

                    TraversalTraceRecorder? traceA = first.Trace;
                    TraversalTraceRecorder? traceB = second.Trace;
                    if (!ranFirst || !ranSecond || traceA == null || traceB == null)
                    {
                        Add(name, false, "the replay worlds did not record a trace");
                        return;
                    }

                    TraversalTraceComparison identical = traceA.CompareTo(traceB, 0);
                    TraversalTraceRecorder perturbed = CopyTrace(traceA, family.VelocityAssertedTarget, 1);
                    TraversalTraceComparison bitwise = traceA.CompareTo(perturbed, 0);
                    TraversalTraceComparison tolerated = traceA.CompareTo(
                        perturbed, TraversalVocabulary.VelocityToleranceMilli);

                    int replayBodies = 0;
                    IReadOnlyList<TraversalStepTrace> recorded = traceA.Steps;
                    for (int i = 0; i < recorded.Count; i++)
                    {
                        replayBodies += recorded[i].Bodies.Count;
                    }

                    bool pass = identical.Matches
                        && identical.MismatchedBodies == 0
                        && identical.MismatchedSteps == 0
                        && identical.ComparedSteps == ReplaySteps
                        && replayBodies >= ReplaySteps
                        && !bitwise.Matches
                        && bitwise.MismatchedBodies >= 1
                        && tolerated.Matches
                        && tolerated.MismatchedBodies == 0
                        && TraversalVocabulary.VelocityToleranceMilli > 0;

                    Add(name, pass,
                        "steps=" + ReplaySteps.ToString(CultureInfo.InvariantCulture)
                        + "; replayA_steps=" + recorded.Count.ToString(CultureInfo.InvariantCulture)
                        + "; replayBodies=" + replayBodies.ToString(CultureInfo.InvariantCulture)
                        + "; identical=" + identical.ToString()
                        + "; perturbedBy=1"
                        + "; atTolerance0=" + bitwise.Matches + "(mismatchedBodies="
                        + bitwise.MismatchedBodies.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; atDeclaredTolerance=" + tolerated.Matches
                        + "(tolerance=" + TraversalVocabulary.VelocityToleranceMilli.ToString(CultureInfo.InvariantCulture)
                        + ", mismatchedBodies=" + tolerated.MismatchedBodies.ToString(CultureInfo.InvariantCulture) + ")"
                        + "; comparisonDetail=" + bitwise.Detail
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
                finally
                {
                    second?.Dispose();
                    first?.Dispose();
                }
            }

            // ------------------------------------------------------------------ 5. presentation rate

            /// <summary>
            /// The same one second of host time, delivered as 30, 60 and 144 equal host samples, commits the SAME
            /// number of logical steps and ends at the same pure-motion velocity in all three runs: the declared
            /// four-step catch-up bound and the retained time debt absorb the extra frames, and a 144 Hz presentation
            /// never advances authority more often than a 30 Hz one (07 s4.2, REF-A06).
            /// </summary>
            private void PresentationRateDoesNotDoubleAdvance()
            {
                const string name = "gc020-presentation-rate-does-not-double-advance";
                var runs = new List<SecondaryFixture>();
                try
                {
                    var committed = new List<ulong>();
                    var velocities = new List<int>();
                    var details = new List<string>();
                    bool allReady = true;

                    for (int i = 0; i < PresentationRates.Length; i++)
                    {
                        int rate = PresentationRates[i];
                        SecondaryFixture run = SecondaryFixture.Build(
                            family, sessionSequence, "traversal-rate-" + rate.ToString(CultureInfo.InvariantCulture), hostTicksNow);
                        runs.Add(run);
                        if (!run.Ready)
                        {
                            allReady = false;
                            details.Add(rate.ToString(CultureInfo.InvariantCulture) + ":<not-built>");
                            continue;
                        }

                        run.SubmitMovement(0, 0);
                        committed.Add(run.PumpSamplesPerSecond(rate));
                        velocities.Add(run.VelocityX);
                        details.Add(rate.ToString(CultureInfo.InvariantCulture)
                            + ":" + committed[committed.Count - 1].ToString(CultureInfo.InvariantCulture)
                            + "/" + velocities[velocities.Count - 1].ToString(CultureInfo.InvariantCulture));
                    }

                    bool sameSteps = allReady && committed.Count == PresentationRates.Length;
                    for (int i = 1; i < committed.Count; i++)
                    {
                        sameSteps &= committed[i] == committed[0];
                    }

                    bool sameVelocity = allReady && velocities.Count == PresentationRates.Length;
                    for (int i = 1; i < velocities.Count; i++)
                    {
                        sameVelocity &= velocities[i] == velocities[0];
                    }

                    // The expected endpoint is the reference's own arithmetic applied once per committed step, never a
                    // hard-coded literal: seeded velocity plus the tailwind step repeated N times (07 s4.3).
                    ulong expectedSteps = TraversalRegistration.TicksPerSecond / family.StepDurationTicks;
                    int expectedVelocity = family.SeededVelocityMilli
                        + (family.ExpectedAcceleratedVelocityMilli - family.SeededVelocityMilli)
                            * (int)expectedSteps;

                    bool reachable = committed.Count > 0
                        && committed[0] == expectedSteps
                        && velocities.Count > 0
                        && velocities[0] == expectedVelocity;

                    int extraFrames = PresentationRates[PresentationRates.Length - 1];
                    bool extraFramesCommitNoExtraStep = committed.Count > 0
                        && (ulong)extraFrames > committed[committed.Count - 1]
                        && sameSteps;

                    bool pass = allReady
                        && sameSteps
                        && sameVelocity
                        && reachable
                        && extraFramesCommitNoExtraStep
                        && committed.Count == 3
                        && runs.Count == 3;

                    Add(name, pass,
                        "runs=" + Join(details)
                        + "; expectedStepsPerSecond=" + expectedSteps.ToString(CultureInfo.InvariantCulture)
                        + "; expectedVelocity=" + expectedVelocity.ToString(CultureInfo.InvariantCulture)
                        + "; sameSteps=" + sameSteps
                        + "; sameVelocity=" + sameVelocity
                        + "; extra144HzFrames=" + extraFrames.ToString(CultureInfo.InvariantCulture)
                        + "; maxStepsPerPump=" + family.MaxStepsPerPump.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
                finally
                {
                    for (int i = runs.Count - 1; i >= 0; i--)
                    {
                        runs[i].Dispose();
                    }
                }
            }

            // ------------------------------------------------------------------ 6. the reparent

            /// <summary>
            /// Reparenting the valley runner scope into the ridge changes the CONTRIBUTION the runner derives
            /// (`+2 -> -1`, so `1.04 -> 1.02` after one more step) while leaving the stable target identity, its
            /// seeded live state and its committed progress exactly as they were: scope movement is composition
            /// movement, not Transform movement and not state transfer (07 s4.3 REF-A01, P-025).
            /// </summary>
            private void ReparentChangesTheContributionAndKeepsState()
            {
                const string name = "gc020-reparent-changes-the-contribution-and-keeps-state";
                try
                {
                    if (host == null || time == null || lane == null || publisher == null || targets == null
                        || seeder == null || stageRuntime == null)
                    {
                        Add(name, false, "the world or its stage runtime is missing");
                        return;
                    }

                    // The ridge's provider has to be installed before the move, or the moved runner would derive no
                    // contribution at all instead of the ridge's own (07 s4.1's `Ridge [Headwind]`).
                    bool secondMounted = PublishEdit(family.MountSecondProvider(), "mount-headwind");

                    TraversalVector3i velocityBeforeMove = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseBeforeMove = PoseOf(family.VelocityAssertedTarget);
                    TraversalProgressRow progressBefore = ReadProgress(family.VelocityAssertedTarget);
                    int liveStateBefore = LiveMotionSlotValue(family.VelocityAssertedTarget);

                    bool reparented = PublishEdit(family.ScopeReparent(), "reparent-valley-runners");

                    TraversalVector3i poseAfterPublication = PoseOf(family.VelocityAssertedTarget);
                    TraversalVector3i velocityAfterPublication = VelocityOf(family.VelocityAssertedTarget);
                    bool publicationMovedNothing = poseAfterPublication.Equals(poseBeforeMove)
                        && velocityAfterPublication.Equals(velocityBeforeMove);

                    bool moved = targets.TryGet(family.VelocityAssertedTarget, out LiveTarget live)
                        && live.Scope.Equals(family.MoveDestination);

                    ulong committed = StepOnce();

                    TraversalVector3i velocityAfter = VelocityOf(family.VelocityAssertedTarget);
                    TraversalProgressRow progressAfter = ReadProgress(family.VelocityAssertedTarget);
                    int liveStateAfter = LiveMotionSlotValue(family.VelocityAssertedTarget);
                    bool ridgeRow = TryActiveBindingValue(family.VelocityAssertedTarget, out int ridgeValue)
                        && ridgeValue == family.SecondProviderValue
                        && ridgeValue == TraversalVocabulary.HeadwindMilli;

                    bool stateKept = progressBefore.Runner.Equals(family.VelocityAssertedTarget)
                        && progressAfter.Runner.Equals(family.VelocityAssertedTarget)
                        && progressAfter.Count == progressBefore.Count
                        && progressAfter.Started == progressBefore.Started
                        && progressAfter.LastCheckpoint.Equals(progressBefore.LastCheckpoint)
                        && liveStateAfter == liveStateBefore
                        && liveStateBefore == family.MutableValue;

                    bool pass = secondMounted
                        && reparented
                        && moved
                        && publicationMovedNothing
                        && committed == 1UL
                        && velocityBeforeMove.X == family.ExpectedAcceleratedVelocityMilli
                        && velocityAfter.X == family.ExpectedReparentedVelocityMilli
                        && ridgeRow
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "headwindMounted=" + secondMounted
                        + "; reparented=" + reparented
                        + "; target=" + family.VelocityAssertedTarget.ToString()
                        + "; scope=" + (targets.TryGet(family.VelocityAssertedTarget, out LiveTarget l) ? l.Scope.ToString() : "<not-live>")
                        + "; destination=" + family.MoveDestination.ToString()
                        + "; velocity=" + velocityBeforeMove.ToString() + "->" + velocityAfter.ToString()
                        + "; expectedVelocity=" + family.ExpectedReparentedVelocityMilli.ToString(CultureInfo.InvariantCulture)
                        + "; publishedPose=" + poseAfterPublication.ToString()
                        + "; publicationMovedNothing=" + publicationMovedNothing
                        + "; ridgeRow=" + ridgeRow
                        + "; progress=" + progressBefore.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + progressAfter.Count.ToString(CultureInfo.InvariantCulture)
                        + "; lastCheckpoint=" + progressAfter.LastCheckpoint.ToString()
                        + "; liveMotionSlot=" + liveStateBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + liveStateAfter.ToString(CultureInfo.InvariantCulture)
                        + "; committedSteps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 7. the unmount

            /// <summary>
            /// Unmounting the acceleration providers removes their configuration contribution and nothing else: the
            /// asserted runner carries no active acceleration row afterwards, one more step leaves its velocity
            /// exactly where it was (no reset, no residual modifier), its pose and committed progress survive, and the
            /// crossing it already committed is still readable from the world's committed events (07 s4.3 REF-A02,
            /// P-045).
            /// </summary>
            private void UnmountKeepsPoseProgressAndReceipts()
            {
                const string name = "gc020-unmount-keeps-pose-progress-and-receipts";
                try
                {
                    if (host == null || time == null || publisher == null || stageRuntime == null)
                    {
                        Add(name, false, "the world or its stage runtime is missing");
                        return;
                    }

                    int crossingsBefore = stageRuntime.Module != null ? stageRuntime.Module.CrossingCount : 0;

                    // After observation 6 the asserted runner sits in the ridge, so the contribution that reaches it
                    // is headwind; the tailwind installation is removed too, so the observation covers both the
                    // effective modifier and an installation that reaches no live runner at all (07 s4.3 REF-A02).
                    bool unmountedEffective = PublishEdit(
                        TraversalCourseComposition.Unmount(TraversalCourseComposition.HeadwindInstance),
                        "unmount-headwind");
                    bool unmountedProvider = PublishEdit(
                        TraversalCourseComposition.Unmount(TraversalCourseComposition.TailwindInstance),
                        "unmount-tailwind");

                    TraversalVector3i velocityBefore = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseBefore = PoseOf(family.VelocityAssertedTarget);
                    TraversalProgressRow progressBefore = ReadProgress(family.VelocityAssertedTarget);
                    bool rowGoneAfterUnmount = !TryActiveBindingValue(family.VelocityAssertedTarget, out int _);
                    ulong committed = StepOnce();

                    TraversalVector3i velocityAfter = VelocityOf(family.VelocityAssertedTarget);
                    TraversalVector3i poseAfter = PoseOf(family.VelocityAssertedTarget);
                    TraversalProgressRow progressAfter = ReadProgress(family.VelocityAssertedTarget);

                    int readableCrossings = CountReadableCrossingEvents(out int eventCount);
                    bool receiptReadable = crossingsBefore > 0 && readableCrossings >= crossingsBefore;

                    bool poseAdvanced = poseAfter.X - poseBefore.X
                        == family.SeededVelocityMilli * family.StepMilliseconds / TraversalVocabulary.MilliScale;
                    bool progressKept = progressBefore.Runner.Equals(family.VelocityAssertedTarget)
                        && progressAfter.Runner.Equals(family.VelocityAssertedTarget)
                        && progressAfter.Count == progressBefore.Count
                        && progressAfter.LastCheckpoint.Equals(progressBefore.LastCheckpoint);

                    bool pass = unmountedEffective
                        && unmountedProvider
                        && rowGoneAfterUnmount
                        && committed == 1UL
                        && velocityAfter.Equals(velocityBefore)
                        && !TryActiveBindingValue(family.VelocityAssertedTarget, out int _)
                        && poseAdvanced
                        && progressKept
                        && receiptReadable
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "unmountedHeadwind=" + unmountedEffective
                        + "; unmountedTailwind=" + unmountedProvider
                        + "; activeRowAfterUnmount=" + !rowGoneAfterUnmount
                        + "; velocity=" + velocityBefore.ToString() + "->" + velocityAfter.ToString()
                        + "; poseAdvancedX=" + (poseAfter.X - poseBefore.X).ToString(CultureInfo.InvariantCulture)
                        + "; progress=" + progressBefore.Count.ToString(CultureInfo.InvariantCulture)
                        + "->" + progressAfter.Count.ToString(CultureInfo.InvariantCulture)
                        + "; lastCheckpoint=" + progressAfter.LastCheckpoint.ToString()
                        + "; crossingsBeforeUnmount=" + crossingsBefore.ToString(CultureInfo.InvariantCulture)
                        + "; readableCrossingEvents=" + readableCrossings.ToString(CultureInfo.InvariantCulture)
                        + "/" + eventCount.ToString(CultureInfo.InvariantCulture)
                        + "; committedSteps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 8. the mode switch

            /// <summary>
            /// `Automatic -> Conservative -> Automatic`: in Conservative mode only the runner whose descriptor
            /// declares the complete explicit opt-in keeps its binding, every automatically eligible runner loses it
            /// and the isolated showcase runner never had one; switching back restores exactly the automatic set.
            /// Neither direction teleports a runner or resets its velocity (07 s4.3, P-013, P-014, P-016).
            /// </summary>
            private void ModeSwitchBothDirections()
            {
                const string name = "gc020-mode-switch-both-directions";
                try
                {
                    if (host == null || lane == null || publisher == null || targets == null || stageRuntime == null)
                    {
                        Add(name, false, "the world or its stage runtime is missing");
                        return;
                    }

                    if (!targets.Contains(family.OptedInTarget))
                    {
                        bool seeded = seeder != null
                            && family.SeedOptedInTarget(new Gc013WorldContext(host, targets, seeder));
                        if (!seeded)
                        {
                            Add(name, false, "the explicitly opted-in runner could not be seeded");
                            return;
                        }
                    }

                    // The providers are remounted here because observation 7 removed them: the mode switch has to be
                    // observed with the contributions present, or "the plain runner loses it" would be vacuous.
                    bool tailwindMounted = PublishEdit(family.MountProvider(), "remount-tailwind");
                    bool headwindMounted = PublishEdit(family.MountSecondProvider(), "remount-headwind");

                    TraversalVector3i velocityBefore = VelocityOf(family.VelocityAssertedTarget);

                    var automatic = new List<string>();
                    bool automaticAsserted = TryActiveBindingValue(family.VelocityAssertedTarget, out int automaticValue);
                    automatic.Add("plain=" + DescribeRow(family.VelocityAssertedTarget));
                    automatic.Add("optedIn=" + DescribeRow(family.OptedInTarget));
                    automatic.Add("isolated=" + DescribeRow(family.IsolatedTarget));

                    bool conservative = PublishEdit(family.ModeSet(PropagationMode.Conservative), "mode-conservative");
                    bool optedInKept = TryActiveBindingValue(family.OptedInTarget, out int optedInConservative)
                        && optedInConservative == family.ProviderValue;
                    bool plainLost = !TryActiveBindingValue(family.VelocityAssertedTarget, out int _);
                    bool isolatedNeverHad = !TryActiveBindingValue(family.IsolatedTarget, out int _);
                    var conservativeRows = new List<string>
                    {
                        "plain=" + DescribeRow(family.VelocityAssertedTarget),
                        "optedIn=" + DescribeRow(family.OptedInTarget),
                        "isolated=" + DescribeRow(family.IsolatedTarget),
                    };

                    bool automaticAgain = PublishEdit(family.ModeSet(PropagationMode.Automatic), "mode-automatic");
                    bool plainRegained = TryActiveBindingValue(family.VelocityAssertedTarget, out int regained)
                        && regained == family.ProviderValue;
                    bool optedInStill = TryActiveBindingValue(family.OptedInTarget, out int _);
                    bool isolatedStillSilent = !TryActiveBindingValue(family.IsolatedTarget, out int _);

                    TraversalVector3i velocityAfter = VelocityOf(family.VelocityAssertedTarget);

                    bool pass = tailwindMounted
                        && headwindMounted
                        && automaticAsserted
                        && TryActiveBindingValue(family.OptedInTarget, out int _)
                        && conservative
                        && optedInKept
                        && plainLost
                        && isolatedNeverHad
                        && automaticAgain
                        && plainRegained
                        && optedInStill
                        && isolatedStillSilent
                        && velocityAfter.Equals(velocityBefore)
                        && lane.Committed.Mode == PropagationMode.Automatic
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "automatic=[" + Join(automatic) + "]"
                        + "; conservative=[" + Join(conservativeRows) + "]"
                        + "; automaticAgain=plain:" + DescribeRow(family.VelocityAssertedTarget)
                        + "; optedInKept=" + optedInKept
                        + "; plainLost=" + plainLost
                        + "; plainRegained=" + plainRegained
                        + "; isolatedNeverHad=" + isolatedNeverHad
                        + "; velocity=" + velocityBefore.ToString() + "->" + velocityAfter.ToString()
                        + "; mode=" + lane.Committed.Mode
                        + "; optInRows=" + DescribeOptIn(family.OptedInTarget)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 9. the future runner

            /// <summary>
            /// A runner spawned beneath the tailwind provider derives the modifier BEFORE its first executable step
            /// and without any per-instance import: its recipe is the ordinary runner recipe, and the binding row the
            /// spawn publication writes already names the provider installation the rule belongs to (07 s4.3, P-013,
            /// P-024).
            /// </summary>
            private void FutureDescendantDerivesBeforeExecution()
            {
                const string name = "gc020-future-descendant-derives-before-execution";
                try
                {
                    if (host == null || time == null || publisher == null || targets == null || seeder == null
                        || stageRuntime == null)
                    {
                        Add(name, false, "the world or its stage runtime is missing");
                        return;
                    }

                    // The publication the spawn rides on must carry no derivable target change: a scope no target
                    // lives in is exactly that, and it is what lets the spawn publish the world's half of that same
                    // composition publication (P-006, P-024).
                    bool neutralEdit = ApplyEdit(
                        family.SpareScopeEdits[1], "spare-scope-b", out DerivedAssemblyReport forward);
                    bool forwardClear = forward.Outcome == DerivedAssemblyOutcome.NoTargetChange;

                    LogicalStepId stepBeforeSpawn = host.CurrentStep;
                    family.PrepareSpawn();
                    DerivedAssemblyReport spawn = pipeline!.PublishSpawn(
                        NextOperation(host.World),
                        family.FutureTarget,
                        family.FutureRecipe,
                        family.FutureScope);

                    // The spawn published the world's half of the neutral publication, so the one series is joined
                    // again and the publication is counted here rather than beside the edit that left it pending
                    // (P-006).
                    NotePublication("spare-scope-b");
                    // The composition index learns about the spawned target here, exactly the way the GC-013 sequence
                    // does: the spawn published the world's assembly for it, and the index registration makes it a
                    // live target the next edit's derivation sees (P-024).
                    bool registered = targets.TryRegister(
                        family.FutureTarget,
                        family.FutureScope,
                        family.FutureRecipe,
                        out DiagnosticCode registerCode,
                        out string registerDetail);

                    bool live = targets.Contains(family.FutureTarget);
                    bool spawned = neutralEdit
                        && forwardClear
                        && registered
                        && spawn.IsSpawn
                        && spawn.Outcome == DerivedAssemblyOutcome.Published
                        && spawn.CountersJoined
                        && live;

                    bool rowBeforeExecution = TryActiveBindingValue(family.FutureTarget, out int rowValue)
                        && rowValue == family.ProviderValue;
                    bool inPublishedAssembly = publisher.Published.Bindings.HasTarget(family.FutureTarget);

                    bool noImport = true;
                    string importDetail = "<none>";
                    if (targets.TryDescriptorOf(family.FutureTarget, out TargetDescriptor? descriptor, out DiagnosticCode code, out string detail)
                        && descriptor != null)
                    {
                        noImport = descriptor.Imports.Count == 0
                            && descriptor.OptIns.Count == 0
                            && descriptor.LocalPatches.Count == 0;
                        importDetail = "imports=" + descriptor.Imports.Count.ToString(CultureInfo.InvariantCulture)
                            + ";optIns=" + descriptor.OptIns.Count.ToString(CultureInfo.InvariantCulture)
                            + ";patches=" + descriptor.LocalPatches.Count.ToString(CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        noImport = false;
                        importDetail = "descriptor missing: " + code + ": " + detail;
                    }

                    // The spawned runner joins the course: the module is bound to its entity under the ordinary
                    // runner recipe, exactly the way every target the gate attached to was bound, so the five stages
                    // really own the new descendant from its next step on (P-005, P-024).
                    TraversalModule? module = stageRuntime.Module;
                    Entity spawnedEntity = EntityOf(family.FutureTarget);
                    if (module != null && spawned && spawnedEntity != Entity.Null)
                    {
                        module.BindRunner(family.FutureTarget, spawnedEntity, family.FutureRecipe);
                    }

                    bool bound = false;
                    if (module != null)
                    {
                        IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
                        for (int i = 0; i < runners.Count; i++)
                        {
                            bound |= runners[i].Target.Equals(family.FutureTarget);
                        }
                    }

                    // "Before its first executable step": the spawn publication committed no logical step, so the row
                    // cannot have come from an integration — it is the rule the spawn itself resolved (P-024).
                    bool stepUnchanged = host.CurrentStep.Equals(stepBeforeSpawn);

                    bool pass = spawned
                        && stepUnchanged
                        && rowBeforeExecution
                        && inPublishedAssembly
                        && noImport
                        && bound
                        && stageRuntime.Module != null
                        && module!.RunnerCount >= family.CourseTargets.Count
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "spawned=" + spawned
                        + "; outcome=" + spawn.Outcome
                        + "; target=" + family.FutureTarget.ToString()
                        + "; scope=" + family.FutureScope.ToString()
                        + "; registered=" + registered
                        + (registered ? string.Empty : "(" + registerCode + ": " + registerDetail + ")")
                        + "; spawnCountersJoined=" + spawn.CountersJoined
                        + "; descriptor=" + importDetail
                        + "; bound=" + bound
                        + "; ownedRunners=" + (stageRuntime.Module != null ? stageRuntime.Module.RunnerCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; currentStep=" + stepBeforeSpawn.Value.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 10. one simulation per step

            /// <summary>
            /// With the REAL local physics scene installed for the course's declared external domain, one engine
            /// simulation happens per ADMITTED step and never per host frame: a teleport intent is applied before the
            /// simulation (the first observed pose reflects it), a repeated call for the same admitted step is
            /// refused, and a 30/60/144-shaped frame sequence adds no extra simulation (04 s7, REF-A05, REF-A06).
            /// </summary>
            private void OneSimulationPerAdmittedStep()
            {
                const string name = "gc020-one-simulation-per-admitted-step";
                try
                {
                    if (host == null || time == null || targets == null || seeder == null || stageRuntime == null
                        || stageRuntime.Module == null || stageRuntime.Physics == null || stageRuntime.PhysicsGate == null)
                    {
                        Add(name, false, "the world has no local physics scene attached (P-034, 04 s7)");
                        return;
                    }

                    UnityPhysicsSceneBackend physics = stageRuntime.Physics;
                    PhysicsAuthorityGate gate = stageRuntime.PhysicsGate;
                    TraversalModule module = stageRuntime.Module;

                    // One body per owner-runner, in the course's declared motion domain: the domain the rigidbody
                    // option makes the engine's own (07 s4.2, P-034).
                    var declared = new List<string>();
                    IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
                    for (int i = 0; i < runners.Count; i++)
                    {
                        TraversalRunnerRef runner = runners[i];
                        var key = new PhysicsBodyKey(runner.Target, TraversalKeys.MotionDomain.Id.Value);
                        PhysicsPose initial = new PhysicsPose(
                            AsPhysics(PoseOf(runner.Target)), AsPhysics(VelocityOf(runner.Target)));
                        PhysicsBodyOutcome outcome = gate.TryDeclareBody(key, initial, out DiagnosticCode code, out string detail);
                        declared.Add(runner.Target.ToString() + "=" + outcome);
                        if (outcome != PhysicsBodyOutcome.Applied)
                        {
                            Add(name, false, "declaring the body of " + runner.Target.ToString() + " was refused: "
                                + code + ": " + detail);
                            return;
                        }
                    }

                    ulong stepsBeforePhysics = host.CurrentStep.Value;
                    int simulationsBefore = physics.SimulateCount;
                    var firstKey = new PhysicsBodyKey(runners[0].Target, TraversalKeys.MotionDomain.Id.Value);
                    var teleportTo = new PhysicsVector3i(5000, 0, 0);
                    PhysicsBodyOutcome applied = gate.TryApplyIntent(
                        firstKey,
                        AuthorityIntentKind.Teleport,
                        PhysicsIntentCodec.WriteTeleport(teleportTo),
                        out string intentDetail);

                    // The intent must be applied BEFORE the simulation: reading the engine's pose right now, with no
                    // simulation in between, already shows the teleport (04 s7).
                    bool poseReflectsIntent = gate.TryReadPose(firstKey, out PhysicsPose poseAfterIntent)
                        && poseAfterIntent.Position == teleportTo;

                    int simulated = 0;
                    for (int i = 0; i < PhysicsSteps; i++)
                    {
                        ulong committed = StepOnce();
                        if (committed == 1UL && gate.TrySimulateExactlyOnce(host.CurrentStep.Value, 0.02d, out string _))
                        {
                            simulated++;
                        }
                    }

                    int simulationsAfterSteps = physics.SimulateCount;
                    bool onePerStep = simulationsAfterSteps - simulationsBefore == PhysicsSteps
                        && simulated == PhysicsSteps
                        && simulationsAfterSteps - simulationsBefore
                            == (int)(host.CurrentStep.Value - stepsBeforePhysics);

                    int duplicateBefore = gate.DuplicateStepRefusalCount;
                    bool duplicateRefused = !gate.TrySimulateExactlyOnce(host.CurrentStep.Value, 0.02d, out string duplicateDetail);
                    bool duplicateCounted = gate.DuplicateStepRefusalCount == duplicateBefore + 1;

                    // A 30/60/144-shaped frame sequence: three host frames at the three REF-A06 rates. Whatever steps
                    // those frames admit get exactly one simulation each, so the frame count never becomes the
                    // simulation count.
                    int framesBefore = 0;
                    int stepsBeforeFrames = (int)host.CurrentStep.Value;
                    int simulationsBeforeFrames = physics.SimulateCount;
                    int[] rates = { 30, 60, 144 };
                    for (int i = 0; i < rates.Length; i++)
                    {
                        framesBefore++;
                        hostTicksNow += TraversalRegistration.TicksPerSecond / (ulong)rates[i];
                        TimeFrameReport frame = time.PumpFrame(hostTicksNow);
                        if (frame.StepsCommitted > 0UL
                            && gate.TrySimulateExactlyOnce(host.CurrentStep.Value, 0.02d, out string _))
                        {
                            // one simulation for the newest admitted step this frame
                        }
                    }

                    int stepsAdmittedByFrames = (int)host.CurrentStep.Value - stepsBeforeFrames;
                    int simulationsByFrames = physics.SimulateCount - simulationsBeforeFrames;
                    bool framesAddNoExtraSimulation = simulationsByFrames == stepsAdmittedByFrames
                        && simulationsByFrames <= framesBefore
                        && physics.SimulateCount - simulationsBefore
                            == (int)(host.CurrentStep.Value - stepsBeforePhysics);

                    bool pass = applied == PhysicsBodyOutcome.Applied
                        && poseReflectsIntent
                        && gate.Bodies.Count == runners.Count
                        && onePerStep
                        && duplicateRefused
                        && duplicateCounted
                        && framesAddNoExtraSimulation
                        && physics.IsDedicatedLocalScene
                        && physics.AutomaticSimulationSuppressed
                        && !physics.DefaultPhysicsScene.Equals(physics.Scene.GetPhysicsScene())
                        && gate.SteppedStepCount == simulationsAfterSteps - simulationsBefore + simulationsByFrames;

                    Add(name, pass,
                        "bodies=" + Join(declared)
                        + "; intent=" + applied + "(" + intentDetail + ")"
                        + "; poseReflectsIntent=" + poseReflectsIntent
                        + "; steps=" + stepsBeforePhysics.ToString(CultureInfo.InvariantCulture)
                        + "->" + host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)
                        + "; simulations=" + simulationsBefore.ToString(CultureInfo.InvariantCulture)
                        + "->" + simulationsAfterSteps.ToString(CultureInfo.InvariantCulture)
                        + "->" + physics.SimulateCount.ToString(CultureInfo.InvariantCulture)
                        + "; simulatedExactlyOnce=" + simulated.ToString(CultureInfo.InvariantCulture)
                        + "; onePerAdmittedStep=" + onePerStep
                        + "; duplicateRefused=" + duplicateRefused + "(" + duplicateDetail + ")"
                        + "; duplicateRefusals=" + gate.DuplicateStepRefusalCount.ToString(CultureInfo.InvariantCulture)
                        + "; frameSequenceFrames=" + framesBefore.ToString(CultureInfo.InvariantCulture)
                        + "; frameSequenceSteps=" + stepsAdmittedByFrames.ToString(CultureInfo.InvariantCulture)
                        + "; frameSequenceSimulations=" + simulationsByFrames.ToString(CultureInfo.InvariantCulture)
                        + "; dedicatedLocalScene=" + physics.IsDedicatedLocalScene
                        + "; automaticSuppressed=" + physics.AutomaticSimulationSuppressed
                        + "; steppedSteps=" + gate.SteppedStepCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 11. external ownership

            /// <summary>
            /// Declaring the engine as the motion authority for the runner recipe makes the integrator leave those
            /// poses alone: `MotionDecisionOf` reports `ExternallyOwned`, the step counts every owned runner as
            /// externally owned and integrates none, and re-selecting the other authority is refused with
            /// `OwnershipConflict` while the selected authority stays what it was — validation rejects the ownership
            /// conflict instead of a write-order workaround (07 s4.2 REF-A05, P-034).
            /// </summary>
            private void ExternallyOwnedPoseIsNotIntegrated()
            {
                const string name = "gc020-externally-owned-pose-is-not-integrated";
                try
                {
                    if (host == null || stageRuntime == null || stageRuntime.Module == null)
                    {
                        Add(name, false, "the world or its traversal module is missing");
                        return;
                    }

                    TraversalModule module = stageRuntime.Module;

                    // The engine owns the course's runners. The authority is declared per RECIPE, and the course's
                    // two runner recipes are the runner recipe and the isolated display runner's, so both are
                    // declared: declaring only one would leave the other runner integrated and "no double
                    // simulation" would be a statement about a subset of the course rather than about it (P-034).
                    bool selected = module.TrySelectMotionAuthority(
                        family.FutureRecipe,
                        TraversalMotionMode.ExternalRigidbody,
                        out DiagnosticCode selectCode,
                        out string selectDetail);
                    bool displaySelected = module.TrySelectMotionAuthority(
                        TraversalKeys.DisplayRunnerRecipe,
                        TraversalMotionMode.ExternalRigidbody,
                        out DiagnosticCode displayCode,
                        out string displayDetail);
                    bool decision = module.MotionDecisionOf(family.FutureRecipe) == TraversalMotionDecision.ExternallyOwned
                        && module.MotionDecisionOf(TraversalKeys.DisplayRunnerRecipe)
                            == TraversalMotionDecision.ExternallyOwned;

                    TraversalVector3i poseBefore = PoseOf(family.FutureTarget);
                    int integratedBefore = module.IntegratedRunnerStepCount;
                    int refusedBefore = module.RefusedIntegrationCount;

                    ulong committed = StepOnce();

                    int integratedDelta = module.IntegratedRunnerStepCount - integratedBefore;
                    TraversalVector3i poseAfter = PoseOf(family.FutureTarget);

                    bool refused = !module.TrySelectMotionAuthority(
                        family.FutureRecipe,
                        TraversalMotionMode.Kinematic,
                        out DiagnosticCode conflictCode,
                        out string conflictDetail);
                    TraversalMotionMode stillSelected = module.MotionAuthorityOf(family.FutureRecipe);

                    bool pass = selected
                        && displaySelected
                        && decision
                        && committed == 1UL
                        && module.LastIntegratedCount == 0
                        && module.LastExternallyOwnedCount == module.RunnerCount
                        && integratedDelta == 0
                        && refused
                        && conflictCode == DiagnosticCode.OwnershipConflict
                        && stillSelected == TraversalMotionMode.ExternalRigidbody
                        && !stillSelected.Equals(TraversalMotionMode.Kinematic)
                        && poseAfter.Equals(poseBefore)
                        && module.RefusedIntegrationCount == refusedBefore
                        && MatchesPublishedAssembly();

                    Add(name, pass,
                        "selected=" + selected + "(" + selectCode + (selected ? string.Empty : ": " + selectDetail) + ")"
                        + "; displaySelected=" + displaySelected + "(" + displayCode
                        + (displaySelected ? string.Empty : ": " + displayDetail) + ")"
                        + "; decision=" + module.MotionDecisionOf(family.FutureRecipe)
                        + "; authority=" + stillSelected
                        + "; integratedInStep=" + module.LastIntegratedCount.ToString(CultureInfo.InvariantCulture)
                        + "; externallyOwnedInStep=" + module.LastExternallyOwnedCount.ToString(CultureInfo.InvariantCulture)
                        + "; ownedRunners=" + module.RunnerCount.ToString(CultureInfo.InvariantCulture)
                        + "; integratedDelta=" + integratedDelta.ToString(CultureInfo.InvariantCulture)
                        + "; reselectRefused=" + refused + "(" + conflictCode + ": " + conflictDetail + ")"
                        + "; pose=" + poseBefore.ToString() + "->" + poseAfter.ToString()
                        + "; committedSteps=" + committed.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 12. committed presentation

            /// <summary>
            /// The committed step's result is presented from immutable images: a committed pose image presents once
            /// and is refused while the token is unchanged; a root-motion proposal is queued and taken for the NEXT
            /// admitted step; every committed crossing event plays exactly once over the world's own committed-event
            /// reader, a second pass suppresses all of them, and a fresh stage over a disabled device refuses audibly
            /// while recording nothing (04 s7, P-045).
            /// </summary>
            private void CommittedAnimationAndAudioOutput()
            {
                const string name = "gc020-committed-animation-and-audio-output";
                try
                {
                    if (host == null || stageRuntime == null || stageRuntime.PoseSource == null
                        || stageRuntime.Animation == null || stageRuntime.AnimationSink == null
                        || stageRuntime.Audio == null || stageRuntime.AudioSink == null)
                    {
                        Add(name, false, "the committed-output adapters are missing");
                        return;
                    }

                    RecordingPoseSource poseSource = stageRuntime.PoseSource;
                    CommittedAnimationStage animation = stageRuntime.Animation;
                    RecordingAnimationSink animationSink = stageRuntime.AnimationSink;
                    CommittedAudioStage audio = stageRuntime.Audio;
                    RecordingAudioSink audioSink = stageRuntime.AudioSink;

                    SnapshotToken token = new SnapshotToken(host.World, host.CurrentEpoch, host.CurrentStep);
                    var poses = new List<CommittedPose>
                    {
                        new CommittedPose(
                            family.VelocityAssertedTarget,
                            PoseOf(family.VelocityAssertedTarget).X,
                            PoseOf(family.VelocityAssertedTarget).Y,
                            PoseOf(family.VelocityAssertedTarget).Z,
                            1),
                    };

                    poseSource.Publish(token, poses);
                    bool presentedOnce = animation.TryPresentOnce(out string firstDetail);
                    bool presentedAgain = animation.TryPresentOnce(out string secondDetail);

                    bool proposalQueued = animation.TryOfferRootMotion(new RootMotionProposal(
                        family.VelocityAssertedTarget, 5, 0, 0, token));
                    IReadOnlyList<RootMotionProposal> taken = animation.TakePendingForNextStep();

                    int crossings = stageRuntime.Module != null ? stageRuntime.Module.CrossingCount : 0;
                    int playedFirst = audio.PresentNextPage(TraversalKeys.CrossingCapacity);
                    int playedSecond = audio.PresentNextPage(TraversalKeys.CrossingCapacity);

                    // A fresh stage over the SAME committed events with a disabled device: the cue resolves (the
                    // event is audible) but nothing is recorded, so the loss is reported rather than silent.
                    var disabledSink = new RecordingAudioSink { Unavailable = true };
                    var disabledCues = new AudioCueTable();
                    disabledCues.TryAdd(
                        TraversalKeys.CrossingSchema,
                        TraversalIdentity.Id("gc020.audio.cue.checkpoint-passed"));
                    WorldMessagePlane? plane = host.Messages;
                    CommittedAudioStage? disabled = plane != null
                        ? new CommittedAudioStage(host.World, plane.Events, disabledSink, disabledCues)
                        : null;
                    int disabledRead = disabled != null ? disabled.PresentNextPage(TraversalKeys.CrossingCapacity) : -1;

                    bool crossingsPlayedOnce = crossings > 0
                        && audio.PlayedCount == crossings
                        && audio.AudibleCount == crossings
                        && audioSink.Plays.Count == crossings
                        && playedFirst == crossings
                        && playedSecond == 0
                        && audio.SuppressedCount == crossings;
                    bool disabledRecordsNothing = disabled != null
                        && disabled.PlayedCount == 0
                        && disabled.RefusedCount == crossings
                        && disabledSink.Plays.Count == 0
                        && disabledRead == 0;

                    bool pass = presentedOnce
                        && !presentedAgain
                        && animation.StaleRefusalCount == 1
                        && animationSink.PresentedTokens.Count == 1
                        && animationSink.LastPoseCount == poses.Count
                        && proposalQueued
                        && animation.ProposalCount == 1
                        && taken.Count == 1
                        && taken[0].Target.Equals(family.VelocityAssertedTarget)
                        && animation.PendingProposals.Count == 0
                        && crossingsPlayedOnce
                        && disabledRecordsNothing
                        && audio.ReadCount >= crossings;

                    Add(name, pass,
                        "poses=" + poses.Count.ToString(CultureInfo.InvariantCulture)
                        + "; presentedOnce=" + presentedOnce + "(" + firstDetail + ")"
                        + "; presentedAgain=" + presentedAgain + "(" + secondDetail + ")"
                        + "; staleRefusals=" + animation.StaleRefusalCount.ToString(CultureInfo.InvariantCulture)
                        + "; presentedTokens=" + animationSink.PresentedTokens.Count.ToString(CultureInfo.InvariantCulture)
                        + "; proposalsQueued=" + animation.ProposalCount.ToString(CultureInfo.InvariantCulture)
                        + "; proposalsTaken=" + taken.Count.ToString(CultureInfo.InvariantCulture)
                        + "; crossings=" + crossings.ToString(CultureInfo.InvariantCulture)
                        + "; played=" + audio.PlayedCount.ToString(CultureInfo.InvariantCulture)
                        + "; audible=" + audio.AudibleCount.ToString(CultureInfo.InvariantCulture)
                        + "; sinkPlays=" + audioSink.Plays.Count.ToString(CultureInfo.InvariantCulture)
                        + "; secondPassPlayed=" + playedSecond.ToString(CultureInfo.InvariantCulture)
                        + "; suppressed=" + audio.SuppressedCount.ToString(CultureInfo.InvariantCulture)
                        + "; disabledRefused=" + (disabled != null ? disabled.RefusedCount : -1).ToString(CultureInfo.InvariantCulture)
                        + "; disabledPlays=" + disabledSink.Plays.Count.ToString(CultureInfo.InvariantCulture)
                        + "; disabledRead=" + disabledRead.ToString(CultureInfo.InvariantCulture)
                        + "; audioRead=" + audio.ReadCount.ToString(CultureInfo.InvariantCulture)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 13. the other two genres

            /// <summary>
            /// The card and narrative genres declare no action phase: walking their real declarations and their
            /// compiled ownership surface finds no traversal stage, no traversal dispatch key, no
            /// `traversal.acceleration` capability and no traversal buffer — optional physics/animation stages are
            /// absent from a genre that has no fixed-step timer (P-001, P-059).
            /// </summary>
            private void CardsAndNarrativeDeclareNoActionPhase()
            {
                const string name = "gc020-cards-and-narrative-declare-no-action-phase";
                try
                {
                    TraversalCourseSurface surface = family.ActionSurface();
                    if (surface.Stages.Count != TraversalKeys.SystemKeys.Length
                        || surface.Systems.Count != TraversalKeys.SystemKeys.Length)
                    {
                        Add(name, false, "the traversal surface is not the five-stage graph: " + surface.ToString());
                        return;
                    }

                    CatalogBuildResult narrativeBuild = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
                    CatalogBuildResult cardBuild = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
                    ImmutableCatalog? narrativeCatalog = narrativeBuild.Catalog;
                    ImmutableCatalog? cardCatalog = cardBuild.Catalog;
                    if (narrativeCatalog == null || cardCatalog == null)
                    {
                        Add(name, false, "a neighbouring catalog was rejected: "
                            + (narrativeCatalog == null ? narrativeBuild.Describe() : string.Empty) + " | "
                            + (cardCatalog == null ? cardBuild.Describe() : string.Empty));
                        return;
                    }

                    var narrative = new Gc013NarrativeHost.NarrativeFamily(
                        narrativeCatalog,
                        Gc013NarrativeHost.Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                        ProbeCatalog.CatalogFingerprint);
                    var cards = new Gc013CardsHost.CardFamily(
                        cardCatalog,
                        Gc013CardsHost.Declarations(),
                        CardCatalog.CatalogFingerprint);

                    var offenders = new List<string>();
                    int walked = 0;
                    walked += WalkDeclarations("narrative", narrative.Declarations, surface, offenders);
                    walked += WalkDeclarations("cards", cards.Declarations, surface, offenders);

                    PipelineDescriptorReport narrativeReport = narrative.CompilePipeline();
                    PipelineDescriptorReport cardReport = cards.CompilePipeline();
                    walked += WalkDescriptor("narrative", narrativeReport, surface, offenders);
                    walked += WalkDescriptor("cards", cardReport, surface, offenders);

                    int walkedStages = CountDeclaredStages(narrative.Declarations) + CountDeclaredStages(cards.Declarations);

                    bool pass = narrativeReport.Succeeded
                        && cardReport.Succeeded
                        && offenders.Count == 0
                        && walked > 0
                        && walkedStages > 0
                        && narrative.Declarations.Count > 0
                        && cards.Declarations.Count > 0
                        && surface.Stages.Count == 5
                        && surface.Systems.Count == 5;

                    Add(name, pass,
                        "narrativeDeclarations=" + narrative.Declarations.Count.ToString(CultureInfo.InvariantCulture)
                        + "; cardDeclarations=" + cards.Declarations.Count.ToString(CultureInfo.InvariantCulture)
                        + "; declaredStages=" + walkedStages.ToString(CultureInfo.InvariantCulture)
                        + "; walkedEntries=" + walked.ToString(CultureInfo.InvariantCulture)
                        + "; narrativePipeline=" + narrativeReport.Outcome
                        + "; cardPipeline=" + cardReport.Outcome
                        + "; traversalStageIds=" + surface.Stages.Count.ToString(CultureInfo.InvariantCulture)
                        + "; traversalSystemKeys=" + surface.Systems.Count.ToString(CultureInfo.InvariantCulture)
                        + "; traversalCapability=" + surface.AccelerationCapability
                        + "; offenders=" + Join(offenders)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ 14. teardown

            /// <summary>
            /// Every lease of this run participates in the world lifecycle: the stage runtime's trace is detached and
            /// its module forgotten, the local physics scene is disposed (which restores the process's previous global
            /// simulation mode and makes the authority unavailable), and the world stops with no outstanding job, no
            /// retained resource and the owned-world registry back at its pre-create baseline (P-047, P-048).
            /// </summary>
            private void TeardownSettlesAndDisposes()
            {
                const string name = "gc020-teardown-settles-and-disposes";
                try
                {
                    if (host == null || time == null)
                    {
                        Add(name, false, "no world");
                        return;
                    }

                    ulong idleSteps = PumpIdleFrames();

                    Gc020StageRuntime runtime = stageRuntime!;
                    TraversalModule? module = runtime.Module;
                    UnityPhysicsSceneBackend? physics = runtime.Physics;
                    int playsBeforeTeardown = runtime.AudioSink != null ? runtime.AudioSink.Plays.Count : 0;

                    time.Clear(out int discardedCommands, out int pendingWakes);
                    _ = discardedCommands;
                    _ = pendingWakes;

                    int registryBeforeStop = UnityWorldRegistry.Count;
                    OperationResult stop = host.Stop(NextOperation(host.World), "gc-020 traversal gate teardown");
                    UnityWorldHost stopped = host;
                    stopped.Dispose();

                    runtime.Dispose();
                    stageRuntime = null;

                    bool traceDetached = module == null || TraversalStepTraceRegistry.Of(module) == null;
                    bool physicsDisposed = physics == null
                        || (!physics.IsAvailable && !physics.TrySimulate(0.02d, out string _));
                    bool globalModeRestored = physics == null || Physics.simulationMode == globalModeBeforeAttach;
                    // The scene handle is reported rather than asserted: Unity's unload is asynchronous, so the scene
                    // can still be valid in the frame dispose runs. What the gate does assert is the adapter's own
                    // availability gate and the restored global mode, which every later call consults (04 s7).

                    int outstanding = stopped.Ledger.OutstandingJobCount;
                    int retained = stopped.Ledger.RetainedResourceCount;
                    int registryAfter = UnityWorldRegistry.Count;

                    bool pass = idleSteps == 0UL
                        && (stop.Outcome == Outcome.Published || stop.Outcome == Outcome.NoChange)
                        && traceDetached
                        && physicsDisposed
                        && globalModeRestored
                        && (runtime.AudioSink == null || runtime.AudioSink.Plays.Count == playsBeforeTeardown)
                        && outstanding == 0
                        && retained == 0
                        && registryAfter == registryBeforeCreate
                        && registryBeforeStop == registryAfter + 1;

                    Add(name, pass,
                        "idleSteps=" + idleSteps.ToString(CultureInfo.InvariantCulture)
                        + "; stop=" + stop.Outcome + "(" + stop.Code + ")"
                        + "; lifecycle=" + stopped.Lifecycle
                        + "; traceDetached=" + traceDetached
                        + "; physicsDisposed=" + physicsDisposed
                        + "; globalMode=" + globalModeBeforeAttach + "->" + Physics.simulationMode
                        + "; physicsSceneValid=" + (physics != null && physics.Scene.IsValid())
                        + "; globalModeRestored=" + globalModeRestored
                        + "; audioPlaysKept=" + playsBeforeTeardown.ToString(CultureInfo.InvariantCulture)
                        + "; outstandingJobs=" + outstanding.ToString(CultureInfo.InvariantCulture)
                        + "; retainedResources=" + retained.ToString(CultureInfo.InvariantCulture)
                        + "; registryBeforeCreate=" + registryBeforeCreate.ToString(CultureInfo.InvariantCulture)
                        + "; registryBeforeStop=" + registryBeforeStop.ToString(CultureInfo.InvariantCulture)
                        + "; registryAfter=" + registryAfter.ToString(CultureInfo.InvariantCulture)
                        + "; publications=" + publications.ToString(CultureInfo.InvariantCulture)
                        + "; counterMismatches=" + counterMismatches.ToString(CultureInfo.InvariantCulture)
                        + (counterMismatches == 0 ? string.Empty : "; firstMismatch=" + firstMismatch)
                        + DescribeFailure());
                }
                catch (Exception exception)
                {
                    Add(name, false, DescribeException(exception));
                }
            }

            // ------------------------------------------------------------------ the control lane

            /// <summary>
            /// Applies one composition edit and publishes the world's assembly for that same publication. P-006 has one
            /// publication series, so an edit the world does not answer would leave the lane one publication ahead and
            /// every later adoption would be refused as stale: the two halves are always done together, and a
            /// `NoTargetChange` derivation is answered with the unchanged assembly so the counters stay joined.
            /// </summary>
            private bool PublishEdit(CompositionEditPayload payload, string label)
            {
                if (!ApplyEdit(payload, label, out DerivedAssemblyReport report))
                {
                    return false;
                }

                if (report.Outcome == DerivedAssemblyOutcome.NoTargetChange
                    && !PublishUnchangedAssembly(NextOperation(host!.World)))
                {
                    return false;
                }

                return NotePublication(label);
            }

            private bool ApplyEdit(CompositionEditPayload payload, string label, out DerivedAssemblyReport report)
            {
                report = new DerivedAssemblyReport { Outcome = DerivedAssemblyOutcome.Refused };
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
                if (report.Outcome == DerivedAssemblyOutcome.Refused)
                {
                    lastFailure = label + ": the world refused the assembly: " + report.Describe();
                    return false;
                }

                return true;
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
                    + ",rows=" + publisher.Published.BindingRowCount.ToString(CultureInfo.InvariantCulture)
                    + ",worldEpoch=" + host.CurrentEpoch.Value.ToString(CultureInfo.InvariantCulture);
            }

            /// <summary>Pumps the same host tick repeatedly: a fixed-step world with no elapsed time commits nothing.</summary>
            private ulong PumpIdleFrames()
            {
                if (time == null)
                {
                    return 0UL;
                }

                ulong committed = 0UL;
                for (int i = 0; i < IdlePumpFrames; i++)
                {
                    committed += time.PumpFrame(hostTicksNow).StepsCommitted;
                }

                return committed;
            }

            /// <summary>
            /// The most recently recorded step of this world's trace and one runner's body record inside it. The trace
            /// is the package's own declared observation of what a step integrated, so a numeric claim about "this step
            /// applied this acceleration" is read from the record rather than reconstructed here (P-008, TEST-022).
            /// </summary>
            private bool TryLastBodyOf(TargetId runner, out TraversalStepTrace step, out TraversalBodyTrace body)
            {
                step = null!;
                body = default(TraversalBodyTrace);
                TraversalTraceRecorder? trace = stageRuntime != null ? stageRuntime.Trace : null;
                if (trace == null || trace.Steps.Count == 0)
                {
                    return false;
                }

                TraversalStepTrace last = trace.Steps[trace.Steps.Count - 1];
                for (int i = 0; i < last.Bodies.Count; i++)
                {
                    if (last.Bodies[i].Runner.Equals(runner))
                    {
                        step = last;
                        body = last.Bodies[i];
                        return true;
                    }
                }

                return false;
            }

            /// <summary>Advances host time by exactly one declared step and pumps it (P-036).</summary>
            private ulong StepOnce()
            {
                if (time == null || host == null)
                {
                    return 0UL;
                }

                hostTicksNow += family.StepDurationTicks;
                return time.PumpFrame(hostTicksNow).StepsCommitted;
            }

            private Entity EntityOf(TargetId target)
            {
                if (seeder != null && seeder.TryGetEntity(target, out Entity entity))
                {
                    return entity;
                }

                return Entity.Null;
            }

            private TraversalVector3i PoseOf(TargetId target)
            {
                if (host == null)
                {
                    return TraversalVector3i.Zero;
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                Entity entity = EntityOf(target);
                return entityManager.Exists(entity) && entityManager.HasComponent<TraversalPose>(entity)
                    ? entityManager.GetComponentData<TraversalPose>(entity).Vector
                    : TraversalVector3i.Zero;
            }

            private TraversalVector3i VelocityOf(TargetId target)
            {
                if (host == null)
                {
                    return TraversalVector3i.Zero;
                }

                EntityManager entityManager = host.EntityWorld.EntityManager;
                Entity entity = EntityOf(target);
                return entityManager.Exists(entity) && entityManager.HasComponent<TraversalVelocity>(entity)
                    ? entityManager.GetComponentData<TraversalVelocity>(entity).Vector
                    : TraversalVector3i.Zero;
            }

            /// <summary>
            /// One millimetre-vector of the rules package as the physics adapter's own declared representation. The two
            /// packages state the same units (millimetres and thousandths of a metre per second), so this is a
            /// conversion between two identical integer layouts, never a scaling (04 s7, 05 s6).
            /// </summary>
            private static PhysicsVector3i AsPhysics(TraversalVector3i value) =>
                new PhysicsVector3i(value.X, value.Y, value.Z);

            private TraversalProgressRow ReadProgress(TargetId runner)
            {
                if (host == null || stageRuntime == null || stageRuntime.Module == null)
                {
                    return default(TraversalProgressRow);
                }

                TraversalAccess.TryReadProgress(
                    host.EntityWorld.EntityManager,
                    stageRuntime.Module.CourseEntity,
                    runner,
                    out TraversalProgressRow progress);
                return progress;
            }

            /// <summary>The seeded live motion slot of one target, or int.MinValue when the target holds none.</summary>
            private int LiveMotionSlotValue(TargetId target)
            {
                if (seeder == null || targets == null || !targets.Contains(target))
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

            /// <summary>True when the target's live owner scope is the given scope or a descendant of it (P-010).</summary>
            private bool IsUnderScope(TargetId target, ScopeId scope)
            {
                if (targets == null || lane == null || !targets.TryGet(target, out LiveTarget live))
                {
                    return false;
                }

                ScopeId cursor = live.Scope;
                for (int i = 0; i < lane.Committed.Scopes.Scopes.Count + 1; i++)
                {
                    if (cursor.Equals(scope))
                    {
                        return true;
                    }

                    if (!lane.Committed.Scopes.TryGet(cursor, out ScopeRecord? record) || record == null)
                    {
                        return false;
                    }

                    if (record.Parent.IsDefault)
                    {
                        return false;
                    }

                    cursor = record.Parent;
                }

                return false;
            }

            private bool TryActiveBindingValue(TargetId target, out int value)
            {
                value = 0;
                if (publisher == null)
                {
                    return false;
                }

                IReadOnlyList<CapabilityBinding> rows = publisher.ReadBindingRows(target);
                for (int i = 0; i < rows.Count; i++)
                {
                    if (rows[i].IsActive
                        && rows[i].Capability.Equals(family.DerivedCapability)
                        && rows[i].OutputSlot == 0U)
                    {
                        value = rows[i].Value;
                        return true;
                    }
                }

                return false;
            }

            private string DescribeRow(TargetId target) =>
                TryActiveBindingValue(target, out int value)
                    ? value.ToString(CultureInfo.InvariantCulture)
                    : "<none>";

            private string DescribeOptIn(TargetId target)
            {
                if (targets == null
                    || !targets.TryDescriptorOf(target, out TargetDescriptor? descriptor, out DiagnosticCode _, out string _)
                    || descriptor == null)
                {
                    return "<no-descriptor>";
                }

                var text = new StringBuilder();
                for (int i = 0; i < descriptor.OptIns.Count; i++)
                {
                    text.Append(i == 0 ? string.Empty : ",").Append(descriptor.OptIns[i].ToString());
                }

                return text.Length == 0 ? "<no-opt-in>" : text.ToString();
            }

            private bool HasCourseStorage(EntityManager entityManager, Entity course) =>
                course != Entity.Null
                && entityManager.Exists(course)
                && entityManager.HasBuffer<TraversalObservationRow>(course)
                && entityManager.HasBuffer<TraversalProgressRow>(course)
                && entityManager.HasBuffer<TraversalCrossingRow>(course)
                && entityManager.HasComponent<TraversalCourseSnapshot>(course);

            private static bool HasRunnerStorage(EntityManager entityManager, Entity runner) =>
                runner != Entity.Null
                && entityManager.Exists(runner)
                && entityManager.HasComponent<TraversalPose>(runner)
                && entityManager.HasComponent<TraversalVelocity>(runner)
                && entityManager.HasComponent<TraversalJumpState>(runner)
                && entityManager.HasComponent<TraversalMovementInput>(runner);

            private static bool HasVolumeStorage(EntityManager entityManager, Entity volume) =>
                volume != Entity.Null
                && entityManager.Exists(volume)
                && entityManager.HasComponent<TraversalCheckpointVolume>(volume);

            /// <summary>Submits one typed movement envelope through the world's own command port (P-042).</summary>
            private InputAdmissionResult SubmitMovement(TargetId runner, int horizontalMilli, byte jumpPressed)
            {
                if (host == null)
                {
                    return new InputAdmissionResult(
                        InputAdmissionOutcome.Refused, DiagnosticCode.None, "no world", default(InputSourceStamp), null);
                }

                if (ingress == null)
                {
                    ingress = new TypedInputIngress(host.World, host);
                }

                InputSourceStamp stamp = MintStamp(host.World, ++sampleSequence);
                var sample = new SampledInputCommand(
                    stamp,
                    family.MovementRoute,
                    runner,
                    family.MovementSchema,
                    null,
                    family.MovementPayload(horizontalMilli, jumpPressed));
                return ingress.Submit(sample);
            }

            private ulong sampleSequence;

            /// <summary>One sample stamp of this scenario's own headless input source (P-004, P-050).</summary>
            private InputSourceStamp MintStamp(WorldId world, ulong sequence) =>
                new InputSourceStamp(
                    world,
                    new Id128(family.SessionSalt, InputSourceLow),
                    sequence,
                    host != null ? host.CurrentStep : LogicalStepId.Zero,
                    host != null ? host.CurrentEpoch : AssemblyEpoch.Zero);

            /// <summary>
            /// Copies one recorded trace into another recorder, optionally perturbing one body's velocity by the given
            /// milli-units. The copy is the declared comparison's other side: an identical copy is what "the same
            /// admitted input replayed" produces, and the perturbed copy is what a one-unit observation difference
            /// produces (P-008, TEST-022).
            /// </summary>
            private static TraversalTraceRecorder CopyTrace(
                TraversalTraceRecorder source,
                TargetId perturbTarget,
                int velocityDelta)
            {
                var copy = new TraversalTraceRecorder();
                IReadOnlyList<TraversalStepTrace> recorded = source.Steps;
                for (int i = 0; i < recorded.Count; i++)
                {
                    TraversalStepTrace step = recorded[i];
                    copy.BeginStep(step.Step, step.Epoch, step.ExternallyOwned);
                    for (int b = 0; b < step.Bodies.Count; b++)
                    {
                        TraversalBodyTrace body = step.Bodies[b];
                        TraversalVector3i velocity = body.Velocity;
                        if (velocityDelta != 0 && body.Runner.Equals(perturbTarget))
                        {
                            velocity = new TraversalVector3i(
                                velocity.X + velocityDelta, velocity.Y, velocity.Z);
                        }

                        copy.RecordBody(new TraversalBodyTrace(
                            body.Runner, body.Pose, velocity, body.AppliedAcceleration, body.Jumped));
                    }

                    for (int c = 0; c < step.Crossings.Count; c++)
                    {
                        copy.RecordCrossing(step.Crossings[c]);
                    }

                    for (int r = 0; r < step.Refusals.Count; r++)
                    {
                        copy.RecordRefusal(step.Refusals[r]);
                    }

                    copy.CompleteStep();
                }

                return copy;
            }

            /// <summary>
            /// Counts the committed crossing events the world's own reader currently holds, and the total number of
            /// committed events beside them. An event is a crossing when its payload schema is the course's crossing
            /// schema (P-045).
            /// </summary>
            private int CountReadableCrossingEvents(out int eventCount)
            {
                eventCount = 0;
                if (host == null || host.Messages == null)
                {
                    return 0;
                }

                int crossings = 0;
                EventCursor cursor = new EventCursor(host.World, default(EventSequence));
                for (int page = 0; page < 4; page++)
                {
                    CommittedEventPage read = host.Messages.Events.Read(cursor, TraversalKeys.CrossingCapacity);
                    if (read == null || read.Events == null || read.Events.Count == 0)
                    {
                        break;
                    }

                    for (int i = 0; i < read.Events.Count; i++)
                    {
                        eventCount++;
                        if (read.Events[i].Schema.Equals(TraversalKeys.CrossingSchema))
                        {
                            crossings++;
                        }
                    }

                    cursor = read.NextCursor;
                }

                return crossings;
            }

            // ------------------------------------------------------------------ neighbours' declarations

            private static int WalkDeclarations(
                string owner,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                TraversalCourseSurface surface,
                List<string> offenders)
            {
                int walked = 0;
                for (int d = 0; d < declarations.Count; d++)
                {
                    PluginManifest manifest = declarations[d].Manifest;
                    for (int s = 0; s < manifest.Stages.Count; s++)
                    {
                        StageSpec stage = manifest.Stages[s];
                        walked++;
                        if (NamesTraversalId(stage.StageId.Value, surface).Length != 0)
                        {
                            offenders.Add(owner + ":stage:" + NamesTraversalId(stage.StageId.Value, surface));
                        }

                        for (int k = 0; k < stage.FactoryKeys.Count; k++)
                        {
                            walked++;
                            if (NamesTraversalKey(stage.FactoryKeys[k], surface).Length != 0)
                            {
                                offenders.Add(owner + ":stageKey:" + NamesTraversalKey(stage.FactoryKeys[k], surface));
                            }
                        }

                        for (int y = 0; y < stage.Systems.Count; y++)
                        {
                            walked++;
                            if (NamesTraversalKey(stage.Systems[y].SystemKey, surface).Length != 0)
                            {
                                offenders.Add(owner + ":systemKey:" + NamesTraversalKey(stage.Systems[y].SystemKey, surface));
                            }
                        }
                    }

                    for (int b = 0; b < manifest.Buffers.Count; b++)
                    {
                        walked++;
                        string named = NamesTraversalId(manifest.Buffers[b].BufferId.Value, surface);
                        if (named.Length != 0)
                        {
                            offenders.Add(owner + ":buffer:" + named);
                        }
                    }

                    for (int c = 0; c < manifest.CapabilityContracts.Count; c++)
                    {
                        walked++;
                        string named = NamesTraversalCapability(
                            manifest.CapabilityContracts[c].Capability.Capability.Value, surface);
                        if (named.Length != 0)
                        {
                            offenders.Add(owner + ":contract:" + named);
                        }
                    }

                    for (int r = 0; r < manifest.DerivationRules.Count; r++)
                    {
                        walked++;
                        string named = NamesTraversalCapability(
                            manifest.DerivationRules[r].OutputCapability.Capability.Value, surface);
                        if (named.Length != 0)
                        {
                            offenders.Add(owner + ":rule:" + named);
                        }
                    }
                }

                return walked;
            }

            private static int WalkDescriptor(
                string owner,
                PipelineDescriptorReport report,
                TraversalCourseSurface surface,
                List<string> offenders)
            {
                if (!report.Succeeded || report.Descriptor == null)
                {
                    return 0;
                }

                int walked = 0;
                OwnershipStageDescriptor descriptor = report.Descriptor;
                for (int s = 0; s < descriptor.Stages.Count; s++)
                {
                    walked++;
                    DescriptorStage stage = descriptor.Stages[s];
                    string named = NamesTraversalId(stage.Stage.Value, surface);
                    if (named.Length != 0)
                    {
                        offenders.Add(owner + ":descriptorStage:" + named);
                    }

                    for (int y = 0; y < stage.Systems.Count; y++)
                    {
                        walked++;
                        string key = NamesTraversalKey(stage.Systems[y].Key, surface);
                        if (key.Length != 0)
                        {
                            offenders.Add(owner + ":descriptorSystem:" + key);
                        }
                    }
                }

                for (int s = 0; s < descriptor.Slots.Count; s++)
                {
                    walked++;
                    string named = NamesTraversalSlot(descriptor.Slots[s].Slot, surface);
                    if (named.Length != 0)
                    {
                        offenders.Add(owner + ":slot:" + named);
                    }
                }

                return walked;
            }

            private static int CountDeclaredStages(IReadOnlyList<CatalogPluginDeclaration> declarations)
            {
                int count = 0;
                for (int d = 0; d < declarations.Count; d++)
                {
                    count += declarations[d].Manifest.Stages.Count;
                }

                return count;
            }

            private static string NamesTraversalId(Id128 id, TraversalCourseSurface surface)
            {
                if (id.Equals(surface.AccelerationCapability.Value))
                {
                    return "traversal.acceleration";
                }

                for (int i = 0; i < surface.Stages.Count; i++)
                {
                    if (id.Equals(surface.Stages[i].Value))
                    {
                        return "stage#" + i.ToString(CultureInfo.InvariantCulture);
                    }
                }

                if (id.Equals(TraversalKeys.ObservationBuffer.Value))
                {
                    return "traversal.buffer.observation";
                }

                if (id.Equals(TraversalKeys.CommandLane.Value))
                {
                    return "traversal.buffer.movement-lane";
                }

                return string.Empty;
            }

            private static string NamesTraversalKey(FactoryKey key, TraversalCourseSurface surface)
            {
                for (int i = 0; i < surface.Systems.Count; i++)
                {
                    if (key.Equals(surface.Systems[i]))
                    {
                        return "system#" + i.ToString(CultureInfo.InvariantCulture);
                    }
                }

                return NamesTraversalId(key.RegistrationKey, surface);
            }

            private static string NamesTraversalCapability(Id128 capability, TraversalCourseSurface surface) =>
                capability.Equals(surface.AccelerationCapability.Value) ? "traversal.acceleration" : string.Empty;

            private static string NamesTraversalSlot(SlotId slot, TraversalCourseSurface surface)
            {
                if (slot.Equals(TraversalVocabulary.AccelerationSlot))
                {
                    return "traversal.acceleration.slot-0";
                }

                if (slot.Equals(TraversalKeys.MotionSlot)
                    || slot.Equals(TraversalKeys.InputSlot)
                    || slot.Equals(TraversalKeys.ObservationSlot)
                    || slot.Equals(TraversalKeys.ProgressSlot)
                    || slot.Equals(TraversalKeys.CrossingSlot)
                    || slot.Equals(TraversalKeys.SnapshotSlot))
                {
                    return "traversal.slot";
                }

                return string.Empty;
            }

            // ------------------------------------------------------------------ a secondary world

            /// <summary>
            /// One self-contained course world built from the same family facts, used by the observations that need a
            /// FRESH world state from the same starting conditions (the replay comparison of observation 4 and the
            /// three presentation rates of observation 5). It is deliberately not a second implementation of the
            /// genre: it uses the family's own declarations, recipes, lane seed, request, registration and provider
            /// mount, and attaches the same stage runtime with the local physics scene left out (this world makes no
            /// engine-authority claim).
            /// </summary>
            private sealed class SecondaryFixture : IDisposable
            {
                private readonly IGc020Family family;
                private UnityWorldHost? host;
                private AssemblyPublisher? publisher;
                private TargetRegistry? registry;
                private LiveTargetIndex? targets;
                private LiveTargetSeeder? seeder;
                private Gc020StageRuntime? stageRuntime;
                private WorldTimeDriver? time;
                private TypedInputIngress? ingress;
                private ulong hostTicks;
                private ulong operationSequence;
                private ulong sampleSequence;

                private SecondaryFixture(IGc020Family family)
                {
                    this.family = family;
                }

                public bool Ready { get; private set; }

                public string Failure { get; private set; } = string.Empty;

                public TraversalTraceRecorder? Trace => stageRuntime != null ? stageRuntime.Trace : null;

                public int VelocityX
                {
                    get
                    {
                        if (host == null || seeder == null)
                        {
                            return int.MinValue;
                        }

                        EntityManager entityManager = host.EntityWorld.EntityManager;
                        if (!seeder.TryGetEntity(family.VelocityAssertedTarget, out Entity entity)
                            || !entityManager.Exists(entity)
                            || !entityManager.HasComponent<TraversalVelocity>(entity))
                        {
                            return int.MinValue;
                        }

                        return entityManager.GetComponentData<TraversalVelocity>(entity).X;
                    }
                }

                public static SecondaryFixture Build(IGc020Family family, IdSequence sessions, string label, ulong hostTicks)
                {
                    var fixture = new SecondaryFixture(family) { hostTicks = hostTicks };
                    try
                    {
                        fixture.Create(sessions, label);
                    }
                    catch (Exception exception)
                    {
                        fixture.Failure = exception.GetType().Name + ": " + exception.Message;
                        fixture.Dispose();
                    }

                    return fixture;
                }

                public InputAdmissionOutcome SubmitMovement(int horizontalMilli, byte jumpPressed)
                {
                    if (host == null || ingress == null)
                    {
                        return InputAdmissionOutcome.Refused;
                    }

                    var stamp = new InputSourceStamp(
                        host.World,
                        new Id128(family.SessionSalt, InputSourceLow),
                        ++sampleSequence,
                        host.CurrentStep,
                        host.CurrentEpoch);
                    var sample = new SampledInputCommand(
                        stamp,
                        family.MovementRoute,
                        family.VelocityAssertedTarget,
                        family.MovementSchema,
                        null,
                        family.MovementPayload(horizontalMilli, jumpPressed));
                    return ingress.Submit(sample).Outcome;
                }

                public ulong PumpSteps(int count)
                {
                    ulong committed = 0UL;
                    for (int i = 0; i < count; i++)
                    {
                        committed += StepOnce();
                    }

                    return committed;
                }

                /// <summary>
                /// Delivers exactly one second of host time as <paramref name="rate"/> equal host samples, with the
                /// division remainder carried by the last sample so the total host time is exactly one second
                /// (P-036). The committed step count is the fixed-step accumulator's own answer, not a formula here.
                /// </summary>
                public ulong PumpSamplesPerSecond(int rate)
                {
                    if (time == null || rate < 1)
                    {
                        return 0UL;
                    }

                    ulong perSample = TraversalRegistration.TicksPerSecond / (ulong)rate;
                    ulong committed = 0UL;
                    for (int i = 0; i < rate; i++)
                    {
                        ulong ticks = i == rate - 1
                            ? TraversalRegistration.TicksPerSecond - (perSample * (ulong)(rate - 1))
                            : perSample;
                        hostTicks += ticks;
                        committed += time.PumpFrame(hostTicks).StepsCommitted;
                    }

                    return committed;
                }

                public void Dispose()
                {
                    stageRuntime?.Dispose();
                    stageRuntime = null;
                    if (host != null)
                    {
                        host.Stop(new OperationId(host.World, family.Issuer, ++operationSequence), "secondary teardown");
                        host.Dispose();
                        host = null;
                    }
                }

                private void Create(IdSequence sessions, string label)
                {
                    _ = label;
                    PipelineDescriptorReport descriptorReport = family.CompilePipeline();
                    if (!descriptorReport.Succeeded
                        || descriptorReport.Descriptor == null
                        || descriptorReport.Adaptation == null)
                    {
                        Failure = "the pipeline refused: " + descriptorReport.Describe();
                        return;
                    }

                    WorldId world = new WorldId(sessions.Next());
                    WorldCreateRequest request = family.CreateRequest(world, NextOperation(world));
                    UnityWorldRegistration secondaryRegistration = family.CreateRegistration(descriptorReport.Adaptation);
                    bool created = UnityWorldRegistry.TryCreate(
                        request, secondaryRegistration, out UnityWorldHost? createdHost, out WorldCreateResult result);
                    host = createdHost;
                    if (!created || host == null)
                    {
                        Failure = "world creation failed: " + result.Code + ": " + result.Detail;
                        return;
                    }

                    registry = new TargetRegistry(world, TargetCapacity);
                    publisher = new AssemblyPublisher(
                        host,
                        registry,
                        family.CreateRecipes(),
                        family.CreateMigrations(),
                        descriptorReport.Descriptor);
                    targets = new LiveTargetIndex(publisher.Recipes);
                    seeder = new LiveTargetSeeder(host, registry, targets);

                    IDerivationValueSource secondaryValues = family.CreateValues();
                    CompositionHost secondaryLane = CompositionHost.CreateDefault(
                        world,
                        family.WorldRootScope,
                        new CatalogManifestSource(family.Catalog, family.Declarations),
                        null,
                        family.LaneSeed,
                        new DerivationModeSwitchValidator(secondaryValues, () => TargetsView()));
                    var secondaryPipeline = new DerivedAssemblyPipeline(
                        host,
                        secondaryLane,
                        publisher,
                        targets,
                        seeder,
                        secondaryValues,
                        null,
                        null,
                        publisher.Migrations,
                        new StagedResourceGate(StagedByteCeiling, family.Issuer),
                        new PlanBudget(
                            PrepareBytesLimit, PrepareBytesLimit, ScratchCapacityBytes, ScratchBytesPerSlot));

                    time = new WorldTimeDriver(host, new StepInputCutoff(8, 16), new PluginClockRegistry(8), 1U);
                    time.AdoptResourceTable(descriptorReport.Adaptation.NativeTable!);

                    if (!family.SeedTargets(new Gc013WorldContext(host, targets, seeder)))
                    {
                        Failure = "the family refused to seed the secondary world";
                        return;
                    }

                    stageRuntime = Gc020StageRuntime.Attach(
                        Gc020TraversalHost.Label,
                        host,
                        family.CourseTarget,
                        descriptorReport,
                        targets,
                        seeder,
                        installPhysics: false);

                    // The provider mount is the one publication this world needs: it gives the asserted runner the
                    // contribution the reference's numeric assertion is about (07 s4.3, REF-A01).
                    CompositionEditPayload mount = family.MountProvider();
                    EditAdmission admission = secondaryLane.SubmitEdit(
                        mount, NextOperation(world), secondaryLane.Committed.Revision);
                    if (!admission.Staged)
                    {
                        Failure = "the provider mount was refused by the lane: " + admission.Kind + "/" + admission.Code;
                        return;
                    }

                    IReadOnlyList<PublishedOperation> published = secondaryLane.Drain();
                    if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
                    {
                        Failure = "the provider mount publication was refused";
                        return;
                    }

                    DerivedAssemblyReport report = secondaryPipeline.PublishDerived(NextOperation(world));
                    if (report.Outcome == DerivedAssemblyOutcome.Refused)
                    {
                        Failure = "the secondary world refused the assembly: " + report.Describe();
                        return;
                    }

                    if (!AssemblyPublisher.MatchesPublishedAssembly(
                            secondaryLane.Committed.Revision,
                            secondaryLane.Committed.Epoch,
                            publisher.PublishedRevision,
                            host.CurrentEpoch))
                    {
                        Failure = "the secondary world's lane and assembly counters disagree (P-006)";
                        return;
                    }

                    // The world's simulation clock starts at its first routable pump (P-036), so this zero-elapsed
                    // pump captures the origin before any host time is delivered: without it the first sample a
                    // caller feeds would merely establish the origin and the one-second comparison of observation 5
                    // would silently lose one sample's worth of time.
                    time.PumpFrame(hostTicks);

                    ingress = new TypedInputIngress(host.World, host);
                    Ready = true;
                }

                private IReadOnlyList<DerivationTarget> TargetsView()
                {
                    if (targets == null)
                    {
                        return Array.Empty<DerivationTarget>();
                    }

                    DerivationInputTargets view = targets.BuildDerivationTargets();
                    return view.Succeeded ? view.Targets : Array.Empty<DerivationTarget>();
                }

                private ulong StepOnce()
                {
                    if (time == null)
                    {
                        return 0UL;
                    }

                    hostTicks += family.StepDurationTicks;
                    return time.PumpFrame(hostTicks).StepsCommitted;
                }

                private OperationId NextOperation(WorldId world)
                {
                    operationSequence++;
                    return new OperationId(world, family.Issuer, operationSequence);
                }
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
            /// digest the EditMode suite asserts on.
            /// </summary>
            private void Add(string bareName, bool passed, string detail)
            {
                lastFailure = string.Empty;
                steps.Add(new Gc020Step(family.Label + "/" + bareName, passed, detail ?? string.Empty));
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
