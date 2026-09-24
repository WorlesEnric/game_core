// GameCore.Composition — the serialized control lane's operation ledger (P-050, P-051, O-18).
//
// The ledger is the single place that decides admission, idempotency and cancellation cutoffs:
//
//  * Every mutating operation has `OperationId = (WorldId, IssuerId, IssuerSequence)` plus a canonical input
//    hash (P-050). A retransmission with the same id and the same hash returns the original row unchanged.
//  * A reused id with a different hash is an `IdempotencyConflict`: it is rejected and the original row is
//    never overwritten.
//  * First submissions from one issuer use strictly increasing sequences. A sequence at or below the issuer's
//    high-water mark that is not a known retransmission is refused, which is what stops an expired result from
//    being re-executed (P-050).
//  * Retention is bounded in count and (optionally) in logical steps. A dropped result becomes
//    `ResultExpired`, and expiry is distinct from an unknown handle (05 s5).
//  * Cancellation is decided here against the phase cutoff: a pending operation is `Cancelled` with no new
//    epoch; anything already applying or settled is `TooLate` and its terminal result stands (P-051).
//
// Nothing in this file reads a clock or a thread; ordering is the admission ordinal the lane assigned.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>Where one admitted operation currently sits relative to the control-lane cutoff.</summary>
    public enum LedgerPhase
    {
        /// <summary>Admitted and planned; nothing published yet, so cancellation still reaches it.</summary>
        Pending = 0,

        /// <summary>Inside publication; the cancellation cutoff has passed (P-051).</summary>
        Applying = 1,

        /// <summary>Terminal; the recorded outcome can only be retrieved, cancelled or expired.</summary>
        Settled = 2,
    }

    /// <summary>How one admission attempt resolved; the caller needs this to distinguish retry from conflict.</summary>
    public enum AdmissionKind
    {
        /// <summary>A new operation was admitted onto the lane.</summary>
        Fresh = 0,

        /// <summary>The same id and the same input hash: the original row and its result are returned.</summary>
        Retransmission = 1,

        /// <summary>The same id with a different input hash: refused, original row untouched (P-050).</summary>
        IdempotencyConflict = 2,

        /// <summary>The retained result is gone; an expired operation cannot re-execute (P-050).</summary>
        ResultExpired = 3,

        /// <summary>The lane is at capacity; the operation is refused with `BudgetExceeded`.</summary>
        CapacityRejected = 5,
    }

    /// <summary>Result of one admission attempt, including the row the caller must use for status reads.</summary>
    public sealed class AdmissionResult
    {
        public AdmissionResult(AdmissionKind kind, DiagnosticCode code, OperationStatusHandle handle, OperationLedgerEntry? entry)
        {
            Kind = kind;
            Code = code;
            Handle = handle;
            Entry = entry;
        }

        public AdmissionKind Kind { get; }

        /// <summary><see cref="DiagnosticCode.None"/> for a fresh admission or a plain retransmission.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>Handle of the new row, or of the original row for a retransmission or conflict.</summary>
        public OperationStatusHandle Handle { get; }

        /// <summary>Present when a row already existed (retransmission, conflict) or when one was just created.</summary>
        public OperationLedgerEntry? Entry { get; }

        public bool Admitted => Kind == AdmissionKind.Fresh || Kind == AdmissionKind.Retransmission;
    }

    /// <summary>
    /// Bounded ledger of one control lane: admission, idempotency, retention, expiry and cancellation cutoffs.
    /// It stores no live world state and calls nothing; the host owns publication.
    /// </summary>
    public sealed class OperationLedger
    {
        private readonly ControlLaneCapacitySettings capacity;
        private readonly OperationExpirySettings expiry;
        private readonly Dictionary<OperationId, LedgerRow> rows = new Dictionary<OperationId, LedgerRow>();
        private readonly List<OperationId> settledOrder = new List<OperationId>();
        private readonly LinkedList<OperationId> expired = new LinkedList<OperationId>();
        private readonly Dictionary<Id128, ulong> issuerHighWater = new Dictionary<Id128, ulong>();
        private ulong nextAdmissionOrdinal;

        public OperationLedger(ControlLaneCapacitySettings capacity, OperationExpirySettings expiry)
        {
            this.capacity = capacity ?? throw new ArgumentNullException(nameof(capacity));
            this.expiry = expiry ?? throw new ArgumentNullException(nameof(expiry));
        }

        public int RowCount => rows.Count;

        public int ExpiredCount => expired.Count;

        public int AdmittedCount { get; private set; }

        public int DuplicateCount { get; private set; }

        public int ConflictCount { get; private set; }

        public int SequenceViolationCount { get; private set; }

        public int CapacityRejectedCount { get; private set; }

        public int ExpireCount { get; private set; }

        public int CancelledCount { get; private set; }

        public int TooLateCount { get; private set; }

        /// <summary>Latest published revision this lane admitted against; a handle's base revision (P-027).</summary>
        public CompositionRevision PublishedRevision { get; private set; } = CompositionRevision.Zero;

        public AssemblyEpoch PublishedEpoch { get; private set; } = AssemblyEpoch.Zero;

        /// <summary>Last conflict code observed, or <see cref="DiagnosticCode.None"/> (inspection aid).</summary>
        public DiagnosticCode LastConflictCode { get; private set; }

        /// <summary>Pending rows in admission order; only these are still cancellable (P-051).</summary>
        public IReadOnlyList<OperationId> PendingInAdmissionOrder()
        {
            List<LedgerRow> pending = new List<LedgerRow>();
            foreach (KeyValuePair<OperationId, LedgerRow> pair in rows)
            {
                if (pair.Value.Phase == LedgerPhase.Pending)
                {
                    pending.Add(pair.Value);
                }
            }

            pending.Sort(CompareRows);
            List<OperationId> order = new List<OperationId>(pending.Count);
            for (int i = 0; i < pending.Count; i++)
            {
                order.Add(pending[i].Operation);
            }

            return order;
        }

        /// <summary>Every retained row in admission order, for inspection and evidence.</summary>
        public IReadOnlyList<OperationLedgerEntry> Entries()
        {
            List<LedgerRow> all = new List<LedgerRow>(rows.Values);
            all.Sort(CompareRows);
            List<OperationLedgerEntry> entries = new List<OperationLedgerEntry>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                entries.Add(all[i].ToEntry());
            }

            return entries;
        }

        /// <summary>
        /// True when a result was retained and has since been dropped: `Expired` is distinct from `Unknown`
        /// (05 s5), and an expired operation id is refused rather than re-executed (P-050).
        /// </summary>
        public bool IsExpired(OperationId operation) =>
            !rows.ContainsKey(operation) &&
            issuerHighWater.TryGetValue(operation.IssuerId, out ulong highWater) &&
            operation.IssuerSequence <= highWater;

        public bool TryGet(OperationId operation, out OperationLedgerEntry? entry)
        {
            if (rows.TryGetValue(operation, out LedgerRow? row))
            {
                entry = row.ToEntry();
                return true;
            }

            entry = null;
            return false;
        }

        /// <summary>Retrieves one row for the host's publication path; null when the row is gone.</summary>
        public LedgerRow? RowOf(OperationId operation) =>
            rows.TryGetValue(operation, out LedgerRow? row) ? row : null;

        /// <summary>
        /// One admission attempt. The row is created only for a fresh admission; a retransmission and a conflict
        /// both return the original row, so a conflicting reuse can never overwrite a stored outcome (P-050).
        /// </summary>
        public AdmissionResult Admit(OperationId operation, ContentHash inputHash, CompositionEditPayload? payload)
        {
            if (rows.TryGetValue(operation, out LedgerRow? existing))
            {
                if (existing.InputHash.Equals(inputHash))
                {
                    DuplicateCount++;
                    return new AdmissionResult(AdmissionKind.Retransmission, DiagnosticCode.None, existing.Handle, existing.ToEntry());
                }

                ConflictCount++;
                LastConflictCode = DiagnosticCode.IdempotencyConflict;
                return new AdmissionResult(AdmissionKind.IdempotencyConflict, DiagnosticCode.IdempotencyConflict, existing.Handle, existing.ToEntry());
            }

            if (IsExpired(operation))
            {
                if (!expired.Contains(operation))
                {
                    SequenceViolationCount++;
                }

                ExpireCount++;
                return new AdmissionResult(
                    AdmissionKind.ResultExpired,
                    DiagnosticCode.ResultExpired,
                    new OperationStatusHandle(operation, PublishedRevision),
                    null);
            }

            if (rows.Count >= capacity.MaxQueuedOperations)
            {
                // The lane is at capacity; refusal is a value, not a queue that grows without bound.
                CapacityRejectedCount++;
                return new AdmissionResult(
                    AdmissionKind.CapacityRejected,
                    DiagnosticCode.BudgetExceeded,
                    new OperationStatusHandle(operation, PublishedRevision),
                    null);
            }

            LedgerRow row = new LedgerRow(operation, inputHash, payload, nextAdmissionOrdinal, PublishedRevision);
            nextAdmissionOrdinal++;
            rows.Add(operation, row);
            issuerHighWater[operation.IssuerId] = operation.IssuerSequence;
            AdmittedCount++;
            return new AdmissionResult(AdmissionKind.Fresh, DiagnosticCode.None, row.Handle, row.ToEntry());
        }

        /// <summary>Sets the phase of a pending row before publication begins, passing the cancellation cutoff.</summary>
        public bool BeginApplying(OperationId operation)
        {
            LedgerRow? row = RowOf(operation);
            if (row == null || row.Phase != LedgerPhase.Pending)
            {
                return false;
            }

            row.Phase = LedgerPhase.Applying;
            return true;
        }

        /// <summary>Settles a row with its terminal outcome and this lane's publication result.</summary>
        public bool Settle(
            OperationId operation,
            Outcome outcome,
            DiagnosticCode code,
            CompositionRevision publishedRevision,
            AssemblyEpoch publishedEpoch,
            SnapshotToken? publishedSnapshot,
            LogicalStepId settledStep)
        {
            LedgerRow? row = RowOf(operation);
            if (row == null || row.Phase == LedgerPhase.Settled)
            {
                return false;
            }

            row.Phase = LedgerPhase.Settled;
            row.Outcome = outcome;
            row.Code = code;
            row.PublishedRevision = publishedRevision;
            row.PublishedEpoch = publishedEpoch;
            row.PublishedSnapshot = publishedSnapshot;
            row.SettledStep = settledStep;

            // The lane's published baseline only moves for a publication; a rejection or cancellation keeps it.
            if (outcome == Outcome.Published || outcome == Outcome.PublishedWithCleanupErrors)
            {
                PublishedRevision = publishedRevision;
                PublishedEpoch = publishedEpoch;
            }

            settledOrder.Add(operation);
            TrimRetention();
            return true;
        }

        /// <summary>Cancels a pending row inside the cutoff; every later phase is `TooLate` (P-051, O-18).</summary>
        public CancelOutcome Cancel(OperationId target)
        {
            LedgerRow? row = RowOf(target);
            if (row == null)
            {
                return IsExpired(target) ? CancelOutcome.ResultExpired : CancelOutcome.Unknown;
            }

            switch (row.Phase)
            {
                case LedgerPhase.Pending:
                    row.Phase = LedgerPhase.Settled;
                    row.Outcome = Outcome.Cancelled;
                    row.Code = DiagnosticCode.Cancelled;
                    settledOrder.Add(target);
                    CancelledCount++;
                    TrimRetention();
                    return CancelOutcome.Cancelled;
                case LedgerPhase.Applying:
                    TooLateCount++;
                    return CancelOutcome.TooLate;
                default:
                    if (row.Outcome == Outcome.Cancelled)
                    {
                        // A repeated cancel returns the same terminal result; it never implies a rollback.
                        return CancelOutcome.Cancelled;
                    }

                    TooLateCount++;
                    return CancelOutcome.TooLate;
            }
        }

        /// <summary>
        /// Advances the retention window in logical steps: a settled row older than the configured window
        /// expires. A step-based window is only applied when the host configured one (P-050).
        /// </summary>
        public int AdvanceRetention(LogicalStepId step)
        {
            if (expiry.RetainedResultSteps == 0UL)
            {
                return 0;
            }

            List<OperationId> toExpire = new List<OperationId>();
            for (int i = 0; i < settledOrder.Count; i++)
            {
                LedgerRow? row = RowOf(settledOrder[i]);
                if (row == null || row.Phase != LedgerPhase.Settled)
                {
                    continue;
                }

                ulong age = step.Value >= row.SettledStep.Value ? step.Value - row.SettledStep.Value : 0UL;
                if (age > expiry.RetainedResultSteps)
                {
                    toExpire.Add(row.Operation);
                }
            }

            for (int i = 0; i < toExpire.Count; i++)
            {
                Expire(toExpire[i]);
            }

            return toExpire.Count;
        }

        private void TrimRetention()
        {
            while (settledOrder.Count > capacity.MaxRetainedResults)
            {
                OperationId oldest = settledOrder[0];
                settledOrder.RemoveAt(0);
                Expire(oldest);
            }
        }

        private void Expire(OperationId operation)
        {
            if (!rows.Remove(operation))
            {
                return;
            }
            settledOrder.Remove(operation);

            expired.AddLast(operation);
            while (expired.Count > capacity.MaxRetainedResults)
            {
                // The tombstone list is bounded too; the issuer high-water mark still prevents re-execution.
                expired.RemoveFirst();
            }

            ExpireCount++;
        }

        private static int CompareRows(LedgerRow left, LedgerRow right) => left.AdmissionOrdinal.CompareTo(right.AdmissionOrdinal);

        /// <summary>One mutable ledger row. Only this assembly's host inspects it directly.</summary>
        public sealed class LedgerRow
        {
            public LedgerRow(OperationId operation, ContentHash inputHash, CompositionEditPayload? payload, ulong admissionOrdinal, CompositionRevision submittedAgainst)
            {
                Operation = operation;
                InputHash = inputHash;
                Payload = payload;
                AdmissionOrdinal = admissionOrdinal;
                Outcome = Outcome.Pending;
                Handle = new OperationStatusHandle(operation, submittedAgainst);
            }

            public OperationId Operation { get; }

            public ContentHash InputHash { get; }

            /// <summary>Decoded declaration of this operation; retained so a cancelled plan can be rebuilt.</summary>
            public CompositionEditPayload? Payload { get; }

            public ulong AdmissionOrdinal { get; }

            public LedgerPhase Phase { get; set; }

            public Outcome Outcome { get; set; }

            public DiagnosticCode Code { get; set; }

            public CompositionRevision PublishedRevision { get; set; }

            public AssemblyEpoch PublishedEpoch { get; set; }

            public SnapshotToken? PublishedSnapshot { get; set; }

            public LogicalStepId SettledStep { get; set; }

            /// <summary>The staged proposal of this operation, inspected by tests and staging diagnostics.</summary>
            public CompositionEditPlan? Plan { get; set; }

            public CleanupReport Cleanup { get; set; } = CleanupReport.Empty;

            public OperationStatusHandle Handle { get; }

            public OperationLedgerEntry ToEntry() =>
                new OperationLedgerEntry(Handle, InputHash, Outcome, Code, PublishedRevision, PublishedEpoch, PublishedSnapshot);
        }
    }
}
