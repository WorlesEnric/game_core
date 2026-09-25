// GameCore.Unity.Runtime - Unity dispatch half of the schedule compiler (GC-009).
//
// The Planning assembly owns the semantic compile (P-040); this adapter hands the compiled schedule to GC-005's
// guarded ordered dispatch, which is the only execution path in V1 (04 sections 3 and 4):
//
//   * the compiled stage order becomes the installed dispatch table (`GuardedDispatchPlan`), so the group's
//     `EnableSystemSorting = false` order is exactly the compiled topological order;
//   * every consumed declared buffer becomes a native resource slot and a playback binding whose producer fences
//     must be combined before dependent readers or structural playback run (P-041, P-043);
//   * the adapter refuses a schedule it cannot dispatch honestly: a buffer whose producing stage is not a
//     predecessor of its consuming stage, a duplicate system key, a non-ascending dispatch index, or a playback
//     point outside the consumer's predecessors (P-028, P-040).
//
// The adapter holds no Unity state of its own beyond the native fence table it builds, and it never invents a
// stage: an empty schedule installs an empty table.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Planning.Scheduling;
using Unity.Jobs;

namespace GameCore.Unity.Runtime.Time
{
    /// <summary>Resolves the dispatch kind of one compiled system key from the generated registry (04 section 8).</summary>
    public interface IScheduleDispatchKindResolver
    {
        /// <summary>False when the key has no generated registration; adaptation then reports a witness (P-028).</summary>
        bool TryResolveKind(FactoryKey systemKey, out SystemDispatchKind kind);
    }

    /// <summary>Dispatch-kind resolution from an explicit table: no reflection and no assembly scan (04 section 3).</summary>
    public sealed class ScheduleDispatchKindTable : IScheduleDispatchKindResolver
    {
        private readonly Dictionary<FactoryKey, SystemDispatchKind> kinds =
            new Dictionary<FactoryKey, SystemDispatchKind>();

        public ScheduleDispatchKindTable Add(FactoryKey key, SystemDispatchKind kind)
        {
            kinds[key] = kind;
            return this;
        }

        public bool TryResolveKind(FactoryKey systemKey, out SystemDispatchKind kind)
            => kinds.TryGetValue(systemKey, out kind);

        public int Count => kinds.Count;
    }

    /// <summary>Why one schedule could not be adapted into a dispatch table (P-028, P-040, P-041).</summary>
    public sealed class ScheduleAdaptationWitness
    {
        public ScheduleAdaptationWitness(DiagnosticCode code, string detail, StageId stage, FactoryKey system, BufferId buffer)
        {
            Code = code;
            Detail = detail ?? string.Empty;
            Stage = stage;
            System = system;
            Buffer = buffer;
        }

        public DiagnosticCode Code { get; }

        public string Detail { get; }

        public StageId Stage { get; }

        public FactoryKey System { get; }

        public BufferId Buffer { get; }

        public override string ToString()
        {
            return DiagnosticCodeText.Of(Code) + ": " + Detail + " stage=" + Stage.ToString()
                + " system=" + System.ToString() + " buffer=" + Buffer.ToString();
        }
    }

    /// <summary>
    /// One declared buffer as a native resource slot. The slot carries the producer's <c>JobHandle</c> outside ECS
    /// component tracking, so a dependent reader or a structural playback can wait for it explicitly (P-041).
    /// </summary>
    public sealed class ScheduleBufferBinding
    {
        public ScheduleBufferBinding(SchedulePlaybackPoint point, int slot, string diagnosticName)
        {
            Point = point;
            Slot = slot;
            DiagnosticName = diagnosticName ?? string.Empty;
        }

        public SchedulePlaybackPoint Point { get; }

        /// <summary>Index into <see cref="NativeDependencyTable"/> this buffer's producer fence is stored in.</summary>
        public int Slot { get; }

        public string DiagnosticName { get; }

        public BufferId Buffer => Point.Buffer;

        public int OwnerStageIndex => Point.OwnerStageIndex;

        public int ConsumerStageIndex => Point.ConsumerStageIndex;

        /// <summary>Producer stage fence indices, both from the buffer contract and its playback point (P-043).</summary>
        public IReadOnlyList<int> ProducerStageIndexes => Point.ProducerStageIndexes;

        /// <summary>Handle a dependent read or this buffer's playback must wait for (P-041).</summary>
        public JobHandle CombineProducerFence(NativeDependencyTable table)
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            return table.CombineIncoming(new[] { Slot });
        }

        public override string ToString()
            => "buffer[" + Slot.ToString(CultureInfo.InvariantCulture) + "]:" + Buffer.ToString() + ":" + DiagnosticName;
    }

    /// <summary>The adapted schedule: exactly what a world registration needs to install and drain it.</summary>
    public sealed class ScheduleAdaptation
    {
        private ScheduleAdaptation(
            bool succeeded,
            GuardedDispatchPlan? stepPlan,
            IReadOnlyList<StageRegistration>? stages,
            IReadOnlyList<ScheduleBufferBinding>? buffers,
            NativeDependencyTable? nativeTable,
            IReadOnlyList<ScheduleAdaptationWitness>? witnesses,
            DiagnosticCode code)
        {
            Succeeded = succeeded;
            StepPlan = stepPlan;
            Stages = ContractCollections.Freeze(stages);
            Buffers = ContractCollections.Freeze(buffers);
            NativeTable = nativeTable;
            Witnesses = ContractCollections.Freeze(witnesses);
            Code = code;
        }

        public bool Succeeded { get; }

        /// <summary>Non-null only on success; install this into <c>GameCoreStepGroup</c> at the assembly fence.</summary>
        public GuardedDispatchPlan? StepPlan { get; }

        /// <summary>One <see cref="StageRegistration"/> per compiled stage, in fence-index order (P-039).</summary>
        public IReadOnlyList<StageRegistration> Stages { get; }

        /// <summary>Buffer slots and playback bindings, in canonical order (P-043).</summary>
        public IReadOnlyList<ScheduleBufferBinding> Buffers { get; }

        /// <summary>Native resource fence table for the declared buffers; null when adaptation failed.</summary>
        public NativeDependencyTable? NativeTable { get; }

        public IReadOnlyList<ScheduleAdaptationWitness> Witnesses { get; }

        public DiagnosticCode Code { get; }

        public string Explain()
        {
            if (Succeeded)
            {
                return "adapted: " + (StepPlan != null ? StepPlan.Entries.Count.ToString(CultureInfo.InvariantCulture) : "0")
                    + " entries, " + Buffers.Count.ToString(CultureInfo.InvariantCulture) + " buffers";
            }

            var text = new System.Text.StringBuilder();
            text.Append("adaptation rejected(").Append(DiagnosticCodeText.Of(Code)).Append(')');
            for (int i = 0; i < Witnesses.Count; i++)
            {
                text.Append(Environment.NewLine).Append("  ").Append(Witnesses[i].ToString());
            }

            return text.ToString();
        }

        internal static ScheduleAdaptation Rejected(
            DiagnosticCode code,
            IReadOnlyList<ScheduleAdaptationWitness> witnesses)
            => new ScheduleAdaptation(false, null, null, null, null, witnesses, code);

        internal static ScheduleAdaptation Adapted(
            GuardedDispatchPlan plan,
            IReadOnlyList<StageRegistration> stages,
            IReadOnlyList<ScheduleBufferBinding> buffers,
            NativeDependencyTable table)
            => new ScheduleAdaptation(true, plan, stages, buffers, table, null, DiagnosticCode.None);
    }

    /// <summary>
    /// Adapts one <see cref="CompiledSchedule"/> into GC-005's installed dispatch shape plus its native resource
    /// slots (04 section 4). Pure mapping: it creates no systems, mutates no ECS storage and runs at the assembly
    /// fence only.
    /// </summary>
    public static class CompiledScheduleAdapter
    {
        public static ScheduleAdaptation Adapt(CompiledSchedule schedule, IScheduleDispatchKindResolver resolver)
        {
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }

            if (resolver == null)
            {
                throw new ArgumentNullException(nameof(resolver));
            }

            var witnesses = new List<ScheduleAdaptationWitness>();

            var stages = new List<StageRegistration>(schedule.Stages.Count);
            for (int i = 0; i < schedule.Stages.Count; i++)
            {
                ScheduleStage stage = schedule.Stages[i];
                stages.Add(new StageRegistration(
                    stage.Stage,
                    StageDiagnosticName(stage),
                    stage.StageIndex,
                    stage.PredecessorStages));
            }

            var entries = new List<GuardedDispatchEntry>(schedule.Entries.Count);
            var seenKeys = new HashSet<FactoryKey>();
            int previousIndex = -1;
            for (int i = 0; i < schedule.Entries.Count; i++)
            {
                ScheduleEntry entry = schedule.Entries[i];

                if (entry.DispatchIndex <= previousIndex)
                {
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.AmbiguousOrder,
                        "Dispatch indices must be strictly ascending for the guarded ordered dispatch (P-040).",
                        entry.Stage,
                        entry.SystemKey,
                        default(BufferId)));
                }

                previousIndex = entry.DispatchIndex;

                if (!seenKeys.Add(entry.SystemKey))
                {
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.OwnershipConflict,
                        "One precompiled system type has one scheduling instance per world in V1 (04 section 4).",
                        entry.Stage,
                        entry.SystemKey,
                        default(BufferId)));
                }

                if (entry.StageIndex < 0 || entry.StageIndex >= schedule.StageCount)
                {
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.AmbiguousOrder,
                        "The entry references a fence index outside the compiled stage count (P-041).",
                        entry.Stage,
                        entry.SystemKey,
                        default(BufferId)));
                    continue;
                }

                if (!schedule.Stages[entry.StageIndex].Stage.Equals(entry.Stage))
                {
                    // The stage index selects the native fence slot and the stage id decides ledger identity and the
                    // commit-time drain check, so a mismatched pair would silently order and validate the wrong stage.
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.AmbiguousOrder,
                        "The entry's stage id does not designate its fence index; index "
                        + entry.StageIndex.ToString(CultureInfo.InvariantCulture) + " holds stage "
                        + schedule.Stages[entry.StageIndex].Stage.ToString() + " (P-040, P-041).",
                        entry.Stage,
                        entry.SystemKey,
                        default(BufferId)));
                    continue;
                }

                if (!resolver.TryResolveKind(entry.SystemKey, out SystemDispatchKind kind))
                {
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.MissingDependency,
                        "No generated registration resolves this system key; a missing registration is an assembly "
                        + "defect, never a silently skipped entry (04 section 8).",
                        entry.Stage,
                        entry.SystemKey,
                        default(BufferId)));
                    continue;
                }

                entries.Add(new GuardedDispatchEntry(
                    entry.Stage,
                    entry.SystemKey,
                    kind,
                    entry.DispatchIndex,
                    entry.StageIndex,
                    entry.PredecessorStages));
            }

            var bufferSlots = new List<ScheduleBufferBinding>(schedule.PlaybackPoints.Count);
            for (int i = 0; i < schedule.PlaybackPoints.Count; i++)
            {
                SchedulePlaybackPoint point = schedule.PlaybackPoints[i];

                if (!Precedes(point.ProducerStageIndexes, point.ConsumerStageIndex, schedule))
                {
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.AmbiguousOrder,
                        "A declared buffer's producing stage is not a predecessor of its consuming stage, so a "
                        + "dependent reader could read before its producer finished (P-040, P-041).",
                        point.ConsumerStage,
                        default(FactoryKey),
                        point.Buffer));
                    continue;
                }

                // The compiler orders the owner before the consumer whenever they differ, which is what the playback
                // point needs: it plays back on the owning stage's fence, and the consumer already inherits that
                // fence index. An owner that is neither the consumer nor ordered before it would have no position.
                if (point.OwnerStageIndex != point.ConsumerStageIndex
                    && !Contains(point.ProducerStageIndexes, point.OwnerStageIndex)
                    && !OrderedBefore(point.OwnerStageIndex, point.ConsumerStageIndex, schedule))
                {
                    witnesses.Add(new ScheduleAdaptationWitness(
                        DiagnosticCode.AmbiguousOrder,
                        "The buffer's owning stage is neither its consumer nor ordered before it, so its playback "
                        + "point has no defined position (P-041).",
                        point.OwnerStage,
                        default(FactoryKey),
                        point.Buffer));
                    continue;
                }

                bufferSlots.Add(new ScheduleBufferBinding(point, bufferSlots.Count, point.Buffer.ToString()));
            }

            if (witnesses.Count > 0)
            {
                DiagnosticCode code = witnesses[0].Code;
                for (int i = 1; i < witnesses.Count; i++)
                {
                    if ((int)witnesses[i].Code < (int)code)
                    {
                        code = witnesses[i].Code;
                    }
                }

                return ScheduleAdaptation.Rejected(code, witnesses);
            }

            var plan = new GuardedDispatchPlan(entries, schedule.BufferBindings, schedule.StageCount);
            if (!plan.TryValidate(out DiagnosticCode planCode, out string planDetail))
            {
                return ScheduleAdaptation.Rejected(planCode, new[]
                {
                    new ScheduleAdaptationWitness(
                        planCode,
                        "The compiled schedule does not form a valid guarded dispatch table: " + planDetail,
                        default(StageId),
                        default(FactoryKey),
                        default(BufferId)),
                });
            }

            return ScheduleAdaptation.Adapted(plan, stages, bufferSlots, new NativeDependencyTable(bufferSlots.Count));
        }

        /// <summary>Builds the type registration of one stage's systems from the generated-style registry.</summary>
        public static IReadOnlyList<SystemRegistration> Registrations(
            ScheduleAdaptation adaptation,
            IReadOnlyList<SystemRegistration> generated)
        {
            if (adaptation == null)
            {
                throw new ArgumentNullException(nameof(adaptation));
            }

            if (generated == null)
            {
                throw new ArgumentNullException(nameof(generated));
            }

            if (!adaptation.Succeeded || adaptation.StepPlan == null)
            {
                throw new InvalidOperationException("A rejected adaptation has no dispatch table to register.");
            }

            var byKey = new Dictionary<FactoryKey, SystemRegistration>();
            for (int i = 0; i < generated.Count; i++)
            {
                byKey[generated[i].Key] = generated[i];
            }

            var registrations = new List<SystemRegistration>();
            for (int i = 0; i < adaptation.StepPlan.Entries.Count; i++)
            {
                GuardedDispatchEntry entry = adaptation.StepPlan.Entries[i];
                if (!byKey.TryGetValue(entry.SystemKey, out SystemRegistration? registration))
                {
                    throw new InvalidOperationException(
                        "No generated registration matches the compiled entry " + entry.SystemKey.ToString() + ".");
                }

                registrations.Add(registration);
            }

            return registrations;
        }

        private static string StageDiagnosticName(ScheduleStage stage)
            => "schedule.stage." + stage.StageIndex.ToString(CultureInfo.InvariantCulture);

        private static bool Contains(IReadOnlyList<int> values, int candidate)
        {
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == candidate)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>True when every producer fence index precedes the consumer in the compiled order (P-040).</summary>
        private static bool Precedes(IReadOnlyList<int> producerStageIndexes, int consumerStageIndex, CompiledSchedule schedule)
        {
            for (int i = 0; i < producerStageIndexes.Count; i++)
            {
                int producer = producerStageIndexes[i];
                if (producer == consumerStageIndex)
                {
                    continue;
                }

                if (producer < 0 || producer >= schedule.StageCount)
                {
                    return false;
                }

                // A backward edge in the compiled order is what the dispatcher's predecessor invariant needs; a
                // transitive path is already reflected in the consumer's own predecessor list (P-041).
                if (producer >= consumerStageIndex)
                {
                    return false;
                }

                if (!Contains(schedule.Stages[consumerStageIndex].PredecessorStages, producer)
                    && !Reaches(producer, consumerStageIndex, schedule))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when the compiled order places one stage strictly before another: a backward fence index that is
        /// either a declared predecessor or reachable through compiled edges (P-040).
        /// </summary>
        private static bool OrderedBefore(int earlierStageIndex, int laterStageIndex, CompiledSchedule schedule)
        {
            if (earlierStageIndex < 0 || laterStageIndex < 0
                || earlierStageIndex >= schedule.StageCount || laterStageIndex >= schedule.StageCount)
            {
                return false;
            }

            if (earlierStageIndex >= laterStageIndex)
            {
                return false;
            }

            return Contains(schedule.Stages[laterStageIndex].PredecessorStages, earlierStageIndex)
                || Reaches(earlierStageIndex, laterStageIndex, schedule);
        }

        private static bool Reaches(int fromStageIndex, int toStageIndex, CompiledSchedule schedule)
        {
            var visited = new bool[schedule.StageCount];
            var queue = new List<int> { fromStageIndex };
            visited[fromStageIndex] = true;
            int head = 0;
            while (head < queue.Count)
            {
                int current = queue[head];
                head++;
                if (current < 0 || current >= schedule.StageCount)
                {
                    continue;
                }

                IReadOnlyList<int> successors = SuccessorsOf(current, schedule);
                for (int i = 0; i < successors.Count; i++)
                {
                    int next = successors[i];
                    if (next == toStageIndex)
                    {
                        return true;
                    }

                    if (next >= 0 && next < schedule.StageCount && !visited[next])
                    {
                        visited[next] = true;
                        queue.Add(next);
                    }
                }
            }

            return false;
        }

        private static IReadOnlyList<int> SuccessorsOf(int stageIndex, CompiledSchedule schedule)
        {
            var successors = new List<int>();
            for (int i = 0; i < schedule.Edges.Count; i++)
            {
                if (schedule.Edges[i].FromIndex == stageIndex)
                {
                    successors.Add(schedule.Edges[i].ToIndex);
                }
            }

            return successors;
        }
    }
}
