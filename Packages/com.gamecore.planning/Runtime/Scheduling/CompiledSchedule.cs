// GameCore.Planning - semantic schedule compilation (GC-009).
// Normative sources: docs/game-core/03-runtime-and-execution.md section 3, docs/game-core/04-unity-integration.md
// section 4, docs/game-core/00-core-protocols.md P-040, P-041, P-043.
// This is the compiled schedule the Unity adapter consumes: a stable topological order with explicit stage fences,
// buffer bindings and deferred playback points. It is data; the compiler has already validated it.
// Unity-free: BCL subset only, no UnityEngine/Unity.* reference (01 section 1).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning.Scheduling
{
    /// <summary>What an ordered stage edge expresses (P-039 declared edges, P-043 producer/consumer buffer edges).</summary>
    public enum ScheduleEdgeKind
    {
        /// <summary>A declared required/optional stage dependency named by a <see cref="StageSpec"/>.</summary>
        Declared = 0,

        /// <summary>A producer-before-consumer edge derived from a declared buffer contract.</summary>
        Buffer = 1,
    }

    /// <summary>
    /// One ordered stage edge of the compiled schedule, named by fence indices so the runtime table needs no
    /// name lookup (P-040, 04 section 4).
    /// </summary>
    public sealed class ScheduleStageEdge
    {
        public ScheduleStageEdge(int fromIndex, int toIndex, StageId from, StageId to, ScheduleEdgeKind kind, bool required)
        {
            FromIndex = fromIndex;
            ToIndex = toIndex;
            From = from;
            To = to;
            Kind = kind;
            Required = required;
        }

        /// <summary>Fence index of the stage whose completion precedes the other stage (P-041).</summary>
        public int FromIndex { get; }

        public int ToIndex { get; }

        public StageId From { get; }

        public StageId To { get; }

        public ScheduleEdgeKind Kind { get; }

        /// <summary>False for an optional declared edge; a required edge that vanished would have been rejected.</summary>
        public bool Required { get; }

        public override string ToString()
        {
            return (Kind == ScheduleEdgeKind.Buffer ? "buffer:" : "declared:")
                + FromIndex.ToString(CultureInfo.InvariantCulture) + "->" + ToIndex.ToString(CultureInfo.InvariantCulture)
                + (Required ? string.Empty : "?");
        }
    }

    /// <summary>
    /// One system entry in the compiled dispatch order. Keys and indices only: the generated registry resolves the
    /// key to its concrete system instance at the assembly fence (04 sections 4 and 8).
    /// </summary>
    public sealed class ScheduleEntry
    {
        public ScheduleEntry(
            int dispatchIndex,
            int stageIndex,
            StageId stage,
            FactoryKey systemKey,
            SystemMultiplicity multiplicity,
            AccessSet access,
            IReadOnlyList<FactoryKey>? predecessorSystems,
            IReadOnlyList<int>? predecessorStages)
        {
            DispatchIndex = dispatchIndex;
            StageIndex = stageIndex;
            Stage = stage;
            SystemKey = systemKey;
            Multiplicity = multiplicity;
            Access = access ?? throw new ArgumentNullException(nameof(access));
            PredecessorSystems = ContractCollections.Freeze(predecessorSystems);
            PredecessorStages = ContractCollections.Freeze(predecessorStages);
        }

        /// <summary>Position in the compiled order; ascending execution order (P-040).</summary>
        public int DispatchIndex { get; }

        /// <summary>Index into the host-owned native fence table; the stage's fence slot (P-041).</summary>
        public int StageIndex { get; }

        public StageId Stage { get; }

        public FactoryKey SystemKey { get; }

        public SystemMultiplicity Multiplicity { get; }

        /// <summary>This system's own read/write set, which entered validation and the plan hash (P-039, P-040).</summary>
        public AccessSet Access { get; }

        /// <summary>Systems of the same stage that must complete before this entry, in canonical key order.</summary>
        public IReadOnlyList<FactoryKey> PredecessorSystems { get; }

        /// <summary>Stages whose completed fences are incoming edges for this entry, ascending (P-041).</summary>
        public IReadOnlyList<int> PredecessorStages { get; }

        public override string ToString()
        {
            return DispatchIndex.ToString(CultureInfo.InvariantCulture) + ":" + Stage.ToString() + "/" + SystemKey.ToString();
        }
    }

    /// <summary>
    /// A deferred structural playback point: a declared buffer's writes play back after its producers finish and
    /// before its dependent readers run (P-041, P-043). It is not a dispatchable system: in the Unity adapter it
    /// attaches to the owning stage's fence, so a consumer waits for the producer handle through the stage table.
    /// </summary>
    public sealed class SchedulePlaybackPoint
    {
        public SchedulePlaybackPoint(
            int playbackIndex,
            BufferId buffer,
            SchemaRef schema,
            FactoryKey orderKey,
            BufferLifetime lifetime,
            BufferOverflowPolicy overflow,
            BufferCancellationPolicy cancellation,
            int capacity,
            StageId ownerStage,
            int ownerStageIndex,
            StageId consumerStage,
            int consumerStageIndex,
            IReadOnlyList<int>? producerStageIndexes,
            IReadOnlyList<FactoryKey>? producerSystems,
            IReadOnlyList<FactoryKey>? consumerSystems)
        {
            PlaybackIndex = playbackIndex;
            Buffer = buffer;
            Schema = schema;
            OrderKey = orderKey;
            Lifetime = lifetime;
            Overflow = overflow;
            Cancellation = cancellation;
            Capacity = capacity;
            OwnerStage = ownerStage;
            OwnerStageIndex = ownerStageIndex;
            ConsumerStage = consumerStage;
            ConsumerStageIndex = consumerStageIndex;
            ProducerStageIndexes = ContractCollections.Freeze(producerStageIndexes);
            ProducerSystems = ContractCollections.Freeze(producerSystems);
            ConsumerSystems = ContractCollections.Freeze(consumerSystems);
        }

        /// <summary>Canonical position of the playback point; ascending owner-stage index then buffer id.</summary>
        public int PlaybackIndex { get; }

        public BufferId Buffer { get; }

        public SchemaRef Schema { get; }

        /// <summary>Declared semantic order key: parallel playback uses it instead of worker order (P-041, 03 s5).</summary>
        public FactoryKey OrderKey { get; }

        public BufferLifetime Lifetime { get; }

        public BufferOverflowPolicy Overflow { get; }

        public BufferCancellationPolicy Cancellation { get; }

        public int Capacity { get; }

        /// <summary>The stage whose local deferred buffer is played back; its fence precedes the consumers (P-041).</summary>
        public StageId OwnerStage { get; }

        public int OwnerStageIndex { get; }

        public StageId ConsumerStage { get; }

        public int ConsumerStageIndex { get; }

        /// <summary>Fence indices of the stages that produce this buffer, ascending.</summary>
        public IReadOnlyList<int> ProducerStageIndexes { get; }

        public IReadOnlyList<FactoryKey> ProducerSystems { get; }

        public IReadOnlyList<FactoryKey> ConsumerSystems { get; }

        public bool IsStageLocal => OwnerStageIndex == ConsumerStageIndex;

        public override string ToString()
        {
            return "playback[" + PlaybackIndex.ToString(CultureInfo.InvariantCulture) + "]:" + Buffer.ToString()
                + " owner=" + OwnerStageIndex.ToString(CultureInfo.InvariantCulture)
                + " consumer=" + ConsumerStageIndex.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// One stage of the compiled schedule, at its canonical fence index. A stage keeps its declared identity,
    /// access set and incoming edges; it never acquires a universal position relative to unrelated plugins (P-039).
    /// </summary>
    public sealed class ScheduleStage
    {
        public ScheduleStage(
            int stageIndex,
            StageId stage,
            uint version,
            Id128 ownerPackage,
            HostAffinity affinity,
            AccessSet access,
            IReadOnlyList<FactoryKey>? factoryKeys,
            IReadOnlyList<Id128>? activationMemberships,
            IReadOnlyList<int>? predecessorStages,
            IReadOnlyList<ScheduleEntry>? systems,
            IReadOnlyList<SchedulePlaybackPoint>? playbackPoints)
        {
            StageIndex = stageIndex;
            Stage = stage;
            Version = version;
            OwnerPackage = ownerPackage;
            Affinity = affinity;
            Access = access ?? throw new ArgumentNullException(nameof(access));
            FactoryKeys = ContractCollections.Freeze(factoryKeys);
            ActivationMemberships = ContractCollections.Freeze(activationMemberships);
            PredecessorStages = ContractCollections.Freeze(predecessorStages);
            Systems = ContractCollections.Freeze(systems);
            PlaybackPoints = ContractCollections.Freeze(playbackPoints);
        }

        /// <summary>Fence index; ascending dispatch order of stages is exactly ascending index (P-040).</summary>
        public int StageIndex { get; }

        public StageId Stage { get; }

        public uint Version { get; }

        public Id128 OwnerPackage { get; }

        public HostAffinity Affinity { get; }

        /// <summary>The stage's declared read/write summary; its systems' sets enter validation individually.</summary>
        public AccessSet Access { get; }

        public IReadOnlyList<FactoryKey> FactoryKeys { get; }

        public IReadOnlyList<Id128> ActivationMemberships { get; }

        /// <summary>Stages whose completed fences are incoming edges for every entry of this stage, ascending.</summary>
        public IReadOnlyList<int> PredecessorStages { get; }

        /// <summary>This stage's system entries in canonical inner order (P-040).</summary>
        public IReadOnlyList<ScheduleEntry> Systems { get; }

        /// <summary>Playback points whose deferred buffer is owned (and played back) by this stage (P-041).</summary>
        public IReadOnlyList<SchedulePlaybackPoint> PlaybackPoints { get; }

        public override string ToString()
        {
            return StageIndex.ToString(CultureInfo.InvariantCulture) + ":" + Stage.ToString()
                + "(" + Systems.Count.ToString(CultureInfo.InvariantCulture) + " systems)";
        }
    }

    /// <summary>
    /// The compiled execution schedule: one canonical topological order, its stage fences, its flattened dispatch
    /// order, its buffer bindings and its deferred playback points. This is what a dispatch table is installed from
    /// (04 section 4) and what the plan hash covers (P-040).
    /// </summary>
    public sealed class CompiledSchedule
    {
        public CompiledSchedule(
            IReadOnlyList<ScheduleStage>? stages,
            IReadOnlyList<ScheduleEntry>? entries,
            IReadOnlyList<ScheduleStageEdge>? edges,
            IReadOnlyList<SchedulePlaybackPoint>? playbackPoints,
            IReadOnlyList<BufferBinding>? bufferBindings,
            ExecutionPlan plan)
        {
            Stages = ContractCollections.Freeze(stages);
            Entries = ContractCollections.Freeze(entries);
            Edges = ContractCollections.Freeze(edges);
            PlaybackPoints = ContractCollections.Freeze(playbackPoints);
            BufferBindings = ContractCollections.Freeze(bufferBindings);
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }

        /// <summary>Stages in canonical order; index into this list is the stage's fence index (P-040).</summary>
        public IReadOnlyList<ScheduleStage> Stages { get; }

        /// <summary>Flattened canonical dispatch order across every stage (P-040).</summary>
        public IReadOnlyList<ScheduleEntry> Entries { get; }

        /// <summary>Ordered stage edges, declared and buffer-derived, in canonical order.</summary>
        public IReadOnlyList<ScheduleStageEdge> Edges { get; }

        /// <summary>Deferred playback points, in canonical order (owner stage index, then buffer id).</summary>
        public IReadOnlyList<SchedulePlaybackPoint> PlaybackPoints { get; }

        /// <summary>Buffer contracts bound to this schedule, for commit-time drain validation (P-043, O-16).</summary>
        public IReadOnlyList<BufferBinding> BufferBindings { get; }

        /// <summary>Contract-shaped DAG (stage and system nodes plus edges) with the semantic plan hash (P-040).</summary>
        public ExecutionPlan Plan { get; }

        /// <summary>Hash over every semantic input: declarations, access sets, edges, buffers and playback points.</summary>
        public ContentHash Hash => Plan.PlanHash;

        public int StageCount => Stages.Count;

        public int SystemCount => Entries.Count;

        public bool IsEmpty => Stages.Count == 0;

        /// <summary>Fence index of one declared stage; false when that stage is not part of this schedule.</summary>
        public bool TryGetStageIndex(StageId stage, out int stageIndex)
        {
            for (int i = 0; i < Stages.Count; i++)
            {
                if (Stages[i].Stage.Equals(stage))
                {
                    stageIndex = Stages[i].StageIndex;
                    return true;
                }
            }

            stageIndex = -1;
            return false;
        }

        /// <summary>True when one ordered stage edge runs between these two fence indices (P-040).</summary>
        public bool HasEdge(int fromStage, int toStage)
        {
            for (int i = 0; i < Edges.Count; i++)
            {
                if (Edges[i].FromIndex == fromStage && Edges[i].ToIndex == toStage)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Entries of one fence index in canonical inner order; empty for an out-of-range index.</summary>
        public IReadOnlyList<ScheduleEntry> EntriesOfStage(int stageIndex)
        {
            for (int i = 0; i < Stages.Count; i++)
            {
                if (Stages[i].StageIndex == stageIndex)
                {
                    return Stages[i].Systems;
                }
            }

            return Array.Empty<ScheduleEntry>();
        }

        /// <summary>Playback points owned by one fence index, in canonical order.</summary>
        public IReadOnlyList<SchedulePlaybackPoint> PlaybackPointsOfStage(int stageIndex)
        {
            for (int i = 0; i < Stages.Count; i++)
            {
                if (Stages[i].StageIndex == stageIndex)
                {
                    return Stages[i].PlaybackPoints;
                }
            }

            return Array.Empty<SchedulePlaybackPoint>();
        }

        public override string ToString()
        {
            return "schedule(stages=" + Stages.Count.ToString(CultureInfo.InvariantCulture)
                + ", systems=" + Entries.Count.ToString(CultureInfo.InvariantCulture)
                + ", playback=" + PlaybackPoints.Count.ToString(CultureInfo.InvariantCulture)
                + ", edges=" + Edges.Count.ToString(CultureInfo.InvariantCulture)
                + ", hash=" + Hash.ToHex() + ")";
        }

        /// <summary>Human-readable canonical order, used by diagnostics and test failure output.</summary>
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append(ToString());
            for (int i = 0; i < Stages.Count; i++)
            {
                ScheduleStage stage = Stages[i];
                text.Append(Environment.NewLine).Append("  ").Append(stage.ToString());
                for (int s = 0; s < stage.Systems.Count; s++)
                {
                    ScheduleEntry entry = stage.Systems[s];
                    text.Append(Environment.NewLine).Append("    ").Append(entry.ToString());
                    if (entry.PredecessorStages.Count > 0)
                    {
                        text.Append(" afterStages=");
                        for (int p = 0; p < entry.PredecessorStages.Count; p++)
                        {
                            text.Append(p == 0 ? string.Empty : ",")
                                .Append(entry.PredecessorStages[p].ToString(CultureInfo.InvariantCulture));
                        }
                    }
                }

                for (int p = 0; p < stage.PlaybackPoints.Count; p++)
                {
                    text.Append(Environment.NewLine).Append("    ").Append(stage.PlaybackPoints[p].ToString());
                }
            }

            return text.ToString();
        }
    }
}
