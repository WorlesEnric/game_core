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

        private DerivationIndexSet(DerivationSnapshot snapshot)
        {
            this.snapshot = snapshot;
            Membership = ScopeMembershipIndex.Build(snapshot, Counters);
            Descriptors = DescriptorTargetIndex.Build(snapshot);
            Providers = ProviderContributionIndex.Build(snapshot, Membership);
            Consumers = ServiceConsumerIndex.Build(snapshot, Membership);
            installsByScope = new Dictionary<Id128, List<DerivationInstall>>();

            IReadOnlyList<DerivationInstall> installs = snapshot.Installs;
            for (int i = 0; i < installs.Count; i++)
            {
                DerivationInstall install = installs[i];
                if (!installsByScope.TryGetValue(install.Scope.Value, out List<DerivationInstall>? list))
                {
                    list = new List<DerivationInstall>();
                    installsByScope.Add(install.Scope.Value, list);
                }

                list.Add(install);
            }

            InstallPathCache = new Dictionary<Id128, IReadOnlyList<DerivationInstall>>();
        }

        /// <summary>Builds every index of one snapshot.</summary>
        public static DerivationIndexSet Build(DerivationSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new DerivationIndexSet(snapshot);
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

        /// <summary>Work counters of every index query answered through this set.</summary>
        public InvalidationCounters Counters { get; } = new InvalidationCounters();

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
            if (InstallPathCache.TryGetValue(scope.Value, out IReadOnlyList<DerivationInstall>? cached))
            {
                return cached;
            }

            List<DerivationInstall> found = new List<DerivationInstall>();
            IReadOnlyList<ScopeId> chain = snapshot.SelfAndAncestors(scope);
            for (int s = 0; s < chain.Count; s++)
            {
                Counters.ScopesVisited++;
                IReadOnlyList<DerivationInstall> atScope = InstallsAt(chain[s]);
                for (int i = 0; i < atScope.Count; i++)
                {
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

                IReadOnlyList<IndexedRule> ofInstall = Providers.RulesOf(install.Instance);
                for (int r = 0; r < ofInstall.Count; r++)
                {
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
            for (int i = 0; i < candidates.Count; i++)
            {
                DerivationTarget target = candidates[i];
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

        private IReadOnlyDictionary<Id128, IReadOnlyList<DerivationInstall>> InstallPathCache { get; }

        internal static int CompareRules(IndexedRule left, IndexedRule right)
        {
            int install = left.Install.Instance.Value.CompareTo(right.Install.Instance.Value);
            return install != 0 ? install : left.Rule.RuleId.Value.CompareTo(right.Rule.RuleId.Value);
        }
    }
}
