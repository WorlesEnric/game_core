// GameCore.Validation.ProbeHost — the GC-020 traversal family contract.
//
// The gate sentence this contract serves, from `docs/game-core/09-implementation-guide.md` (Wave 6, GC-020) and
// `docs/game-core/07-reference-compositions.md` s4 (the traversal challenge reference):
//
//   "Integrate a fixed-step real-time slice with one authority per physical domain." — with the acceptance
//   "one admitted step integrates exactly once; a presentation rate never advances simulation twice; an engine-owned
//    pose is never integrated by gameplay; observation replay separates rule repeatability from native physics."
//
// One runner, ONE family (the traversal genre is new here: this is not a partial part of an existing host, so
// `Gc020TraversalHost` owns its own nested family class). Everything a family needs in order to be an ordinary
// GC-013 family — its catalog, lane seed, live targets, provider mount and the installation that mount creates —
// already comes from `IGc013Family`, so this gate integrates the traversal course with the same real control lane,
// assembly publisher and derived-assembly pipeline the earlier gates use instead of a parallel implementation.
//
// This file adds only the surface the fixed-step gate needs from the genre:
//
//   * the family's own command identity (route, schema) and the payload it really sends over it, so the input step
//     submits a typed movement envelope the traversal package's own `TraversalMovementInputReader` decodes (P-042);
//   * the live targets whose pose, velocity and progress the observations read, and the scopes the reparent moves
//     (07 s4.3);
//   * the declared numeric facts the reference's own assertions quote (`1.00 -> 1.04 -> 1.02`, the 20 ms step and the
//     four-step catch-up), so the scenario compares two numbers the rules package computed (07 s4.3, REF-A01/A06);
//   * the stage runtime the compiled five stages resolve while they dispatch, including the REAL local physics scene
//     the course's external-authority option installs (04 s7, REF-A05);
//   * the traversal surface an unrelated family must NOT contain, so the card and narrative declarations can be
//     walked for a leaked action/physics phase (P-001, P-059).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Traversal;
using GameCore.Rules.Narrative;
using GameCore.Rules.Traversal;
using GameCore.Unity.Adapters.Animation;
using GameCore.Unity.Adapters.Audio;
using GameCore.Unity.Adapters.Physics;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named GC-020 observation: what was checked and the values it was computed from.</summary>
    public sealed class Gc020Step
    {
        public Gc020Step(string name, bool passed, string detail)
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
    /// Full result of one GC-020 run: the named observations plus one digest over them, computed over the canonical
    /// `name=pass|fail` lines with the same digest function the narrative trace, the GC-013 result, the GC-019 result
    /// and the Wave 4 gate result use. A run that records a different set of observations (or a failing one)
    /// therefore cannot report the digest the observation table implies.
    /// </summary>
    public sealed class Gc020ScenarioResult
    {
        public Gc020ScenarioResult(string label, IReadOnlyList<Gc020Step> steps)
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
        public IReadOnlyList<Gc020Step> Steps { get; }

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
                + "; steps=" + Steps.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "; failed=" + failed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (failed.Count == 0 ? string.Empty : ": " + string.Join(" | ", failed.ToArray()));
        }
    }

    /// <summary>
    /// The traversal ids one genre declares: the five fixed-step stage identities, the five dispatch keys and the
    /// `traversal.acceleration` capability. It exists so the card and narrative observations can walk those genres'
    /// declarations and assert that none of these ids leaked into a genre that has no action/physics phase (P-001,
    /// P-059) — the equality is identity-based, never name-based.
    /// </summary>
    public sealed class TraversalCourseSurface
    {
        public TraversalCourseSurface(
            IReadOnlyList<StageId> stages,
            IReadOnlyList<FactoryKey> systems,
            CapabilityId accelerationCapability)
        {
            Stages = stages ?? throw new ArgumentNullException(nameof(stages));
            Systems = systems ?? throw new ArgumentNullException(nameof(systems));
            AccelerationCapability = accelerationCapability;
        }

        /// <summary>`traversal.input` .. `traversal.output`, in declared dispatch order (07 s4.2).</summary>
        public IReadOnlyList<StageId> Stages { get; }

        /// <summary>The five generated dispatch keys of those stages (P-039).</summary>
        public IReadOnlyList<FactoryKey> Systems { get; }

        /// <summary>The inherited acceleration capability the course's modifiers contribute (07 s4.1).</summary>
        public CapabilityId AccelerationCapability { get; }

        public override string ToString() =>
            "stages=" + Stages.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";systems=" + Systems.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";acceleration=" + AccelerationCapability.ToString();
    }

    /// <summary>
    /// The fixture-side stage runtime the traversal course's five compiled systems resolve while they dispatch: the
    /// course module its own scenario attaches, the world's live target map, the bounded per-step observation trace,
    /// the committed-output adapters that present the step's result (animation and audio), and — only when the genre
    /// declares it — the REAL local physics scene that owns the course's external physical domain.
    ///
    /// A traversal world whose systems dispatch without this runtime leaves the movement route unconsumed and faults
    /// the step commit (P-043), so a world that pumps admitted steps must have one attached. It owns no gameplay rule:
    /// it is the same objects the traversal package's own stages resolve, held by the runner so both halves of this
    /// gate run the genre's real five stages.
    /// </summary>
    public sealed class Gc020StageRuntime : IDisposable
    {
        private static int nextPhysicsScene;

        private Gc020StageRuntime(
            string kind,
            TraversalModule module,
            TraversalTraceRecorder trace,
            UnityPhysicsSceneBackend? physics,
            PhysicsAuthorityGate? physicsGate,
            CommittedAudioStage audio,
            RecordingAudioSink audioSink,
            CommittedAnimationStage animation,
            RecordingAnimationSink animationSink,
            RecordingPoseSource poseSource,
            int descriptorStageCount)
        {
            Kind = kind;
            Module = module;
            Trace = trace;
            Physics = physics;
            PhysicsGate = physicsGate;
            Audio = audio;
            AudioSink = audioSink;
            Animation = animation;
            AnimationSink = animationSink;
            PoseSource = poseSource;
            DescriptorStageCount = descriptorStageCount;
        }

        /// <summary>Which genre's module this runtime holds.</summary>
        public string Kind { get; }

        /// <summary>The traversal course module: the entities, counters and motion-authority table of this world.</summary>
        public TraversalModule? Module { get; }

        /// <summary>The bounded per-step observation trace the integrate and checkpoint stages write (P-008, TEST-022).</summary>
        public TraversalTraceRecorder? Trace { get; }

        /// <summary>The real Unity local physics scene, when the genre declared an external physical domain (04 s7).</summary>
        public UnityPhysicsSceneBackend? Physics { get; }

        /// <summary>The once-per-admitted-step gate over that scene (REF-A05, REF-A06).</summary>
        public PhysicsAuthorityGate? PhysicsGate { get; }

        /// <summary>The committed-crossing audio stage over the world's own committed event reader (04 s7).</summary>
        public CommittedAudioStage? Audio { get; }

        /// <summary>The recording audio sink behind <see cref="Audio"/>; nothing is really played.</summary>
        public RecordingAudioSink? AudioSink { get; }

        /// <summary>The committed-pose animation stage.</summary>
        public CommittedAnimationStage? Animation { get; }

        /// <summary>The recording animation sink behind <see cref="Animation"/>.</summary>
        public RecordingAnimationSink? AnimationSink { get; }

        /// <summary>The committed-pose source the animation stage reads.</summary>
        public RecordingPoseSource? PoseSource { get; }

        /// <summary>Stages the compiled descriptor declared, so a caller can report what the runtime was attached for.</summary>
        public int DescriptorStageCount { get; }

        /// <summary>
        /// Attaches the traversal course runtime to its just-seeded world: the module over the course entity, the
        /// per-step trace, the committed-output adapters, and the local physics scene when the genre asked for one.
        /// Every live target of the index is then bound to the module by its declared recipe — the course entity as
        /// the course, a runner recipe as a runner, a checkpoint recipe as a volume at its declared course ordinal —
        /// so the five stages resolve exactly the entities the world really seeded (P-004, P-005, P-015).
        /// </summary>
        public static Gc020StageRuntime Attach(
            string kind,
            UnityWorldHost host,
            TargetId courseTarget,
            PipelineDescriptorReport descriptor,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder,
            bool installPhysics)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (targets == null)
            {
                throw new ArgumentNullException(nameof(targets));
            }

            if (seeder == null)
            {
                throw new ArgumentNullException(nameof(seeder));
            }

            TraversalModule module = TraversalModule.Attach(host, courseTarget);
            TraversalTraceRecorder trace = TraversalStepTraceRegistry.Attach(host.World);
            BindEveryTarget(module, courseTarget, targets, seeder);

            WorldMessagePlane? plane = host.Messages;
            var audioSink = new RecordingAudioSink();
            CommittedAudioStage? audio = null;
            if (plane != null)
            {
                var cues = new AudioCueTable();
                cues.TryAdd(
                    TraversalKeys.CrossingSchema,
                    TraversalIdentity.Id("gc020.audio.cue.checkpoint-passed"));
                audio = new CommittedAudioStage(host.World, plane.Events, audioSink, cues);
            }

            var poseSource = new RecordingPoseSource(new SnapshotToken(host.World, host.CurrentEpoch, host.CurrentStep));
            var animationSink = new RecordingAnimationSink();
            var animation = new CommittedAnimationStage(poseSource, animationSink);

            // The real adapter is created only when the genre declares the external physical domain: a card or
            // narrative world never reaches this branch, so their compiled schedules stay physics-free (P-059).
            UnityPhysicsSceneBackend? physics = null;
            PhysicsAuthorityGate? gate = null;
            if (installPhysics)
            {
                physics = new UnityPhysicsSceneBackend(
                    "GameCoreTraversalCourseScene-" + System.Threading.Interlocked.Increment(ref nextPhysicsScene).ToString(
                        System.Globalization.CultureInfo.InvariantCulture), 16, true);
                gate = new PhysicsAuthorityGate(physics);
            }

            int stageCount = descriptor != null && descriptor.Succeeded && descriptor.Descriptor != null
                ? descriptor.Descriptor.Stages.Count
                : 0;

            return new Gc020StageRuntime(
                kind, module, trace, physics, gate, audio, audioSink, animation, animationSink, poseSource, stageCount);
        }

        /// <summary>
        /// Detaches this runtime from its world: the local scene is unloaded and the process's previous global
        /// simulation mode restored, the world's trace recorder is forgotten, and the module leaves the process-side
        /// module list so a process that ran several worlds holds only live ones (P-047, P-048).
        /// </summary>
        public void Dispose()
        {
            Physics?.Dispose();
            if (Module != null)
            {
                TraversalStepTraceRegistry.Detach(Module.Host.World);
                Module.Dispose();
            }
        }

        /// <summary>
        /// Binds every live target of the index to the module, classified by the recipe the target was registered
        /// with: the course target becomes the course entity, a runner recipe becomes a runner and a checkpoint recipe
        /// becomes a volume at its declared ordinal. A target of another recipe is left unbound rather than guessed.
        /// </summary>
        private static void BindEveryTarget(
            TraversalModule module,
            TargetId courseTarget,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder)
        {
            IReadOnlyList<LiveTarget> live = targets.Targets;
            for (int i = 0; i < live.Count; i++)
            {
                TargetId target = live[i].Target;
                if (!seeder.TryGetEntity(target, out Entity entity) || entity == Entity.Null)
                {
                    continue;
                }

                if (target.Equals(courseTarget))
                {
                    module.BindCourse(entity);
                    continue;
                }

                DefinitionRef recipe = live[i].Recipe;
                if (recipe.Equals(TraversalKeys.RunnerRecipe) || recipe.Equals(TraversalKeys.DisplayRunnerRecipe))
                {
                    module.BindRunner(target, entity, recipe);
                    continue;
                }

                if (recipe.Equals(TraversalKeys.CheckpointRecipe))
                {
                    module.BindVolume(target, entity, OrdinalOf(target));
                }
            }
        }

        /// <summary>The declared course ordinal of one checkpoint target (07 s4.2's ascending course definition).</summary>
        private static uint OrdinalOf(TargetId volume) =>
            volume.Equals(CheckpointOneTarget)
                ? TraversalVocabulary.CheckpointOneOrdinal
                : volume.Equals(CheckpointTwoTarget)
                    ? TraversalVocabulary.CheckpointTwoOrdinal
                    : TraversalVocabulary.CheckpointOneOrdinal;

        /// <summary>The first checkpoint volume's declared target identity (07 s4.2's course definition).</summary>
        private static TargetId CheckpointOneTarget => TraversalIdentity.Target(TraversalVocabulary.CheckpointOne);

        /// <summary>The second checkpoint volume's declared target identity.</summary>
        private static TargetId CheckpointTwoTarget => TraversalIdentity.Target(TraversalVocabulary.CheckpointTwo);
    }

    /// <summary>
    /// One genre's declared facts for the GC-020 fixed-step gate: everything <see cref="IGc013Family"/> declares, plus
    /// the movement command identity and payload, the live targets and scopes the traversal observations read, the
    /// reference's declared numeric facts, the traversal surface an unrelated genre must not contain, the genre's own
    /// stage runtime its five compiled stages resolve, and the declaration of whether its course installs the real
    /// local physics scene.
    /// </summary>
    public interface IGc020Family : IGc013Family
    {
        /// <summary>The route one admitted movement envelope travels (`traversal.route.movement`, P-042).</summary>
        RouteId MovementRoute { get; }

        /// <summary>The payload schema of that route, which the package's own reader is bound to (P-042, 04 s8).</summary>
        SchemaRef MovementSchema { get; }

        /// <summary>
        /// The genre's own declared payload: one captured movement input (horizontal acceleration, vertical
        /// acceleration, jump flag) encoded exactly the way `TraversalCommandCodec` encodes it, so the adapter never
        /// invents a payload and the package's reader really decodes it (05 s6).
        /// </summary>
        FrozenPayload MovementPayload(int horizontalMilli, byte jumpPressed);

        /// <summary>
        /// The runner whose velocity the one-step numeric assertion reads: the valley runner the tailwind provider
        /// reaches, i.e. the target `07 s4.3`'s `1.00 -> 1.04` step is asserted about (REF-A01).
        /// </summary>
        TargetId VelocityAssertedTarget { get; }

        /// <summary>
        /// The subtree root the sequence reparents: the runner whose branch moves from the valley into the ridge, so
        /// the same stable target observes a different contribution before and after (07 s4.3, P-025).
        /// </summary>
        TargetId ReparentedTarget { get; }

        /// <summary>Every runner the course owns, in canonical target order: the set the integrator advances (P-008).</summary>
        IReadOnlyList<TargetId> CourseTargets { get; }

        /// <summary>The course's own target: it owns run progress and the committed crossing output (07 s4.2).</summary>
        TargetId CourseTarget { get; }

        /// <summary>The second checkpoint volume, in the ridge: the course's next expected checkpoint (07 s4.2).</summary>
        TargetId SecondCheckpoint { get; }

        /// <summary>The valley runner scope the sequence reparents, i.e. <see cref="IGc013Family.MovedScope"/>.</summary>
        ScopeId ValleyRunnerScope { get; }

        /// <summary>The ridge runner scope the reparent moves it into, i.e. <see cref="IGc013Family.MoveDestination"/>.</summary>
        ScopeId RidgeRunnerScope { get; }

        /// <summary>The velocity after one tailwind step, and the page's velocity tolerance (07 s4.3).</summary>
        int ExpectedAcceleratedVelocityMilli { get; }

        /// <summary>The velocity after the reparent's headwind step (07 s4.3).</summary>
        int ExpectedReparentedVelocityMilli { get; }

        /// <summary>The baseline horizontal speed the fixture seeds (07 s4.3).</summary>
        int SeededVelocityMilli { get; }

        /// <summary>The world definition's declared fixed step, in host ticks (P-036).</summary>
        ulong StepDurationTicks { get; }

        /// <summary>The declared catch-up bound of one host update: at most this many steps per pump (P-036).</summary>
        uint MaxStepsPerPump { get; }

        /// <summary>The world definition's declared fixed step, in whole milliseconds (20 for this fixture).</summary>
        int StepMilliseconds { get; }

        /// <summary>
        /// The traversal ids this genre declares. An unrelated genre walking its own declarations must contain none
        /// of them, which is how "no action/physics phase leaked into cards or narrative" becomes observable rather
        /// than asserted by name (P-001, P-059).
        /// </summary>
        TraversalCourseSurface ActionSurface();

        /// <summary>
        /// Attaches the genre's own stage runtime to its just-seeded world: the module its five compiled stages
        /// resolve (and its own scenario attaches), with every live target mapped and, when the genre declares it, the
        /// real local physics scene installed for the course's external physical domain. Called once per world, right
        /// after <see cref="IGc013Family.SeedTargets"/> built the context's targets; the runner disposes it in its
        /// teardown (P-005, P-043).
        /// </summary>
        Gc020StageRuntime AttachStageRuntime(
            UnityWorldHost host,
            PipelineDescriptorReport descriptor,
            LiveTargetIndex targets,
            LiveTargetSeeder seeder);

        /// <summary>
        /// Declares that one world's course installs the real local physics scene. The traversal genre answers yes
        /// because 07 s4.2's optional rigidbody mode makes an engine solver the sole physical authority; the card and
        /// narrative genres never call this, so their worlds stay ECS-owned and physics-free (P-034, P-059).
        /// </summary>
        void ConfigurePhysics(WorldId world);
    }
}
