// GameCore.Observation.Tests — GC-016: immutable committed images and explicit retention refusals (O-17, P-007,
// P-045).
//
// Every observation below is read from a real Unity world built by `ObservationFamilyHarness`: the same host, the
// same bounded `StepPublicationStore` and the same frozen boundary lease the checkpoint seam consumes. The
// expectations are values the world itself reports — `host.CurrentStep`, `host.CurrentEpoch`,
// `host.Publications.Last` — never a number this file decides.
//
// The store-level observations of the saturated-lease-pool case run against a deliberately small
// `StepPublicationStore` over the same world incarnation, because the host's own store is configured with the
// production bounds (32 images, 16 leases) and would need 17 live leases to refuse one. The host's own counts are
// asserted separately in the same test.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Observation;
using GameCore.Unity.Runtime;
using NUnit.Framework;
using Unity.Entities;

namespace GameCore.Observation.Tests
{
    /// <summary>Immutable observation storage: leases, verification and the pinned-image guarantee.</summary>
    [TestFixture]
    public sealed class ObservationImmutabilityTests
    {
        [TearDown]
        public void TearDown()
        {
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// One committed image is leased as a value: the token matches the newest boundary, the step and epoch match
        /// the world's own counters, the leased bytes hash to the image's payload hash, and the lease verifies (P-045).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void CommittedBoundaryLease_IsAnImmutableVerifiableImage(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                Assert.That(
                    world.Host.Observation.TryGetLatestBoundary(out SnapshotToken boundary), Is.True,
                    "the " + family + " world has published no committed image (P-045)");

                CommittedBoundaryLeaseResult result = world.LeaseLatestBoundary(16);
                Assert.That(result.Outcome, Is.EqualTo(CommittedBoundaryOutcome.Leased), result.CodeText);
                Assert.That(result.Leased, Is.True);
                Assert.That(result.Code, Is.EqualTo(DiagnosticCode.None));
                Assert.That(result.Token.Equals(boundary), Is.True, "the lease must name the boundary it resolved");
                Assert.That(result.Lease, Is.Not.Null);

                var lease = result.Lease as CommittedBoundaryLease;
                Assert.That(lease, Is.Not.Null, "the store grants its own frozen boundary lease (P-053)");
                Assert.That(lease!.IsDisposed, Is.False);
                Assert.That(lease.World.Session.Equals(world.World.Session), Is.True);
                Assert.That(lease.Token.Equals(boundary), Is.True);
                Assert.That(lease.Step.Equals(world.Host.CurrentStep), Is.True,
                    "the leased step must be the world's committed step");
                Assert.That(lease.Epoch.Equals(world.Host.CurrentEpoch), Is.True,
                    "the leased epoch must be the world's published epoch");
                Assert.That(lease.Verify(), Is.True,
                    "the leased bytes must be exactly the bytes of the committed image (P-045)");
                Assert.That(lease.IsDisposed, Is.False);

                PublishedStepImage image = world.Host.Publications.Last!;
                Assert.That(image.IsImageOf(lease.Token), Is.True);
                Assert.That(lease.PayloadHash.Equals(image.PayloadHash), Is.True);
                Assert.That(lease.StateHash.Equals(image.StateHash), Is.True);

                // The image's state bytes are the canonical payload: hashing the lease's own copy reproduces the
                // lease's payload hash without trusting the store's own answer.
                byte[] copy = Copy(lease.State.Bytes);
                Assert.That(copy.Length, Is.EqualTo(ContentHash.SizeInBytes));
                Assert.That(ContentHash.Compute(copy).Equals(lease.PayloadHash), Is.True);
                Assert.That(lease.State.Length, Is.EqualTo(ContentHash.SizeInBytes));

                // Events, when the boundary carries any, are values of this lease: at or below the boundary's step
                // and owned by this world incarnation (P-045).
                for (int i = 0; i < lease.EventCount; i++)
                {
                    CommittedEvent committed = lease.EventAt(i);
                    Assert.That(committed.Cursor.World.Session.Equals(world.World.Session), Is.True);
                    Assert.That(committed.Step.CompareTo(lease.Step) <= 0, Is.True,
                        "a boundary lease never exposes an event above its own step");
                }

                // A world that declares no boundary-facts source reports that explicitly, never an empty queue.
                Assert.That(lease.QueueDisposition, Is.EqualTo(BoundaryQueueDisposition.Unspecified));
                Assert.That(lease.QueuedCommandCount, Is.EqualTo(0));
                Assert.That(lease.StagedOperationCount, Is.EqualTo(0));

                StepPublicationStore store = world.Host.Publications;
                Assert.That(store.IsPinned(lease.Token), Is.True, "a granted lease pins its image (P-007)");
                Assert.That(store.HasPublished(lease.Token), Is.True);
                Assert.That(store.ActiveLeaseCount, Is.EqualTo(1));
                Assert.That(world.Host.Observation.BoundaryLeaseCount, Is.EqualTo(1));
                Assert.That(world.Host.Observation.NoPublicationCount, Is.EqualTo(0));

                // Writing to the copy this test took out cannot reach the store's image.
                copy[0] = (byte)(copy[0] ^ 0xFF);
                Assert.That(ContentHash.Compute(copy).Equals(image.PayloadHash), Is.False,
                    "the mutated copy must not hash to the committed payload");
                Assert.That(Read(lease.State.Bytes, 0), Is.EqualTo(Read(image.State.Bytes, 0)),
                    "the store's image is unchanged by a write to the caller's copy");
                Assert.That(store.Last!.PayloadHash.Equals(lease.PayloadHash), Is.True);
                Assert.That(lease.Verify(), Is.True);

                lease.Dispose();
                Assert.That(lease.IsDisposed, Is.True);
                Assert.That(store.IsPinned(lease.Token), Is.False, "disposal releases the pin exactly once");
                Assert.That(store.ActiveLeaseCount, Is.EqualTo(0));
                Assert.That(store.ReleasedLeaseCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// The lease is a value surface: no member of it hands out a writable world reference, and advancing the
        /// world after the lease was taken cannot change the bytes the lease holds (P-007, P-045, TEST-014).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void CommittedBoundaryLease_ExposesNoWritableStateAndSurvivesWorldAdvance(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                AssertNoWritableWorldReference(typeof(ICommittedBoundaryLease));
                AssertNoWritableWorldReference(typeof(CommittedBoundaryLease));

                CommittedBoundaryLeaseResult result = world.LeaseLatestBoundary(16);
                var lease = result.Lease as CommittedBoundaryLease;
                Assert.That(lease, Is.Not.Null, result.CodeText);

                SnapshotToken leased = lease!.Token;
                byte[] before = Copy(lease.State.Bytes);
                ContentHash payloadBefore = lease.PayloadHash;
                ContentHash stateBefore = lease.StateHash;
                int eventsBefore = lease.EventCount;

                ulong stepsBefore = world.Host.CurrentStep.Value;
                int imagesBefore = world.Host.Publications.PublishedCount;

                // Advance the world with real committed steps: each admitted command is exactly one logical step.
                world.CommitOneStep();
                world.CommitOneStep();

                Assert.That(world.Host.CurrentStep.Value, Is.EqualTo(stepsBefore + 2UL));
                Assert.That(world.Host.Publications.PublishedCount, Is.GreaterThan(imagesBefore));

                PublishedStepImage latest = world.Host.Publications.Last!;
                Assert.That(latest.Token.Equals(leased), Is.False,
                    "the world published a newer image while the lease was held");
                Assert.That(latest.Token.LogicalStepId.CompareTo(lease.Step) > 0, Is.True);

                Assert.That(lease.Token.Equals(leased), Is.True, "the lease keeps naming its own image");
                Assert.That(lease.PayloadHash.Equals(payloadBefore), Is.True);
                Assert.That(lease.StateHash.Equals(stateBefore), Is.True);
                Assert.That(lease.EventCount, Is.EqualTo(eventsBefore));
                Assert.That(Copy(lease.State.Bytes), Is.EqualTo(before),
                    "advancing the world must not change the bytes a live lease holds");
                Assert.That(lease.Verify(), Is.True);
                Assert.That(world.Host.Publications.HasPublished(leased), Is.True);
                Assert.That(world.Host.Publications.IsPinned(leased), Is.True);

                // An independent acquisition of the same token still returns the same image, so the lease is a
                // snapshot of the store's retained image and not a private copy of it.
                SnapshotAcquireResult again = world.Host.Publications.Acquire(leased);
                Assert.That(again.Succeeded, Is.True, again.Code.ToString());
                Assert.That(again.Lease, Is.Not.Null);
                Assert.That(Copy(again.Lease!.State.Bytes), Is.EqualTo(before));
                again.Lease.Dispose();

                lease.Dispose();
            }
        }

        /// <summary>
        /// A pinned image is never evicted: publishing further committed images keeps the leasable token, keeps the
        /// lease verifiable, keeps the retained window inside its hard bound, and releases the pin on disposal
        /// (P-007, P-045).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void PinnedImage_IsNeverOverwrittenOrEvictedWhileLeased(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                StepPublicationStore store = world.Host.Publications;

                CommittedBoundaryLeaseResult result = world.LeaseLatestBoundary(16);
                var lease = result.Lease as CommittedBoundaryLease;
                Assert.That(lease, Is.Not.Null, result.CodeText);
                SnapshotToken pinned = lease!.Token;
                byte[] before = Copy(lease.State.Bytes);
                int evictedBefore = store.EvictedCount;

                world.CommitOneStep();
                world.CommitOneStep();
                world.CommitOneStep();

                Assert.That(store.IsPinned(pinned), Is.True, "the still-held lease pins its image (P-007)");
                Assert.That(store.HasPublished(pinned), Is.True, "a pinned image is never dropped by retention");
                Assert.That(store.PinnedImageCount, Is.GreaterThanOrEqualTo(1));
                Assert.That(lease.Verify(), Is.True);
                Assert.That(Copy(lease.State.Bytes), Is.EqualTo(before));
                Assert.That(store.RetainedCount <= store.Retention + store.MaxConcurrentLeases, Is.True,
                    "retention plus one image per lease is the hard bound a pinned image may not exceed");
                Assert.That(store.RetainedCount <= store.MaxRetainedImages, Is.True);
                Assert.That(store.EvictedCount, Is.GreaterThanOrEqualTo(evictedBefore));
                Assert.That(world.Host.Publications.Last!.Token.Equals(pinned), Is.False);

                lease.Dispose();
                Assert.That(store.IsPinned(pinned), Is.False);

                // Whether the window has already moved past this image is the store's own answer; both outcomes are
                // the honest one, and this test asserts the answer the store really gives.
                if (store.HasPublished(pinned))
                {
                    SnapshotAcquireResult after = store.Acquire(pinned);
                    Assert.That(after.Succeeded, Is.True, after.Code.ToString());
                    Assert.That(Copy(after.Lease!.State.Bytes), Is.EqualTo(before));
                    after.Lease.Dispose();
                }
                else
                {
                    SnapshotAcquireResult expired = store.Acquire(pinned);
                    Assert.That(expired.Outcome, Is.EqualTo(SnapshotAcquireOutcome.Expired));
                    Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.CursorExpired));
                    Assert.That(expired.Lease, Is.Null);
                }
            }
        }

        /// <summary>
        /// Bounded retention refuses instead of overwriting: a saturated lease pool reports `Backpressure` with
        /// `SnapshotBackpressure`, counts it, and the image the lease reads stays byte-identical; the host's own
        /// store keeps its production bounds and never refuses a real publication (P-007, P-045).
        /// </summary>
        [TestCase(ObservationFamilyHarness.NarrativeFamily)]
        [TestCase(ObservationFamilyHarness.CardsFamily)]
        [Timeout(600000)]
        public void SaturatedLeasePool_ReportsBackpressureAndKeepsTheLeasedImage(string family)
        {
            using (ObservationFamilyWorld world = ObservationFamilyHarness.Create(family))
            {
                StepPublicationStore host = world.Host.Publications;
                Assert.That(host.Retention, Is.EqualTo(ObservationRetention.Default.SnapshotRetention));
                Assert.That(host.MaxConcurrentLeases, Is.EqualTo(ObservationRetention.Default.MaxConcurrentLeases));
                Assert.That(host.MaxRetainedImages, Is.EqualTo(ObservationRetention.Default.MaxRetainedImages));
                Assert.That(host.RetainedCount <= host.MaxRetainedImages, Is.True);
                Assert.That(host.PublishedCount, Is.GreaterThan(0));
                Assert.That(host.RefusedPublicationCount, Is.EqualTo(0),
                    "a correct commit boundary never refuses a publication (P-044)");
                Assert.That(host.ForeignWorldCount, Is.EqualTo(0));
                Assert.That(world.Host.Observation.Snapshots.World.Session.Equals(world.World.Session), Is.True);
                Assert.That(world.Host.Observation.Events, Is.Not.Null,
                    "both families declare a message plane, so their world has a committed-event store");

                var store = new StepPublicationStore(world.World, 2, 1);
                Assert.That(store.Retention, Is.EqualTo(2));
                Assert.That(store.MaxConcurrentLeases, Is.EqualTo(1));
                Assert.That(store.MaxRetainedImages, Is.EqualTo(3));
                Assert.That(store.World.Session.Equals(world.World.Session), Is.True);

                Assert.That(store.Publish(world.StepCommitOf(1UL, 3)), Is.True);
                Assert.That(store.Publish(world.StepCommitOf(2UL, 3)), Is.True);
                Assert.That(store.Publish(world.StepCommitOf(3UL, 3)), Is.True);
                Assert.That(store.RetainedCount, Is.EqualTo(2), "retention evicts the oldest unpinned image");
                Assert.That(store.EvictedCount, Is.EqualTo(1));
                Assert.That(store.PublishedCount, Is.EqualTo(3));
                Assert.That(store.RefusedPublicationCount, Is.EqualTo(0));

                PublishedStepImage newest = store.Last!;
                PublishedStepImage older = store.Retained[0];
                Assert.That(older.Token.Equals(newest.Token), Is.False);

                SnapshotAcquireResult first = store.Acquire(newest.Token);
                Assert.That(first.Succeeded, Is.True, first.Code.ToString());
                Assert.That(first.Lease, Is.Not.Null);
                ISnapshotLease lease = first.Lease!;
                byte[] before = Copy(lease.State.Bytes);

                // The pool is saturated: the second lease is refused as a value, and leased memory is untouched.
                SnapshotAcquireResult refused = store.Acquire(older.Token);
                Assert.That(refused.Outcome, Is.EqualTo(SnapshotAcquireOutcome.Backpressure));
                Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.SnapshotBackpressure));
                Assert.That(refused.Lease, Is.Null);
                Assert.That(store.BackpressureCount, Is.EqualTo(1));
                Assert.That(store.ActiveLeaseCount, Is.EqualTo(1));
                Assert.That(store.RetainedCount, Is.EqualTo(2));

                Assert.That(store.Publish(world.StepCommitOf(4UL, 3)), Is.True);
                Assert.That(store.HasPublished(newest.Token), Is.True,
                    "a pinned image survives a publication that trims retention");
                Assert.That(store.IsPinned(newest.Token), Is.True);
                Assert.That(store.PinnedImageCount, Is.EqualTo(1));
                Assert.That(store.RetainedCount <= store.MaxRetainedImages, Is.True);
                Assert.That(store.Acquire(newest.Token).Outcome, Is.EqualTo(SnapshotAcquireOutcome.Backpressure));
                Assert.That(store.BackpressureCount, Is.EqualTo(2));
                Assert.That(Copy(lease.State.Bytes), Is.EqualTo(before));
                Assert.That(store.Verify(lease, out ContentHash payloadHash), Is.True);
                Assert.That(payloadHash.Equals(newest.PayloadHash), Is.True);

                lease.Dispose();
                Assert.That(store.IsPinned(newest.Token), Is.False);
                Assert.That(store.ActiveLeaseCount, Is.EqualTo(0));
                Assert.That(store.ReleasedLeaseCount, Is.EqualTo(1));

                SnapshotAcquireResult expired = store.Acquire(older.Token);
                Assert.That(expired.Outcome, Is.EqualTo(SnapshotAcquireOutcome.Expired),
                    "the image retention dropped is not leasable any more (P-045)");
                Assert.That(expired.Code, Is.EqualTo(DiagnosticCode.CursorExpired));
                Assert.That(expired.Lease, Is.Null);
                Assert.That(store.ExpiryCount, Is.EqualTo(1));
            }
        }

        /// <summary>
        /// A boundary lease hands out values only. This is the structural half of "no writable component reference
        /// escapes inspection" (P-045, TEST-014): no member of the frozen seam or of the granted lease returns an
        /// ECS world handle, an entity, a query or a native container.
        /// </summary>
        private static void AssertNoWritableWorldReference(Type leaseType)
        {
            var forbidden = new List<string>();
            var members = new List<MemberInfo>();
            members.AddRange(leaseType.GetProperties(BindingFlags.Public | BindingFlags.Instance));
            members.AddRange(leaseType.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

            for (int i = 0; i < members.Count; i++)
            {
                Type? returned = members[i] is PropertyInfo property
                    ? property.PropertyType
                    : (members[i] as MethodInfo)?.ReturnType;

                if (returned == null)
                {
                    continue;
                }

                string name = returned.FullName ?? returned.Name;
                if (returned == typeof(Entity)
                    || returned == typeof(EntityManager)
                    || returned == typeof(EntityQuery)
                    || returned.FullName == "Unity.Entities.ArchetypeChunk"
                    || name.StartsWith("Unity.Collections.Native", StringComparison.Ordinal))
                {
                    forbidden.Add(leaseType.Name + "." + members[i].Name + " -> " + name);
                }
            }

            Assert.That(forbidden, Is.Empty,
                "a lease that exposes a writable world reference is not an immutable observation (P-045)");
        }

        private static byte[] Copy(IReadOnlyList<byte> bytes)
        {
            var copy = new byte[bytes.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = bytes[i];
            }

            return copy;
        }

        private static byte Read(IReadOnlyList<byte> bytes, int index) => bytes[index];
    }
}
