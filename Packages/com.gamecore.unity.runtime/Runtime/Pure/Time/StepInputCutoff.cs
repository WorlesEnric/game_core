// GameCore.Unity.Runtime - engine-free temporal core (GC-009), namespace GameCore.Execution.Time.
//
// Step input cutoff (P-037). An input cutoff seals each logical step's batch with a monotonic host-assigned
// admission sequence: commands/wakes arriving after the cutoff wait for the next step, a fixed-step world consumes
// the sealed prefix up to its configured capacity and retains the remainder, a duplicate request key returns its
// recorded result instead of executing twice, and a full bounded queue backpressures instead of dropping reliable
// input. The host assigns the sequence; wall time, thread timing and worker completion never order input (P-008).
//
// This file is Unity-free and is compiled both into the Unity assembly GameCore.Unity.Runtime and by the
// plain-dotnet project dotnet/src/GameCore.Execution (Runtime/Pure/**).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Time
{
    /// <summary>What one admitted unit of demand is (P-036, P-037, P-042).</summary>
    public enum DemandKind
    {
        /// <summary>An accepted external command envelope.</summary>
        Command = 0,

        /// <summary>A registered plugin wake request; it advances a command-driven world without a player command.</summary>
        Wake = 1,
    }

    /// <summary>One admitted unit of demand, stamped with the host's monotonic admission sequence (P-037).</summary>
    public readonly struct DemandRecord
    {
        public readonly AdmissionSequence Sequence;
        public readonly Id128 RequestKey;
        public readonly SchemaRef Schema;
        public readonly DemandKind Kind;

        public DemandRecord(AdmissionSequence sequence, Id128 requestKey, SchemaRef schema, DemandKind kind)
        {
            Sequence = sequence;
            RequestKey = requestKey;
            Schema = schema;
            Kind = kind;
        }

        public bool IsWake => Kind == DemandKind.Wake;

        public override string ToString()
        {
            return Kind + ":" + Sequence.Value.ToString(CultureInfo.InvariantCulture) + ":" + RequestKey.ToString();
        }
    }

    /// <summary>
    /// The sealed input batch of one logical step (P-037). Sealing is a snapshot of the host-assigned prefix, not a
    /// claim about how many commands a domain accepted.
    /// </summary>
    public sealed class InputSeal
    {
        public InputSeal(
            LogicalStepId step,
            bool sealedBatch,
            AdmissionSequence first,
            AdmissionSequence last,
            int admittedCommands,
            int admittedWakes,
            int deferred,
            IReadOnlyList<DemandRecord>? admitted)
        {
            Step = step;
            SealedBatch = sealedBatch;
            First = first;
            Last = last;
            AdmittedCommands = admittedCommands;
            AdmittedWakes = admittedWakes;
            Deferred = deferred;
            Admitted = ContractCollections.Freeze(admitted);
        }

        public LogicalStepId Step { get; }

        /// <summary>True when this step consumed at least one admitted unit; a sealed empty batch is legal.</summary>
        public bool SealedBatch { get; }

        /// <summary>First admission sequence of the sealed prefix; default when nothing was admitted.</summary>
        public AdmissionSequence First { get; }

        public AdmissionSequence Last { get; }

        public int AdmittedCommands { get; }

        public int AdmittedWakes { get; }

        /// <summary>Units still queued for a later step; a fixed-step world retains this remainder (P-037).</summary>
        public int Deferred { get; }

        /// <summary>The admitted prefix in ascending host admission order.</summary>
        public IReadOnlyList<DemandRecord> Admitted { get; }

        public int AdmittedCount => AdmittedCommands + AdmittedWakes;

        /// <summary>An empty seal: a paused or idle world seals nothing and drops nothing (P-035, P-036).</summary>
        public static InputSeal None(LogicalStepId step, int deferred)
            => new InputSeal(step, false, default(AdmissionSequence), default(AdmissionSequence), 0, 0, deferred, null);

        public override string ToString()
        {
            return "seal(step=" + Step.Value.ToString(CultureInfo.InvariantCulture)
                + ", commands=" + AdmittedCommands.ToString(CultureInfo.InvariantCulture)
                + ", wakes=" + AdmittedWakes.ToString(CultureInfo.InvariantCulture)
                + ", deferred=" + Deferred.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }

    /// <summary>
    /// Host-side input cutoff of one world (P-037). The host admits commands and wakes here, seals a batch per
    /// logical step, and retains whatever the step's capacity did not consume.
    /// </summary>
    public sealed class StepInputCutoff : ITelemetryOwner
    {
        string ITelemetryOwner.TelemetryOwner => "gamecore.time.cutoff";

        private readonly List<DemandRecord> pending = new List<DemandRecord>();
        private readonly Dictionary<Id128, AdmissionSequence> admittedKeys = new Dictionary<Id128, AdmissionSequence>();

        private ulong nextSequence;
        private readonly int maxPending;
        private readonly int maxRememberedKeys;

        public StepInputCutoff(int maxPending, int maxRememberedKeys)
        {
            if (maxPending <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPending), "The pending queue must be positive (P-037).");
            }

            if (maxRememberedKeys <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxRememberedKeys),
                    "Duplicate detection needs a positive bounded key window (P-037).");
            }

            this.maxPending = maxPending;
            this.maxRememberedKeys = maxRememberedKeys;
        }

        /// <summary>Units queued and not yet sealed into a step.</summary>
        public int PendingCount => pending.Count;

        /// <summary>Highest admission sequence this host assigned; zero means none was assigned yet (P-008).</summary>
        public ulong LastAssignedSequence => nextSequence;

        /// <summary>Duplicates refused because their request key was already admitted (P-037).</summary>
        public int DuplicateCount { get; private set; }

        /// <summary>Admissions refused because the bounded pending queue was full (P-042 backpressure).</summary>
        public int OverflowCount { get; private set; }

        /// <summary>Batches sealed so far, minus those returned by <see cref="Unseal"/>; a paused or idle pump
        /// seals none.</summary>
        public int SealCount { get; private set; }

        /// <summary>Batches that left a remainder queued because their capacity was exhausted (P-037).</summary>
        public int DeferredSealCount { get; private set; }

        /// <summary>Deepest queue this cutoff ever held, so a stable backlog is observable rather than inferred.</summary>
        public int MaxQueueDepth { get; private set; }

        /// <summary>Request keys currently remembered for duplicate detection.</summary>
        public int RememberedKeyCount => admittedKeys.Count;

        /// <summary>
        /// Writes the input-cutoff counters through the fixed compact schema (GC-023): a stable backlog is visible
        /// as the high-water mark, and a refused admission because the bounded queue was full is request overflow
        /// (P-037, P-043).
        /// </summary>
        public void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.ObserveMax(TelemetryCounter.RequestHighWater, MaxQueueDepth);
            into.Add(TelemetryCounter.RequestOverflow, OverflowCount);
            into.Add(TelemetryCounter.StaleResults, DuplicateCount);
        }

        /// <summary>
        /// Admits one unit of demand under a host-assigned monotonic sequence. A duplicate request key is refused
        /// with its original sequence: it returns its recorded result instead of executing again (P-037).
        /// </summary>
        public bool TryAdmit(
            Id128 requestKey,
            SchemaRef schema,
            DemandKind kind,
            out AdmissionSequence sequence,
            out DiagnosticCode code)
        {
            if (requestKey.IsDefault)
            {
                sequence = default(AdmissionSequence);
                code = DiagnosticCode.UnsupportedVersion;
                return false;
            }

            if (admittedKeys.TryGetValue(requestKey, out AdmissionSequence original))
            {
                DuplicateCount++;
                sequence = original;
                code = DiagnosticCode.IdempotencyConflict;
                return false;
            }

            if (pending.Count >= maxPending)
            {
                OverflowCount++;
                sequence = default(AdmissionSequence);
                code = DiagnosticCode.BudgetExceeded;
                return false;
            }

            nextSequence++;
            sequence = new AdmissionSequence(nextSequence);
            pending.Add(new DemandRecord(sequence, requestKey, schema, kind));
            if (pending.Count > MaxQueueDepth)
            {
                MaxQueueDepth = pending.Count;
            }

            // Host admission order is the canonical order, so the remembered window is bounded and explicit
            // rather than an unbounded ledger (the operation ledger owns long-term results).
            if (admittedKeys.Count >= maxRememberedKeys)
            {
                Id128 oldest = default(Id128);
                ulong oldestSequence = ulong.MaxValue;
                foreach (KeyValuePair<Id128, AdmissionSequence> entry in admittedKeys)
                {
                    if (entry.Value.Value < oldestSequence)
                    {
                        oldestSequence = entry.Value.Value;
                        oldest = entry.Key;
                    }
                }

                admittedKeys.Remove(oldest);
            }

            admittedKeys.Add(requestKey, sequence);
            code = DiagnosticCode.None;
            return true;
        }

        /// <summary>Records a refused duplicate without queueing it; the caller already has the original result.</summary>
        public void NoteDuplicate() => DuplicateCount++;

        /// <summary>
        /// Seals the next batch of a logical step: the host-assigned prefix in admission order, at most
        /// <paramref name="capacity"/> units. The unsealed remainder stays queued for a later step (P-036, P-037).
        /// </summary>
        public InputSeal Seal(LogicalStepId step, uint capacity)
        {
            if (capacity == 0U || pending.Count == 0)
            {
                return InputSeal.None(step, pending.Count);
            }

            int take = pending.Count < capacity ? pending.Count : (int)capacity;
            var admitted = new List<DemandRecord>(take);
            int commands = 0;
            int wakes = 0;
            for (int i = 0; i < take; i++)
            {
                DemandRecord record = pending[i];
                admitted.Add(record);
                if (record.IsWake)
                {
                    wakes++;
                }
                else
                {
                    commands++;
                }
            }

            pending.RemoveRange(0, take);
            SealCount++;
            if (pending.Count > 0)
            {
                DeferredSealCount++;
            }

            return new InputSeal(
                step,
                true,
                admitted[0].Sequence,
                admitted[admitted.Count - 1].Sequence,
                commands,
                wakes,
                pending.Count,
                admitted);
        }

        /// <summary>
        /// Returns a sealed batch to the front of the queue because its step never ran: a fixed-step frame below one
        /// step duration, a paused world or a refused step retains its input instead of consuming it (P-036, P-037).
        /// Restored units keep their original admission sequence, so the host order is unchanged.
        /// </summary>
        public int Unseal(InputSeal? seal)
        {
            if (seal == null || !seal.SealedBatch || seal.Admitted.Count == 0)
            {
                return 0;
            }

            pending.InsertRange(0, seal.Admitted);
            if (SealCount > 0)
            {
                SealCount--;
            }

            if (seal.Deferred > 0 && DeferredSealCount > 0)
            {
                DeferredSealCount--;
            }

            if (pending.Count > MaxQueueDepth)
            {
                MaxQueueDepth = pending.Count;
            }

            return seal.Admitted.Count;
        }

        /// <summary>Seals nothing while keeping every queued unit: the paused-world case (P-035, O-26).</summary>
        public InputSeal SealNothing(LogicalStepId step) => InputSeal.None(step, pending.Count);

        /// <summary>Drops every queued unit and forgets the remembered keys; used at teardown (P-048).</summary>
        public int Clear()
        {
            int dropped = pending.Count;
            pending.Clear();
            admittedKeys.Clear();
            nextSequence = 0UL;
            DeferredSealCount = 0;
            MaxQueueDepth = 0;
            SealCount = 0;
            return dropped;
        }

        public override string ToString()
        {
            return "cutoff(pending=" + pending.Count.ToString(CultureInfo.InvariantCulture)
                + ", assigned=" + nextSequence.ToString(CultureInfo.InvariantCulture)
                + ", duplicates=" + DuplicateCount.ToString(CultureInfo.InvariantCulture)
                + ", overflow=" + OverflowCount.ToString(CultureInfo.InvariantCulture) + ")";
        }
    }
}
