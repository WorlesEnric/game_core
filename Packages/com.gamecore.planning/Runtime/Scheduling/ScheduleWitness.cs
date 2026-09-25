// GameCore.Planning - semantic schedule compilation (GC-009).
// Normative sources: docs/game-core/03-runtime-and-execution.md section 3 ("report AmbiguousOrder otherwise",
// "diagnostic witness before publication"), docs/game-core/00-core-protocols.md P-008, P-028, P-039..P-043.
// Unity-free: BCL subset only, no UnityEngine/Unity.* reference (01 section 1).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Planning.Scheduling
{
    /// <summary>
    /// What a rejection witness is about. It names the declaration shape, so a diagnostic can point at the exact
    /// conflicting declarations or at the cycle path instead of only returning an error code (P-028, 03 section 3).
    /// </summary>
    public enum ScheduleWitnessKind
    {
        /// <summary>Two declarations share a StageId but declare different contract versions (P-039 coalescing).</summary>
        StageVersionMismatch = 0,

        /// <summary>Two declarations of one stage name different owning packages (P-039, P-001).</summary>
        StageOwnerMismatch = 1,

        /// <summary>Two declarations of one stage declare different host affinities (P-039).</summary>
        StageAffinityMismatch = 2,

        /// <summary>The same system key appears twice in one stage with non-identical declarations (04 section 4).</summary>
        DuplicateSystemDeclaration = 3,

        /// <summary>A required stage edge names a stage that is not part of the active declaration set (P-039).</summary>
        RequiredStageEdgeMissing = 4,

        /// <summary>A required inner system edge names a system absent from its stage (P-039).</summary>
        RequiredSystemEdgeMissing = 5,

        /// <summary>The stage dependency graph contains a cycle; <see cref="ScheduleWitness.CyclePath"/> names it.</summary>
        StageCycle = 6,

        /// <summary>One stage's inner system graph contains a cycle; <see cref="ScheduleWitness.SystemCyclePath"/> names it.</summary>
        SystemCycle = 7,

        /// <summary>Two systems overlap read/write with no directed path and no proven disjoint partitions (P-040).</summary>
        AmbiguousAccessOrder = 8,

        /// <summary>A declared buffer's consuming stage is not active while a producer stage is (P-043).</summary>
        BufferConsumerMissing = 9,

        /// <summary>A declared buffer's owning stage is not active while the buffer itself is (P-043).</summary>
        BufferOwnerMissing = 10,

        /// <summary>A stage declares a buffer port whose buffer has no <see cref="BufferSpec"/> contract (P-043).</summary>
        BufferPortMissing = 11,

        /// <summary>A declared port direction contradicts the buffer contract's producer/consumer stages (P-043).</summary>
        BufferPortDirectionMismatch = 12,

        /// <summary>
        /// A buffer's producing stages are not ordered before its owning stage, so the deferred playback point has
        /// no defined position relative to its producers (P-040, P-041).
        /// </summary>
        PlaybackOrderUndefined = 13,

        /// <summary>Two buffer contracts declare the same buffer id (P-043: one contract per buffer).</summary>
        DuplicateBufferContract = 14,
    }

    /// <summary>
    /// One edge witness: why a schedule was rejected, with the declarations that conflict, the schema and access
    /// modes of an unordered access pair, or the cycle path. Immutable; a rejection carries every witness found.
    /// </summary>
    public sealed class ScheduleWitness
    {
        public ScheduleWitness(
            ScheduleWitnessKind kind,
            DiagnosticCode code,
            string detail,
            StageId stage = default(StageId),
            StageId relatedStage = default(StageId),
            FactoryKey system = default(FactoryKey),
            FactoryKey relatedSystem = default(FactoryKey),
            SchemaRef schema = default(SchemaRef),
            AccessMode mode = AccessMode.Read,
            AccessMode relatedMode = AccessMode.Read,
            Id128 partition = default(Id128),
            Id128 relatedPartition = default(Id128),
            BufferId buffer = default(BufferId),
            PortDirection portDirection = PortDirection.Producer,
            IReadOnlyList<StageId>? cyclePath = null,
            IReadOnlyList<FactoryKey>? systemCyclePath = null)
        {
            Kind = kind;
            Code = code;
            Detail = detail ?? string.Empty;
            Stage = stage;
            RelatedStage = relatedStage;
            System = system;
            RelatedSystem = relatedSystem;
            Schema = schema;
            Mode = mode;
            RelatedMode = relatedMode;
            Partition = partition;
            RelatedPartition = relatedPartition;
            Buffer = buffer;
            PortDirection = portDirection;
            CyclePath = ContractCollections.Freeze(cyclePath);
            SystemCyclePath = ContractCollections.Freeze(systemCyclePath);
        }

        public ScheduleWitnessKind Kind { get; }

        /// <summary>Protocol diagnostic code this witness rejects with (00 section 9, P-040).</summary>
        public DiagnosticCode Code { get; }

        public string Detail { get; }

        /// <summary>Stage the witness is about; default for a system-only witness outside a stage.</summary>
        public StageId Stage { get; }

        /// <summary>The other stage of an edge witness; default when the witness has no second stage.</summary>
        public StageId RelatedStage { get; }

        public FactoryKey System { get; }

        public FactoryKey RelatedSystem { get; }

        /// <summary>Overlapping schema of an access witness; default when not applicable.</summary>
        public SchemaRef Schema { get; }

        public AccessMode Mode { get; }

        public AccessMode RelatedMode { get; }

        /// <summary>Declared partition of the first access; default (zero) means unpartitioned.</summary>
        public Id128 Partition { get; }

        public Id128 RelatedPartition { get; }

        public BufferId Buffer { get; }

        public PortDirection PortDirection { get; }

        /// <summary>Stage cycle path, starting and ending at the same stage; empty for other witnesses.</summary>
        public IReadOnlyList<StageId> CyclePath { get; }

        /// <summary>Inner system cycle path, starting and ending at the same system key; empty otherwise.</summary>
        public IReadOnlyList<FactoryKey> SystemCyclePath { get; }

        public bool IsCycle => Kind == ScheduleWitnessKind.StageCycle || Kind == ScheduleWitnessKind.SystemCycle;

        public override string ToString()
        {
            var text = new StringBuilder();
            text.Append(Kind.ToString()).Append('(').Append(DiagnosticCodeText.Of(Code)).Append("): ").Append(Detail);
            if (!Stage.IsDefault)
            {
                text.Append(" stage=").Append(Stage.ToString());
            }

            if (!RelatedStage.IsDefault)
            {
                text.Append(" relatedStage=").Append(RelatedStage.ToString());
            }

            if (!System.Equals(default(FactoryKey)))
            {
                text.Append(" system=").Append(System.ToString());
            }

            if (!RelatedSystem.Equals(default(FactoryKey)))
            {
                text.Append(" relatedSystem=").Append(RelatedSystem.ToString());
            }

            if (!Schema.Id.IsDefault)
            {
                text.Append(" schema=").Append(Schema.ToString());
            }

            if (CyclePath.Count > 0)
            {
                text.Append(" path=");
                for (int i = 0; i < CyclePath.Count; i++)
                {
                    text.Append(i == 0 ? string.Empty : " -> ").Append(CyclePath[i].ToString());
                }
            }

            if (SystemCyclePath.Count > 0)
            {
                text.Append(" systemPath=");
                for (int i = 0; i < SystemCyclePath.Count; i++)
                {
                    text.Append(i == 0 ? string.Empty : " -> ").Append(SystemCyclePath[i].ToString());
                }
            }

            return text.ToString();
        }
    }

    /// <summary>
    /// Canonical witness order, so a rejection is byte-stable under a shuffled declaration order (P-008, TEST-022):
    /// code, kind, stage, related stage, system, related system, buffer, then the detail text.
    /// </summary>
    public sealed class ScheduleWitnessComparer : IComparer<ScheduleWitness>
    {
        public static readonly ScheduleWitnessComparer Instance = new ScheduleWitnessComparer();

        public int Compare(ScheduleWitness? x, ScheduleWitness? y)
        {
            if (ReferenceEquals(x, y))
            {
                return 0;
            }

            if (x == null)
            {
                return -1;
            }

            if (y == null)
            {
                return 1;
            }

            int order = ((int)x.Code).CompareTo((int)y.Code);
            if (order != 0)
            {
                return order;
            }

            order = ((int)x.Kind).CompareTo((int)y.Kind);
            if (order != 0)
            {
                return order;
            }

            order = x.Stage.CompareTo(y.Stage);
            if (order != 0)
            {
                return order;
            }

            order = x.RelatedStage.CompareTo(y.RelatedStage);
            if (order != 0)
            {
                return order;
            }

            order = FactoryKeyComparer.Instance.Compare(x.System, y.System);
            if (order != 0)
            {
                return order;
            }

            order = FactoryKeyComparer.Instance.Compare(x.RelatedSystem, y.RelatedSystem);
            if (order != 0)
            {
                return order;
            }

            order = x.Buffer.CompareTo(y.Buffer);
            if (order != 0)
            {
                return order;
            }

            // An access witness carries a schema, two modes and two partitions: without these keys two distinct
            // conflicts of the same system pair would tie, and their relative order would fall back to the order the
            // declarations happened to be listed in (P-008, TEST-022).
            order = x.Schema.Id.Value.CompareTo(y.Schema.Id.Value);
            if (order != 0)
            {
                return order;
            }

            order = x.Schema.Version.CompareTo(y.Schema.Version);
            if (order != 0)
            {
                return order;
            }

            order = ((int)x.Mode).CompareTo((int)y.Mode);
            if (order != 0)
            {
                return order;
            }

            order = ((int)x.RelatedMode).CompareTo((int)y.RelatedMode);
            if (order != 0)
            {
                return order;
            }

            order = x.Partition.CompareTo(y.Partition);
            if (order != 0)
            {
                return order;
            }

            order = x.RelatedPartition.CompareTo(y.RelatedPartition);
            if (order != 0)
            {
                return order;
            }

            order = string.CompareOrdinal(x.Detail, y.Detail);

            return string.CompareOrdinal(DescribePath(x), DescribePath(y));
        }

        private static string DescribePath(ScheduleWitness witness)
        {
            if (witness.CyclePath.Count == 0 && witness.SystemCyclePath.Count == 0)
            {
                return string.Empty;
            }

            return witness.ToString();
        }
    }

    /// <summary>
    /// Outcome of one schedule compilation: either a compiled schedule, or the diagnostic code plus every edge
    /// witness found. A rejected compilation never exposes a partial schedule (03 section 3, P-028).
    /// </summary>
    public sealed class ScheduleCompilation
    {
        private ScheduleCompilation(
            bool succeeded,
            CompiledSchedule? schedule,
            DiagnosticCode code,
            IReadOnlyList<ScheduleWitness>? witnesses,
            string detail,
            IReadOnlyList<StageId>? cyclePath,
            bool witnessesTruncated)
        {
            Succeeded = succeeded;
            Schedule = schedule;
            Code = code;
            Witnesses = ContractCollections.Freeze(witnesses);
            Detail = detail ?? string.Empty;
            CyclePath = ContractCollections.Freeze(cyclePath);
            WitnessesTruncated = witnessesTruncated;
        }

        public bool Succeeded { get; }

        /// <summary>Non-null only when <see cref="Succeeded"/>.</summary>
        public CompiledSchedule? Schedule { get; }

        /// <summary>Primary diagnostic code; <see cref="DiagnosticCode.None"/> on success.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>Every witness found in the phase that rejected the schedule, in canonical order.</summary>
        public IReadOnlyList<ScheduleWitness> Witnesses { get; }

        public string Detail { get; }

        /// <summary>Primary stage cycle path for a rejected cyclic graph; empty when no stage cycle was found.</summary>
        public IReadOnlyList<StageId> CyclePath { get; }

        /// <summary>True when more witnesses existed than the compiler retains (they are not silently dropped).</summary>
        public bool WitnessesTruncated { get; }

        public static ScheduleCompilation Compiled(CompiledSchedule schedule)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            return new ScheduleCompilation(true, schedule, DiagnosticCode.None, null, "compiled", null, false);
        }

        public static ScheduleCompilation Rejected(
            DiagnosticCode code,
            IReadOnlyList<ScheduleWitness>? witnesses,
            string detail,
            IReadOnlyList<StageId>? cyclePath,
            bool witnessesTruncated)
        {
            return new ScheduleCompilation(
                false,
                null,
                code == DiagnosticCode.None ? DiagnosticCode.AmbiguousOrder : code,
                witnesses,
                detail,
                cyclePath,
                witnessesTruncated);
        }

        /// <summary>Every witness rendered on its own line; used by diagnostics and by the tests' failure output.</summary>
        public string Explain()
        {
            if (Succeeded)
            {
                return "compiled: " + (Schedule != null ? Schedule.ToString() : "no schedule");
            }

            var text = new StringBuilder();
            text.Append("rejected(").Append(DiagnosticCodeText.Of(Code)).Append("): ").Append(Detail);
            for (int i = 0; i < Witnesses.Count; i++)
            {
                text.Append(Environment.NewLine).Append("  ").Append(Witnesses[i].ToString());
            }

            if (WitnessesTruncated)
            {
                text.Append(Environment.NewLine).Append("  (further witnesses truncated)");
            }

            return text.ToString();
        }

        public override string ToString()
        {
            return (Succeeded ? "compiled" : "rejected:" + Code.ToString())
                + " witnesses=" + Witnesses.Count.ToString(CultureInfo.InvariantCulture);
        }
    }
}
