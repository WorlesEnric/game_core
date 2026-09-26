// GameCore.Execution.Observation — one world's observation storage surface (GC-016).
//
// GC-005 published one committed step image per step (`StepPublicationStore`) and GC-007 attached bounded committed
// events to it (`CommittedEventStore`). This type does not fork either store: it is the single read surface over
// them, and it is where the cross-cutting read behaviours live that belong to neither store alone:
//
//   * resynchronization: one call turning a lagging reader's gap into the newest committed token plus the event
//     cursor at that boundary (P-045, O-17);
//   * the committed-boundary lease a checkpoint consumes, with the image, its events and the queued-command
//     disposition observed together (P-053, O-20; frozen seam in `CommittedBoundary.cs`);
//   * the read counters that make backpressure, expiry and resynchronization explicit rather than inferred.
//
// Everything here is Unity-free. The owning world host constructs one over its own stores; a world with no message
// plane simply has no committed events, which this type reports as such instead of inventing a stream.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Messages;

namespace GameCore.Execution.Observation
{
    /// <summary>
    /// Read surface of one world's committed observation: immutable step images, bounded committed events, and the
    /// committed boundary a checkpoint leases. It owns no storage and mutates nothing.
    /// </summary>
    public sealed class WorldObservation : IObservationReader, ICommittedEventReader, ICommittedBoundaryReader, ITelemetryOwner
    {
        string ITelemetryOwner.TelemetryOwner => "gamecore.observation";

        /// <summary>Bound on the pages one boundary lease walks; a lease is bounded work, not a full history scan.</summary>
        private const int BoundaryPageSize = 64;

        private const int MaxBoundaryPages = 64;

        private readonly StepPublicationStore snapshots;
        private readonly CommittedEventStore? events;
        private ICommittedBoundaryFactsSource facts;

        public WorldObservation(
            StepPublicationStore snapshots,
            CommittedEventStore? events,
            ICommittedBoundaryFactsSource? facts)
        {
            this.snapshots = snapshots ?? throw new ArgumentNullException(nameof(snapshots));
            if (events != null && !events.World.Session.Equals(snapshots.World.Session))
            {
                throw new ArgumentException(
                    "The committed-event store belongs to another world incarnation than the snapshot store (P-004).",
                    nameof(events));
            }

            this.events = events;
            this.facts = facts ?? UnspecifiedBoundaryFacts.Instance;
        }

        public WorldId World => snapshots.World;

        /// <summary>The bounded committed-image store this observation reads (GC-005, extended by GC-016).</summary>
        public StepPublicationStore Snapshots => snapshots;

        /// <summary>The bounded committed-event store, or null when this world declares no message plane (GC-007).</summary>
        public CommittedEventStore? Events => events;

        /// <summary>Boundary facts source in force; a world without one reports an explicit unspecified queue.</summary>
        public ICommittedBoundaryFactsSource Facts => facts;

        /// <summary>Snapshot acquisitions granted.</summary>
        public int AcquireCount { get; private set; }

        /// <summary>Event pages answered.</summary>
        public int EventPageCount { get; private set; }

        /// <summary>Boundary leases granted.</summary>
        public int BoundaryLeaseCount { get; private set; }

        /// <summary>Boundary leases refused, for every reason except "nothing published" (which is counted separately).</summary>
        public int BoundaryRefusalCount { get; private set; }

        /// <summary>Boundary leases refused because this world has published nothing.</summary>
        public int NoPublicationCount { get; private set; }

        /// <summary>Reads and leases that walked past dropped events and reported the gap.</summary>
        public int EventGapReportCount { get; private set; }

        /// <summary>Resynchronizations served.</summary>
        public int ResyncCount { get; private set; }

        /// <summary>Resynchronizations that found nothing to restart from.</summary>
        public int ResyncUnavailableCount { get; private set; }

        /// <summary>Committed-event reads refused because the world declares no message plane at all.</summary>
        public int NoEventPlaneRefusalCount { get; private set; }

        /// <summary>
        /// Writes the observation surface through the fixed compact schema (GC-023). The world's observation is the
        /// single reader-facing owner, so the two stores it wraps (images and events) are reported separately and
        /// this section carries only the read-surface refusals and resynchronizations.
        /// </summary>
        public void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.Add(
                TelemetryCounter.StaleResults,
                NoPublicationCount + ResyncCount + ResyncUnavailableCount + NoEventPlaneRefusalCount);
            into.Add(TelemetryCounter.DiscardedCallbacks, EventGapReportCount);
        }

        /// <summary>Attaches the facts source the boundary lease reads; null restores the explicit unspecified source.</summary>
        public void AttachBoundaryFacts(ICommittedBoundaryFactsSource? source) =>
            facts = source ?? UnspecifiedBoundaryFacts.Instance;

        /// <summary>Leases one retained immutable image; refusals are values, never exceptions (P-007, P-045).</summary>
        public SnapshotAcquireResult Acquire(SnapshotToken token)
        {
            SnapshotAcquireResult result = snapshots.Acquire(token);
            if (result.Succeeded)
            {
                AcquireCount++;
            }

            return result;
        }

        /// <summary>
        /// Reads one bounded page of committed events. A world with no message plane has no committed events at
        /// all, so only the zero cursor is answerable and anything else is an explicit expiry (P-045).
        /// </summary>
        public CommittedEventPage Read(EventCursor cursor, int maxEvents)
        {
            if (maxEvents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvents), "A cursor page requires a positive bound.");
            }

            if (events != null)
            {
                CommittedEventPage page = events.Read(cursor, maxEvents);
                EventPageCount++;
                if (page.Outcome == CursorOutcome.CursorExpired)
                {
                    EventGapReportCount++;
                }

                return page;
            }

            if (!cursor.World.Session.Equals(World.Session))
            {
                NoEventPlaneRefusalCount++;
                return new CommittedEventPage(CursorOutcome.CursorExpired, null, cursor);
            }

            if (cursor.Sequence.Value == 0UL)
            {
                return new CommittedEventPage(CursorOutcome.Ok, null, cursor);
            }

            NoEventPlaneRefusalCount++;
            return new CommittedEventPage(CursorOutcome.CursorExpired, null, cursor);
        }

        /// <summary>The newest committed image this world can still hand out, if any (P-045).</summary>
        public bool TryGetLatestBoundary(out SnapshotToken token) => snapshots.TryResync(out token);

        /// <summary>Its committed-event cursor, or the zero cursor when the world declares no plane.</summary>
        public EventCursor LatestEventCursor() =>
            events != null ? events.CurrentCursor() : new EventCursor(World, EventSequence.Zero);

        /// <summary>Events retention dropped, so a resynchronizing reader knows the size of its gap (P-045).</summary>
        public ulong DroppedEventCount => events != null ? (ulong)events.DroppedCount : 0UL;

        /// <summary>
        /// The one call a lagging reader makes after `CursorExpired`: the newest retained image to re-lease and the
        /// cursor to continue from. A world that has published nothing says so instead of returning a stale token.
        /// </summary>
        public SnapshotResynchronization Resynchronize()
        {
            if (!snapshots.TryResync(out SnapshotToken token))
            {
                ResyncUnavailableCount++;
                return SnapshotResynchronization.Unavailable(World, DroppedEventCount);
            }

            ResyncCount++;
            return SnapshotResynchronization.From(token, LatestEventCursor(), DroppedEventCount, snapshots.RetainedCount);
        }

        /// <summary>
        /// Leases one committed boundary for a checkpoint: the immutable image, its committed events at or below its
        /// step, and the queued-command disposition observed together. The lease holds a real snapshot lease, so the
        /// image it reads cannot be evicted or overwritten while it lives (P-007, P-053).
        /// </summary>
        public CommittedBoundaryLeaseResult LeaseCommittedBoundary(CommittedBoundaryRequest request)
        {
            SnapshotToken token;
            if (request.IsLatest)
            {
                if (!snapshots.TryResync(out token))
                {
                    NoPublicationCount++;
                    return CommittedBoundaryLeaseResult.NoPublication();
                }
            }
            else
            {
                token = request.Token;
            }

            SnapshotAcquireResult acquired = snapshots.Acquire(token);
            if (!acquired.Succeeded || acquired.Lease == null)
            {
                BoundaryRefusalCount++;
                switch (acquired.Outcome)
                {
                    case SnapshotAcquireOutcome.Backpressure:
                        return CommittedBoundaryLeaseResult.Backpressure(token);
                    case SnapshotAcquireOutcome.ForeignWorld:
                        return CommittedBoundaryLeaseResult.ForeignWorld(token);
                    default:
                        return CommittedBoundaryLeaseResult.Expired(token);
                }
            }

            if (!snapshots.TryGetImage(token, out PublishedStepImage? image) || image == null)
            {
                // A granted lease always names a retained image; this can only be a torn read of a non-retaining
                // store, and the honest answer is to refuse rather than describe bytes nobody vouched for.
                acquired.Lease.Dispose();
                BoundaryRefusalCount++;
                return CommittedBoundaryLeaseResult.Expired(token);
            }

            IReadOnlyList<CommittedEvent> window = BoundaryEvents(
                token, request.EventOffset, request.MaxEvents, out int gap, out bool hasMore);
            if (gap > 0)
            {
                EventGapReportCount++;
            }

            ICommittedBoundaryFactsSource source = facts;
            var lease = new CommittedBoundaryLease(
                acquired.Lease,
                image.StateHash,
                image.PayloadHash,
                window,
                window.Count == 0 ? new EventCursor(World, EventSequence.Zero) : window[0].Cursor,
                window.Count == 0 ? new EventCursor(World, EventSequence.Zero) : window[window.Count - 1].Cursor,
                gap,
                hasMore,
                source.QueueDisposition,
                source.QueuedCommandCount,
                source.StagedOperationCount);

            BoundaryLeaseCount++;
            return CommittedBoundaryLeaseResult.Acquired(token, lease);
        }

        /// <summary>
        /// The committed events at or below one boundary's step, in canonical order, skipping
        /// <paramref name="offset"/> events and stopping at the window bound. Retention gaps are reported, never
        /// skipped silently, and the walk is bounded by <see cref="MaxBoundaryPages"/> pages.
        /// </summary>
        private IReadOnlyList<CommittedEvent> BoundaryEvents(
            SnapshotToken token,
            uint offset,
            int maxEvents,
            out int gap,
            out bool hasMore)
        {
            var window = new List<CommittedEvent>();
            gap = 0;
            hasMore = false;
            if (events == null || maxEvents == 0)
            {
                return window;
            }

            gap = events.DroppedCount;
            EventSequence first = events.FirstRetainedSequence;
            EventCursor cursor = new EventCursor(
                World, first.Value == 0UL ? EventSequence.Zero : new EventSequence(first.Value - 1UL));
            uint skipped = 0U;
            for (int page = 0; page < MaxBoundaryPages; page++)
            {
                CommittedEventPage read = events.Read(cursor, BoundaryPageSize);
                if (read.Outcome != CursorOutcome.Ok || read.Events.Count == 0)
                {
                    return window;
                }

                for (int i = 0; i < read.Events.Count; i++)
                {
                    CommittedEvent committed = read.Events[i];
                    if (committed.Step.CompareTo(token.LogicalStepId) > 0)
                    {
                        return window;
                    }

                    if (skipped < offset)
                    {
                        skipped++;
                        continue;
                    }

                    if (window.Count >= maxEvents)
                    {
                        hasMore = true;
                        return window;
                    }

                    window.Add(committed);
                }

                cursor = read.NextCursor;
            }

            return window;
        }

        public override string ToString() =>
            "WorldObservation(" + World.ToString()
            + ", images=" + snapshots.RetainedCount.ToString(CultureInfo.InvariantCulture)
            + ", leases=" + snapshots.ActiveLeaseCount.ToString(CultureInfo.InvariantCulture)
            + ")";
    }
}
