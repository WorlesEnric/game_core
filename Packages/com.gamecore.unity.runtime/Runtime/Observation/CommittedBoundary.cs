// GameCore.Execution.Observation — the committed boundary a checkpoint is taken at (GC-016).
//
// P-053 requires a checkpoint to be taken at a committed boundary with "explicit command disposition"; O-20 makes
// the boundary the captured unit ("snapshot boundary, command inclusion option") and O-21 requires the restore to
// repair references and cursors against a fully published world. GC-016 owns observation storage, so it owns the
// read side of that boundary, and this file is the FROZEN seam the persistence task (GC-018) consumes:
//
//   * ICommittedBoundaryReader.LeaseCommittedBoundary returns one immutable, disposable view of exactly one
//     committed image: the image's own token, its state hash, the immutable state bytes, the committed events at
//     or below the boundary's step, and the queued-command/staged-operation facts the checkpoint's include/reject
//     cutoff needs;
//   * the lease holds a real snapshot lease, so the image it reads can never be overwritten or evicted while the
//     checkpoint copies it (P-007);
//   * a refusal is a value with a stable code: nobody may capture a boundary this world cannot lease, and nothing
//     here mutates world state (P-051: status/query methods are pure).
//
// Additive-only after this commit: consumers may rely on these names and signatures. `ICommittedBoundaryFactsSource`
// is the seam by which the owning modules (control lane, message plane) contribute their own counts without this
// module reaching into them.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Observation
{
    /// <summary>How one committed-boundary lease attempt resolved.</summary>
    public enum CommittedBoundaryOutcome
    {
        /// <summary>The lease exists and names exactly one committed image.</summary>
        Leased = 0,

        /// <summary>The requested token is outside retention; the caller resynchronizes from the latest boundary.</summary>
        Expired = 1,

        /// <summary>The lease pool is saturated, so no new lease is granted and no leased image is overwritten (P-007).</summary>
        Backpressure = 2,

        /// <summary>The requested token belongs to another world incarnation (P-004).</summary>
        ForeignWorld = 3,

        /// <summary>This world has published no committed image yet, so there is no boundary to lease.</summary>
        NoPublication = 4,
    }

    /// <summary>
    /// How the checkpoint protocol treats commands admitted but not yet executed at the boundary it captures
    /// (P-053/O-20 "explicit command disposition"). <see cref="Unspecified"/> means the world declared no facts
    /// source, which is never silently reported as an empty queue.
    /// </summary>
    public enum BoundaryQueueDisposition
    {
        Unspecified = 0,

        /// <summary>The queued commands are part of the captured boundary.</summary>
        Included = 1,

        /// <summary>The queued commands are explicitly rejected by the capture, and their count is reported.</summary>
        Rejected = 2,
    }

    /// <summary>
    /// One boundary lease request. <see cref="IsLatest"/> asks for whatever the world has committed last, which is
    /// what a checkpoint at an idle/end-step boundary uses; otherwise <see cref="Token"/> is leased exactly, and a
    /// token outside retention is reported as <see cref="CommittedBoundaryOutcome.Expired"/>.
    /// </summary>
    public readonly struct CommittedBoundaryRequest
    {
        private CommittedBoundaryRequest(SnapshotToken token, int maxEvents, uint eventOffset, bool latest)
        {
            if (maxEvents < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvents), "An event window is never negative (P-045).");
            }

            Token = token;
            MaxEvents = maxEvents;
            EventOffset = eventOffset;
            IsLatest = latest;
        }

        /// <summary>Exact token to lease; unused when <see cref="IsLatest"/> is true.</summary>
        public SnapshotToken Token { get; }

        /// <summary>Bound on the committed events the lease exposes; zero captures the image without events.</summary>
        public int MaxEvents { get; }

        /// <summary>Events to skip from the start of retained history, so a large event tail can be paged.</summary>
        public uint EventOffset { get; }

        /// <summary>True when the lease names the latest committed image instead of an exact token.</summary>
        public bool IsLatest { get; }

        /// <summary>Leases whatever this world committed last: the boundary a checkpoint is normally taken at.</summary>
        public static CommittedBoundaryRequest Latest(int maxEvents) =>
            new CommittedBoundaryRequest(default(SnapshotToken), maxEvents, 0U, true);

        /// <summary>Leases one exact committed image at one exact event window.</summary>
        public static CommittedBoundaryRequest At(SnapshotToken token, int maxEvents, uint eventOffset)
        {
            return new CommittedBoundaryRequest(token, maxEvents, eventOffset, false);
        }

        public override string ToString() =>
            (IsLatest ? "latest" : Token.ToString())
            + "+" + EventOffset.ToString(CultureInfo.InvariantCulture)
            + "/" + MaxEvents.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The facts only the owning modules know at a committed boundary: how many commands are admitted but not yet
    /// executed, how many control-lane operations are staged and unpublished, and how the capture treats them.
    /// The observation module never invents these values; a world that supplies no source reports
    /// <see cref="BoundaryQueueDisposition.Unspecified"/> and zero counts.
    /// </summary>
    public interface ICommittedBoundaryFactsSource
    {
        /// <summary>Commands admitted at the boundary whose step has not committed yet (P-037, P-042).</summary>
        int QueuedCommandCount { get; }

        /// <summary>Control-lane operations admitted but not published at the boundary (P-051).</summary>
        int StagedOperationCount { get; }

        /// <summary>Whether the capture includes or rejects the queued commands (P-053).</summary>
        BoundaryQueueDisposition QueueDisposition { get; }
    }

    /// <summary>The facts of a world that declares no source: explicit, never a fabricated empty queue.</summary>
    public sealed class UnspecifiedBoundaryFacts : ICommittedBoundaryFactsSource
    {
        public static readonly UnspecifiedBoundaryFacts Instance = new UnspecifiedBoundaryFacts();

        private UnspecifiedBoundaryFacts()
        {
        }

        public int QueuedCommandCount => 0;

        public int StagedOperationCount => 0;

        public BoundaryQueueDisposition QueueDisposition => BoundaryQueueDisposition.Unspecified;

        public override string ToString() => "BoundaryFacts(unspecified)";
    }

    /// <summary>
    /// The immutable, disposable view of one committed boundary. It exposes only values: identifiers, hashes and
    /// frozen payload copies. There is deliberately no member that returns an <c>EntityManager</c>, an `Entity`, a
    /// native container or any writable world reference, which is what makes "no writable component reference
    /// escapes inspection" a property of the type instead of a convention (P-045, TEST-014).
    /// </summary>
    public interface ICommittedBoundaryLease : IDisposable
    {
        /// <summary>World incarnation this boundary belongs to (P-004).</summary>
        WorldId World { get; }

        /// <summary>The one committed image this boundary names.</summary>
        SnapshotToken Token { get; }

        AssemblyEpoch Epoch { get; }

        LogicalStepId Step { get; }

        /// <summary>Hash the publisher recorded for this image's semantic state (P-044).</summary>
        ContentHash StateHash { get; }

        /// <summary>SHA-256 of the frozen bytes in <see cref="State"/>.</summary>
        ContentHash PayloadHash { get; }

        /// <summary>The immutable bytes of the committed image; never live world memory (P-007, P-045).</summary>
        FrozenPayload State { get; }

        /// <summary>Committed events this lease exposes, at or below <see cref="Step"/>, in canonical order.</summary>
        int EventCount { get; }

        /// <summary>The event at one index of this lease's frozen window.</summary>
        CommittedEvent EventAt(int index);

        /// <summary>Cursor of the first exposed event; a reader continues from here.</summary>
        EventCursor FirstEventCursor { get; }

        /// <summary>Cursor of the last exposed event, or <see cref="FirstEventCursor"/> when the window is empty.</summary>
        EventCursor LastEventCursor { get; }

        /// <summary>Events dropped by retention before this window: an explicit gap, never a silent skip (P-045).</summary>
        int EventGapCount { get; }

        /// <summary>True when the window ends before the boundary and more events remain at <see cref="EventOffset"/>.</summary>
        bool HasMoreEvents { get; }

        /// <summary>How the capture treats commands queued at this boundary (P-053).</summary>
        BoundaryQueueDisposition QueueDisposition { get; }

        /// <summary>Commands admitted but not executed at this boundary.</summary>
        int QueuedCommandCount { get; }

        /// <summary>Control-lane operations staged and unpublished at this boundary.</summary>
        int StagedOperationCount { get; }

        /// <summary>True once this lease's single permitted disposal has run.</summary>
        bool IsDisposed { get; }
    }

    /// <summary>
    /// Read side of the committed boundary (frozen seam for GC-018). Implemented by the observation storage of the
    /// owning world; it reads the published pointer only and never mutates world or ledger state.
    /// </summary>
    public interface ICommittedBoundaryReader
    {
        /// <summary>The latest committed image token, if this world has published one that is still retained.</summary>
        bool TryGetLatestBoundary(out SnapshotToken token);

        /// <summary>Leases one committed boundary; every refusal is a value with a stable code (P-007, P-045).</summary>
        CommittedBoundaryLeaseResult LeaseCommittedBoundary(CommittedBoundaryRequest request);
    }

    /// <summary>Result of one boundary lease attempt, including the lease only when it was granted.</summary>
    public sealed class CommittedBoundaryLeaseResult
    {
        private CommittedBoundaryLeaseResult(
            CommittedBoundaryOutcome outcome,
            DiagnosticCode code,
            SnapshotToken token,
            ICommittedBoundaryLease? lease)
        {
            Outcome = outcome;
            Code = code;
            Token = token;
            Lease = lease;
        }

        public CommittedBoundaryOutcome Outcome { get; }

        /// <summary>Stable code of this outcome: `None` only for <see cref="CommittedBoundaryOutcome.Leased"/>.</summary>
        public DiagnosticCode Code { get; }

        public string CodeText => DiagnosticCodeText.Of(Code);

        /// <summary>The token the lease would have named, so a refusal still says which boundary was requested.</summary>
        public SnapshotToken Token { get; }

        /// <summary>Non-null only for <see cref="CommittedBoundaryOutcome.Leased"/>.</summary>
        public ICommittedBoundaryLease? Lease { get; }

        public bool Leased => Outcome == CommittedBoundaryOutcome.Leased;

        public static CommittedBoundaryLeaseResult Acquired(SnapshotToken token, ICommittedBoundaryLease lease)
        {
            if (lease == null)
            {
                throw new ArgumentNullException(nameof(lease));
            }

            return new CommittedBoundaryLeaseResult(
                CommittedBoundaryOutcome.Leased, DiagnosticCode.None, token, lease);
        }

        public static CommittedBoundaryLeaseResult Expired(SnapshotToken token) =>
            new CommittedBoundaryLeaseResult(
                CommittedBoundaryOutcome.Expired, DiagnosticCode.CursorExpired, token, null);

        public static CommittedBoundaryLeaseResult Backpressure(SnapshotToken token) =>
            new CommittedBoundaryLeaseResult(
                CommittedBoundaryOutcome.Backpressure, DiagnosticCode.SnapshotBackpressure, token, null);

        public static CommittedBoundaryLeaseResult ForeignWorld(SnapshotToken token) =>
            new CommittedBoundaryLeaseResult(
                CommittedBoundaryOutcome.ForeignWorld, DiagnosticCode.StaleHandle, token, null);

        public static CommittedBoundaryLeaseResult NoPublication() =>
            new CommittedBoundaryLeaseResult(
                CommittedBoundaryOutcome.NoPublication, DiagnosticCode.ResultExpired, default(SnapshotToken), null);

        public override string ToString() =>
            "BoundaryLease(" + Outcome.ToString() + ", " + CodeText + ", " + Token.ToString() + ")";
    }

    /// <summary>
    /// One granted boundary lease. It is constructed only by the observation storage, holds the snapshot lease that
    /// pins the image for its whole lifetime, and frees nothing but that lease on disposal.
    /// </summary>
    public sealed class CommittedBoundaryLease : ICommittedBoundaryLease
    {
        private readonly ISnapshotLease imageLease;
        private readonly IReadOnlyList<CommittedEvent> events;
        private readonly ContentHash stateHash;
        private readonly ContentHash payloadHash;
        private readonly EventCursor firstCursor;
        private readonly EventCursor lastCursor;
        private readonly int eventGapCount;
        private readonly bool hasMoreEvents;
        private readonly BoundaryQueueDisposition queueDisposition;
        private readonly int queuedCommandCount;
        private readonly int stagedOperationCount;

        internal CommittedBoundaryLease(
            ISnapshotLease imageLease,
            ContentHash stateHash,
            ContentHash payloadHash,
            IReadOnlyList<CommittedEvent>? events,
            EventCursor firstCursor,
            EventCursor lastCursor,
            int eventGapCount,
            bool hasMoreEvents,
            BoundaryQueueDisposition queueDisposition,
            int queuedCommandCount,
            int stagedOperationCount)
        {
            this.imageLease = imageLease ?? throw new ArgumentNullException(nameof(imageLease));
            this.stateHash = stateHash;
            this.payloadHash = payloadHash;
            this.events = ContractCollections.Freeze(events);
            this.firstCursor = firstCursor;
            this.lastCursor = lastCursor;
            this.eventGapCount = eventGapCount;
            this.hasMoreEvents = hasMoreEvents;
            this.queueDisposition = queueDisposition;
            this.queuedCommandCount = queuedCommandCount;
            this.stagedOperationCount = stagedOperationCount;
        }

        public WorldId World => imageLease.Token.World;

        public SnapshotToken Token => imageLease.Token;

        public AssemblyEpoch Epoch => imageLease.Token.AssemblyEpoch;

        public LogicalStepId Step => imageLease.Token.LogicalStepId;

        public ContentHash StateHash => stateHash;

        public ContentHash PayloadHash => payloadHash;

        public FrozenPayload State => imageLease.State;

        public int EventCount => events.Count;

        public CommittedEvent EventAt(int index) => events[index];

        public EventCursor FirstEventCursor => firstCursor;

        public EventCursor LastEventCursor => lastCursor;

        public int EventGapCount => eventGapCount;

        public bool HasMoreEvents => hasMoreEvents;

        public BoundaryQueueDisposition QueueDisposition => queueDisposition;

        public int QueuedCommandCount => queuedCommandCount;

        public int StagedOperationCount => stagedOperationCount;

        public bool IsDisposed { get; private set; }

        /// <summary>
        /// Verifies the leased bytes against this committed image's payload hash
        /// (P-045, TEST-014).
        /// </summary>
        public bool Verify()
        {
            IReadOnlyList<byte> leased = imageLease.State.Bytes;
            return leased.Count == ContentHash.SizeInBytes
                && payloadHash.Equals(ContentHash.Compute(ToArray(leased)));
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            imageLease.Dispose();
        }

        public override string ToString() =>
            "Boundary(" + Token.ToString() + ", events=" + events.Count.ToString(CultureInfo.InvariantCulture)
            + ", queue=" + queueDisposition.ToString() + ")";

        private static byte[] ToArray(IReadOnlyList<byte> bytes)
        {
            var copy = new byte[bytes.Count];
            for (int i = 0; i < copy.Length; i++)
            {
                copy[i] = bytes[i];
            }

            return copy;
        }
    }
}
