#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution
{
    /// <summary>
    /// One entry of the flattened, compile-time-ordered dispatch table (04 s4, P-039/P-040). The entry carries
    /// keys and indices only; the Unity catalog resolves the key to a concrete system instance.
    /// </summary>
    public sealed class GuardedDispatchEntry
    {
        public GuardedDispatchEntry(
            StageId stage,
            FactoryKey systemKey,
            SystemDispatchKind kind,
            int dispatchIndex,
            int stageIndex,
            IReadOnlyList<int>? predecessorStages)
        {
            Stage = stage;
            SystemKey = systemKey;
            Kind = kind;
            DispatchIndex = dispatchIndex;
            StageIndex = stageIndex;
            PredecessorStages = ContractCollections.Freeze(predecessorStages);
        }

        public StageId Stage { get; }

        public FactoryKey SystemKey { get; }

        public SystemDispatchKind Kind { get; }

        /// <summary>Position in the compiled order; ascending execution order (P-040).</summary>
        public int DispatchIndex { get; }

        /// <summary>Index into the host-owned native fence table.</summary>
        public int StageIndex { get; }

        /// <summary>Stages whose completed fences are incoming edges for this entry (P-041).</summary>
        public IReadOnlyList<int> PredecessorStages { get; }

        public bool IsInfrastructure => Kind == SystemDispatchKind.InfrastructureGroup;

        public override string ToString()
        {
            return DispatchIndex.ToString(CultureInfo.InvariantCulture) + ":" + Kind + ":" + Stage.ToString()
                + "/" + SystemKey.ToString();
        }
    }

    /// <summary>Outcome of commit-time drain validation (P-043, O-16).</summary>
    public readonly struct DrainValidation
    {
        public readonly bool Succeeded;
        public readonly DiagnosticCode Code;
        public readonly BufferId UnconsumedBuffer;

        public DrainValidation(bool succeeded, DiagnosticCode code, BufferId unconsumedBuffer)
        {
            Succeeded = succeeded;
            Code = code;
            UnconsumedBuffer = unconsumedBuffer;
        }

        public static DrainValidation Ok => new DrainValidation(true, DiagnosticCode.None, default(BufferId));
    }

    /// <summary>
    /// The compiled ordered dispatch table of one adapter group. It is data: well-formedness, ordering and drain
    /// rules are checked here, while invocation, fences and fault latching live in the Unity group (04 s4).
    /// </summary>
    public sealed class GuardedDispatchPlan
    {
        public static readonly GuardedDispatchPlan Empty =
            new GuardedDispatchPlan(null, null, 0);

        public GuardedDispatchPlan(
            IReadOnlyList<GuardedDispatchEntry>? entries,
            IReadOnlyList<BufferBinding>? bufferBindings,
            int stageCount)
        {
            Entries = ContractCollections.Freeze(entries);
            BufferBindings = ContractCollections.Freeze(bufferBindings);
            StageCount = stageCount < 0 ? 0 : stageCount;
        }

        public IReadOnlyList<GuardedDispatchEntry> Entries { get; }

        public IReadOnlyList<BufferBinding> BufferBindings { get; }

        /// <summary>Number of stage fence slots this plan needs (P-041).</summary>
        public int StageCount { get; }

        public bool IsEmpty => Entries.Count == 0;

        /// <summary>Projects this plan onto the frozen seam's epoch-bound dispatch table.</summary>
        public OrderedDispatchTable ToOrderedTable(AssemblyEpoch epoch)
        {
            var table = new List<SystemDispatchEntry>(Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                GuardedDispatchEntry entry = Entries[i];
                table.Add(new SystemDispatchEntry(entry.Stage, entry.SystemKey, entry.Kind, entry.DispatchIndex));
            }

            return new OrderedDispatchTable(epoch, table, BufferBindings);
        }

        /// <summary>
        /// Ordering and index validity: strictly ascending dispatch indices, unique system keys, in-range stage
        /// indices and strictly backward predecessor references (P-040).
        /// </summary>
        public bool TryValidate(out DiagnosticCode code, out string detail)
        {
            HashSet<FactoryKey> keys = new HashSet<FactoryKey>();
            int previousIndex = -1;
            for (int i = 0; i < Entries.Count; i++)
            {
                GuardedDispatchEntry entry = Entries[i];
                if (entry.DispatchIndex <= previousIndex)
                {
                    code = DiagnosticCode.AmbiguousOrder;
                    detail = "Dispatch indices must be strictly ascending (P-040).";
                    return false;
                }

                previousIndex = entry.DispatchIndex;

                if (!keys.Add(entry.SystemKey))
                {
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "A system key appears twice in one dispatch table; one precompiled system type has one "
                        + "scheduling instance per world in V1 (04 s4).";
                    return false;
                }

                if (entry.StageIndex < 0 || entry.StageIndex >= StageCount)
                {
                    code = DiagnosticCode.AmbiguousOrder;
                    detail = "A dispatch entry references a stage index outside the declared stage count.";
                    return false;
                }

                for (int p = 0; p < entry.PredecessorStages.Count; p++)
                {
                    int predecessor = entry.PredecessorStages[p];
                    if (predecessor < 0 || predecessor >= StageCount || predecessor >= entry.StageIndex)
                    {
                        code = DiagnosticCode.Cycle;
                        detail = "Stage predecessors must be declared stage indices strictly below the entry's own "
                            + "stage index, so a stage edge can only point backward in the compiled order (P-040).";
                        return false;
                    }
                }
            }

            code = DiagnosticCode.None;
            detail = string.Empty;
            return true;
        }

        /// <summary>
        /// Commit-time drain validation: if any producer of a declared buffer ran, its consuming stage must have
        /// run in the same step, so reliable input is never silently dropped at commit (P-043, O-16).
        /// </summary>
        /// <param name="dispatchedKeys">Reused set of keys dispatched in this step.</param>
        /// <param name="stageDispatched">Reused per-stage flags, at least <see cref="StageCount"/> long.</param>
        public DrainValidation ValidateDrains(HashSet<FactoryKey> dispatchedKeys, bool[] stageDispatched)
        {
            if (dispatchedKeys == null)
            {
                throw new ArgumentNullException(nameof(dispatchedKeys));
            }

            if (stageDispatched == null)
            {
                throw new ArgumentNullException(nameof(stageDispatched));
            }

            for (int i = 0; i < BufferBindings.Count; i++)
            {
                BufferBinding binding = BufferBindings[i];
                bool produced = false;
                for (int p = 0; p < binding.Producers.Count; p++)
                {
                    if (dispatchedKeys.Contains(binding.Producers[p]))
                    {
                        produced = true;
                        break;
                    }
                }

                if (!produced)
                {
                    continue;
                }

                bool consumed = false;
                for (int e = 0; e < Entries.Count; e++)
                {
                    if (Entries[e].Stage.Equals(binding.ConsumerStage))
                    {
                        consumed = stageDispatched[Entries[e].StageIndex];
                        break;
                    }
                }

                if (!consumed)
                {
                    return new DrainValidation(false, DiagnosticCode.MissingDependency, binding.Buffer);
                }
            }

            return DrainValidation.Ok;
        }
    }

    /// <summary>
    /// Deterministic 128-bit id sequence: a fixed category salt in the high word and a strictly increasing low
    /// word, so repeated runs produce byte-identical fixtures (P-008, TEST-022). No clock, thread or randomness.
    /// </summary>
    public sealed class IdSequence
    {
        private readonly ulong salt;
        private ulong next;

        public IdSequence(ulong salt)
            : this(salt, 0UL)
        {
        }

        public IdSequence(ulong salt, ulong start)
        {
            this.salt = salt;
            next = start;
        }

        public ulong Salt => salt;

        public ulong LastIssued => next;

        /// <summary>Next identity; refuses at the unsigned maximum instead of wrapping (P-005).</summary>
        public Id128 Next()
        {
            if (next == ulong.MaxValue)
            {
                throw new InvalidOperationException("The deterministic id sequence is exhausted and never wraps.");
            }

            next++;
            return new Id128(salt, next);
        }
    }
}
