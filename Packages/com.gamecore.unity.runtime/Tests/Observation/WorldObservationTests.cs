// GameCore.Execution.Tests.Observation — the committed boundary, its event window and resynchronization (GC-016).
//
// These cases pin P-045 ("lagging readers receive `CursorExpired` and resynchronize from a snapshot"), P-053's
// boundary lease (image + events + queued-command disposition together) and the frozen seam GC-018 consumes:
// `ICommittedBoundaryReader.LeaseCommittedBoundary`.
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Messages;
using GameCore.Execution.Observation;
using NUnit.Framework;

namespace GameCore.Execution.Tests.Observation
{
    [TestFixture]
    public sealed class WorldObservationTests
    {
        [Test]
        public void ResynchronizationNamesTheNewestImageAndTheGapItCannotRecover()
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 8, 4);
            var events = new CommittedEventStore(ObservationFixture.World, 2);
            var observation = new WorldObservation(snapshots, events, null);

            Assert.That(observation.Resynchronize().Resynchronized, Is.False, "A world with nothing published cannot resync.");
            Assert.That(observation.ResyncUnavailableCount, Is.EqualTo(1));

            for (ulong step = 1UL; step <= 5UL; step++)
            {
                snapshots.Publish(ObservationFixture.CommitWithEvent(step));
                events.Publish(new[] { ObservationFixture.Event(step, step) });
            }

            CommittedEventPage lagging = observation.Read(
                new EventCursor(ObservationFixture.World, EventSequence.Zero), 8);
            Assert.That(lagging.Outcome, Is.EqualTo(CursorOutcome.CursorExpired),
                "A cursor behind retention is told, not silently skipped (P-045).");
            Assert.That(lagging.Events.Count, Is.Zero);
            Assert.That(observation.EventGapReportCount, Is.EqualTo(1));

            SnapshotResynchronization resynchronization = observation.Resynchronize();
            Assert.That(resynchronization.Resynchronized, Is.True);
            Assert.That(resynchronization.Token, Is.EqualTo(ObservationFixture.Token(5UL)));
            Assert.That(resynchronization.Cursor.Sequence, Is.EqualTo(new EventSequence(5UL)));
            Assert.That(resynchronization.DroppedEvents, Is.EqualTo(3UL), "The gap is reported as a count.");
            Assert.That(resynchronization.RetainedImages, Is.EqualTo(5));
            Assert.That(resynchronization.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(observation.ResyncCount, Is.EqualTo(1));
            Assert.That(observation.TryGetLatestBoundary(out SnapshotToken latest), Is.True);
            Assert.That(latest, Is.EqualTo(resynchronization.Token));
            Assert.That(observation.LatestEventCursor().Sequence, Is.EqualTo(new EventSequence(5UL)));
            Assert.That(observation.DroppedEventCount, Is.EqualTo(3UL));

            SnapshotAcquireResult acquired = observation.Acquire(resynchronization.Token);
            Assert.That(acquired.Succeeded, Is.True);
            Assert.That(snapshots.Verify(acquired.Lease!, out ContentHash hash), Is.True);
            Assert.That(hash, Is.EqualTo(ObservationFixture.ExpectedHash(5UL)));
            Assert.That(observation.AcquireCount, Is.EqualTo(1));
            acquired.Lease!.Dispose();
        }

        [Test]
        public void ABoundaryLeaseCarriesTheImageItsEventsAndTheQueueFactsTogether()
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 8, 4);
            var events = new CommittedEventStore(ObservationFixture.World, 8);
            var observation = new WorldObservation(
                snapshots, events, new ObservationFixture.Facts(2, 1, BoundaryQueueDisposition.Included));

            for (ulong step = 1UL; step <= 3UL; step++)
            {
                snapshots.Publish(ObservationFixture.CommitWithEvent(step));
                events.Publish(new[] { ObservationFixture.Event(step, step) });
            }

            CommittedBoundaryLeaseResult leased = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.Latest(8));
            Assert.That(leased.Outcome, Is.EqualTo(CommittedBoundaryOutcome.Leased));
            Assert.That(leased.Code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(leased.Lease, Is.Not.Null);

            ICommittedBoundaryLease lease = leased.Lease!;
            Assert.That(lease.Token, Is.EqualTo(ObservationFixture.Token(3UL)));
            Assert.That(lease.World.Session, Is.EqualTo(ObservationFixture.World.Session));
            Assert.That(lease.Step, Is.EqualTo(new LogicalStepId(3UL)));
            Assert.That(lease.Epoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(lease.StateHash, Is.EqualTo(StepFingerprint.Compute(ObservationFixture.World, AssemblyEpoch.First, new LogicalStepId(3UL), ObservationFixture.Dispatched)));
            Assert.That(lease.PayloadHash, Is.EqualTo(ObservationFixture.ExpectedHash(3UL)));
            Assert.That(lease.EventCount, Is.EqualTo(3));
            Assert.That(lease.FirstEventCursor.Sequence, Is.EqualTo(EventSequence.First));
            Assert.That(lease.LastEventCursor.Sequence, Is.EqualTo(new EventSequence(3UL)));
            Assert.That(lease.EventAt(0).Step, Is.EqualTo(LogicalStepId.First));
            Assert.That(lease.EventGapCount, Is.Zero);
            Assert.That(lease.HasMoreEvents, Is.False);
            Assert.That(lease.QueueDisposition, Is.EqualTo(BoundaryQueueDisposition.Included));
            Assert.That(lease.QueuedCommandCount, Is.EqualTo(2));
            Assert.That(lease.StagedOperationCount, Is.EqualTo(1));
            Assert.That(lease.IsDisposed, Is.False);

            var concrete = (CommittedBoundaryLease)lease;
            Assert.That(concrete.Verify(), Is.True, "The leased bytes are the committed image of this token.");

            lease.Dispose();
            lease.Dispose();
            Assert.That(lease.IsDisposed, Is.True);
            Assert.That(snapshots.ActiveLeaseCount, Is.Zero, "The boundary lease holds exactly one snapshot lease.");
            Assert.That(snapshots.ReleasedLeaseCount, Is.EqualTo(1));
            Assert.That(observation.BoundaryLeaseCount, Is.EqualTo(1));
        }

        [Test]
        public void BoundaryEventWindowsPageWithoutInventingContinuity()
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 16, 4);
            var events = new CommittedEventStore(ObservationFixture.World, 3);
            var observation = new WorldObservation(snapshots, events, null);

            for (ulong step = 1UL; step <= 10UL; step++)
            {
                snapshots.Publish(ObservationFixture.CommitWithEvent(step));
                events.Publish(new[] { ObservationFixture.Event(step, step) });
            }

            CommittedBoundaryLeaseResult first = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(10UL), 4, 0U));
            Assert.That(first.Leased, Is.True);
            Assert.That(first.Lease!.EventCount, Is.EqualTo(3), "Retention keeps three events at or below the boundary.");
            Assert.That(first.Lease.FirstEventCursor.Sequence, Is.EqualTo(new EventSequence(8UL)));
            Assert.That(first.Lease.LastEventCursor.Sequence, Is.EqualTo(new EventSequence(10UL)));
            Assert.That(first.Lease.HasMoreEvents, Is.False, "The window reached the boundary, so nothing is pending.");
            Assert.That(first.Lease.EventGapCount, Is.EqualTo(7), "Retention dropped seven events: an explicit gap.");
            first.Lease!.Dispose();

            CommittedBoundaryLeaseResult middle = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(10UL), 1, 1U));
            Assert.That(middle.Leased, Is.True);
            Assert.That(middle.Lease!.EventCount, Is.EqualTo(1));
            Assert.That(middle.Lease.FirstEventCursor.Sequence, Is.EqualTo(new EventSequence(9UL)));
            Assert.That(middle.Lease.HasMoreEvents, Is.True, "One more in-boundary event follows the page.");
            middle.Lease!.Dispose();

            CommittedBoundaryLeaseResult tail = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(10UL), 4, 2U));
            Assert.That(tail.Leased, Is.True);
            Assert.That(tail.Lease!.EventCount, Is.EqualTo(1), "Only the last retained event is at or below the boundary.");
            Assert.That(tail.Lease.LastEventCursor.Sequence, Is.EqualTo(new EventSequence(10UL)));
            Assert.That(tail.Lease.HasMoreEvents, Is.False);
            tail.Lease!.Dispose();

            CommittedBoundaryLeaseResult empty = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(10UL), 0, 0U));
            Assert.That(empty.Leased, Is.True);
            Assert.That(empty.Lease!.EventCount, Is.Zero, "A state-only checkpoint asks for no events.");
            empty.Lease!.Dispose();

            Assert.That(observation.EventGapReportCount, Is.GreaterThan(0));
        }

        [Test]
        public void BoundaryRefusalsAreValuesWithStableCodes()
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 4, 1);
            var events = new CommittedEventStore(ObservationFixture.World, 4);
            var observation = new WorldObservation(snapshots, events, null);

            CommittedBoundaryLeaseResult nothing = observation.LeaseCommittedBoundary(CommittedBoundaryRequest.Latest(4));
            Assert.That(nothing.Outcome, Is.EqualTo(CommittedBoundaryOutcome.NoPublication));
            Assert.That(nothing.Code, Is.EqualTo(DiagnosticCode.ResultExpired));
            Assert.That(observation.NoPublicationCount, Is.EqualTo(1));

            snapshots.Publish(ObservationFixture.Commit(1UL));
            snapshots.Publish(ObservationFixture.Commit(2UL));

            CommittedBoundaryLeaseResult held = observation.LeaseCommittedBoundary(CommittedBoundaryRequest.Latest(4));
            Assert.That(held.Leased, Is.True);

            CommittedBoundaryLeaseResult pressed = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(1UL), 4, 0U));
            Assert.That(pressed.Outcome, Is.EqualTo(CommittedBoundaryOutcome.Backpressure));
            Assert.That(pressed.Code, Is.EqualTo(DiagnosticCode.SnapshotBackpressure));
            Assert.That(pressed.Lease, Is.Null);

            CommittedBoundaryLeaseResult foreign = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(
                    new SnapshotToken(ObservationFixture.OtherWorld, AssemblyEpoch.First, LogicalStepId.First), 4, 0U));
            Assert.That(foreign.Outcome, Is.EqualTo(CommittedBoundaryOutcome.ForeignWorld));
            Assert.That(foreign.Code, Is.EqualTo(DiagnosticCode.StaleHandle));

            held.Lease!.Dispose();
            CommittedBoundaryLeaseResult expired = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(9UL), 4, 0U));
            Assert.That(expired.Outcome, Is.EqualTo(CommittedBoundaryOutcome.Expired));
            Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.CursorExpired));
            Assert.That(observation.BoundaryRefusalCount, Is.EqualTo(3));
        }

        [Test]
        public void AWorldWithoutAPlaneReportsNoEventsInsteadOfInventingAStream()
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 4, 2);
            var observation = new WorldObservation(snapshots, null, null);
            snapshots.Publish(ObservationFixture.Commit(1UL));

            Assert.That(observation.Events, Is.Null);
            Assert.That(observation.Facts.QueueDisposition, Is.EqualTo(BoundaryQueueDisposition.Unspecified),
                "A world that declares no facts source never reports an empty queue as if it had one.");

            CommittedEventPage start = observation.Read(
                new EventCursor(ObservationFixture.World, EventSequence.Zero), 4);
            Assert.That(start.Outcome, Is.EqualTo(CursorOutcome.Ok));
            Assert.That(start.Events.Count, Is.Zero);

            CommittedEventPage beyond = observation.Read(
                new EventCursor(ObservationFixture.World, new EventSequence(3UL)), 4);
            Assert.That(beyond.Outcome, Is.EqualTo(CursorOutcome.CursorExpired));
            Assert.That(observation.NoEventPlaneRefusalCount, Is.EqualTo(1));
            Assert.That(observation.EventPageCount, Is.Zero);
            Assert.That(observation.LatestEventCursor().Sequence, Is.EqualTo(EventSequence.Zero));
            Assert.That(observation.DroppedEventCount, Is.Zero);

            CommittedBoundaryLeaseResult leased = observation.LeaseCommittedBoundary(
                CommittedBoundaryRequest.At(ObservationFixture.Token(1UL), 4, 0U));
            Assert.That(leased.Leased, Is.True);
            Assert.That(leased.Lease!.EventCount, Is.Zero);
            Assert.That(leased.Lease.QueueDisposition, Is.EqualTo(BoundaryQueueDisposition.Unspecified));
            leased.Lease!.Dispose();
        }

        [Test]
        public void EventRetentionReportsGapsAndCountsRedeliveries()
        {
            var events = new CommittedEventStore(ObservationFixture.World, 2);
            events.Publish(new[] { ObservationFixture.Event(1UL, 1UL), ObservationFixture.Event(2UL, 2UL) });
            events.Publish(new[] { ObservationFixture.Event(3UL, 3UL) });

            Assert.That(events.Count, Is.EqualTo(2));
            Assert.That(events.DroppedCount, Is.EqualTo(1), "The drop is explicit (P-045).");
            Assert.That(events.FirstRetainedSequence, Is.EqualTo(new EventSequence(2UL)));
            Assert.That(
                events.Read(new EventCursor(ObservationFixture.World, EventSequence.Zero), 8).Outcome,
                Is.EqualTo(CursorOutcome.CursorExpired),
                "A cursor behind retention is never answered as continuity.");
            Assert.That(events.RedeliveryCount, Is.Zero, "An expired cursor delivers nothing to count twice.");

            // Re-reading a retained page is the at-least-once case TEST-014 retries: the same identity comes back and
            // is counted, and only the identities still retained can come back at all (bounded memory).
            var continuous = new EventCursor(ObservationFixture.World, EventSequence.First);
            Assert.That(events.Read(continuous, 8).Events.Count, Is.EqualTo(2));
            Assert.That(events.RedeliveryCount, Is.Zero, "The first read of a retained identity is not a redelivery.");
            Assert.That(events.Read(continuous, 8).Events.Count, Is.EqualTo(2));
            Assert.That(events.RedeliveryCount, Is.EqualTo(2), "The second read of the same identities is a redelivery.");
        }

        [Test]
        public void BoundaryRequestsAreValidated()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CommittedBoundaryRequest.Latest(-1));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => CommittedBoundaryRequest.At(ObservationFixture.Token(1UL), -1, 0U));

            CommittedBoundaryRequest latest = CommittedBoundaryRequest.Latest(4);
            Assert.That(latest.IsLatest, Is.True);
            Assert.That(latest.MaxEvents, Is.EqualTo(4));

            CommittedBoundaryRequest exact = CommittedBoundaryRequest.At(ObservationFixture.Token(2UL), 4, 1U);
            Assert.That(exact.IsLatest, Is.False);
            Assert.That(exact.Token, Is.EqualTo(ObservationFixture.Token(2UL)));
            Assert.That(exact.EventOffset, Is.EqualTo(1U));
            Assert.That(exact.ToString(), Does.Contain("SnapshotToken"));
        }
    }
}
