// GameCore.Execution.Observation — delayed-consumer delivery with per-consumer dedup (GC-016).
//
// P-045: "Internal publication is once per committed operation/step in a live world; subscriber delivery is
// at-least-once within retention, using `(WorldId, event sequence)` deduplication." TEST-014 delays one consumer
// until its retention window expires, retries delivery with the same event identity, and requires that the retry
// neither re-applies anything nor hides the gap.
//
// The ledger of "already delivered" therefore belongs to the consumer, not to the store: two consumers must not
// share one identity set, and the set must be bounded (a consumer that runs for a long time cannot grow it without
// limit — TEST-023's bounded-memory requirement). Each delivery window keeps the last `deliveredWindow` sequences
// it delivered and refuses to hand the same sequence out twice.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Observation
{
    /// <summary>What one delivery poll did.</summary>
    public enum DeliveryDisposition
    {
        /// <summary>New committed events were delivered in canonical order.</summary>
        Delivered = 0,

        /// <summary>Every event the page carried was already delivered to this consumer; nothing is handed out twice.</summary>
        Duplicate = 1,

        /// <summary>The cursor fell behind retention: the consumer must resynchronize from a snapshot (P-045).</summary>
        ResyncRequired = 2,

        /// <summary>Nothing new was committed at or after the cursor.</summary>
        NoEvents = 3,
    }

    /// <summary>One bounded delivery poll result: what happened, which events are new, and where to continue.</summary>
    public sealed class DeliveryBatch
    {
        internal DeliveryBatch(
            DeliveryDisposition disposition,
            IReadOnlyList<CommittedEvent>? events,
            EventCursor nextCursor,
            int duplicatesSuppressed,
            int eventsPerPage)
        {
            Disposition = disposition;
            Events = ContractCollections.Freeze(events);
            NextCursor = nextCursor;
            DuplicatesSuppressed = duplicatesSuppressed;
            EventsPerPage = eventsPerPage;
        }

        public DeliveryDisposition Disposition { get; }

        /// <summary>Events this consumer has not seen before; empty for every non-delivering disposition.</summary>
        public IReadOnlyList<CommittedEvent> Events { get; }

        /// <summary>Cursor to pass to the next poll; unchanged when the poll did not advance a page.</summary>
        public EventCursor NextCursor { get; }

        /// <summary>Repeats this poll recognised by `(world, sequence)` and therefore did not deliver again.</summary>
        public int DuplicatesSuppressed { get; }

        /// <summary>The page bound this consumer polls with.</summary>
        public int EventsPerPage { get; }

        public bool Delivered => Disposition == DeliveryDisposition.Delivered;

        public bool RequiresResync => Disposition == DeliveryDisposition.ResyncRequired;

        public override string ToString() =>
            "Delivery(" + Disposition.ToString()
            + ", events=" + Events.Count.ToString(CultureInfo.InvariantCulture)
            + ", duplicates=" + DuplicatesSuppressed.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One subscriber's bounded at-least-once delivery window over one world's committed events. It owns only its
    /// own cursor and its own bounded identity window: it never mutates world state, never re-applies gameplay and
    /// never claims exactly-once (an external adapter still needs its own idempotency boundary, P-045).
    /// </summary>
    public sealed class DelayedConsumerDelivery
    {
        private readonly WorldObservation observation;
        private readonly Queue<ulong> deliveredOrder = new Queue<ulong>();
        private readonly HashSet<ulong> delivered = new HashSet<ulong>();
        private EventCursor cursor;

        public DelayedConsumerDelivery(
            WorldObservation observation,
            EventCursor start,
            int deliveredWindow,
            int maxEventsPerPage)
        {
            this.observation = observation ?? throw new ArgumentNullException(nameof(observation));
            if (!start.World.Session.Equals(observation.World.Session))
            {
                throw new ArgumentException(
                    "A delivery window belongs to the world of its start cursor (P-004).", nameof(start));
            }

            if (deliveredWindow <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(deliveredWindow), "A delivery window is a bounded positive set (P-045).");
            }

            if (maxEventsPerPage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEventsPerPage), "A page bound is positive (P-045).");
            }

            cursor = start;
            DeliveredWindow = deliveredWindow;
            MaxEventsPerPage = maxEventsPerPage;
        }

        /// <summary>How many already-delivered identities this consumer remembers; its only growing structure's bound.</summary>
        public int DeliveredWindow { get; }

        public int MaxEventsPerPage { get; }

        public EventCursor Cursor => cursor;

        /// <summary>Events handed to this consumer exactly once, so far.</summary>
        public int DeliveredCount { get; private set; }

        /// <summary>Repeats suppressed by `(world, sequence)` identity.</summary>
        public int DuplicateCount { get; private set; }

        /// <summary>Polls that reported the cursor had fallen behind retention.</summary>
        public int ResyncRequiredCount { get; private set; }

        /// <summary>Polls that found nothing new.</summary>
        public int IdlePollCount { get; private set; }

        /// <summary>
        /// Polls one bounded page from this consumer's own cursor: the normal at-least-once path.
        /// </summary>
        public DeliveryBatch Poll() => PollFrom(cursor);

        /// <summary>
        /// Polls one bounded page from an explicit cursor, which is how a consumer retries a delivery it never
        /// acknowledged (P-045: delivery is at-least-once within retention, so the same identities may arrive
        /// twice). A page whose events were all delivered before is reported as
        /// <see cref="DeliveryDisposition.Duplicate"/> and hands out nothing; a cursor behind retention is reported
        /// as <see cref="DeliveryDisposition.ResyncRequired"/> so the consumer resnapshots instead of pretending
        /// continuity. The consumer's cursor only ever moves forward.
        /// </summary>
        public DeliveryBatch PollFrom(EventCursor from)
        {
            CommittedEventPage page = observation.Read(from, MaxEventsPerPage);
            if (page.Outcome == CursorOutcome.CursorExpired)
            {
                ResyncRequiredCount++;
                return new DeliveryBatch(
                    DeliveryDisposition.ResyncRequired, null, cursor, 0, MaxEventsPerPage);
            }

            if (page.Events.Count == 0)
            {
                IdlePollCount++;
                return new DeliveryBatch(DeliveryDisposition.NoEvents, null, cursor, 0, MaxEventsPerPage);
            }

            var fresh = new List<CommittedEvent>();
            int duplicates = 0;
            for (int i = 0; i < page.Events.Count; i++)
            {
                CommittedEvent committed = page.Events[i];
                if (!Remember(committed.Cursor.Sequence))
                {
                    duplicates++;
                    continue;
                }

                fresh.Add(committed);
            }

            if (page.NextCursor.Sequence.CompareTo(cursor.Sequence) > 0)
            {
                cursor = page.NextCursor;
            }

            DuplicateCount += duplicates;
            if (fresh.Count == 0)
            {
                return new DeliveryBatch(
                    DeliveryDisposition.Duplicate, null, cursor, duplicates, MaxEventsPerPage);
            }

            DeliveredCount += fresh.Count;
            return new DeliveryBatch(
                DeliveryDisposition.Delivered, fresh, cursor, duplicates, MaxEventsPerPage);
        }


        /// <summary>
        /// Resynchronizes after <see cref="DeliveryDisposition.ResyncRequired"/>: adopts the boundary cursor of the
        /// newest retained image and returns where the reader restarted. The consumer's identity window is kept,
        /// because identities it already delivered stay delivered across the gap.
        /// </summary>
        public SnapshotResynchronization Resynchronize()
        {
            SnapshotResynchronization resynchronization = observation.Resynchronize();
            if (resynchronization.Resynchronized)
            {
                cursor = resynchronization.Cursor;
                ResyncCount++;
            }

            return resynchronization;
        }

        /// <summary>True when this identity is new to this consumer; remembers it and evicts the oldest identity.</summary>
        private bool Remember(EventSequence sequence)
        {
            if (!delivered.Add(sequence.Value))
            {
                return false;
            }

            deliveredOrder.Enqueue(sequence.Value);
            while (deliveredOrder.Count > DeliveredWindow)
            {
                delivered.Remove(deliveredOrder.Dequeue());
            }

            return true;
        }

        public override string ToString() =>
            "DeliveryWindow(" + cursor.ToString()
            + ", delivered=" + DeliveredCount.ToString(CultureInfo.InvariantCulture)
            + ", duplicates=" + DuplicateCount.ToString(CultureInfo.InvariantCulture)
            + ", remembered=" + RememberedIdentities.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
