// GameCore.Gameplay.Traversal — the traversal course's stable identities and declared vocabulary (GC-020).
//
// Normative sources: 07 s4 (the fixed-step motion and observation reference), 00 P-002/P-034 (participants, one owner
// per authoritative domain), P-039/P-040 (declared stages, system keys and their dependencies), P-043 (a declared
// buffer names its producers, its single consuming owner, its order key, its lifetime and its capacity) and 04 s8
// (registration is data: a precompiled factory key resolves a plugin, never reflection).
//
// Every identity is derived from a canonical stable name with the production `StableNameKeyDerivation`, which is the
// same rule `TraversalVocabulary` (the rules package) and the content compiler use, so the gameplay package, the
// rule package and a generated catalog cannot disagree about what `traversal.integrate` or `traversal.acceleration`
// means. The five stages are 07 s4.2's graph verbatim:
//
//   traversal.input -> traversal.integrate -> traversal.sense -> traversal.checkpoints -> traversal.output
//   traversal.integrate ---------------------------------------------------------> traversal.output
//
// FOUR OWNERS, ONE PER WRITTEN DOMAIN. 07 s4.2 names the authorities with distinct state, and this file declares one
// owner for each domain a stage writes: `traversal.TraversalRuntime` owns pose/velocity/jump state, the input adapter
// owns the input it captured, the sensor adapter owns the observations it sealed, and `traversal.CheckpointRuntime`
// owns run progress, the committed crossings and the committed image. No two owners write the same domain, which is
// what lets the compiled schedule order them without a coordinator (P-034, P-040).
#nullable enable
using GameCore.Contracts;
using GameCore.Rules.Traversal;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>The traversal course's stable identities, domains, slots, buffers, routes and declared bounds.</summary>
    public static class TraversalKeys
    {
        // ---------------------------------------------------------------- plugin declarations

        /// <summary>
        /// The precompiled plugin factory this build's catalog registers for every traversal plugin declaration
        /// (`traversal.factory.course-plugin`, a `PluginFactory` registration).
        /// </summary>
        public static readonly FactoryKey PluginFactoryKey =
            TraversalIdentity.Key("traversal.factory.course-plugin");

        /// <summary>
        /// The configuration schema every traversal declaration is admitted under. The catalog validates both the
        /// factory key and this schema before a mount is planned (P-009, P-020).
        /// </summary>
        public static readonly SchemaRef ConfigSchema = TraversalIdentity.SchemaRef("traversal.schema.course-config");

        /// <summary>Stable plugin type of one traversal declaration; one declaration per plugin type (P-009).</summary>
        public static PluginTypeId PluginType(string declarationStableName) =>
            TraversalIdentity.PluginType(declarationStableName + ".type");

        /// <summary>Stable instance identity of one traversal mount; it survives a remount (P-005).</summary>
        public static PluginInstanceId Instance(string declarationStableName) =>
            TraversalIdentity.Instance(declarationStableName + ".instance");

        // ---------------------------------------------------------------- stages (07 s4.2)

        /// <summary>`traversal.input`: the input adapter's sampling stage (07 s4.2).</summary>
        public static readonly StageId InputStage = TraversalIdentity.Stage("traversal.stage.input");

        /// <summary>`traversal.integrate`: it integrates pose and velocity for its owned runners (07 s4.2).</summary>
        public static readonly StageId IntegrateStage = TraversalIdentity.Stage("traversal.stage.integrate");

        /// <summary>`traversal.sense`: it seals spatial checkpoint observations (07 s4.2).</summary>
        public static readonly StageId SenseStage = TraversalIdentity.Stage("traversal.stage.sense");

        /// <summary>`traversal.checkpoints`: the sole writer of run progress (07 s4.2).</summary>
        public static readonly StageId CheckpointStage = TraversalIdentity.Stage("traversal.stage.checkpoints");

        /// <summary>`traversal.output`: it prepares the coherent committed snapshot (07 s4.2).</summary>
        public static readonly StageId OutputStage = TraversalIdentity.Stage("traversal.stage.output");

        // ---------------------------------------------------------------- systems

        /// <summary>Generated dispatch key of the `traversal.input` system.</summary>
        public static readonly FactoryKey InputSystem = TraversalIdentity.Key("traversal.system.input");

        /// <summary>Generated dispatch key of the `traversal.integrate` system.</summary>
        public static readonly FactoryKey IntegrateSystem = TraversalIdentity.Key("traversal.system.integrate");

        /// <summary>Generated dispatch key of the `traversal.sense` system.</summary>
        public static readonly FactoryKey SenseSystem = TraversalIdentity.Key("traversal.system.sense");

        /// <summary>Generated dispatch key of the `traversal.checkpoints` system.</summary>
        public static readonly FactoryKey CheckpointSystem = TraversalIdentity.Key("traversal.system.checkpoints");

        /// <summary>Generated dispatch key of the `traversal.output` system.</summary>
        public static readonly FactoryKey OutputSystem = TraversalIdentity.Key("traversal.system.output");

        /// <summary>The five traversal system keys in dispatch order, so a scenario can assert the registered set.</summary>
        public static readonly FactoryKey[] SystemKeys =
        {
            InputSystem,
            IntegrateSystem,
            SenseSystem,
            CheckpointSystem,
            OutputSystem,
        };

        // ---------------------------------------------------------------- owners (07 s4.2)

        /// <summary>
        /// The owner of runner motion: `KinematicPose`, `Velocity` and `JumpState` (07 s4.2's table). One instance
        /// owns every runner entity of the course, so one bounded decision integrates the whole course's motion.
        /// </summary>
        public static readonly OwnerId MotionOwner = TraversalIdentity.Owner("traversal.owner.motion");

        /// <summary>
        /// The owner of the captured movement input (07 s4.2's `MovementInput`: "`InputAdapter` sampling stage;
        /// immutable step input after capture").
        /// </summary>
        public static readonly OwnerId InputOwner = TraversalIdentity.Owner("traversal.owner.input");

        /// <summary>
        /// The owner of the sealed spatial observations (07 s4.2's `CheckpointObservation[]`: "`CourseSensorAdapter`;
        /// sealed spatial observations, not a progress mutation").
        /// </summary>
        public static readonly OwnerId SensorOwner = TraversalIdentity.Owner("traversal.owner.sensor");

        /// <summary>
        /// The owner of run progress and of the committed crossing output (07 s4.2's `RunProgress` and
        /// `CheckpointPassed`, both `CheckpointRuntime`). One owner for both is what makes "one progress increment and
        /// one committed award" a single bounded decision (P-034, P-044).
        /// </summary>
        public static readonly OwnerId CheckpointOwner = TraversalIdentity.Owner("traversal.owner.checkpoint");

        // ---------------------------------------------------------------- domains and slots

        /// <summary>Domain of `KinematicPose`, `Velocity` and `JumpState` on a runner entity (07 s4.2).</summary>
        public static readonly SchemaRef MotionDomain = TraversalIdentity.SchemaRef("traversal.domain.motion");

        /// <summary>Domain of `MovementInput` on a runner entity (07 s4.2).</summary>
        public static readonly SchemaRef InputDomain = TraversalIdentity.SchemaRef("traversal.domain.input");

        /// <summary>Domain of the sealed observation rows on the course entity (07 s4.2).</summary>
        public static readonly SchemaRef ObservationDomain = TraversalIdentity.SchemaRef("traversal.domain.observation");

        /// <summary>Domain of `RunProgress` rows on the course entity (07 s4.2).</summary>
        public static readonly SchemaRef ProgressDomain = TraversalIdentity.SchemaRef("traversal.domain.progress");

        /// <summary>Domain of the committed crossing rows on the course entity (07 s4.2).</summary>
        public static readonly SchemaRef CrossingDomain = TraversalIdentity.SchemaRef("traversal.domain.crossing");

        /// <summary>Domain of the committed snapshot component on the course entity (07 s4.2).</summary>
        public static readonly SchemaRef SnapshotDomain = TraversalIdentity.SchemaRef("traversal.domain.snapshot");

        /// <summary>Slot of the runner motion domain.</summary>
        public static readonly SlotId MotionSlot = TraversalIdentity.Slot("traversal.slot.motion");

        /// <summary>Slot of the captured input domain.</summary>
        public static readonly SlotId InputSlot = TraversalIdentity.Slot("traversal.slot.input");

        /// <summary>Slot of the sealed observation domain.</summary>
        public static readonly SlotId ObservationSlot = TraversalIdentity.Slot("traversal.slot.observation");

        /// <summary>Slot of the run-progress domain.</summary>
        public static readonly SlotId ProgressSlot = TraversalIdentity.Slot("traversal.slot.progress");

        /// <summary>Slot of the committed crossing domain.</summary>
        public static readonly SlotId CrossingSlot = TraversalIdentity.Slot("traversal.slot.crossing");

        /// <summary>Slot of the committed snapshot domain.</summary>
        public static readonly SlotId SnapshotSlot = TraversalIdentity.Slot("traversal.slot.snapshot");

        /// <summary>Physical layout key of the motion slot: the runner's own pose/velocity components.</summary>
        public static readonly FactoryKey MotionLayout = TraversalIdentity.Key("traversal.layout.motion");

        /// <summary>Physical layout key of the input slot: the runner's captured-input component.</summary>
        public static readonly FactoryKey InputLayout = TraversalIdentity.Key("traversal.layout.input");

        /// <summary>Physical layout key of the observation slot: the course entity's sealed observation buffer.</summary>
        public static readonly FactoryKey ObservationLayout = TraversalIdentity.Key("traversal.layout.observation");

        /// <summary>Physical layout key of the progress slot: the course entity's progress buffer.</summary>
        public static readonly FactoryKey ProgressLayout = TraversalIdentity.Key("traversal.layout.progress");

        /// <summary>Physical layout key of the crossing slot: the course entity's crossing buffer.</summary>
        public static readonly FactoryKey CrossingLayout = TraversalIdentity.Key("traversal.layout.crossing");

        /// <summary>Physical layout key of the snapshot slot: the course entity's snapshot component.</summary>
        public static readonly FactoryKey SnapshotLayout = TraversalIdentity.Key("traversal.layout.snapshot");

        /// <summary>Physical component schema of the motion slot (a runner's `KinematicPose`).</summary>
        public static readonly SchemaRef MotionComponent = TraversalIdentity.SchemaRef("traversal.schema.pose");

        /// <summary>Physical component schema of the input slot.</summary>
        public static readonly SchemaRef InputComponent = TraversalIdentity.SchemaRef("traversal.schema.movement-input");

        /// <summary>Physical component schema of the observation slot.</summary>
        public static readonly SchemaRef ObservationComponent =
            TraversalIdentity.SchemaRef("traversal.schema.checkpoint-observation");

        /// <summary>Physical component schema of the progress slot.</summary>
        public static readonly SchemaRef ProgressComponent = TraversalIdentity.SchemaRef("traversal.schema.run-progress");

        /// <summary>Physical component schema of the crossing slot.</summary>
        public static readonly SchemaRef CrossingComponent =
            TraversalIdentity.SchemaRef("traversal.schema.checkpoint-passed");

        /// <summary>Physical component schema of the snapshot slot.</summary>
        public static readonly SchemaRef SnapshotComponent = TraversalIdentity.SchemaRef("traversal.schema.course-snapshot");

        /// <summary>Field key of the runner's pose component within the motion slot.</summary>
        public static readonly FactoryKey PoseField = TraversalIdentity.Key("traversal.field.motion.pose");

        /// <summary>Field key of the runner's velocity component within the motion slot.</summary>
        public static readonly FactoryKey VelocityField = TraversalIdentity.Key("traversal.field.motion.velocity");

        /// <summary>Field key of the runner's jump state within the motion slot.</summary>
        public static readonly FactoryKey JumpField = TraversalIdentity.Key("traversal.field.motion.jump");

        /// <summary>Field key of the runner's captured input within the input slot.</summary>
        public static readonly FactoryKey MovementInputField = TraversalIdentity.Key("traversal.field.input.captured");

        /// <summary>Field key of the sealed observation rows within the observation slot.</summary>
        public static readonly FactoryKey ObservationRowsField = TraversalIdentity.Key("traversal.field.observation.rows");

        /// <summary>Field key of the progress rows within the progress slot.</summary>
        public static readonly FactoryKey ProgressRowsField = TraversalIdentity.Key("traversal.field.progress.rows");

        /// <summary>Field key of the crossing rows within the crossing slot.</summary>
        public static readonly FactoryKey CrossingRowsField = TraversalIdentity.Key("traversal.field.crossing.rows");

        /// <summary>Field key of the committed snapshot within the snapshot slot.</summary>
        public static readonly FactoryKey SnapshotField = TraversalIdentity.Key("traversal.field.snapshot.image");

        // ---------------------------------------------------------------- declared step buffer (P-043)

        /// <summary>
        /// The declared step buffer between `traversal.sense` and `traversal.checkpoints` (07 s4.2): the sensor
        /// adapter produces the sealed observations and the checkpoint owner is their single consumer, so the compiled
        /// schedule carries a real producer-before-consumer edge and a deferred playback point.
        /// </summary>
        public static readonly BufferId ObservationBuffer = TraversalIdentity.Buffer("traversal.buffer.observation");

        /// <summary>Order key of the observation buffer; consumption order is declared, never incidental (P-008).</summary>
        public static readonly FactoryKey ObservationOrderKey = TraversalIdentity.Key("traversal.order.observation");

        // ---------------------------------------------------------------- command lane (P-042)

        /// <summary>The route one captured movement input is admitted through (`traversal.route.movement`).</summary>
        public static readonly RouteId CommandRoute = TraversalIdentity.Route("traversal.route.movement");

        /// <summary>
        /// Producer key the host's ingress rows carry, so a lane's origin is declared rather than inferred (P-043):
        /// every admitted movement envelope entered through the world's own host ingress.
        /// </summary>
        public static readonly FactoryKey HostIngressProducer = TraversalIdentity.Key("traversal.producer.host");

        /// <summary>Ingress buffer of the movement route.</summary>
        public static readonly BufferId CommandLane = TraversalIdentity.Buffer("traversal.buffer.movement-lane");

        /// <summary>
        /// The world definition's declared fixed step, in whole milliseconds: the reference's configured 20 ms
        /// (07 s4.1). The integration reads this value, so a world with another step duration declares another one.
        /// </summary>
        public const int StepMilliseconds = TraversalVocabulary.StepMilliseconds;

        /// <summary>The world definition's declared fixed step, in host ticks.</summary>
        public const ulong StepDurationTicks = TraversalVocabulary.StepDurationTicks;

        /// <summary>Order key of the movement lane (P-008: admission sequence first).</summary>
        public static readonly FactoryKey CommandOrderKey = TraversalIdentity.Key("traversal.order.command");

        /// <summary>Payload schema of one captured movement input (`traversal.schema.movement-input`).</summary>
        public static readonly SchemaRef CommandSchema = TraversalIdentity.SchemaRef("traversal.schema.movement-input");

        /// <summary>Schema of the committed crossing event one accepted crossing exposes (P-045).</summary>
        public static readonly SchemaRef CrossingSchema = TraversalIdentity.SchemaRef("traversal.schema.checkpoint-passed");

        // ---------------------------------------------------------------- recipes

        /// <summary>The runner recipe, as a target definition reference resolves it (P-015, P-024).</summary>
        public static readonly DefinitionRef RunnerRecipe = RecipeOfStableName(TraversalVocabulary.RunnerRecipe);

        /// <summary>The checkpoint recipe; it declares only its sensor contract.</summary>
        public static readonly DefinitionRef CheckpointRecipe = RecipeOfStableName(TraversalVocabulary.CheckpointRecipe);

        /// <summary>The display runner recipe, beneath the isolation boundary (P-016).</summary>
        public static readonly DefinitionRef DisplayRunnerRecipe =
            RecipeOfStableName(TraversalVocabulary.DisplayRunnerRecipe);

        /// <summary>One target recipe as a definition reference: `&lt;recipe&gt;.definition` at revision one.</summary>
        public static DefinitionRef RecipeOfStableName(string recipeStableName) =>
            TraversalIdentity.Recipe(recipeStableName + ".definition", recipeStableName);

        // ---------------------------------------------------------------- world and operation identity

        /// <summary>World definition of a traversal course world.</summary>
        public static readonly WorldDefinitionId WorldDefinition =
            new WorldDefinitionId(TraversalIdentity.Id("traversal.world-definition.course"));

        /// <summary>Issuer of every operation this fixture mints; a stable id, never a timing value (P-050).</summary>
        public static readonly Id128 Issuer = TraversalIdentity.Id("traversal.issuer.course-challenge");

        // ---------------------------------------------------------------- declared bounds and seeded state

        /// <summary>Capacity of a runner's captured-input step buffer, i.e. one row per sealed step.</summary>
        public const int InputCapacity = 4;

        /// <summary>
        /// Capacity of the sealed observation buffer. The sensor adapter explicitly handles a full query buffer as
        /// possible truncation rather than growing it without bound (07 s4.2).
        /// </summary>
        public const int ObservationCapacity = 32;

        /// <summary>Capacity of the course's progress rows: one row per runner.</summary>
        public const int ProgressCapacity = 16;

        /// <summary>Capacity of the committed crossing rows one step may produce (a bounded write set, P-044).</summary>
        public const int CrossingCapacity = 16;

        /// <summary>Capacity of the movement route's bounded ingress lane (07 s4.1's four-step catch-up fits).</summary>
        public const int CommandCapacity = 8;

        /// <summary>Bounded number of externally delivered observations the sensor feed accepts (07 s4.2).</summary>
        public const int SensorFeedCapacity = 16;

        /// <summary>
        /// Radius of the fixture's data-defined checkpoint volumes, in millimetres: a checkpoint volume is
        /// "declared host spatial query ... simple checkpoint volumes from pure data" (07 s4.2), so it is data.
        /// </summary>
        public const int CheckpointRadiusMilli = 250;

        /// <summary>The ground plane of this package's declared motion policy, in millimetres (see the rules package).</summary>
        public const int GroundYMilli = 0;

        /// <summary>Seed displacement of the checkpoint volumes along x, so the two volumes differ.</summary>
        public const int CheckpointOneXMilli = 0;

        /// <summary>Seed displacement of the second checkpoint volume along x.</summary>
        public const int CheckpointTwoXMilli = 10000;

        /// <summary>The declared catch-up bound of one host update (07 s4.2's "at most four simulation steps").</summary>
        public const uint MaxStepsPerPump = TraversalVocabulary.MaxStepsPerPump;
    }
}
