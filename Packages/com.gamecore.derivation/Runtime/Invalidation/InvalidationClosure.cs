// GameCore.Derivation — the invalidation closure of one edit (GC-013, P-023/P-025).
//
// The closure answers exactly one question: which targets must be re-derived? It is a *superset* of the affected
// set by construction — every seed below names targets that provably can change (02 s7's change table).
// Soundness is the whole point; a missing seed would silently carry a stale assembly, so each seed is derived
// from an index rather than from a heuristic:
//
//   * scope created / facts changed / moved  -> the targets inside that subtree. A capability boundary,
//     exclusion set or import grant at a scope addresses that scope and its descendants (P-016), and a move
//     changes the whole ancestor path of the subtree (P-025).
//   * target created / moved / descriptor changed -> that target (P-015, P-024).
//   * installation changed -> the candidate population of every rule it declares, in *both* snapshots (its old
//     reach to retract, its new reach to apply). The population is the snapshot's own indexed candidate set
//     (`TargetsInReach`), so a provider change dirties no more targets than the rule can select (P-023).
//   * installation inside a moved scope -> also changed, because rank uses the provider's depth (P-018).
//   * selection override changed -> the addressed scope subtree or target (P-018).
//   * ordering keys changed -> the population of the affected rule (P-019).
//   * service consumer of a changed provider -> changed as well, because a provider change can flip a consumer
//     into `WaitingForDependencies`, which retracts its contributions (P-012).
//
// A mode switch, a contract-table change or a different world incarnation reports `WholeWorld` instead, which is
// what P-014 and TEST-008 require the cost report to state explicitly.
//
// The closure is deliberately blind to rules: which rule has to be re-tested follows from the dirty *targets* and
// the install-path index, so the incremental engine never has to scan the rule set against the world.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>The result of one invalidation: the targets to re-derive and the scopes they live in (P-023).</summary>
    public sealed class InvalidationClosureResult
    {
        public InvalidationClosureResult(
            IReadOnlyList<TargetId>? dirtyTargets,
            IReadOnlyList<ScopeId>? dirtyScopes,
            bool wholeWorld,
            DerivationChangeSet changeSet,
            InvalidationCounters counters)
        {
            DirtyTargets = ContractCollections.Freeze(dirtyTargets);
            DirtyScopes = ContractCollections.Freeze(dirtyScopes);
            WholeWorld = wholeWorld;
            ChangeSet = changeSet;
            Counters = counters;
        }

        /// <summary>Targets whose effective assembly must be recomputed, in canonical target order.</summary>
        public IReadOnlyList<TargetId> DirtyTargets { get; }

        /// <summary>Scopes that hold a dirty target, in canonical scope order (TEST-008's locality evidence).</summary>
        public IReadOnlyList<ScopeId> DirtyScopes { get; }

        /// <summary>
        /// True when the change legitimately invalidates the world: a mode switch (P-014), a contract-table change
        /// or a different world incarnation. The caller re-derives everything and reports that cost (TEST-008).
        /// </summary>
        public bool WholeWorld { get; }

        public DerivationChangeSet ChangeSet { get; }

        public InvalidationCounters Counters { get; }

        /// <summary>True when nothing needs re-deriving: the caller carries the whole previous result.</summary>
        public bool IsEmpty => !WholeWorld && DirtyTargets.Count == 0;

        /// <summary>Canonical audit text of this closure (P-052, 02 s9).</summary>
        public string Describe() =>
            "invalidation{" + (WholeWorld ? "whole-world;" : string.Empty)
            + "dirtyTargets=" + DirtyTargets.Count.ToString(CultureInfo.InvariantCulture)
            + ";dirtyScopes=" + DirtyScopes.Count.ToString(CultureInfo.InvariantCulture)
            + ";" + Counters.Describe() + "}";

        public override string ToString() => Describe();
    }

    /// <summary>Computes the dependency closure of one change set over two indexed snapshots (P-023).</summary>
    public static class InvalidationClosure
    {
        /// <summary>
        /// Computes the invalidation of <paramref name="changeSet"/> between two snapshots. Passing the pre-built
        /// index sets is optional; without them the closure builds what it needs, which is what a single query
        /// wants and what a long sweep avoids.
        /// </summary>
        public static InvalidationClosureResult Compute(
            DerivationSnapshot previous,
            DerivationSnapshot next,
            DerivationChangeSet changeSet,
            DerivationIndexSet? previousIndexes = null,
            DerivationIndexSet? nextIndexes = null,
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

            IReadOnlyList<string> reasons = changeSet.Reasons();
            for (int i = 0; i < reasons.Count; i++)
            {
                counts.AddReason(reasons[i]);
            }

            if (changeSet.WorldChanged || changeSet.ModeChanged || changeSet.ContractsChanged)
            {
                // A mode switch (P-014), a catalog change or a new incarnation invalidates the world; the cost is
                // reported rather than hidden (TEST-008).
                List<TargetId> allTargets = new List<TargetId>(next.Targets.Count);
                for (int i = 0; i < next.Targets.Count; i++)
                {
                    allTargets.Add(next.Targets[i].Target);
                }

                List<ScopeId> allScopes = new List<ScopeId>(next.Scopes.Count);
                for (int i = 0; i < next.Scopes.Count; i++)
                {
                    allScopes.Add(next.Scopes[i].Scope);
                }

                counts.WholeWorld = true;
                counts.DirtyTargets = allTargets.Count;
                counts.DirtyScopes = allScopes.Count;
                return new InvalidationClosureResult(allTargets, allScopes, true, changeSet, counts);
            }

            // The indexes are built only for the local case: a whole-world invalidation never consults them, and
            // building two index sets to answer it would be work with no answer (P-023).
            DerivationIndexSet oldIndexes = previousIndexes ?? DerivationIndexSet.Build(previous);
            DerivationIndexSet newIndexes = nextIndexes ?? DerivationIndexSet.Build(next);

            HashSet<Id128> dirtyTargets = new HashSet<Id128>();
            HashSet<Id128> dirtyScopes = new HashSet<Id128>();
            HashSet<Id128> changedInstalls = new HashSet<Id128>();
            for (int i = 0; i < changeSet.ChangedInstalls.Count; i++)
            {
                changedInstalls.Add(changeSet.ChangedInstalls[i].Value);
            }

            // 1. Scope seeds: a created scope, an edited boundary/exclusion/import set, or a moved subtree.
            for (int i = 0; i < changeSet.CreatedScopes.Count; i++)
            {
                DirtySubtree(newIndexes, changeSet.CreatedScopes[i], dirtyTargets, dirtyScopes, counts);
            }

            for (int i = 0; i < changeSet.ChangedScopeFacts.Count; i++)
            {
                DirtySubtree(newIndexes, changeSet.ChangedScopeFacts[i], dirtyTargets, dirtyScopes, counts);
            }

            for (int i = 0; i < changeSet.ScopeMoves.Count; i++)
            {
                // A move changes the whole ancestor path of the moved subtree: its targets get new inherited
                // configuration, and providers inside it change depth, a P-018 rank component.
                DirtySubtree(newIndexes, changeSet.ScopeMoves[i].Scope, dirtyTargets, dirtyScopes, counts);
                IReadOnlyList<ScopeId> subtree = newIndexes.Membership.Subtree(changeSet.ScopeMoves[i].Scope);
                for (int s = 0; s < subtree.Count; s++)
                {
                    IReadOnlyList<DerivationInstall> atScope = newIndexes.InstallsAt(subtree[s]);
                    for (int a = 0; a < atScope.Count; a++)
                    {
                        changedInstalls.Add(atScope[a].Instance.Value);
                    }
                }
            }

            // 2. Target seeds: created, moved or descriptor-changed targets.
            for (int i = 0; i < changeSet.CreatedTargets.Count; i++)
            {
                DirtyTarget(next, changeSet.CreatedTargets[i], dirtyTargets, dirtyScopes);
            }

            for (int i = 0; i < changeSet.TargetMoves.Count; i++)
            {
                DirtyTarget(next, changeSet.TargetMoves[i].Target, dirtyTargets, dirtyScopes);
            }

            for (int i = 0; i < changeSet.DescriptorChangedTargets.Count; i++)
            {
                DirtyTarget(next, changeSet.DescriptorChangedTargets[i], dirtyTargets, dirtyScopes);
            }

            // 3. Ordering-key edits belong to a rule: dirty that rule's population in both snapshots.
            for (int i = 0; i < changeSet.ChangedRuleKeys.Count; i++)
            {
                RuleId rule = changeSet.ChangedRuleKeys[i];
                IReadOnlyList<IndexedRule> oldRules = oldIndexes.Providers.RulesWithId(rule);
                for (int r = 0; r < oldRules.Count; r++)
                {
                    DirtyRulePopulation(
                        previous, oldRules[r].Install.Scope, oldRules[r].Rule, dirtyTargets, dirtyScopes, counts, next);
                }

                IReadOnlyList<IndexedRule> newRules = newIndexes.Providers.RulesWithId(rule);
                for (int r = 0; r < newRules.Count; r++)
                {
                    DirtyRulePopulation(
                        next, newRules[r].Install.Scope, newRules[r].Rule, dirtyTargets, dirtyScopes, counts, next);
                }
            }

            // 4. Selection-override edits address a scope subtree or one target (P-018).
            if (changeSet.OverridesChanged)
            {
                for (int i = 0; i < changeSet.OverrideScopes.Count; i++)
                {
                    DirtySubtree(newIndexes, changeSet.OverrideScopes[i], dirtyTargets, dirtyScopes, counts);
                }

                for (int i = 0; i < changeSet.OverrideTargets.Count; i++)
                {
                    DirtyTarget(next, changeSet.OverrideTargets[i], dirtyTargets, dirtyScopes);
                }
            }

            // 5. A changed provider can flip a consumer into WaitingForDependencies, which retracts that
            //    consumer's contributions (P-012), so the consumer is as changed as the provider it lost.
            List<PluginInstanceId> pending = new List<PluginInstanceId>();
            foreach (Id128 value in changedInstalls)
            {
                pending.Add(new PluginInstanceId(value));
            }

            while (pending.Count > 0)
            {
                List<PluginInstanceId> discovered = new List<PluginInstanceId>();
                for (int i = 0; i < pending.Count; i++)
                {
                    PluginInstanceId instance = pending[i];
                    DiscoverConsumers(previous, oldIndexes, instance, changedInstalls, discovered, counts);
                    DiscoverConsumers(next, newIndexes, instance, changedInstalls, discovered, counts);
                }

                pending = discovered;
            }

            // 6. Every changed installation contributes its rules' populations in both snapshots: the old reach is
            //    what has to be retracted, the new reach is what has to be applied.
            foreach (Id128 value in changedInstalls)
            {
                PluginInstanceId instance = new PluginInstanceId(value);
                DirtyInstallPopulations(previous, instance, dirtyTargets, dirtyScopes, counts, next);
                DirtyInstallPopulations(next, instance, dirtyTargets, dirtyScopes, counts, next);
            }

            List<TargetId> dirty = new List<TargetId>(dirtyTargets.Count);
            foreach (Id128 value in dirtyTargets)
            {
                dirty.Add(new TargetId(value));
            }

            dirty.Sort((left, right) => left.Value.CompareTo(right.Value));
            List<ScopeId> scopes = new List<ScopeId>(dirtyScopes.Count);
            foreach (Id128 value in dirtyScopes)
            {
                scopes.Add(new ScopeId(value));
            }

            scopes.Sort(CanonicalDerivationOrder.CompareScopeIds);
            counts.DirtyTargets = dirty.Count;
            counts.DirtyScopes = scopes.Count;
            // `DirtyRules` is filled by the engine, which knows which rules have a dirty candidate; the closure
            // reports the populations it enumerated instead.
            return new InvalidationClosureResult(dirty, scopes, false, changeSet, counts);
        }

        /// <summary>
        /// The rule-skip test: whether a rule's reach domain contains any dirty scope. When it does not, the rule
        /// has no candidate inside the invalidated set and must never be enumerated (P-023, TEST-008).
        /// </summary>
        public static bool ReachesAny(
            DerivationIndexSet indexes,
            ScopeId providerScope,
            PropagationReach reach,
            IReadOnlyList<ScopeId> dirtyScopes)
        {
            if (indexes == null)
            {
                throw new ArgumentNullException(nameof(indexes));
            }

            for (int i = 0; i < dirtyScopes.Count; i++)
            {
                if (indexes.Membership.IsInReach(providerScope, reach, dirtyScopes[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static void DirtySubtree(
            DerivationIndexSet indexes,
            ScopeId scope,
            HashSet<Id128> dirtyTargets,
            HashSet<Id128> dirtyScopes,
            InvalidationCounters counts)
        {
            IReadOnlyList<ScopeId> subtree = indexes.Membership.Subtree(scope);
            for (int i = 0; i < subtree.Count; i++)
            {
                AddScope(indexes, subtree[i], dirtyScopes);
            }

            IReadOnlyList<DerivationTarget> targets = indexes.Membership.TargetsInSubtree(scope);
            counts.TargetsVisited += targets.Count;
            for (int i = 0; i < targets.Count; i++)
            {
                dirtyTargets.Add(targets[i].Target.Value);
                dirtyScopes.Add(targets[i].Scope.Value);
            }
        }

        private static void DirtyInstallPopulations(
            DerivationSnapshot snapshot,
            PluginInstanceId instance,
            HashSet<Id128> dirtyTargets,
            HashSet<Id128> dirtyScopes,
            InvalidationCounters counts,
            DerivationSnapshot reportSnapshot)
        {
            if (!snapshot.TryGetInstall(instance, out DerivationInstall? install) || install == null)
            {
                return;
            }

            IReadOnlyList<DerivationRule> rules = install.Manifest.DerivationRules;
            for (int r = 0; r < rules.Count; r++)
            {
                DirtyRulePopulation(
                    snapshot, install.Scope, rules[r], dirtyTargets, dirtyScopes, counts, reportSnapshot);
            }
        }

        private static void DirtyRulePopulation(
            DerivationSnapshot snapshot,
            ScopeId providerScope,
            DerivationRule rule,
            HashSet<Id128> dirtyTargets,
            HashSet<Id128> dirtyScopes,
            InvalidationCounters counts,
            DerivationSnapshot reportSnapshot)
        {
            IReadOnlyList<DerivationTarget> population = snapshot.TargetsInReach(
                providerScope, rule.Reach, rule.SelectorContracts, rule.OutputCapability.Capability);
            counts.IndexBucketsVisited++;
            counts.IndexTargetsVisited += population.Count;
            counts.TargetsVisited += population.Count;
            counts.RulesVisited++;
            for (int i = 0; i < population.Count; i++)
            {
                DirtyTarget(reportSnapshot, population[i].Target, dirtyTargets, dirtyScopes);
            }

            AddScope(snapshot, providerScope, dirtyScopes);
        }

        private static void DiscoverConsumers(
            DerivationSnapshot snapshot,
            DerivationIndexSet indexes,
            PluginInstanceId provider,
            HashSet<Id128> changedInstalls,
            List<PluginInstanceId> discovered,
            InvalidationCounters counts)
        {
            if (!snapshot.TryGetInstall(provider, out DerivationInstall? install) || install == null)
            {
                return;
            }

            IReadOnlyList<ServiceExport> exports = install.Manifest.ServiceExports;
            for (int e = 0; e < exports.Count; e++)
            {
                ServiceExport export = exports[e];
                IReadOnlyList<ServiceConsumerEdge> consumers = indexes.Consumers.ConsumersOfProvider(
                    provider, export.Contract, export.Visibility);
                for (int c = 0; c < consumers.Count; c++)
                {
                    counts.ServiceConsumersAffected++;
                    if (changedInstalls.Add(consumers[c].Consumer.Instance.Value))
                    {
                        discovered.Add(consumers[c].Consumer.Instance);
                    }
                }
            }
        }

        private static void DirtyTarget(
            DerivationSnapshot reportSnapshot,
            TargetId target,
            HashSet<Id128> dirtyTargets,
            HashSet<Id128> dirtyScopes)
        {
            if (!reportSnapshot.TryGetTarget(target, out DerivationTarget? found) || found == null)
            {
                // A target that no longer exists in the report snapshot has nothing left to derive; its retired
                // contributions are reported by the delta instead (P-024).
                return;
            }

            dirtyTargets.Add(target.Value);
            dirtyScopes.Add(found.Scope.Value);
        }

        private static void AddScope(DerivationIndexSet indexes, ScopeId scope, HashSet<Id128> dirtyScopes)
        {
            if (indexes.Membership.Contains(scope))
            {
                dirtyScopes.Add(scope.Value);
            }
        }

        private static void AddScope(DerivationSnapshot snapshot, ScopeId scope, HashSet<Id128> dirtyScopes)
        {
            if (snapshot.TryGetScope(scope, out _))
            {
                dirtyScopes.Add(scope.Value);
            }
        }
    }
}
