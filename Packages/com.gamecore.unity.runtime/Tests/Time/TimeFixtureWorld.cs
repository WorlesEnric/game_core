// GameCore.Unity.Runtime test fixture (GC-009): the fixture module, its systems and its world registration.
//
// HAZARD IF THE FIRST UNITY BUILD DISAGREES: these managed systems live in the Editor-only test assembly. GC-005
// proved the same pattern inside a runtime assembly (`Fixtures/Runtime`, assembly `GameCore.Unity.Fixtures`). If
// Entities' source generator or `CreateSystemManaged<T>` refuses a test-assembly system, move `TimeFixture*` into
// `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/Time/` unchanged and let the test assembly reference
// `GameCore.Unity.Fixtures` (which it already does). No test logic would change.
// The fixture world is deliberately tiny: one producer stage whose real scheduled job writes a native container the
// ECS does not track and whose handle is recorded only in the time module's native dependency table, one consumer
// stage that must wait for that handle before it reads, and one output stage that runs on host frames even while the
// world is idle. Nothing here reuses or replaces GC-005's fixture.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Unity.Runtime.Time;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Time
{
    /// <summary>
    /// Per-world state of the time fixture: the non-component container, its native fence table, the adopted
    /// temporal driver and the stage indices the systems need. Held in a table keyed by the live world, because a
    /// system instance cannot receive constructor arguments from <c>CreateSystemManaged</c> (04 section 8).
    /// </summary>
    public sealed class TimeFixtureModule : IDisposable
    {
        private static readonly List<TimeFixtureModule> modules = new List<TimeFixtureModule>();

        private readonly TimeFixtureContainer container;

        internal TimeFixtureModule(World world, UnityWorldHost host, int resultSlot, int produceStageIndex, int consumeStageIndex)
        {
            World = world;
            Host = host;
            container = new TimeFixtureContainer(4);
            Observed = new NativeArray<int>(1, Allocator.Persistent);
            Native = new NativeDependencyTable(Math.Max(1, resultSlot + 1));
            ResultSlot = resultSlot;
            ProduceStageIndex = produceStageIndex;
            ConsumeStageIndex = consumeStageIndex;
        }

        public World World { get; }

        public UnityWorldHost Host { get; }

        public NativeDependencyTable Native { get; }

        public int ResultSlot { get; }

        public int ProduceStageIndex { get; }

        public int ConsumeStageIndex { get; }

        /// <summary>Value the consumer job observed in the non-component container (a main-thread read copy).</summary>
        public NativeArray<int> Observed { get; }

        /// <summary>Incoming handle the consumer stage combined for its read; default means it waited for nothing.</summary>
        public JobHandle LastConsumerIncoming { get; internal set; }

        /// <summary>Handle the producer stage scheduled; the value the consumer must have waited for.</summary>
        public JobHandle LastProducerHandle { get; internal set; }

        /// <summary>Value the producer job wrote this step; the consumer must observe exactly this.</summary>
        public int LastProducedValue { get; internal set; }

        public NativeArray<int> Results => container.Results;

        /// <summary>Time driver adopted by this world (cutoff, plugin clocks and the wrapped pump).</summary>
        public WorldTimeDriver? Time { get; internal set; }

        public static TimeFixtureModule Attach(
            World world,
            UnityWorldHost host,
            int resultSlot,
            int produceStageIndex,
            int consumeStageIndex)
        {
            var module = new TimeFixtureModule(world, host, resultSlot, produceStageIndex, consumeStageIndex);
            modules.Add(module);
            return module;
        }

        public static bool TryGet(World world, out TimeFixtureModule? module)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].World == world)
                {
                    module = modules[i];
                    return true;
                }
            }

            module = null;
            return false;
        }

        public static void Detach(World world)
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                if (modules[i].World == world)
                {
                    modules[i].Dispose();
                    modules.RemoveAt(i);
                }
            }
        }

        public static void DetachAll()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Dispose();
            }

            modules.Clear();
        }

        public void Dispose()
        {
            container.Dispose();
            if (Observed.IsCreated)
            {
                Observed.Dispose();
            }

            Native.Dispose();
        }
    }

    /// <summary>
    /// Producer stage. It schedules a real Burst job on a container the ECS does not track and deliberately does
    /// <b>not</b> write the handle back into the system dependency: the only record of the producer is the native
    /// dependency table and the host stage fence, which is exactly the case 04 section 4 requires to be explicit.
    /// </summary>
    [DisableAutoCreation]
    public partial class TimeFixtureProduceSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<TimeFixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty || !TimeFixtureModule.TryGet(World, out TimeFixtureModule? module) || module == null)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            TimeFixtureTrail trail = EntityManager.GetComponentData<TimeFixtureTrail>(entity);
            trail.ProduceCount++;
            EntityManager.SetComponentData(entity, trail);

            int value = 100 + trail.ProduceCount;
            JobHandle handle = new TimeFixtureWriteJob { Results = module.Results, Value = value }
                .Schedule(default(JobHandle));

            module.LastProducerHandle = handle;
            module.LastProducedValue = value;

            module.Native.Store(
                module.ResultSlot,
                handle,
                module.ProduceStageIndex,
                TimeFixtureKeys.ProduceStage,
                TimeFixtureKeys.ProduceSystem,
                module.Host.CurrentEpoch,
                module.Host.CurrentStep,
                module.Host.Driver,
                module.Host.StepGroup.Fences);
        }
    }

    /// <summary>
    /// Consumer stage. It schedules its own job with the producer's fence combined in, completes it on the main
    /// thread, and records what it observed: a dependent read waits for an unfinished producer (P-041).
    /// </summary>
    [DisableAutoCreation]
    public partial class TimeFixtureConsumeSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<TimeFixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty || !TimeFixtureModule.TryGet(World, out TimeFixtureModule? module) || module == null)
            {
                return;
            }

            JobHandle producer = module.Native.CombineIncoming(module.ResultSlot);
            module.LastConsumerIncoming = producer;

            JobHandle read = new TimeFixtureReadJob { Results = module.Results, Observed = module.Observed }
                .Schedule(JobHandle.CombineDependencies(Dependency, producer));

            // A main-thread read must wait for its producers: complete the chain that includes the producer fence.
            read.Complete();

            Entity entity = trailQuery.GetSingletonEntity();
            TimeFixtureTrail trail = EntityManager.GetComponentData<TimeFixtureTrail>(entity);
            trail.ConsumeCount++;
            trail.ConsumedValue = module.Observed[0];
            EntityManager.SetComponentData(entity, trail);
        }
    }

    /// <summary>Output stage: presentation runs on host frames, including idle ones, and never advances a step.</summary>
    [DisableAutoCreation]
    public partial class TimeFixtureCaptureSystem : SystemBase
    {
        private EntityQuery trailQuery;

        protected override void OnCreate()
        {
            base.OnCreate();
            trailQuery = GetEntityQuery(ComponentType.ReadWrite<TimeFixtureTrail>());
        }

        protected override void OnUpdate()
        {
            if (trailQuery.IsEmpty)
            {
                return;
            }

            Entity entity = trailQuery.GetSingletonEntity();
            TimeFixtureTrail trail = EntityManager.GetComponentData<TimeFixtureTrail>(entity);
            trail.CaptureCount++;
            EntityManager.SetComponentData(entity, trail);
        }
    }

    /// <summary>Builds the fixture world registration: two ordered gameplay stages plus one output stage.</summary>
    public static class TimeFixtureRegistration
    {
        /// <summary>Fence index space of the fixture: produce, consume, capture.</summary>
        public const int StageCount = 3;

        public const int ProduceStageIndex = 0;
        public const int ConsumeStageIndex = 1;
        public const int CaptureStageIndex = 2;

        /// <summary>100 ns host ticks per second, matching the fixed-step fixture configuration (P-036).</summary>
        public const ulong HostTicksPerSecond = 10_000_000UL;

        public static readonly WorldDefinitionId CommandWorldDefinition =
            new WorldDefinitionId(new Id128(TimeFixtureKeys.Namespace, 0x3001UL));

        public static readonly WorldDefinitionId FixedWorldDefinition =
            new WorldDefinitionId(new Id128(TimeFixtureKeys.Namespace, 0x3002UL));

        /// <summary>10 ms steps with a four-step catch-up limit per pump (P-036).</summary>
        public static FixedStepSettings FixedStepConfiguration()
            => new FixedStepSettings(100_000UL, HostTicksPerSecond, 4U, usesUnscaledHostClock: true);

        public static WorldCreateRequest CommandDrivenRequest(WorldId world, OperationId operation)
            => new WorldCreateRequest(
                world,
                CommandWorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                ContentHash.Empty,
                operation,
                null);

        public static WorldCreateRequest FixedStepRequest(WorldId world, OperationId operation)
            => new WorldCreateRequest(
                world,
                FixedWorldDefinition,
                TemporalModel.FixedStep,
                PropagationMode.Automatic,
                ContentHash.Empty,
                operation,
                FixedStepConfiguration());

        public static UnityWorldRegistration Create()
        {
            return new UnityWorldRegistration(
                "GameCoreTimeFixtureWorld",
                Stages(),
                Systems(),
                GuardedDispatchPlan.Empty,
                StepPlan(),
                OutputPlan(),
                world => TimeFixtureWorldState.Seed(world));
        }

        /// <summary>
        /// Creates a fixture world, attaches its module and adopts a temporal driver. The caller owns disposal.
        /// </summary>
        public static bool TryCreateWorld(
            WorldId world,
            OperationId operation,
            bool fixedStep,
            uint perStepCommandCapacity,
            out UnityWorldHost? host,
            out TimeFixtureModule? module,
            out WorldCreateResult result)
        {
            UnityWorldRegistration registration = Create();
            bool created = UnityWorldRegistry.TryCreate(
                fixedStep ? FixedStepRequest(world, operation) : CommandDrivenRequest(world, operation),
                registration,
                out host,
                out result);

            module = null;
            if (!created || host == null)
            {
                return false;
            }

            module = TimeFixtureModule.Attach(host.EntityWorld, host, 0, ProduceStageIndex, ConsumeStageIndex);
            module.Time = new WorldTimeDriver(
                host,
                new StepInputCutoff(64, 128),
                new PluginClockRegistry(32),
                perStepCommandCapacity);

            // The host owns the native resource table of the compiled schedule; the temporal driver decides when its
            // step begins, so it resets the table at step admission (P-041).
            module.Time.AdoptResourceTable(module.Native);
            return true;
        }

        private static IReadOnlyList<StageRegistration> Stages()
        {
            return new List<StageRegistration>
            {
                new StageRegistration(TimeFixtureKeys.ProduceStage, "time.fixture.stage.produce", ProduceStageIndex, null),
                new StageRegistration(
                    TimeFixtureKeys.ConsumeStage,
                    "time.fixture.stage.consume",
                    ConsumeStageIndex,
                    new[] { ProduceStageIndex }),
                new StageRegistration(TimeFixtureKeys.CaptureStage, "time.fixture.stage.capture", CaptureStageIndex, null),
            };
        }

        /// <summary>Generated-style registrations of the fixture systems; the compiled-schedule test selects from it.</summary>
        public static IReadOnlyList<SystemRegistration> Systems()
        {
            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<TimeFixtureProduceSystem>(
                    TimeFixtureKeys.ProduceSystem,
                    TimeFixtureKeys.ProduceStage,
                    "TimeFixtureProduceSystem"),
                new ManagedSystemRegistration<TimeFixtureConsumeSystem>(
                    TimeFixtureKeys.ConsumeSystem,
                    TimeFixtureKeys.ConsumeStage,
                    "TimeFixtureConsumeSystem"),
                new ManagedSystemRegistration<TimeFixtureCaptureSystem>(
                    TimeFixtureKeys.CaptureSystem,
                    TimeFixtureKeys.CaptureStage,
                    "TimeFixtureCaptureSystem"),
            };
        }

        private static GuardedDispatchPlan StepPlan()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                new GuardedDispatchEntry(
                    TimeFixtureKeys.ProduceStage,
                    TimeFixtureKeys.ProduceSystem,
                    SystemDispatchKind.ManagedSystem,
                    0,
                    ProduceStageIndex,
                    null),
                new GuardedDispatchEntry(
                    TimeFixtureKeys.ConsumeStage,
                    TimeFixtureKeys.ConsumeSystem,
                    SystemDispatchKind.ManagedSystem,
                    1,
                    ConsumeStageIndex,
                    new[] { ProduceStageIndex }),
            };

            // The declared buffer makes the commit-time drain check require the consumer stage after a producer ran
            // (P-043, O-16), which is the same contract the compiled schedule produces.
            var bindings = new List<BufferBinding>
            {
                new BufferBinding(
                    TimeFixtureKeys.ResultBuffer,
                    new[] { TimeFixtureKeys.ProduceSystem },
                    TimeFixtureKeys.ConsumeStage),
            };

            return new GuardedDispatchPlan(entries, bindings, StageCount);
        }

        private static GuardedDispatchPlan OutputPlan()
        {
            var entries = new List<GuardedDispatchEntry>
            {
                new GuardedDispatchEntry(
                    TimeFixtureKeys.CaptureStage,
                    TimeFixtureKeys.CaptureSystem,
                    SystemDispatchKind.ManagedSystem,
                    0,
                    CaptureStageIndex,
                    null),
            };

            return new GuardedDispatchPlan(entries, null, StageCount);
        }
    }
}
