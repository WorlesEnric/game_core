// GameCore.Execution.Tests.Observation — bounded snapshot retention, pinning and backpressure (GC-016).
//
// These cases pin the exact behaviours the task's acceptance names: "a pinned image is never overwritten", "cursor
// expiry and snapshot backpressure are explicit", and "no writable component reference escapes inspection"
// (P-007, P-044, P-045, TEST-014, TEST-023).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Observation;
using NUnit.Framework;

namespace GameCore.Execution.Tests.Observation
{
    [TestFixture]
    public sealed class SnapshotRetentionTests
    {
        [Test]
        public void PublishingExposesExactlyOneCompleteImagePerStep()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 4, 2);

            Assert.That(store.PublishedCount, Is.Zero);
            Assert.That(store.Last, Is.Null);
            Assert.That(store.TryResync(out SnapshotToken none), Is.False, "Nothing is published yet.");

            for (ulong step = 1UL; step <= 5UL; step++)
            {
                Assert.That(store.Publish(ObservationFixture.Commit(step)), Is.True);
            }

            Assert.That(store.PublishedCount, Is.EqualTo(5));
            Assert.That(store.RetainedCount, Is.EqualTo(4), "Retention is bounded by count (P-045).");
            Assert.That(store.EvictedCount, Is.EqualTo(1), "The eviction is counted, never silent.");
            Assert.That(store.Last!.Token, Is.EqualTo(ObservationFixture.Token(5UL)));
            Assert.That(store.Last!.PayloadHash, Is.EqualTo(ObservationFixture.ExpectedHash(5UL)));
            Assert.That(store.HasPublished(ObservationFixture.Token(1UL)), Is.False);
            Assert.That(store.HasPublished(ObservationFixture.Token(5UL)), Is.True);
            Assert.That(store.TryResync(out SnapshotToken latest), Is.True);
            Assert.That(latest, Is.EqualTo(ObservationFixture.Token(5UL)));

            Assert.That(store.Publish(ObservationFixture.Commit(5UL)), Is.False, "A duplicate never publishes twice.");
            Assert.That(store.Publish(ObservationFixture.Commit(4UL)), Is.False, "A stale step never replaces a newer one.");
            Assert.That(store.RefusedPublicationCount, Is.EqualTo(2));
            Assert.That(store.PublishedCount, Is.EqualTo(5), "A refused publication changes nothing.");
            Assert.That(store.Last!.Token, Is.EqualTo(ObservationFixture.Token(5UL)));
        }

        [Test]
        public void ALeasedImageIsPinnedAndNeverEvictedOrOverwritten()
        {
            // Retention of one with a single lease: the only image the window can drop is the pinned one and the
            // newest image is never a candidate, so the trim must stall and report it rather than free leased memory.
            var store = new StepPublicationStore(ObservationFixture.World, 1, 1);
            Assert.That(store.Publish(ObservationFixture.Commit(1UL)), Is.True);

            SnapshotAcquireResult acquired = store.Acquire(ObservationFixture.Token(1UL));
            Assert.That(acquired.Succeeded, Is.True);
            ISnapshotLease pinned = acquired.Lease!;
            byte[] pinnedBytes = CopyOf(pinned.State);
            Assert.That(store.IsPinned(ObservationFixture.Token(1UL)), Is.True);
            Assert.That(store.PinnedImageCount, Is.EqualTo(1));

            for (ulong step = 2UL; step <= 4UL; step++)
            {
                Assert.That(store.Publish(ObservationFixture.Commit(step)), Is.True);
            }

            Assert.That(store.HasPublished(ObservationFixture.Token(1UL)), Is.True, "A pinned image is never evicted.");
            Assert.That(store.IsPinned(ObservationFixture.Token(1UL)), Is.True);
            Assert.That(store.PinnedRetentionStallCount, Is.GreaterThan(0),
                "A trim that cannot reach its window reports the stall instead of dropping leased memory (P-007).");
            Assert.That(store.RetainedCount, Is.EqualTo(store.MaxRetainedImages),
                "Nominal window plus one image per possible lease is the hard bound (P-007).");
            Assert.That(CopyOf(pinned.State), Is.EqualTo(pinnedBytes), "The leased bytes never change underneath the reader.");
            Assert.That(store.Verify(pinned, out ContentHash payloadHash), Is.True);
            Assert.That(payloadHash, Is.EqualTo(ObservationFixture.ExpectedHash(1UL)));
            Assert.That(store.Last!.Token, Is.EqualTo(ObservationFixture.Token(4UL)), "Publication still advances.");
            Assert.That(store.HasPublished(store.Last!.Token), Is.True, "The newest image is never evicted either.");

            pinned.Dispose();
            pinned.Dispose();
            Assert.That(store.ReleasedLeaseCount, Is.EqualTo(1), "Disposing a lease twice releases it once (P-048).");
            Assert.That(store.PinnedImageCount, Is.Zero);
            Assert.That(store.HasPublished(ObservationFixture.Token(1UL)), Is.False,
                "Once the pin is gone the released image leaves the window normally.");
            Assert.That(store.RetainedCount, Is.EqualTo(1), "Retention returns to the nominal window.");
            Assert.That(store.Acquire(ObservationFixture.Token(1UL)).Outcome, Is.EqualTo(SnapshotAcquireOutcome.Expired));
            Assert.That(store.ExpiryCount, Is.EqualTo(1), "Expiry is explicit (P-045).");
        }

        [Test]
        public void LeasePoolBackpressureIsAValueThatNeverOverwritesLeasedMemory()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 4, 1);
            store.Publish(ObservationFixture.Commit(1UL));
            store.Publish(ObservationFixture.Commit(2UL));

            SnapshotAcquireResult first = store.Acquire(ObservationFixture.Token(1UL));
            Assert.That(first.Succeeded, Is.True);
            Assert.That(store.ActiveLeaseCount, Is.EqualTo(1));

            SnapshotAcquireResult pressed = store.Acquire(ObservationFixture.Token(2UL));
            Assert.That(pressed.Outcome, Is.EqualTo(SnapshotAcquireOutcome.Backpressure));

            Assert.That(pressed.Code, Is.EqualTo(DiagnosticCode.SnapshotBackpressure));
            Assert.That(pressed.Succeeded, Is.False);
            Assert.That(pressed.Lease, Is.Null);
            Assert.That(store.BackpressureCount, Is.EqualTo(1));

            Assert.That(store.Verify(first.Lease!, out ContentHash hash), Is.True);
            Assert.That(hash, Is.EqualTo(ObservationFixture.ExpectedHash(1UL)),
                "A refused lease never touched the memory the granted lease reads (P-007).");

            first.Lease!.Dispose();
            Assert.That(store.Acquire(ObservationFixture.Token(2UL)).Succeeded, Is.True,
                "Releasing the lease frees the pool again.");
        }

        [Test]
        public void ForeignWorldTokensAreRefusedAsValuesAndAtPublication()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 4, 2);
            store.Publish(ObservationFixture.Commit(1UL));

            SnapshotAcquireResult foreign = store.Acquire(
                new SnapshotToken(ObservationFixture.OtherWorld, AssemblyEpoch.First, LogicalStepId.First));
            Assert.That(foreign.Outcome, Is.EqualTo(SnapshotAcquireOutcome.ForeignWorld));
            Assert.That(foreign.Code, Is.EqualTo(DiagnosticCode.StaleHandle));
            Assert.That(store.ForeignWorldCount, Is.EqualTo(1));
            Assert.That(store.HasPublished(
                new SnapshotToken(ObservationFixture.OtherWorld, AssemblyEpoch.First, LogicalStepId.First)), Is.False);

            var foreignCommit = new StepCommitEvent(
                new SnapshotToken(ObservationFixture.OtherWorld, AssemblyEpoch.First, LogicalStepId.First),
                null,
                EventSequence.Zero,
                StepFingerprint.Compute(ObservationFixture.OtherWorld, AssemblyEpoch.First, LogicalStepId.First, 0));
            Assert.Throws<ArgumentException>(() => store.Publish(foreignCommit));
            Assert.That(store.PublishedCount, Is.EqualTo(1), "A refused publication of another incarnation changes nothing.");
        }

        [Test]
        public void VerifiedPayloadsAreCompleteAndBelongToTheirOwnToken()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 4, 4);
            for (ulong step = 1UL; step <= 3UL; step++)
            {
                store.Publish(ObservationFixture.Commit(step));
            }

            for (ulong step = 1UL; step <= 3UL; step++)
            {
                SnapshotAcquireResult acquired = store.Acquire(ObservationFixture.Token(step));
                Assert.That(acquired.Succeeded, Is.True);
                ISnapshotLease lease = acquired.Lease!;

                Assert.That(lease.Token.LogicalStepId, Is.EqualTo(new LogicalStepId(step)));
                Assert.That(lease.State.Length, Is.EqualTo(ContentHash.SizeInBytes));
                Assert.That(store.Verify(lease, out ContentHash payloadHash), Is.True);
                Assert.That(payloadHash, Is.EqualTo(ObservationFixture.ExpectedHash(step)),
                    "The leased bytes are exactly this step's committed image, never a neighbour's.");
                Assert.That(store.TryGetImage(lease.Token, out PublishedStepImage? image), Is.True);
                Assert.That(image!.IsImageOf(lease.Token), Is.True);
                Assert.That(image.PayloadHash, Is.EqualTo(payloadHash));
                Assert.That(store.TryGetImage(ObservationFixture.Token(step == 1UL ? 2UL : 1UL), out PublishedStepImage? other), Is.True);
                Assert.That(other!.PayloadHash, Is.Not.EqualTo(payloadHash), "Distinct steps have distinct images.");

                lease.Dispose();
            }

            Assert.That(store.ActiveLeaseCount, Is.Zero);
        }

        [Test]
        public void RetainedIsABoundedCopyAReaderCanEnumerateSafely()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 4, 2);
            store.Publish(ObservationFixture.Commit(1UL));
            store.Publish(ObservationFixture.Commit(2UL));

            IReadOnlyList<PublishedStepImage> snapshot = store.Retained;
            Assert.That(snapshot.Count, Is.EqualTo(2));

            store.Publish(ObservationFixture.Commit(3UL));
            Assert.That(snapshot.Count, Is.EqualTo(2), "The returned view is a copy, not the live list.");
            Assert.That(store.RetainedCount, Is.EqualTo(3));
            Assert.That(store.TrimRetention(), Is.Zero, "A window of three inside a retention of four evicts nothing.");
        }

        [Test]
        public void RetentionSettingsAreValidatedAndBounded()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationRetention(0, 8, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationRetention(8, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ObservationRetention(8, 8, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StepPublicationStore(ObservationFixture.World, 0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new StepPublicationStore(ObservationFixture.World, 1, 0));

            ObservationRetention retention = ObservationRetention.Default;
            Assert.That(retention.SnapshotRetention, Is.EqualTo(32));
            Assert.That(retention.MaxConcurrentLeases, Is.EqualTo(16));
            Assert.That(retention.MaxRetainedImages, Is.EqualTo(48));
            Assert.That(retention.ToString(), Does.Contain("images=32"));
        }

        private static byte[] CopyOf(FrozenPayload payload)
        {
            IReadOnlyList<byte> bytes = payload.Bytes;
            var copy = new byte[bytes.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = bytes[i];
            }

            return copy;
        }
    }
}
