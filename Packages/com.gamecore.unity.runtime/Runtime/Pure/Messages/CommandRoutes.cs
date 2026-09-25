// GameCore.Execution.Messages — generated-style command routing (GC-007).
//
// Normative sources: docs/game-core/00-core-protocols.md P-042 (the host validates envelope, route and capacity;
// the state owner validates gameplay; `Request` is a typed transient message routed to a named owner and consumer
// step), P-043 (bounded work; a required gameplay input overflow rejects before mutation), P-047 (a route retires
// with a terminal cancelled result unless the port declares stable-ID rebinding) and
// docs/game-core/04-unity-integration.md s8 (IL2CPP: the executable type set is known at build time, so routing is
// generated data, never reflection).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Execution.Messages
{
    /// <summary>
    /// One generated route: the stable route identity maps to exactly one owner (its port), one payload schema and
    /// one consumer stage. A generated registration table produces these; nothing is discovered reflectively.
    /// </summary>
    public sealed class CommandRoute
    {
        public CommandRoute(
            RouteId route,
            OwnerId owner,
            SchemaRef payloadSchema,
            StageId ownerStage,
            StageId consumerStage,
            BufferId ingressBuffer,
            FactoryKey ingressProducer,
            int capacity,
            bool allowsRebind)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A route declares a positive bounded capacity (P-043).");
            }

            Route = route;
            Owner = owner;
            PayloadSchema = payloadSchema;
            OwnerStage = ownerStage;
            ConsumerStage = consumerStage;
            IngressBuffer = ingressBuffer;
            IngressProducer = ingressProducer;
            Capacity = capacity;
            AllowsRebind = allowsRebind;
        }

        /// <summary>The bounded native lane an admitted command of this route is appended to (P-043).</summary>
        public BufferId IngressBuffer { get; }

        /// <summary>Declared producer key used when the host appends an admitted command (P-043).</summary>
        public FactoryKey IngressProducer { get; }

        public RouteId Route { get; }

        /// <summary>The single owner that decides gameplay for this route (P-042).</summary>
        public OwnerId Owner { get; }

        public SchemaRef PayloadSchema { get; }

        public StageId OwnerStage { get; }

        /// <summary>The stage that consumes this route's admitted requests in a logical step (P-042).</summary>
        public StageId ConsumerStage { get; }

        /// <summary>Bounded capacity of this route's reliable ingress queue (P-043).</summary>
        public int Capacity { get; }

        /// <summary>True when the port permits rebinding unexecuted work to a compatible owner (P-047).</summary>
        public bool AllowsRebind { get; }

        public override string ToString() => Route.ToString() + "->" + Owner.ToString();
    }

    /// <summary>
    /// The world's generated routing table. Lookup is a dictionary of stable ids, so a route can never be resolved
    /// by a guessed type name or a string (04 s8).
    /// </summary>
    public sealed class CommandRouteTable
    {
        private readonly Dictionary<Id128, CommandRoute> routes = new Dictionary<Id128, CommandRoute>();
        private readonly HashSet<Id128> retired = new HashSet<Id128>();

        public int Count => routes.Count;

        public int RetiredCount => retired.Count;

        /// <summary>Registers one generated route. A duplicate route id is a registration defect, not a shadow (P-039).</summary>
        public bool TryAdd(CommandRoute route, out string failure)
        {
            if (route == null)
            {
                throw new ArgumentNullException(nameof(route));
            }

            if (route.Route.Value.IsDefault)
            {
                failure = "a default zero route id is not a command route (P-042)";
                return false;
            }

            if (route.Owner.Value.IsDefault)
            {
                failure = "route " + route.Route.ToString()
                    + " names a default zero owner; a request routes to one named owner (P-042)";
                return false;
            }

            if (routes.ContainsKey(route.Route.Value))
            {
                failure = "route " + route.Route.ToString()
                    + " is registered twice; one route has one generated registration (04 s8)";
                return false;
            }

            routes.Add(route.Route.Value, route);
            failure = string.Empty;
            return true;
        }

        public bool TryResolve(RouteId route, out CommandRoute? resolved)
            => routes.TryGetValue(route.Value, out resolved);

        public bool IsRetired(RouteId route) => retired.Contains(route.Value);

        /// <summary>
        /// Retires one route. Accepted but unexecuted work to it finishes `Cancelled(RouteRetired)` unless the port
        /// declares stable-ID rebinding (P-047).
        /// </summary>
        public bool Retire(RouteId route)
        {
            if (!routes.ContainsKey(route.Value))
            {
                return false;
            }

            retired.Add(route.Value);
            return true;
        }

        /// <summary>Retires every route of one owner: the lifecycle change that ends its ingress (P-047).</summary>
        public int RetireOwner(OwnerId owner)
        {
            var toRetire = new List<Id128>();
            foreach (KeyValuePair<Id128, CommandRoute> pair in routes)
            {
                if (pair.Value.Owner.Equals(owner))
                {
                    toRetire.Add(pair.Key);
                }
            }

            for (int i = 0; i < toRetire.Count; i++)
            {
                retired.Add(toRetire[i]);
            }

            return toRetire.Count;
        }

        /// <summary>Reopens one retired route after a successful remount of its owner (P-046).</summary>
        public bool Reopen(RouteId route) => routes.ContainsKey(route.Value) && retired.Remove(route.Value);

        public IReadOnlyList<CommandRoute> RoutesInCanonicalOrder()
        {
            var all = new List<CommandRoute>(routes.Values);
            all.Sort((left, right) => left.Route.Value.CompareTo(right.Route.Value));
            return all;
        }

        /// <summary>The route that the owner declares for one payload schema/consumer stage pair, or none.</summary>
        public bool TryFindRoute(OwnerId owner, SchemaRef schema, StageId consumerStage, out CommandRoute? route)
        {
            route = null;
            CommandRoute? best = null;
            foreach (KeyValuePair<Id128, CommandRoute> pair in routes)
            {
                CommandRoute candidate = pair.Value;
                if (!candidate.Owner.Equals(owner)
                    || !candidate.PayloadSchema.Id.Value.Equals(schema.Id.Value)
                    || !candidate.ConsumerStage.Equals(consumerStage))
                {
                    continue;
                }

                if (best == null || candidate.Route.Value.CompareTo(best.Route.Value) < 0)
                {
                    best = candidate;
                }
            }

            route = best;
            return best != null;
        }

        public override string ToString()
            => "routes=" + routes.Count.ToString(CultureInfo.InvariantCulture)
                + ", retired=" + retired.Count.ToString(CultureInfo.InvariantCulture);
    }
}
