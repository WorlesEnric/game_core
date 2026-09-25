#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution
{
    /// <summary>
    /// One committed step image. It carries the token that identifies the image and the canonical bytes a reader
    /// may lease. Extracting real gameplay state into an image belongs to live publication (GC-008); this store is
    /// the W1 step boundary that proves publication happens exactly once per committed step. GC-016 adds the
    /// payload hash, which lets a reader verify that the bytes it leased are complete and belong to its own token
    /// instead of trusting the reference it was handed (P-045).
    /// </summary>
    public sealed class PublishedStepImage
    {
        public PublishedStepImage(SnapshotToken token, FrozenPayload state, ContentHash stateHash, int eventCount)
        {
            Token = token;
            State = state ?? throw new ArgumentNullException(nameof(state));
            StateHash = stateHash;
            EventCount = eventCount;
            byte[] payload = new byte[State.Length];
            for (int i = 0; i < payload.Length; i++)
            {
                payload[i] = State.Bytes[i];
            }

            PayloadHash = ContentHash.Compute(payload);
        }

        public SnapshotToken Token { get; }

        public FrozenPayload State { get; }

        public ContentHash StateHash { get; }

        public int EventCount { get; }

        /// <summary>
        /// SHA-256 of the frozen image bytes, independently verifiable from a leased copy.
        /// The image currently carries the canonical state fingerprint bytes, so this digest differs
        /// from <see cref="StateHash"/>.
        /// </summary>
        public ContentHash PayloadHash { get; }

        /// <summary>True when this image is the committed image of exactly <paramref name="token"/>.</summary>
        public bool IsImageOf(SnapshotToken token) => Token.Equals(token);

        public override string ToString() => "image:" + Token.ToString();
    }

    /// <summary>
    /// Canonical fingerprint of one committed step, used as <c>StepCommitEvent.StateHash</c>. Its scope is exactly
    /// the world incarnation, the assembly epoch, the committed logical step and the number of dispatched entries,
    /// so two runs of the same committed shape produce the same bytes (TEST-022). Extracting real gameplay state
    /// into the image belongs to live publication (GC-008); this fingerprint must be replaced rather than trusted
    /// for gameplay-state equality.
    /// </summary>
    public static class StepFingerprint
    {
        /// <summary>Exact scope of <see cref="Compute"/>, recorded beside every produced value.</summary>
        public const string Scope =
            "SHA-256 over 40 canonical bytes: world session high (8), world session low (8), assembly epoch (8), "
            + "logical step (8), dispatched entry count (8); every field big-endian unsigned 64-bit";

        public static ContentHash Compute(WorldId world, AssemblyEpoch epoch, LogicalStepId step, int dispatchedCount)
        {
            byte[] record = new byte[40];
            Id128Codec.WriteBigEndian(world.Session, record, 0);
            WriteUInt64BigEndian(epoch.Value, record, 16);
            WriteUInt64BigEndian(step.Value, record, 24);
            WriteUInt64BigEndian(dispatchedCount < 0 ? 0UL : (ulong)dispatchedCount, record, 32);
            return ContentHash.Compute(record);
        }

        private static void WriteUInt64BigEndian(ulong value, byte[] destination, int offset)
        {
            for (int i = 0; i < 8; i++)
            {
                destination[offset + i] = (byte)(value >> ((7 - i) * 8));
            }
        }
    }


    /// <summary>
    /// Bounded store of committed step images (GC-005, extended by GC-016). Observers lease immutable images at
    /// published tokens only (P-045); a token outside retention reports expiry, and a saturated lease pool reports
    /// backpressure instead of overwriting leased memory (P-007).
    ///
    /// GC-016 adds three properties the initial slice did not have and the W5 gate needs:
    ///
    ///  * a **pinned image is never evicted**. Eviction skips every image a live lease holds, so the bytes a reader
    ///    is reading can never be dropped underneath it; while observers hold leases the retained window may exceed
    ///    the nominal <see cref="Retention"/>, bounded by <see cref="MaxRetainedImages"/>. When every candidate is
    ///    pinned the trim stops and reports it instead of silently dropping one.
    ///  * **concurrent readers**. Reads (`Acquire`, `TryGetImage`, `HasPublished`, `Retained`, lease disposal) are
    ///    synchronized against each other and against the publication, so a reader thread can lease from the
    ///    published pointer while the owning main thread publishes. Publication itself stays serialized: it is the
    ///    world's commit boundary (P-030, P-044).
    ///  * **explicit counters** for expiry, backpressure, foreign worlds, eviction and pinned stalls, so retention
    ///    behaviour is evidence rather than inference (TEST-023).
    /// </summary>
    public sealed class StepPublicationStore : IObservationReader
    {
        private readonly object gate = new object();
        private readonly List<PublishedStepImage> retained = new List<PublishedStepImage>();
        private readonly List<SnapshotLease> activeLeases = new List<SnapshotLease>();

        public StepPublicationStore(WorldId world, int retention, int maxConcurrentLeases)
        {
            if (retention <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(retention), "Retention must be positive.");
            }

            if (maxConcurrentLeases <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrentLeases), "The lease pool must be positive.");
            }

            World = world;
            Retention = retention;
            MaxConcurrentLeases = maxConcurrentLeases;
        }

        public WorldId World { get; }

        /// <summary>Number of retained committed images; older unpinned ones are dropped with an explicit report.</summary>
        public int Retention { get; }

        public int MaxConcurrentLeases { get; }

        /// <summary>Nominal window plus one image per possible lease: the hard bound a pinned image may not exceed.</summary>
        public int MaxRetainedImages => Retention + MaxConcurrentLeases;

        public int ActiveLeaseCount
        {
            get
            {
                lock (gate)
                {
                    return activeLeases.Count;
                }
            }
        }

        /// <summary>Retained committed images right now; the nominal window unless observers hold leases.</summary>
        public int RetainedCount
        {
            get
            {
                lock (gate)
                {
                    return retained.Count;
                }
            }
        }

        public int PublishedCount { get; private set; }

        /// <summary>Publications refused because they were stale or duplicates; a correct commit never hits this.</summary>
        public int RefusedPublicationCount { get; private set; }

        /// <summary>Images dropped by retention because they were outside the window and pinned by nobody.</summary>
        public int EvictedCount { get; private set; }

        /// <summary>Trims that had to stop because every eviction candidate was pinned by a live lease (P-007).</summary>
        public int PinnedRetentionStallCount { get; private set; }

        /// <summary>Acquisitions refused because the lease pool was saturated.</summary>
        public int BackpressureCount { get; private set; }

        /// <summary>Acquisitions refused because the token is outside retention (P-045).</summary>
        public int ExpiryCount { get; private set; }

        /// <summary>Acquisitions refused because the token belongs to another world incarnation (P-004).</summary>
        public int ForeignWorldCount { get; private set; }

        /// <summary>Leases disposed, at most once each.</summary>
        public int ReleasedLeaseCount { get; private set; }

        public PublishedStepImage? Last { get; private set; }

        /// <summary>
        /// Retained images as a bounded copy, so a reader can enumerate them while the publisher appends without
        /// risking a collection-modified failure. Take it once per inspection, not per element.
        /// </summary>
        public IReadOnlyList<PublishedStepImage> Retained
        {
            get
            {
                lock (gate)
                {
                    return retained.ToArray();
                }
            }
        }

        /// <summary>
        /// Publishes one committed step. Returns false without mutating state when the token is not a forward
        /// step, so this stays callable inside a nonthrowing commit (P-030, P-044).
        /// </summary>
        public bool Publish(StepCommitEvent committed)
        {
            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            SnapshotToken token = committed.Token;
            if (!token.World.Session.Equals(World.Session))
            {
                throw new ArgumentException(
                    "A step image may only be published into the store of its own world incarnation (P-004).",
                    nameof(committed));
            }

            lock (gate)
            {
                PublishedStepImage? last = Last;
                if (last != null)
                {
                    SnapshotToken previous = last.Token;
                    bool staleStep = token.LogicalStepId.CompareTo(previous.LogicalStepId) < 0;
                    bool duplicate = token.Equals(previous);
                    bool staleEpoch = token.AssemblyEpoch.CompareTo(previous.AssemblyEpoch) < 0;
                    if (staleStep || duplicate || staleEpoch)
                    {
                        RefusedPublicationCount++;
                        return false;
                    }
                }

                byte[] state = committed.StateHash.ToArray();
                var image = new PublishedStepImage(
                    token,
                    new FrozenPayload(state),
                    committed.StateHash,
                    committed.Events.Count);

                retained.Add(image);
                Last = image;
                PublishedCount++;
                TrimRetention();
                return true;
            }
        }

        /// <summary>True when the token identifies a retained committed image (P-045).</summary>
        public bool HasPublished(SnapshotToken token)
        {
            lock (gate)
            {
                return FindImageLocked(token) != null;
            }
        }

        public bool TryGetImage(SnapshotToken token, out PublishedStepImage? image)
        {
            lock (gate)
            {
                image = FindImageLocked(token);
                return image != null;
            }
        }

        /// <summary>True while a live lease holds this image, which is what keeps it out of eviction (P-007).</summary>
        public bool IsPinned(SnapshotToken token)
        {
            lock (gate)
            {
                return IsPinnedLocked(token);
            }
        }

        /// <summary>Retained images a live lease currently pins.</summary>
        public int PinnedImageCount
        {
            get
            {
                lock (gate)
                {
                    int count = 0;
                    for (int i = 0; i < retained.Count; i++)
                    {
                        if (IsPinnedLocked(retained[i].Token))
                        {
                            count++;
                        }
                    }

                    return count;
                }
            }
        }

        /// <summary>
        /// Restores the nominal window by evicting the oldest images no live lease pins. Two images are never
        /// evicted: one a live lease holds (P-007), and the newest — the published pointer must always name a
        /// retained image, or `Last` and `HasPublished(Last.Token)` would disagree the moment a window is full of
        /// pins. A trim whose every candidate is pinned stops and counts the stall rather than dropping memory a
        /// reader may be reading. Returns the number of images this call evicted.
        /// </summary>
        public int TrimRetention()
        {
            int evicted = 0;
            int stalled = 0;
            lock (gate)
            {
                while (retained.Count > Retention)
                {
                    int index = OldestEvictableLocked();
                    if (index < 0)
                    {
                        stalled = 1;
                        break;
                    }

                    retained.RemoveAt(index);
                    evicted++;
                }

                if (evicted > 0)
                {
                    EvictedCount += evicted;
                }

                if (stalled != 0)
                {
                    PinnedRetentionStallCount++;
                }
            }

            return evicted;
        }

        /// <summary>
        /// The newest retained committed image, which is what a lagging reader resynchronizes from (P-045).
        /// False when this world has published nothing that is still retained.
        /// </summary>
        public bool TryResync(out SnapshotToken token)
        {
            lock (gate)
            {
                PublishedStepImage? last = Last;
                if (last == null)
                {
                    token = default(SnapshotToken);
                    return false;
                }

                token = last.Token;
                return true;
            }
        }


        /// <summary>
        /// Verifies that a lease's bytes are exactly the committed image of the lease's own token. A granted lease
        /// always pins its image, so this holds for any lease of this store; it is the read-side proof that no
        /// observer is handed a mixed image (TEST-014).
        /// </summary>
        public bool Verify(ISnapshotLease lease, out ContentHash payloadHash)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            payloadHash = ContentHash.Empty;
            if (!lease.Token.World.Session.Equals(World.Session))
            {
                return false;
            }

            if (!TryGetImage(lease.Token, out PublishedStepImage? image) || image == null)
            {
                return false;
            }

            payloadHash = image.PayloadHash;
            IReadOnlyList<byte> leased = lease.State.Bytes;
            IReadOnlyList<byte> committedBytes = image.State.Bytes;
            if (leased.Count != committedBytes.Count)
            {
                return false;
            }

            for (int i = 0; i < leased.Count; i++)
            {
                if (leased[i] != committedBytes[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Leases one retained immutable image; expected refusals are values, never exceptions (P-007).</summary>
        public SnapshotAcquireResult Acquire(SnapshotToken token)
        {
            if (!token.World.Session.Equals(World.Session))
            {
                ForeignWorldCount++;
                return SnapshotAcquireResult.ForeignWorld(token);
            }

            lock (gate)
            {
                if (activeLeases.Count >= MaxConcurrentLeases)
                {
                    BackpressureCount++;
                    return SnapshotAcquireResult.Backpressure(token);
                }

                PublishedStepImage? image = FindImageLocked(token);
                if (image == null)
                {
                    ExpiryCount++;
                    return SnapshotAcquireResult.Expired(token);
                }

                var lease = new SnapshotLease(this, image);
                activeLeases.Add(lease);
                return SnapshotAcquireResult.Acquired(token, lease);
            }
        }

        private void Release(SnapshotLease lease)
        {
            bool removed;
            lock (gate)
            {
                removed = activeLeases.Remove(lease);
            }

            if (!removed)
            {
                return;
            }

            ReleasedLeaseCount++;
            // Releasing the last pin can make the nominal window reachable again; the publisher also trims after
            // every publication, so retention never depends on a reader disposing a lease.
            TrimRetention();
        }

        private PublishedStepImage? FindImageLocked(SnapshotToken token)
        {
            for (int i = 0; i < retained.Count; i++)
            {
                if (retained[i].Token.Equals(token))
                {
                    return retained[i];
                }
            }

            return null;
        }

        private bool IsPinnedLocked(SnapshotToken token)
        {
            for (int i = 0; i < activeLeases.Count; i++)
            {
                if (activeLeases[i].Token.Equals(token))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The oldest image that may leave the window: unpinned, and never the newest published image. -1 when
        /// every candidate is pinned, which is the stall the trim reports instead of evicting leased memory.
        /// </summary>
        private int OldestEvictableLocked()
        {
            int candidates = retained.Count - 1;
            for (int i = 0; i < candidates; i++)
            {
                if (!IsPinnedLocked(retained[i].Token))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Disposable immutable image lease; it never exposes writable world memory (P-045).</summary>
        private sealed class SnapshotLease : ISnapshotLease
        {
            private readonly StepPublicationStore store;
            private readonly PublishedStepImage image;

            public SnapshotLease(StepPublicationStore store, PublishedStepImage image)
            {
                this.store = store;
                this.image = image;
            }

            public SnapshotToken Token => image.Token;

            public FrozenPayload State => image.State;

            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                if (IsDisposed)
                {
                    return;
                }

                IsDisposed = true;
                store.Release(this);
            }
        }
    }

    /// <summary>
    /// Observer fan-out for world lifecycle changes and committed steps. Delivery happens only after the
    /// publication pointer switched, and a failing subscriber is a delivery diagnostic: it is counted and
    /// reported, and it can never roll back or unpublish a committed step (P-031, P-044, P-045).
    /// </summary>
    public sealed class ObservationHub
    {
        private readonly List<IWorldLifecycleObserver> observers = new List<IWorldLifecycleObserver>();

        public int DeliveryFailureCount { get; private set; }

        public string LastDeliveryFailure { get; private set; } = string.Empty;

        public int ObserverCount => observers.Count;

        public void Add(IWorldLifecycleObserver observer)
        {
            if (observer == null)
            {
                throw new ArgumentNullException(nameof(observer));
            }

            if (!observers.Contains(observer))
            {
                observers.Add(observer);
            }
        }

        public bool Remove(IWorldLifecycleObserver observer) => observers.Remove(observer);

        public void Clear() => observers.Clear();

        public void NotifyLifecycle(WorldLifecycleChange change)
        {
            if (change == null)
            {
                throw new ArgumentNullException(nameof(change));
            }

            for (int i = 0; i < observers.Count; i++)
            {
                try
                {
                    observers[i].OnWorldLifecycleChanged(change);
                }
                catch (Exception exception)
                {
                    RecordFailure("lifecycle", exception);
                }
            }
        }

        public void NotifyStepCommitted(StepCommitEvent committed)
        {
            if (committed == null)
            {
                throw new ArgumentNullException(nameof(committed));
            }

            for (int i = 0; i < observers.Count; i++)
            {
                try
                {
                    observers[i].OnStepCommitted(committed);
                }
                catch (Exception exception)
                {
                    RecordFailure("step commit", exception);
                }
            }
        }

        private void RecordFailure(string kind, Exception exception)
        {
            DeliveryFailureCount++;
            LastDeliveryFailure = kind + ": " + exception.GetType().FullName + ": " + exception.Message;
        }

        public override string ToString()
            => "observers=" + observers.Count.ToString(CultureInfo.InvariantCulture)
                + " failures=" + DeliveryFailureCount.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Main-thread discipline of the owned world hosts. V1 executes the world pump, dispatch and disposal on the
    /// Unity main thread; off-thread entry points are rejected instead of racing (04 s3, P-058).
    /// </summary>
    public static class GameCoreThreading
    {
        private static int mainThreadId = -1;

        public static bool IsCaptured => mainThreadId >= 0;

        public static int MainThreadId => mainThreadId;

        /// <summary>Records the calling thread as the main thread. Called by the application reset/boot path.</summary>
        public static void CaptureMainThread() => mainThreadId = Environment.CurrentManagedThreadId;

        /// <summary>True when the main thread is unknown, or the caller is it.</summary>
        public static bool IsMainThread()
            => mainThreadId < 0 || Environment.CurrentManagedThreadId == mainThreadId;

        public static void RequireMainThread(string operation)
        {
            if (!IsMainThread())
            {
                throw new InvalidOperationException(
                    operation + " must run on the Unity main thread; the world pump, dispatch and disposal are "
                    + "main-thread operations in V1 (04 s3).");
            }
        }
    }
}
