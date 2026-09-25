// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - restore reservation ledger (GC-018).
//
// Normative sources: 00 P-050 ("Create/restore use a caller-reserved fresh WorldId before allocation; the
// application's bounded host ledger holds this session reservation and operation until the new world owns it, and
// retains the reservation through failed/cancelled attempts for its process lifetime... A per-session ledger returns
// the same pending/terminal result for a retransmission"), O-21 ("Preconditions: C/unexposed new world→B; validate
// schema/catalog, rebuild identities/composition, repair references, restore state and cursors before Running.
// Failure: Unsupported schema/migration rejects without affecting existing world; cancel destroys staged world;
// duplicate returns same restored session.") and P-049 ("Recovery creates a new `WorldId`... old callbacks/handles
// never become valid").
//
// The ledger is deliberately small and engine-free: it exists so a restore cannot reuse a session id, cannot
// allocate a second world for one reservation, and cannot lose the record of a failed attempt.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Persistence
{
    /// <summary>Terminal state of one restore attempt (O-21).</summary>
    public enum RestoreAttemptOutcome
    {
        /// <summary>Reserved, not yet started.</summary>
        Pending = 0,

        /// <summary>The staged world was validated and exposed with the reserved session id (O-21).</summary>
        Published = 1,

        /// <summary>The attempt refused before exposure; the reservation is retained and the id is not reusable.</summary>
        Rejected = 2,

        /// <summary>The caller cancelled before publication; the staged world was destroyed (O-21).</summary>
        Cancelled = 3,

        /// <summary>A post-write failure faulted the staging world, which is destroyed and never exposed (P-031).</summary>
        Faulted = 4,
    }

    /// <summary>One reserved restore attempt; the durable answer to a retransmitted restore request (P-050).</summary>
    public sealed class RestoreAttempt
    {
        internal RestoreAttempt(WorldId session, OperationId operation, ContentHash inputHash)
        {
            Session = session;
            Operation = operation;
            InputHash = inputHash;
            Outcome = RestoreAttemptOutcome.Pending;
        }

        /// <summary>The caller-reserved fresh session id; never reused, including after a failed attempt (P-004, P-050).</summary>
        public WorldId Session { get; }

        public OperationId Operation { get; }

        /// <summary>Canonical hash of the request; a conflicting reuse of one operation id is rejected (P-050).</summary>
        public ContentHash InputHash { get; }

        public RestoreAttemptOutcome Outcome { get; internal set; }

        public DiagnosticCode Code { get; internal set; }

        public string Detail { get; internal set; } = string.Empty;

        /// <summary>How many times this reservation was observed by a request (the first request included).</summary>
        public int RequestCount { get; internal set; }

        public bool IsTerminal => Outcome != RestoreAttemptOutcome.Pending;

        public override string ToString() =>
            "restore(" + Session.Session.ToString() + "," + Outcome.ToString() + ")";
    }

    /// <summary>The outcome of reserving a session for one restore request (P-050).</summary>
    public enum RestoreReservationKind
    {
        /// <summary>The reservation is new; the caller may stage a world for it.</summary>
        Fresh = 0,

        /// <summary>A retransmission of the same request; the same attempt is returned and nothing is staged again.</summary>
        Retransmission = 1,

        /// <summary>One operation id was reused with a different request; never overwritten (P-050).</summary>
        IdempotencyConflict = 2,

        /// <summary>The session id is already reserved or live; a session is never reused (P-004, P-050).</summary>
        SessionInUse = 3,

        /// <summary>The request named an all-zero session, which is not a valid identity (P-004).</summary>
        InvalidSession = 4,

        /// <summary>The ledger's bounded capacity is exhausted; the request is refused, not queued forever (P-022).</summary>
        LedgerFull = 5,
    }

    /// <summary>
    /// Bounded host ledger of restore reservations. One entry per reserved session, retained for the process
    /// lifetime including after a failed or cancelled attempt, so an old session id can never be handed out twice
    /// (P-050).
    /// </summary>
    public sealed class RestoreReservationLedger
    {
        private readonly Dictionary<Id128, RestoreAttempt> bySession = new Dictionary<Id128, RestoreAttempt>();
        private readonly Dictionary<OperationId, RestoreAttempt> byOperation = new Dictionary<OperationId, RestoreAttempt>();
        private readonly int capacity;

        public RestoreReservationLedger(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A reservation ledger requires positive capacity.");
            }

            this.capacity = capacity;
        }

        public int Capacity => capacity;

        /// <summary>Reservations held, including terminal ones; they are never forgotten (P-050).</summary>
        public int Count => bySession.Count;

        /// <summary>Requests refused a reservation because of a conflicting operation id (P-050).</summary>
        public int ConflictCount { get; private set; }

        /// <summary>Requests refused because their session id was already reserved (P-004).</summary>
        public int ReuseRejectionCount { get; private set; }

        /// <summary>Requests served from an existing reservation rather than staging a new attempt (P-050).</summary>
        public int RetransmissionCount { get; private set; }

        /// <summary>Every reservation, in reservation order (P-008).</summary>
        public IReadOnlyList<RestoreAttempt> Attempts
        {
            get
            {
                var attempts = new List<RestoreAttempt>(bySession.Values);
                attempts.Sort(CompareAttempts);
                return attempts;
            }
        }

        /// <summary>
        /// Reserves one session for one restore request. A retransmission returns the original attempt, so a
        /// duplicate restore call never stages a second world and never returns a second session (O-21, P-050).
        /// </summary>
        public RestoreReservationKind TryReserve(
            WorldId session,
            OperationId operation,
            ContentHash inputHash,
            out RestoreAttempt? attempt,
            out DiagnosticCode code,
            out string detail)
        {
            attempt = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (session.Session.IsDefault)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "a restore reservation requires a caller-reserved fresh session id; all-zero is not an identity (P-004).";
                return RestoreReservationKind.InvalidSession;
            }

            if (bySession.TryGetValue(session.Session, out RestoreAttempt? reserved) && reserved != null)
            {
                if (reserved.Operation.Equals(operation) && reserved.InputHash.Equals(inputHash))
                {
                    // The same attempt, not a new one: copying the reservation is what makes restore idempotent.
                    reserved.RequestCount++;
                    RetransmissionCount++;
                    attempt = reserved;
                    return RestoreReservationKind.Retransmission;
                }

                ReuseRejectionCount++;
                code = DiagnosticCode.StaleHandle;
                detail = "session " + session.Session.ToString() + " is already reserved by operation "
                    + reserved.Operation.ToString() + "; a session is never reused (P-004, P-050).";
                return RestoreReservationKind.SessionInUse;
            }

            if (byOperation.TryGetValue(operation, out RestoreAttempt? existing) && existing != null)
            {
                // One operation id names one attempt, so a different request under the same id is a conflict rather
                // than an overwrite (P-050).
                ConflictCount++;
                code = DiagnosticCode.IdempotencyConflict;
                detail = "operation " + operation.ToString() + " already reserved session "
                    + existing.Session.Session.ToString() + " with another request (P-050).";
                return RestoreReservationKind.IdempotencyConflict;
            }

            if (bySession.Count >= capacity)
            {
                code = DiagnosticCode.BudgetExceeded;
                detail = "the reservation ledger holds " + capacity.ToString(CultureInfo.InvariantCulture)
                    + " reservations; a session reservation is never silently dropped (P-022, P-050).";
                return RestoreReservationKind.LedgerFull;
            }

            var created = new RestoreAttempt(session, operation, inputHash) { RequestCount = 1 };
            bySession.Add(session.Session, created);
            byOperation.Add(operation, created);
            attempt = created;
            return RestoreReservationKind.Fresh;
        }

        /// <summary>Records one terminal outcome for a reservation; a terminal outcome is never rewritten (P-051).</summary>
        public bool TrySettle(
            WorldId session,
            RestoreAttemptOutcome outcome,
            DiagnosticCode code,
            string detail)
        {
            if (!bySession.TryGetValue(session.Session, out RestoreAttempt? attempt) || attempt == null)
            {
                return false;
            }

            if (attempt.IsTerminal && attempt.Outcome != RestoreAttemptOutcome.Pending)
            {
                // A published attempt stays published: a later report cannot unpublish an exposed world (P-030).
                return false;
            }

            attempt.Outcome = outcome;
            attempt.Code = code;
            attempt.Detail = detail ?? string.Empty;
            return true;
        }

        /// <summary>Looks up the attempt a session was reserved for; false when the session was never reserved.</summary>
        public bool TryGet(WorldId session, out RestoreAttempt? attempt)
            => bySession.TryGetValue(session.Session, out attempt);

        /// <summary>Looks up the attempt an operation reserved; false when the operation is unknown (P-050).</summary>
        public bool TryGet(OperationId operation, out RestoreAttempt? attempt)
            => byOperation.TryGetValue(operation, out attempt);

        private static int CompareAttempts(RestoreAttempt left, RestoreAttempt right) =>
            left.Session.Session.CompareTo(right.Session.Session);
    }
}
