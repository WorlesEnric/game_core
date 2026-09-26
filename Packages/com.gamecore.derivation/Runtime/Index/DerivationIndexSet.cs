// GameCore.Derivation — one build of every P-023 index over one snapshot (GC-013).
//
// P-023 lists six indexes; five of them are here (state-slot support belongs to the ownership/state model of
// GC-007/GC-008, not to derivation):
//
//   ancestry/membership      -> ScopeMembershipIndex
//   descriptor-to-target     -> DescriptorTargetIndex
//   provider/rule-to-source  -> ProviderContributionIndex   (+ the install-path index below)
//   capability reverse deps  -> ProviderContributionIndex.RulesOutputting / CapabilitiesReaching
//   service consumers        -> ServiceConsumerIndex
//
// plus the install-path index: every installation whose scope is on a target's ancestor-or-self chain. That is the
// complete set of providers that can ever reach that target, because every reach selector of P-013
// (`SelfAndDescendants`, `DescendantsOnly`, `LocalOnly`) selects targets inside the provider's own subtree. The
// path index is what makes the inheritance fingerprint local: a change to an installation off the path cannot
// change what derives on the target (P-025).
//
// Indexes are derived data and rebuildable from the committed composition; nothing here is authoritative state.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Every P-023 index of one derivation snapshot, built once (GC-013).</summary>
    public sealed class DerivationIndexSet
    {
        private readonly DerivationSnapshot snapshot;
        private readonly Dictionary<Id128, List<DerivationInstall>> installsByScope;

        private DerivationIndexSet(DerivationSnapshot snapshot, InvalidationCounters? counters)
        {
            this.snapshot = snapshot;
            // The reported counter object may be the caller's, so index work lands in the counters the caller
            // reports instead of in a set nobody reads (GC-023). Without a caller-supplied set the indexes own it.
            Counters = counters ?? new InvalidationCounters();
            Membership = ScopeMembershipIndex.Build(snapshot, Counters);
            Descriptors = DescriptorTargetIndex.Build(snapshot);
            Providers = ProviderContributionIndex.Build(snapshot, Membership);
            Consumers = ServiceConsumerIndex.Build(snapshot, Membership);
            installsByScope = BuildInstallsByScope(snapshot);


            InstallPathCache = new Dictionary<Id128, IReadOnlyList<DerivationInstall>>();
        }

        private DerivationIndexSet(
            DerivationSnapshot snapshot,
            InvalidationCounters counters,
            ScopeMembershipIndex membership,
            DescriptorTargetIndex descriptors,
            ProviderContributionIndex providers,
            ServiceConsumerIndex consumers,
            Dictionary<Id128, List<DerivationInstall>> installsByScope)
        {
            this.snapshot = snapshot;
            Counters = counters;
            Membership = membership;
            Descriptors = descriptors;
            Providers = providers;
            Consumers = consumers;
            this.installsByScope = installsByScope;
            InstallPathCache = new Dictionary<Id128, IReadOnlyList<DerivationInstall>>();
        }


        /// <summary>
        /// Builds every index of one snapshot. Passing <paramref name="counters"/> makes the indexes report their
        /// work through that object, which is how a caller's `ControlNodesVisited` covers the control-plane nodes
        /// the indexes walked (GC-023); without it the index set owns a fresh counter object.
        /// </summary>
        public static DerivationIndexSet Build(DerivationSnapshot snapshot, InvalidationCounters? counters = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new DerivationIndexSet(snapshot, counters);
        }

        /// <summary>
        /// Builds the next snapshot's indexes from an already-built previous set when the change set proves the
        /// snapshot-derived membership and descriptor domains are unchanged. Install-only edits reuse those domains
        /// with the caller's current counters and rebuild the install-derived indexes from <paramref name="next"/>;
        /// structural, descriptor, mode, catalog, override or rule-key edits conservatively fall back to a full build.
        /// </summary>
        public static DerivationIndexSet BuildIncremental(
            DerivationIndexSet previous,
            DerivationSnapshot next,
            DerivationChangeSet changeSet,
            InvalidationCounters? counters = null)
        {
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }

            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (changeSet == null)
            {
                throw new ArgumentNullException(nameof(changeSet));
            }

            InvalidationCounters counts = counters ?? new InvalidationCounters();
            if (!CanReuseInstallOnlyDomains(previous, next, changeSet))
            {
                return Build(next, counts);
            }

            ScopeMembershipIndex membership = ScopeMembershipIndex.ReuseUnchangedDomain(
                previous.Membership, next, counts);
            DescriptorTargetIndex descriptors = DescriptorTargetIndex.ReuseUnchangedTargets(previous.Descriptors, next);
            return new DerivationIndexSet(
                next,
                counts,
                membership,
                descriptors,
                ProviderContributionIndex.Build(next, membership),
                ServiceConsumerIndex.Build(next, membership),
                BuildInstallsByScope(next));
        }


        /// <summary>The snapshot these indexes describe.</summary>
        public DerivationSnapshot Snapshot => snapshot;

        /// <summary>Ancestry and membership (P-010, P-023).</summary>
        public ScopeMembershipIndex Membership { get; }

        /// <summary>Descriptor-to-target buckets and structural descriptor comparison (P-015, P-023).</summary>
        public DescriptorTargetIndex Descriptors { get; }

        /// <summary>Provider/rule-to-source and capability reverse dependencies (P-017, P-023).</summary>
        public ProviderContributionIndex Providers { get; }

        /// <summary>Service-consumer adjacency (P-011, P-012, P-023).</summary>
        public ServiceConsumerIndex Consumers { get; }

        /// <summary>Work counters of every index query answered through this set (the caller's object when given).</summary>
        public InvalidationCounters Counters { get; }

        /// <summary>Installs registered directly at one scope, in canonical identity order (any lifecycle state).</summary>
        public IReadOnlyList<DerivationInstall> InstallsAt(ScopeId scope) =>
            installsByScope.TryGetValue(scope.Value, out List<DerivationInstall>? list)
                ? list
                : (IReadOnlyList<DerivationInstall>)Array.Empty<DerivationInstall>();

        /// <summary>
        /// Every installation registered at the target scope or at one of its ancestors: the complete provider set
        /// that can reach the target under any P-013 reach selector. Nearest scope first, canonical within a scope.
        /// </summary>
        public IReadOnlyList<DerivationInstall> InstallsOnPath(ScopeId scope)
        {
            // The query is one control node, counted *before* the cache lookup, because TEST-023 requires the
            // counters to count control work even when a traversal is cached or returns no matches (GC-023).
            TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
            if (InstallPathCache.TryGetValue(scope.Value, out IReadOnlyList<DerivationInstall>? cached))
            {
                return cached;
            }

            List<DerivationInstall> found = new List<DerivationInstall>();
            IReadOnlyList<ScopeId> chain = snapshot.SelfAndAncestors(scope);
            for (int s = 0; s < chain.Count; s++)
            {
                Counters.ScopesVisited++;
                TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
                IReadOnlyList<DerivationInstall> atScope = InstallsAt(chain[s]);
                for (int i = 0; i < atScope.Count; i++)
                {
                    TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
                    found.Add(atScope[i]);
                }
            }

            IReadOnlyList<DerivationInstall> result = found.AsReadOnly();
            InstallPathCache[scope.Value] = result;
            return result;
        }

        /// <summary>
        /// The rules that can reach one target, in canonical (installation, rule) order: every rule of every active
        /// installation on the target's path. This is the candidate rule set of that target, and the payload a
        /// derived-recipe variant caches (P-024).
        /// </summary>
        public IReadOnlyList<IndexedRule> RulesReaching(TargetId target, out DerivationTarget? foundTarget)
        {
            if (!snapshot.TryGetTarget(target, out DerivationTarget? target2) || target2 == null)
            {
                foundTarget = null;
                return Array.Empty<IndexedRule>();
            }

            foundTarget = target2;
            IReadOnlyList<DerivationInstall> path = InstallsOnPath(target2.Scope);
            List<IndexedRule> rules = new List<IndexedRule>();
            for (int i = 0; i < path.Count; i++)
            {
                DerivationInstall install = path[i];
                if (!install.IsActive)
                {
                    continue;
                }

                // Each install on the path, and each rule declared by an active install, is a control node.
                TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
                IReadOnlyList<IndexedRule> ofInstall = Providers.RulesOf(install.Instance);
                for (int r = 0; r < ofInstall.Count; r++)
                {
                    TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
                    rules.Add(ofInstall[r]);
                }
            }

            rules.Sort(CompareRules);
            return rules.AsReadOnly();
        }

        /// <summary>
        /// The candidate population of one rule restricted to a target set, computed by testing each target rather
        /// than enumerating the rule's whole reach domain (P-023: examine indexed candidates and changed paths).
        /// The test is exactly the snapshot's own population predicate, so the result is a subset of
        /// <see cref="DerivationSnapshot.TargetsInReach"/> for the same rule.
        /// </summary>
        public List<DerivationTarget> CandidateTargets(
            ScopeId providerScope,
            PropagationReach reach,
            IReadOnlyList<SchemaRef> selectors,
            CapabilityId capability,
            IReadOnlyList<DerivationTarget> candidates)
        {
            List<DerivationTarget> population = new List<DerivationTarget>();
            // Each candidate examined is a control node; counted whether or not it ends up in the population.
            for (int i = 0; i < candidates.Count; i++)
            {
                DerivationTarget target = candidates[i];
                TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
                if (!Membership.IsInReach(providerScope, reach, target.Scope))
                {
                    continue;
                }

                if (selectors.Count == 0)
                {
                    population.Add(target);
                    continue;
                }

                bool advertised = false;
                for (int s = 0; s < selectors.Count && !advertised; s++)
                {
                    advertised = target.DeclaresSchemaId(selectors[s].Id);
                }

                if (advertised || target.DeclaresCapabilityId(capability))
                {
                    population.Add(target);
                }
            }

            population.Sort(CanonicalDerivationOrder.CompareTargets);
            return population;
        }

        /// <summary>
        /// The provider closure of one scope: every active rule that can reach it, grouped by capability, in
        /// canonical order. P-025 diffs the union of the old and new closure when a subtree moves.
        /// </summary>
        public Dictionary<Id128, List<IndexedRule>> ProviderClosure(ScopeId scope)
        {
            IReadOnlyList<DerivationInstall> path = InstallsOnPath(scope);
            Dictionary<Id128, List<IndexedRule>> closure = new Dictionary<Id128, List<IndexedRule>>();
            for (int i = 0; i < path.Count; i++)
            {
                DerivationInstall install = path[i];
                if (!install.IsActive)
                {
                    continue;
                }

                IReadOnlyList<IndexedRule> rules = Providers.RulesOf(install.Instance);
                for (int r = 0; r < rules.Count; r++)
                {
                    TelemetryCounting.Count(Counters.Telemetry, TelemetryCounter.ControlNodesVisited);
                    IndexedRule rule = rules[r];
                    if (!Membership.IsInReach(rule.Install.Scope, rule.Rule.Reach, scope))
                    {
                        continue;
                    }

                    if (!closure.TryGetValue(rule.Output.Value, out List<IndexedRule>? list))
                    {
                        list = new List<IndexedRule>();
                        closure.Add(rule.Output.Value, list);
                    }

                    list.Add(rule);
                }
            }

            return closure;
        }

        private Dictionary<Id128, IReadOnlyList<DerivationInstall>> InstallPathCache { get; }

        private static Dictionary<Id128, List<DerivationInstall>> BuildInstallsByScope(DerivationSnapshot snapshot)
        {
            Dictionary<Id128, List<DerivationInstall>> index = new Dictionary<Id128, List<DerivationInstall>>();
            IReadOnlyList<DerivationInstall> installs = snapshot.Installs;
            for (int i = 0; i < installs.Count; i++)
            {
                DerivationInstall install = installs[i];
                if (!index.TryGetValue(install.Scope.Value, out List<DerivationInstall>? list))
                {
                    list = new List<DerivationInstall>();
                    index.Add(install.Scope.Value, list);
                }

                list.Add(install);
            }

            return index;
        }

        private static bool CanReuseInstallOnlyDomains(
            DerivationIndexSet previous,
            DerivationSnapshot next,
            DerivationChangeSet changeSet)
        {
            return !changeSet.WorldChanged
                && !changeSet.ModeChanged
                && !changeSet.ContractsChanged
                && !changeSet.OverridesChanged
                && previous.Snapshot.Scopes.Count == next.Scopes.Count
                && previous.Snapshot.Targets.Count == next.Targets.Count
                && changeSet.CreatedScopes.Count == 0
                && changeSet.RemovedScopes.Count == 0
                && changeSet.ChangedScopeFacts.Count == 0
                && changeSet.ScopeMoves.Count == 0
                && changeSet.CreatedTargets.Count == 0
                && changeSet.RetiredTargets.Count == 0
                && changeSet.TargetMoves.Count == 0
                && changeSet.DescriptorChangedTargets.Count == 0
                && changeSet.ChangedRuleKeys.Count == 0;
        }

        internal static int CompareRules(IndexedRule left, IndexedRule right)
        {
            int install = left.Install.Instance.Value.CompareTo(right.Install.Instance.Value);
            return install != 0 ? install : left.Rule.RuleId.Value.CompareTo(right.Rule.RuleId.Value);
        }
    }
}
