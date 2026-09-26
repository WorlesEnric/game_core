// GameCore.Rules.Traversal — the traversal challenge's stable names and the pure numeric representation (GC-020).
//
// Normative sources: 07 s4 (the fixed-step motion and observation reference). The stable names are the ones the
// reference composition's tree, recipes, capability and stages use, and they are declared once here so the
// gameplay package, the physics/animation/audio adapters and the qualification gate cannot disagree about what
// `traversal.acceleration` or `traversal.integrate` means.
//
// Shape (07 s4.1):
//
//   course-world      [TraversalRuntime, CheckpointRuntime, InputAdapter, CourseSensorAdapter]
//   ├── valley        [Tailwind, optional]
//   │   ├── runners
//   │   │   └── runner-a : RunnerRecipe
//   │   ├── checkpoint-1 : CheckpointRecipe
//   │   └── showcase  [CapabilityIsolation: traversal.acceleration]
//   │       └── runner-display : RunnerRecipe
//   └── ridge         [Headwind, optional]
//       ├── runners
//       │   └── runner-b : RunnerRecipe
//       └── checkpoint-2 : CheckpointRecipe
//
// `traversal.acceleration` is `Additive` over one registered componentwise int32 reducer, so two applicable
// modifiers add in canonical contribution order and their retraction removes only that source (P-017-P-019).
//
// NUMERIC REPRESENTATION. The pure-motion fixture is deliberately integer: every length is millimetres and every
// velocity/acceleration is thousandths of a metre per second, so one fixed step is exact integer arithmetic and a
// replay of the same admitted input produces byte-identical state (P-008, TEST-022). The reference's
// `1.00 -> 1.04 -> 1.02` values are `1000 -> 1040 -> 1020` in that representation. Floating point, Unity
// `Rigidbody` simulation and host physics queries are explicitly OUTSIDE this repeatability claim (P-008, 07 s4.2):
// they are compared by recorded observation, never by bitwise equality.
#nullable enable
using GameCore.Contracts;

namespace GameCore.Rules.Traversal
{
    /// <summary>The traversal challenge's stable names and the identities derived from them (07 s4, GC-020).</summary>
    public static class TraversalVocabulary
    {
        // ---------------------------------------------------------------- scopes (07 s4.1)

        /// <summary>The course root scope; every other scope is created under it (P-010).</summary>
        public const string CourseWorld = "traversal.course-world";

        /// <summary>The valley scope: the tailwind provider's installation scope.</summary>
        public const string Valley = "traversal.valley";

        /// <summary>The valley's runner scope, holding `runner-a`.</summary>
        public const string ValleyRunners = "traversal.valley-runners";

        /// <summary>The showcase scope; isolated from `traversal.acceleration`, so a modifier never reaches it (P-016).</summary>
        public const string Showcase = "traversal.showcase";

        /// <summary>The ridge scope: the headwind provider's installation scope and the reparent destination.</summary>
        public const string Ridge = "traversal.ridge";

        /// <summary>The ridge's runner scope, which the reparent moves the valley runner subtree into.</summary>
        public const string RidgeRunners = "traversal.ridge-runners";

        // ---------------------------------------------------------------- installations (07 s4.1)

        /// <summary>The course state executor: it owns pose, velocity and jump state (07 s4.2).</summary>
        public const string TraversalRuntime = "traversal.traversal-runtime";

        /// <summary>The progress owner: it owns `RunProgress` and the committed crossing output (07 s4.2).</summary>
        public const string CheckpointRuntime = "traversal.checkpoint-runtime";

        /// <summary>The input adapter: it samples one immutable movement input per captured step (07 s4.2).</summary>
        public const string InputAdapter = "traversal.input-adapter";

        /// <summary>The sensor adapter: it seals spatial checkpoint observations (07 s4.2).</summary>
        public const string CourseSensorAdapter = "traversal.course-sensor-adapter";

        /// <summary>The tailwind modifier: `(+2, 0, 0)` m/s² on the acceleration slot (07 s4.1).</summary>
        public const string Tailwind = "traversal.tailwind";

        /// <summary>The headwind modifier: `(-1, 0, 0)` m/s² on the acceleration slot (07 s4.1).</summary>
        public const string Headwind = "traversal.headwind";

        // ---------------------------------------------------------------- reusable recipes (07 s4.1)

        /// <summary>The runner target recipe; every runner target is declared against it.</summary>
        public const string RunnerRecipe = "traversal.runner-recipe";

        /// <summary>The checkpoint target recipe; it declares only the sensor contract, so no modifier selects it.</summary>
        public const string CheckpointRecipe = "traversal.checkpoint-recipe";

        /// <summary>A display-only runner recipe inside the isolated showcase scope.</summary>
        public const string DisplayRunnerRecipe = "traversal.display-runner-recipe";

        // ---------------------------------------------------------------- targets (07 s4.1)

        /// <summary>The valley's first runner target.</summary>
        public const string RunnerA = "runner-a";

        /// <summary>The ridge's runner, which the reparent destination holds after the move.</summary>
        public const string RunnerB = "runner-b";

        /// <summary>A runner target created after the composition was declared (P-024's future descendant).</summary>
        public const string RunnerC = "runner-c";

        /// <summary>The display runner beneath the isolation boundary.</summary>
        public const string RunnerDisplay = "runner-display";

        /// <summary>The first checkpoint volume, in the valley.</summary>
        public const string CheckpointOne = "checkpoint-1";

        /// <summary>The second checkpoint volume, in the ridge.</summary>
        public const string CheckpointTwo = "checkpoint-2";

        // ---------------------------------------------------------------- capability and slots (07 s4.1)

        /// <summary>The Additive acceleration capability whose effective value is the runner's extra acceleration.</summary>
        public const string Acceleration = "traversal.acceleration";

        /// <summary>The runner recipe's selector contract; a rule that names it selects runner targets only.</summary>
        public const string AccelerationTarget = "traversal.acceleration-target";

        /// <summary>The checkpoint recipe's sensor contract; a checkpoint declares this and nothing else.</summary>
        public const string SensorTarget = "traversal.sensor-target";

        /// <summary>The payload schema of the `traversal.acceleration` slot: three fixed-width int32 components.</summary>
        public const string EffectiveAccelerationSchema = "traversal.effective-acceleration";

        // ---------------------------------------------------------------- rules and registrations

        /// <summary>Rule-name suffix of every modifier's `traversal.acceleration` rule.</summary>
        public const string AccelerationSuffix = ".acceleration";

        /// <summary>The registered componentwise int32-triple reducer bound to the acceleration slot (P-009, P-019).</summary>
        public const string AccelerationReducer = "traversal.reducer.vec3i-sum";

        /// <summary>The registered always-accepting predicate every traversal modifier rule uses.</summary>
        public const string AlwaysPredicate = "traversal.predicate.always";

        /// <summary>`traversal.acceleration` occupies stratum 0; nothing in this fixture depends on it (P-021).</summary>
        public const int AccelerationStratum = 0;

        /// <summary>The tailwind modifier's x acceleration: +2 m/s², in thousandths (07 s4.1).</summary>
        public const int TailwindMilli = 2000;

        /// <summary>The headwind modifier's x acceleration: -1 m/s², in thousandths (07 s4.1).</summary>
        public const int HeadwindMilli = -1000;

        // ---------------------------------------------------------------- the fixed step (07 s4.1, s4.2)

        /// <summary>The fixture's configured fixed step: 20 ms, i.e. a 50 Hz simulation rate (07 s4).</summary>
        public const int StepMilliseconds = 20;

        /// <summary>Host ticks per second of the qualification clock (10 MHz, the runtime's declared clock rate).</summary>
        public const ulong TicksPerSecond = 10000000UL;

        /// <summary>Host ticks of one fixed step: 20 ms of a 10 MHz clock.</summary>
        public const ulong StepDurationTicks = TicksPerSecond * (ulong)StepMilliseconds / 1000UL;

        /// <summary>
        /// The declared catch-up bound of one host update (07 s4.2: "processes at most four simulation steps in one
        /// host update, and retains excess backlog for subsequent updates").
        /// </summary>
        public const uint MaxStepsPerPump = 4U;

        /// <summary>
        /// The fixture's configured maximum retained backlog, in whole steps (07 s4.2: "Reaching the configured
        /// maximum retained backlog pauses advancement with a diagnostic rather than silently discarding
        /// authoritative time. This is fixture configuration"). Twenty steps is 400 ms of unprocessed host time.
        /// </summary>
        public const ulong MaxRetainedBacklogSteps = 20UL;

        /// <summary>The scale of the integer representation: 1000 milli-units per metre or per m/s (see the header).</summary>
        public const int MilliScale = 1000;

        /// <summary>The baseline horizontal speed the fixture seeds: 1.00 m/s, so `Tailwind` yields 1.04 (07 s4.3).</summary>
        public const int SeededVelocityMilli = 1000;

        /// <summary>
        /// The fixture's declared comparison policy for pure-motion state: 5 mm/s (07 s4.3's "within the specified
        /// float tolerance"). The integer fixture matches exactly; this bound exists so the SAME comparison code can
        /// be applied to a recorded engine-observation trace, which is a different claim (P-008).
        /// </summary>
        public const int VelocityToleranceMilli = 5;

        /// <summary>Velocity after the tailwind step, in thousandths: `1.00 + 2 * 0.02 = 1.04` (07 s4.3).</summary>
        public const int VelocityAfterTailwindMilli = SeededVelocityMilli + (TailwindMilli * StepMilliseconds / MilliScale);

        /// <summary>Velocity after a reparent to `Headwind`, in thousandths: `1.04 - 1 * 0.02 = 1.02` (07 s4.3).</summary>
        public const int VelocityAfterHeadwindMilli =
            VelocityAfterTailwindMilli + (HeadwindMilli * StepMilliseconds / MilliScale);

        // ---------------------------------------------------------------- the checkpoint course

        /// <summary>Checkpoint ordinals in the course definition, ascending; progress follows this order (07 s4.2).</summary>
        public const uint CheckpointOneOrdinal = 0U;

        /// <summary>Ordinal of the second checkpoint in the course definition.</summary>
        public const uint CheckpointTwoOrdinal = 1U;

        /// <summary>Number of volumes in the fixture's course definition.</summary>
        public const int CourseLength = 2;

        /// <summary>The crossing sequence a fresh run starts from: no crossing has been observed yet.</summary>
        public const uint FirstCrossingSequence = 1U;

        // ---------------------------------------------------------------- generated registration identities

        /// <summary>The `traversal.acceleration` capability identity.</summary>
        public static CapabilityId AccelerationCapability { get; } = TraversalIdentity.Capability(Acceleration);

        /// <summary>Slot 0 of `traversal.acceleration`, derived as a contract declaration derives it (05 s2).</summary>
        public static SlotId AccelerationSlot { get; } = TraversalIdentity.Slot(Acceleration + ".slot-0");

        /// <summary>The versioned capability reference of the acceleration slot.</summary>
        public static CapabilityRef AccelerationContract { get; } = TraversalIdentity.CapabilityRef(Acceleration);

        /// <summary>The versioned schema reference of the acceleration slot's payload.</summary>
        public static SchemaRef EffectiveAccelerationSchemaRef { get; } =
            TraversalIdentity.SchemaRef(EffectiveAccelerationSchema);

        /// <summary>The generated key a traversal catalog binds the componentwise int32-triple reducer under.</summary>
        public static FactoryKey AccelerationReducerKey { get; } = TraversalIdentity.Key(AccelerationReducer);

        /// <summary>The generated key a traversal catalog binds the always-accepting predicate under.</summary>
        public static FactoryKey AlwaysPredicateKey { get; } = TraversalIdentity.Key(AlwaysPredicate);

        /// <summary>The `traversal.acceleration` rule identity of one modifier: `&lt;install&gt;` + suffix.</summary>
        public static RuleId AccelerationRule(string installStableName) =>
            TraversalIdentity.Rule(installStableName + AccelerationSuffix);

        /// <summary>The selector schema identity of one target recipe, as a rule declaration names it.</summary>
        public static SchemaRef SelectorSchema(string recipeStableName) =>
            TraversalIdentity.SchemaRef(recipeStableName);
    }
}
