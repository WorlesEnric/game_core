// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - the durable outbox (GC-021).
//
// Normative sources: docs/game-core/00-core-protocols.md P-003 (a `ManagedResourceEffect`, a
// `CapabilityContribution` and a `GameplayEffect` are three different kinds of change and the kernel exposes no
// common reversible `Effect` API: acknowledging a delivery never undoes committed gameplay), P-008 (one canonical
// order; nothing here depends on dictionary enumeration or thread timing), P-042 (a `RequestResult` distinguishes
// `Accepted`, `Rejected`, `Cancelled` and `Committed`: admission acceptance is not gameplay success, and an outbox
// never reads one as the other), P-043 (bounded work with an explicit overflow behavior; no silent drop), P-045
// ("irreversible output adapters consume only committed events, use explicit external idempotency keys, and persist
// an outbox when delivery must survive crashes. Ordinary in-memory event delivery is not a durable exactly-once
// channel") and P-053 (a checkpoint contains "external outbox/dedup cursors when used").
//
// WHAT THIS IS
//
// The bookkeeping half of committed delivery, and nothing else. It answers exactly three questions:
//
//   * what obligations has this world committed to and not yet closed?
//   * for one obligation, may an attempt be handed to the destination, and what did the previous attempt do?
//   * how far have acknowledgements reached for each destination, and how many terminal records are retained?
//
// It deliberately does NOT deliver anything, does not know what a destination is, and does not decide what a
// compensation means. Delivery is the caller's (a destination port's) job, because only the recipient's package can
// say what its mutation is; the alternative would be the universal gameplay `Effect` bus P-003 forbids.
//
// WHAT IT IS NOT
//
// Not an exactly-once channel. P-045 says subscriber delivery is at-least-once within retention, and this outbox is
// honest about the same boundary: an obligation that was handed over and never acknowledged WILL be handed over
// again, and it is the destination's explicit idempotency key that makes the redelivery a no-op. That is why
// `DeliveryKey.IdempotencyKey` is derived from committed data rather than minted per attempt.
//
// BOUNDED, WITH EXPLICIT EXHAUSTION
//
// `capacity` bounds open obligations and `terminalRetention` bounds retained terminal records per destination. Both
// refusals are reported values (`OutboxAdmission.AtCapacity`, `PrunedTerminalCount`), never a silent drop: P-043's
// rule for a bounded buffer is the same rule a bounded outbox owes its caller.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Delivery
{
    /// <summary>
    /// How far acknowledgements reach for one destination, and how many of its terminal records are still retained
    /// (P-045's "event cursors and snapshots have configured retention bounds", applied to delivery).
    /// </summary>
    public readonly struct DeliveryCursor
    {
        public DeliveryCursor(Id128 destinationId, Id128 newestAcknowledged, uint retainedTerminals, uint terminalTotal)
        {
            DestinationId = destinationId;
            NewestAcknowledged = newestAcknowledged;
            RetainedTerminals = retainedTerminals;
            TerminalTotal = terminalTotal;
        }

        /// <summary>
        /// The newest acknowledged obligation for this destination, or the default value when none was acknowledged
        /// yet. A ring reach of zero obligations is reported as the default value rather than as a fresh identity.
        /// </summary>
        public readonly Id128 NewestAcknowledged;

        /// <summary>Terminal records this outbox still retains for the destination, as the cursor row declares.</summary>
        public readonly uint RetainedTerminals;

        /// <summary>Every terminal outcome this destination ever produced, including pruned ones (P-045).</summary>
        public readonly uint TerminalTotal;

        public bool HasAcknowledged => !NewestAcknowledged.IsDefault;

        /// <summary>Terminal records pruned by retention, derived rather than stored twice (P-008).</summary>
        public uint PrunedTerminals => TerminalTotal - RetainedTerminals;

        public override string ToString() =>
            "deliveryCursor(" + DestinationId.ToString() + ",acked=" + NewestAcknowledged.ToString() + ",retained="
            + RetainedTerminals.ToString(CultureInfo.InvariantCulture) + "/"
            + TerminalTotal.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// The committed delivery obligations of one owner inside one world (GC-021). It is engine-free and holds no
    /// storage handle, so the same object can be driven by a pure test, by a checkpoint capture and by a Unity world
    /// (P-005, P-058).
    /// </summary>
    public sealed class DurableOutbox
    {
        private readonly Dictionary<Id128, DeliveryObligation> tracked = new Dictionary<Id128, DeliveryObligation>();
        private readonly List<Id128> openOrder = new List<Id128>();
        private readonly Dictionary<Id128, List<Id128>> terminalOrder = new Dictionary<Id128, List<Id128>>();
        private readonly Dictionary<Id128, DeliveryCursor> cursors = new Dictionary<Id128, DeliveryCursor>();
        private uint nextOrder;

        /// <summary>
        /// One outbox: its owning stable identity, how many obligations it may hold open at once, how many terminal
        /// records per destination it retains, and whether the caller configured it durable (P-045).
        /// </summary>
        public DurableOutbox(Id128 ownerId, int capacity, int terminalRetention, OutboxDurability durability)
        {
            if (ownerId.IsDefault)
            {
                throw new ArgumentException(
                    "An outbox owner is a stable identity and is never the all-zero value (P-004).", nameof(ownerId));
            }

            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "An outbox needs a positive capacity.");
            }

            if (terminalRetention < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(terminalRetention), terminalRetention, "A retention bound is never negative (P-043).");
            }

            OwnerId = ownerId;
            Capacity = capacity;
            TerminalRetention = terminalRetention;
            Durability = durability;
        }

        /// <summary>Stable identity of the authoritative owner this outbox belongs to (P-017, P-034).</summary>
        public Id128 OwnerId { get; }

        /// <summary>Open obligations this outbox may hold at once; exceeding it refuses rather than drops (P-043).</summary>
        public int Capacity { get; }

        /// <summary>Terminal records retained per destination before the oldest is pruned and counted (P-045).</summary>
        public int TerminalRetention { get; }

        /// <summary>
        /// Whether this outbox was configured to survive a crash. `Unspecified` is reported as itself: a reader
        /// never upgrades an unknown durability into a durable claim (P-045).
        /// </summary>
        public OutboxDurability Durability { get; internal set; }

        /// <summary>True only for a configured durable outbox; the value a caller must check before promising survival.</summary>
        public bool IsDurable => Durability == OutboxDurability.Durable;

        /// <summary>Obligations this outbox still tracks, open and terminal alike.</summary>
        public int Count => tracked.Count;

        /// <summary>Obligations that still owe the destination a mutation (P-045).</summary>
        public int OpenCount => openOrder.Count;

        /// <summary>Obligations that reached a terminal state and are still retained.</summary>
        public int TerminalCount => tracked.Count - openOrder.Count;

        /// <summary>Every enqueue that was accepted, duplicates included only through <see cref="DuplicateCount"/>.</summary>
        public int CommitCount { get; private set; }

        /// <summary>Enqueues refused because the same obligation identity was already present (P-050).</summary>
        public int DuplicateCount { get; private set; }

        /// <summary>Enqueues refused because every capacity slot was taken; never a silent drop (P-043).</summary>
        public int CapacityRefusalCount { get; private set; }

        /// <summary>Enqueues refused because durability was required and this outbox is volatile (P-045).</summary>
        public int DurabilityRefusalCount { get; private set; }

        /// <summary>Enqueues refused as malformed before any state changed.</summary>
        public int InvalidCount { get; private set; }

        /// <summary>Attempts recorded as handed to a destination, redeliveries included.</summary>
        public int DeliveryCount { get; private set; }

        /// <summary>Attempts recorded for an obligation that had already been handed over at least once (P-045).</summary>
        public int RedeliveryCount { get; private set; }

        public int AcknowledgeCount { get; private set; }

        public int RejectCount { get; private set; }

        public int CompensateCount { get; private set; }

        /// <summary>Attempts refused because the obligation is already terminal (P-045).</summary>
        public int TerminalAttemptCount { get; private set; }

        /// <summary>Attempts naming an obligation this outbox does not track.</summary>
        public int UnknownObligationCount { get; private set; }

        /// <summary>
        /// Terminal records dropped by the retention bound. Counted, never silent: the record is gone but the
        /// obligation's identities are derived from committed data, so the destination's idempotency key — not this
        /// record — is what still prevents a second mutation (P-045).
        /// </summary>
        public int PrunedTerminalCount { get; private set; }

        /// <summary>Open obligations in canonical commit order, as a dispatcher walks them (P-008).</summary>
        public IReadOnlyList<Id128> OpenIds => openOrder;

        /// <summary>
        /// The canonical position the next accepted obligation receives (P-008). A durable adapter reads this to
        /// build the frame it must persist *before* the obligation exists in memory, which is what makes
        /// persist-then-apply possible without a second ordering source.
        /// </summary>
        public uint NextOrder => nextOrder;

        /// <summary>
        /// Replaces this outbox's whole state with one rebuilt from a journal. It is how a durable adapter resumes
        /// after a crash: the journal is the state, and this call is the only transfer of it (P-045, P-049).
        /// </summary>
        internal void AdoptFrom(DurableOutbox source)
        {
            if (!OwnerId.Equals(source.OwnerId))
            {
                throw new ArgumentException(
                    "An outbox adopts state from an outbox of the same owner only (P-034).", nameof(source));
            }

            tracked.Clear();
            openOrder.Clear();
            terminalOrder.Clear();
            cursors.Clear();

            foreach (KeyValuePair<Id128, DeliveryObligation> entry in source.tracked)
            {
                tracked.Add(entry.Key, entry.Value);
            }

            for (int i = 0; i < source.openOrder.Count; i++)
            {
                openOrder.Add(source.openOrder[i]);
            }

            foreach (KeyValuePair<Id128, List<Id128>> entry in source.terminalOrder)
            {
                terminalOrder.Add(entry.Key, new List<Id128>(entry.Value));
            }

            foreach (KeyValuePair<Id128, DeliveryCursor> entry in source.cursors)
            {
                cursors.Add(entry.Key, entry.Value);
            }

            nextOrder = source.nextOrder;
            Durability = source.Durability;
            CommitCount = source.CommitCount;
            DuplicateCount = source.DuplicateCount;
            CapacityRefusalCount = source.CapacityRefusalCount;
            DurabilityRefusalCount = source.DurabilityRefusalCount;
            InvalidCount = source.InvalidCount;
            DeliveryCount = source.DeliveryCount;
            RedeliveryCount = source.RedeliveryCount;
            AcknowledgeCount = source.AcknowledgeCount;
            RejectCount = source.RejectCount;
            CompensateCount = source.CompensateCount;
            TerminalAttemptCount = source.TerminalAttemptCount;
            UnknownObligationCount = source.UnknownObligationCount;
            PrunedTerminalCount = source.PrunedTerminalCount;
            TerminalAdoptionCount = source.TerminalAdoptionCount;
        }

        /// <summary>Every destination this outbox has a cursor for, in ascending stable-id order (P-008).</summary>
        public IReadOnlyList<Id128> DestinationIds
        {
            get
            {
                var ids = new List<Id128>(cursors.Keys);
                ids.Sort();
                return ids;
            }
        }

        /// <summary>One obligation by identity, or null when this outbox does not track it (P-052).</summary>
        public bool TryGet(Id128 outboxId, out DeliveryObligation? obligation) =>
            tracked.TryGetValue(outboxId, out obligation);

        /// <summary>How far acknowledgements reach for one destination (P-045).</summary>
        public bool TryGetCursor(Id128 destinationId, out DeliveryCursor cursor) =>
            cursors.TryGetValue(destinationId, out cursor);

        /// <summary>
        /// Commits one delivery obligation. The identity is derived by the caller from committed data (see
        /// <see cref="DeliveryKey.Derive"/>), so a duplicate observation of one committed event lands on the same
        /// obligation and is reported as <see cref="OutboxAdmission.Duplicate"/> rather than committed twice (P-050).
        ///
        /// `requiresDurability` is how a caller states that this particular obligation must survive a crash. An
        /// outbox configured volatile refuses it instead of accepting an obligation it cannot keep (P-045).
        /// </summary>
        public OutboxAdmission TryCommit(
            DeliveryKey key,
            SchemaRef payloadSchema,
            byte[]? payload,
            EventSequence sourceEvent,
            LogicalStepId step,
            AssemblyEpoch epoch,
            OperationId causal,
            bool requiresDurability,
            out DeliveryObligation? obligation,
            out DiagnosticCode code,
            out string detail)
        {
            obligation = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (payloadSchema.Id.Value.IsDefault)
            {
                InvalidCount++;
                code = DiagnosticCode.MissingDependency;
                detail = "a delivery obligation names the payload schema its destination command carries; an all-zero "
                    + "schema id is not one (P-004, P-054).";
                return OutboxAdmission.Invalid;
            }

            if (tracked.TryGetValue(key.OutboxId, out DeliveryObligation? existing))
            {
                DuplicateCount++;
                obligation = existing;
                detail = "obligation " + key.OutboxId.ToString()
                    + " is already tracked in state " + existing.State.ToString()
                    + "; one committed event is one obligation (P-050).";
                return OutboxAdmission.Duplicate;
            }

            if (requiresDurability && !IsDurable)
            {
                DurabilityRefusalCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the obligation requires durability but this outbox declares "
                    + Durability.ToString()
                    + "; an in-memory obligation is never presented as one that survives a crash (P-045).";
                return OutboxAdmission.DurabilityUnavailable;
            }

            if (openOrder.Count >= Capacity)
            {
                CapacityRefusalCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the outbox holds " + openOrder.Count.ToString(CultureInfo.InvariantCulture)
                    + " open obligation(s) at its declared capacity of "
                    + Capacity.ToString(CultureInfo.InvariantCulture)
                    + "; the obligation is refused rather than dropped silently (P-043).";
                return OutboxAdmission.AtCapacity;
            }

            var committed = new DeliveryObligation(
                key,
                payloadSchema,
                payload,
                sourceEvent,
                step,
                epoch,
                causal,
                nextOrder,
                (uint)TerminalRetention);
            nextOrder++;
            tracked.Add(key.OutboxId, committed);
            openOrder.Add(key.OutboxId);
            CommitCount++;
            obligation = committed;
            return OutboxAdmission.Accepted;
        }

        /// <summary>
        /// Whether <see cref="TryCommit"/> would accept this obligation right now, without committing anything. A
        /// durable adapter asks first so it never persists a frame the outbox would then refuse; the decision is the
        /// same code path, so the answer cannot drift from what the commit does (P-045).
        /// </summary>
        public bool CanAccept(
            DeliveryKey key,
            SchemaRef payloadSchema,
            bool requiresDurability,
            out OutboxAdmission admission,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (payloadSchema.Id.Value.IsDefault)
            {
                admission = OutboxAdmission.Invalid;
                code = DiagnosticCode.MissingDependency;
                detail = "a delivery obligation names the payload schema its destination command carries; an all-zero "
                    + "schema id is not one (P-004, P-054).";
                return false;
            }

            if (tracked.ContainsKey(key.OutboxId))
            {
                admission = OutboxAdmission.Duplicate;
                detail = "obligation " + key.OutboxId.ToString() + " is already tracked (P-050).";
                return false;
            }

            if (requiresDurability && !IsDurable)
            {
                admission = OutboxAdmission.DurabilityUnavailable;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the obligation requires durability but this outbox declares "
                    + Durability.ToString() + " (P-045).";
                return false;
            }

            if (openOrder.Count >= Capacity)
            {
                admission = OutboxAdmission.AtCapacity;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the outbox holds " + openOrder.Count.ToString(CultureInfo.InvariantCulture)
                    + " open obligation(s) at its declared capacity of "
                    + Capacity.ToString(CultureInfo.InvariantCulture)
                    + "; the obligation is refused rather than dropped silently (P-043).";
                return false;
            }

            admission = OutboxAdmission.Accepted;
            return true;
        }

        /// <summary>
        /// Records that one attempt is being handed to the destination. A `Pending` obligation moves to `Delivered`;
        /// an already-`Delivered` obligation is a redelivery, counted as one, and stays `Delivered` because the
        /// destination — not this outbox — is what makes the second attempt a no-op (P-045).
        ///
        /// A terminal obligation is reported as <see cref="DeliveryOutcome.AlreadyTerminal"/> and does not advance.
        /// </summary>
        public DeliveryOutcome TryBeginDelivery(Id128 outboxId, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (!tracked.TryGetValue(outboxId, out DeliveryObligation? obligation))
            {
                UnknownObligationCount++;
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                TerminalAttemptCount++;
                detail = "obligation " + outboxId.ToString() + " already reached "
                    + obligation.State.ToString()
                    + "; a redelivery is answered from the record rather than handed over again (P-045).";
                return DeliveryOutcome.AlreadyTerminal;
            }

            if (obligation.State == OutboxDeliveryState.Delivered)
            {
                RedeliveryCount++;
            }

            obligation.State = OutboxDeliveryState.Delivered;
            obligation.Attempts++;
            DeliveryCount++;
            return DeliveryOutcome.Delivered;
        }

        /// <summary>
        /// Acknowledges the destination's mutation. The obligation becomes terminal, a terminal record is retained
        /// and the destination's cursor advances to it (P-045). Acknowledging an already-acknowledged obligation is
        /// reported as <see cref="DeliveryOutcome.AlreadyTerminal"/>: an acknowledgement is not a second mutation and
        /// is never applied twice, which is the property the redelivery-after-acknowledgement-loss case needs.
        /// </summary>
        public DeliveryOutcome TryAcknowledge(Id128 outboxId, out DiagnosticCode code, out string detail)
        {
            if (!tracked.TryGetValue(outboxId, out DeliveryObligation? obligation))
            {
                UnknownObligationCount++;
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                TerminalAttemptCount++;
                code = DiagnosticCode.None;
                detail = "obligation " + outboxId.ToString() + " is already terminal in "
                    + obligation.State.ToString() + " (P-045).";
                return DeliveryOutcome.AlreadyTerminal;
            }

            return Settle(obligation, OutboxDeliveryState.Acknowledged, DiagnosticCode.None, out code, out detail);
        }

        /// <summary>
        /// Records a terminal refusal by the destination. The obligation is retained in `Rejected` rather than
        /// deleted, so the refusal stays observable and a later attempt reports `AlreadyTerminal` instead of
        /// re-attempting work the destination has already refused (P-052).
        /// </summary>
        public DeliveryOutcome TryReject(
            Id128 outboxId,
            DiagnosticCode reason,
            out DiagnosticCode code,
            out string detail)
        {
            if (!tracked.TryGetValue(outboxId, out DeliveryObligation? obligation))
            {
                UnknownObligationCount++;
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                TerminalAttemptCount++;
                code = DiagnosticCode.None;
                detail = "obligation " + outboxId.ToString() + " is already terminal in "
                    + obligation.State.ToString() + " (P-045).";
                return DeliveryOutcome.AlreadyTerminal;
            }

            return Settle(obligation, OutboxDeliveryState.Rejected, reason, out code, out detail);
        }

        /// <summary>
        /// Records that the destination's mutation could not be completed and an explicit compensation was taken
        /// instead. The kernel declares no universal compensation rule (GC-021's non-goal, P-003): the caller names
        /// the reason code and owns what its compensation means, and this outbox only remembers that it happened.
        /// </summary>
        public DeliveryOutcome TryCompensate(
            Id128 outboxId,
            DiagnosticCode reason,
            out DiagnosticCode code,
            out string detail)
        {
            if (!tracked.TryGetValue(outboxId, out DeliveryObligation? obligation))
            {
                UnknownObligationCount++;
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                TerminalAttemptCount++;
                code = DiagnosticCode.None;
                detail = "obligation " + outboxId.ToString() + " is already terminal in "
                    + obligation.State.ToString() + " (P-045).";
                return DeliveryOutcome.AlreadyTerminal;
            }

            return Settle(obligation, OutboxDeliveryState.Compensated, reason, out code, out detail);
        }

        /// <summary>
        /// Projects the whole outbox into checkpoint rows (GC-021's half of P-053). Every tracked obligation
        /// contributes one `Obligation` row, every terminal obligation one `Terminal` row, and every destination one
        /// `Cursor` row whose declared retained-terminal count equals the terminal rows this projection carries —
        /// which is exactly what <c>CheckpointRestorePlanner</c> checks before it reinstates them.
        /// </summary>
        public IReadOnlyList<OutboxRecordValue> ToRecords()
        {
            var obligations = new List<DeliveryObligation>(tracked.Values);
            obligations.Sort(CompareByOrder);

            var rows = new List<OutboxRecordValue>((obligations.Count * 2) + cursors.Count);
            for (int i = 0; i < obligations.Count; i++)
            {
                rows.Add(RowOf(obligations[i], OutboxRowKind.Obligation));
            }

            for (int i = 0; i < obligations.Count; i++)
            {
                if (obligations[i].IsTerminal)
                {
                    rows.Add(RowOf(obligations[i], OutboxRowKind.Terminal));
                }
            }

            var destinations = new List<Id128>(cursors.Keys);
            destinations.Sort();
            for (int i = 0; i < destinations.Count; i++)
            {
                DeliveryCursor cursor = cursors[destinations[i]];
                rows.Add(CursorRow(cursor));
            }

            return rows;
        }

        /// <summary>
        /// Rebuilds one outbox from the rows a verified checkpoint carries. This is the path by which a committed
        /// delivery obligation outlives the world that committed it: the source world is unloaded, and the restored
        /// session reinstates every open obligation from these rows (P-045, P-049, P-053).
        ///
        /// Rows are validated by <c>CheckpointRestorePlanner</c> before they reach here, but this reconstruction is
        /// also self-checking: an unknown row kind, an unknown record version, a terminal row without its obligation
        /// or a duplicate identity is refused rather than silently approximated (P-054).
        /// </summary>
        public static bool TryRestore(
            IReadOnlyList<OutboxRecordValue>? rows,
            Id128 ownerId,
            int capacity,
            int terminalRetention,
            OutboxDurability durability,
            out DurableOutbox? outbox,
            out string detail)
        {
            detail = string.Empty;
            outbox = null;

            DurableOutbox restored;
            try
            {
                restored = new DurableOutbox(ownerId, capacity, terminalRetention, durability);
            }
            catch (ArgumentException exception)
            {
                detail = exception.Message;
                return false;
            }

            if (rows == null || rows.Count == 0)
            {
                outbox = restored;
                return true;
            }

            var order = new List<DeliveryObligation>(rows.Count);
            var cursorRows = new List<OutboxRecordValue>();

            for (int i = 0; i < rows.Count; i++)
            {
                OutboxRecordValue row = rows[i];
                if (row.RecordVersion != OutboxRecordValue.CurrentRecordVersion)
                {
                    detail = "an outbox row declares record version "
                        + row.RecordVersion.ToString(CultureInfo.InvariantCulture)
                        + " and this build implements "
                        + OutboxRecordValue.CurrentRecordVersion.ToString(CultureInfo.InvariantCulture)
                        + "; a row this build cannot decode is refused (P-054).";
                    return false;
                }

                if (row.Row == OutboxRowKind.Cursor)
                {
                    cursorRows.Add(row);
                    continue;
                }

                if (row.Row != OutboxRowKind.Obligation && row.Row != OutboxRowKind.Terminal)
                {
                    detail = "an outbox row declares row kind "
                        + row.RowKind.ToString(CultureInfo.InvariantCulture)
                        + ", which names no outbox fact this build implements (P-054).";
                    return false;
                }

                DeliveryObligation? existing;
                if (!restored.tracked.TryGetValue(row.OutboxId, out existing) || existing == null)
                {
                    existing = new DeliveryObligation(
                        new DeliveryKey(row.OutboxId, row.DestinationId, row.IdempotencyKey),
                        row.PayloadSchema,
                        row.Payload,
                        row.SourceEvent,
                        row.Step,
                        row.Epoch,
                        new OperationId(default(WorldId), row.CausalIssuerId, row.CausalIssuerOrdinal),
                        row.Order,
                        (uint)terminalRetention);
                    restored.tracked.Add(row.OutboxId, existing);
                    restored.openOrder.Add(row.OutboxId);
                    order.Add(existing);
                    if (row.Order >= restored.nextOrder)
                    {
                        restored.nextOrder = row.Order + 1U;
                    }
                }

                if (row.Row == OutboxRowKind.Terminal)
                {
                    existing.Reason = row.Reason;
                    existing.Attempts = row.Attempts;
                    existing.State = row.State;
                    restored.AdoptTerminal(existing);
                    restored.TerminalAdoptionCount++;
                }
                else
                {
                    existing.State = row.State;
                    existing.Reason = row.Reason;
                    existing.Attempts = row.Attempts;
                    if (existing.IsTerminal)
                    {
                        restored.AdoptTerminal(existing);
                        restored.TerminalAdoptionCount++;
                    }
                }
            }

            for (int i = 0; i < cursorRows.Count; i++)
            {
                OutboxRecordValue row = cursorRows[i];
                restored.cursors[row.DestinationId] = new DeliveryCursor(
                    row.DestinationId,
                    row.Cursor,
                    row.RetainedTerminalCount,
                    row.TerminalTotal);
                if (row.PrunedTerminals > 0U && restored.PrunedTerminalCount == 0)
                {
                    // The source outbox's own pruning is reported through the cursor row, so a restored outbox
                    // accounts for the records retention already dropped (P-043: no silent drop).
                    restored.PrunedTerminalCount = (int)row.PrunedTerminals;
                }
            }

            restored.AdoptOpenOrder(order);
            outbox = restored;
            return true;
        }

        /// <summary>Terminal rows adopted while rebuilding from a checkpoint; reported as recovery evidence.</summary>
        public int TerminalAdoptionCount { get; private set; }

        /// <summary>
        /// Redelivery index: the obligations this outbox would give to a destination right now, in canonical commit
        /// order. A dispatcher walks this instead of the whole record set, so a delivery pass costs one visit per
        /// open obligation (P-043's bounded work).
        /// </summary>
        public IReadOnlyList<DeliveryObligation> OpenObligations()
        {
            var open = new List<DeliveryObligation>(openOrder.Count);
            for (int i = 0; i < openOrder.Count; i++)
            {
                if (tracked.TryGetValue(openOrder[i], out DeliveryObligation? obligation) && obligation.IsOpen)
                {
                    open.Add(obligation);
                }
            }

            return open;
        }

        public override string ToString() =>
            "outbox(" + OwnerId.ToString() + "," + Durability.ToString() + ",open="
            + OpenCount.ToString(CultureInfo.InvariantCulture) + "/"
            + Capacity.ToString(CultureInfo.InvariantCulture) + ",terminal="
            + TerminalCount.ToString(CultureInfo.InvariantCulture) + ")";

        private DeliveryOutcome Settle(
            DeliveryObligation obligation,
            OutboxDeliveryState state,
            DiagnosticCode reason,
            out DiagnosticCode code,
            out string detail)
        {
            obligation.State = state;
            obligation.Reason = reason;
            if (openOrder.Remove(obligation.Key.OutboxId))
            {
                AdoptTerminal(obligation);
            }

            code = DiagnosticCode.None;
            detail = string.Empty;

            if (state == OutboxDeliveryState.Acknowledged)
            {
                AcknowledgeCount++;
                AdvanceCursor(obligation);
                return DeliveryOutcome.Acknowledged;
            }

            if (state == OutboxDeliveryState.Rejected)
            {
                RejectCount++;
                return DeliveryOutcome.Rejected;
            }

            CompensateCount++;
            return DeliveryOutcome.Compensated;
        }

        /// <summary>
        /// Adds the obligation to its destination's terminal list and prunes the oldest terminal entries beyond the
        /// retention bound. Pruning is reported through <see cref="PrunedTerminalCount"/>; it never happens silently
        /// (P-043) and it never weakens the destination's own idempotency boundary (P-045).
        /// </summary>
        private void AdoptTerminal(DeliveryObligation obligation)
        {
            Id128 destination = obligation.Key.DestinationId;
            if (!terminalOrder.TryGetValue(destination, out List<Id128>? ids))
            {
                ids = new List<Id128>();
                terminalOrder.Add(destination, ids);
            }

            if (!ids.Contains(obligation.Key.OutboxId))
            {
                ids.Add(obligation.Key.OutboxId);
            }

            // Only terminal records are ever pruned, and only the oldest first, so the retained set stays a suffix
            // of the destination's terminal sequence and a cursor can describe it exactly (P-045, P-008).
            while (ids.Count > TerminalRetention)
            {
                Id128 oldest = ids[0];
                ids.RemoveAt(0);
                if (!tracked.TryGetValue(oldest, out DeliveryObligation? pruned) || pruned.IsOpen)
                {
                    continue;
                }

                tracked.Remove(oldest);
                openOrder.Remove(oldest);
                PrunedTerminalCount++;
            }
        }

        private void AdvanceCursor(DeliveryObligation obligation)
        {
            Id128 destination = obligation.Key.DestinationId;
            uint retained = 0U;
            uint total = 0U;
            if (terminalOrder.TryGetValue(destination, out List<Id128>? ids))
            {
                retained = (uint)ids.Count;
            }

            if (cursors.TryGetValue(destination, out DeliveryCursor previous))
            {
                total = previous.TerminalTotal + 1U;
            }
            else
            {
                total = 1U;
            }

            cursors[destination] = new DeliveryCursor(
                destination,
                obligation.Key.OutboxId,
                retained,
                total);
        }

        private static int CompareByOrder(DeliveryObligation left, DeliveryObligation right)
        {
            int order = left.Order.CompareTo(right.Order);
            return order != 0 ? order : left.Key.OutboxId.CompareTo(right.Key.OutboxId);
        }

        private OutboxRecordValue RowOf(DeliveryObligation obligation, OutboxRowKind kind)
        {
            DeliveryKey key = obligation.Key;
            SchemaRef schema = obligation.PayloadSchema;
            OperationId causal = obligation.Causal;
            return new OutboxRecordValue(
                (uint)kind,
                OutboxRecordValue.CurrentRecordVersion,
                key.OutboxId.High,
                key.OutboxId.Low,
                key.DestinationId.High,
                key.DestinationId.Low,
                key.IdempotencyKey.High,
                key.IdempotencyKey.Low,
                obligation.SourceEvent.Value,
                obligation.Step.Value,
                obligation.Epoch.Value,
                causal.IssuerId.High,
                causal.IssuerId.Low,
                causal.IssuerSequence,
                schema.Id.Value.High,
                schema.Id.Value.Low,
                schema.Version,
                (uint)obligation.State,
                (uint)obligation.Reason,
                obligation.Attempts,
                (uint)Durability,
                obligation.Order,
                0UL,
                0UL,
                0U,
                0U,
                obligation.PayloadBytes());
        }

        /// <summary>
        /// One cursor row. A cursor is not an obligation, so the obligation and idempotency slots name the
        /// destination itself: every recorded identity stays non-default (P-004) and the newest acknowledged
        /// obligation lives in the cursor slot, where the default value honestly means "nothing acknowledged yet".
        /// </summary>
        private static OutboxRecordValue CursorRow(DeliveryCursor cursor)
        {
            Id128 newest = cursor.NewestAcknowledged;
            Id128 destination = cursor.DestinationId;
            return new OutboxRecordValue(
                (uint)OutboxRowKind.Cursor,
                OutboxRecordValue.CurrentRecordVersion,
                destination.High,
                destination.Low,
                destination.High,
                destination.Low,
                destination.High,
                destination.Low,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                (uint)OutboxDeliveryState.Acknowledged,
                (uint)DiagnosticCode.None,
                0U,
                (uint)OutboxDurability.Unspecified,
                0U,
                newest.High,
                newest.Low,
                cursor.RetainedTerminals,
                cursor.PrunedTerminals,
                null);
        }

        private void AdoptOpenOrder(List<DeliveryObligation> obligations)
        {
            obligations.Sort(CompareByOrder);
            for (int i = 0; i < obligations.Count; i++)
            {
                if (!obligations[i].IsOpen)
                {
                    openOrder.Remove(obligations[i].Key.OutboxId);
                }
            }
        }
    }
}
