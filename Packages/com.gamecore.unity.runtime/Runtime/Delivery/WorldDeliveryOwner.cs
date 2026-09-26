// GameCore.Unity.Runtime - the world-side delivery owner (GC-021).
//
// Normative sources: docs/game-core/00-core-protocols.md P-002 (`WorldHost` is the sole authority for world
// lifecycle, command admission and publication; a plugin never writes live ECS state, and this owner is a caller of
// `Submit`, never a second authority), P-042 (a destination command is submitted through the ordinary command
// ingress, and its result distinguishes `Accepted`, `Rejected`, `Cancelled` and `Committed`), P-045 (an irreversible
// output adapter consumes *committed* events only and persists an outbox when delivery must survive crashes) and
// P-058 (managed composition stays outside Burst; this is control-plane work on the committed boundary).
//
// WHAT THIS FILE IS
//
// The one place where a committed event becomes a delivery obligation and an open obligation becomes a submitted
// destination command. Everything it needs from a genre is a port:
//
//   * `IDeliveryObligationSource` — "does this committed event produce an obligation, and what command carries it?"
//     The kernel cannot answer that, and P-003 forbids inventing a universal gameplay effect to make it answerable.
//   * `IDestinationPort` (from the engine-free delivery core) — "apply this attempt, or say why not".
//
// Everything else is mechanical and is therefore here: a per-owner committed-event cursor so one committed event is
// observed once, canonical order, bounded work per poll and per dispatch pass, and the outbox rows a checkpoint
// capture reads.
//
// WHY THE CURSOR IS EXPLICIT
//
// P-045's subscriber delivery is at-least-once within retention, so a poll can legitimately see an event twice (a
// resynchronization, a restarted poll, a restored world). Enqueueing derives the obligation identity from the
// committed event (see `DeliveryKey.Derive`), so a second observation lands on the outbox's `Duplicate` answer
// instead of a second obligation — the cursor reduces work, it is not what makes the delivery safe.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Delivery;
using GameCore.Unity.Runtime.Messages;

namespace GameCore.Unity.Runtime.Delivery
{
    /// <summary>One destination command a caller wants the world to deliver (GC-021).</summary>
    public readonly struct DeliveryObligationRequest
    {
        public DeliveryObligationRequest(
            Id128 destinationId,
            SchemaRef commandSchema,
            byte[]? payload,
            bool requiresDurability)
        {
            DestinationId = destinationId;
            CommandSchema = commandSchema;
            Payload = payload;
            RequiresDurability = requiresDurability;
        }

        /// <summary>Stable identity of the destination the command is addressed to (P-004).</summary>
        public readonly Id128 DestinationId;

        /// <summary>Payload schema the destination accepts; a mismatch is refused before the port is called (P-054).</summary>
        public readonly SchemaRef CommandSchema;

        /// <summary>The command bytes, or null when the obligation carries no payload of its own.</summary>
        public readonly byte[]? Payload;

        /// <summary>
        /// True when the caller needs this obligation to survive a crash. An adapter with no journal refuses it
        /// rather than accepting an obligation it cannot keep (P-045).
        /// </summary>
        public readonly bool RequiresDurability;

        public override string ToString() => "deliveryRequest(" + DestinationId.ToString() + ","
            + CommandSchema.ToString() + ")";
    }

    /// <summary>
    /// The genre half of delivery: which committed events a world turns into obligations, and what command each
    /// obligation carries. It is a port because only the recipient's package can say what its mutation is (P-003).
    /// </summary>
    public interface IDeliveryObligationSource
    {
        /// <summary>
        /// Describes the obligation one committed event produces. False means "this event is not mine" and is
        /// counted, not treated as an error: a world may carry several sources, each claiming its own schemas (P-045).
        /// </summary>
        bool TryDescribe(in CommittedEvent committed, out DeliveryObligationRequest request);
    }

    /// <summary>
    /// One world's committed delivery obligations: the outbox, the durable adapter over it, the per-owner
    /// committed-event cursor and the registered destination ports (GC-021).
    ///
    /// It is a caller of the world's own command ingress and never a second authority: dispatching an obligation
    /// submits an ordinary `CommandEnvelope` and reads back the ordinary result (P-002, P-042).
    /// </summary>
    public sealed class WorldDeliveryOwner : IDisposable
    {
        private readonly List<IDestinationPort> ports = new List<IDestinationPort>();
        private readonly Dictionary<Id128, IDestinationPort> portsById = new Dictionary<Id128, IDestinationPort>();
        private readonly Dictionary<ulong, Id128> causalByEvent = new Dictionary<ulong, Id128>();

        public WorldDeliveryOwner(
            UnityWorldHost host,
            Id128 ownerId,
            int capacity,
            int terminalRetention,
            OutboxDurability durability,
            IDeliveryJournal? journal = null,
            IDeliveryStepHook? hook = null)
        {
            Host = host ?? throw new ArgumentNullException(nameof(host));
            Outbox = new DurableOutbox(ownerId, capacity, terminalRetention, durability);
            Adapter = new DurableDeliveryAdapter(Outbox, journal, hook);
            Cursor = new EventCursor(host.World, EventSequence.Zero);
        }

        /// <summary>The world this owner belongs to; every obligation it commits names that session (P-004).</summary>
        public UnityWorldHost Host { get; }

        /// <summary>Stable identity of the authoritative owner this outbox belongs to (P-017, P-034).</summary>
        public Id128 OwnerId => Outbox.OwnerId;

        public DurableOutbox Outbox { get; }

        public DurableDeliveryAdapter Adapter { get; }

        /// <summary>The committed-event position this owner has already observed (P-045).</summary>
        public EventCursor Cursor { get; private set; }

        /// <summary>Committed events this owner read.</summary>
        public int ObservedEventCount { get; private set; }

        /// <summary>Committed events no source claimed; counted so an unused schema is visible (P-052).</summary>
        public int UnclaimedEventCount { get; private set; }

        /// <summary>Obligations committed from committed events.</summary>
        public int EnqueuedCount { get; private set; }

        /// <summary>Committed events that produced an obligation the outbox already held (P-050).</summary>
        public int DuplicateEventCount { get; private set; }

        /// <summary>Observations refused because the outbox was full; never a silent drop (P-043).</summary>
        public int CapacityRefusalCount { get; private set; }

        /// <summary>Poll passes whose committed-event read reported an expired cursor (P-045).</summary>
        public int CursorExpiryCount { get; private set; }

        /// <summary>Attempts handed to a destination port.</summary>
        public int DispatchAttemptCount { get; private set; }

        /// <summary>Obligations settled as acknowledged.</summary>
        public int AcknowledgedCount { get; private set; }

        /// <summary>Obligations settled as terminally refused by their destination.</summary>
        public int RejectedCount { get; private set; }

        /// <summary>Obligations settled by an explicit compensation.</summary>
        public int CompensatedCount { get; private set; }

        /// <summary>Attempts whose destination reported the mutation was already there (P-045).</summary>
        public int AlreadyAppliedCount { get; private set; }

        /// <summary>Obligations reinstated from a checkpoint's outbox section (P-053).</summary>
        public int ReinstateCount { get; private set; }

        public bool IsDisposed { get; private set; }

        /// <summary>True when this owner persists its obligations; the value a caller must check before relying on it.</summary>
        public bool IsDurable => Adapter.IsDurable;

        /// <summary>
        /// Registers one destination port. Two ports claiming one destination identity is an ownership conflict and
        /// is refused rather than resolved by registration order (P-034, P-008).
        /// </summary>
        public bool TryRegisterDestination(IDestinationPort port, out string detail)
        {
            detail = string.Empty;
            if (port == null)
            {
                throw new ArgumentNullException(nameof(port));
            }

            if (portsById.ContainsKey(port.DestinationId))
            {
                detail = "destination " + port.DestinationId.ToString()
                    + " already has a registered port; one destination has one owner (P-034).";
                return false;
            }

            portsById.Add(port.DestinationId, port);
            ports.Add(port);
            return true;
        }

        /// <summary>
        /// Reads the world's committed events from this owner's cursor and commits one obligation per event a source
        /// claims. Reading uses the world's own committed-event reader, so an event is only ever a *committed* event
        /// and never a staged one (P-044, P-045).
        ///
        /// `causal` is the operation this poll is attributed to. When it is the default value the event's own causal
        /// request is used instead, which is what keeps the destination command's operation identity traceable to the
        /// step that committed it (P-042, P-050).
        /// </summary>
        public int PollCommittedEvents(IDeliveryObligationSource source, OperationId causal, int maxEvents)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (IsDisposed)
            {
                return 0;
            }

            WorldMessagePlane? plane = Host.Messages;
            if (plane == null || maxEvents <= 0)
            {
                return 0;
            }

            CommittedEventPage page = plane.ReadEvents(Cursor, maxEvents);
            if (page.Outcome == CursorOutcome.CursorExpired)
            {
                // A lagging owner is told, not guessed at: the world's retention dropped events this owner had not
                // read, and the honest response is to report it and resynchronize from the current end (P-045).
                CursorExpiryCount++;
                Cursor = new EventCursor(Host.World, plane.LastEventSequence);
                return 0;
            }

            int committed = 0;
            IReadOnlyList<CommittedEvent> events = page.Events;
            for (int i = 0; i < events.Count; i++)
            {
                CommittedEvent committedEvent = events[i];
                ObservedEventCount++;

                if (!source.TryDescribe(committedEvent, out DeliveryObligationRequest request))
                {
                    UnclaimedEventCount++;
                    continue;
                }

                OperationId causalRequest = causal.World.Session.IsDefault
                    ? committedEvent.CausalRequest
                    : causal;

                DeliveryKey key = DeliveryKey.Derive(
                    Host.World,
                    committedEvent.Cursor.Sequence,
                    request.DestinationId,
                    request.CommandSchema);

                OutboxAdmission admission = Adapter.TryCommit(
                    key,
                    request.CommandSchema,
                    request.Payload,
                    committedEvent.Cursor.Sequence,
                    committedEvent.Step,
                    committedEvent.Epoch,
                    causalRequest,
                    request.RequiresDurability,
                    out DeliveryObligation? obligation,
                    out DiagnosticCode code,
                    out string detail);

                if (admission == OutboxAdmission.Accepted && obligation != null)
                {
                    EnqueuedCount++;
                    committed++;
                    causalByEvent[committedEvent.Cursor.Sequence.Value] = key.OutboxId;
                }
                else if (admission == OutboxAdmission.Duplicate)
                {
                    DuplicateEventCount++;
                    if (obligation != null)
                    {
                        causalByEvent[committedEvent.Cursor.Sequence.Value] = obligation.Key.OutboxId;
                    }
                }
                else if (admission == OutboxAdmission.AtCapacity)
                {
                    CapacityRefusalCount++;
                }
                else if (admission == OutboxAdmission.JournalRefused || admission == OutboxAdmission.DurabilityUnavailable)
                {
                    CapacityRefusalCount++;
                }
            }

            Cursor = page.NextCursor;
            return committed;
        }

        /// <summary>
        /// Hands up to <paramref name="maxObligations"/> open obligations to their registered ports, in canonical
        /// commit order. Bounded per pass, because a delivery pass is control-plane work on a committed boundary and
        /// must not become an unbounded traversal (P-043, P-058).
        ///
        /// A destination with no registered port leaves its obligation open and is counted: an unmounted provider is
        /// exactly the "destination unavailable" case, and it is never an error and never a drop (P-012, P-045).
        /// </summary>
        public int DispatchOpenObligations(int maxObligations)
        {
            if (IsDisposed || maxObligations <= 0)
            {
                return 0;
            }

            IReadOnlyList<DeliveryObligation> open = Outbox.OpenObligations();
            int dispatched = 0;
            for (int i = 0; i < open.Count && dispatched < maxObligations; i++)
            {
                DeliveryObligation obligation = open[i];
                if (!portsById.TryGetValue(obligation.Key.DestinationId, out IDestinationPort? port) || port == null)
                {
                    UnavailableDestinationCount++;
                    continue;
                }

                DeliveryOutcome outcome = Adapter.TryDeliver(
                    obligation.Key.OutboxId,
                    port,
                    out DiagnosticCode code,
                    out string detail);
                dispatched++;
                DispatchAttemptCount++;

                if (outcome == DeliveryOutcome.Acknowledged)
                {
                    AcknowledgedCount++;
                }
                else if (outcome == DeliveryOutcome.Rejected)
                {
                    RejectedCount++;
                }
                else if (outcome == DeliveryOutcome.Compensated)
                {
                    CompensatedCount++;
                }
            }

            AlreadyAppliedCount = Adapter.AlreadyAppliedCount;
            return dispatched;
        }

        /// <summary>Attempts refused because no port owns the obligation's destination (P-012, P-052).</summary>
        public int UnavailableDestinationCount { get; private set; }

        /// <summary>
        /// The outbox rows a checkpoint capture records (P-053). The caller supplies this list to its capture
        /// context, so the document carries exactly the obligations this world had at its committed boundary.
        /// </summary>
        public IReadOnlyList<OutboxRecordValue> ToRecords() => Outbox.ToRecords();

        /// <summary>
        /// Reinstates obligations from a checkpoint's outbox section into this (restored) owner. This is the call
        /// that makes a committed delivery obligation outlive the unload of the world that committed it (P-045,
        /// P-049): the restored session owns the obligation and must deliver it, even though the original session and
        /// its retained events are gone.
        /// </summary>
        public bool TryReinstate(IReadOnlyList<OutboxRecordValue> rows, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!DurableOutbox.TryRestore(
                    rows,
                    Outbox.OwnerId,
                    Outbox.Capacity,
                    Outbox.TerminalRetention,
                    Adapter.Durability,
                    out DurableOutbox? restored,
                    out detail)
                || restored == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            Outbox.AdoptFrom(restored);
            ReinstateCount = rows == null ? 0 : rows.Count;
            return true;
        }

        /// <summary>The outbox identity a committed event produced, for a caller that must correlate the two.</summary>
        public bool TryGetObligationOf(EventSequence sequence, out Id128 outboxId) =>
            causalByEvent.TryGetValue(sequence.Value, out outboxId);

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            ports.Clear();
            portsById.Clear();
            causalByEvent.Clear();
        }

        public override string ToString() =>
            "deliveryOwner(" + OwnerId.ToString() + "," + Outbox.Durability.ToString() + ",open="
            + Outbox.OpenCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
