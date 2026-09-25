// GameCore.Derivation — the old/new provider closure diff of one moved subtree (GC-013, P-025).
//
// P-025: "A subtree move diffs the union of old and new ancestor rule/service sets, plus descendant overrides and
// dependency closure." The invalidation closure already decides *which* targets a move dirties; this type answers
// the narrower, reviewable question a move raises: for the moved scope, which provider rules stop reaching it,
// which start, and which reach it either way.
//
// The two sides are computed from different trees on purpose: the old closure is taken from the previous snapshot
// (the moved scope's old ancestor chain) and the new closure from the next snapshot (its new chain). That is what
// makes the diff meaningful — a single walk of the new tree could only report one side. Because a move may change
// the depth of the provider scopes inside the moved subtree, and depth is a P-018 rank component, the diff also
// reports the rules whose rank inputs changed.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Which provider rules leave, join or remain in a moved scope's closure, and which changed rank (P-025).</summary>
    public sealed class ProviderClosureDiff
    {
        private ProviderClosureDiff(
            ScopeId scope,
            IReadOnlyList<IndexedRule>? removed,
            IReadOnlyList<IndexedRule>? added,
            IReadOnlyList<IndexedRule>? retained,
            IReadOnlyList<IndexedRule>? rankChanged,
            bool empty)
        {
            Scope = scope;
            Removed = ContractCollections.Freeze(removed);
            Added = ContractCollections.Freeze(added);
            Retained = ContractCollections.Freeze(retained);
            RankChanged = ContractCollections.Freeze(rankChanged);
            IsEmpty = empty;
        }

        /// <summary>The moved scope whose closure was diffed.</summary>
        public ScopeId Scope { get; }

        /// <summary>Rules that reached the scope before the move and do not reach it afterwards.</summary>
        public IReadOnlyList<IndexedRule> Removed { get; }

        /// <summary>Rules that reach the scope only after the move.</summary>
        public IReadOnlyList<IndexedRule> Added { get; }

        /// <summary>Rules that reach the scope on both sides with an unchanged provider depth (P-018).</summary>
        public IReadOnlyList<IndexedRule> Retained { get; }

        /// <summary>Rules that reach the scope on both sides but whose provider depth changed, which reorders rank.</summary>
        public IReadOnlyList<IndexedRule> RankChanged { get; }

        /// <summary>True when the two closures agree on every rule and every depth.</summary>
        public bool IsEmpty { get; }

        /// <summary>Canonical audit text of this diff (P-052).</summary>
        public string Describe() =>
            "closureDiff{scope=" + Scope.ToString()
            + ";removed=" + Removed.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";added=" + Added.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";retained=" + Retained.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";rankChanged=" + RankChanged.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "}";

        public override string ToString() => Describe();

        /// <summary>
        /// Diffs the provider closure of one scope across two indexed snapshots. The snapshots are the *trees*
        /// each side is read from, so a caller passes the same scope identity twice.
        /// </summary>
        public static ProviderClosureDiff Compute(
            DerivationSnapshot previous,
            DerivationIndexSet previousIndexes,
            DerivationSnapshot next,
            DerivationIndexSet nextIndexes,
            ScopeId scope)
        {
            if (previous == null)
            {
                throw new ArgumentNullException(nameof(previous));
            }

            if (previousIndexes == null)
            {
                throw new ArgumentNullException(nameof(previousIndexes));
            }

            if (next == null)
            {
                throw new ArgumentNullException(nameof(next));
            }

            if (nextIndexes == null)
            {
                throw new ArgumentNullException(nameof(nextIndexes));
            }

            Dictionary<Id128, List<IndexedRule>> oldClosure = previousIndexes.ProviderClosure(scope);
            Dictionary<Id128, List<IndexedRule>> newClosure = nextIndexes.ProviderClosure(scope);

            List<IndexedRule> removed = new List<IndexedRule>();
            List<IndexedRule> retained = new List<IndexedRule>();
            List<IndexedRule> rankChanged = new List<IndexedRule>();
            List<IndexedRule> added = new List<IndexedRule>();

            foreach (KeyValuePair<Id128, List<IndexedRule>> pair in oldClosure)
            {
                List<IndexedRule>? now;
                if (!newClosure.TryGetValue(pair.Key, out now) || now == null)
                {
                    removed.AddRange(pair.Value);
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    IndexedRule rule = pair.Value[i];
                    int oldDepth = previous.Depth(rule.Install.Scope);
                    int newDepth = next.Depth(rule.Install.Scope);
                    if (newDepth != oldDepth)
                    {
                        rankChanged.Add(rule);
                    }
                    else if (Contains(now, rule))
                    {
                        retained.Add(rule);
                    }
                    else
                    {
                        // The same capability is still supplied, but by a different set of rules: the exact keys
                        // are what retraction works on (P-017).
                        removed.Add(rule);
                    }
                }
            }

            foreach (KeyValuePair<Id128, List<IndexedRule>> pair in newClosure)
            {
                if (!oldClosure.TryGetValue(pair.Key, out List<IndexedRule>? before) || before == null)
                {
                    added.AddRange(pair.Value);
                    continue;
                }

                for (int i = 0; i < pair.Value.Count; i++)
                {
                    IndexedRule rule = pair.Value[i];
                    int oldDepth = previous.Depth(rule.Install.Scope);
                    int newDepth = next.Depth(rule.Install.Scope);
                    if (newDepth != oldDepth)
                    {
                        continue;
                    }

                    if (!Contains(before, rule))
                    {
                        added.Add(rule);
                    }
                }
            }

            removed.Sort(DerivationIndexSet.CompareRules);
            added.Sort(DerivationIndexSet.CompareRules);
            retained.Sort(DerivationIndexSet.CompareRules);
            rankChanged.Sort(DerivationIndexSet.CompareRules);
            bool empty = removed.Count == 0 && added.Count == 0 && rankChanged.Count == 0;
            return new ProviderClosureDiff(scope, removed, added, retained, rankChanged, empty);
        }

        private static bool Contains(List<IndexedRule> rules, IndexedRule candidate)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Install.Instance.Equals(candidate.Install.Instance)
                    && rules[i].Rule.RuleId.Equals(candidate.Rule.RuleId))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
