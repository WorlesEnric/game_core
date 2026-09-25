// GameCore.Unity.Fixtures — W2 integration gate: the world the whole chain runs in.
//
// Four stages, their systems, the gate's native dependency container and its registration. The step table this
// world starts with is the one GC-009's compiler produced and GC-009's adapter turned into a dispatch plan; GC-008's
// publisher rebinds the group at every publication from the same compiled order, so the executed order is the
// compiled order at every epoch (04 section 4, P-040).
//
// The systems are ordinary gameplay-side systems: an owner that drains its bounded lane and commits, a producer
// that schedules a real job into a native container and publishes its handle only through the native dependency
// table, two per-partition writers of one domain, and a dependent reader that must wait for the producer's fence
// (P-034, P-041, P-042).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Planning;
using CompiledSchedule = GameCore.Planning.Scheduling.CompiledSchedule;
using GameCore.Derivation.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Fixtures
{
    /// <summary>
    /// The settle stage's real job. It writes only a native container, so the ECS safety system cannot complete it
    /// implicitly and the handle must travel through the explicit dependency table (04 section 4, P-041).
    /// </summary>
    public struct W2GateSettleJob : IJob
    {
        public NativeArray<int> Output;

        public int Value;

        public void Execute()
        {
            if (Output.IsCreated && Output.Length > 0)
            {
                Output[0] = Value;
            }
        }
    }

    /// <summary>
    /// Fixture-side state of one gate world: the compiled schedule it runs, the adapter result that installed it,
    /// the native dependency table its producer publishes through, and the entities its systems share.
    /// </summary>
    public sealed class W2GateModule : IDisposable
    {
        private static readonly List<W2GateModule> modules = new List<W2GateModule>();

        private readonly UnityWorldHost host;
        private readonly Dictionary<Id128, Entity> targetEntities = new Dictionary<Id128, Entity>();
        private bool disposed;

        private W2GateModule(UnityWorldHost host, CompiledSchedule schedule, ScheduleAdaptation adaptation)
        {
            this.host = host;
            Schedule = schedule;
            Adaptation = adaptation;
            Native = adaptation.NativeTable ?? new NativeDependencyTable(1);
            ResultSlot = adaptation.Buffers.Count > 0 ? adaptation.Buffers[0].Slot : 0;
            Container = new NativeArray<int>(1, Allocator.Persistent);

            EntityManager entityManager = host.EntityWorld.EntityManager;
            RootEntity = entityManager.CreateEntity();
            entityManager.AddComponentData(RootEntity, new W2GateTrail());
            entityManager.AddComponentData(RootEntity, new W2GateTrait());
            entityManager.SetName(RootEntity, "W2GateRoot");
        }

        public UnityWorldHost Host => host;

        /// <summary>The compiled schedule this world's dispatch table was installed from (GC-009's own output).</summary>
        public CompiledSchedule Schedule { get; }

        /// <summary>The adapter result that produced the installed plan and the native resource slots.</summary>
        public ScheduleAdaptation Adaptation { get; }

        /// <summary>Native dependency table the settle stage publishes its handle through (P-041).</summary>
        public NativeDependencyTable Native { get; }

        /// <summary>Native resource slot of the declared buffer whose producer fence a reader must combine.</summary>
        public int ResultSlot { get; }

        /// <summary>The producer's non-component container; only a completed fence makes reading it safe.</summary>
        public NativeArray<int> Container { get; }

        /// <summary>The one gate root entity carrying the trail and the trait components.</summary>
        public Entity RootEntity { get; }

        public int CommandStageIndex { get; private set; } = -1;

        public int SettleStageIndex { get; private set; } = -1;

        public int ClaimStageIndex { get; private set; } = -1;

        public int ProjectStageIndex { get; private set; } = -1;

        /// <summary>Steps this module's producer scheduled a job for; the fixture's own production evidence.</summary>
        public int SettleJobCount { get; internal set; }

        /// <summary>Time driver adopted by this gate world (cutoff, plugin clocks and the wrapped pump).</summary>
        public WorldTimeDriver? Time { get; internal set; }

        /// <summary>Targets this world's composition index registered, so the owner can resolve a message's target.</summary>
        public int MappedTargetCount => targetEntities.Count;

        public static W2GateModule Attach(UnityWorldHost host, CompiledSchedule schedule, ScheduleAdaptation adaptation)
        {
            var module = new W2GateModule(host, schedule, adaptation);
            module.ResolveStageIndexes();
            modules.Add(module);
            return module;
        }

        public static bool TryGet(World world, out W2GateModule? module)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].host.EntityWorld == world)
                {
                    module = modules[i];
                    return true;
                }
            }

            module = null;
            return false;
        }

        public static void DetachAll()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Dispose();
            }

            modules.Clear();
        }

        /// <summary>
        /// Records the entity that carries one live target's state. The scenario calls it when it seeds a target, so
        /// the owner stage resolves a message's stable target id without reading a second authority (P-004).
        /// </summary>
        public void MapTarget(TargetId target, Entity entity) => targetEntities[target.Value] = entity;

        /// <summary>Resolves one stable target id to the entity that carries its state; a miss is a refusal.</summary>
        public bool TryEntity(TargetId target, out Entity entity) => targetEntities.TryGetValue(target.Value, out entity);

        /// <summary>Reads the gate root's trail component, or false when the root is gone.</summary>
        public bool TryReadTrail(out W2GateTrail trail)
        {
            EntityManager entityManager = host.EntityWorld.EntityManager;
            if (!host.EntityWorld.IsCreated || !entityManager.Exists(RootEntity))
            {
                trail = default(W2GateTrail);
                return false;
            }

            trail = entityManager.GetComponentData<W2GateTrail>(RootEntity);
            return true;
        }

        /// <summary>Reads the gate root's trait component, or false when the root is gone.</summary>
        public bool TryReadTrait(out W2GateTrait trait)
        {
            EntityManager entityManager = host.EntityWorld.EntityManager;
            if (!host.EntityWorld.IsCreated || !entityManager.Exists(RootEntity))
            {
                trait = default(W2GateTrait);
                return false;
            }

            trait = entityManager.GetComponentData<W2GateTrait>(RootEntity);
            return true;
        }

        /// <summary>Fence slot of one compiled stage in the dispatcher's own table; default when none recorded it.</summary>
        public JobHandle FenceSlotOf(int stageIndex)
            => host.StepGroup.Fences == null
                ? default(JobHandle)
                : host.StepGroup.Fences.CombineIncoming(new[] { stageIndex });

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            // A container must never be freed while a scheduled writer still targets it: settle the writer first,
            // and only then release the storage (P-047, P-048). A failed completion is reported, never thrown from
            // a teardown path.
            Native.CompleteStepFence();

            if (Container.IsCreated)
            {
                Container.Dispose();
            }

            Native.Dispose();
            modules.Remove(this);
        }

        private void ResolveStageIndexes()
        {
            if (Schedule.TryGetStageIndex(W2GateKeys.CommandStage, out int command))
            {
                CommandStageIndex = command;
            }

            if (Schedule.TryGetStageIndex(W2GateKeys.SettleStage, out int settle))
            {
                SettleStageIndex = settle;
            }

            if (Schedule.TryGetStageIndex(W2GateKeys.ClaimStage, out int claim))
            {
                ClaimStageIndex = claim;
            }

            if (Schedule.TryGetStageIndex(W2GateKeys.ProjectStage, out int project))
            {
                ProjectStageIndex = project;
            }
        }
    }

    /// <summary>
    /// Owner stage of domain A: it drains its bounded lane batch, decodes each payload with the generated reader,
    /// writes the authoritative state of the addressed target and commits the request with its result event
    /// (P-034, P-042, P-044). A request it cannot serve is rejected observably, never dropped.
    /// </summary>
    [DisableAutoCreation]
    public partial class W2GateCommandSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!W2GateModule.TryGet(World, out W2GateModule? module) || module == null)
            {
                return;
            }

            WorldMessagePlane? plane = module.Host.Messages;
            if (plane == null)
            {
                return;
            }

            IReadOnlyList<StepMessage> batch = plane.DrainOwnerBatch(W2GateKeys.QuestOwner);
            if (batch.Count == 0)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            for (int i = 0; i < batch.Count; i++)
            {
                StepMessage message = batch[i];
                byte[] payload = plane.PayloadOf(message);

                // A generated reader, never reflection and never a guessed default (04 section 8, P-042).
                if (plane.Readers.TryRead<int>(message.PayloadSchema, payload, out int value, out string _)
                    != PayloadDecodeOutcome.Decoded)
                {
                    plane.Reject(message, DiagnosticCode.UnsupportedVersion, plane.ExecutingStep);
                    continue;
                }

                if (!module.TryEntity(message.Target, out Entity entity) || !entityManager.Exists(entity))
                {
                    // The request names no live target of this world: the rejection is an observable result (P-037).
                    plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                    continue;
                }

                // One authoritative store per domain (P-032): the owner advances the quest slot's value in the
                // target's own state storage, and the committed event carries exactly that value.
                if (!entityManager.HasBuffer<TargetSlotState>(entity))
                {
                    plane.Reject(message, DiagnosticCode.MissingDependency, plane.ExecutingStep);
                    continue;
                }

                DynamicBuffer<TargetSlotState> slots = entityManager.GetBuffer<TargetSlotState>(entity);
                if (!AssemblyStorage.TryFindSlot(slots, W2GateKeys.QuestOwner, W2GateKeys.QuestSlot, out int row))
                {
                    plane.Reject(message, DiagnosticCode.StaleHandle, plane.ExecutingStep);
                    continue;
                }

                TargetSlotState state = slots[row];
                state.Value += value;
                state.Active = 1;
                slots[row] = state;

                // The committed event carries the authoritative value the step produced, so an observer's page and
                // the live storage can be compared field by field (P-044, P-045).
                FrozenPayload committed = IntegrationSlotValues.WriteInt32(state.Value);
                if (!plane.Commit(message, W2GateKeys.ResultSchema, committed, plane.ExecutingStep, out string _))
                {
                    plane.Reject(message, DiagnosticCode.BudgetExceeded, plane.ExecutingStep);
                }
            }

            plane.ReleaseConsumed(W2GateKeys.QuestOwner);
        }
    }

    /// <summary>
    /// Settle stage of domain C. It advances the trail's step count and schedules a real job whose output goes to a
    /// native container. It deliberately does <b>not</b> write the handle back into <c>Dependency</c>: its only
    /// carrier is the native dependency table, so the later stage's wait is load-bearing rather than incidental
    /// (04 section 4, P-041).
    /// </summary>
    [DisableAutoCreation]
    public partial class W2GateSettleSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!W2GateModule.TryGet(World, out W2GateModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (entityManager.Exists(module.RootEntity))
            {
                W2GateTrail trail = entityManager.GetComponentData<W2GateTrail>(module.RootEntity);
                trail.Steps += 1;
                entityManager.SetComponentData(module.RootEntity, trail);
            }

            module.SettleJobCount++;
            JobHandle handle = new W2GateSettleJob
            {
                Output = module.Container,
                Value = module.SettleJobCount,
            }.Schedule(default(JobHandle));

            module.Native.Store(
                module.ResultSlot,
                handle,
                module.SettleStageIndex,
                W2GateKeys.SettleStage,
                W2GateKeys.SettleSystem,
                module.Host.CurrentEpoch,
                module.Host.CurrentStep,
                module.Host.Driver,
                module.Host.StepGroup.Fences);
        }
    }

    /// <summary>
    /// First per-partition writer of domain B: it owns one generated partition of the trait rows and writes only its
    /// own field (P-034, P-040).
    /// </summary>
    [DisableAutoCreation]
    public partial class W2GateClaimLeftSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!W2GateModule.TryGet(World, out W2GateModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.RootEntity))
            {
                return;
            }

            W2GateTrait trait = entityManager.GetComponentData<W2GateTrait>(module.RootEntity);
            trait.LeftValue += 1;
            entityManager.SetComponentData(module.RootEntity, trait);
        }
    }

    /// <summary>Second per-partition writer of domain B: the mirror image on its own field.</summary>
    [DisableAutoCreation]
    public partial class W2GateClaimRightSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!W2GateModule.TryGet(World, out W2GateModule? module) || module == null)
            {
                return;
            }

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.RootEntity))
            {
                return;
            }

            W2GateTrait trait = entityManager.GetComponentData<W2GateTrait>(module.RootEntity);
            trait.RightValue += 1;
            entityManager.SetComponentData(module.RootEntity, trait);
        }
    }

    /// <summary>
    /// Project stage of domain C: it waits for the settle stage's native producer fence, reads the value that job
    /// wrote, and records both facts. The wait is what 04 section 4 requires of a dependent read of a
    /// non-component resource (P-041).
    /// </summary>
    [DisableAutoCreation]
    public partial class W2GateProjectSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!W2GateModule.TryGet(World, out W2GateModule? module) || module == null)
            {
                return;
            }

            JobHandle producer = module.Native.CombineIncoming(module.ResultSlot);
            JobHandle.CombineDependencies(Dependency, producer).Complete();

            int observed = module.Container.IsCreated && module.Container.Length > 0 ? module.Container[0] : 0;

            EntityManager entityManager = EntityManager;
            if (!entityManager.Exists(module.RootEntity))
            {
                return;
            }

            W2GateTrail trail = entityManager.GetComponentData<W2GateTrail>(module.RootEntity);
            trail.ProjectedValue = observed;
            trail.WaitedOnNativeFence = producer.Equals(default(JobHandle)) ? (byte)0 : (byte)1;
            entityManager.SetComponentData(module.RootEntity, trail);
        }
    }

    /// <summary>The gate's precompiled spawn recipes and their direct typed appliers (P-024, 04 section 6).</summary>
    public static class W2GateRecipes
    {
        /// <summary>Base progress a gate recipe installs before its derived rows are published.</summary>
        public const int BaseProgress = 0;

        public static SpawnRecipe Villager(W2GateRecipeApplier applier)
            => Recipe(W2GateKeys.VillagerRecipe, W2GateKeys.VillagerRecipeSchema, applier);

        public static SpawnRecipe QuestGate(W2GateRecipeApplier applier)
            => Recipe(W2GateKeys.QuestGateRecipe, W2GateKeys.QuestGateRecipeSchema, applier);

        public static SpawnRecipe DecorativeCrowd(W2GateRecipeApplier applier)
            => Recipe(W2GateKeys.DecorativeCrowdRecipe, W2GateKeys.DecorativeCrowdRecipeSchema, applier);

        public static SpawnRecipe QuestEncounter(W2GateRecipeApplier applier)
            => Recipe(W2GateKeys.QuestEncounterRecipe, W2GateKeys.QuestEncounterRecipeSchema, applier);

        /// <summary>The forward provider's recipe: registered, but no live target uses it (see the handoff).</summary>
        public static SpawnRecipe Forward(W2GateRecipeApplier applier)
            => Recipe(W2GateKeys.ForwardRecipe, W2GateKeys.ForwardRecipeSchema, applier);

        /// <summary>The world's closed recipe catalog over one applier instance (P-015, 04 section 6).</summary>
        public static SpawnRecipeCatalog Catalog(W2GateRecipeApplier applier)
        {
            return new SpawnRecipeCatalog(new List<SpawnRecipe>
            {
                Villager(applier),
                QuestGate(applier),
                DecorativeCrowd(applier),
                QuestEncounter(applier),
                Forward(applier),
            });
        }

        private static SpawnRecipe Recipe(DefinitionRef recipe, string schemaName, ISpawnApplier applier)
        {
            var descriptor = new TargetDescriptor(
                recipe,
                new List<SchemaRef> { FixtureIds.SchemaRef(schemaName, 1U) },
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(
                recipe,
                descriptor,
                new List<SchemaRef> { FixtureIds.SchemaRef(schemaName, 1U) },
                applier);
        }
    }

    /// <summary>Generated-style system registrations, message plane and creation requests of one gate world.</summary>
    public static class W2GateRegistration
    {
        /// <summary>World name; the host appends the session id, so every world name is an inspectable incarnation.</summary>
        public const string WorldName = "GameCoreW2GateWorld";

        public static readonly WorldDefinitionId GateWorldDefinition =
            new WorldDefinitionId(new Id128(W2GateKeys.Namespace, 0x4001UL));

        /// <summary>The gate's command lane: reliable, one step, one owner and one consuming stage (P-043).</summary>
        public static MessagePlaneRegistration Messages()
        {
            var route = new CommandRoute(
                W2GateKeys.CommandRoute,
                W2GateKeys.QuestOwner,
                W2GateKeys.CommandSchema,
                W2GateKeys.CommandStage,
                W2GateKeys.CommandStage,
                W2GateKeys.CommandBuffer,
                W2GateKeys.HostIngressProducer,
                4,
                false);

            var buffer = new MessageBufferDescriptor(
                W2GateKeys.CommandBuffer,
                W2GateKeys.CommandSchema,
                new[] { W2GateKeys.HostIngressProducer },
                W2GateKeys.QuestOwner,
                W2GateKeys.CommandStage,
                W2GateKeys.CommandStage,
                W2GateKeys.CommandOrderKey,
                BufferLifetime.Step,
                4,
                64,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            return new MessagePlaneRegistration(
                new List<CommandRoute> { route },
                new List<MessageBufferDescriptor> { buffer },
                null,
                maxPendingRequests: 8,
                maxRetainedResults: 8,
                maxRetainedEvents: 8,
                maxEventsPerStep: 4,
                nextStepCapacity: 2);
        }

        /// <summary>The generated typed reader of the gate plane (04 section 8).</summary>
        public static CommandPayloadReaders Readers()
        {
            var readers = new CommandPayloadReaders();
            if (!readers.TryBind(new W2GatePayloadReader(), out string failure))
            {
                throw new InvalidOperationException("the gate reader registration failed: " + failure);
            }

            return readers;
        }

        /// <summary>
        /// The world's composition root: the compiled schedule the adapter installed becomes its initial step plan,
        /// and GC-008's publisher rebinds the group from the same compiled order at every publication.
        /// </summary>
        public static UnityWorldRegistration Create(
            ScheduleAdaptation adaptation,
            IReadOnlyList<SystemRegistration> systems)
        {
            if (adaptation == null)
            {
                throw new ArgumentNullException(nameof(adaptation));
            }

            if (!adaptation.Succeeded || adaptation.StepPlan == null)
            {
                throw new ArgumentException(
                    "a rejected schedule adaptation has no dispatch table to register: " + adaptation.Explain(),
                    nameof(adaptation));
            }

            return new UnityWorldRegistration(
                WorldName,
                adaptation.Stages,
                systems,
                GuardedDispatchPlan.Empty,
                adaptation.StepPlan,
                GuardedDispatchPlan.Empty,
                null,
                Messages(),
                Readers());
        }

        /// <summary>The gate's four stages: one system each, plus the second per-partition writer of domain B.</summary>
        public static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<W2GateCommandSystem>(W2GateKeys.CommandSystem, W2GateKeys.CommandStage, "W2GateCommandSystem"),
                new ManagedSystemRegistration<W2GateSettleSystem>(W2GateKeys.SettleSystem, W2GateKeys.SettleStage, "W2GateSettleSystem"),
                new ManagedSystemRegistration<W2GateClaimLeftSystem>(W2GateKeys.ClaimLeftSystem, W2GateKeys.ClaimStage, "W2GateClaimLeftSystem"),
                new ManagedSystemRegistration<W2GateClaimRightSystem>(W2GateKeys.ClaimRightSystem, W2GateKeys.ClaimStage, "W2GateClaimRightSystem"),
                new ManagedSystemRegistration<W2GateProjectSystem>(W2GateKeys.ProjectSystem, W2GateKeys.ProjectStage, "W2GateProjectSystem"),
            };
        }

        /// <summary>Creation request of one command-driven gate world (O-01, P-035).</summary>
        public static WorldCreateRequest CommandDrivenRequest(WorldId world, OperationId operation, ContentHash catalogHash)
        {
            return new WorldCreateRequest(
                world,
                GateWorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                catalogHash,
                operation,
                null);
        }
    }
}
