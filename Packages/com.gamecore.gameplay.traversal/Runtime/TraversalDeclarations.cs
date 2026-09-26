// GameCore.Gameplay.Traversal — the course plugin's generated-style declarations (GC-020).
//
// Normative sources: 07 s4.2's five-stage graph (`traversal.input -> traversal.integrate -> traversal.sense ->
// traversal.checkpoints -> traversal.output`, plus `traversal.integrate -> traversal.output`), 07 s4.1's Additive
// `traversal.acceleration` capability with its two modifiers, P-034 (one owner per authoritative domain),
// P-039/P-040 (declared stages, system keys, access sets and the compiled DAG), P-043 (a declared buffer names its
// producers, its single consuming owner, its order key, its lifetime and its bounded capacity) and 04 s8
// (registration is data: every declaration below is a direct typed reference).
//
// The shape mirrors `CardTableDeclarations`: real `PluginManifest` sets that `OwnershipSchedulePipeline.Build`
// validates with the production validators and compiles with the production compiler. Nothing here re-implements a
// kernel module.
//
// THE INTEGRATE EDGE. `traversal.integrate` writes the motion domain and `traversal.output` reads it, so the second
// DAG edge of 07 s4.2 is declared as a real `requiredAfter` stage edge rather than left to the scheduler to infer
// (P-040: "the scheduler MUST NOT invent gameplay order from package load order").
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Rules.Traversal;

namespace GameCore.Gameplay.Traversal
{
    /// <summary>
    /// The five-stage traversal plan, its five systems, its one declared step buffer and its six owned domains.
    /// </summary>
    public static class TraversalDeclarations
    {
        /// <summary>Owner package identity of every traversal declaration; the traversal gameplay package.</summary>
        public static readonly Id128 OwnerPackage = TraversalIdentity.Id("traversal.package.gameplay");

        /// <summary>
        /// The course runtime's declaration: the five-stage plan, its six domains and its declared observation buffer.
        /// </summary>
        public static PluginManifest CourseRuntime(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                null,
                null,
                new List<CapabilityContract>(),
                new List<DerivationRule>(),
                Slots(),
                Stages(),
                new List<BufferSpec> { ObservationStepBuffer() });
        }

        /// <summary>
        /// One acceleration modifier's declaration: one `traversal.acceleration` capability contract and one
        /// `Additive` rule whose selector is the runner recipe. This is the inherited rule modifier contribution:
        /// mounting it makes every compatible existing runner's next integrated step apply the extra acceleration,
        /// and a runner created later inherits the same contribution automatically (P-013, P-015, P-024).
        /// </summary>
        public static PluginManifest AccelerationModifier(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            string ruleStableName,
            TraversalVector3i acceleration)
        {
            return Manifest(
                pluginType,
                factoryKey,
                configSchema,
                null,
                null,
                new List<CapabilityContract> { AccelerationContract() },
                new List<DerivationRule> { AccelerationRule(ruleStableName, acceleration) },
                null,
                null,
                null);
        }

        /// <summary>
        /// The `traversal.acceleration` capability contract: one output slot, `Additive`, and the registered
        /// int32 sum reducer of the traversal rules package (P-017, P-019). The slot id is derived
        /// exactly as a contract declaration derives it (`&lt;capability&gt;.slot-0`), which is the identity
        /// `TraversalVocabulary` publishes.
        /// </summary>
        public static CapabilityContract AccelerationContract()
        {
            return new CapabilityContract(
                TraversalVocabulary.AccelerationContract,
                TraversalVocabulary.AccelerationStratum,
                new List<OutputSlotSchema>
                {
                    new OutputSlotSchema(
                        TraversalVocabulary.AccelerationSlot,
                        TraversalVocabulary.EffectiveAccelerationSchemaRef),
                },
                new List<SlotCompositionPolicy>
                {
                    // The reducer is the version carrier of the fold (05 s3): key `traversal.reducer.vec3i-sum`, v1.
                    new SlotCompositionPolicy(
                        TraversalVocabulary.AccelerationSlot,
                        CompositionPolicy.Additive,
                        TraversalVocabulary.AccelerationReducerKey),
                },
                null);
        }

        /// <summary>
        /// One modifier's `traversal.acceleration` rule. The payload is exactly one fixed-width big-endian int32
        /// (05 s6), the same encoding `TraversalPayloadCodec` writes and the integration reads, so the derived value a
        /// binding row carries and the value the integrator applies cannot disagree. The declared slot value is the
        /// additional acceleration along X, which is why 07 s4.1's two modifiers vary only that component.
        /// </summary>
        public static DerivationRule AccelerationRule(string ruleStableName, TraversalVector3i acceleration)
        {
            return new DerivationRule(
                TraversalIdentity.Rule(ruleStableName),
                TraversalVocabulary.AccelerationContract,
                TraversalVocabulary.AccelerationStratum,
                1U,
                new List<SchemaRef> { TraversalVocabulary.SelectorSchema(TraversalVocabulary.RunnerRecipe) },
                TraversalVocabulary.AlwaysPredicateKey,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Additive,
                TraversalPayloadCodec.WriteAcceleration(acceleration.X));
        }

        /// <summary>
        /// The declared stages, in dispatch order, each naming the stage it must follow. The chain is declared rather
        /// than inferred because P-040 forbids the scheduler from inventing gameplay order: motion integrates before
        /// anything reads the pose it produced, and the compiled DAG must contain those edges as declared ones.
        /// </summary>
        public static IReadOnlyList<StageSpec> Stages()
        {
            // 1. `traversal.input`: it decodes the admitted movement envelopes into the owner's per-runner captured
            //    input. It writes no pose, which is why its write claim is only the input domain.
            var input = Stage(
                TraversalKeys.InputStage,
                "traversal.stage.input",
                null,
                new AccessSet(new[]
                {
                    new AccessDeclaration(TraversalKeys.InputDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        TraversalKeys.InputSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(TraversalKeys.InputDomain, AccessMode.ReadWrite, default(Id128)),
                        })),
                });

            // 2. `traversal.integrate`: the sole writer of the motion domain (07 s4.2). It reads the captured input
            //    and the effective derived acceleration, and it writes pose, velocity and jump state directly for its
            var integrate = Stage(
                TraversalKeys.IntegrateStage,
                "traversal.stage.integrate",
                new List<StageId> { TraversalKeys.InputStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(TraversalKeys.MotionDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        TraversalKeys.IntegrateSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(TraversalKeys.MotionDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(TraversalKeys.InputDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            // 3. `traversal.sense`: it waits for the pose it observes and seals spatial observations. It writes only
            //    its own observation domain, so the sensor can never mutate progress (07 s4.2).
            var sense = Stage(
                TraversalKeys.SenseStage,
                "traversal.stage.sense",
                new List<StageId> { TraversalKeys.IntegrateStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(TraversalKeys.ObservationDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        TraversalKeys.SenseSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(TraversalKeys.ObservationDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(TraversalKeys.MotionDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            // 4. `traversal.checkpoints`: the sole writer of run progress and of the committed crossing output. It
            //    rejects duplicate and out-of-order observations and emits one award per accepted crossing (07 s4.2).
            var checkpoints = Stage(
                TraversalKeys.CheckpointStage,
                "traversal.stage.checkpoints",
                new List<StageId> { TraversalKeys.SenseStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(TraversalKeys.ProgressDomain, AccessMode.ReadWrite, default(Id128)),
                    new AccessDeclaration(TraversalKeys.CrossingDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        TraversalKeys.CheckpointSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(TraversalKeys.ProgressDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(TraversalKeys.CrossingDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(TraversalKeys.ObservationDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            // 5. `traversal.output`: it reads the integrated motion and the committed crossings and prepares the
            //    coherent snapshot of the step. It writes only its own snapshot domain, so no prediction of motion or
            //    progress becomes authority (07 s4.2).
            var output = Stage(
                TraversalKeys.OutputStage,
                "traversal.stage.output",
                new List<StageId> { TraversalKeys.CheckpointStage, TraversalKeys.IntegrateStage },
                new AccessSet(new[]
                {
                    new AccessDeclaration(TraversalKeys.SnapshotDomain, AccessMode.ReadWrite, default(Id128)),
                }),
                new List<SystemSpec>
                {
                    System(
                        TraversalKeys.OutputSystem,
                        new AccessSet(new[]
                        {
                            new AccessDeclaration(TraversalKeys.SnapshotDomain, AccessMode.ReadWrite, default(Id128)),
                            new AccessDeclaration(TraversalKeys.MotionDomain, AccessMode.Read, default(Id128)),
                            new AccessDeclaration(TraversalKeys.CrossingDomain, AccessMode.Read, default(Id128)),
                        })),
                });

            return new List<StageSpec> { input, integrate, sense, checkpoints, output };
        }

        /// <summary>
        /// The declared step buffer between `traversal.sense` and `traversal.checkpoints` (07 s4.2, P-043): the
        /// sensor produces the sealed observations and the checkpoint owner is their single consumer, so the schedule
        /// carries a real producer-before-consumer edge and a deferred playback point.
        /// </summary>
        public static BufferSpec ObservationStepBuffer()
        {
            return new BufferSpec(
                TraversalKeys.ObservationBuffer,
                TraversalKeys.ObservationDomain,
                new List<FactoryKey> { TraversalKeys.SenseSystem },
                TraversalKeys.SenseStage,
                TraversalKeys.CheckpointStage,
                TraversalKeys.ObservationOrderKey,
                BufferLifetime.Step,
                TraversalKeys.ObservationCapacity,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);
        }

        /// <summary>
        /// The six owned domains, one owner each (P-034). Motion belongs to the runtime, the captured input to
        /// the input adapter, the sealed observations to the sensor adapter and progress, crossings and the committed
        /// image to the checkpoint runtime. No two owners write one domain, and each last-support disposition is
        /// declared: motion, progress and the committed image are durable gameplay state and stay dormant; the
        /// step-scoped input and observation rows are derived data and are removed.
        /// </summary>
        public static IReadOnlyList<StateSlotSpec> Slots()
        {
            return new List<StateSlotSpec>
            {
                // Durable: a runner's pose, velocity and jump state are the run's own history (07 s4.3: unmounting a
                // modifier leaves "velocity, pose, and committed progress" in place).
                Slot(
                    TraversalKeys.MotionSlot,
                    TraversalKeys.MotionOwner,
                    TraversalKeys.MotionDomain,
                    TraversalKeys.MotionLayout,
                    new[]
                    {
                        TraversalKeys.PoseField,
                        TraversalKeys.VelocityField,
                        TraversalKeys.JumpField,
                    },
                    LastSupportPolicy.PreserveDormant),
                // Step-scoped derived data: a captured input is only meaningful for the step it was sealed in.
                Slot(
                    TraversalKeys.InputSlot,
                    TraversalKeys.InputOwner,
                    TraversalKeys.InputDomain,
                    TraversalKeys.InputLayout,
                    new[] { TraversalKeys.MovementInputField },
                    LastSupportPolicy.RemoveDerived),
                // Step-scoped derived data: sealed observations are consumed by the same step's commit or dropped.
                Slot(
                    TraversalKeys.ObservationSlot,
                    TraversalKeys.SensorOwner,
                    TraversalKeys.ObservationDomain,
                    TraversalKeys.ObservationLayout,
                    new[] { TraversalKeys.ObservationRowsField },
                    LastSupportPolicy.RemoveDerived),
                // Durable: committed progress is the run's own history and survives a modifier's retraction.
                Slot(
                    TraversalKeys.ProgressSlot,
                    TraversalKeys.CheckpointOwner,
                    TraversalKeys.ProgressDomain,
                    TraversalKeys.ProgressLayout,
                    new[] { TraversalKeys.ProgressRowsField },
                    LastSupportPolicy.PreserveDormant),
                // Durable committed output: already-awarded crossings are not undone by a later retraction (P-003).
                Slot(
                    TraversalKeys.CrossingSlot,
                    TraversalKeys.CheckpointOwner,
                    TraversalKeys.CrossingDomain,
                    TraversalKeys.CrossingLayout,
                    new[] { TraversalKeys.CrossingRowsField },
                    LastSupportPolicy.PreserveDormant),
                // The committed image is recomputed every step, so losing its support removes a derived row rather
                // than preserving a stale claim about the current step.
                Slot(
                    TraversalKeys.SnapshotSlot,
                    TraversalKeys.CheckpointOwner,
                    TraversalKeys.SnapshotDomain,
                    TraversalKeys.SnapshotLayout,
                    new[] { TraversalKeys.SnapshotField },
                    LastSupportPolicy.RemoveDerived),
                // Portable checkpoint projection of the traversal runtime's authoritative pose/velocity and
                // progress. The physical ECS components remain the authority; each int32 row is explicitly
                // declared so a later composition publication can preserve it rather than rejecting unknown
                // state (P-032, P-053). No second physical field writer is introduced.
                CheckpointSlot("traversal.slot.motion.position-x"),
                CheckpointSlot("traversal.slot.motion.position-y"),
                CheckpointSlot("traversal.slot.motion.position-z"),
                CheckpointSlot("traversal.slot.motion.velocity-x"),
                CheckpointSlot("traversal.slot.motion.velocity-y"),
                CheckpointSlot("traversal.slot.motion.velocity-z"),
                CheckpointSlot("traversal.slot.motion.grounded"),
                CheckpointSlot("traversal.slot.progress.present"),
                CheckpointSlot("traversal.slot.progress.count"),
                CheckpointSlot("traversal.slot.progress.started"),
                CheckpointSlot("traversal.slot.progress.checkpoint-low-0"),
                CheckpointSlot("traversal.slot.progress.checkpoint-low-1"),
                CheckpointSlot("traversal.slot.progress.checkpoint-high-0"),
                CheckpointSlot("traversal.slot.progress.checkpoint-high-1"),
                CheckpointSlot("traversal.slot.progress.crossing-sequence"),
                CheckpointSlot("traversal.slot.progress.crossing-step-0"),
                CheckpointSlot("traversal.slot.progress.crossing-step-1"),
            };
        }

        private static StateSlotSpec CheckpointSlot(string stableName) => Slot(
            TraversalIdentity.Slot(stableName), TraversalKeys.MotionOwner, TraversalKeys.MotionDomain,
            default(FactoryKey), Array.Empty<FactoryKey>(), LastSupportPolicy.PreserveDormant);

        /// <summary>One declared stage with its single system and its declared predecessors.</summary>
        private static StageSpec Stage(
            StageId stage,
            string stageStableName,
            IReadOnlyList<StageId>? requiredAfter,
            AccessSet readWriteSet,
            IReadOnlyList<SystemSpec> systems)
        {
            return new StageSpec(
                stage,
                1U,
                OwnerPackage,
                HostAffinity.ManagedMain,
                new List<FactoryKey> { TraversalIdentity.Key(stageStableName) },
                null,
                readWriteSet,
                null,
                requiredAfter,
                null,
                null,
                systems,
                null);
        }

        /// <summary>One system entry with its declared access set (never empty: undeclared access rejects, P-039).</summary>
        private static SystemSpec System(FactoryKey key, AccessSet access)
        {
            return new SystemSpec(key, SystemMultiplicity.World, access, null, null, null, null);
        }

        /// <summary>One owned state slot: owner, domain, layout, physical fields and its last-support disposition.</summary>
        private static StateSlotSpec Slot(
            SlotId slot,
            OwnerId owner,
            SchemaRef domain,
            FactoryKey layout,
            IReadOnlyList<FactoryKey> fields,
            LastSupportPolicy lastSupport)
        {
            var ownership = new List<FieldOwnership>(fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                ownership.Add(new FieldOwnership(domain, fields[i].RegistrationKey));
            }

            return new StateSlotSpec(
                slot,
                owner,
                domain,
                layout,
                ownership,
                default(FactoryKey),
                default(FactoryKey),
                default(FactoryKey),
                lastSupport,
                default(FactoryKey),
                null);
        }

        /// <summary>One plugin manifest with the traversal package's declared protocol range.</summary>
        private static PluginManifest Manifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            IReadOnlyList<ServiceExport>? serviceExports,
            IReadOnlyList<ServiceDependency>? serviceDependencies,
            IReadOnlyList<CapabilityContract>? contracts,
            IReadOnlyList<DerivationRule>? rules,
            IReadOnlyList<StateSlotSpec>? slots,
            IReadOnlyList<StageSpec>? stages,
            IReadOnlyList<BufferSpec>? buffers)
        {
            return new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                serviceExports,
                serviceDependencies,
                contracts,
                rules,
                null,
                slots,
                stages,
                buffers,
                null);
        }
    }
}
