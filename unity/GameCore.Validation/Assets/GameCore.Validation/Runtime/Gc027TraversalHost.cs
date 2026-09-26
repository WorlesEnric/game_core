// GameCore.Validation.ProbeHost — the GC-027 recovery half of the traversal course family.
//
// `Gc020TraversalHost.CourseFamily` is already `partial` across two files: `Gc020TraversalHost.cs` declares it as
// `IGc020Family` (its catalog, scope tree, live targets, movement route and stage runtime) and
// `W6FamilyTraversalHost.cs` adds the Wave 6 gate's own cycle contract. This is the third part, and it adds the two
// halves a recovery needs:
//
//   * `IGc018Family` — the checkpoint half: the one pending movement command a capture dispositions (P-053's
//     "already queued external commands are either included with ledger/cutoff or explicitly rejected"), the motion
//     slot seeded dormant, the persistent logical-step clock and its wake payload, and the composition enrichment a
//     capture has to carry (P-032, P-038, P-016). `TryAttachRuntime` is answered by the course's own
//     `AttachStageRuntime`, so the recovery runner attaches the real five-stage module over the local physics scene
//     exactly as `Gc020Scenario` does (P-001);
//   * `IGc027Family` — the recovery half: the genre's declared temporal model (a fixed-step course must recover as a
//     fixed-step course, P-036), the catalog fingerprint, the committed checkpoint codecs, the migration graph and
//     the allocated schema set, the composition edit that faults the source world after its first live write, and
//     the engine-physics domain the run proves is re-derived rather than restored (P-054).
//
// WHAT THIS FILE DELIBERATELY DOES NOT DO
//
// It declares no delivery destination. The traversal course has no outbox and no external effect, so
// `HasDeliveryObligation` is false and the run skips the delivery observations rather than inventing an endpoint for
// a genre that has none (P-003: only the recipient's package can say what its mutation is). It also declines to
// publish the fixture's declared `SetupEdits`, because the course tree is already the world definition's declared
// tree in the lane seed — the same reason `Gc020Scenario` does not publish them (P-010).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Execution.Persistence;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.Planning.Scheduling;
using GameCore.Rules.Traversal;
using GameCore.Unity.Adapters.Physics;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Persistence;
using GameCore.Validation.Generated;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The GC-027 recovery facts of one traversal course: its pending movement command, its dormant motion row, its
    /// persistent clock, its boundaries and its engine-physics domain.
    /// </summary>
    public static partial class Gc020TraversalHost
    {
        /// <summary>Horizontal acceleration the pending movement sample carries; zero is a legal captured input (P-042).</summary>
        private const int PendingInputHorizontalMilli = 0;

        /// <summary>Stable name of the course's persistent logical-step clock (P-038).</summary>
        private const string WakeClockName = "traversal.clock.step";

        /// <summary>Value the dormant progress row is seeded with; it is not the seeded run progress (P-032).</summary>
        private const int DormantProgressValue = 7;

        /// <summary>Admitted steps the source world runs before its capture, so its motion state has really moved.</summary>
        private const uint RecoveryAdmittedSteps = 2U;

        /// <summary>
        /// The recovery family: the course's own declarations plus the checkpoint and recovery halves. It is the
        /// same `CourseFamily` instance type the GC-020 and Wave 6 gates drive, so all three qualification paths run
        /// one family implementation (P-001).
        /// </summary>
        public sealed partial class CourseFamily : IGc027Family
        {
            private CheckpointCodecSet? checkpointCodecs;
            private CheckpointMigrationRegistry? recoveryMigrations;
            private IReadOnlyList<SchemaRef>? allocatedSchemas;
            private Gc027PhysicsDomain? physicsDomain;
            private TraversalRunnerRef? physicsSubject;
            private Gc020StageRuntime? attachedRuntime;
            private PipelineDescriptorReport? descriptorForAttach;

            // ---------------------------------------------------------------- the checkpoint half (IGc018Family)

            /// <summary>
            /// One movement command on the course's own declared route, admitted and left unexecuted, so the capture
            /// has a real queued external command to disposition (P-037, P-053). Its payload is the production
            /// traversal codec's output, so a restore re-admits exactly the bytes the source world admitted — which is
            /// what makes "pending-input disposition intact" a comparison rather than a claim.
            /// </summary>
            public CommandEnvelope QueuedCommand(WorldId world, OperationId operation)
            {
                return new CommandEnvelope(
                    operation,
                    MovementRoute,
                    VelocityAssertedTarget,
                    MovementSchema,
                    null,
                    MovementPayload(PendingInputHorizontalMilli, 0));
            }

            /// <summary>Schema version the active motion row is seeded at (P-032).</summary>
            public uint ActiveSlotVersion => TraversalKeys.MotionDomain.Version;

            /// <summary>The course's declared persistent logical-step clock (P-038).</summary>
            public Id128 WakeClockId => TraversalIdentity.Id(WakeClockName);

            /// <summary>The motion domain schema the clock's wake declares (P-038, P-053).</summary>
            public SchemaRef WakePayloadSchema => TraversalKeys.MotionDomain;

            /// <summary>The valley runner owns both the active motion row and the dormant progress row (P-032).</summary>
            public TargetId DormantTarget => VelocityAssertedTarget;

            public OwnerId DormantOwner => TraversalKeys.MotionOwner;

            /// <summary>The progress slot: a different slot of the same owner and target as the active motion row.</summary>
            public SlotId DormantSlot => TraversalKeys.ProgressSlot;

            public uint DormantVersion => TraversalKeys.MotionDomain.Version;

            public int DormantValue => DormantProgressValue;

            /// <summary>
            /// One capability-isolation member and one explicit exclusion on the showcase scope (P-016). The showcase
            /// owns the display runner and carries no boundary of its own, so a recovery that reopened the boundary or
            /// dropped the exclusion cannot report the same grant rows.
            /// </summary>
            public CompositionEditPayload BoundaryEnrichment()
            {
                var members = new List<Id128> { TraversalVocabulary.AccelerationCapability.Value };
                var exclusions = new List<ExclusionRule>
                {
                    new ExclusionRule(
                        ExclusionTargetKind.Capability,
                        TraversalVocabulary.AccelerationCapability.Value,
                        default(ScopeId),
                        default(TargetId),
                        false),
                };

                return Gc018Scenario.ScopeBoundaries(
                    TraversalCourseComposition.ShowcaseScope,
                    new IsolationSet(false, null),
                    new IsolationSet(false, members),
                    exclusions);
            }

            public ScopeId EnrichedScope => TraversalCourseComposition.ShowcaseScope;

            /// <summary>
            /// Attaches the course's own five-stage runtime to a just-built world, so the recovered world's input
            /// stage really consumes its ingress lane and its integrate stage really owns the motion domain — the
            /// same attachment `Gc020Scenario` performs, over the same real local physics scene (P-001, P-043).
            /// </summary>
            public bool TryAttachRuntime(Gc018RuntimeWorld world, out string detail)
            {
                if (world == null)
                {
                    throw new ArgumentNullException(nameof(world));
                }

                if (descriptorForAttach == null)
                {
                    detail = "the traversal family has no compiled pipeline descriptor to attach its runtime with.";
                    return false;
                }

                Gc020StageRuntime attached = AttachStageRuntime(
                    world.Host, descriptorForAttach, world.Targets, world.Seeder);
                attachedRuntime = attached;
                physicsDomain = BuildPhysicsDomain(attached);
                detail = string.Empty;
                return true;
            }

            /// <summary>Releases the runtime the last attach created and its local scene (P-047, P-048).</summary>
            public void DetachRuntime(Gc018RuntimeWorld world)
            {
                attachedRuntime?.Dispose();
                attachedRuntime = null;
                physicsDomain = null;
                physicsSubject = null;
                _ = world;
            }

            // ---------------------------------------------------------------- the recovery half (IGc027Family)

            /// <summary>
            /// The traversal course declares no delivery obligation: it has no outbox and no external effect, so the
            /// run skips the delivery observations instead of inventing an endpoint (P-003, P-045).
            /// </summary>
            public bool HasDeliveryObligation => false;

            // The seven delivery members below exist because the interface requires them, and each answers the only
            // honest value a genre with no delivery destination has: an unset identity, an empty payload and a
            // zero-capacity outbox. Nothing in the protocol reads them while `HasDeliveryObligation` is false, and a
            // run that did would be refused rather than silently addressed to an invented endpoint (P-003, P-004).

            /// <summary>Unset: the course addresses no delivery destination (P-003, P-004).</summary>
            public Id128 DeliveryDestinationId => default(Id128);

            /// <summary>Unset: the course accepts no destination command schema (P-054).</summary>
            public SchemaRef DeliveryCommandSchema => default(SchemaRef);

            /// <summary>Empty: the course carries no destination command bytes (P-045).</summary>
            public byte[] DeliveryPayload() => Array.Empty<byte>();

            /// <summary>Unset: the course records no obligation payload schema (P-053).</summary>
            public SchemaRef DeliveryPayloadSchema => default(SchemaRef);

            /// <summary>Zero: the course holds no open obligation, so its capacity is none (P-043).</summary>
            public int OutboxCapacity => 0;

            /// <summary>Zero: the course retains no terminal delivery record (P-045).</summary>
            public int OutboxTerminalRetention => 0;

            /// <summary>
            /// `Unspecified`, not `Durable`: a genre with no outbox makes no durability claim at all, and claiming one
            /// would be the false promise P-045 forbids (P-045).
            /// </summary>
            public OutboxDurability OutboxDurabilityClass => OutboxDurability.Unspecified;

            /// <summary>
            /// False: the course tree is already the world definition's declared tree in <see cref="LaneSeed"/>, so
            /// publishing the fixture's scope creates would duplicate scopes the seed already carries (P-010).
            /// </summary>
            public bool PublishesDeclaredSetupEdits => false;

            /// <summary>
            /// The declared fixed-step model. A recovery of this course is a fixed-step world, which is what makes
            /// "one simulation per admitted step" a claim about the recovered world too (P-036, 07 s4.1).
            /// </summary>
            public TemporalModel TemporalModel => TemporalModel.FixedStep;

            /// <summary>The course's declared 20 ms step and four-step catch-up bound (07 s4.1, P-036).</summary>
            public FixedStepSettings FixedStep => new FixedStepSettings(
                StepDurationTicks,
                TraversalRegistration.TicksPerSecond,
                MaxStepsPerPump,
                false);

            /// <summary>
            /// The declared step in seconds, which is the delta one admitted engine simulation is stepped for. It is
            /// derived from the world's own declaration rather than written twice, so a changed step cannot leave the
            /// engine simulating a different interval than the world commits (P-036, REF-A06).
            /// </summary>
            public double FixedStepSeconds =>
                (double)TraversalKeys.StepDurationTicks / TraversalRegistration.TicksPerSecond;

            /// <summary>
            /// The course's real local physics scene and its once-per-admitted-step gate, wrapped so the recovery
            /// runner can drive them without naming a traversal or adapter type. Null until the runtime is attached.
            /// </summary>
            public Gc027PhysicsDomain? PhysicsDomain => physicsDomain;

            /// <summary>
            /// The course's own second modifier mount — the headwind at the ridge — submitted to fault the source
            /// world after its first live write. It is a real publication the declared catalog accepts, so the
            /// injected fault fires inside a real apply rather than in validation (TEST-016 row 5, P-031).
            /// </summary>
            public CompositionEditPayload FaultEdit() => MountSecondProvider();

            /// <summary>The emitted catalog fingerprint as a value, so a scenario can never mistype it (P-028).</summary>
            public ContentHash CatalogHash()
            {
                if (!ContentHash.TryParseHex(CatalogFingerprint, out ContentHash parsed))
                {
                    throw new InvalidOperationException(
                        "the traversal family's catalog fingerprint literal is not 64 lowercase hex characters (P-028).");
                }

                return parsed;
            }

            /// <summary>The committed generated checkpoint codecs of this family's catalog (P-054).</summary>
            public CheckpointCodecSet Codecs
            {
                get
                {
                    if (checkpointCodecs == null)
                    {
                        if (!Gc018CheckpointCodecs.TryBuild(
                                out CheckpointSerializerBindings? _, out CheckpointCodecSet? built, out string detail)
                            || built == null)
                        {
                            throw new InvalidOperationException(
                                "the committed checkpoint catalog's serializers could not be bound: " + detail);
                        }

                        checkpointCodecs = built;
                    }

                    return checkpointCodecs;
                }
            }

            /// <summary>
            /// The directed migration graph this destination registers. The traversal catalog declares no schema step,
            /// so the registry is empty and a captured schema version without a path is refused by the planner rather
            /// than approximated (P-054).
            /// </summary>
            public CheckpointMigrationRegistry DirectMigrations =>
                recoveryMigrations ?? (recoveryMigrations = new CheckpointMigrationRegistry(new List<ISchemaMigrationStep>()));

            /// <summary>
            /// The schemas this destination can allocate at the version it carries: the checkpoint container, every
            /// recipe the family's catalog registers and the payload schema its declared wake names (P-054).
            /// </summary>
            public IReadOnlyList<SchemaRef> AllocatedSchemas
            {
                get
                {
                    if (allocatedSchemas != null)
                    {
                        return allocatedSchemas;
                    }

                    var schemas = new List<SchemaRef> { CheckpointFormat.DocumentSchema };
                    SpawnRecipeCatalog recipes = CreateRecipes();
                    for (int i = 0; i < recipes.Recipes.Count; i++)
                    {
                        AddDistinct(schemas, recipes.Recipes[i].Recipe.Schema);
                    }

                    AddDistinct(schemas, WakePayloadSchema);
                    allocatedSchemas = schemas;
                    return allocatedSchemas;
                }
            }

            /// <summary>
            /// Builds the recovery-facing physics domain over the course's real runtime: the authoritative pose is
            /// read from the world's own ECS storage (the quantity a checkpoint carries), the engine pose from the
            /// local scene, and the counters from the scene and its gate (04 s7, P-054).
            /// </summary>
            private Gc027PhysicsDomain BuildPhysicsDomain(Gc020StageRuntime runtime)
            {
                TraversalModule? module = runtime.Module;
                UnityPhysicsSceneBackend? physics = runtime.Physics;
                PhysicsAuthorityGate? gate = runtime.PhysicsGate;
                if (module == null || physics == null || gate == null)
                {
                    throw new InvalidOperationException(
                        "the traversal course attached without its module or its local physics scene, so its engine "
                        + "domain cannot be observed (04 s7).");
                }

                var bodies = new List<TargetId>();
                IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
                for (int i = 0; i < runners.Count; i++)
                {
                    bodies.Add(runners[i].Target);
                    if (physicsSubject == null)
                    {
                        physicsSubject = runners[i];
                    }
                }

                TraversalRunnerRef subject = physicsSubject
                    ?? throw new InvalidOperationException(
                        "the course owns no runner, so it declares no physical body to observe (07 s4.2).");

                return new Gc027PhysicsDomain(
                    "traversal-course",
                    physics.IsDedicatedLocalScene,
                    bodies,
                    target => AuthoritativePose(module, target),
                    target => EnginePose(gate, target),
                    target => DeclareBody(gate, target),
                    (step, seconds) => StepScene(gate, step, seconds),
                    () => physics.SimulateCount);
            }

            /// <summary>
            /// The world's own authoritative pose and velocity for one runner: the ECS components the integrate stage
            /// writes and a checkpoint serializes. This is the quantity a recovery restores; the engine's own pose is
            /// re-derived from it and never restored (P-054).
            /// </summary>
            private Gc027PhysicsPose AuthoritativePose(TraversalModule module, TargetId target)
            {
                if (!module.TryRunner(target, out Entity runner) || runner == Entity.Null)
                {
                    return default(Gc027PhysicsPose);
                }

                EntityManager entityManager = module.Host.EntityWorld.EntityManager;
                if (!entityManager.Exists(runner))
                {
                    return default(Gc027PhysicsPose);
                }

                TraversalVector3i position = entityManager.HasComponent<TraversalPose>(runner)
                    ? entityManager.GetComponentData<TraversalPose>(runner).Vector
                    : TraversalVector3i.Zero;
                TraversalVector3i velocity = entityManager.HasComponent<TraversalVelocity>(runner)
                    ? entityManager.GetComponentData<TraversalVelocity>(runner).Vector
                    : TraversalVector3i.Zero;
                return new Gc027PhysicsPose(
                    position.X, position.Y, position.Z, velocity.X, velocity.Y, velocity.Z);
            }

            /// <summary>The engine's own pose for one runner body, or the zero pose when the scene holds no body.</summary>
            private Gc027PhysicsPose EnginePose(PhysicsAuthorityGate gate, TargetId target)
            {
                var key = new PhysicsBodyKey(target, TraversalKeys.MotionDomain.Id.Value);
                if (!gate.TryReadPose(key, out PhysicsPose pose))
                {
                    return default(Gc027PhysicsPose);
                }

                return new Gc027PhysicsPose(
                    pose.Position.X, pose.Position.Y, pose.Position.Z,
                    pose.Velocity.X, pose.Velocity.Y, pose.Velocity.Z);
            }

            /// <summary>
            /// Declares one runner body at the world's authoritative pose. A recovered world runs this to RE-DERIVE
            /// the engine's state from the ECS state the checkpoint carried, which is why the engine's own simulation
            /// counter is a fresh count rather than a continuation (06 s7, P-054).
            /// </summary>
            private bool DeclareBody(PhysicsAuthorityGate gate, TargetId target)
            {
                Gc027PhysicsPose authoritative = AuthoritativePose(
                    attachedRuntime?.Module ?? throw new InvalidOperationException("no runtime is attached"),
                    target);
                var key = new PhysicsBodyKey(target, TraversalKeys.MotionDomain.Id.Value);
                var initial = new PhysicsPose(
                    new PhysicsVector3i(authoritative.PositionX, authoritative.PositionY, authoritative.PositionZ),
                    new PhysicsVector3i(authoritative.VelocityX, authoritative.VelocityY, authoritative.VelocityZ));
                return gate.TryDeclareBody(key, initial, out DiagnosticCode _, out string _)
                    == PhysicsBodyOutcome.Applied;
            }

            /// <summary>One admissible engine step reduced to the three outcomes the runner distinguishes (REF-A06).</summary>
            private PhysicsStepOutcome StepScene(PhysicsAuthorityGate gate, ulong admittedStep, double seconds)
            {
                int duplicatesBefore = gate.DuplicateStepRefusalCount;
                if (gate.TrySimulateExactlyOnce(admittedStep, seconds, out string _))
                {
                    return PhysicsStepOutcome.Stepped;
                }

                return gate.DuplicateStepRefusalCount > duplicatesBefore
                    ? PhysicsStepOutcome.DuplicateStepRefused
                    : PhysicsStepOutcome.Refused;
            }

            /// <summary>True: the traversal course installs a real local `PhysicsScene` (04 s7, REF-A05).</summary>
            public bool DeclaresEnginePhysicsDomain => true;

            /// <summary>
            /// One movement sample over the course's own declared route, which the runner submits before each pump so
            /// the integrate stage really advances the runners and the recovered world's motion is a value that moved
            /// (P-037, P-042).
            /// </summary>
            public CommandEnvelope? StepInput(WorldId world, OperationId operation) =>
                new CommandEnvelope(
                    operation,
                    MovementRoute,
                    VelocityAssertedTarget,
                    MovementSchema,
                    null,
                    MovementPayload(PendingInputHorizontalMilli, 0));

            /// <summary>
            /// The declared steps the source world admits before the capture: enough for the seeded velocity to
            /// advance the valley runner, so its recovered pose and velocity are not the seed values (07 s4.3).
            /// </summary>
            public uint AdmittedStepsBeforeFault => RecoveryAdmittedSteps;

            /// <summary>
            /// The course's authoritative state as canonical text: every runner's ECS pose and velocity — the
            /// quantity the integrate stage owns and a checkpoint serializes — plus its accepted-checkpoint progress.
            /// The recovery compares this text between the two worlds, so "authoritative motion survived" is a
            /// comparison of the worlds' own values (P-053, 07 s4.3).
            /// </summary>
            public string? AuthoritativeStateText(UnityWorldHost world)
            {
                if (world == null)
                {
                    throw new ArgumentNullException(nameof(world));
                }

                TraversalModule? module = attachedRuntime?.Module;
                if (module == null)
                {
                    // A world with no attached runtime has no motion state to read, which is a refusal the caller
                    // reports rather than an empty string it could mistake for agreement (P-052).
                    return null;
                }

                EntityManager entityManager = world.EntityWorld.EntityManager;
                var text = new System.Text.StringBuilder();
                IReadOnlyList<TraversalRunnerRef> runners = module.Runners();
                for (int i = 0; i < runners.Count; i++)
                {
                    TraversalRunnerRef runner = runners[i];
                    if (!entityManager.Exists(runner.Entity))
                    {
                        text.Append(runner.Target.ToString()).Append("=absent;");
                        continue;
                    }

                    TraversalVector3i pose = entityManager.HasComponent<TraversalPose>(runner.Entity)
                        ? entityManager.GetComponentData<TraversalPose>(runner.Entity).Vector
                        : TraversalVector3i.Zero;
                    TraversalVector3i velocity = entityManager.HasComponent<TraversalVelocity>(runner.Entity)
                        ? entityManager.GetComponentData<TraversalVelocity>(runner.Entity).Vector
                        : TraversalVector3i.Zero;
                    text.Append(runner.Target.ToString())
                        .Append("=p").Append(pose.X.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(',').Append(pose.Y.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(',').Append(pose.Z.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append("v").Append(velocity.X.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(',').Append(velocity.Y.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(',').Append(velocity.Z.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Append(';');

                    // The accepted-checkpoint progress is the course's own durable fact: a recovery that lost it would
                    // restart the run at the first volume while reporting unchanged motion (07 s4.2, P-032).
                    if (TraversalAccess.TryReadProgress(
                            entityManager, module.CourseEntity, runner.Target, out TraversalProgressRow progress))
                    {
                        text.Append("progress=")
                            .Append(progress.Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
                            .Append('/').Append(progress.LastCheckpoint.ToString())
                            .Append(';');
                    }
                }

                return text.ToString();
            }

            /// <summary>True when the recovery-facing physics domain has been built for this world.</summary>
            public bool PhysicsDomainAttached => physicsDomain != null;

            /// <summary>
            /// Remembers the descriptor a recovery attach must pass to <see cref="AttachStageRuntime"/>. The runner
            /// compiles the pipeline once and hands it here, so the attach uses the same compiled descriptor the
            /// world's dispatch tables were installed from rather than recompiling one that could differ (GC-009).
            /// </summary>
            public void NotePipelineForAttach(PipelineDescriptorReport descriptor) => descriptorForAttach = descriptor;

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
        }

        /// <summary>
        /// The traversal course family a GC-027 run drives, over the fixture catalog this revision ships. There is no
        /// committed generated traversal catalog (its emission is GC-025's catalog work), so this is the one catalog
        /// and the run says so rather than implying a second one ran (P-060).
        /// </summary>
        public static IGc027Family RecoveryFamily()
        {
            CatalogBuildResult build = TraversalCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written traversal catalog was rejected: " + build.Describe());
            }

            return new CourseFamily(
                build.Catalog,
                Declarations(),
                TraversalCatalogTable.Fingerprint().ToHex());
        }
    }
}
