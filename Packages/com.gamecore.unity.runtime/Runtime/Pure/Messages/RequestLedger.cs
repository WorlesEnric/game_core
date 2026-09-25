// GameCore.Execution.Messages — command/request admission, dedup and result integration (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-037 (sealed step input, duplicate request keys return
// their recorded result, a domain rejection still commits an observable rejected result in a logical step),
// P-042 (admission acceptance is not gameplay success; RequestResult is Accepted/Rejected/Cancelled/Committed
// with a reason and optional committed event cursor), P-043 (capacity/backpressure rejects before mutation) and
// P-050 (operation identity, monotonic issuer sequences, bounded result retention).
//
// The ledger is the data-plane twin of GC-004's control-lane `OperationLedger`: the control lane owns composition
// proposals, this one owns gameplay commands and typed inter-system requests. It stores no world state and calls
// nothing, so an owner's direct write never passes through it.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>Where one admitted request came from; it decides which monotonic sequence applies (P-050).</summary>
    public enum RequestOrigin
    {
        /// <summary>An external `CommandEnvelope`: the request key carries `(WorldId, IssuerId, IssuerSequence)`.</summary>
        External = 0,

        /// <summary>A typed transient request between systems inside one step; not a committed fact (P-042).</summary>
        Internal = 1,
    }

    /// <summary>How one admission attempt resolved.</summary>
    public enum RequestAdmissionKind
    {
        /// <summary>A new row was created; the request waits for its consumer stage.</summary>
        Fresh = 0,

        /// <summary>The same request key with the same input hash returned its original recorded result (P-037).</summary>
        Retransmission = 1,

        /// <summary>The request key was reused with a different input or route; the original row is kept (P-050).</summary>
        IdempotencyConflict = 2,

        /// <summary>The request identity is older than the issuer's high-water mark and was not a retransmission.</summary>
        SequenceViolation = 3,

        /// <summary>The queue is at its bounded capacity; nothing was recorded (P-043).</summary>
        CapacityRejected = 4,

        /// <summary>No generated route matches the request; nothing was recorded (P-042).</summary>
        RouteUnknown = 5,

        /// <summary>The route is retired; the work is not executed (P-047).</summary>
        RouteRetired = 6,

        /// <summary>The payload schema does not match the route's declared schema.</summary>
        SchemaMismatch = 7,

        /// <summary>The request's world or target identity does not belong to this world (P-004, P-005).</summary>
        StaleReference = 8,
    }

    /// <summary>Result of one admission attempt, including the row the caller reads status from.</summary>
    public sealed class RequestAdmission
    {
        internal RequestAdmission(
            RequestAdmissionKind kind,
            DiagnosticCode code,
            OperationId request,
            RouteId route,
            TargetId target,
            bool admitted,
            RequestOutcome outcome)
        {
            Kind = kind;
            Code = code;
            Request = request;
            Route = route;
            Target = target;
            Admitted = admitted;
            Outcome = outcome;
        }

        public RequestAdmissionKind Kind { get; }

        /// <summary><see cref="DiagnosticCode.None"/> for a fresh admission or a plain retransmission.</summary>
        public DiagnosticCode Code { get; }

        public OperationId Request { get; }

        public RouteId Route { get; }

        public TargetId Target { get; }

        /// <summary>True when the request is admitted (fresh or retransmitted); false when it was refused.</summary>
        public bool Admitted { get; }

        /// <summary>
        /// The recorded request result: `Accepted` for a fresh admission, the original result for a retransmission,
        /// or the refusal itself. Admission acceptance is never reported as gameplay success (P-042).
        /// </summary>
        public RequestOutcome Outcome { get; }

        public override string ToString() => Kind + ":" + Request.ToString();
    }

    /// <summary>One retained request row. Only this assembly's world runtime inspects it directly.</summary>
    public sealed class RequestRow
    {
        internal RequestRow(
            OperationId request,
            RouteId route,
            TargetId target,
            SchemaRef schema,
            ContentHash inputHash,
            RequestOrigin origin,
            MessageOrderKey order,
            LogicalStepId admittedStep,
            AssemblyEpoch admittedEpoch)
        {
            Request = request;
            Route = route;
            Target = target;
            Schema = schema;
            InputHash = inputHash;
            Origin = origin;
            Order = order;
            AdmittedStep = admittedStep;
            AdmittedEpoch = admittedEpoch;
            Outcome = new RequestOutcome(request, route, target, RequestResultKind.Accepted, DiagnosticCode.None, default(EventCursor), order);
        }

        public OperationId Request { get; }

        public RouteId Route { get; }

        public TargetId Target { get; }

        public SchemaRef Schema { get; }

        internal ContentHash InputHash { get; }

        public RequestOrigin Origin { get; }

        /// <summary>Canonical order key assigned at admission; the merge order of the step's batch (P-037).</summary>
        public MessageOrderKey Order { get; }

        /// <summary>Logical step the request is sealed into (P-037).</summary>
        public LogicalStepId AdmittedStep { get; }

        public AssemblyEpoch AdmittedEpoch { get; }

        /// <summary>Latest recorded result of this request.</summary>
        public RequestOutcome Outcome { get; internal set; }

        /// <summary>Logical step in which the terminal result was recorded, or zero while pending.</summary>
        public LogicalStepId TerminalStep { get; internal set; }

        /// <summary>True while the row is `Accepted` and its owner has not decided yet (P-042).</summary>
        public bool IsPending => Outcome.Kind == RequestResultKind.Accepted;

        public override string ToString() => Request.ToString() + ":" + Outcome.Kind;
    }

    /// <summary>
    /// Bounded request ledger of one world's command plane: admission, dedup, capacity backpressure, terminal
    /// results and bounded retention. It never mutates gameplay state.
    /// </summary>
    public sealed class RequestLedger
    {
        private readonly CommandRouteTable routes;
        private readonly int maxPending;
        private readonly int maxRetainedResults;
        private readonly Dictionary<OperationId, RequestRow> rows = new Dictionary<OperationId, RequestRow>();
        private readonly List<OperationId> pendingOrder = new List<OperationId>();
        private readonly LinkedList<OperationId> expiredOrder = new LinkedList<OperationId>();
        private readonly HashSet<OperationId> expired = new HashSet<OperationId>();
        private readonly Dictionary<Id128, ulong> issuerHighWater = new Dictionary<Id128, ulong>();

        private ulong nextAdmissionSequence;

        public RequestLedger(CommandRouteTable routes, int maxPending, int maxRetainedResults)
        {
            this.routes = routes ?? throw new ArgumentNullException(nameof(routes));
            if (maxPending <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPending), "A bounded command queue requires a positive capacity (P-043).");
            }

            if (maxRetainedResults <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRetainedResults), "Bounded result retention must be positive (P-050).");
            }

            this.maxPending = maxPending;
            this.maxRetainedResults = maxRetainedResults;
        }

        public int RowCount => rows.Count;

        public int PendingCount => pendingOrder.Count;

        public int ExpiredCount => expired.Count;

        public int MaxPending => maxPending;

        public int MaxRetainedResults => maxRetainedResults;

        public int AdmittedCount { get; private set; }

        public int DuplicateCount { get; private set; }

        public int ConflictCount { get; private set; }

        public int SequenceViolationCount { get; private set; }

        public int CapacityRejectedCount { get; private set; }

        public int RouteRejectedCount { get; private set; }

        public int SchemaRejectedCount { get; private set; }

        public int StaleCount { get; private set; }

        public int ExpireCount { get; private set; }

        public int CommittedCount { get; private set; }

        public int RejectedCount { get; private set; }

        public int CancelledCount { get; private set; }

        /// <summary>Highest host-assigned admission sequence issued (P-037).</summary>
        public AdmissionSequence LastAdmissionSequence => new AdmissionSequence(nextAdmissionSequence);

        /// <summary>
        /// Admits one command or typed request. The host validates the envelope, the route and the capacity first;
        /// the state owner validates gameplay later, so a fresh admission records `Accepted`, not success (P-042).
        /// </summary>
        public RequestAdmission Admit(
            OperationId request,
            RouteId route,
            TargetId target,
            SchemaRef schema,
            ContentHash inputHash,
            RequestOrigin origin,
            WorldId world,
            LogicalStepId step,
            AssemblyEpoch epoch)
        {
            if (RequestOfOtherWorld(request, world))
            {
                StaleCount++;
                return Refuse(RequestAdmissionKind.StaleReference, DiagnosticCode.StaleHandle, request, route, target);
            }

            if (rows.TryGetValue(request, out RequestRow? existing) && existing != null)
            {
                bool sameInput = existing.InputHash.Equals(inputHash)
                    && existing.Route.Value.Equals(route.Value)
                    && existing.Schema.Id.Value.Equals(schema.Id.Value)
                    && existing.Schema.Version == schema.Version;
                if (sameInput)
                {
                    // A duplicate request key returns its recorded result; it never executes a second time (P-037).
                    DuplicateCount++;
                    return new RequestAdmission(
                        RequestAdmissionKind.Retransmission,
                        existing.Outcome.Reason,
                        request,
                        existing.Route,
                        existing.Target,
                        admitted: true,
                        outcome: existing.Outcome);
                }

                ConflictCount++;
                return new RequestAdmission(
                    RequestAdmissionKind.IdempotencyConflict,
                    DiagnosticCode.IdempotencyConflict,
                    request,
                    existing.Route,
                    existing.Target,
                    admitted: false,
                    outcome: existing.Outcome);
            }

            if (expired.Contains(request) || IsExpiredByHighWater(request, origin))
            {
                // Expired is distinct from unknown, and an expired identity is never re-executed (P-050).
                ExpireCount++;
                return new RequestAdmission(
                    RequestAdmissionKind.SequenceViolation,
                    DiagnosticCode.ResultExpired,
                    request,
                    route,
                    target,
                    admitted: false,
                    outcome: new RequestOutcome(request, route, target, RequestResultKind.Rejected, DiagnosticCode.ResultExpired, default(EventCursor), default(MessageOrderKey)));
            }

            if (!routes.TryResolve(route, out CommandRoute? resolved) || resolved == null)
            {
                RouteRejectedCount++;
                return Refuse(RequestAdmissionKind.RouteUnknown, DiagnosticCode.MissingDependency, request, route, target);
            }

            if (routes.IsRetired(route))
            {
                // The route retired: the work finishes Cancelled(RouteRetired), it is not silently lost (P-047).
                RouteRejectedCount++;
                return Refuse(RequestAdmissionKind.RouteRetired, DiagnosticCode.Cancelled, request, route, target);
            }

            if (!resolved.PayloadSchema.Id.Value.Equals(schema.Id.Value) || resolved.PayloadSchema.Version != schema.Version)
            {
                SchemaRejectedCount++;
                return Refuse(RequestAdmissionKind.SchemaMismatch, DiagnosticCode.UnsupportedVersion, request, route, target);
            }

            if (pendingOrder.Count >= maxPending)
            {
                // A reliable queue at capacity backpressures: nothing is recorded and no sequence is consumed (P-043).
                CapacityRejectedCount++;
                return Refuse(RequestAdmissionKind.CapacityRejected, DiagnosticCode.BudgetExceeded, request, route, target);
            }

            nextAdmissionSequence++;
            var order = new MessageOrderKey(new AdmissionSequence(nextAdmissionSequence), AssignOrdinal(request), target.Value);
            var row = new RequestRow(request, route, target, schema, inputHash, origin, order, step, epoch);
            rows.Add(request, row);
            pendingOrder.Add(request);
            if (origin == RequestOrigin.External)
            {
                issuerHighWater[request.IssuerId] = request.IssuerSequence;
            }

            AdmittedCount++;
            return new RequestAdmission(
                RequestAdmissionKind.Fresh,
                DiagnosticCode.None,
                request,
                route,
                target,
                admitted: true,
                outcome: row.Outcome);
        }

        /// <summary>Rows still waiting for their consumer stage, in canonical admission order (P-037).</summary>
        public IReadOnlyList<RequestRow> PendingRows()
        {
            var pending = new List<RequestRow>(pendingOrder.Count);
            for (int i = 0; i < pendingOrder.Count; i++)
            {
                if (rows.TryGetValue(pendingOrder[i], out RequestRow? row) && row != null && row.IsPending)
                {
                    pending.Add(row);
                }
            }

            // Canonical order: the sealed step, then the host-assigned admission sequence, then the declared
            // ordinal and the stable origin. Registration order, dictionary enumeration and lane index never order
            // a step's admitted batch (P-008, P-037).
            pending.Sort(CompareRowsByAdmission);

            return pending;
        }

        /// <summary>Every retained row, in canonical admission order, for inspection and evidence.</summary>
        public IReadOnlyList<RequestRow> Rows()
        {
            var all = new List<RequestRow>(rows.Values);
            all.Sort(CompareRowsByAdmission);
            return all;
        }

        private static int CompareRowsByAdmission(RequestRow left, RequestRow right)
        {
            int byStep = left.AdmittedStep.CompareTo(right.AdmittedStep);
            if (byStep != 0)
            {
                return byStep;
            }

            int byAdmission = left.Order.Admitted.CompareTo(right.Order.Admitted);
            if (byAdmission != 0)
            {
                return byAdmission;
            }

            int byOrdinal = left.Order.Ordinal.CompareTo(right.Order.Ordinal);
            return byOrdinal != 0 ? byOrdinal : left.Order.OriginKey.CompareTo(right.Order.OriginKey);
        }

        public bool TryGet(OperationId request, out RequestRow? row) => rows.TryGetValue(request, out row);

        /// <summary>
        /// True when this request identity was retained and has since been dropped. An expired identity is refused
        /// instead of re-executed, and it stays distinguishable from one that was never seen (P-050).
        /// </summary>
        public bool IsExpired(OperationId request) => expired.Contains(request) || (!rows.ContainsKey(request) && IsExpiredByHighWater(request, RequestOrigin.External));

        /// <summary>
        /// Records the owner's gameplay decision for one admitted request. `Committed` requires the committed event
        /// cursor the outcome was published with; `Rejected` still commits an observable rejected result in the
        /// logical step, so both are terminal (P-037, P-042).
        /// </summary>
        public bool TrySettle(OperationId request, RequestResultKind kind, DiagnosticCode reason, EventCursor cursor, LogicalStepId step)
        {
            if (kind == RequestResultKind.Accepted)
            {
                throw new ArgumentException(
                    "Settling a request with Accepted would report admission as a gameplay result (P-042).",
                    nameof(kind));
            }

            if (!rows.TryGetValue(request, out RequestRow? row) || row == null)
            {
                return false;
            }

            if (!row.IsPending)
            {
                // A terminal result is recorded once; a repeated settle is a defect, not a second execution.
                return false;
            }

            row.Outcome = new RequestOutcome(row.Request, row.Route, row.Target, kind, reason, cursor, row.Order);
            row.TerminalStep = step;
            pendingOrder.Remove(request);

            switch (kind)
            {
                case RequestResultKind.Committed:
                    CommittedCount++;
                    break;
                case RequestResultKind.Cancelled:
                    CancelledCount++;
                    break;
                default:
                    RejectedCount++;
                    break;
            }

            RetainTerminal(request);
            return true;
        }

        /// <summary>
        /// Attaches the committed event cursor of an already-committed request. The cursor is only known once the
        /// step's events are published, so the owner's `Committed` result is completed here rather than guessed (P-042).
        /// </summary>
        public bool TryUpdateCommittedCursor(OperationId request, EventCursor cursor)
        {
            if (!rows.TryGetValue(request, out RequestRow? row) || row == null)
            {
                return false;
            }

            if (row.Outcome.Kind != RequestResultKind.Committed)
            {
                // Only an outcome that actually committed may carry a committed cursor.
                return false;
            }

            row.Outcome = new RequestOutcome(
                row.Request,
                row.Route,
                row.Target,
                RequestResultKind.Committed,
                row.Outcome.Reason,
                cursor,
                row.Order);
            return true;
        }

        /// <summary>
        /// Cancels admitted work that has not started, including a whole retired route (P-047). A request the owner
        /// already settled is left untouched, because cancellation never implies rollback (P-051).
        /// </summary>
        public int CancelPending(RouteId route, LogicalStepId step)
        {
            int cancelled = 0;
            var targets = new List<OperationId>();
            for (int i = 0; i < pendingOrder.Count; i++)
            {
                if (rows.TryGetValue(pendingOrder[i], out RequestRow? row) && row != null && row.Route.Value.Equals(route.Value))
                {
                    targets.Add(row.Request);
                }
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (TrySettle(targets[i], RequestResultKind.Cancelled, DiagnosticCode.Cancelled, default(EventCursor), step))
                {
                    cancelled++;
                }
            }

            return cancelled;
        }

        /// <summary>Cancels every pending request routed to one owner, e.g. when its activation is suspended (P-047).</summary>
        public int CancelPendingOfOwner(OwnerId owner, LogicalStepId step)
        {
            int cancelled = 0;
            var targets = new List<OperationId>();
            for (int i = 0; i < pendingOrder.Count; i++)
            {
                if (rows.TryGetValue(pendingOrder[i], out RequestRow? row) && row != null
                    && routes.TryResolve(row.Route, out CommandRoute? resolved) && resolved != null
                    && resolved.Owner.Equals(owner))
                {
                    targets.Add(row.Request);
                }
            }

            for (int i = 0; i < targets.Count; i++)
            {
                if (TrySettle(targets[i], RequestResultKind.Cancelled, DiagnosticCode.Cancelled, default(EventCursor), step))
                {
                    cancelled++;
                }
            }

            return cancelled;
        }

        /// <summary>
        /// Retains a bounded window of terminal results. Beyond it the row is dropped and its identity is refused as
        /// `ResultExpired` rather than re-executed (P-050).
        /// </summary>
        private void RetainTerminal(OperationId request)
        {
            expiredOrder.AddLast(request);
            while (expiredOrder.Count > maxRetainedResults)
            {
                LinkedListNode<OperationId> oldest = expiredOrder.First!;
                expiredOrder.RemoveFirst();
                OperationId dropped = oldest.Value;
                rows.Remove(dropped);
                expired.Add(dropped);
            }
        }

        private bool IsExpiredByHighWater(OperationId request, RequestOrigin origin)
        {
            if (origin != RequestOrigin.External)
            {
                return false;
            }

            if (request.IssuerId.IsDefault)
            {
                return false;
            }

            return issuerHighWater.TryGetValue(request.IssuerId, out ulong highWater) && request.IssuerSequence <= highWater;
        }

        private static bool RequestOfOtherWorld(OperationId request, WorldId world)
            => !request.World.Session.Equals(world.Session);

        private static uint AssignOrdinal(OperationId request)
        {
            // The issuer sequence is the declared ordinal within one step's batch; an internal request uses zero.
            return request.IssuerSequence > uint.MaxValue ? uint.MaxValue : (uint)request.IssuerSequence;
        }

        private static RequestAdmission Refuse(
            RequestAdmissionKind kind,
            DiagnosticCode code,
            OperationId request,
            RouteId route,
            TargetId target)
            => new RequestAdmission(
                kind,
                code,
                request,
                route,
                target,
                admitted: false,
                outcome: new RequestOutcome(request, route, target, RequestResultKind.Rejected, code, default(EventCursor), default(MessageOrderKey)));

        public override string ToString()
            => "rows=" + rows.Count.ToString(CultureInfo.InvariantCulture)
                + ", pending=" + pendingOrder.Count.ToString(CultureInfo.InvariantCulture)
                + ", committed=" + CommittedCount.ToString(CultureInfo.InvariantCulture)
                + ", rejected=" + RejectedCount.ToString(CultureInfo.InvariantCulture)
                + ", cancelled=" + CancelledCount.ToString(CultureInfo.InvariantCulture);
    }
}
