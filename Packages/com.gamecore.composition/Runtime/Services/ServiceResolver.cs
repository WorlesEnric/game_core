// GameCore.Composition — deterministic service resolution (P-011) and required dependency closure (P-012).
//
// P-011 fixes the resolution rules exactly: identical in both propagation modes, a consumer declares each
// dependency, a provider is private to its scope unless it exports to descendants, a service-isolation boundary
// blocks ancestor providers for named contracts, the nearest visible provider wins only with an explicit
// `OverrideAncestor` declaration (otherwise multiple visible single-binding providers are `ServiceConflict`),
// same-scope duplicate single bindings always conflict, a multi-binding contract returns every visible provider
// in registered stable order, explicit provider selection is allowed inside the visibility boundary, and
// sibling search is forbidden. P-012 adds the outcome: a missing *required* provider leaves the consumer
// `WaitingForDependencies` instead of failing, an optional binding rebinds to its declared fallback, providers
// order before consumers, and a closure cycle rejects the whole proposal.
//
// Every step is a pure function of the input graph. Nothing here reads a clock, a dictionary enumeration or an
// insertion ordinal, so resolution cannot depend on registration timing (P-008).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Composition
{
    /// <summary>
    /// One installation as the service resolver sees it: identity, scope, declared state, exports and effective
    /// dependencies. This is a pure input, not live state (05 s1).
    /// </summary>
    public sealed class ServiceNode
    {
        public ServiceNode(
            PluginInstanceId instance,
            PluginTypeId pluginType,
            ScopeId scope,
            InstallationState declaredState,
            ActivationEpoch activationEpoch,
            IReadOnlyList<ServiceExport>? exports,
            IReadOnlyList<ServiceDependency>? dependencies,
            IReadOnlyList<ServiceSelection>? selections)
        {
            Instance = instance;
            PluginType = pluginType;
            Scope = scope;
            DeclaredState = declaredState;
            ActivationEpoch = activationEpoch;
            Exports = ContractCollections.Freeze(exports);
            Dependencies = ContractCollections.Freeze(dependencies);
            Selections = CanonicalScopeOrder.SortSelections(selections);
        }

        public PluginInstanceId Instance { get; }

        public PluginTypeId PluginType { get; }

        public ScopeId Scope { get; }

        /// <summary>Declared lifecycle state before resolution (P-046).</summary>
        public InstallationState DeclaredState { get; }

        public ActivationEpoch ActivationEpoch { get; }

        public IReadOnlyList<ServiceExport> Exports { get; }

        /// <summary>Manifest-declared dependencies; an instance selection overrides only the chosen provider (P-011).</summary>
        public IReadOnlyList<ServiceDependency> Dependencies { get; }

        /// <summary>Instance-level explicit provider selections inside the visibility boundary (P-011, 05 s3).</summary>
        public IReadOnlyList<ServiceSelection> Selections { get; }

        /// <summary>
        /// True when the installation can serve a service at all. A waiting, suspended, retiring, disposed or
        /// failed installation cannot: P-012 requires required providers to prepare before their consumers.
        /// </summary>
        public bool CanProvide =>
            DeclaredState == InstallationState.Active ||
            DeclaredState == InstallationState.Preparing ||
            DeclaredState == InstallationState.Registered;

        /// <summary>The effective selected provider for a contract, preferring the instance selection (P-011).</summary>
        public ProviderInstallationId SelectedProviderFor(ContractRef contract)
        {
            for (int i = 0; i < Selections.Count; i++)
            {
                if (Selections[i].Contract.ContractId.Equals(contract.ContractId))
                {
                    return Selections[i].Provider;
                }
            }

            return default(ProviderInstallationId);
        }
    }

    /// <summary>Resolution of one consumer dependency: a binding, or the diagnostic that explains its absence.</summary>
    public sealed class DependencyResolution
    {
        public DependencyResolution(
            ServiceDependency dependency,
            IReadOnlyList<ServiceBinding>? bindings,
            DiagnosticCode code,
            IReadOnlyList<ProviderInstallationId>? visibleProviders)
        {
            Dependency = dependency;
            Bindings = ContractCollections.Freeze(bindings);
            Code = code;
            VisibleProviders = ContractCollections.Freeze(visibleProviders);
        }

        public ServiceDependency Dependency { get; }

        /// <summary>
        /// The epoch-bound bindings of this dependency in canonical order. A multi-binding contract has one
        /// binding per visible provider; a single-binding contract has at most one. For an optional dependency
        /// that resolved to no provider, the declared fallback appears here (P-011, P-012).
        /// </summary>
        public IReadOnlyList<ServiceBinding> Bindings { get; }

        /// <summary><see cref="DiagnosticCode.None"/> when resolved; otherwise the rejection reason.</summary>
        public DiagnosticCode Code { get; }

        /// <summary>Every visible provider in canonical order, so a conflict explains its smallest known set (P-052).</summary>
        public IReadOnlyList<ProviderInstallationId> VisibleProviders { get; }

        public bool Resolved => Bindings.Count != 0;
    }

    /// <summary>Resolution of one installation: resulting lifecycle state, bindings and diagnostics (P-046).</summary>
    public sealed class ServiceNodeResolution
    {
        public ServiceNodeResolution(
            ServiceNode node,
            InstallationState state,
            IReadOnlyList<DependencyResolution>? dependencies,
            IReadOnlyList<Diagnostic>? diagnostics)
        {
            Node = node;
            State = state;
            Dependencies = ContractCollections.Freeze(dependencies);
            Diagnostics = ContractCollections.Freeze(diagnostics);
        }

        public ServiceNode Node { get; }

        /// <summary><see cref="InstallationState.WaitingForDependencies"/> when a required provider is absent (P-012).</summary>
        public InstallationState State { get; }

        public IReadOnlyList<DependencyResolution> Dependencies { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>Epoch-bound bindings of this installation in canonical contract order (P-007).</summary>
        public IReadOnlyList<ServiceBinding> Bindings
        {
            get
            {
                if (!IsActive)
                {
                    return Array.Empty<ServiceBinding>();
                }

                List<ServiceBinding> bindings = new List<ServiceBinding>();
                for (int i = 0; i < Dependencies.Count; i++)
                {
                    IReadOnlyList<ServiceBinding> resolved = Dependencies[i].Bindings;
                    for (int b = 0; b < resolved.Count; b++)
                    {
                        bindings.Add(resolved[b]);
                    }
                }

                bindings.Sort(CompareBindings);
                return bindings;
            }
        }

        public bool IsActive => State == InstallationState.Active;

        /// <summary>True when a required dependency has no provider in this world (P-012).</summary>
        public bool WaitForDependencies => State == InstallationState.WaitingForDependencies;

        private static int CompareBindings(ServiceBinding left, ServiceBinding right)
        {
            int contract = left.Contract.ContractId.CompareTo(right.Contract.ContractId);
            if (contract != 0)
            {
                return contract;
            }

            return left.Provider.Value.CompareTo(right.Provider.Value);
        }
    }

    /// <summary>Whole-world resolution output plus the activation order of the required dependency closure.</summary>
    public sealed class ServiceResolution
    {
        public ServiceResolution(
            DiagnosticCode code,
            IReadOnlyList<ServiceNodeResolution>? nodes,
            IReadOnlyList<PluginInstanceId>? activationOrder)
        {
            Code = code;
            Nodes = ContractCollections.Freeze(nodes);
            ActivationOrder = ContractCollections.Freeze(activationOrder);
        }

        /// <summary><see cref="DiagnosticCode.None"/> when the whole graph resolved; otherwise the closure failure.</summary>
        public DiagnosticCode Code { get; }

        public IReadOnlyList<ServiceNodeResolution> Nodes { get; }

        /// <summary>Providers before consumers for required edges (P-012), in canonical tie-broken order.</summary>
        public IReadOnlyList<PluginInstanceId> ActivationOrder { get; }

        public bool Succeeded => Code == DiagnosticCode.None;

        public bool TryGet(PluginInstanceId instance, out ServiceNodeResolution? resolution)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Node.Instance.Equals(instance))
                {
                    resolution = Nodes[i];
                    return true;
                }
            }

            resolution = null;
            return false;
        }
    }

    /// <summary>Pure service resolver implementing P-011 visibility/conflict and P-012 closure rules.</summary>
    public static class ServiceResolver
    {
        /// <summary>
        /// Resolves every installation's dependencies against one scope tree. The order of <paramref name="nodes"/>
        /// never affects the result: providers are grouped by canonical identity and visited by scope depth.
        /// </summary>
        public static ServiceResolution Resolve(ScopeRegistry scopes, IReadOnlyList<ServiceNode>? nodes)
        {
            if (scopes == null)
            {
                throw new ArgumentNullException(nameof(scopes));
            }

            List<ServiceNode> ordered = new List<ServiceNode>();
            if (nodes != null)
            {
                for (int i = 0; i < nodes.Count; i++)
                {
                    ServiceNode node = nodes[i];
                    if (node == null)
                    {
                        throw new ArgumentException("A service node must not be null.", nameof(nodes));
                    }

                    ordered.Add(node);
                }
            }

            ordered.Sort(CompareNodes);

            // Duplicate live stable instance identities are rejected by the catalog/host before resolution
            // (P-004); the resolver reports the conflict instead of silently choosing one provider.
            for (int i = 1; i < ordered.Count; i++)
            {
                if (ordered[i].Instance.Equals(ordered[i - 1].Instance))
                {
                    return new ServiceResolution(DiagnosticCode.OwnershipConflict, Array.Empty<ServiceNodeResolution>(), Array.Empty<PluginInstanceId>());
                }
            }

            ScopeRegistry tree = scopes;
            if (!ClosureOrder(tree, ordered, out List<PluginInstanceId>? activationOrder, out DiagnosticCode closureCode))
            {
                return new ServiceResolution(closureCode, Array.Empty<ServiceNodeResolution>(), Array.Empty<PluginInstanceId>());
            }

            Dictionary<PluginInstanceId, int> indices = new Dictionary<PluginInstanceId, int>(ordered.Count);
            for (int i = 0; i < ordered.Count; i++)
            {
                indices.Add(ordered[i].Instance, i);
            }

            List<ServiceNodeResolution> resolutions = new List<ServiceNodeResolution>(ordered.Count);
            for (int i = 0; i < activationOrder!.Count; i++)
            {
                int index = indices[activationOrder[i]];
                ServiceNode node = ordered[index];
                ServiceNodeResolution resolved = ResolveNode(tree, ordered, node);
                resolutions.Add(resolved);
                ordered[index] = new ServiceNode(node.Instance, node.PluginType, node.Scope, resolved.State,
                    node.ActivationEpoch, node.Exports, node.Dependencies, node.Selections);
            }

            resolutions.Sort((left, right) => CompareNodes(left.Node, right.Node));

            return new ServiceResolution(DiagnosticCode.None, resolutions, activationOrder);
        }

        private static ServiceNodeResolution ResolveNode(ScopeRegistry scopes, List<ServiceNode> all, ServiceNode consumer)
        {
            if (consumer.DeclaredState == InstallationState.Suspended ||
                consumer.DeclaredState == InstallationState.Quiescing ||
                consumer.DeclaredState == InstallationState.Retiring ||
                consumer.DeclaredState == InstallationState.Disposed ||
                consumer.DeclaredState == InstallationState.Failed)
            {
                return new ServiceNodeResolution(consumer, consumer.DeclaredState, null, null);
            }

            List<DependencyResolution> resolutions = new List<DependencyResolution>(consumer.Dependencies.Count);
            List<Diagnostic> diagnostics = new List<Diagnostic>();
            bool waiting = false;

            for (int i = 0; i < consumer.Dependencies.Count; i++)
            {
                ServiceDependency dependency = consumer.Dependencies[i];
                List<ProviderCandidate> visible = VisibleProviders(scopes, all, consumer, dependency);
                List<ProviderCandidate> chosen = new List<ProviderCandidate>();
                DiagnosticCode code = DiagnosticCode.None;
                ProviderInstallationId selected = consumer.SelectedProviderFor(dependency.Contract);
                bool hasSelection = !selected.IsDefault || dependency.HasSelectedProvider;

                if (dependency.Contract.ContractId.IsDefault)
                {
                    // A default identity is not an identity at all; the declaration cannot be resolved (P-004).
                    code = DiagnosticCode.UnsupportedVersion;
                }
                else if (visible.Count == 0)
                {
                    code = DiagnosticCode.MissingDependency;
                }
                else if (hasSelection)
                {
                    if (selected.IsDefault)
                    {
                        selected = dependency.SelectedProvider;
                    }

                    ProviderCandidate? single = FindSelected(all, visible, selected, out code);
                    if (single.HasValue)
                    {
                        chosen.Add(single.Value);
                    }
                }
                else
                {
                    chosen = SelectBindings(visible, out code);
                }

                List<ProviderInstallationId> visibleIds = new List<ProviderInstallationId>(visible.Count);
                for (int v = 0; v < visible.Count; v++)
                {
                    visibleIds.Add(new ProviderInstallationId(visible[v].Node.Instance.Value));
                }

                List<ServiceBinding> bindings = new List<ServiceBinding>(chosen.Count);
                for (int c = 0; c < chosen.Count; c++)
                {
                    bindings.Add(new ServiceBinding(
                        chosen[c].Export.Contract,
                        new ProviderInstallationId(chosen[c].Node.Instance.Value),
                        chosen[c].Node.ActivationEpoch,
                        chosen[c].LeaseId(),
                        chosen[c].Export.BindingKind,
                        false));
                }

                if (bindings.Count == 0)
                {
                    if (dependency.Required)
                    {
                        // P-012: a missing required provider leaves the installation waiting; never half-active.
                        waiting = true;
                        diagnostics.Add(Diagnostic.Create(
                            code == DiagnosticCode.None ? DiagnosticCode.MissingDependency : code,
                            OperationPhase.Validation,
                            default(OperationId),
                            "Required service " + dependency.Contract.ContractId.ToString() + " has no visible provider."));
                    }
                    else if (dependency.HasFallback)
                    {
                        // An optional binding rebinds to its declared fallback, which is local to the consumer.
                        bindings.Add(new ServiceBinding(
                            dependency.Contract,
                            new ProviderInstallationId(consumer.Instance.Value),
                            consumer.ActivationEpoch,
                            consumer.Instance.Value,
                            ServiceBindingKind.Single,
                            true));
                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticCode.MissingDependency,
                            OperationPhase.Validation,
                            default(OperationId),
                            "Optional service " + dependency.Contract.ContractId.ToString() + " uses its declared fallback."));
                    }
                    else
                    {
                        // Optional absence is explicit: it is a diagnostic, not a silent substitute (P-011).
                        diagnostics.Add(Diagnostic.Create(
                            DiagnosticCode.MissingDependency,
                            OperationPhase.Validation,
                            default(OperationId),
                            "Optional service " + dependency.Contract.ContractId.ToString() + " is absent."));
                    }
                }

                resolutions.Add(new DependencyResolution(dependency, bindings, code, visibleIds));
            }

            InstallationState state = consumer.DeclaredState;
            if (waiting)
            {
                state = InstallationState.WaitingForDependencies;
            }
            else if (state == InstallationState.WaitingForDependencies)
            {
                // Dependencies are satisfied again, so an automatically waiting installation resumes (P-012).
                state = InstallationState.Active;
            }
            else if (state == InstallationState.Registered || state == InstallationState.Preparing)
            {
                state = InstallationState.Active;
            }

            return new ServiceNodeResolution(consumer, state, resolutions, diagnostics);
        }

        private static ProviderCandidate? FindSelected(
            List<ServiceNode> all,
            List<ProviderCandidate> visible,
            ProviderInstallationId selected,
            out DiagnosticCode code)
        {
            code = DiagnosticCode.None;
            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i].Node.Instance.Value.Equals(selected.Value))
                {
                    return visible[i];
                }
            }

            // An explicit selection never falls back. Distinguish "no such installation" from "not visible here".
            bool exists = false;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Instance.Value.Equals(selected.Value))
                {
                    exists = true;
                    break;
                }
            }

            code = exists ? DiagnosticCode.ServiceConflict : DiagnosticCode.MissingDependency;
            return null;
        }

        /// <summary>
        /// The bindings one dependency receives. A multi-binding contract returns every visible provider in its
        /// registered stable order; a single-binding contract returns at most one. A contract whose visible
        /// declarations mix single and multi binding is `ServiceConflict`, because the contract cannot be both
        /// (P-011).
        /// </summary>
        private static List<ProviderCandidate> SelectBindings(List<ProviderCandidate> visible, out DiagnosticCode code)
        {
            code = DiagnosticCode.None;
            List<ProviderCandidate> multi = new List<ProviderCandidate>();
            List<ProviderCandidate> single = new List<ProviderCandidate>();
            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i].Export.BindingKind == ServiceBindingKind.Multi)
                {
                    multi.Add(visible[i]);
                }
                else
                {
                    single.Add(visible[i]);
                }
            }

            List<ProviderCandidate> chosen = new List<ProviderCandidate>();
            if (multi.Count != 0)
            {
                if (single.Count != 0)
                {
                    code = DiagnosticCode.ServiceConflict;
                    return chosen;
                }

                // Registered stable order: the candidate list is already sorted by canonical provider identity.
                chosen.AddRange(multi);
                return chosen;
            }

            ProviderCandidate? winner = SelectWinner(single, out code);
            if (winner.HasValue)
            {
                chosen.Add(winner.Value);
            }

            return chosen;
        }

        private static ProviderCandidate? SelectWinner(List<ProviderCandidate> visible, out DiagnosticCode code)
        {
            code = DiagnosticCode.None;

            // Single-binding candidates only; the multi-binding path never reaches this method (P-011).
            List<ProviderCandidate> sameScope = new List<ProviderCandidate>();

            int nearestDepth = int.MinValue;
            for (int i = 0; i < visible.Count; i++)
            {
                ProviderCandidate candidate = visible[i];
                if (candidate.Export.BindingKind == ServiceBindingKind.Multi)
                {
                    continue;
                }

                if (candidate.Depth > nearestDepth)
                {
                    nearestDepth = candidate.Depth;
                    sameScope.Clear();
                    sameScope.Add(candidate);
                }
                else if (candidate.Depth == nearestDepth)
                {
                    sameScope.Add(candidate);
                }
            }

            if (nearestDepth == int.MinValue)
            {
                code = DiagnosticCode.MissingDependency;
                return null;
            }

            if (sameScope.Count > 1)
            {
                code = DiagnosticCode.ServiceConflict;
                return null;
            }

            ProviderCandidate nearest = sameScope[0];
            bool outerExists = false;
            for (int i = 0; i < visible.Count; i++)
            {
                if (visible[i].Export.BindingKind == ServiceBindingKind.Multi)
                {
                    continue;
                }

                if (visible[i].Depth < nearest.Depth)
                {
                    outerExists = true;
                    break;
                }
            }

            if (outerExists && !nearest.Export.OverridesAncestor)
            {
                // The nearest provider only wins a contest with an ancestor when it declares the override.
                code = DiagnosticCode.ServiceConflict;
                return null;
            }

            return nearest;
        }

        /// <summary>
        /// Every visible provider of one dependency in canonical order. Providers are grouped by scope and the
        /// depth of each candidate is cached, so selection never re-walks the tree per comparison.
        /// </summary>
        private static List<ProviderCandidate> VisibleProviders(
            ScopeRegistry scopes,
            List<ServiceNode> all,
            ServiceNode consumer,
            ServiceDependency dependency,
            bool includeWaiting = false)
        {
            List<ProviderCandidate> visible = new List<ProviderCandidate>();
            IReadOnlyList<ScopeId> domain = Domain(scopes, consumer.Scope, dependency.Domain);

            for (int i = 0; i < domain.Count; i++)
            {
                ScopeId scope = domain[i];
                if (!scopes.TryGet(scope, out ScopeRecord? record) || record == null)
                {
                    continue;
                }

                int depth = record.Depth;
                for (int n = 0; n < all.Count; n++)
                {
                    ServiceNode provider = all[n];
                    if (provider.Instance.Equals(consumer.Instance) || !provider.Scope.Equals(scope) ||
                        (!provider.CanProvide && !(includeWaiting && provider.DeclaredState == InstallationState.WaitingForDependencies)))
                    {
                        continue;
                    }

                    for (int e = 0; e < provider.Exports.Count; e++)
                    {
                        ServiceExport export = provider.Exports[e];
                        if (!export.Contract.ContractId.Equals(dependency.Contract.ContractId))
                        {
                            continue;
                        }

                        // Visibility: private stays in its own scope; only an export reaches descendants (P-011).
                        bool sameScope = provider.Scope.Equals(consumer.Scope);
                        if (!sameScope && export.Visibility != ServiceVisibility.ExportToDescendants)
                        {
                            continue;
                        }

                        // A service-isolation boundary blocks ancestor providers for named contracts (P-016).
                        if (!sameScope && BlockedByIsolation(scopes, consumer.Scope, provider.Scope, dependency.Contract.ContractId))
                        {
                            continue;
                        }

                        if (!VersionAccepted(export.Contract.Version, dependency.SupportedVersions))
                        {
                            continue;
                        }

                        visible.Add(new ProviderCandidate(provider, export, depth));
                    }
                }
            }

            visible.Sort(CompareCandidates);
            return visible;
        }

        /// <summary>
        /// True when a service-isolation boundary strictly between the provider and the consumer blocks the
        /// contract. A provider installed at the boundary itself still works (P-016), and a descendant cannot
        /// reopen an ancestor boundary.
        /// </summary>
        private static bool BlockedByIsolation(ScopeRegistry scopes, ScopeId consumer, ScopeId provider, Id128 contract)
        {
            ScopeId current = consumer;
            while (scopes.TryGet(current, out ScopeRecord? record) && record != null && !record.Parent.IsDefault)
            {
                if (record.Scope.Equals(provider))
                {
                    return false;
                }

                if (Blocks(record.ServiceIsolation, contract))
                {
                    return true;
                }

                current = record.Parent;
            }

            return false;
        }

        private static bool Blocks(IsolationSet set, Id128 contract)
        {
            if (set == null || set.AllContracts)
            {
                return set != null && set.AllContracts;
            }

            for (int i = 0; i < set.Contracts.Count; i++)
            {
                if (set.Contracts[i].Equals(contract))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool VersionAccepted(uint version, VersionRange range)
        {
            if (range.MinVersion == 0U && range.MaxVersion == 0U)
            {
                // An unset range accepts the declared contract version itself; a schema always declares its range.
                return true;
            }

            return version >= range.MinVersion && version <= range.MaxVersion;
        }

        private static IReadOnlyList<ScopeId> Domain(ScopeRegistry scopes, ScopeId scope, ServiceResolutionDomain domain)
        {
            switch (domain)
            {
                case ServiceResolutionDomain.SelfOnly:
                    return scopes.TryGet(scope, out ScopeRecord? _) ? new[] { scope } : Array.Empty<ScopeId>();
                default:
                    // AncestorsAndSelf and WorldImported share the upward domain. A world service is one an
                    // ancestor installation exports; declaring the domain is what makes the import explicit
                    // (P-011). Sibling scopes are never in this domain.
                    return scopes.SelfAndAncestors(scope);
            }
        }

        /// <summary>
        /// Required-dependency topological order, providers before consumers, with canonical tie-breaking.
        /// A cycle among required dependencies rejects the whole proposal rather than publishing a partial
        /// closure (P-012).
        /// </summary>
        private static bool ClosureOrder(
            ScopeRegistry scopes,
            List<ServiceNode> nodes,
            out List<PluginInstanceId>? order,
            out DiagnosticCode code)
        {
            order = null;
            code = DiagnosticCode.None;

            Dictionary<Id128, ServiceNode> byInstance = new Dictionary<Id128, ServiceNode>();
            for (int i = 0; i < nodes.Count; i++)
            {
                byInstance[nodes[i].Instance.Value] = nodes[i];
            }

            // Edges provider -> consumer for every required dependency that has a visible provider.
            Dictionary<Id128, List<Id128>> edges = new Dictionary<Id128, List<Id128>>();
            Dictionary<Id128, int> incoming = new Dictionary<Id128, int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                edges[nodes[i].Instance.Value] = new List<Id128>();
                incoming[nodes[i].Instance.Value] = 0;
            }

            for (int i = 0; i < nodes.Count; i++)
            {
                ServiceNode consumer = nodes[i];
                for (int d = 0; d < consumer.Dependencies.Count; d++)
                {
                    ServiceDependency dependency = consumer.Dependencies[d];
                    if (!dependency.Required)
                    {
                        continue;
                    }

                    List<ProviderCandidate> visible = VisibleProviders(scopes, nodes, consumer, dependency, true);
                    if (visible.Count == 0)
                    {
                        // Absence is not a closure cycle: the consumer simply becomes Waiting (P-012).
                        continue;
                    }

                    // Every provider that will actually bind orders before this consumer, so a multi-binding
                    // contract contributes one edge per provider (P-011, P-012).
                    List<ProviderCandidate> bound;
                    if (dependency.HasSelectedProvider || !consumer.SelectedProviderFor(dependency.Contract).IsDefault)
                    {
                        ProviderInstallationId selected = consumer.SelectedProviderFor(dependency.Contract);
                        if (selected.IsDefault)
                        {
                            selected = dependency.SelectedProvider;
                        }

                        bound = new List<ProviderCandidate>();
                        ProviderCandidate? chosen = FindSelected(nodes, visible, selected, out DiagnosticCode selectionCode);
                        if (!chosen.HasValue)
                        {
                            if (selectionCode == DiagnosticCode.MissingDependency)
                            {
                                continue;
                            }

                            code = selectionCode;
                            return false;
                        }

                        bound.Add(chosen.Value);
                    }
                    else
                    {
                        bound = SelectBindings(visible, out DiagnosticCode winnerCode);
                        if (bound.Count == 0)
                        {
                            if (winnerCode == DiagnosticCode.MissingDependency)
                            {
                                continue;
                            }

                            // A conflict is a real closure failure: providers and consumers cannot be ordered.
                            code = winnerCode;
                            return false;
                        }
                    }

                    for (int b = 0; b < bound.Count; b++)
                    {
                        PluginInstanceId provider = bound[b].Node.Instance;
                        if (provider.Equals(consumer.Instance) || !byInstance.ContainsKey(provider.Value))
                        {
                            continue;
                        }

                        List<Id128> list = edges[provider.Value];
                        if (!list.Contains(consumer.Instance.Value))
                        {
                            list.Add(consumer.Instance.Value);
                            incoming[consumer.Instance.Value] = incoming[consumer.Instance.Value] + 1;
                        }
                    }
                }
            }

            foreach (KeyValuePair<Id128, List<Id128>> pair in edges)
            {
                pair.Value.Sort(CanonicalOrder.Compare);
            }

            List<PluginInstanceId> ready = new List<PluginInstanceId>();
            for (int i = 0; i < nodes.Count; i++)
            {
                if (incoming[nodes[i].Instance.Value] == 0)
                {
                    ready.Add(nodes[i].Instance);
                }
            }

            ready.Sort(CompareInstances);
            List<PluginInstanceId> result = new List<PluginInstanceId>(nodes.Count);
            while (ready.Count > 0)
            {
                PluginInstanceId next = ready[0];
                ready.RemoveAt(0);
                result.Add(next);

                List<Id128> outgoing = edges[next.Value];
                for (int i = 0; i < outgoing.Count; i++)
                {
                    int remaining = incoming[outgoing[i]] - 1;
                    incoming[outgoing[i]] = remaining;
                    if (remaining == 0)
                    {
                        ready.Add(new PluginInstanceId(outgoing[i]));
                    }
                }

                ready.Sort(CompareInstances);
            }

            if (result.Count != nodes.Count)
            {
                code = DiagnosticCode.Cycle;
                return false;
            }

            order = result;
            return true;
        }

        private static int CompareNodes(ServiceNode left, ServiceNode right) => left.Instance.Value.CompareTo(right.Instance.Value);

        private static int CompareInstances(PluginInstanceId left, PluginInstanceId right) => left.Value.CompareTo(right.Value);

        private static int CompareCandidates(ProviderCandidate left, ProviderCandidate right) => left.Node.Instance.Value.CompareTo(right.Node.Instance.Value);

        /// <summary>One visible provider of one dependency, with its scope depth cached for selection.</summary>
        private readonly struct ProviderCandidate
        {
            public readonly ServiceNode Node;
            public readonly ServiceExport Export;
            public readonly int Depth;

            public ProviderCandidate(ServiceNode node, ServiceExport export, int depth)
            {
                Node = node;
                Export = export;
                Depth = depth;
            }

            /// <summary>
            /// Process-local lease identity of a binding. It is derived from the provider instance and its
            /// activation epoch, so the same resolution yields the same lease id and a rebinding yields a new
            /// one (P-007). It is never serialized as plan identity (05 s4).
            /// </summary>
            public Id128 LeaseId() => CompositionSchemas.NameId(
                "lease:" + Node.Instance.Value.ToString() + "@" + Node.ActivationEpoch.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
