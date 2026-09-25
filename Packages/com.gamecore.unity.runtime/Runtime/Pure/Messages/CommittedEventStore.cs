// GameCore.Execution.Messages — bounded committed-event and snapshot output (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-044 (a successful logical step prepares committed events
// and a consistent snapshot, then nonthrowingly advances the step and exposes output together), P-045 (observers see
// immutable images at `(epoch, step)` publication only; a `CommittedEvent` records stable IDs, schema/revision,
// step, epoch, event sequence and causal request; retention is bounded and a lagging reader receives
// `CursorExpired`) and P-007 (bounded retention rejects rather than overwriting leased memory).
//
// This is the early-slice output path the task asks for: it does not replace GC-005's `StepPublicationStore`, it
// attaches committed events to the very `StepCommitEvent` that store already publishes. The event sequence and the
// step image therefore move together, and a step that faults publishes neither.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>One staged event of the step being built: the payload is already frozen and immutable (P-045).</summary>
    public readonly struct StagedCommittedEvent
    {
        public readonly SchemaRef Schema;
        public readonly TargetId Target;
        public readonly OperationId CausalRequest;
        public readonly FrozenPayload Payload;

        public StagedCommittedEvent(SchemaRef schema, TargetId target, OperationId causalRequest, FrozenPayload payload)
        {
            Schema = schema;
            Target = target;
            CausalRequest = causalRequest;
            Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        }

        public override string ToString() => Schema.ToString() + " on " + Target.ToString();
    }

    /// <summary>
    /// Stages the committed events of one logical step and publishes them with the step's image. Nothing here is
    /// visible before publication: a staged event is a candidate until `StepCommitEvent` carries it (P-044, P-045).
    /// </summary>
    public sealed class StepOutputCollector
    {
        private readonly List<StagedCommittedEvent> staged = new List<StagedCommittedEvent>();
        private readonly int maxEventsPerStep;

        public StepOutputCollector(int maxEventsPerStep)
        {
            if (maxEventsPerStep <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEventsPerStep), "A bounded step output requires a positive event bound (P-045).");
            }

            this.maxEventsPerStep = maxEventsPerStep;
        }

        public int StagedCount => staged.Count;

        public int MaxEventsPerStep => maxEventsPerStep;

        /// <summary>Staged events that had to be refused because the declared bound was reached; never silent.</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Total events published with committed steps.</summary>
        public int PublishedCount { get; private set; }

        /// <summary>
        /// Stages one candidate event. Exceeding the declared bound refuses the event and counts it, so a runaway
        /// producer cannot grow the step's output without bound (P-022, P-043).
        /// </summary>
        public bool TryStage(SchemaRef schema, TargetId target, OperationId causalRequest, FrozenPayload payload, out string failure)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (staged.Count >= maxEventsPerStep)
            {
                RefusedCount++;
                failure = "the step's committed-event bound of "
                    + maxEventsPerStep.ToString(CultureInfo.InvariantCulture)
                    + " is reached; the event was refused rather than published unbounded (P-045)";
                return false;
            }

            staged.Add(new StagedCommittedEvent(schema, target, causalRequest, payload));
            failure = string.Empty;
            return true;
        }

        /// <summary>
        /// Builds the committed events of one step, in canonical order and with the next event sequence range. The
        /// staging list is emptied only here, at the publication boundary (P-044).
        /// </summary>
        public IReadOnlyList<CommittedEvent> BuildCommittedEvents(
            WorldId world,
            AssemblyEpoch epoch,
            LogicalStepId step,
            EventSequence firstSequence,
            out EventSequence nextSequence)
        {
            nextSequence = firstSequence;
            if (staged.Count == 0)
            {
                return Array.Empty<CommittedEvent>();
            }

            var ordered = new List<StagedCommittedEvent>(staged);
            ordered.Sort(CompareStaged);

            var committed = new List<CommittedEvent>(ordered.Count);
            EventSequence sequence = firstSequence;
            for (int i = 0; i < ordered.Count; i++)
            {
                StagedCommittedEvent candidate = ordered[i];
                if (!sequence.TryIncrement(out EventSequence next))
                {
                    // Event sequences never wrap; demanding a reset is the caller's protocol decision (P-005).
                    throw new InvalidOperationException(
                        "the committed event sequence is exhausted; a world cannot wrap event identities (P-005).");
                }

                committed.Add(new CommittedEvent(
                    new EventCursor(world, next),
                    candidate.Schema,
                    epoch,
                    step,
                    candidate.CausalRequest,
                    candidate.Payload));
                sequence = next;
            }

            nextSequence = sequence;
            staged.Clear();
            return committed;
        }

        /// <summary>Counts events that really reached publication, i.e. carried by a committed step image (P-044).</summary>
        public void MarkPublished(int count)
        {
            if (count > 0)
            {
                PublishedCount += count;
            }
        }

        /// <summary>Drops every staged event; the step that staged them never committed (P-031).</summary>
        public void Abort()
        {
            staged.Clear();
        }

        private static int CompareStaged(StagedCommittedEvent left, StagedCommittedEvent right)
        {
            // Canonical event order: causal request, then stable target, then schema. It never depends on the order
            // producers staged their events (P-008, P-045).
            int byRequest = CompareRequests(left.CausalRequest, right.CausalRequest);
            if (byRequest != 0)
            {
                return byRequest;
            }

            int byTarget = left.Target.Value.CompareTo(right.Target.Value);
            if (byTarget != 0)
            {
                return byTarget;
            }

            int bySchema = left.Schema.Id.Value.CompareTo(right.Schema.Id.Value);
            return bySchema != 0 ? bySchema : left.Schema.Version.CompareTo(right.Schema.Version);
        }

        private static int CompareRequests(OperationId left, OperationId right)
        {
            int byWorld = left.World.Session.CompareTo(right.World.Session);
            if (byWorld != 0)
            {
                return byWorld;
            }

            int byIssuer = left.IssuerId.CompareTo(right.IssuerId);
            if (byIssuer != 0)
            {
                return byIssuer;
            }

            return left.IssuerSequence.CompareTo(right.IssuerSequence);
        }

        public override string ToString()
            => "staged=" + staged.Count.ToString(CultureInfo.InvariantCulture)
                + "/" + maxEventsPerStep.ToString(CultureInfo.InvariantCulture)
                + ", published=" + PublishedCount.ToString(CultureInfo.InvariantCulture)
                + ", refused=" + RefusedCount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Bounded retained-event reader of one world (P-045, O-17). It answers cursor pages from committed events only,
    /// reports a gap as `CursorExpired` instead of pretending continuity, and counts a repeated delivery so a
    /// subscriber can be at-least-once within retention without an external dedup protocol of its own.
    /// </summary>
    public sealed class CommittedEventStore : ICommittedEventReader
    {
        private readonly List<CommittedEvent> retained = new List<CommittedEvent>();
        private readonly HashSet<ulong> deliveredSequences = new HashSet<ulong>();

        public CommittedEventStore(WorldId world, int retention)
        {
            if (retention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retention), "Event retention must be positive (P-045).");
            }

            World = world;
            Retention = retention;
        }

        public WorldId World { get; }

        /// <summary>Number of committed events retained; older ones drop with an explicit gap report (P-045).</summary>
        public int Retention { get; }

        public int Count => retained.Count;

        /// <summary>Lowest retained event sequence, or zero when nothing is retained.</summary>
        public EventSequence FirstRetainedSequence => retained.Count == 0 ? EventSequence.Zero : retained[0].Cursor.Sequence;

        /// <summary>Highest published event sequence.</summary>
        public EventSequence LastSequence { get; private set; } = EventSequence.Zero;

        /// <summary>Events dropped because retention is bounded; an explicit, countable gap (P-045).</summary>
        public int DroppedCount { get; private set; }

        /// <summary>Page reads that reported an expired cursor instead of a false continuation.</summary>
        public int CursorExpiredCount { get; private set; }

        /// <summary>A cursor that lagged past retention and must resynchronize from a snapshot (P-045).</summary>
        public int ResyncRequiredCount { get; private set; }

        /// <summary>Pages that a subscriber re-read with the same identity; delivery is at-least-once (P-045).</summary>
        public int RedeliveryCount { get; private set; }

        /// <summary>
        /// Publishes one step's committed events. Called only at the step's publication boundary, so an event and the
        /// step image it belongs to become visible together (P-044).
        /// </summary>
        public void Publish(IReadOnlyList<CommittedEvent>? events)
        {
            if (events == null)
            {
                return;
            }

            for (int i = 0; i < events.Count; i++)
            {
                CommittedEvent committed = events[i];
                if (!committed.Cursor.World.Session.Equals(World.Session))
                {
                    throw new ArgumentException(
                        "a committed event may only be published into the store of its own world incarnation (P-004).",
                        nameof(events));
                }

                if (committed.Cursor.Sequence.CompareTo(LastSequence) <= 0)
                {
                    throw new ArgumentException(
                        "committed event sequences are strictly increasing per world; "
                        + committed.Cursor.Sequence.ToString() + " is not above " + LastSequence.ToString() + ".",
                        nameof(events));
                }

                LastSequence = committed.Cursor.Sequence;
                retained.Add(committed);
            }

            while (retained.Count > Retention)
            {
                retained.RemoveAt(0);
                DroppedCount++;
            }
        }

        /// <summary>Retained events at or after a cursor, bounded by <paramref name="maxEvents"/> (P-045).</summary>
        public CommittedEventPage Read(EventCursor cursor, int maxEvents)
        {
            if (maxEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvents), "A cursor page requires a positive bound.");
            }

            if (!cursor.World.Session.Equals(World.Session))
            {
                CursorExpiredCount++;
                return new CommittedEventPage(CursorOutcome.CursorExpired, null, cursor);
            }

            if (retained.Count != 0 && IsBehindRetention(cursor.Sequence))
            {
                // The cursor fell behind retention: report the gap and let the reader resynchronize from a snapshot.
                CursorExpiredCount++;
                ResyncRequiredCount++;
                return new CommittedEventPage(CursorOutcome.CursorExpired, null, new EventCursor(World, FirstRetainedSequence));
            }

            if (cursor.Sequence.CompareTo(LastSequence) > 0)
            {
                CursorExpiredCount++;
                return new CommittedEventPage(CursorOutcome.CursorExpired, null, new EventCursor(World, LastSequence));
            }

            var page = new List<CommittedEvent>();
            EventCursor next = cursor;
            for (int i = 0; i < retained.Count && page.Count < maxEvents; i++)
            {
                CommittedEvent committed = retained[i];
                if (committed.Cursor.Sequence.CompareTo(cursor.Sequence) <= 0)
                {
                    continue;
                }

                if (!deliveredSequences.Add(committed.Cursor.Sequence.Value))
                {
                    // A repeated read with the same identity is a redelivery: counted, never treated as a new fact.
                    RedeliveryCount++;
                }

                page.Add(committed);
                next = committed.Cursor;
            }

            return new CommittedEventPage(CursorOutcome.Ok, page, next);
        }

        /// <summary>
        /// True when a cursor can no longer be answered continuously: the next event it needs was already dropped.
        /// A cursor immediately below the first retained event is still continuous, because the reader has seen that
        /// predecessor; anything older is an explicit gap (P-045).
        /// </summary>
        private bool IsBehindRetention(EventSequence cursor)
        {
            if (retained.Count == 0)
            {
                return false;
            }

            ulong first = FirstRetainedSequence.Value;
            return first != 0UL && cursor.Value + 1UL < first;
        }

        /// <summary>The cursor a fresh reader starts from: the last published event of this world.</summary>
        public EventCursor CurrentCursor() => new EventCursor(World, LastSequence);

        public override string ToString()
            => "events=" + retained.Count.ToString(CultureInfo.InvariantCulture)
                + "/" + Retention.ToString(CultureInfo.InvariantCulture)
                + " last=" + LastSequence.ToString();
    }
}
