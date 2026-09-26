// GameCore.Execution.Tests.Observation — concurrent readers see complete epoch/step images (GC-016).
//
// P-045 lets an observer read at "any host thread through synchronized publication pointer" and requires immutable
// images at `(epoch, step)` publication only. This case runs a real publisher against real reader threads and
// asserts that every lease a reader obtains verifies against its own token's committed fingerprint: no reader ever
// observes a mixed, half-written or neighbour's image, and no read ever throws while the window moves underneath it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Observation;
using NUnit.Framework;

namespace GameCore.Execution.Tests.Observation
{
    [TestFixture]
    public sealed class ConcurrentObservationTests
    {
        private const int PublishedSteps = 120;

        private const int ReaderThreads = 3;

        [Test]
        public void ConcurrentReadersAlwaysLeaseACompleteImageOfTheirOwnToken()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 8, 4);
            var failures = new List<string>();
            Assert.That(store.Publish(ObservationFixture.Commit(1UL)), Is.True);
            var gate = new object();
            var verifiedByReaders = new int[1];

            var readers = new Thread[ReaderThreads];
            for (int r = 0; r < readers.Length; r++)
            {
                var reader = new Thread(() => ReadWhilePublishing(store, gate, failures, verifiedByReaders));
                reader.IsBackground = true;
                readers[r] = reader;
                reader.Start();
            }

            for (ulong step = 2UL; step <= PublishedSteps; step++)
            {
                if (!store.Publish(ObservationFixture.Commit(step)))
                {
                    Record(gate, failures, "the publisher refused a forward step");
                }
            }

            for (int r = 0; r < readers.Length; r++)
            {
                Assert.That(readers[r].Join(TimeSpan.FromSeconds(60)), Is.True, "a reader thread must finish");
            }

            lock (gate)
            {
                Assert.That(failures, Is.Empty, string.Join("; ", failures));
            }

            Assert.That(store.PublishedCount, Is.EqualTo(PublishedSteps));
            Assert.That(store.ActiveLeaseCount, Is.Zero, "Every reader disposed its lease.");
            Assert.That(store.RetainedCount, Is.LessThanOrEqualTo(store.MaxRetainedImages));
            Assert.That(store.Last!.Token.LogicalStepId, Is.EqualTo(new LogicalStepId(PublishedSteps)));
            Assert.That(verifiedByReaders[0], Is.GreaterThan(0),
                "the readers must really have leased and verified published images, not only been refused");
        }

        [Test]
        public void APinnedImageStaysReadableWhileTheWindowMovesUnderneathIt()
        {
            var store = new StepPublicationStore(ObservationFixture.World, 2, 2);
            store.Publish(ObservationFixture.Commit(1UL));

            SnapshotAcquireResult acquired = store.Acquire(ObservationFixture.Token(1UL));
            Assert.That(acquired.Succeeded, Is.True);
            ISnapshotLease pinned = acquired.Lease!;
            var expected = new List<byte>(pinned.State.Bytes);

            var failures = new List<string>();
            var gate = new object();
            var readers = new Thread[2];
            for (int r = 0; r < readers.Length; r++)
            {
                var reader = new Thread(() => ReReadPinnedImage(store, gate, failures, expected));
                reader.IsBackground = true;
                readers[r] = reader;
                reader.Start();
            }

            for (ulong step = 2UL; step <= 40UL; step++)
            {
                Assert.That(store.Publish(ObservationFixture.Commit(step)), Is.True);
            }

            for (int r = 0; r < readers.Length; r++)
            {
                Assert.That(readers[r].Join(TimeSpan.FromSeconds(60)), Is.True);
            }

            lock (gate)
            {
                Assert.That(failures, Is.Empty, string.Join("; ", failures));
            }

            Assert.That(store.IsPinned(ObservationFixture.Token(1UL)), Is.True);
            Assert.That(store.HasPublished(ObservationFixture.Token(1UL)), Is.True);
            Assert.That(store.Verify(pinned, out ContentHash hash), Is.True);
            Assert.That(hash, Is.EqualTo(ObservationFixture.ExpectedHash(1UL)));
            Assert.That(store.RetainedCount, Is.LessThanOrEqualTo(store.MaxRetainedImages));

            pinned.Dispose();
            Assert.That(store.PinnedImageCount, Is.Zero);
            Assert.That(store.ActiveLeaseCount, Is.Zero);
        }

        /// <summary>
        /// One reader loop: take the newest announced token, lease it, and prove the lease is that token's own
        /// complete image. Refusals are legal values here, because the window keeps moving while the reader runs.
        /// </summary>
        private static void ReadWhilePublishing(
            StepPublicationStore store, object gate, List<string> failures, int[] verified)
        {
            for (int i = 0; i < 400; i++)
            {
                if (!store.TryResync(out SnapshotToken token))
                {
                    continue;
                }

                if (!token.World.Session.Equals(ObservationFixture.World.Session))
                {
                    Record(gate, failures, "a reader resolved a token of another incarnation");
                    continue;
                }

                SnapshotAcquireResult acquired = store.Acquire(token);
                if (!acquired.Succeeded || acquired.Lease == null)
                {
                    if (acquired.Lease != null)
                    {
                        Record(gate, failures, "an unsuccessful acquisition carried a lease");
                    }

                    continue;
                }

                ISnapshotLease lease = acquired.Lease;
                if (!lease.Token.Equals(token))
                {
                    Record(gate, failures, "a lease named a token other than the one requested");
                }
                else if (!store.Verify(lease, out ContentHash payloadHash))
                {
                    Record(gate, failures, "a leased image did not verify against its token");
                }
                else if (!payloadHash.Equals(ObservationFixture.ExpectedHash(token.LogicalStepId.Value)))
                {
                    Record(gate, failures, "a lease carried the hash of another step");
                }
                else
                {
                    System.Threading.Interlocked.Increment(ref verified[0]);
                }

                lease.Dispose();
            }
        }

        /// <summary>One reader loop over a pinned image: its bytes never change while the publisher trims around it.</summary>
        private static void ReReadPinnedImage(
            StepPublicationStore store, object gate, List<string> failures, List<byte> expected)
        {
            for (int i = 0; i < 200; i++)
            {
                SnapshotAcquireResult repeated = store.Acquire(ObservationFixture.Token(1UL));
                if (!repeated.Succeeded || repeated.Lease == null)
                {
                    continue;
                }

                var bytes = new List<byte>(repeated.Lease.State.Bytes);
                if (bytes.Count != expected.Count)
                {
                    Record(gate, failures, "a pinned image changed its length");
                }
                else
                {
                    for (int b = 0; b < bytes.Count; b++)
                    {
                        if (bytes[b] != expected[b])
                        {
                            Record(gate, failures, "a pinned image changed its bytes");
                            break;
                        }
                    }
                }

                repeated.Lease.Dispose();
            }
        }

        private static void Record(object gate, List<string> failures, string failure)
        {
            lock (gate)
            {
                failures.Add(failure);
            }
        }
    }
}
