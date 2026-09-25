// GameCore.Derivation — the service-consumer reverse index (GC-013, P-011/P-012/P-023).
//
// P-023 lists "service consumers" among the indexes the implementation must maintain, and 02 s7 says a provider
// mount/unmount/replacement invalidates "its source scope, rule contracts, service consumers". The consumer graph
// is declared data — a manifest's `ServiceDependencies` name each required/optional contract, and
// `ServiceExports` name what an installation offers — so this index inverts it:
//
//   contract  ->  installations that depend on it        (ConsumersOf)
//   provider  ->  installations that could bind to it    (ConsumersOfProvider)
//   scope     ->  consumers whose visibility starts there (ConsumersUnder)
//
// Visibility follows P-011: a consumer may bind a provider at its own scope or a visible ancestor, and a provider
// is visible to descendants when it exports. Sibling search is forbidden, so this walk only ever goes upward from
// the consumer or downward from an exporting provider — never sideways.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>One declared service dependency of one active installation: the consumer edge of the graph.</summary>
    public readonly struct ServiceConsumerEdge
    {
        public readonly DerivationInstall Consumer;
        public readonly ServiceDependency Dependency;

        public ServiceConsumerEdge(DerivationInstall consumer, ServiceDependency dependency)
        {
            Consumer = consumer;
            Dependency = dependency;
        }

        public ContractRef Contract => Dependency.Contract;

        public bool IsRequired => Dependency.Required;

        public override string ToString() =>
            "Consumer(" + Consumer.Instance.ToString() + "->" + Contract.ToString() + ")";
    }

    /// <summary>Reverse service adjacency of one snapshot: who consumes what, and who a provider serves (P-011).</summary>
    public sealed class ServiceConsumerIndex
    {
        private readonly DerivationSnapshot snapshot;
        private readonly ScopeMembershipIndex membership;
        private readonly List<ServiceConsumerEdge> edges = new List<ServiceConsumerEdge>();
        private readonly Dictionary<Id128, List<ServiceConsumerEdge>> byContract;
        private readonly Dictionary<Id128, List<PluginInstanceId>> providersByContract;

        private ServiceConsumerIndex(DerivationSnapshot snapshot, ScopeMembershipIndex membership)
        {
            this.snapshot = snapshot;
            this.membership = membership;
            byContract = new Dictionary<Id128, List<ServiceConsumerEdge>>();
            providersByContract = new Dictionary<Id128, List<PluginInstanceId>>();

            IReadOnlyList<DerivationInstall> installs = snapshot.ActiveInstalls;
            for (int i = 0; i < installs.Count; i++)
            {
                DerivationInstall install = installs[i];
                IReadOnlyList<ServiceDependency> dependencies = install.Manifest.ServiceDependencies;
                for (int d = 0; d < dependencies.Count; d++)
                {
                    ServiceConsumerEdge edge = new ServiceConsumerEdge(install, dependencies[d]);
                    edges.Add(edge);
                    if (!byContract.TryGetValue(edge.Contract.ContractId.Value, out List<ServiceConsumerEdge>? list))
                    {
                        list = new List<ServiceConsumerEdge>();
                        byContract.Add(edge.Contract.ContractId.Value, list);
                    }

                    list.Add(edge);
                }

                IReadOnlyList<ServiceExport> exports = install.Manifest.ServiceExports;
                for (int e = 0; e < exports.Count; e++)
                {
                    Id128 contract = exports[e].Contract.ContractId.Value;
                    if (!providersByContract.TryGetValue(contract, out List<PluginInstanceId>? list))
                    {
                        list = new List<PluginInstanceId>();
                        providersByContract.Add(contract, list);
                    }

                    list.Add(install.Instance);
                }
            }
        }

        /// <summary>Builds the service-consumer index of one snapshot.</summary>
        public static ServiceConsumerIndex Build(DerivationSnapshot snapshot) =>
            Build(snapshot, ScopeMembershipIndex.Build(snapshot));

        /// <summary>Builds the index reusing an already-built membership index.</summary>
        public static ServiceConsumerIndex Build(DerivationSnapshot snapshot, ScopeMembershipIndex membership)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (membership == null)
            {
                throw new ArgumentNullException(nameof(membership));
            }

            return new ServiceConsumerIndex(snapshot, membership);
        }

        /// <summary>Every declared dependency edge, in canonical (installation, declaration) order.</summary>
        public IReadOnlyList<ServiceConsumerEdge> Edges => edges;

        /// <summary>Installations declaring a dependency on one service contract (P-011).</summary>
        public IReadOnlyList<ServiceConsumerEdge> ConsumersOf(ContractRef contract) =>
            byContract.TryGetValue(contract.ContractId.Value, out List<ServiceConsumerEdge>? list)
                ? list
                : (IReadOnlyList<ServiceConsumerEdge>)Array.Empty<ServiceConsumerEdge>();

        /// <summary>Installations declaring an export of one service contract, canonical order (P-011).</summary>
        public IReadOnlyList<PluginInstanceId> ProvidersOf(ContractRef contract) =>
            providersByContract.TryGetValue(contract.ContractId.Value, out List<PluginInstanceId>? list)
                ? list
                : (IReadOnlyList<PluginInstanceId>)Array.Empty<PluginInstanceId>();

        /// <summary>
        /// Consumers whose declared visibility can reach one provider installation: same scope, a visible
        /// ancestor, or the provider's own scope subtree when it exports (P-011). Sibling scopes are never
        /// returned, because they are not on either path.
        /// </summary>
        public IReadOnlyList<ServiceConsumerEdge> ConsumersOfProvider(
            PluginInstanceId provider,
            ContractRef contract,
            ServiceVisibility visibility)
        {
            IReadOnlyList<ServiceConsumerEdge> candidates = ConsumersOf(contract);
            if (candidates.Count == 0 || !snapshot.TryGetInstall(provider, out DerivationInstall? source) || source == null)
            {
                return Array.Empty<ServiceConsumerEdge>();
            }

            List<ServiceConsumerEdge> consumers = new List<ServiceConsumerEdge>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                ServiceConsumerEdge edge = candidates[i];
                if (CanReach(edge, source.Scope, visibility))
                {
                    consumers.Add(edge);
                }
            }

            return consumers.AsReadOnly();
        }

        /// <summary>
        /// Every consumer that could be affected by a change at one scope: a consumer at the scope, in its
        /// subtree, or at an ancestor whose provider this scope can see. This is the closure seed of a provider
        /// mount/unmount/replace (02 s7).
        /// </summary>
        public IReadOnlyList<ServiceConsumerEdge> ConsumersAround(ScopeId scope)
        {
            if (!membership.Contains(scope))
            {
                return Array.Empty<ServiceConsumerEdge>();
            }

            List<ServiceConsumerEdge> consumers = new List<ServiceConsumerEdge>();
            HashSet<Id128> seen = new HashSet<Id128>();
            IReadOnlyList<ScopeId> below = membership.Subtree(scope);
            IReadOnlyList<ScopeId> chain = membership.Snapshot.SelfAndAncestors(scope);
            for (int e = 0; e < edges.Count; e++)
            {
                ServiceConsumerEdge edge = edges[e];
                if (!seen.Add(edge.Consumer.Instance.Value))
                {
                    continue;
                }

                ScopeId consumerScope = edge.Consumer.Scope;
                if (IsIn(below, consumerScope) || IsIn(chain, consumerScope))
                {
                    consumers.Add(edge);
                }
            }

            return consumers.AsReadOnly();
        }

        private bool CanReach(ServiceConsumerEdge edge, ScopeId providerScope, ServiceVisibility visibility)
        {
            ScopeId consumerScope = edge.Consumer.Scope;
            if (consumerScope.Equals(providerScope))
            {
                return true;
            }

            if (visibility == ServiceVisibility.ExportToDescendants && membership.IsSelfOrAncestorOf(providerScope, consumerScope))
            {
                return true;
            }

            // A dependency resolved up the chain may still be satisfied by a nearer provider; the edge stays
            // reachable when the provider scope is on the consumer's visible path (P-011, no sibling search).
            return membership.IsSelfOrAncestorOf(providerScope, consumerScope)
                && (edge.Dependency.Domain == ServiceResolutionDomain.AncestorsAndSelf
                    || edge.Dependency.Domain == ServiceResolutionDomain.WorldImported);
        }

        private static bool IsIn(IReadOnlyList<ScopeId> scopes, ScopeId candidate)
        {
            for (int i = 0; i < scopes.Count; i++)
            {
                if (scopes[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
