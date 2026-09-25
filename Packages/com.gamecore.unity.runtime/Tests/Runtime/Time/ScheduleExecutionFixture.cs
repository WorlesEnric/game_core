// GameCore.Unity.Runtime test fixture (GC-009): a world whose dispatch table is produced by the *real* schedule
// compiler, used to evidence two GC-009 acceptance items that structural-only tests cannot:
//
//   * "disjoint work overlaps safely" - two stages whose access sets are disjoint compile with no edge between them,
//     so the dispatcher hands each entry an empty incoming fence and neither job is chained onto the other;
//   * "actual jobs complete before dependent reads/playback" - a producer job writes a component and a later stage's
//     deferred structural playback (ECB) must observe it, which is only possible if the playback waited for the
//     producer's handle.
//
// Both worlds are built the way the product builds one: `ScheduleExecutionRegistration.Declarations` is handed to
// `ScheduleCompiler`, the compiled schedule is adapted by `CompiledScheduleAdapter` into GC-005's guarded dispatch
// table, and the resulting `UnityWorldRegistration` creates a real owned world. Nothing here hand-writes a dispatch
// table, so the order, the edges and the incoming-dependency sets under test are the compiler's own output.
//
// Two measurement notes, because both matter for the honesty of the assertions:
//
//  1. `SystemBase.Dependency` read inside `OnUpdate` is recomputed by the safety manager from the component types the
//     system has declared so far, so it is "what this system would wait on", not literally the dispatcher's incoming
//     fence. That is exactly the quantity the overlap assertion needs (a job that does not wait on its neighbour), and
//     the dispatcher's own per-stage fences are read separately through the public `NativeFenceTable`, which is where
//     the dispatcher really stored them.
//  2. The producer stage deliberately does NOT write its handle back to `Dependency` (as GC-005's non-component
//     fixture also does not), and it hands its handle to `NativeDependencyTable` instead. Since the guarded dispatcher
//     overwrites a stage's fence slot with the system's own post-update dependency, the native table is the only
//     carrier of that handle - which is what makes the playback wait load-bearing rather than incidental.
//
// All fixture counters live on the managed module rather than in an ECS component, so the fixture never performs a
// main-thread component write while a job is pending.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Planning.Scheduling;
using GameCore.Unity.Runtime.Time;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Tests.Time
{
    /// <summary>Generated-style keys of the execution fixture; its namespace can never collide with a package key.</summary>
    public static class ScheduleExecutionKeys
    {
        public const ulong Namespace = 0x4743584558454355UL;

        public static readonly StageId LeftStage = Stage(1UL);
        public static readonly StageId RightStage = Stage(2UL);
        public static readonly StageId ObserveStage = Stage(3UL);
        public static readonly StageId ProduceStage = Stage(4UL);
        public static readonly StageId PlaybackStage = Stage(5UL);

        public static readonly FactoryKey LeftSystem = Key(1UL, "gc009.exec.left");
        public static readonly FactoryKey RightSystem = Key(2UL, "gc009.exec.right");
        public static readonly FactoryKey ObserveSystem = Key(3UL, "gc009.exec.observe");
        public static readonly FactoryKey ProduceSystem = Key(4UL, "gc009.exec.produce");
        public static readonly FactoryKey PlaybackSystem = Key(5UL, "gc009.exec.playback");

        /// <summary>The declared buffer whose producer fence the structural playback point must wait for (P-041).</summary>
        public static readonly BufferId PlaybackBuffer = new BufferId(new Id128(Namespace, 0x2001UL));

        public static readonly SchemaRef LeftSchema = Schema(1UL);
        public static readonly SchemaRef RightSchema = Schema(2UL);
        public static readonly SchemaRef SourceSchema = Schema(3UL);
        public static readonly SchemaRef ObservedSchema = Schema(4UL);

        public static StageId Stage(ulong ordinal) => new StageId(new Id128(Namespace, 0x1000UL + ordinal));

        public static SchemaRef Schema(ulong ordinal)
            => new SchemaRef(new SchemaId(new Id128(Namespace, 0x3000UL + ordinal)), 1U);

        private static FactoryKey Key(ulong ordinal, string stableName)
            => new FactoryKey(new Id128(Namespace, ordinal), NameKeyVersion(stableName));

        private static uint NameKeyVersion(string stableName)
        {
            uint hash = 2166136261U;
            for (int i = 0; i < stableName.Length; i++)
            {
                hash ^= stableName[i];
                hash *= 16777619U;
            }

            return hash == 0U ? 1U : hash;
        }
    }

    /// <summary>Written by the left stage's job; seeded to -1 so only the job can produce the asserted value.</summary>
    public struct LeftValue : IComponentData
    {
        public int Value;
    }

    /// <summary>Written by the right stage's job; a different component type is what makes the two disjoint.</summary>
    public struct RightValue : IComponentData
    {
        public int Value;
    }

    /// <summary>Written by the playback fixture's producer job and read by the later playback stage.</summary>
    public struct PlaybackSourceValue : IComponentData
    {
        public int Value;
    }

    /// <summary>Added and set only by the deferred structural playback of the later stage.</summary>
    public struct PlaybackObserved : IComponentData
    {
        public int Value;
    }

    /// <summary>Left stage job: writes its own component and stamps its own timing container.</summary>
    public struct LeftWriteJob : IJob
    {
        public ComponentLookup<LeftValue> Values;

        public Entity Target;

        public int Value;

        /// <summary>Exclusive to this job, so two disjoint jobs never touch one container (P-041).</summary>
        public NativeArray<long> Timing;

        public void Execute()
        {
            Timing[0] = Stopwatch.GetTimestamp();
            Values[Target] = new LeftValue { Value = Value };
            Timing[1] = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Right stage job: the mirror of <see cref="LeftWriteJob"/> on a different component and container.</summary>
    public struct RightWriteJob : IJob
    {
        public ComponentLookup<RightValue> Values;

        public Entity Target;

        public int Value;

        public NativeArray<long> Timing;

        public void Execute()
        {
            Timing[0] = Stopwatch.GetTimestamp();
            Values[Target] = new RightValue { Value = Value };
            Timing[1] = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Producer job of the playback fixture. It writes the component the later playback must observe, and it also
    /// writes a non-component container: the component write is what the playback's *value* assertion is about, while
    /// the container is the resource Unity's job safety gates, so the unsafe variant of the playback can be shown to be
    /// rejected rather than merely reading a stale value.
    /// </summary>
    public struct PlaybackSourceWriteJob : IJob
    {
        public ComponentLookup<PlaybackSourceValue> Values;

        public Entity Target;

        public int Value;

        /// <summary>Non-component resource the producer owns until its handle completes (P-041).</summary>
        public NativeArray<int> Container;

        public void Execute()
        {
            Values[Target] = new PlaybackSourceValue { Value = Value };
            Container[0] = Value;
        }
    }

    /// <summary>Per-world state of the execution fixture, keyed by the live world (04 section 8).</summary>
    public sealed class ScheduleExecutionModule : IDisposable
    {
        private static readonly List<ScheduleExecutionModule> modules = new List<ScheduleExecutionModule>();

        private ScheduleExecutionModule(
            World world,
            UnityWorldHost host,
            CompiledSchedule schedule,
            ScheduleAdaptation adaptation)
        {
            World = world;
            Host = host;
            Schedule = schedule;
            Adaptation = adaptation;
            Native = new NativeDependencyTable(Math.Max(1, adaptation.Buffers.Count));
            ResultSlot = adaptation.Buffers.Count > 0 ? adaptation.Buffers[0].Slot : 0;
            LeftTiming = new NativeArray<long>(2, Allocator.Persistent);
            RightTiming = new NativeArray<long>(2, Allocator.Persistent);
            Container = new NativeArray<int>(1, Allocator.Persistent);

            EntityManager entityManager = world.EntityManager;
            LeftEntity = entityManager.CreateEntity(typeof(LeftValue));
            entityManager.SetComponentData(LeftEntity, new LeftValue { Value = -1 });

            RightEntity = entityManager.CreateEntity(typeof(RightValue));
            entityManager.SetComponentData(RightEntity, new RightValue { Value = -1 });

            SourceEntity = entityManager.CreateEntity(typeof(PlaybackSourceValue));
            entityManager.SetComponentData(SourceEntity, new PlaybackSourceValue { Value = -1 });

            SetTarget = entityManager.CreateEntity(typeof(PlaybackObserved));
            entityManager.SetComponentData(SetTarget, new PlaybackObserved { Value = -1 });

            // Deliberately without PlaybackObserved: adding it is the structural change the playback performs.
            AddTarget = entityManager.CreateEntity();
        }

        public World World { get; }

        public UnityWorldHost Host { get; }

        /// <summary>The schedule this world's dispatch table was installed from (the compiler's own output).</summary>
        public CompiledSchedule Schedule { get; }

        /// <summary>The adapter result installed in this world's step group.</summary>
        public ScheduleAdaptation Adaptation { get; }

        public NativeDependencyTable Native { get; }

        public int ResultSlot { get; }

        public NativeArray<long> LeftTiming { get; }

        public NativeArray<long> RightTiming { get; }

        /// <summary>The producer job's non-component container; the gated resource the unsafe variant reads (P-041).</summary>
        public NativeArray<int> Container { get; }

        public Entity LeftEntity { get; }

        public Entity RightEntity { get; }

        /// <summary>Carries the component the producer job writes; -1 until that job completes.</summary>
        public Entity SourceEntity { get; }

        /// <summary>Already carries <see cref="PlaybackObserved"/>; the playback sets its value.</summary>
        public Entity SetTarget { get; }

        /// <summary>Does not carry <see cref="PlaybackObserved"/>; adding it is the structural change.</summary>
        public Entity AddTarget { get; }

        /// <summary>Time driver adopted by this world (cutoff, plugin clocks and the wrapped pump).</summary>
        public WorldTimeDriver? Time { get; internal set; }

        /// <summary>Fence index of each compiled stage, resolved from the compiled schedule.</summary>
        public int LeftStageIndex { get; private set; } = -1;

        public int RightStageIndex { get; private set; } = -1;

        public int ObserveStageIndex { get; private set; } = -1;

        public int ProduceStageIndex { get; private set; } = -1;

        public int PlaybackStageIndex { get; private set; } = -1;

        /// <summary>
        /// The value this system's own safety-manager dependency resolved to on its first read. It is a cheap
        /// consistency check rather than independent evidence: for a stage with no declared component access this
        /// resolves to the default handle whatever the dispatcher passed in, so it can only ever confirm that nothing
        /// in this step's dispatch gave the entry a producer to wait for. The load-bearing observations of
        /// non-serialization are <see cref="RightSawLeftSlot"/> (the neighbour's real handle was already recorded in
        /// the dispatcher's fence when this entry ran) together with the compiled predecessor sets.
        /// </summary>
        public JobHandle LeftWaitValue { get; internal set; }

        /// <summary>The right entry's version of <see cref="LeftWaitValue"/>.</summary>
        public JobHandle RightWaitValue { get; internal set; }

        /// <summary>
        /// The right stage's fence slot as the left entry found it, i.e. before the right stage had run at all. A
        /// default value here proves the left job could not have been chained onto the right one.
        /// </summary>
        public JobHandle LeftSawRightSlot { get; internal set; }

        /// <summary>
        /// The left stage's fence slot as the right entry found it. This is the decisive observation: the left job's
        /// handle was already recorded in the dispatcher's own stage fence, yet the right entry still waited on
        /// nothing, so the dispatch did not serialize the two disjoint jobs (P-040, P-041).
        /// </summary>
        public JobHandle RightSawLeftSlot { get; internal set; }

        /// <summary>Handle each stage's job produced.</summary>
        public JobHandle LeftHandle { get; internal set; }

        public JobHandle RightHandle { get; internal set; }

        /// <summary>The left stage's fence slot, read by the ordered observer stage after both disjoint stages ran.</summary>
        public JobHandle LeftSlot { get; internal set; }

        /// <summary>The right stage's fence slot, read by the ordered observer stage.</summary>
        public JobHandle RightSlot { get; internal set; }

        /// <summary>
        /// The combination the ordered observer entry would have waited on: read from the dispatcher's own per-stage
        /// fence table over both disjoint predecessor stages, which is exactly what the guarded dispatcher combines
        /// for that entry (P-041).
        /// </summary>
        public JobHandle ObserveWaitsOn { get; internal set; }

        /// <summary>Value each job was told to write; the component can only hold it if the job ran.</summary>
        public int LeftWritten { get; internal set; }

        public int RightWritten { get; internal set; }

        public int LeftDispatchCount { get; internal set; }

        public int RightDispatchCount { get; internal set; }

        public JobHandle ProduceHandle { get; internal set; }

        /// <summary>
        /// The produce stage's own fence slot as the playback entry found it. It is default, because the producer does
        /// not publish through <c>Dependency</c> and the dispatcher overwrote the slot that the native table had
        /// forwarded into - so the native table really is the only carrier of the producer's handle (P-041).
        /// </summary>
        public JobHandle ProduceSlot { get; internal set; }

        public int ProducedValue { get; internal set; }

        /// <summary>Handle the playback stage combined for its producer: the producer's own handle (P-041).</summary>
        public JobHandle PlaybackIncoming { get; internal set; }

        /// <summary>Value the playback stage read from the producer's component before playing its structural change.</summary>
        public int PlaybackReadValue { get; internal set; }

        /// <summary>
        /// Job records the world ledger still held when this stage ran. It is host bookkeeping - the dispatcher
        /// recorded these work items before the stage ran and retires them at the commit boundary - so it shows the
        /// step had recorded that work, not that a job was caught mid-execution (06 section 5).
        /// </summary>
        public int OutstandingJobsDuringStep { get; internal set; }

        /// <summary>The observe stage ran; proves the step reached the point after both disjoint stages dispatched.</summary>
        public int ObserveDispatchCount { get; internal set; }

        /// <summary>
        /// Fixture fault injection: when true the playback stage reads the producer's container without waiting for
        /// its handle, which is the broken implementation the safety system must reject (P-041).
        /// </summary>
        public bool SkipProducerWait { get; set; }

        public static ScheduleExecutionModule Attach(
            World world,
            UnityWorldHost host,
            CompiledSchedule schedule,
            ScheduleAdaptation adaptation)
        {
            var module = new ScheduleExecutionModule(world, host, schedule, adaptation);
            module.ResolveStageIndexes();
            modules.Add(module);
            return module;
        }

        public static bool TryGet(World world, out ScheduleExecutionModule? module)
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

        public static void DetachAll()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Dispose();
            }

            modules.Clear();
        }

        /// <summary>Fence slot of one compiled stage in the dispatcher's own table; default when nothing recorded it.</summary>
        public JobHandle FenceSlotOf(int stageIndex)
            => Host.StepGroup.Fences!.CombineIncoming(new[] { stageIndex });

        public void Dispose()
        {
            // A container must not be disposed while a job still has it recorded as written. The host settles
            // registered work at teardown, so this is a belt-and-braces completion for a fixture step that never
            // committed; a failure here must not mask the assertion the test was actually making.
            CompleteQuietly(ProduceHandle);
            CompleteQuietly(LeftHandle);
            CompleteQuietly(RightHandle);

            if (LeftTiming.IsCreated)
            {
                LeftTiming.Dispose();
            }

            if (RightTiming.IsCreated)
            {
                RightTiming.Dispose();
            }

            if (Container.IsCreated)
            {
                Container.Dispose();
            }

            Native.Dispose();
        }

        /// <summary>Fence index of one compiled stage; -1 when the schedule does not contain it.</summary>
        public int StageIndexOf(StageId stage)
            => Schedule.TryGetStageIndex(stage, out int index) ? index : -1;

        private static void CompleteQuietly(JobHandle handle)
        {
            if (handle.Equals(default(JobHandle)))
            {
                return;
            }

            try
            {
                handle.Complete();
            }
            catch (Exception)
            {
                // Teardown continues; the host owns settling registered work.
            }
        }

        private void ResolveStageIndexes()
        {
            LeftStageIndex = StageIndexOf(ScheduleExecutionKeys.LeftStage);
            RightStageIndex = StageIndexOf(ScheduleExecutionKeys.RightStage);
            ObserveStageIndex = StageIndexOf(ScheduleExecutionKeys.ObserveStage);
            ProduceStageIndex = StageIndexOf(ScheduleExecutionKeys.ProduceStage);
            PlaybackStageIndex = StageIndexOf(ScheduleExecutionKeys.PlaybackStage);
        }
    }

    /// <summary>
    /// Left stage of the disjoint pair. It schedules its job under the dependency the dispatcher handed it and
    /// publishes the result through <c>Dependency</c>, exactly as a normal system does, and it also records the right
    /// stage's fence slot as it finds it - which is empty, because the right stage has not run yet.
    /// </summary>
    [DisableAutoCreation]
    public partial class ScheduleExecutionLeftSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!ScheduleExecutionModule.TryGet(World, out ScheduleExecutionModule? module) || module == null)
            {
                return;
            }

            module.LeftWaitValue = Dependency;
            module.LeftSawRightSlot = module.FenceSlotOf(module.RightStageIndex);
            module.LeftDispatchCount++;
            int value = 1000 + module.LeftDispatchCount;

            ComponentLookup<LeftValue> values = GetComponentLookup<LeftValue>(false);
            JobHandle handle = new LeftWriteJob
            {
                Values = values,
                Target = module.LeftEntity,
                Value = value,
                Timing = module.LeftTiming,
            }.Schedule(default(JobHandle));

            module.LeftHandle = handle;
            module.LeftWritten = value;
            Dependency = handle;
        }
    }

    /// <summary>
    /// Right stage of the disjoint pair: the mirror image, on a different component and container. It records the
    /// left stage's fence slot as it finds it, which by then already holds the left job's handle.
    /// </summary>
    [DisableAutoCreation]
    public partial class ScheduleExecutionRightSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!ScheduleExecutionModule.TryGet(World, out ScheduleExecutionModule? module) || module == null)
            {
                return;
            }

            module.RightWaitValue = Dependency;
            module.RightSawLeftSlot = module.FenceSlotOf(module.LeftStageIndex);
            module.RightDispatchCount++;
            int value = 2000 + module.RightDispatchCount;

            ComponentLookup<RightValue> values = GetComponentLookup<RightValue>(false);
            JobHandle handle = new RightWriteJob
            {
                Values = values,
                Target = module.RightEntity,
                Value = value,
                Timing = module.RightTiming,
            }.Schedule(default(JobHandle));

            module.RightHandle = handle;
            module.RightWritten = value;
            Dependency = handle;
        }
    }

    /// <summary>
    /// Observer stage, ordered after both disjoint stages. It declares no ECS access and schedules nothing: it reads
    /// the dispatcher's own per-stage fence slots for its two predecessors and records what the world's job ledger
    /// held at that point in the step.
    /// </summary>
    [DisableAutoCreation]
    public partial class ScheduleExecutionObserveSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!ScheduleExecutionModule.TryGet(World, out ScheduleExecutionModule? module) || module == null)
            {
                return;
            }

            module.LeftSlot = module.FenceSlotOf(module.LeftStageIndex);
            module.RightSlot = module.FenceSlotOf(module.RightStageIndex);
            module.ObserveWaitsOn = module.Host.StepGroup.Fences!.CombineIncoming(
                new[] { module.LeftStageIndex, module.RightStageIndex });
            module.OutstandingJobsDuringStep = module.Host.Ledger.OutstandingJobCount;
            module.ObserveDispatchCount++;
        }
    }

    /// <summary>
    /// Producer stage of the playback fixture. Like GC-005's non-component fixture it does <b>not</b> write its handle
    /// back into <c>Dependency</c>: its only carrier is the native dependency table (which also forwards the handle
    /// into the host stage fence), so the later playback's wait is load-bearing rather than incidental.
    /// </summary>
    [DisableAutoCreation]
    public partial class ScheduleExecutionProduceSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!ScheduleExecutionModule.TryGet(World, out ScheduleExecutionModule? module) || module == null)
            {
                return;
            }

            module.ProducedValue++;

            ComponentLookup<PlaybackSourceValue> values = GetComponentLookup<PlaybackSourceValue>(false);
            JobHandle handle = new PlaybackSourceWriteJob
            {
                Values = values,
                Target = module.SourceEntity,
                Value = module.ProducedValue,
                Container = module.Container,
            }.Schedule(default(JobHandle));

            module.ProduceHandle = handle;
            module.Native.Store(
                module.ResultSlot,
                handle,
                module.ProduceStageIndex,
                ScheduleExecutionKeys.ProduceStage,
                ScheduleExecutionKeys.ProduceSystem,
                module.Host.CurrentEpoch,
                module.Host.CurrentStep,
                module.Host.Driver,
                module.Host.StepGroup.Fences);
        }
    }

    /// <summary>
    /// Playback stage: it waits for the producer's handle, reads the component the producer's job wrote, and only then
    /// plays back a deferred structural change carrying that observed value. Producer completion before playback is
    /// exactly what 04 section 4 requires; <see cref="ScheduleExecutionModule.SkipProducerWait"/> injects the broken
    /// variant, which reads the producer's container with its handle still outstanding.
    /// </summary>
    [DisableAutoCreation]
    public partial class ScheduleExecutionPlaybackSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            if (!ScheduleExecutionModule.TryGet(World, out ScheduleExecutionModule? module) || module == null)
            {
                return;
            }

            JobHandle producer = module.Native.CombineIncoming(module.ResultSlot);
            module.PlaybackIncoming = producer;
            module.ProduceSlot = module.FenceSlotOf(module.ProduceStageIndex);
            module.OutstandingJobsDuringStep = module.Host.Ledger.OutstandingJobCount;

            if (module.SkipProducerWait)
            {
                // Deliberately unsynchronized: the producer's handle has never been completed, so this main-thread
                // read of the container it owns is what the job safety system must reject (P-041).
                module.PlaybackReadValue = module.Container[0];
            }
            else
            {
                JobHandle.CombineDependencies(Dependency, producer).Complete();
                module.PlaybackReadValue =
                    EntityManager.GetComponentData<PlaybackSourceValue>(module.SourceEntity).Value;
            }

            var ecb = new EntityCommandBuffer(Allocator.TempJob);
            try
            {
                ecb.SetComponent(module.SetTarget, new PlaybackObserved { Value = module.PlaybackReadValue });
                ecb.AddComponent(module.AddTarget, new PlaybackObserved { Value = module.PlaybackReadValue });
                ecb.Playback(EntityManager);
            }
            finally
            {
                ecb.Dispose();
            }
        }
    }

    /// <summary>Which declaration set a fixture world is built from.</summary>
    public enum ScheduleExecutionShape
    {
        /// <summary>Two unordered stages with disjoint access, plus an observer stage ordered after both.</summary>
        Overlap = 0,

        /// <summary>A producer stage and a later playback stage joined by a declared buffer contract.</summary>
        Playback = 1,
    }

    /// <summary>Declarations, compilation and world creation for the execution fixture.</summary>
    public static class ScheduleExecutionRegistration
    {
        public static readonly WorldDefinitionId OverlapWorldDefinition =
            new WorldDefinitionId(new Id128(ScheduleExecutionKeys.Namespace, 0x4001UL));

        public static readonly WorldDefinitionId PlaybackWorldDefinition =
            new WorldDefinitionId(new Id128(ScheduleExecutionKeys.Namespace, 0x4002UL));

        public static WorldCreateRequest Request(ScheduleExecutionShape shape, WorldId world, OperationId operation)
            => new WorldCreateRequest(
                world,
                shape == ScheduleExecutionShape.Overlap ? OverlapWorldDefinition : PlaybackWorldDefinition,
                TemporalModel.CommandDriven,
                PropagationMode.Automatic,
                ContentHash.Empty,
                operation,
                null);

        /// <summary>
        /// The fixture declarations. The two disjoint stages declare different schemas and no edge to each other, so
        /// the compiler must accept them unordered; the playback stages are joined only by a declared buffer port.
        /// </summary>
        public static ScheduleDeclarations Declarations(ScheduleExecutionShape shape)
        {
            if (shape == ScheduleExecutionShape.Playback)
            {
                var produceStage = new StageSpec(
                    ScheduleExecutionKeys.ProduceStage,
                    1U,
                    new Id128(ScheduleExecutionKeys.Namespace, 0x5000UL),
                    HostAffinity.ManagedMain,
                    null,
                    null,
                    new AccessSet(new[]
                    {
                        new AccessDeclaration(ScheduleExecutionKeys.SourceSchema, AccessMode.Write, default(Id128)),
                    }),
                    null,
                    null,
                    null,
                    null,
                    new[]
                    {
                        SystemSpecOf(
                            ScheduleExecutionKeys.ProduceSystem,
                            new AccessDeclaration(ScheduleExecutionKeys.SourceSchema, AccessMode.Write, default(Id128))),
                    },
                    new[]
                    {
                        new BufferPort(ScheduleExecutionKeys.PlaybackBuffer, PortDirection.Producer, ScheduleExecutionKeys.ProduceStage),
                    });

                var playbackStage = new StageSpec(
                    ScheduleExecutionKeys.PlaybackStage,
                    1U,
                    new Id128(ScheduleExecutionKeys.Namespace, 0x5000UL),
                    HostAffinity.ManagedMain,
                    null,
                    null,
                    new AccessSet(new[]
                    {
                        new AccessDeclaration(ScheduleExecutionKeys.SourceSchema, AccessMode.Read, default(Id128)),
                        new AccessDeclaration(ScheduleExecutionKeys.ObservedSchema, AccessMode.Write, default(Id128)),
                    }),
                    null,
                    null,
                    null,
                    null,
                    new[]
                    {
                        SystemSpecOf(
                            ScheduleExecutionKeys.PlaybackSystem,
                            new AccessDeclaration(ScheduleExecutionKeys.SourceSchema, AccessMode.Read, default(Id128)),
                            new AccessDeclaration(ScheduleExecutionKeys.ObservedSchema, AccessMode.Write, default(Id128))),
                    },
                    new[]
                    {
                        new BufferPort(ScheduleExecutionKeys.PlaybackBuffer, PortDirection.Consumer, ScheduleExecutionKeys.PlaybackStage),
                    });

                var buffers = new List<BufferSpec>
                {
                    new BufferSpec(
                        ScheduleExecutionKeys.PlaybackBuffer,
                        ScheduleExecutionKeys.SourceSchema,
                        new[] { ScheduleExecutionKeys.ProduceSystem },
                        ScheduleExecutionKeys.ProduceStage,
                        ScheduleExecutionKeys.PlaybackStage,
                        new FactoryKey(new Id128(ScheduleExecutionKeys.Namespace, 0x6001UL), 1U),
                        BufferLifetime.Stage,
                        16,
                        BufferOverflowPolicy.RejectBeforeMutation,
                        BufferCancellationPolicy.Drain),
                };

                return new ScheduleDeclarations(new[] { produceStage, playbackStage }, buffers);
            }

            var leftStage = new StageSpec(
                ScheduleExecutionKeys.LeftStage,
                1U,
                new Id128(ScheduleExecutionKeys.Namespace, 0x5000UL),
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[]
                {
                    new AccessDeclaration(ScheduleExecutionKeys.LeftSchema, AccessMode.Write, default(Id128)),
                }),
                null,
                null,
                null,
                null,
                new[]
                {
                    SystemSpecOf(
                        ScheduleExecutionKeys.LeftSystem,
                        new AccessDeclaration(ScheduleExecutionKeys.LeftSchema, AccessMode.Write, default(Id128))),
                },
                null);

            var rightStage = new StageSpec(
                ScheduleExecutionKeys.RightStage,
                1U,
                new Id128(ScheduleExecutionKeys.Namespace, 0x5000UL),
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(new[]
                {
                    new AccessDeclaration(ScheduleExecutionKeys.RightSchema, AccessMode.Write, default(Id128)),
                }),
                null,
                null,
                null,
                null,
                new[]
                {
                    SystemSpecOf(
                        ScheduleExecutionKeys.RightSystem,
                        new AccessDeclaration(ScheduleExecutionKeys.RightSchema, AccessMode.Write, default(Id128))),
                },
                null);

            // No access declarations at all: the observer only reads the dispatcher's fences and the step's job
            // ledger, so it can be ordered after both disjoint stages without acquiring an access edge to their data.
            var observeStage = new StageSpec(
                ScheduleExecutionKeys.ObserveStage,
                1U,
                new Id128(ScheduleExecutionKeys.Namespace, 0x5000UL),
                HostAffinity.ManagedMain,
                null,
                null,
                new AccessSet(null),
                null,
                new[] { ScheduleExecutionKeys.LeftStage, ScheduleExecutionKeys.RightStage },
                null,
                null,
                new[] { SystemSpecOf(ScheduleExecutionKeys.ObserveSystem, null) },
                null);

            return new ScheduleDeclarations(new[] { leftStage, rightStage, observeStage }, null);
        }

        public static ScheduleDispatchKindTable Kinds()
            => new ScheduleDispatchKindTable()
                .Add(ScheduleExecutionKeys.LeftSystem, SystemDispatchKind.ManagedSystem)
                .Add(ScheduleExecutionKeys.RightSystem, SystemDispatchKind.ManagedSystem)
                .Add(ScheduleExecutionKeys.ObserveSystem, SystemDispatchKind.ManagedSystem)
                .Add(ScheduleExecutionKeys.ProduceSystem, SystemDispatchKind.ManagedSystem)
                .Add(ScheduleExecutionKeys.PlaybackSystem, SystemDispatchKind.ManagedSystem);

        /// <summary>Compiles the fixture declarations with the real compiler; throws when it refuses them.</summary>
        public static CompiledSchedule Compile(ScheduleExecutionShape shape)
        {
            ScheduleDeclarations declarations = Declarations(shape);
            ScheduleCompilation compilation = ScheduleCompiler.Compile(declarations);
            if (!compilation.Succeeded || compilation.Schedule == null)
            {
                throw new InvalidOperationException(
                    "The execution fixture's own declarations must compile: " + compilation.Explain());
            }

            return compilation.Schedule;
        }

        /// <summary>Compiles and adapts without creating a world; used by the structural assertions.</summary>
        public static ScheduleAdaptation Adapt(ScheduleExecutionShape shape)
        {
            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(Compile(shape), Kinds());
            if (!adaptation.Succeeded)
            {
                throw new InvalidOperationException(
                    "The execution fixture's schedule must adapt: " + adaptation.Explain());
            }

            return adaptation;
        }

        /// <summary>
        /// Creates the fixture world from its compiled schedule, attaches the module and adopts a temporal driver.
        /// </summary>
        public static bool TryCreateWorld(
            ScheduleExecutionShape shape,
            WorldId world,
            OperationId operation,
            out UnityWorldHost? host,
            out ScheduleExecutionModule? module,
            out WorldCreateResult result)
        {
            CompiledSchedule schedule = Compile(shape);
            ScheduleAdaptation adaptation = CompiledScheduleAdapter.Adapt(schedule, Kinds());
            if (!adaptation.Succeeded)
            {
                throw new InvalidOperationException(
                    "The execution fixture's schedule must adapt: " + adaptation.Explain());
            }

            var registration = new UnityWorldRegistration(
                "GameCoreScheduleExecutionWorld",
                adaptation.Stages,
                Systems(shape),
                GuardedDispatchPlan.Empty,
                adaptation.StepPlan!,
                GuardedDispatchPlan.Empty,
                null);

            bool created = UnityWorldRegistry.TryCreate(
                Request(shape, world, operation),
                registration,
                out host,
                out result);

            module = null;
            if (!created || host == null)
            {
                return false;
            }

            module = ScheduleExecutionModule.Attach(host.EntityWorld, host, schedule, adaptation);
            module.Time = new WorldTimeDriver(
                host,
                new StepInputCutoff(16, 32),
                new PluginClockRegistry(8),
                1U);

            // The native table is the compiled schedule's resource table: the driver resets it at step admission and
            // the producer stage stores its handle in it (P-041).
            module.Time.AdoptResourceTable(module.Native);
            return true;
        }

        private static SystemSpec SystemSpecOf(FactoryKey key, AccessDeclaration? access)
            => new SystemSpec(
                key,
                SystemMultiplicity.World,
                access.HasValue ? new AccessSet(new[] { access.Value }) : new AccessSet(null),
                null,
                null,
                null,
                null);

        /// <summary>
        /// Only the registrations this shape's compiled table names. A registration for a stage the shape does not
        /// declare would fail world-registration validation (P-039), so the list is per shape by construction.
        /// </summary>
        private static IReadOnlyList<SystemRegistration> Systems(ScheduleExecutionShape shape)
        {
            if (shape == ScheduleExecutionShape.Playback)
            {
                return new List<SystemRegistration>
                {
                    new ManagedSystemRegistration<ScheduleExecutionProduceSystem>(
                        ScheduleExecutionKeys.ProduceSystem,
                        ScheduleExecutionKeys.ProduceStage,
                        "ScheduleExecutionProduceSystem"),
                    new ManagedSystemRegistration<ScheduleExecutionPlaybackSystem>(
                        ScheduleExecutionKeys.PlaybackSystem,
                        ScheduleExecutionKeys.PlaybackStage,
                        "ScheduleExecutionPlaybackSystem"),
                };
            }

            return new List<SystemRegistration>
            {
                new ManagedSystemRegistration<ScheduleExecutionLeftSystem>(
                    ScheduleExecutionKeys.LeftSystem,
                    ScheduleExecutionKeys.LeftStage,
                    "ScheduleExecutionLeftSystem"),
                new ManagedSystemRegistration<ScheduleExecutionRightSystem>(
                    ScheduleExecutionKeys.RightSystem,
                    ScheduleExecutionKeys.RightStage,
                    "ScheduleExecutionRightSystem"),
                new ManagedSystemRegistration<ScheduleExecutionObserveSystem>(
                    ScheduleExecutionKeys.ObserveSystem,
                    ScheduleExecutionKeys.ObserveStage,
                    "ScheduleExecutionObserveSystem"),
            };
        }
    }
}
