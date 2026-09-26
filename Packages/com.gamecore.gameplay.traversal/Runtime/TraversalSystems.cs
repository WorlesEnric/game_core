// GameCore.Gameplay.Traversal — the course runtime module and its five ordered stages (GC-020).
//
// Normative sources: 07 s4.2's schema/authority table and its five-stage graph, 07 s4.3's before/after operations,
// P-034 (one owner per authoritative domain; an engine-owned physical domain is declared external and its ECS data is
// stamped observation), P-041/P-043/P-044 (tracked work, bounded declared buffers, and one bounded commit per logical
// step) and P-036 (a fixed-step world advances only by its admitted steps).
//
// The five systems are ordinary gameplay systems; the ECS storage they write is this package's own and the kernel
// services they use are the real ones: the world's `WorldMessagePlane` for the bounded movement lane and the committed
// crossing events, the published assembly for the inherited acceleration configuration, and the driver's own step
// boundary for "every write of one step happens in one step, or none does".
//
// WHAT EACH STAGE DOES, IN ONE LINE EACH.
//   input       drains the admitted movement envelopes into per-runner captured input (the adapter's sealed batch).
//   integrate   applies exactly one fixed step of the rules package's arithmetic to each owned runner, reading the
//               effective derived acceleration from the published binding row and writing pose/velocity/jump directly.
//   sense       tests the data-defined checkpoint volumes from the pose it just integrated and seals one observation
//               per crossing candidate, plus whatever the adapter's external feed delivered.
//   checkpoints deduplicates and orders those observations through the pure course rules, writes progress and emits
//               one committed crossing event per accepted crossing.
//   output      writes the step's coherent snapshot row.
//
// THE EXTERNAL-AUTHORITY SEAM. When a runner's recipe is declared externally owned (the optional rigidbody mode), the
// integrator must NOT integrate it: this module consults its own motion-authority table and skips such runners,
// counting them, which is what makes "no double simulation" (REF-A05) observable in a live world rather than a
// convention. The pose gameplay reads for those runners is the engine observation the adapter copied back, and this
// package refuses to write it from gameplay (P-034, 04 s7).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Entities;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>What one runner's motion owner decided for one step: integrate it, or leave it to the engine.</summary>
    public enum TraversalMotionDecision
    {
        /// <summary>ECS owns the pose: this package integrates it (07 s4.2's Core-owned kinematic mode).</summary>
        IntegrateInEcs = 0,

        /// <summary>An external authority owns the pose: integrating it here would be a second writer (P-034).</summary>
        ExternallyOwned = 1,

        /// <summary>The runner has no pose storage, so there is nothing to integrate (P-005).</summary>
        MissingStorage = 2,
    }

    /// <summary>Which authority owns one recipe's motion domain (07 s4.2, P-034).</summary>
    public enum TraversalMotionMode
    {
        /// <summary>ECS owns the pose and this package integrates it: the fixture's primary mode (07 s4.2).</summary>
        Kinematic = 0,

        /// <summary>
        /// The engine's physical solver owns pose/velocity and ECS records the synchronized observation (04 s7's
        /// optional rigidbody mode). Selecting it forbids this package's integrator from writing the same pose.
        /// </summary>
        ExternalRigidbody = 1,
    }

    /// <summary>
    /// The runtime state of one traversal course world: the entities the course owns, the counters a scenario asserts
    /// on, the bounded external observation feed the sensor adapter posts into and the declared motion-authority table.
    /// It owns no gameplay rule: every rule lives in `GameCore.Rules.Traversal`.
    /// </summary>
    public sealed class TraversalModule : IDisposable
    {
        private static readonly List<TraversalModule> Modules = new List<TraversalModule>();

        private readonly List<Runner> runners = new List<Runner>();
        private readonly List<Volume> volumes = new List<Volume>();
        private readonly Dictionary<ulong, Runner> runnersByTarget = new Dictionary<ulong, Runner>();
        private readonly Dictionary<ulong, Volume> volumesByTarget = new Dictionary<ulong, Volume>();
        private readonly List<TraversalObservationRow> feed = new List<TraversalObservationRow>();
        private readonly Dictionary<Id128, TraversalMotionMode> motionModeByRecipe =
            new Dictionary<Id128, TraversalMotionMode>();

        private readonly TargetId courseTarget;

        private bool disposed;

        private TraversalModule(UnityWorldHost host, TargetId courseTarget)
        {
            Host = host;
            this.courseTarget = courseTarget;
        }

        /// <summary>The world this course runs in; the same host class the other families use (P-002).</summary>
        public UnityWorldHost Host { get; }

        /// <summary>The course entity: the sealed observations, progress, crossings and committed image.</summary>
        public Entity CourseEntity { get; private set; } = Entity.Null;

        /// <summary>The stable target identity of the course entity this module owns (P-004).</summary>
        public TargetId CourseTarget => courseTarget;

        /// <summary>The runner recipe whose motion mode this module was first asked about (the mode's subject).</summary>
        public DefinitionRef SubjectRecipe { get; private set; }

        /// <summary>Runners this module owns.</summary>
        public int RunnerCount => runners.Count;

        /// <summary>Checkpoint volumes of the course definition.</summary>
        public int VolumeCount => volumes.Count;

        /// <summary>Admitted movement inputs the input stage decoded into captured input.</summary>
        public int DecodedInputCount { get; private set; }

        /// <summary>Admitted inputs the input stage refused as a bounded-work rejection or a decode refusal.</summary>
        public int InputRejectionCount { get; private set; }

        /// <summary>Runners the integrator advanced in the last step.</summary>
        public int LastIntegratedCount { get; private set; }

        /// <summary>Runners the integrator skipped because an external authority owns their pose (P-034).</summary>
        public int LastExternallyOwnedCount { get; private set; }

        /// <summary>Runners the integrator could not advance because they had no pose storage (P-005).</summary>
        public int MissingStorageCount { get; private set; }

        /// <summary>Runner integrations this module has applied in total (REF-A06's fixed-step evidence).</summary>
        public int IntegratedRunnerStepCount { get; private set; }

        /// <summary>Fixed steps this module has advanced at all.</summary>
        public int SteppedStepCount { get; private set; }

        /// <summary>Integrations the rules refused with a reason instead of applying half of a step (P-044).</summary>
        public int RefusedIntegrationCount { get; private set; }

        /// <summary>Sealed observations the sensor produced.</summary>
        public int ObservationCount { get; private set; }

        /// <summary>Observations the sensor refused because the bounded buffer was already full.</summary>
        public int ObservationOverflowCount { get; private set; }

        /// <summary>Crossings the checkpoint owner accepted and committed.</summary>
        public int CrossingCount { get; private set; }

        /// <summary>Observations the checkpoint owner refused as a duplicate crossing (07 s4.2, REF-A04).</summary>
        public int DuplicateObservationCount { get; private set; }

        /// <summary>Observations the checkpoint owner refused as out of course order (07 s4.2).</summary>
        public int OutOfOrderObservationCount { get; private set; }

        /// <summary>Observations the checkpoint owner refused because they were stamped by an older step.</summary>
        public int StaleObservationCount { get; private set; }

        /// <summary>Observations naming a volume the course definition does not contain.</summary>
        public int UnknownVolumeObservationCount { get; private set; }

        /// <summary>Committed snapshot rows the output stage wrote.</summary>
        public int OutputRowCount { get; private set; }

        /// <summary>The last effective acceleration the integrator read from a published binding row (P-015).</summary>
        public int LastEffectiveAccelerationMilli { get; private set; }

        /// <summary>Integrations whose runner carried no active acceleration row, so the honest value was zero.</summary>
        public int AccelerationMissCount { get; private set; }

        /// <summary>Committed crossing events the checkpoint owner staged for the world's committed output (P-045).</summary>
        public int StagedEventCount { get; private set; }

        /// <summary>
        /// When true, the sensor emits its first observation twice in the same step. It is the declared fixture
        /// configuration for 07 s4.2's "Multiple trigger callbacks do not produce repeated awards": a volume that
        /// reports the same crossing twice must still award it once (REF-A04).
        /// </summary>
        public bool DuplicateFirstObservation { get; set; }

        /// <summary>The external observation feed's declared capacity (P-043: bounded work, no silent drop).</summary>
        public int FeedCapacity => TraversalKeys.SensorFeedCapacity;

        /// <summary>Observations delivered into the feed and not yet sealed by the sensor stage.</summary>
        public int PendingFeedCount => feed.Count;

        /// <summary>Observations the feed refused because it was full; a refusal, never a silent drop.</summary>
        public int FeedOverflowCount { get; private set; }

        /// <summary>
        /// Creates and registers the module of one owned world. The course target identity is required because the
        /// module answers the movement route's declared domain version for it, and a module that could not name its own
        /// course entity would have to guess which target its committed step belongs to (P-004, P-042).
        /// </summary>
        public static TraversalModule Attach(UnityWorldHost host, TargetId courseTarget)
        {
            if (host == null)
            {
                throw new ArgumentNullException(nameof(host));
            }

            if (courseTarget.IsDefault)
            {
                throw new ArgumentException("a course module must name its course target (P-004).", nameof(courseTarget));
            }

            var module = new TraversalModule(host, courseTarget);
            Modules.Add(module);
            return module;
        }

        /// <summary>Resolves the module of one ECS world; systems reach the host and the course through it.</summary>
        public static bool TryGet(World world, out TraversalModule? module)
        {
            for (int i = 0; i < Modules.Count; i++)
            {
                if (Modules[i].Host.EntityWorld == world)
                {
                    module = Modules[i];
                    return true;
                }
            }

            module = null;
            return false;
        }

        /// <summary>Disposes every attached module; a scenario calls this after its world is torn down.</summary>
        public static void DetachAll()
        {
            for (int i = Modules.Count - 1; i >= 0; i--)
            {
                Modules[i].Dispose();
            }

            Modules.Clear();
        }

        /// <summary>
        /// Records the course entity the seeding step created and binds the route's domain-version authority (P-042),
        /// so a movement envelope's `ExpectedDomainVersion` is checked against the run's own committed step rather
        /// than ignored.
        /// </summary>
        public void BindCourse(Entity course)
        {
            CourseEntity = course;
            WorldMessagePlane? plane = Host.Messages;
            if (plane != null)
            {
                plane.BindDomainVersion(TraversalKeys.CommandRoute, new TraversalStepDomainVersion(this));
            }
        }

        /// <summary>Records one runner entity under its stable target identity (P-004, P-005).</summary>
        public void BindRunner(TargetId target, Entity runner, DefinitionRef recipe)
        {
            var entry = new Runner(target, runner, recipe);
            runnersByTarget[target.Value.Low] = entry;
            runners.Add(entry);
            runners.Sort(CompareRunners);
        }

        /// <summary>Records one checkpoint volume entity under its stable target identity and course ordinal.</summary>
        public void BindVolume(TargetId target, Entity volume, uint ordinal)
        {
            var entry = new Volume(target, volume, ordinal);
            volumesByTarget[target.Value.Low] = entry;
            volumes.Add(entry);
            volumes.Sort(CompareVolumes);
        }

        /// <summary>Resolves one runner target to its entity; false when no such runner exists (P-005).</summary>
        public bool TryRunner(TargetId target, out Entity runner)
        {
            if (runnersByTarget.TryGetValue(target.Value.Low, out Runner entry))
            {
                runner = entry.Entity;
                return true;
            }

            runner = Entity.Null;
            return false;
        }

        /// <summary>Resolves one checkpoint target to its entity; false when the course holds no such volume.</summary>
        public bool TryVolume(TargetId target, out Entity volume)
        {
            if (volumesByTarget.TryGetValue(target.Value.Low, out Volume entry))
            {
                volume = entry.Entity;
                return true;
            }

            volume = Entity.Null;
            return false;
        }

        /// <summary>Every runner of the course, in canonical target order (P-008).</summary>
        public IReadOnlyList<TraversalRunnerRef> Runners()
        {
            var all = new List<TraversalRunnerRef>(runners.Count);
            for (int i = 0; i < runners.Count; i++)
            {
                all.Add(new TraversalRunnerRef(runners[i].Target, runners[i].Entity, runners[i].Recipe));
            }

            return all;
        }

        /// <summary>Every checkpoint volume of the course, in ascending ordinal order (P-008).</summary>
        public IReadOnlyList<TraversalVolumeRef> Volumes()
        {
            var all = new List<TraversalVolumeRef>(volumes.Count);
            for (int i = 0; i < volumes.Count; i++)
            {
                all.Add(new TraversalVolumeRef(volumes[i].Target, volumes[i].Entity, volumes[i].Ordinal));
            }

            return all;
        }

        /// <summary>The course definition as the rules package's value, in ascending ordinal order (07 s4.2).</summary>
        public CheckpointCourse Course()
        {
            var ordered = new List<TargetId>(volumes.Count);
            for (int i = 0; i < volumes.Count; i++)
            {
                ordered.Add(volumes[i].Target);
            }

            return new CheckpointCourse(ordered);
        }

        // ------------------------------------------------------------------ the declared motion authority

        /// <summary>
        /// Declares which authority owns the motion domain of one recipe, defaulting to ECS-owned kinematic
        /// (07 s4.2: "The fixture selects Core-owned kinematic motion"). A conflicting selection is refused as a value,
        /// so activating both owners for the same recipe is an explicit `OwnershipConflict` rather than a silent
        /// double-writer (P-034, REF-A05).
        /// </summary>
        public bool TrySelectMotionAuthority(
            DefinitionRef recipe,
            TraversalMotionMode mode,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (recipe.Id.IsDefault)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "a motion authority must name a recipe (P-004)";
                return false;
            }

            if (SubjectRecipe.Id.IsDefault)
            {
                SubjectRecipe = recipe;
            }

            if (motionModeByRecipe.TryGetValue(recipe.Id.Value, out TraversalMotionMode existing))
            {
                if (existing == mode)
                {
                    return true;
                }

                code = DiagnosticCode.OwnershipConflict;
                detail = "recipe " + recipe.Id.ToString() + " already has " + existing
                    + " motion authority; enabling both owners for overlapping targets is the double simulation that"
                    + " P-034 and REF-A05 forbid, not a write-order problem to work around";
                return false;
            }

            motionModeByRecipe[recipe.Id.Value] = mode;
            return true;
        }

        /// <summary>The selected motion authority of one recipe; ECS-owned when nobody selected one (07 s4.2).</summary>
        public TraversalMotionMode MotionAuthorityOf(DefinitionRef recipe) =>
            motionModeByRecipe.TryGetValue(recipe.Id.Value, out TraversalMotionMode mode)
                ? mode
                : TraversalMotionMode.Kinematic;

        /// <summary>Recipes whose motion authority this module declared, in canonical recipe order (P-008).</summary>
        public IReadOnlyList<TraversalMotionDeclaration> MotionAuthorityDeclarations()
        {
            var all = new List<TraversalMotionDeclaration>(motionModeByRecipe.Count);
            foreach (KeyValuePair<Id128, TraversalMotionMode> pair in motionModeByRecipe)
            {
                all.Add(new TraversalMotionDeclaration(pair.Key, pair.Value));
            }

            all.Sort(CompareDeclarations);
            return all;
        }

        /// <summary>
        /// The decision one runner's motion gets: ECS integrates it, or an external authority owns it and gameplay
        /// must not write it (P-034). The two outcomes are exclusive by construction, which is what prevents two
        /// writable copies of one pose.
        /// </summary>
        public TraversalMotionDecision MotionDecisionOf(DefinitionRef recipe) =>
            MotionAuthorityOf(recipe) == TraversalMotionMode.Kinematic
                ? TraversalMotionDecision.IntegrateInEcs
                : TraversalMotionDecision.ExternallyOwned;

        // ------------------------------------------------------------------ the external observation feed

        /// <summary>
        /// Posts one already-sampled observation through the adapter's declared feed. The observation carries its own
        /// sampling stamp, so a completion that arrives from an older activation is refused by the checkpoint owner
        /// rather than being treated as this step's crossing (P-047, REF-A04).
        /// </summary>
        public bool TryPostObservation(
            in TraversalObservationRow observation,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (observation.Runner.IsDefault || observation.Checkpoint.IsDefault)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "an observation must name a runner and a checkpoint volume (P-005)";
                return false;
            }

            if (feed.Count >= TraversalKeys.SensorFeedCapacity)
            {
                FeedOverflowCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the observation feed is at its declared capacity of "
                    + TraversalKeys.SensorFeedCapacity.ToString(CultureInfo.InvariantCulture)
                    + "; a full feed is a refusal, never a silent drop (P-043)";
                return false;
            }

            feed.Add(observation);
            return true;
        }

        /// <summary>Takes the feed's rows in delivery order and clears it; the sensor stage seals them in one step.</summary>
        public IReadOnlyList<TraversalObservationRow> TakeFeed()
        {
            var taken = new List<TraversalObservationRow>(feed.Count);
            for (int i = 0; i < feed.Count; i++)
            {
                taken.Add(feed[i]);
            }

            feed.Clear();
            return taken;
        }

        // ------------------------------------------------------------------ recorded counters used by the stages

        /// <summary>Records one decoded input row, or one the input stage refused.</summary>
        public void RecordInput(bool rejected)
        {
            if (rejected)
            {
                InputRejectionCount++;
                return;
            }

            DecodedInputCount++;
        }

        /// <summary>Records one integrated step and its per-runner outcome (P-034).</summary>
        public void RecordIntegration(int integrated, int externallyOwned, int missingStorage, bool newStep)
        {
            LastIntegratedCount = integrated;
            LastExternallyOwnedCount = externallyOwned;
            MissingStorageCount += missingStorage;
            IntegratedRunnerStepCount += integrated;
            if (newStep)
            {
                SteppedStepCount++;
            }
        }

        /// <summary>Records the effective acceleration one integration read, and whether any row carried it.</summary>
        public void RecordEffectiveAcceleration(int milli, bool missing)
        {
            LastEffectiveAccelerationMilli = milli;
            if (missing)
            {
                AccelerationMissCount++;
            }
        }

        /// <summary>Records one integration the rules refused with a reason (P-044: no half-applied motion).</summary>
        public void RecordRefusedIntegration() => RefusedIntegrationCount++;

        /// <summary>Records one sealed observation, or one the bounded buffer refused (P-043).</summary>
        public void RecordObservation(bool sealedInBuffer)
        {
            if (sealedInBuffer)
            {
                ObservationCount++;
                return;
            }

            ObservationOverflowCount++;
        }

        /// <summary>Records one checkpoint verdict (07 s4.2, REF-A04).</summary>
        public void RecordVerdict(CheckpointVerdict verdict)
        {
            switch (verdict)
            {
                case CheckpointVerdict.Accepted:
                    CrossingCount++;
                    return;
                case CheckpointVerdict.DuplicateCrossing:
                    DuplicateObservationCount++;
                    return;
                case CheckpointVerdict.OutOfOrder:
                    OutOfOrderObservationCount++;
                    return;
                case CheckpointVerdict.AlreadyComplete:
                    OutOfOrderObservationCount++;
                    return;
                case CheckpointVerdict.UnknownCheckpoint:
                    UnknownVolumeObservationCount++;
                    return;
                default:
                    StaleObservationCount++;
                    return;
            }
        }

        /// <summary>Records one committed crossing event staged for the world's committed output (P-045).</summary>
        public void RecordStagedEvent() => StagedEventCount++;

        /// <summary>Records one written committed snapshot row.</summary>
        public void RecordOutput() => OutputRowCount++;

        /// <inheritdoc />
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            Modules.Remove(this);
        }

        private static int CompareRunners(Runner left, Runner right) =>
            left.Target.Value.CompareTo(right.Target.Value);

        private static int CompareVolumes(Volume left, Volume right) =>
            left.Ordinal != right.Ordinal
                ? left.Ordinal.CompareTo(right.Ordinal)
                : left.Target.Value.CompareTo(right.Target.Value);

        private static int CompareDeclarations(TraversalMotionDeclaration left, TraversalMotionDeclaration right) =>
            left.Recipe.CompareTo(right.Recipe);

        /// <summary>
        /// The course's declared domain version for one target (P-042): the module reports the step its own committed
        /// image carries, and only for a target this module actually owns. The kernel never learns what the number
        /// means.
        /// </summary>
        private sealed class TraversalStepDomainVersion : IDomainVersionAuthority
        {
            private readonly TraversalModule module;

            public TraversalStepDomainVersion(TraversalModule module)
            {
                this.module = module;
            }

            /// <inheritdoc />
            public bool TryGetDomainVersion(TargetId target, out ulong version)
            {
                version = 0UL;
                if (module.CourseEntity == Entity.Null
                    || !module.Host.EntityWorld.EntityManager.Exists(module.CourseEntity))
                {
                    return false;
                }

                if (!module.Host.EntityWorld.EntityManager.HasComponent<TraversalCourseSnapshot>(module.CourseEntity))
                {
                    return false;
                }

                if (!module.runnersByTarget.ContainsKey(target.Value.Low) && !target.Equals(module.courseTarget))
                {
                    // A target this module does not own is reported as "no such domain" rather than being given
                    // another target's number (P-005, P-042).
                    return false;
                }

                TraversalCourseSnapshot snapshot =
                    module.Host.EntityWorld.EntityManager.GetComponentData<TraversalCourseSnapshot>(module.CourseEntity);
                version = snapshot.Step;
                return true;
            }
        }

        private readonly struct Runner
        {
            public readonly TargetId Target;
            public readonly Entity Entity;
            public readonly DefinitionRef Recipe;

            public Runner(TargetId target, Entity entity, DefinitionRef recipe)
            {
                Target = target;
                Entity = entity;
                Recipe = recipe;
            }
        }

        private readonly struct Volume
        {
            public readonly TargetId Target;
            public readonly Entity Entity;
            public readonly uint Ordinal;

            public Volume(TargetId target, Entity entity, uint ordinal)
            {
                Target = target;
                Entity = entity;
                Ordinal = ordinal;
            }
        }
    }

    /// <summary>One declared motion authority: one recipe and the mode selected for it.</summary>
    public readonly struct TraversalMotionDeclaration
    {
        /// <summary>The recipe whose motion domain was declared.</summary>
        public readonly Id128 Recipe;

        /// <summary>The selected authority.</summary>
        public readonly TraversalMotionMode Mode;

        /// <summary>Builds one declaration.</summary>
        public TraversalMotionDeclaration(Id128 recipe, TraversalMotionMode mode)
        {
            Recipe = recipe;
            Mode = mode;
        }

        /// <inheritdoc />
        public override string ToString() => Recipe.ToString() + ":" + Mode;
    }

    /// <summary>One runner of the course: its stable target identity, its entity and its recipe (P-004, P-005).</summary>
    public readonly struct TraversalRunnerRef
    {
        /// <summary>The runner's stable target identity.</summary>
        public readonly TargetId Target;

        /// <summary>The runner's ECS entity.</summary>
        public readonly Entity Entity;

        /// <summary>The recipe the runner was spawned from; it selects the motion authority (P-015).</summary>
        public readonly DefinitionRef Recipe;

        /// <summary>Builds one runner reference.</summary>
        public TraversalRunnerRef(TargetId target, Entity entity, DefinitionRef recipe)
        {
            Target = target;
            Entity = entity;
            Recipe = recipe;
        }
    }

    /// <summary>One checkpoint volume of the course: its stable identity, entity and declared ordinal (07 s4.2).</summary>
    public readonly struct TraversalVolumeRef
    {
        /// <summary>The volume's stable target identity.</summary>
        public readonly TargetId Target;

        /// <summary>The volume's ECS entity.</summary>
        public readonly Entity Entity;

        /// <summary>The volume's ordinal in the course definition.</summary>
        public readonly uint Ordinal;

        /// <summary>Builds one volume reference.</summary>
        public TraversalVolumeRef(TargetId target, Entity entity, uint ordinal)
        {
            Target = target;
            Entity = entity;
            Ordinal = ordinal;
        }
    }

    /// <summary>
    /// Storage helpers for the course's ECS rows. They hold no rule: they install a recipe's base layout and read the
    /// published binding row, which is what a gameplay system needs and what a scenario seeds.
    /// </summary>
    public static class TraversalAccess
    {
        /// <summary>Installs a runner's base storage: pose, velocity, jump state and captured input.</summary>
        public static void InstallRunnerStorage(
            EntityManager entityManager,
            Entity runner,
            TraversalVector3i position,
            TraversalVector3i velocity)
        {
            entityManager.AddComponentData(runner, TraversalPose.Of(position, velocity.Y == 0 ? (byte)1 : (byte)0));
            entityManager.AddComponentData(runner, TraversalVelocity.Of(velocity));
            entityManager.AddComponentData(runner, default(TraversalJumpState));
            entityManager.AddComponentData(runner, default(TraversalMovementInput));
        }

        /// <summary>Installs a checkpoint volume's base storage from its declared definition (07 s4.2).</summary>
        public static void InstallVolumeStorage(
            EntityManager entityManager,
            Entity volume,
            uint ordinal,
            TraversalVector3i center,
            int radius)
        {
            entityManager.AddComponentData(volume, new TraversalCheckpointVolume
            {
                Ordinal = ordinal,
                CenterX = center.X,
                CenterY = center.Y,
                CenterZ = center.Z,
                Radius = radius,
                Open = 1,
            });
        }

        /// <summary>Installs the course entity's storage: observations, progress, crossings and the image.</summary>
        public static void InstallCourseStorage(EntityManager entityManager, Entity course)
        {
            entityManager.AddBuffer<TraversalObservationRow>(course);
            entityManager.AddBuffer<TraversalProgressRow>(course);
            entityManager.AddBuffer<TraversalCrossingRow>(course);
            entityManager.AddComponentData(course, default(TraversalCourseSnapshot));
        }

        /// <summary>
        /// The effective `traversal.acceleration` value of one runner, read from its published binding row (07 s4.2:
        /// "`EffectiveAcceleration` — Assembly bridge — Read-only effective derived configuration"). A runner with no
        /// active row reports zero and <paramref name="missing"/> true, so a caller never mistakes "no published
        /// configuration" for "a modifier of zero" (P-009, P-015).
        /// </summary>
        public static int EffectiveAccelerationX(EntityManager entityManager, Entity runner, out bool missing)
        {
            missing = true;
            if (!entityManager.Exists(runner) || !entityManager.HasBuffer<CapabilityBinding>(runner))
            {
                return 0;
            }

            DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(runner);
            if (!AssemblyStorage.TryFindBinding(
                    bindings, TraversalVocabulary.AccelerationCapability, 0U, out CapabilityBinding row)
                || !row.IsActive)
            {
                return 0;
            }

            missing = false;
            return row.Value;
        }

        /// <summary>
        /// Ensures the course entity has one progress row per runner, in canonical runner order, without disturbing an
        /// existing run's progress (07 s4.2: reacquiring a run resumes its data rather than resetting it).
        /// </summary>
        public static void EnsureProgressRow(EntityManager entityManager, Entity course, TargetId runner)
        {
            if (!entityManager.Exists(course) || !entityManager.HasBuffer<TraversalProgressRow>(course))
            {
                return;
            }

            DynamicBuffer<TraversalProgressRow> rows = entityManager.GetBuffer<TraversalProgressRow>(course);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Runner.Equals(runner))
                {
                    return;
                }
            }

            rows.Add(new TraversalProgressRow
            {
                Runner = runner,
                Count = 0U,
                Started = 0,
                LastCrossingSequence = 0U,
                LastCheckpoint = default(TargetId),
                LastCrossingStep = 0UL,
            });

            // The rows are canonically ordered by runner identity, so two runs of one fixture produce the same row
            // order and a replay compares like with like (P-008).
            var ordered = new List<TraversalProgressRow>(rows.Length);
            for (int i = 0; i < rows.Length; i++)
            {
                ordered.Add(rows[i]);
            }

            ordered.Sort(CompareProgressRows);
            rows.Clear();
            for (int i = 0; i < ordered.Count; i++)
            {
                rows.Add(ordered[i]);
            }
        }

        /// <summary>The progress row of one runner; false when the course holds none (P-005).</summary>
        public static bool TryReadProgress(
            EntityManager entityManager,
            Entity course,
            TargetId runner,
            out TraversalProgressRow progress)
        {
            progress = default(TraversalProgressRow);
            if (!entityManager.Exists(course) || !entityManager.HasBuffer<TraversalProgressRow>(course))
            {
                return false;
            }

            DynamicBuffer<TraversalProgressRow> rows = entityManager.GetBuffer<TraversalProgressRow>(course);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Runner.Equals(runner))
                {
                    progress = rows[i];
                    return true;
                }
            }

            return false;
        }

        /// <summary>Writes one progress row back, refusing a row the course does not hold (P-005).</summary>
        public static bool TryWriteProgress(
            EntityManager entityManager,
            Entity course,
            in TraversalProgressRow progress)
        {
            if (!entityManager.Exists(course) || !entityManager.HasBuffer<TraversalProgressRow>(course))
            {
                return false;
            }

            DynamicBuffer<TraversalProgressRow> rows = entityManager.GetBuffer<TraversalProgressRow>(course);
            for (int i = 0; i < rows.Length; i++)
            {
                if (rows[i].Runner.Equals(progress.Runner))
                {
                    rows[i] = progress;
                    return true;
                }
            }

            return false;
        }

        private static int CompareProgressRows(TraversalProgressRow left, TraversalProgressRow right) =>
            left.Runner.Value.CompareTo(right.Runner.Value);
    }

    /// <summary>
    /// `traversal.input` (07 s4.2): it consumes the admitted movement envelopes through the owner's bounded lane,
    /// decodes each payload with the reader bound to its schema, and writes the captured input of the runner the
    /// envelope names. A payload no reader covers and a runner this course does not own are refusals with an observable
    /// result, so nothing is silently dropped (P-042, P-043).
    /// </summary>
    [DisableAutoCreation]
    public partial class TraversalInputSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!TraversalModule.TryGet(World, out TraversalModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            EntityManager entityManager = EntityManager;
            if (plane == null || !entityManager.Exists(module.CourseEntity))
            {
                return;
            }

            // The captured input is step-scoped: this stage clears it first, so a step with no admitted movement
            // leaves every runner's input un-captured instead of exposing the previous step's request (P-043).
            IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
            for (int i = 0; i < runners.Count; i++)
            {
                if (!entityManager.Exists(runners[i].Entity)
                    || !entityManager.HasComponent<TraversalMovementInput>(runners[i].Entity))
                {
                    continue;
                }

                var cleared = default(TraversalMovementInput);
                cleared.CapturedStep = plane.ExecutingStep.Value;
                entityManager.SetComponentData(runners[i].Entity, cleared);
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(TraversalKeys.InputOwner);
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                if (!module.TryRunner(message.Target, out Entity runner)
                    || !entityManager.HasComponent<TraversalMovementInput>(runner))
                {
                    // A movement envelope for a target this course does not own has no owner to answer it: the
                    // kernel's route check admitted the envelope and the domain refuses it here (P-042).
                    plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                    module.RecordInput(true);
                    continue;
                }

                byte[] payload = plane.PayloadOf(message);
                if (plane.Readers.TryRead(
                        message.PayloadSchema,
                        payload,
                        out TraversalMovementInputValue decoded,
                        out string _) != PayloadDecodeOutcome.Decoded)
                {
                    plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                    module.RecordInput(true);
                    continue;
                }

                entityManager.SetComponentData(runner, new TraversalMovementInput
                {
                    HorizontalMilli = decoded.HorizontalMilli,
                    VerticalMilli = decoded.VerticalMilli,
                    JumpPressed = decoded.JumpPressed,
                    Captured = 1,
                    CapturedStep = plane.ExecutingStep.Value,
                });

                module.RecordInput(false);
            }

            // The lane's rows are released only now, after every payload has been decoded into ECS: the input stage is
            // the lane's single declared consumer, so a payload is never read after release (P-043). Without this
            // release the lane still holds rows at the commit boundary, `ValidateCommit` reports them unconsumed and
            // the step faults instead of committing (P-031, P-043).
            plane.ReleaseConsumed(TraversalKeys.InputOwner);
        }
    }

    /// <summary>
    /// `traversal.integrate` (07 s4.2): it advances every owned runner by exactly one fixed step of the rules
    /// package's arithmetic, reading the effective derived acceleration from the published binding row and writing
    /// pose, velocity and jump state directly. A runner whose recipe is declared externally owned is skipped and
    /// counted: integrating it would be a second writer of one pose (P-034, REF-A05).
    /// </summary>
    [DisableAutoCreation]
    public partial class TraversalIntegrateSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!TraversalModule.TryGet(World, out TraversalModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.CourseEntity))
            {
                return;
            }

            TraversalTraceRecorder? trace = TraversalStepTraceRegistry.Of(module);
            trace?.BeginStep(module.Host.CurrentStep.Value, module.Host.CurrentEpoch.Value, module.LastExternallyOwnedCount);

            int integrated = 0;
            int externallyOwned = 0;
            int missingStorage = 0;
            IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
            for (int i = 0; i < runners.Count; i++)
            {
                TraversalRunnerRef runner = runners[i];
                if (module.MotionDecisionOf(runner.Recipe) == TraversalMotionDecision.ExternallyOwned)
                {
                    externallyOwned++;
                    continue;
                }

                if (!entityManager.Exists(runner.Entity)
                    || !entityManager.HasComponent<TraversalPose>(runner.Entity)
                    || !entityManager.HasComponent<TraversalVelocity>(runner.Entity)
                    || !entityManager.HasComponent<TraversalMovementInput>(runner.Entity))
                {
                    missingStorage++;
                    continue;
                }

                TraversalPose pose = entityManager.GetComponentData<TraversalPose>(runner.Entity);
                TraversalVelocity velocity = entityManager.GetComponentData<TraversalVelocity>(runner.Entity);
                TraversalMovementInput input = entityManager.GetComponentData<TraversalMovementInput>(runner.Entity);
                int accelerationX = TraversalAccess.EffectiveAccelerationX(entityManager, runner.Entity, out bool missing);
                module.RecordEffectiveAcceleration(accelerationX, missing);

                var request = new TraversalBodyStepRequest(
                    pose.Vector,
                    velocity.Vector,
                    new TraversalVector3i(accelerationX, 0, 0),
                    input.Captured != 0 ? input.HorizontalMilli : 0,
                    input.Captured != 0 ? input.JumpPressed : (byte)0,
                    pose.Grounded,
                    TraversalKeys.StepMilliseconds);

                if (!TraversalMotionRules.TryStep(in request, out TraversalBodyStepResult result, out string failure))
                {
                    // A refused integration is a domain rejection, not a world fault: the step commits normally with an
                    // observable refusal and no half-applied motion (P-044).
                    trace?.RecordRefusal(failure);
                    module.RecordRefusedIntegration();
                    continue;
                }

                entityManager.SetComponentData(runner.Entity, TraversalPose.Of(result.Pose, result.Grounded));
                entityManager.SetComponentData(runner.Entity, TraversalVelocity.Of(result.Velocity));
                TraversalJumpState jump = entityManager.GetComponentData<TraversalJumpState>(runner.Entity);
                if (result.Jumped != 0)
                {
                    jump.JumpCount++;
                    jump.LastJumpStep = module.Host.CurrentStep.Value;
                }

                jump.Airborne = result.Grounded != 0 ? (byte)0 : (byte)1;
                entityManager.SetComponentData(runner.Entity, jump);
                integrated++;
                trace?.RecordBody(new TraversalBodyTrace(
                    runner.Target, result.Pose, result.Velocity, result.AppliedAcceleration, result.Jumped));
            }

            module.RecordIntegration(integrated, externallyOwned, missingStorage, newStep: true);
        }
    }

    /// <summary>
    /// `traversal.sense` (07 s4.2): it waits for the pose jobs of `traversal.integrate` because its observations
    /// depend on their result, tests the data-defined checkpoint volumes and seals one observation per crossing
    /// candidate. It also seals the adapter's external feed rows, each carrying the stamp it was sampled with, so an
    /// old activation's delivery is refused downstream rather than mistaken for this step's crossing. It never writes
    /// progress or pose.
    /// </summary>
    [DisableAutoCreation]
    public partial class TraversalSenseSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!TraversalModule.TryGet(World, out TraversalModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.CourseEntity)
                || !entityManager.HasBuffer<TraversalObservationRow>(module.CourseEntity))
            {
                return;
            }

            // The sealed observations are step-scoped: the producer clears them first, so a step of no crossings
            // leaves the buffer empty rather than exposing the previous step's rows (P-043).
            DynamicBuffer<TraversalObservationRow> sealedRows =
                entityManager.GetBuffer<TraversalObservationRow>(module.CourseEntity);
            sealedRows.Clear();

            LogicalStepId step = module.Host.CurrentStep;
            AssemblyEpoch epoch = module.Host.CurrentEpoch;
            IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
            IReadOnlyList<TraversalVolumeRef> volumes = module.Volumes();
            for (int r = 0; r < runners.Count; r++)
            {
                if (!entityManager.Exists(runners[r].Entity)
                    || !entityManager.HasComponent<TraversalPose>(runners[r].Entity))
                {
                    continue;
                }

                TraversalVector3i position =
                    entityManager.GetComponentData<TraversalPose>(runners[r].Entity).Vector;
                for (int v = 0; v < volumes.Count; v++)
                {
                    if (!entityManager.Exists(volumes[v].Entity)
                        || !entityManager.HasComponent<TraversalCheckpointVolume>(volumes[v].Entity))
                    {
                        continue;
                    }

                    TraversalCheckpointVolume volume =
                        entityManager.GetComponentData<TraversalCheckpointVolume>(volumes[v].Entity);
                    if (!volume.IsOpen || !volume.Contains(in position))
                    {
                        continue;
                    }

                    var row = new TraversalObservationRow
                    {
                        Runner = runners[r].Target,
                        Checkpoint = volumes[v].Target,
                        CheckpointOrdinal = volume.Ordinal,
                        CrossingSequence = 1U,
                        SampledStep = step.Value,
                        SampledEpoch = epoch.Value,
                        FromExternalFeed = 0,
                    };

                    Seal(module, sealedRows, in row);

                    if (module.DuplicateFirstObservation)
                    {
                        // The declared fixture policy for 07 s4.2's "multiple trigger callbacks": the identical
                        // crossing is reported twice in one step, and the owner must still award it once (REF-A04).
                        Seal(module, sealedRows, in row);
                        module.DuplicateFirstObservation = false;
                    }
                }
            }

            IReadOnlyList<TraversalObservationRow> delivered = module.TakeFeed();
            for (int i = 0; i < delivered.Count; i++)
            {
                TraversalObservationRow row = delivered[i];
                Seal(module, sealedRows, in row);
            }
        }

        /// <summary>
        /// Seals one observation into the bounded step buffer, counting an overflow instead of growing the buffer
        /// without bound (P-043: bounded work, explicit refusal).
        /// </summary>
        private static void Seal(
            TraversalModule module,
            DynamicBuffer<TraversalObservationRow> rows,
            in TraversalObservationRow row)
        {
            bool fits = rows.Length < TraversalKeys.ObservationCapacity;
            module.RecordObservation(fits);
            if (fits)
            {
                rows.Add(row);
            }
        }
    }

    /// <summary>
    /// `traversal.checkpoints` (07 s4.2): the sole writer of run progress and of the committed crossing output. It
    /// applies the pure course rules to each sealed observation, refusing duplicates and out-of-order crossings, and
    /// emits exactly one `CheckpointPassed` per accepted crossing (REF-A04).
    /// </summary>
    [DisableAutoCreation]
    public partial class TraversalCheckpointSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!TraversalModule.TryGet(World, out TraversalModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.CourseEntity)
                || !entityManager.HasBuffer<TraversalObservationRow>(module.CourseEntity)
                || !entityManager.HasBuffer<TraversalProgressRow>(module.CourseEntity)
                || !entityManager.HasBuffer<TraversalCrossingRow>(module.CourseEntity))
            {
                return;
            }

            DynamicBuffer<TraversalObservationRow> observations =
                entityManager.GetBuffer<TraversalObservationRow>(module.CourseEntity);
            DynamicBuffer<TraversalCrossingRow> crossings =
                entityManager.GetBuffer<TraversalCrossingRow>(module.CourseEntity);
            crossings.Clear();

            if (observations.Length == 0)
            {
                return;
            }

            CheckpointCourse course = module.Course();
            LogicalStepId step = module.Host.CurrentStep;
            AssemblyEpoch epoch = module.Host.CurrentEpoch;
            WorldMessagePlane? plane = module.Host.Messages;
            TraversalTraceRecorder? trace = TraversalStepTraceRegistry.Of(module);

            for (int i = 0; i < observations.Length; i++)
            {
                TraversalObservationRow observation = observations[i];
                if (observation.SampledStep != step.Value)
                {
                    // A delivery stamped by another step is a retired activation's arrival: it writes nothing
                    // authoritative and acquires no authority (P-047, REF-A04).
                    module.RecordVerdict(CheckpointVerdict.Malformed);
                    continue;
                }

                TraversalAccess.EnsureProgressRow(entityManager, module.CourseEntity, observation.Runner);
                if (!TraversalAccess.TryReadProgress(
                        entityManager, module.CourseEntity, observation.Runner, out TraversalProgressRow progress))
                {
                    module.RecordVerdict(CheckpointVerdict.Malformed);
                    continue;
                }

                var candidate = new CheckpointObservation(
                    observation.Runner,
                    observation.Checkpoint,
                    observation.CrossingSequence,
                    observation.SampledStep);

                new RunProgress(progress.Count, progress.Started, progress.LastCrossingSequence, progress.LastCheckpoint).TryAdvance(
                    course, in candidate, out RunProgress next, out CheckpointVerdict verdict);
                module.RecordVerdict(verdict);

                if (verdict != CheckpointVerdict.Accepted)
                {
                    continue;
                }

                if (crossings.Length >= TraversalKeys.CrossingCapacity)
                {
                    // The declared bounded write set is full: the rest of this step's crossings are refused before any
                    // write, so the step's output stays a bounded, coherent set (P-043, P-044).
                    module.RecordVerdict(CheckpointVerdict.Malformed);
                    continue;
                }

                var written = new TraversalProgressRow
                {
                    Runner = progress.Runner,
                    Count = next.Count,
                    Started = next.Started,
                    LastCrossingSequence = next.LastCrossingSequence,
                    LastCheckpoint = next.LastCheckpoint,
                    LastCrossingStep = step.Value,
                };

                if (!TraversalAccess.TryWriteProgress(entityManager, module.CourseEntity, in written))
                {
                    module.RecordVerdict(CheckpointVerdict.Malformed);
                    continue;
                }

                var crossing = new TraversalCrossingRow
                {
                    Runner = observation.Runner,
                    Checkpoint = observation.Checkpoint,
                    CheckpointOrdinal = observation.CheckpointOrdinal,
                    CountAfter = next.Count,
                    CrossingSequence = observation.CrossingSequence,
                    Step = step.Value,
                    Epoch = epoch.Value,
                };

                crossings.Add(crossing);
                trace?.RecordCrossing(new TraversalCrossingTrace(
                    observation.Runner, observation.Checkpoint, next.Count));

                // The committed event is staged for this step's commit: an observer sees it only if the whole step
                // commits, and a step failure publishes none of it (P-044, P-045).
                if (plane != null
                    && plane.Output.TryStage(
                        TraversalKeys.CrossingSchema,
                        observation.Runner,
                        default(OperationId),
                        TraversalCommandCodec.WriteCrossing(in crossing),
                        out string _))
                {
                    module.RecordStagedEvent();
                }
            }
        }
    }

    /// <summary>
    /// `traversal.output` (07 s4.2): it reads the integrated motion and the crossings this step committed and writes
    /// one coherent snapshot row. It writes only its own snapshot domain, so no prediction of motion or progress
    /// becomes authority.
    /// </summary>
    [DisableAutoCreation]
    public partial class TraversalOutputSystem : SystemBase
    {
        /// <inheritdoc />
        protected override void OnUpdate()
        {
            if (!TraversalModule.TryGet(World, out TraversalModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.CourseEntity)
                || !entityManager.HasComponent<TraversalCourseSnapshot>(module.CourseEntity))
            {
                return;
            }

            int observationCount = 0;
            if (entityManager.HasBuffer<TraversalObservationRow>(module.CourseEntity))
            {
                observationCount = entityManager.GetBuffer<TraversalObservationRow>(module.CourseEntity).Length;
            }

            int crossingCount = 0;
            if (entityManager.HasBuffer<TraversalCrossingRow>(module.CourseEntity))
            {
                crossingCount = entityManager.GetBuffer<TraversalCrossingRow>(module.CourseEntity).Length;
            }

            var snapshot = new TraversalCourseSnapshot
            {
                Step = module.Host.CurrentStep.Value,
                Epoch = module.Host.CurrentEpoch.Value,
                IntegratedCount = module.LastIntegratedCount,
                ObservationCount = observationCount,
                CrossingCount = crossingCount,
                RefusedObservationCount = module.DuplicateObservationCount
                    + module.OutOfOrderObservationCount
                    + module.StaleObservationCount
                    + module.UnknownVolumeObservationCount,
                ExternallyOwnedCount = module.LastExternallyOwnedCount,
            };

            entityManager.SetComponentData(module.CourseEntity, snapshot);
            module.RecordOutput();

            TraversalTraceRecorder? trace = TraversalStepTraceRegistry.Of(module);
            trace?.CompleteStep();
        }
    }
}
