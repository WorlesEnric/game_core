// GameCore.Derivation — ancestry and membership index (GC-013, P-023).
//
// P-023 requires the implementation to index scope ancestry *and* membership so that an edit invalidates its
// dependency closure instead of rescanning the tree. This index answers the three questions the invalidation
// closure and the incremental engine ask:
//
//   * is a target scope inside a rule's reach domain from a provider scope? (reach containment, P-013)
//   * which scopes and which targets are in one scope's subtree? (the affected subtree of an edit, P-016/P-025)
//   * what is the ancestor chain and depth of one scope? (boundaries, imports, exclusions and rank, P-016/P-018)
//
// Every query is a dictionary lookup or a walk of one ancestor chain, so a local edit visits the affected
// subtree (plus the paths to it) and never an untouched sibling. `Counters` records exactly how much was visited
// so a test can prove that bound (TEST-008).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>The scope ancestry/membership index of one derivation snapshot (P-010, P-023).</summary>
    public sealed class ScopeMembershipIndex
    {
        private readonly DerivationSnapshot snapshot;
        private readonly Dictionary<Id128, ScopeId> parentOf;
        private readonly Dictionary<Id128, int> depthOf;
        private readonly Dictionary<Id128, List<ScopeId>> childrenOf;
        private readonly Dictionary<Id128, List<DerivationTarget>> targetsInScope;
        private readonly Dictionary<Id128, IReadOnlyList<ScopeId>> subtrees =
            new Dictionary<Id128, IReadOnlyList<ScopeId>>();

        private ScopeMembershipIndex(DerivationSnapshot snapshot, InvalidationCounters counters)
        {
            this.snapshot = snapshot;
            Counters = counters;
            parentOf = new Dictionary<Id128, ScopeId>();
            depthOf = new Dictionary<Id128, int>();
            childrenOf = new Dictionary<Id128, List<ScopeId>>();
            targetsInScope = new Dictionary<Id128, List<DerivationTarget>>();

            IReadOnlyList<DerivationScope> scopes = snapshot.Scopes;
            for (int i = 0; i < scopes.Count; i++)
            {
                DerivationScope scope = scopes[i];
                parentOf[scope.Scope.Value] = scope.Parent;
                depthOf[scope.Scope.Value] = snapshot.Depth(scope.Scope);
                if (!scope.Parent.IsDefault)
                {
                    AddChild(scope.Parent, scope.Scope);
                }
            }

            IReadOnlyList<DerivationTarget> targets = snapshot.Targets;
            for (int i = 0; i < targets.Count; i++)
            {
                DerivationTarget target = targets[i];
                if (!targetsInScope.TryGetValue(target.Scope.Value, out List<DerivationTarget>? list))
                {
                    list = new List<DerivationTarget>();
                    targetsInScope.Add(target.Scope.Value, list);
                }

                // `snapshot.Targets` is in canonical order, so every per-scope list is canonical too: the index
                // never keeps a second, differently ordered copy of membership.
                list.Add(target);
            }
        }

        private ScopeMembershipIndex(
            DerivationSnapshot snapshot,
            InvalidationCounters counters,
            Dictionary<Id128, ScopeId> parentOf,
            Dictionary<Id128, int> depthOf,
            Dictionary<Id128, List<ScopeId>> childrenOf,
            Dictionary<Id128, List<DerivationTarget>> targetsInScope)
        {
            this.snapshot = snapshot;
            Counters = counters;
            this.parentOf = parentOf;
            this.depthOf = depthOf;
            this.childrenOf = childrenOf;
            this.targetsInScope = targetsInScope;
        }


        /// <summary>Builds the membership index of one snapshot. Building it is a property of the snapshot, not an edit.</summary>
        public static ScopeMembershipIndex Build(DerivationSnapshot snapshot, InvalidationCounters? counters = null)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new ScopeMembershipIndex(snapshot, counters ?? new InvalidationCounters());
        }

        /// <summary>
        /// Reuses immutable membership tables when the change set proves scope ancestry and target ownership are
        /// unchanged, while rebinding query counters and the snapshot identity to the current derivation.
        /// </summary>
        internal static ScopeMembershipIndex ReuseUnchangedDomain(
            ScopeMembershipIndex previous,
            DerivationSnapshot snapshot,
            InvalidationCounters? counters = null)
        {
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }

            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            return new ScopeMembershipIndex(
                snapshot,
                counters ?? new InvalidationCounters(),
                previous.parentOf,
                previous.depthOf,
                previous.childrenOf,
                previous.targetsInScope);
        }

        /// <summary>Work counters of every query answered by this index (P-023 evidence).</summary>
        public InvalidationCounters Counters { get; }

        /// <summary>Depth of a scope, or -1 when this snapshot does not contain it.</summary>
        public int DepthOf(ScopeId scope) => depthOf.TryGetValue(scope.Value, out int depth) ? depth : -1;

        /// <summary>Default means the world root or an unknown scope (P-010).</summary>
        public ScopeId ParentOf(ScopeId scope) => parentOf.TryGetValue(scope.Value, out ScopeId parent) ? parent : default(ScopeId);

        /// <summary>True when this snapshot contains the scope.</summary>
        public bool Contains(ScopeId scope) => depthOf.ContainsKey(scope.Value);

        /// <summary>Self plus every descendant in canonical order; empty for an unknown scope.</summary>
        public IReadOnlyList<ScopeId> Subtree(ScopeId scope)
        {
            if (subtrees.TryGetValue(scope.Value, out IReadOnlyList<ScopeId>? cached))
            {
                return cached;
            }

            if (!depthOf.ContainsKey(scope.Value))
            {
                return Array.Empty<ScopeId>();
            }

            List<ScopeId> collected = new List<ScopeId>();
            Collect(scope, collected);
            collected.Sort(CanonicalDerivationOrder.CompareScopeIds);
            IReadOnlyList<ScopeId> result = collected.AsReadOnly();
            subtrees[scope.Value] = result;
            return result;
        }

        /// <summary>Ancestor chain of one scope, nearest first, excluding the scope itself (P-010).</summary>
        public IReadOnlyList<ScopeId> AncestorsOf(ScopeId scope)
        {
            List<ScopeId> chain = new List<ScopeId>();
            ScopeId current = ParentOf(scope);
            while (!current.IsDefault && depthOf.ContainsKey(current.Value))
            {
                chain.Add(current);
                current = ParentOf(current);
            }

            Counters.AncestorChainSteps += chain.Count + 1;
            return chain.AsReadOnly();
        }

        /// <summary>
        /// Reach containment of P-013 computed from the membership index: true when a target scope is selected by
        /// a rule with this reach installed at <paramref name="providerScope"/>.
        /// </summary>
        public bool IsInReach(ScopeId providerScope, PropagationReach reach, ScopeId targetScope)
        {
            Counters.ReachTests++;
            if (!depthOf.ContainsKey(targetScope.Value) || !depthOf.ContainsKey(providerScope.Value))
            {
                return false;
            }

            switch (reach)
            {
                case PropagationReach.LocalOnly:
                    return targetScope.Equals(providerScope);
                case PropagationReach.DescendantsOnly:
                    return !targetScope.Equals(providerScope) && IsSelfOrAncestorOf(providerScope, targetScope);
                default:
                    return IsSelfOrAncestorOf(providerScope, targetScope);
            }
        }

        /// <summary>True when <paramref name="scope"/> is <paramref name="candidate"/> or one of its ancestors.</summary>
        public bool IsSelfOrAncestorOf(ScopeId scope, ScopeId candidate)
        {
            ScopeId current = candidate;
            int steps = 0;
            while (!current.IsDefault && depthOf.ContainsKey(current.Value))
            {
                steps++;
                if (current.Equals(scope))
                {
                    Counters.AncestorChainSteps += steps;
                    return true;
                }

                current = ParentOf(current);
            }

            Counters.AncestorChainSteps += steps + 1;
            return false;
        }

        /// <summary>Targets owned directly by a scope, canonical order.</summary>
        public IReadOnlyList<DerivationTarget> TargetsInScope(ScopeId scope) =>
            targetsInScope.TryGetValue(scope.Value, out List<DerivationTarget>? list)
                ? list
                : (IReadOnlyList<DerivationTarget>)Array.Empty<DerivationTarget>();

        /// <summary>
        /// Every target inside a scope's subtree, canonical target order. The walk counts the scopes and targets it
        /// touched, which is the counter a locality test asserts on (TEST-008).
        /// </summary>
        public IReadOnlyList<DerivationTarget> TargetsInSubtree(ScopeId scope)
        {
            IReadOnlyList<ScopeId> subtree = Subtree(scope);
            if (subtree.Count == 0)
            {
                return Array.Empty<DerivationTarget>();
            }

            List<DerivationTarget> targets = new List<DerivationTarget>();
            for (int i = 0; i < subtree.Count; i++)
            {
                Counters.ScopesVisited++;
                IReadOnlyList<DerivationTarget> inScope = TargetsInScope(subtree[i]);
                for (int t = 0; t < inScope.Count; t++)
                {
                    targets.Add(inScope[t]);
                }
            }

            Counters.TargetsVisited += targets.Count;
            targets.Sort(CanonicalDerivationOrder.CompareTargets);
            return targets.AsReadOnly();
        }

        /// <summary>How many targets live inside a scope's subtree, counted through the membership index.</summary>
        public int SubtreeTargetCount(ScopeId scope)
        {
            IReadOnlyList<ScopeId> subtree = Subtree(scope);
            int count = 0;
            for (int i = 0; i < subtree.Count; i++)
            {
                count += TargetsInScope(subtree[i]).Count;
            }

            return count;
        }

        /// <summary>Depth of the deepest scope in the snapshot; the world's shape, used by cost reports.</summary>
        public int MaxDepth
        {
            get
            {
                int max = 0;
                foreach (KeyValuePair<Id128, int> pair in depthOf)
                {
                    if (pair.Value > max)
                    {
                        max = pair.Value;
                    }
                }

                return max;
            }
        }

        internal DerivationSnapshot Snapshot => snapshot;

        private void AddChild(ScopeId parent, ScopeId child)
        {
            if (!childrenOf.TryGetValue(parent.Value, out List<ScopeId>? list))
            {
                list = new List<ScopeId>();
                childrenOf.Add(parent.Value, list);
            }

            list.Add(child);
        }

        private void Collect(ScopeId scope, List<ScopeId> collected)
        {
            Counters.ScopesVisited++;
            collected.Add(scope);
            if (!childrenOf.TryGetValue(scope.Value, out List<ScopeId>? children))
            {
                return;
            }

            for (int i = 0; i < children.Count; i++)
            {
                Collect(children[i], collected);
            }
        }
    }
}
