// GameCore.Validation.ProbeHost — the real-Unity-jobs replay variant (GC-023, TEST-022/TEST-023).
//
// Why this file exists. GC-023's first replay accepts "the 10,000-step integer fixture across supported worker
// counts", and the first version of it proved that claim over a deterministic *model* of producer scheduling: the
// worker count decided a managed permutation, and setting `JobsUtility.JobWorkerCount` changed nothing about how the
// fixture's work ran. That is a weaker claim than TEST-022's "shuffled internal producer/completion order and
// supported worker counts", because the scheduler being varied was the fixture's own. The orchestrator review named
// the gap; this file closes it with the real thing:
//
//   * **The producers are real Burst jobs.** `ReplayProducerJob` is an `IJobParallelFor` scheduled with
//     `innerloopBatchCount = 1`, so the ranges spread across whatever worker threads the world actually has.
//   * **They write the bounded storage the runtime really uses.** Each batch's payload is written straight into the
//     `NativeMessageLane`'s bounded byte arena at a range the producing stage reserved first (`TryReservePayload`),
//     which is the runtime's documented native producer path (P-041, 03 s5) - the same path
//     `NativeMessageLanesTests`' `MessagePayloadJob` exercises. A thread histogram records which worker thread
//     executed each batch, so "more than one worker ran producer work" is recorded evidence rather than an
//     assumption.
//   * **The runtime performs the canonical merge and the owner commit.** The committing stage copies the produced
//     values out, publishes rows through the lane's own managed publish path (`TryPublishRow`, which validates
//     declared producer, owner, capacity and reserved payload range), asks the plane for the owner batch
//     (`MergeOwnerBatch`, whose order comes only from the canonical message key) and reduces the merged rows into
//     the owner's authoritative integer state. Publisher order is a *seeded permutation*, so the merge is the thing
//     that has to neutralize scheduling order: `MessageOrderComparer` keys on the host-assigned sequence, the
//     declared ordinal and the stable origin identity - never lane, append or worker index (P-008).
//   * **Everything that could depend on the worker count is compared.** One run per supported worker count, each
//     repeated with two different publish permutations, must produce the same per-step state hashes, the same
//     canonical event identities and the same chain hash (`ReplayStateHash.Chain`, shared with the modeled runner).
//
// What is deliberately *not* claimed: native physics or floating-point lockstep. The producer's arithmetic is
// integral and independent of the thread it runs on, which is what makes "the same state on 1, 2, 4 and 8 workers"
// a statement about ordering rather than about arithmetic.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Replay;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Messages;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Stable identities of the real-jobs replay world, all derived from documented names (P-004).</summary>
    public static class ReplayJobsKeys
    {
        public static readonly WorldDefinitionId WorldDefinition =
            new WorldDefinitionId(FixtureIds.Id("gamecore.validation.replay.jobs.world"));

        public static readonly OwnerId Owner = new OwnerId(FixtureIds.Id("gamecore.validation.replay.jobs.owner"));

        public static readonly RouteId Route = new RouteId(FixtureIds.Id("gamecore.validation.replay.jobs.route"));

        public static readonly BufferId Buffer = new BufferId(FixtureIds.Id("gamecore.validation.replay.jobs.buffer"));

        public static readonly StageId ProduceStage =
            new StageId(FixtureIds.Id("gamecore.validation.replay.jobs.stage.produce"));

        public static readonly StageId CommitStage =
            new StageId(FixtureIds.Id("gamecore.validation.replay.jobs.stage.commit"));

        public static readonly FactoryKey ProducerSystem =
            new FactoryKey(FixtureIds.Id("gamecore.validation.replay.jobs.system.produce"), 1U);

        public static readonly FactoryKey CommitSystem =
            new FactoryKey(FixtureIds.Id("gamecore.validation.replay.jobs.system.commit"), 1U);

        /// <summary>Declared producer key the lane validates every published row against (P-043).</summary>
        public static readonly FactoryKey Producer =
            new FactoryKey(FixtureIds.Id("gamecore.validation.replay.jobs.producer"), 1U);

        /// <summary>Declared message order key of the lane: the canonical merge is keyed, not appended (P-008).</summary>
        public static readonly FactoryKey OrderKey =
            new FactoryKey(FixtureIds.Id("gamecore.validation.replay.jobs.order-key"), 1U);

        public static readonly SchemaRef PayloadSchema =
            new SchemaRef(new SchemaId(FixtureIds.Id("gamecore.validation.replay.jobs.payload")), 1U);

        /// <summary>Stage fence slots: the producer stage, then the committing stage that depends on it.</summary>
        public const int ProduceStageIndex = 0;

        public const int CommitStageIndex = 1;

        public const int StageCount = 2;

        /// <summary>
        /// One `IJobParallelFor` index per worker-batch boundary. A batch size of one means the ranges can spread
        /// across every worker the player has, which is the point: the fixture must be sensitive to worker count.
        /// </summary>
        public const int InnerLoopBatchCount = 1;

        /// <summary>Payload bytes one produced row carries: one big-endian int32 contribution.</summary>
        public const int PayloadBytes = 4;

        /// <summary>Thread-index slots recorded. `JobsUtility.ThreadIndex` is below this on every supported target.</summary>
        public const int ThreadSlots = 64;
    }

    /// <summary>
    /// One declared producer batch: a stable target, a declared ordinal and an integral seed. Every field is
    /// deterministic and independent of the worker count, so any difference between two runs is scheduling.
    /// </summary>
    public readonly struct ReplayProducerBatch
    {
        public ReplayProducerBatch(TargetId target, uint ordinal, int originKey, int seed, int baseValue)
        {
            Target = target;
            Ordinal = ordinal;
            OriginKey = originKey;
            Seed = seed;
            BaseValue = baseValue;
        }

        public TargetId Target { get; }

        /// <summary>Declared ordinal of this producer's contribution to its target within one step (P-008).</summary>
        public uint Ordinal { get; }

        /// <summary>Stable origin identity used by the canonical message key; never a lane or worker index.</summary>
        public int OriginKey { get; }

        /// <summary>Integral seed of the deterministic per-batch work.</summary>
        public int Seed { get; }

        public int BaseValue { get; }

        public override string ToString() =>
            Target.ToString() + "#" + Ordinal.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The real Burst producer. It is `IJobParallelFor` over the declared batches with an inner-loop batch size of
    /// one, it writes into the runtime lane's bounded byte arena (never a scenario-owned container), it computes its
    /// contribution with integral arithmetic so the result cannot depend on which thread ran it, and it records the
    /// thread that executed it.
    /// </summary>
    [BurstCompile]
    public struct ReplayProducerJob : IJobParallelFor
    {
        /// <summary>Main-thread-reserved arena ranges, one per batch (P-041: reserve, then write).</summary>
        [ReadOnly] public NativeArray<int> PayloadOffsets;

        [ReadOnly] public NativeArray<int> Seeds;

        [ReadOnly] public NativeArray<int> BaseValues;

        /// <summary>The runtime lane's bounded payload arena; this is the bounded parallel writer of P-041.</summary>
        public NativeArray<byte> Payload;

        /// <summary>Per-batch contribution the committing stage reduces in canonical order.</summary>
        [WriteOnly] public NativeArray<int> BatchValues;

        /// <summary>Per-thread execution histogram: index is `JobsUtility.ThreadIndex` (P-060 evidence).</summary>
        public NativeArray<int> ThreadExecutions;

        /// <summary>Integral work per batch, so a batch is not so trivial that the scheduler runs it inline.</summary>
        public int WorkIterations;

        /// <summary>Worker thread this batch executed on; recorded, never used for anything semantic (P-008).</summary>
        [NativeSetThreadIndex] public int ThreadIndex;

        public void Execute(int index)
        {
            // Integral, thread-independent work. No floating point, no clock, no managed call: the value a batch
            // contributes cannot depend on the worker that produced it, which is exactly what makes the
            // worker-count comparison a statement about ordering and not about arithmetic.
            int value = BaseValues[index];
            int state = Seeds[index];
            for (int i = 0; i < WorkIterations; i++)
            {
                state = unchecked((state * 31) + i + 1);
            }

            value += state & 0x3FF;

            int offset = PayloadOffsets[index];
            if (offset >= 0 && offset + ReplayJobsKeys.PayloadBytes <= Payload.Length)
            {
                // Big-endian int32, matching the repository's canonical byte order (05 s6).
                Payload[offset] = (byte)(value >> 24);
                Payload[offset + 1] = (byte)(value >> 16);
                Payload[offset + 2] = (byte)(value >> 8);
                Payload[offset + 3] = (byte)value;
            }

            BatchValues[index] = value;

            if (ThreadExecutions.IsCreated && (uint)ThreadIndex < (uint)ThreadExecutions.Length)
            {
                ThreadExecutions[ThreadIndex] = ThreadExecutions[ThreadIndex] + 1;
            }
        }
    }

    /// <summary>The deterministic shape of one real-jobs world: how many targets and producers per target.</summary>
    public readonly struct ReplayJobsShape
    {
        public ReplayJobsShape(int targets, int producersPerTarget, int steps, int workIterations, int stateBound)
        {
            Targets = targets;
            ProducersPerTarget = producersPerTarget;
            Steps = steps;
            WorkIterations = workIterations;
            StateBound = stateBound;
        }

        /// <summary>
        /// The reference shape: 8 targets x 8 producers = 64 batches per step, 64 wake-driven steps, and enough
        /// integral work per batch that the job system really spreads the ranges across the worker threads it has
        /// (a trivial batch can be executed inline by the thread that completes the job, which would make the
        /// multi-thread observation meaningless).
        /// </summary>
        public static ReplayJobsShape Reference =>
            new ReplayJobsShape(targets: 8, producersPerTarget: 8, steps: 64, workIterations: 1024, stateBound: 1000000);

        public int Targets { get; }

        public int ProducersPerTarget { get; }

        public int Steps { get; }

        public int WorkIterations { get; }

        public int StateBound { get; }

        /// <summary>Batches the producer job covers, i.e. the `IJobParallelFor` index count.</summary>
        public int BatchCount => Targets * ProducersPerTarget;

        /// <summary>Stable target identity of one target; derived from a declared name, never from an order (P-004).</summary>
        public TargetId Target(int index) =>
            new TargetId(FixtureIds.Id("gamecore.validation.replay.jobs.target-" + index.ToString(CultureInfo.InvariantCulture)));

        /// <summary>The declared batch declarations; identical for every run of this shape.</summary>
        public IReadOnlyList<ReplayProducerBatch> Batches()
        {
            var batches = new List<ReplayProducerBatch>(BatchCount);
            for (int t = 0; t < Targets; t++)
            {
                TargetId target = Target(t);
                for (int p = 0; p < ProducersPerTarget; p++)
                {
                    batches.Add(new ReplayProducerBatch(
                        target,
                        (uint)p,
                        originKey: t + 1,
                        seed: unchecked((7919 * (t + 1)) + (31 * (p + 1))),
                        baseValue: ((t + 1) * 3) + p));
                }
            }

            return batches.AsReadOnly();
        }

        public string Describe() =>
            "jobs-shape{targets=" + Targets.ToString(CultureInfo.InvariantCulture)
            + ";producersPerTarget=" + ProducersPerTarget.ToString(CultureInfo.InvariantCulture)
            + ";batches=" + BatchCount.ToString(CultureInfo.InvariantCulture)
            + ";steps=" + Steps.ToString(CultureInfo.InvariantCulture)
            + ";workIterations=" + WorkIterations.ToString(CultureInfo.InvariantCulture)
            + ";innerLoopBatch=" + ReplayJobsKeys.InnerLoopBatchCount.ToString(CultureInfo.InvariantCulture) + "}";

        public override string ToString() => Describe();
    }

    /// <summary>
    /// The one observable output of a real-jobs replay: the per-step canonical hashes, the final hashes and the
    /// worker evidence. Two runs with the same shape and different worker counts or publish permutations must agree
    /// on every hash field.
    /// </summary>
    public sealed class ReplayJobsRun
    {
        public ReplayJobsRun(
            int workers,
            int effectiveWorkers,
            uint publishSeed,
            ReplayJobsShape shape,
            IReadOnlyList<ContentHash>? stepHashes,
            ContentHash finalState,
            ContentHash eventHash,
            ContentHash stateChain,
            IReadOnlyList<int>? threadHistogram,
            int jobSchedules,
            int producedRows,
            int rejectedRows,
            int completedSteps,
            int mergeReorderObservations,
            int reorderedStepCount,
            string detail)
        {
            Workers = workers;
            EffectiveWorkers = effectiveWorkers;
            PublishSeed = publishSeed;
            Shape = shape;
            StepHashes = stepHashes ?? Array.Empty<ContentHash>();
            FinalState = finalState;
            EventHash = eventHash;
            StateChain = stateChain;
            ThreadHistogram = threadHistogram ?? Array.Empty<int>();
            JobSchedules = jobSchedules;
            ProducedRows = producedRows;
            RejectedRows = rejectedRows;
            CompletedSteps = completedSteps;
            MergeReorderObservations = mergeReorderObservations;
            ReorderedStepCount = reorderedStepCount;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Requested `JobsUtility.JobWorkerCount`.</summary>
        public int Workers { get; }

        /// <summary>Value read back from `JobsUtility.JobWorkerCount` after the request.</summary>
        public int EffectiveWorkers { get; }

        /// <summary>Seed of the publish permutation this run used, so append order differed between runs.</summary>
        public uint PublishSeed { get; }

        public ReplayJobsShape Shape { get; }

        /// <summary>Canonical state hash of every committed step, in order.</summary>
        public IReadOnlyList<ContentHash> StepHashes { get; }

        public ContentHash FinalState { get; }

        public ContentHash EventHash { get; }

        public ContentHash StateChain { get; }

        /// <summary>Executions per worker thread index; entry 0 is the main thread.</summary>
        public IReadOnlyList<int> ThreadHistogram { get; }

        /// <summary>Producer jobs scheduled, one per committed step.</summary>
        public int JobSchedules { get; }

        public int ProducedRows { get; }

        public int RejectedRows { get; }

        public int CompletedSteps { get; }

        /// <summary>Positions where the canonical merge reordered the append order, summed over all steps.</summary>
        public int MergeReorderObservations { get; }

        /// <summary>Steps whose append order the canonical merge really reordered.</summary>
        public int ReorderedStepCount { get; }

        public string Detail { get; }

        /// <summary>Distinct thread indices that executed at least one producer batch; 1 means no parallelism.</summary>
        public int DistinctThreads
        {
            get
            {
                int count = 0;
                for (int i = 0; i < ThreadHistogram.Count; i++)
                {
                    if (ThreadHistogram[i] != 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        /// <summary>Highest worker thread index observed, or -1 when nothing ran.</summary>
        public int HighestThreadIndex
        {
            get
            {
                for (int i = ThreadHistogram.Count - 1; i >= 0; i--)
                {
                    if (ThreadHistogram[i] != 0)
                    {
                        return i;
                    }
                }

                return -1;
            }
        }

        /// <summary>True when the two runs agree on every canonical hash of the replay.</summary>
        public bool SameHashes(ReplayJobsRun other)
        {
            if (other == null || other.StepHashes.Count != StepHashes.Count)
            {
                return false;
            }

            if (!FinalState.Equals(other.FinalState)
                || !EventHash.Equals(other.EventHash)
                || !StateChain.Equals(other.StateChain))
            {
                return false;
            }

            for (int i = 0; i < StepHashes.Count; i++)
            {
                if (!StepHashes[i].Equals(other.StepHashes[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public string Describe() =>
            "jobs-run{workers=" + Workers.ToString(CultureInfo.InvariantCulture)
            + ";effective=" + EffectiveWorkers.ToString(CultureInfo.InvariantCulture)
            + ";seed=" + PublishSeed.ToString(CultureInfo.InvariantCulture)
            + ";steps=" + CompletedSteps.ToString(CultureInfo.InvariantCulture)
            + ";schedules=" + JobSchedules.ToString(CultureInfo.InvariantCulture)
            + ";rows=" + ProducedRows.ToString(CultureInfo.InvariantCulture)
            + ";rejected=" + RejectedRows.ToString(CultureInfo.InvariantCulture)
            + ";distinctThreads=" + DistinctThreads.ToString(CultureInfo.InvariantCulture)
            + ";reordered=" + ReorderedStepCount.ToString(CultureInfo.InvariantCulture)
            + ";workerThreads=" + WorkerThreadCount().ToString(CultureInfo.InvariantCulture)
            + ";state=" + FinalState.ToHex()
            + ";events=" + EventHash.ToHex()
            + ";chain=" + StateChain.ToHex()
            + (Detail.Length == 0 ? string.Empty : ";" + Detail)
            + "}";

        /// <summary>
        /// Thread indices of worker threads proper: `JobsUtility.ThreadIndex` 0 is the thread that completes, so a
        /// count of *worker* threads excludes it. This is the number that shows the producer work spread.
        /// </summary>
        public int WorkerThreadCount()
        {
            int count = 0;
            for (int i = 1; i < ThreadHistogram.Count; i++)
            {
                if (ThreadHistogram[i] != 0)
                {
                    count++;
                }
            }

            return count;
        }

        public override string ToString() => Describe();
    }

    /// <summary>
    /// Per-world state of the real-jobs replay: the declared batches, the native outputs of the producer job, the
    /// bounded lane the job writes into, the owner's authoritative integer state and the recorded hashes. One
    /// instance exists per live world and it is the only place either system finds its inputs (P-002: no second
    /// authority).
    /// </summary>
    public sealed class ReplayJobsModule : IDisposable
    {
        private static readonly List<ReplayJobsModule> modules = new List<ReplayJobsModule>();

        private readonly UnityWorldHost host;
        private readonly Dictionary<Id128, int> stateByTarget = new Dictionary<Id128, int>();
        private readonly Dictionary<Id128, int> appliedByTarget = new Dictionary<Id128, int>();
        private readonly List<ContentHash> stepHashes = new List<ContentHash>();
        private readonly List<string> eventIdentities = new List<string>();
        private readonly List<StepMessage> publishScratch = new List<StepMessage>();
        private readonly List<StepMessage> mergedScratch = new List<StepMessage>();
        private readonly List<ReplayTargetState> stateScratch = new List<ReplayTargetState>();

        private NativeArray<int> payloadOffsets;
        private NativeArray<int> seeds;
        private NativeArray<int> baseValues;
        private NativeArray<int> batchValues;
        private NativeArray<int> threadExecutions;
        private readonly Id128[] originKeys;
        private bool disposed;

        private ReplayJobsModule(UnityWorldHost host, ReplayJobsShape shape)
        {
            this.host = host;
            Shape = shape;
            Batches = shape.Batches();
            payloadOffsets = new NativeArray<int>(shape.BatchCount, Allocator.Persistent);
            seeds = new NativeArray<int>(shape.BatchCount, Allocator.Persistent);
            baseValues = new NativeArray<int>(shape.BatchCount, Allocator.Persistent);
            batchValues = new NativeArray<int>(shape.BatchCount, Allocator.Persistent);
            threadExecutions = new NativeArray<int>(ReplayJobsKeys.ThreadSlots, Allocator.Persistent);
            originKeys = new Id128[Batches.Count];
            for (int i = 0; i < Batches.Count; i++)
            {
                seeds[i] = Batches[i].Seed;
                baseValues[i] = Batches[i].BaseValue;
                originKeys[i] = FixtureIds.Id(
                    "gamecore.validation.replay.jobs.origin-"
                    + Batches[i].OriginKey.ToString(CultureInfo.InvariantCulture));
            }
        }

        /// <summary>Declared batches, in declaration order; the job's index count.</summary>
        public IReadOnlyList<ReplayProducerBatch> Batches { get; }

        public ReplayJobsShape Shape { get; }

        /// <summary>Producer jobs this module scheduled; one per committed step.</summary>
        public int JobSchedules { get; private set; }

        /// <summary>Rows the runtime lane accepted from the producer job.</summary>
        public int ProducedRows { get; private set; }

        /// <summary>Rows the runtime lane refused; any non-zero value is a defect (P-043).</summary>
        public int RejectedRows { get; private set; }

        /// <summary>Steps the committing stage reduced.</summary>
        public int CompletedSteps { get; private set; }

        /// <summary>
        /// Positions across all steps where the canonical merge order differed from the publish (append) order.
        /// Non-zero is what makes the comparison meaningful: it is the evidence that the merge, not the producer,
        /// decided the owner's batch order (P-008, 03 s5).
        /// </summary>
        public int MergeReorderObservations { get; private set; }

        /// <summary>Steps whose append order the canonical merge really reordered.</summary>
        public int ReorderedStepCount { get; private set; }

        /// <summary>Per-step canonical state hash, in commit order.</summary>
        public IReadOnlyList<ContentHash> StepHashes => stepHashes;

        public static ReplayJobsModule Attach(UnityWorldHost host, ReplayJobsShape shape)
        {
            var module = new ReplayJobsModule(host, shape);
            modules.Add(module);
            return module;
        }

        public static ReplayJobsModule? Of(World world)
        {
            for (int i = 0; i < modules.Count; i++)
            {
                if (modules[i].host.EntityWorld == world)
                {
                    return modules[i];
                }
            }

            return null;
        }

        public static void DetachAll()
        {
            for (int i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Dispose();
            }

            modules.Clear();
        }

        /// <summary>The world's declared lane, or null when the plane was not created.</summary>
        private NativeMessageLane? Lane
        {
            get
            {
                WorldMessagePlane? plane = host.Messages;
                if (plane == null)
                {
                    return null;
                }

                return plane.Lanes.TryGetLane(ReplayJobsKeys.Buffer, out NativeMessageLane? lane) ? lane : null;
            }
        }

        /// <summary>
        /// The producing stage: reserve one bounded arena range per batch through the lane's own reservation path,
        /// then schedule the real parallel producer job over those ranges and hand its handle to the step fence.
        /// </summary>
        public bool TryScheduleProducer(
            LogicalStepId step,
            AssemblyEpoch epoch,
            JobHandle inputDependency,
            out JobHandle output,
            out string failure)
        {
            output = default(JobHandle);
            NativeMessageLane? lane = Lane;
            if (lane == null)
            {
                failure = "the world has no declared lane for " + ReplayJobsKeys.Buffer.ToString();
                return false;
            }

            for (int i = 0; i < Batches.Count; i++)
            {
                BufferAppendOutcome reserved = lane.TryReservePayload(
                    ReplayJobsKeys.PayloadBytes, out int offset, out string reserveDetail);
                if (reserved != BufferAppendOutcome.Accepted)
                {
                    // A reliable lane at its declared bound refuses before any mutation (P-043).
                    failure = reserveDetail;
                    return false;
                }

                payloadOffsets[i] = offset;
            }

            var job = new ReplayProducerJob
            {
                PayloadOffsets = payloadOffsets,
                Seeds = seeds,
                BaseValues = baseValues,
                Payload = lane.Payload,
                BatchValues = batchValues,
                ThreadExecutions = threadExecutions,
                WorkIterations = Shape.WorkIterations,
            };

            // innerloopBatchCount = 1: the ranges may spread across every worker the world actually has, which is
            // what makes this fixture sensitive to `JobsUtility.JobWorkerCount` instead of to a model of it.
            output = job.ScheduleParallel(Batches.Count, ReplayJobsKeys.InnerLoopBatchCount, inputDependency);
            lane.TrackPayloadWriter(output);

            // Flush the batch now so the worker threads can pick ranges up before this step's fence completes.
            JobsUtility.ScheduleBatchedJobs();
            JobSchedules++;
            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// The committing stage: publish the produced rows through the lane's own publish path in a seeded
        /// permutation, take the owner batch from the plane's canonical merge, reduce it into the owner's integer
        /// state and release the lanes for the next step.
        /// </summary>
        public bool TryCommit(LogicalStepId step, AssemblyEpoch epoch, uint publishSeed, out string failure)
        {
            NativeMessageLane? lane = Lane;
            WorldMessagePlane? plane = host.Messages;
            if (lane == null || plane == null)
            {
                failure = "the world has no declared lane or plane";
                return false;
            }

            if (lane.Capacity < Batches.Count)
            {
                failure = "the lane declares room for " + lane.Capacity.ToString(CultureInfo.InvariantCulture)
                    + " row(s) but the shape produces " + Batches.Count.ToString(CultureInfo.InvariantCulture);
                return false;
            }

            int rowCount = lane.RowCount;
            if (rowCount != 0)
            {
                failure = "the lane already held " + rowCount.ToString(CultureInfo.InvariantCulture)
                    + " unconsumed row(s) at the start of the committing stage";
                return false;
            }

            // The lane's row storage is written by the producing stage; copy the produced values out before
            // publishing, because publishing advances the lane's own cursor through the same storage.
            publishScratch.Clear();
            for (int i = 0; i < Batches.Count; i++)
            {
                int offset = payloadOffsets[i];
                int value = batchValues[i];
                var row = new StepMessage(
                    step,
                    epoch,
                    default(OperationId),
                    ReplayJobsKeys.Route,
                    ReplayJobsKeys.Owner,
                    Batches[i].Target,
                    ReplayJobsKeys.PayloadSchema,
                    MessageKind.Receipt,
                    new MessageOrderKey(AdmissionSequence.Zero, Batches[i].Ordinal, originKeys[i]),
                    ReplayJobsKeys.Producer,
                    offset,
                    ReplayJobsKeys.PayloadBytes);
                publishScratch.Add(row);

                // The arena value the job wrote must be the value the committing stage will reduce: this is the
                // payload's own consistency check, read back through the lane's bounded arena (P-041).
                byte[] written = lane.PayloadOf(row);
                if (written.Length != ReplayJobsKeys.PayloadBytes || ReadBigEndianInt32(written) != value)
                {
                    failure = "batch " + i.ToString(CultureInfo.InvariantCulture)
                        + " published value " + value.ToString(CultureInfo.InvariantCulture)
                        + " but the arena holds " + (written.Length == ReplayJobsKeys.PayloadBytes
                            ? ReadBigEndianInt32(written).ToString(CultureInfo.InvariantCulture)
                            : "<" + written.Length.ToString(CultureInfo.InvariantCulture) + " bytes>");
                    return false;
                }
            }

            // Publish in a seeded permutation. Append order therefore differs between runs while the canonical
            // merge key does not, so what the comparison proves is exactly that the merge - not the producer, the
            // lane or the worker - decides the owner's batch order (P-008, 03 s5).
            int[] order = WorkerSchedule.CompletionOrder(Batches.Count, publishSeed);
            for (int i = 0; i < order.Length; i++)
            {
                BufferAppendOutcome published = lane.TryPublishRow(publishScratch[order[i]], out string publishDetail);
                if (published != BufferAppendOutcome.Accepted)
                {
                    failure = "the lane refused row " + order[i].ToString(CultureInfo.InvariantCulture)
                        + ": " + publishDetail;
                    return false;
                }

                ProducedRows++;
            }

            // The lane is the authority on its own refusals (P-043: a reliable overflow is reported, never dropped).
            RejectedRows = lane.RejectedCount;

            // The runtime's canonical merge: the owner's step batch in the order the canonical message key defines.
            IReadOnlyList<StepMessage> merged = plane.MergeOwnerBatch(ReplayJobsKeys.Owner);
            mergedScratch.Clear();
            for (int i = 0; i < merged.Count; i++)
            {
                mergedScratch.Add(merged[i]);
            }

            // How much work the merge had to do: publish position i held declaration row `order[i]`, so a position
            // where the merged row is a different one is a row the merge had to move (P-008's evidence).
            int reordered = 0;
            for (int i = 0; i < mergedScratch.Count && i < order.Length; i++)
            {
                if (!mergedScratch[i].Order.Equals(publishScratch[order[i]].Order))
                {
                    reordered++;
                }
            }

            MergeReorderObservations += reordered;
            if (reordered != 0)
            {
                ReorderedStepCount++;
            }

            if (mergedScratch.Count != Batches.Count)
            {
                failure = "the canonical merge returned " + mergedScratch.Count.ToString(CultureInfo.InvariantCulture)
                    + " row(s) for " + Batches.Count.ToString(CultureInfo.InvariantCulture) + " produced";
                return false;
            }

            for (int i = 0; i < mergedScratch.Count; i++)
            {
                StepMessage row = mergedScratch[i];
                int contribution = ReadBigEndianInt32(lane.PayloadOf(row));
                stateByTarget.TryGetValue(row.Target.Value, out int current);
                appliedByTarget.TryGetValue(row.Target.Value, out int applied);
                int next = current + contribution;
                if (Shape.StateBound > 0 && next > Shape.StateBound)
                {
                    next = Shape.StateBound;
                }

                stateByTarget[row.Target.Value] = next;
                appliedByTarget[row.Target.Value] = applied + 1;
                if (next != current)
                {
                    eventIdentities.Add(ReplayStateHash.EventIdentityText(
                        row.Target, ReplayJobsKeys.PayloadSchema, step, epoch, next));
                }
            }

            stateScratch.Clear();
            for (int t = 0; t < Shape.Targets; t++)
            {
                TargetId target = Shape.Target(t);
                stateByTarget.TryGetValue(target.Value, out int value);
                appliedByTarget.TryGetValue(target.Value, out int applied);
                stateScratch.Add(new ReplayTargetState(target, value, applied));
            }

            // The state table is walked in canonical target order (the loop above), so the per-step hash is a
            // function of what was committed and not of the order the merge happened to hand the rows over in.
            stepHashes.Add(ReplayStateHash.IntegerStateHash(stateScratch));
            CompletedSteps++;

            // The owner consumed its step batch, so the lane starts the next step empty (P-037, P-043).
            plane.ReleaseConsumed(ReplayJobsKeys.Owner);

            failure = string.Empty;
            return true;
        }

        /// <summary>The run's final canonical hashes, computed from what the module committed.</summary>
        public ReplayJobsRun Finish(int workers, int effectiveWorkers, uint publishSeed, string detail)
        {
            stateScratch.Clear();
            for (int t = 0; t < Shape.Targets; t++)
            {
                TargetId target = Shape.Target(t);
                stateByTarget.TryGetValue(target.Value, out int value);
                appliedByTarget.TryGetValue(target.Value, out int applied);
                stateScratch.Add(new ReplayTargetState(target, value, applied));
            }

            var histogram = new int[threadExecutions.Length];
            for (int i = 0; i < histogram.Length; i++)
            {
                histogram[i] = threadExecutions[i];
            }

            return new ReplayJobsRun(
                workers,
                effectiveWorkers,
                publishSeed,
                Shape,
                stepHashes.ToArray(),
                ReplayStateHash.IntegerStateHash(stateScratch),
                ReplayStateHash.EventHash(eventIdentities),
                ReplayStateHash.Chain(stepHashes),
                histogram,
                JobSchedules,
                ProducedRows,
                RejectedRows,
                CompletedSteps,
                MergeReorderObservations,
                ReorderedStepCount,
                detail);
        }

        /// <summary>Reads one canonical big-endian int32, the form the producer job wrote (05 s6).</summary>
        private static int ReadBigEndianInt32(byte[] bytes) =>
            (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (payloadOffsets.IsCreated)
            {
                payloadOffsets.Dispose();
            }

            if (seeds.IsCreated)
            {
                seeds.Dispose();
            }

            if (baseValues.IsCreated)
            {
                baseValues.Dispose();
            }

            if (batchValues.IsCreated)
            {
                batchValues.Dispose();
            }

            if (threadExecutions.IsCreated)
            {
                threadExecutions.Dispose();
            }
        }
    }

    /// <summary>
    /// The producing stage's system: schedules the real parallel producer job that writes the bounded lane's arena.
    /// It schedules and hands the handle to the step fence; it reads no result, because reading one before the
    /// committing stage's fence would be exactly the access-after-release P-041 forbids.
    /// </summary>
    public partial class ReplayProducerSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            ReplayJobsModule? module = ReplayJobsModule.Of(World);
            UnityWorldHost? host = ReplayJobsHostLookup.HostOf(World);
            if (module == null || host == null)
            {
                return;
            }

            // The step being executed and its epoch come from the world's own authority, so a produced row is
            // stamped with the step it really belongs to (P-002, P-006).
            if (!module.TryScheduleProducer(
                host.CurrentStep,
                host.CurrentEpoch,
                Dependency,
                out JobHandle handle,
                out string failure))
            {
                throw new InvalidOperationException("the producer stage refused to schedule: " + failure);
            }

            Dependency = handle;
        }
    }

    /// <summary>
    /// The committing stage's system: completes the producer's handle, publishes the produced rows through the
    /// lane's own publish path, takes the canonical owner batch from the plane and reduces it into the owner's
    /// integer state.
    /// </summary>
    public partial class ReplayCommitSystem : SystemBase
    {
        /// <summary>
        /// Seed of the publish permutation this step uses. It is derived from the *step* rather than from a static
        /// field, so two runs of the same record publish the same permutation at the same step and the comparison
        /// isolates the worker count from the append order. `ReplayJobsRunner` drives it, and the default keeps a
        /// bare dispatch of this system meaningful.
        /// </summary>
        public uint PublishSeed { get; set; } = 977U;

        protected override void OnUpdate()
        {
            ReplayJobsModule? module = ReplayJobsModule.Of(World);
            UnityWorldHost? host = ReplayJobsHostLookup.HostOf(World);
            if (module == null || host == null)
            {
                return;
            }

            // The producer's handle travels in this system's dependency through the stage edge the registration
            // declares (P-041: a consumer cannot read an unfinished producer). Completing here is what makes the
            // arena, the batch values and the thread histogram safe to read on the main thread.
            Dependency.Complete();

            uint seed = unchecked(PublishSeed + (uint)host.CurrentStep.Value);
            if (!module.TryCommit(host.CurrentStep, host.CurrentEpoch, seed, out string failure))
            {
                throw new InvalidOperationException("the committing stage refused the step: " + failure);
            }
        }
    }

    /// <summary>Host lookup shared by the two systems; a world with no owned host is not this scenario's world.</summary>
    internal static class ReplayJobsHostLookup
    {
        internal static UnityWorldHost? HostOf(World world) =>
            UnityWorldRegistry.TryGetByEntityWorld(world, out UnityWorldHost? host) ? host : null;
    }

    /// <summary>
    /// The real-jobs world's composition root: two stages, one system each, the committing stage depending on the
    /// producing stage, and a message plane declaring the one bounded lane the producer job writes into.
    /// </summary>
    public static class ReplayJobsRegistration
    {
        public const string WorldName = "GameCoreValidationReplayJobsWorld";

        /// <summary>The declared lane: reliable, one step, one owner, one consumer stage, small bounded capacity.</summary>
        public static MessagePlaneRegistration Messages(int batchCount)
        {
            var buffer = new MessageBufferDescriptor(
                ReplayJobsKeys.Buffer,
                ReplayJobsKeys.PayloadSchema,
                new[] { ReplayJobsKeys.Producer },
                ReplayJobsKeys.Owner,
                ReplayJobsKeys.CommitStage,
                ReplayJobsKeys.CommitStage,
                ReplayJobsKeys.OrderKey,
                BufferLifetime.Step,
                capacity: batchCount,
                byteCapacity: batchCount * ReplayJobsKeys.PayloadBytes,
                BufferOverflowPolicy.RejectBeforeMutation,
                BufferCancellationPolicy.Drain);

            return new MessagePlaneRegistration(
                null,
                new List<MessageBufferDescriptor> { buffer },
                null,
                maxPendingRequests: 4,
                maxRetainedResults: 4,
                maxRetainedEvents: 8,
                maxEventsPerStep: batchCount,
                nextStepCapacity: 2);
        }

        public static UnityWorldRegistration Create(int batchCount)
        {
            var stages = new List<StageRegistration>
            {
                new StageRegistration(ReplayJobsKeys.ProduceStage, "replay.jobs.produce", ReplayJobsKeys.ProduceStageIndex, null),
                new StageRegistration(
                    ReplayJobsKeys.CommitStage,
                    "replay.jobs.commit",
                    ReplayJobsKeys.CommitStageIndex,
                    new[] { ReplayJobsKeys.ProduceStageIndex }),
            };

            var systems = new List<SystemRegistration>
            {
                new ManagedSystemRegistration<ReplayProducerSystem>(
                    ReplayJobsKeys.ProducerSystem, ReplayJobsKeys.ProduceStage, "ReplayProducerSystem"),
                new ManagedSystemRegistration<ReplayCommitSystem>(
                    ReplayJobsKeys.CommitSystem, ReplayJobsKeys.CommitStage, "ReplayCommitSystem"),
            };

            var entries = new List<GuardedDispatchEntry>
            {
                new GuardedDispatchEntry(
                    ReplayJobsKeys.ProduceStage,
                    ReplayJobsKeys.ProducerSystem,
                    SystemDispatchKind.ManagedSystem,
                    0,
                    ReplayJobsKeys.ProduceStageIndex,
                    null),
                new GuardedDispatchEntry(
                    ReplayJobsKeys.CommitStage,
                    ReplayJobsKeys.CommitSystem,
                    SystemDispatchKind.ManagedSystem,
                    1,
                    ReplayJobsKeys.CommitStageIndex,
                    new[] { ReplayJobsKeys.ProduceStageIndex }),
            };

            var stepPlan = new GuardedDispatchPlan(entries, null, ReplayJobsKeys.StageCount);

            return new UnityWorldRegistration(
                WorldName,
                stages,
                systems,
                GuardedDispatchPlan.Empty,
                stepPlan,
                GuardedDispatchPlan.Empty,
                null,
                Messages(batchCount),
                null);
        }
    }

    /// <summary>
    /// Drives the real-jobs replay: one owned world, one step per registered wake, the real Burst producer job
    /// writing the runtime's bounded lane, the runtime's canonical merge and the owner commit. Everything the
    /// worker count could influence is hashed, so two runs at different worker counts are compared by value rather
    /// than by inspection.
    /// </summary>
    public static class ReplayJobsRunner
    {
        /// <summary>Host ticks of one pump frame; the world is command-driven, so the delta is not simulation time.</summary>
        private const ulong HostTick = 1_000_000UL;

        /// <summary>
        /// Runs one replay at the requested `JobsUtility.JobWorkerCount` and returns its canonical hashes plus the
        /// worker evidence. The original worker count is restored before returning, whatever happens.
        /// </summary>
        public static ReplayJobsRun Run(int workers, uint publishSeed, ReplayJobsShape? shape = null, int worldOrdinal = 0)
        {
            ReplayJobsShape effectiveShape = shape ?? ReplayJobsShape.Reference;
            var session = new WorldId(new Id128(0x47433032334A4F42UL, 0x0102030405060708UL + (ulong)worldOrdinal));
            int originalWorkers = JobsUtility.JobWorkerCount;
            UnityWorldHost? host = null;
            ReplayJobsModule? module = null;
            int effectiveWorkers = originalWorkers;
            var detail = new StringBuilder();
            try
            {
                JobsUtility.JobWorkerCount = workers;
                effectiveWorkers = JobsUtility.JobWorkerCount;

                bool created = UnityWorldRegistry.TryCreate(
                    new WorldCreateRequest(
                        session,
                        ReplayJobsKeys.WorldDefinition,
                        TemporalModel.CommandDriven,
                        PropagationMode.Automatic,
                        ContentHash.Empty,
                        new OperationId(session, FixtureIds.Id("gamecore.validation.replay.jobs.issuer"), 1UL),
                        null),
                    ReplayJobsRegistration.Create(effectiveShape.BatchCount),
                    out host,
                    out WorldCreateResult createResult);

                if (!created || host == null)
                {
                    return new ReplayJobsRun(
                        workers, effectiveWorkers, publishSeed, effectiveShape,
                        Array.Empty<ContentHash>(), ContentHash.Empty, ContentHash.Empty, ContentHash.Empty,
                        new int[ReplayJobsKeys.ThreadSlots], 0, 0, 0, 0, 0, 0,
                        "the world was not created: " + createResult.Code + " " + createResult.Detail);
                }

                module = ReplayJobsModule.Attach(host, effectiveShape);

                // The publish permutation is a property of the run, not of the world's shape, so two runs of the
                // same worker count with different seeds append the same rows in different orders; the canonical
                // merge is what has to neutralize that.
                ISystemDispatchCatalog catalog = host.Systems;
                if (catalog.TryResolve(ReplayJobsKeys.CommitSystem, out SystemDispatchTarget target)
                    && target.ManagedSystem is ReplayCommitSystem commitSystem)
                {
                    commitSystem.PublishSeed = publishSeed;
                }
                else
                {
                    detail.Append("the committing system was not resolvable; the publish seed was not applied;");
                }

                for (int step = 0; step < effectiveShape.Steps; step++)
                {
                    LogicalStepId expected = new LogicalStepId((ulong)step + 1UL);
                    host.RequestWake(1U);
                    WorldPumpResult pump = host.PumpFrame(HostTick * (ulong)(step + 2));
                    if (pump.Advance == null || pump.Advance.Outcome != Outcome.Published)
                    {
                        detail.Append("step").Append(step).Append(":no-publish(")
                            .Append(pump.Advance == null ? "no-advance" : pump.Advance.Outcome.ToString()).Append(");");
                        break;
                    }

                    if (!host.CurrentStep.Equals(expected))
                    {
                        detail.Append("step").Append(step).Append(":wrong-step(")
                            .Append(host.CurrentStep.Value.ToString(CultureInfo.InvariantCulture)).Append(");");
                        break;
                    }
                }

                ReplayJobsRun run = module.Finish(
                    workers, effectiveWorkers, publishSeed, detail.ToString());
                detail.Clear();
                return run;
            }
            catch (Exception exception)
            {
                detail.Append("unhandled ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
                return new ReplayJobsRun(
                    workers, effectiveWorkers, publishSeed, effectiveShape,
                    Array.Empty<ContentHash>(), ContentHash.Empty, ContentHash.Empty, ContentHash.Empty,
                    new int[ReplayJobsKeys.ThreadSlots], 0, 0, 0, 0, 0, 0,
                    detail.ToString());
            }
            finally
            {
                JobsUtility.JobWorkerCount = originalWorkers;
                if (module != null)
                {
                    module.Dispose();
                }

                ReplayJobsModule.DetachAll();
                if (host != null)
                {
                    try
                    {
                        host.Stop(
                            new OperationId(session, FixtureIds.Id("gamecore.validation.replay.jobs.issuer"), 2UL),
                            "GC-023 real-jobs replay teardown");
                    }
                    catch (Exception)
                    {
                        // The world's own lifecycle reports a teardown failure; it must not mask the replay result.
                    }
                }

                UnityWorldRegistry.Remove(session);
            }
        }
    }
}
