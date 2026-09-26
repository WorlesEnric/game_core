// GameCore.Execution.Tests.Observation — delayed-consumer delivery and its per-consumer dedup (GC-016).
//
// TEST-014 delays one consumer until its retention window expires, retries delivery with the same event identity
// and requires that the retry neither re-applies anything nor hides the gap. P-045 makes the dedup identity
// `(WorldId, event sequence)` and delivery at-least-once within retention, so the ledger of delivered identities
// belongs to the consumer and is bounded.
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
    public sealed class DelayedConsumerDeliveryTests
    {
        private static WorldObservation Build(int eventRetention, params ulong[] steps)
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 16, 4);
            var events = new CommittedEventStore(ObservationFixture.World, eventRetention);
            for (int i = 0; i < steps.Length; i++)
            {
                snapshots.Publish(ObservationFixture.CommitWithEvent(steps[i]));
                events.Publish(new[] { ObservationFixture.Event(steps[i], steps[i]) });
            }

            return new WorldObservation(snapshots, events, null);
        }

        [Test]
        public void OneConsumerSeesEachIdentityOnceAndARetryOfTheSamePageIsSuppressed()
        {
            WorldObservation observation = Build(8, 1UL, 2UL, 3UL);
            var consumer = new DelayedConsumerDelivery(
                observation, new EventCursor(ObservationFixture.World, EventSequence.Zero), 8, 4);

            DeliveryBatch first = consumer.Poll();
            Assert.That(first.Disposition, Is.EqualTo(DeliveryDisposition.Delivered));
            Assert.That(first.Events.Count, Is.EqualTo(3));
            Assert.That(first.Events[0].Cursor.Sequence, Is.EqualTo(EventSequence.First));
            Assert.That(first.Events[2].Cursor.Sequence, Is.EqualTo(new EventSequence(3UL)));
            Assert.That(first.NextCursor.Sequence, Is.EqualTo(new EventSequence(3UL)));
            Assert.That(consumer.DeliveredCount, Is.EqualTo(3));
            Assert.That(consumer.RememberedIdentities, Is.EqualTo(3));

            DeliveryBatch retry = consumer.PollFrom(new EventCursor(ObservationFixture.World, EventSequence.Zero));
            Assert.That(retry.Disposition, Is.EqualTo(DeliveryDisposition.Duplicate),
                "A retry of a delivery the consumer never acknowledged repeats nothing (P-045).");
            Assert.That(retry.Events.Count, Is.Zero);
            Assert.That(retry.DuplicatesSuppressed, Is.EqualTo(3));
            Assert.That(consumer.DeliveredCount, Is.EqualTo(3), "A duplicate is never delivered twice.");
            Assert.That(consumer.DuplicateCount, Is.EqualTo(3));
            Assert.That(consumer.ResyncCount, Is.Zero);

            DeliveryBatch idle = consumer.Poll();
            Assert.That(idle.Disposition, Is.EqualTo(DeliveryDisposition.NoEvents));
            Assert.That(idle.Events.Count, Is.Zero);
            Assert.That(consumer.IdlePollCount, Is.EqualTo(1));
        }

        [Test]
        public void ALaggingConsumerIsToldToResynchronizeAndThenContinues()
        {
            WorldObservation observation = Build(2, 1UL, 2UL, 3UL, 4UL);
            var consumer = new DelayedConsumerDelivery(
                observation, new EventCursor(ObservationFixture.World, EventSequence.Zero), 8, 4);

            DeliveryBatch behind = consumer.Poll();
            Assert.That(behind.Disposition, Is.EqualTo(DeliveryDisposition.ResyncRequired));
            Assert.That(behind.RequiresResync, Is.True);
            Assert.That(behind.Events.Count, Is.Zero, "The gap is never papered over with a partial page.");
            Assert.That(consumer.ResyncRequiredCount, Is.EqualTo(1));
            Assert.That(consumer.DeliveredCount, Is.Zero);

            SnapshotResynchronization resynchronization = consumer.Resynchronize();
            Assert.That(resynchronization.Resynchronized, Is.True);
            Assert.That(resynchronization.DroppedEvents, Is.EqualTo(2UL));
            Assert.That(consumer.ResyncCount, Is.EqualTo(1));
            Assert.That(consumer.Cursor.Sequence, Is.EqualTo(new EventSequence(4UL)));

            // The world publishes one more committed event; the resynchronized consumer receives exactly that one.
            observation.Snapshots.Publish(ObservationFixture.CommitWithEvent(5UL));
            observation.Events!.Publish(new[] { ObservationFixture.Event(5UL, 5UL) });

            DeliveryBatch continued = consumer.Poll();
            Assert.That(continued.Disposition, Is.EqualTo(DeliveryDisposition.Delivered));
            Assert.That(continued.Events.Count, Is.EqualTo(1));
            Assert.That(continued.Events[0].Cursor.Sequence, Is.EqualTo(new EventSequence(5UL)));
            Assert.That(consumer.DeliveredCount, Is.EqualTo(1));
        }

        [Test]
        public void TheRememberedIdentityWindowIsBounded()
        {
            WorldObservation observation = Build(8, 1UL, 2UL, 3UL, 4UL, 5UL);
            var consumer = new DelayedConsumerDelivery(
                observation, new EventCursor(ObservationFixture.World, EventSequence.Zero), 2, 8);

            DeliveryBatch batch = consumer.Poll();
            Assert.That(batch.Events.Count, Is.EqualTo(5));
            Assert.That(consumer.DeliveredCount, Is.EqualTo(5));
            Assert.That(consumer.RememberedIdentities, Is.EqualTo(2),
                "The identity window is bounded, so a long-running consumer cannot grow it without limit (TEST-023).");
        }

        [Test]
        public void ADeliveryWindowBelongsToOneWorldIncarnation()
        {
            WorldObservation observation = Build(8, 1UL);
            Assert.Throws<ArgumentException>(() => new DelayedConsumerDelivery(
                observation,
                new EventCursor(ObservationFixture.OtherWorld, EventSequence.Zero),
                4,
                4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DelayedConsumerDelivery(
                observation, new EventCursor(ObservationFixture.World, EventSequence.Zero), 0, 4));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DelayedConsumerDelivery(
                observation, new EventCursor(ObservationFixture.World, EventSequence.Zero), 4, 0));
        }

        [Test]
        public void AConsumerOfAWorldWithoutAPlaneIdlesInsteadOfFailing()
        {
            var snapshots = new StepPublicationStore(ObservationFixture.World, 4, 2);
            var observation = new WorldObservation(snapshots, null, null);
            snapshots.Publish(ObservationFixture.Commit(1UL));

            var consumer = new DelayedConsumerDelivery(
                observation, new EventCursor(ObservationFixture.World, EventSequence.Zero), 4, 4);
            DeliveryBatch batch = consumer.Poll();
            Assert.That(batch.Disposition, Is.EqualTo(DeliveryDisposition.NoEvents));
            Assert.That(consumer.DeliveredCount, Is.Zero);
            Assert.That(consumer.Cursor.Sequence, Is.EqualTo(EventSequence.Zero));
        }
    }
}
