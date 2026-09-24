#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>
    /// Step publication boundary tests (GC-005, TEST-014/TEST-018). One committed step exposes exactly one immutable
    /// image, a refused publication never moves the step, readers lease images only inside retention, and a
    /// subscriber failure is a delivery diagnostic that can never undo a commit (P-044, P-045, P-031).
    /// </summary>
    [TestFixture]
    public sealed class PublicationBoundaryTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x5055424C49534857UL, 1UL));
        private static readonly WorldId OtherWorld = new WorldId(new Id128(0x5055424C49534857UL, 2UL));

        private static StepCommitEvent Commit(ulong step, ulong epoch)
        {
            var token = new SnapshotToken(World, new AssemblyEpoch(epoch), new LogicalStepId(step));
            ContentHash hash = StepFingerprint.Compute(World, new AssemblyEpoch(epoch), new LogicalStepId(step), 3);
            return new StepCommitEvent(token, null, EventSequence.Zero, hash);
        }

        [Test]
        public void PublishingOneStepExposesExactlyOneImage()
        {
            var store = new StepPublicationStore(World, 4, 2);
            Assert.That(store.PublishedCount, Is.EqualTo(0));
            Assert.That(store.Last, Is.Null);

            StepCommitEvent committed = Commit(1UL, 1UL);
            Assert.That(store.Publish(committed), Is.True);
            Assert.That(store.PublishedCount, Is.EqualTo(1));
            Assert.That(store.Last, Is.Not.Null);
            Assert.That(store.Last!.Token, Is.EqualTo(committed.Token));
            Assert.That(store.Last.State.Length, Is.EqualTo(ContentHash.SizeInBytes));
            Assert.That(store.HasPublished(committed.Token), Is.True);

            Assert.That(store.Publish(committed), Is.False, "A duplicate publication is refused, not counted twice.");
            Assert.That(store.RefusedPublicationCount, Is.EqualTo(1));
            Assert.That(store.PublishedCount, Is.EqualTo(1));

            Assert.That(store.Publish(Commit(0UL, 1UL)), Is.False, "A stale step can never replace a newer image.");
            Assert.That(store.Last!.Token.LogicalStepId, Is.EqualTo(new LogicalStepId(1UL)));
        }

        [Test]
        public void AcquireReportsAcquiredExpiredForeignWorldAndBackpressure()
        {
            var store = new StepPublicationStore(World, 2, 1);
            StepCommitEvent first = Commit(1UL, 1UL);
            StepCommitEvent second = Commit(2UL, 1UL);
            store.Publish(first);
            store.Publish(second);

            var future = new SnapshotToken(World, AssemblyEpoch.First, new LogicalStepId(9UL));
            Assert.That(store.Acquire(future).Outcome, Is.EqualTo(SnapshotAcquireOutcome.Expired));
            Assert.That(store.Acquire(future).Code, Is.EqualTo(DiagnosticCode.CursorExpired));

            var foreign = new SnapshotToken(OtherWorld, AssemblyEpoch.First, new LogicalStepId(1UL));
            SnapshotAcquireResult foreignResult = store.Acquire(foreign);
            Assert.That(foreignResult.Outcome, Is.EqualTo(SnapshotAcquireOutcome.ForeignWorld));
            Assert.That(foreignResult.Succeeded, Is.False);

            SnapshotAcquireResult acquired = store.Acquire(second.Token);
            Assert.That(acquired.Succeeded, Is.True);
            Assert.That(acquired.Lease, Is.Not.Null);
            Assert.That(store.ActiveLeaseCount, Is.EqualTo(1));

            SnapshotAcquireResult pressed = store.Acquire(first.Token);
            Assert.That(pressed.Outcome, Is.EqualTo(SnapshotAcquireOutcome.Backpressure), "A saturated lease pool never overwrites leased memory.");
            Assert.That(pressed.Code, Is.EqualTo(DiagnosticCode.SnapshotBackpressure));

            acquired.Lease!.Dispose();
            acquired.Lease.Dispose();
            Assert.That(store.ActiveLeaseCount, Is.EqualTo(0), "Releasing a lease twice releases it once.");

            // Retention of two: publishing a third image drops the oldest one, which then reports expiry.
            store.Publish(Commit(3UL, 1UL));
            Assert.That(store.HasPublished(first.Token), Is.False);
            Assert.That(store.Acquire(first.Token).Outcome, Is.EqualTo(SnapshotAcquireOutcome.Expired));
            Assert.That(store.Last!.Token.LogicalStepId, Is.EqualTo(new LogicalStepId(3UL)));
        }

        [Test]
        public void ForeignWorldImagesAreRefusedAtPublication()
        {
            var store = new StepPublicationStore(World, 4, 2);
            var foreignToken = new SnapshotToken(OtherWorld, AssemblyEpoch.First, new LogicalStepId(1UL));
            var committed = new StepCommitEvent(
                foreignToken,
                null,
                EventSequence.Zero,
                StepFingerprint.Compute(OtherWorld, AssemblyEpoch.First, LogicalStepId.First, 0));

            Assert.Throws<ArgumentException>(() => store.Publish(committed));
            Assert.That(store.PublishedCount, Is.EqualTo(0));
        }

        [Test]
        public void ASubscriberFailureIsCountedAndCannotUndoTheCommit()
        {
            var hub = new ObservationHub();
            var counting = new CountingObserver();
            hub.Add(new ThrowingObserver());
            hub.Add(counting);
            hub.Add(counting);

            Assert.That(hub.ObserverCount, Is.EqualTo(2), "The same observer is registered once.");

            StepCommitEvent committed = Commit(1UL, 1UL);
            hub.NotifyStepCommitted(committed);
            hub.NotifyLifecycle(new WorldLifecycleChange(World, WorldLifecycleState.Created, WorldLifecycleState.Running, DiagnosticCode.None));

            Assert.That(counting.Steps, Is.EqualTo(1), "One failing subscriber does not stop delivery to the others.");
            Assert.That(counting.LifecycleChanges, Is.EqualTo(1));
            Assert.That(hub.DeliveryFailureCount, Is.EqualTo(2));
            Assert.That(hub.LastDeliveryFailure, Does.Contain("fixture subscriber failure"));

            Assert.That(hub.Remove(counting), Is.True);
            Assert.That(hub.Remove(counting), Is.False);
            Assert.That(hub.ObserverCount, Is.EqualTo(1));
        }

        [Test]
        public void StepFingerprintIsScopedAndDeterministic()
        {
            ContentHash first = StepFingerprint.Compute(World, AssemblyEpoch.First, new LogicalStepId(3UL), 2);
            ContentHash again = StepFingerprint.Compute(World, AssemblyEpoch.First, new LogicalStepId(3UL), 2);
            ContentHash otherStep = StepFingerprint.Compute(World, AssemblyEpoch.First, new LogicalStepId(4UL), 2);
            ContentHash otherWorld = StepFingerprint.Compute(OtherWorld, AssemblyEpoch.First, new LogicalStepId(3UL), 2);

            Assert.That(first, Is.EqualTo(again), "The same committed shape produces the same fingerprint (TEST-022).");
            Assert.That(first, Is.Not.EqualTo(otherStep));
            Assert.That(first, Is.Not.EqualTo(otherWorld));
            Assert.That(first.IsEmpty, Is.False);
            Assert.That(StepFingerprint.Scope, Does.Contain("40 canonical bytes"));
        }

        [Test]
        public void MainThreadDisciplineIsExplicitlyCaptured()
        {
            GameCoreThreading.CaptureMainThread();

            Assert.That(GameCoreThreading.IsCaptured, Is.True);
            Assert.That(GameCoreThreading.IsMainThread(), Is.True);
            Assert.DoesNotThrow(() => GameCoreThreading.RequireMainThread("fixture operation"));
            Assert.That(GameCoreThreading.MainThreadId, Is.EqualTo(Environment.CurrentManagedThreadId()));
        }

        private sealed class CountingObserver : IWorldLifecycleObserver
        {
            public int Steps { get; private set; }

            public int LifecycleChanges { get; private set; }

            public void OnWorldLifecycleChanged(WorldLifecycleChange change) => LifecycleChanges++;

            public void OnStepCommitted(StepCommitEvent committed) => Steps++;
        }

        private sealed class ThrowingObserver : IWorldLifecycleObserver
        {
            public void OnWorldLifecycleChanged(WorldLifecycleChange change)
                => throw new InvalidOperationException("fixture subscriber failure");

            public void OnStepCommitted(StepCommitEvent committed)
                => throw new InvalidOperationException("fixture subscriber failure");
        }
    }
}
