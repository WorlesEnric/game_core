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
    /// the W1 step boundary that proves publication happens exactly once per committed step.
    /// </summary>
    public sealed class PublishedStepImage
    {
        public PublishedStepImage(SnapshotToken token, FrozenPayload state, ContentHash stateHash, int eventCount)
        {
            Token = token;
            State = state ?? throw new ArgumentNullException(nameof(state));
            StateHash = stateHash;
            EventCount = eventCount;
        }

        public SnapshotToken Token { get; }

        public FrozenPayload State { get; }

        public ContentHash StateHash { get; }

        public int EventCount { get; }

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
    /// Bounded store of committed step images. Observers lease immutable images at published tokens only (P-045);
    /// a token outside retention reports expiry, and a saturated lease pool reports backpressure instead of
    /// overwriting leased memory (P-007).
    /// </summary>
    public sealed class StepPublicationStore : IObservationReader
    {
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

        /// <summary>Number of retained committed images; older ones are dropped with an explicit expiry report.</summary>
        public int Retention { get; }

        public int MaxConcurrentLeases { get; }

        public int ActiveLeaseCount => activeLeases.Count;

        public int PublishedCount { get; private set; }

        /// <summary>Publications refused because they were stale or duplicates; a correct commit never hits this.</summary>
        public int RefusedPublicationCount { get; private set; }

        public PublishedStepImage? Last { get; private set; }

        public IReadOnlyList<PublishedStepImage> Retained => retained;

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

            if (Last != null)
            {
                SnapshotToken last = Last.Token;
                bool staleStep = token.LogicalStepId.CompareTo(last.LogicalStepId) < 0;
                bool duplicate = token.Equals(last);
                bool staleEpoch = token.AssemblyEpoch.CompareTo(last.AssemblyEpoch) < 0;
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

            Last = image;
            PublishedCount++;
            retained.Add(image);
            if (retained.Count > Retention)
            {
                retained.RemoveAt(0);
            }

            return true;
        }

        /// <summary>True when the token identifies a retained committed image (P-045).</summary>
        public bool HasPublished(SnapshotToken token)
        {
            for (int i = 0; i < retained.Count; i++)
            {
                if (retained[i].Token.Equals(token))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetImage(SnapshotToken token, out PublishedStepImage? image)
        {
            for (int i = 0; i < retained.Count; i++)
            {
                if (retained[i].Token.Equals(token))
                {
                    image = retained[i];
                    return true;
                }
            }

            image = null;
            return false;
        }

        /// <summary>Leases one retained immutable image; expected refusals are values, never exceptions (P-007).</summary>
        public SnapshotAcquireResult Acquire(SnapshotToken token)
        {
            if (!token.World.Session.Equals(World.Session))
            {
                return SnapshotAcquireResult.ForeignWorld(token);
            }

            if (activeLeases.Count >= MaxConcurrentLeases)
            {
                return SnapshotAcquireResult.Backpressure(token);
            }

            if (!TryGetImage(token, out PublishedStepImage? image) || image == null)
            {
                return SnapshotAcquireResult.Expired(token);
            }

            var lease = new SnapshotLease(this, image);
            activeLeases.Add(lease);
            return SnapshotAcquireResult.Acquired(token, lease);
        }

        private void Release(SnapshotLease lease) => activeLeases.Remove(lease);

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
