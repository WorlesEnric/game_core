// GameCore.Derivation — the invalidation change set between two snapshots (GC-013, P-023).
//
// P-023 says "an edit invalidates its dependency closure"; 02 s7 lists the seeds per change kind. This type is the
// declared diff between the previously derived snapshot and the next one: it names *what* changed, never *which
// targets are affected* — the affected set is the closure's job (`InvalidationClosure`). Keeping the two apart is
// what makes the closure testable and the change set auditable: a caller that already knows what an edit did (the
// composition lane produces a `CompositionChangeSet`) can hand the facts over, and a caller that does not can ask
// for the canonical diff with `Diff`.
//
// Every comparison here is a comparison of *identity plus semantic content*: descriptor facts, an installation's
// scope/state/priority and the full declaration of each of its rules, the capability-contract table, the ordering
// keys and the selection overrides. An installation's config revision, config hash, generation and activation
// epoch are deliberately excluded, because derivation never reads them (a reconfiguration that changes no rule
// payload cannot change any assembly; P-020 configuration lives in the rule payload for derivation purposes).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>The declaration of one moved scope: what it was under and what it is under now (P-025).</summary>
    public readonly struct ScopeMove
    {
        public readonly ScopeId Scope;
        public readonly ScopeId OldParent;
        public readonly ScopeId NewParent;

        public ScopeMove(ScopeId scope, ScopeId oldParent, ScopeId newParent)
        {
            Scope = scope;
            OldParent = oldParent;
            NewParent = newParent;
        }

        public override string ToString() =>
            "Move(" + Scope.ToString() + ", " + OldParent.ToString() + " -> " + NewParent.ToString() + ")";
    }

    /// <summary>The declaration of one moved target: its old and new owner scope (P-010, P-025).</summary>
    public readonly struct TargetScopeMove
    {
        public readonly TargetId Target;
        public readonly ScopeId OldScope;
        public readonly ScopeId NewScope;

        public TargetScopeMove(TargetId target, ScopeId oldScope, ScopeId newScope)
        {
            Target = target;
            OldScope = oldScope;
            NewScope = newScope;
        }

        public override string ToString() =>
            "TargetMove(" + Target.ToString() + ", " + OldScope.ToString() + " -> " + NewScope.ToString() + ")";
    }

    /// <summary>What changed between the previously derived snapshot and the next one (P-023, 02 s7).</summary>
    public sealed class DerivationChangeSet
    {
        /// <summary>An empty change set: nothing at all differs.</summary>
        public static DerivationChangeSet Empty { get; } = new DerivationChangeSet(
            false,
            false,
            false,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            false,
            null,
            null,
            null);

        private DerivationChangeSet(
            bool worldChanged,
            bool modeChanged,
            bool contractsChanged,
            IReadOnlyList<ScopeId>? createdScopes,
            IReadOnlyList<ScopeId>? removedScopes,
            IReadOnlyList<ScopeId>? changedScopeFacts,
            IReadOnlyList<ScopeMove>? scopeMoves,
            IReadOnlyList<TargetId>? createdTargets,
            IReadOnlyList<TargetId>? retiredTargets,
            IReadOnlyList<TargetScopeMove>? targetMoves,
            IReadOnlyList<TargetId>? descriptorChangedTargets,
            IReadOnlyList<PluginInstanceId>? changedInstalls,
            bool overridesChanged,
            IReadOnlyList<ScopeId>? overrideScopes,
            IReadOnlyList<TargetId>? overrideTargets,
            IReadOnlyList<RuleId>? changedRuleKeys)
        {
            WorldChanged = worldChanged;
            ModeChanged = modeChanged;
            ContractsChanged = contractsChanged;
            CreatedScopes = ContractCollections.Freeze(createdScopes);
            RemovedScopes = ContractCollections.Freeze(removedScopes);
            ChangedScopeFacts = ContractCollections.Freeze(changedScopeFacts);
            ScopeMoves = ContractCollections.Freeze(scopeMoves);
            CreatedTargets = ContractCollections.Freeze(createdTargets);
            RetiredTargets = ContractCollections.Freeze(retiredTargets);
            TargetMoves = ContractCollections.Freeze(targetMoves);
            DescriptorChangedTargets = ContractCollections.Freeze(descriptorChangedTargets);
            ChangedInstalls = ContractCollections.Freeze(changedInstalls);
            OverridesChanged = overridesChanged;
            OverrideScopes = ContractCollections.Freeze(overrideScopes);
            OverrideTargets = ContractCollections.Freeze(overrideTargets);
            ChangedRuleKeys = ContractCollections.Freeze(changedRuleKeys);
        }

        /// <summary>The world incarnation differs; the previous result is not a base for this world at all (P-004).</summary>
        public bool WorldChanged { get; }

        /// <summary>The world propagation mode changed, which may legitimately invalidate the world (P-013, P-014).</summary>
        public bool ModeChanged { get; }

        /// <summary>The capability-contract table changed, so every composition must be re-checked (P-017, P-019).</summary>
        public bool ContractsChanged { get; }

        public IReadOnlyList<ScopeId> CreatedScopes { get; }

        public IReadOnlyList<ScopeId> RemovedScopes { get; }

        /// <summary>Scopes whose capability isolation, exclusions or import grants changed (P-013, P-016).</summary>
        public IReadOnlyList<ScopeId> ChangedScopeFacts { get; }

        public IReadOnlyList<ScopeMove> ScopeMoves { get; }

        public IReadOnlyList<TargetId> CreatedTargets { get; }

        public IReadOnlyList<TargetId> RetiredTargets { get; }

        public IReadOnlyList<TargetScopeMove> TargetMoves { get; }

        public IReadOnlyList<TargetId> DescriptorChangedTargets { get; }

        /// <summary>
        /// Installations whose record or declared rules changed, plus every installation created or removed. A
        /// changed installation includes one whose lifecycle state changed, because an inactive installation
        /// retracts its contributions (P-012).
        /// </summary>
        public IReadOnlyList<PluginInstanceId> ChangedInstalls { get; }

        /// <summary>True when the selection-override set differs; the addressed domains are in the two lists below.</summary>
        public bool OverridesChanged { get; }

        /// <summary>Scopes addressed by an override that changed, in either snapshot (P-018).</summary>
        public IReadOnlyList<ScopeId> OverrideScopes { get; }

        /// <summary>Targets addressed by an override that changed, in either snapshot (P-018).</summary>
        public IReadOnlyList<TargetId> OverrideTargets { get; }

        /// <summary>True when this change set names no change at all.</summary>
        public bool IsEmpty =>
            !WorldChanged
            && !ModeChanged
            && !ContractsChanged
            && !OverridesChanged
            && CreatedScopes.Count == 0
            && RemovedScopes.Count == 0
            && ChangedScopeFacts.Count == 0
            && ScopeMoves.Count == 0
            && CreatedTargets.Count == 0
            && RetiredTargets.Count == 0
            && TargetMoves.Count == 0
            && DescriptorChangedTargets.Count == 0
            && ChangedInstalls.Count == 0;

        /// <summary>Stable reason keys of this change set, sorted; the closure maps them onto its own report.</summary>
        public IReadOnlyList<string> Reasons()
        {
            List<string> reasons = new List<string>();
            if (WorldChanged)
            {
                reasons.Add(InvalidationReasons.WorldChanged);
            }

            if (ModeChanged)
            {
                reasons.Add(InvalidationReasons.ModeChanged);
            }

            if (ContractsChanged)
            {
                reasons.Add(InvalidationReasons.ContractsChanged);
            }

            if (CreatedScopes.Count != 0)
            {
                reasons.Add(InvalidationReasons.ScopeCreated);
            }

            if (RemovedScopes.Count != 0)
            {
                reasons.Add(InvalidationReasons.ScopeRemoved);
            }

            if (ChangedScopeFacts.Count != 0)
            {
                reasons.Add(InvalidationReasons.ScopeFactsChanged);
            }

            if (ScopeMoves.Count != 0)
            {
                reasons.Add(InvalidationReasons.ScopeReparented);
            }

            if (CreatedTargets.Count != 0)
            {
                reasons.Add(InvalidationReasons.TargetCreated);
            }

            if (RetiredTargets.Count != 0)
            {
                reasons.Add(InvalidationReasons.TargetRetired);
            }

            if (TargetMoves.Count != 0)
            {
                reasons.Add(InvalidationReasons.TargetMoved);
            }

            if (DescriptorChangedTargets.Count != 0)
            {
                reasons.Add(InvalidationReasons.DescriptorChanged);
            }

            if (ChangedInstalls.Count != 0)
            {
                reasons.Add(InvalidationReasons.InstallChanged);
            }

            if (OverridesChanged)
            {
                reasons.Add(InvalidationReasons.OverrideChanged);
            }

            if (ChangedRuleKeys.Count != 0)
            {
                reasons.Add(InvalidationReasons.RuleKeysChanged);
            }

            reasons.Sort(StringComparer.Ordinal);
            return reasons.AsReadOnly();
        }

        /// <summary>
        /// The canonical diff of two snapshots. This is the complete change detection of P-023 over the snapshot
        /// model: scopes (parent, isolation, exclusions, imports), installations (record and every declared rule),
        /// targets (owner scope and descriptor), the contract table, the ordering keys and the overrides.
        /// </summary>
        public static DerivationChangeSet Diff(
            DerivationSnapshot previous,
            DerivationSnapshot next,
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

            InvalidationCounters counts = counters ?? new InvalidationCounters();
            bool worldChanged = !previous.World.Session.Equals(next.World.Session);
            bool modeChanged = previous.Mode != next.Mode;
            bool contractsChanged = !CatalogEquals(previous.Contracts, next.Contracts);

            List<ScopeId> created = new List<ScopeId>();
            List<ScopeId> removed = new List<ScopeId>();
            List<ScopeId> factsChanged = new List<ScopeId>();
            List<ScopeMove> moves = new List<ScopeMove>();

            IReadOnlyList<DerivationScope> oldScopes = previous.Scopes;
            IReadOnlyList<DerivationScope> newScopes = next.Scopes;
            counts.ScopesCompared += oldScopes.Count + newScopes.Count;
            for (int i = 0; i < newScopes.Count; i++)
            {
                DerivationScope scope = newScopes[i];
                if (!previous.TryGetScope(scope.Scope, out DerivationScope? old) || old == null)
                {
                    created.Add(scope.Scope);
                    continue;
                }

                if (!old.Parent.Equals(scope.Parent))
                {
                    moves.Add(new ScopeMove(scope.Scope, old.Parent, scope.Parent));
                }

                if (!ScopeFactsEqual(old, scope))
                {
                    factsChanged.Add(scope.Scope);
                }
            }

            for (int i = 0; i < oldScopes.Count; i++)
            {
                if (!next.TryGetScope(oldScopes[i].Scope, out _))
                {
                    removed.Add(oldScopes[i].Scope);
                }
            }

            List<TargetId> createdTargets = new List<TargetId>();
            List<TargetId> retiredTargets = new List<TargetId>();
            List<TargetScopeMove> targetMoves = new List<TargetScopeMove>();
            List<TargetId> descriptorChanged = new List<TargetId>();

            IReadOnlyList<DerivationTarget> oldTargets = previous.Targets;
            IReadOnlyList<DerivationTarget> newTargets = next.Targets;
            counts.TargetsCompared += oldTargets.Count + newTargets.Count;
            for (int i = 0; i < newTargets.Count; i++)
            {
                DerivationTarget target = newTargets[i];
                if (!previous.TryGetTarget(target.Target, out DerivationTarget? old) || old == null)
                {
                    createdTargets.Add(target.Target);
                    continue;
                }

                if (!old.Scope.Equals(target.Scope))
                {
                    targetMoves.Add(new TargetScopeMove(target.Target, old.Scope, target.Scope));
                }

                if (!DescriptorTargetIndex.DescriptorsEqual(old.Descriptor, target.Descriptor))
                {
                    descriptorChanged.Add(target.Target);
                }
            }

            for (int i = 0; i < oldTargets.Count; i++)
            {
                if (!next.TryGetTarget(oldTargets[i].Target, out _))
                {
                    retiredTargets.Add(oldTargets[i].Target);
                }
            }

            List<PluginInstanceId> changedInstalls = new List<PluginInstanceId>();
            IReadOnlyList<DerivationInstall> oldInstalls = previous.Installs;
            IReadOnlyList<DerivationInstall> newInstalls = next.Installs;
            counts.InstallsCompared += oldInstalls.Count + newInstalls.Count;
            for (int i = 0; i < newInstalls.Count; i++)
            {
                DerivationInstall install = newInstalls[i];
                if (!previous.TryGetInstall(install.Instance, out DerivationInstall? old) || old == null)
                {
                    changedInstalls.Add(install.Instance);
                    continue;
                }

                if (!InstallsEqual(old, install))
                {
                    changedInstalls.Add(install.Instance);
                }
            }

            for (int i = 0; i < oldInstalls.Count; i++)
            {
                if (!next.TryGetInstall(oldInstalls[i].Instance, out _))
                {
                    changedInstalls.Add(oldInstalls[i].Instance);
                }
            }

            List<RuleId> changedRuleKeys = FindChangedRuleKeys(previous, next);
            bool overridesChanged = !OverridesEqual(previous, next);
            List<ScopeId> overrideScopes = new List<ScopeId>();
            List<TargetId> overrideTargets = new List<TargetId>();
            if (overridesChanged)
            {
                CollectOverrideDomains(previous.Overrides, overrideScopes, overrideTargets);
                CollectOverrideDomains(next.Overrides, overrideScopes, overrideTargets);
            }

            return new DerivationChangeSet(
                worldChanged,
                modeChanged,
                contractsChanged,
                created,
                removed,
                factsChanged,
                moves,
                createdTargets,
                retiredTargets,
                targetMoves,
                descriptorChanged,
                changedInstalls,
                overridesChanged,
                overrideScopes,
                overrideTargets,
                changedRuleKeys);
        }

        /// <summary>
        /// Rule identities whose declared ordering keys differ between the two snapshots (P-019). The closure
        /// treats the installation declaring such a rule as changed.
        /// </summary>
        public IReadOnlyList<RuleId> ChangedRuleKeys { get; }

        /// <summary>Canonical text of the change set; the audit form used in evidence files (P-052).</summary>
        public string Describe()
        {
            List<string> parts = new List<string>();
            for (int i = 0; i < Reasons().Count; i++)
            {
                parts.Add(Reasons()[i]);
            }

            parts.Sort(StringComparer.Ordinal);
            return "change{" + string.Join(",", parts.ToArray())
                + ";scopes+" + CreatedScopes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "/-" + RemovedScopes.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "/~" + (ChangedScopeFacts.Count + ScopeMoves.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ";targets+" + CreatedTargets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "/-" + RetiredTargets.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "/~" + (TargetMoves.Count + DescriptorChangedTargets.Count).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ";installs~" + ChangedInstalls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ";ruleKeys~" + ChangedRuleKeys.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + "}";
        }

        private static bool ScopeFactsEqual(DerivationScope left, DerivationScope right)
        {
            if (left.CapabilityIsolation.AllContracts != right.CapabilityIsolation.AllContracts
                || left.CapabilityIsolation.Contracts.Count != right.CapabilityIsolation.Contracts.Count)
            {
                return false;
            }

            for (int i = 0; i < left.CapabilityIsolation.Contracts.Count; i++)
            {
                if (!left.CapabilityIsolation.Contracts[i].Equals(right.CapabilityIsolation.Contracts[i]))
                {
                    return false;
                }
            }

            if (left.Exclusions.Count != right.Exclusions.Count || left.Imports.Count != right.Imports.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Exclusions.Count; i++)
            {
                if (!CanonicalDerivationOrder.ExclusionsEqual(left.Exclusions[i], right.Exclusions[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < left.Imports.Count; i++)
            {
                if (!CanonicalDerivationOrder.ImportsEqual(left.Imports[i], right.Imports[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool InstallsEqual(DerivationInstall left, DerivationInstall right)
        {
            if (!left.Record.Scope.Equals(right.Record.Scope)
                || left.Record.Priority != right.Record.Priority
                || left.State != right.State
                || left.Record.Generation.Value != right.Record.Generation.Value)
            {
                return false;
            }

            IReadOnlyList<DerivationRule> oldRules = left.Manifest.DerivationRules;
            IReadOnlyList<DerivationRule> newRules = right.Manifest.DerivationRules;
            if (oldRules.Count != newRules.Count)
            {
                return false;
            }

            for (int i = 0; i < oldRules.Count; i++)
            {
                if (!RulesEqual(oldRules[i], newRules[i]))
                {
                    return false;
                }
            }

            return ServiceDeclarationsEqual(left.Manifest, right.Manifest);
        }

        /// <summary>
        /// True when two rules are indistinguishable to derivation: identity, output capability, stratum, declared
        /// slot bound, selectors, predicate key, lower-stratum inputs, reach, export flag, both priorities, the
        /// composition policy and the immutable payload bytes (P-017, P-018, P-019, P-021).
        /// </summary>
        public static bool RulesEqual(DerivationRule left, DerivationRule right)
        {
            if (ReferenceEquals(left, right))
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            return left.RuleId.Equals(right.RuleId)
                && left.OutputCapability.Equals(right.OutputCapability)
                && left.OutputStratum == right.OutputStratum
                && left.MaxOutputSlots == right.MaxOutputSlots
                && left.Reach == right.Reach
                && left.ExportToDescendants == right.ExportToDescendants
                && left.Priority == right.Priority
                && left.Policy == right.Policy
                && left.StaticPredicate.Equals(right.StaticPredicate)
                && DescriptorTargetIndex.SchemasEqual(left.SelectorContracts, right.SelectorContracts)
                && DescriptorTargetIndex.CapabilitiesEqual(left.InputCapabilities, right.InputCapabilities)
                && SlotComposer.PayloadEquals(left.PayloadDefinition, right.PayloadDefinition);
        }

        private static bool ServiceDeclarationsEqual(PluginManifest left, PluginManifest right)
        {
            IReadOnlyList<ServiceDependency> oldDependencies = left.ServiceDependencies;
            IReadOnlyList<ServiceDependency> newDependencies = right.ServiceDependencies;
            if (oldDependencies.Count != newDependencies.Count)
            {
                return false;
            }

            for (int i = 0; i < oldDependencies.Count; i++)
            {
                if (!oldDependencies[i].Contract.Equals(newDependencies[i].Contract)
                    || oldDependencies[i].Required != newDependencies[i].Required)
                {
                    return false;
                }
            }

            IReadOnlyList<ServiceExport> oldExports = left.ServiceExports;
            IReadOnlyList<ServiceExport> newExports = right.ServiceExports;
            if (oldExports.Count != newExports.Count)
            {
                return false;
            }

            for (int i = 0; i < oldExports.Count; i++)
            {
                if (!oldExports[i].Contract.Equals(newExports[i].Contract)
                    || oldExports[i].Visibility != newExports[i].Visibility)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CatalogEquals(CapabilityCatalog left, CapabilityCatalog right) =>
            left.Contracts.Count == right.Contracts.Count
            && CapabilityCatalogHash.Text(left) == CapabilityCatalogHash.Text(right);

        private static List<RuleId> FindChangedRuleKeys(DerivationSnapshot previous, DerivationSnapshot next)
        {
            List<RuleId> changed = new List<RuleId>();
            for (int i = 0; i < next.RuleKeys.Count; i++)
            {
                DerivationRuleKeys keys = next.RuleKeys[i];
                if (!previous.TryGetRuleKeys(keys.Rule, out DerivationRuleKeys? old) || old == null)
                {
                    changed.Add(keys.Rule);
                    continue;
                }

                if (!RuleKeysEqual(old, keys))
                {
                    changed.Add(keys.Rule);
                }
            }

            for (int i = 0; i < previous.RuleKeys.Count; i++)
            {
                if (!next.TryGetRuleKeys(previous.RuleKeys[i].Rule, out _))
                {
                    changed.Add(previous.RuleKeys[i].Rule);
                }
            }

            return changed;
        }

        private static bool RuleKeysEqual(DerivationRuleKeys left, DerivationRuleKeys right)
        {
            if (!left.Capability.Equals(right.Capability) || !left.SelfKey.Equals(right.SelfKey))
            {
                return false;
            }

            if (left.Before.Count != right.Before.Count || left.After.Count != right.After.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Before.Count; i++)
            {
                if (!CanonicalDerivationOrder.OrderKeyEdgesEqual(left.Before[i], right.Before[i]))
                {
                    return false;
                }
            }

            for (int i = 0; i < left.After.Count; i++)
            {
                if (!CanonicalDerivationOrder.OrderKeyEdgesEqual(left.After[i], right.After[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool OverridesEqual(DerivationSnapshot previous, DerivationSnapshot next)
        {
            IReadOnlyList<ProviderSelectionOverride> left = previous.Overrides;
            IReadOnlyList<ProviderSelectionOverride> right = next.Overrides;
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!CanonicalDerivationOrder.OverridesEqual(left[i], right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void CollectOverrideDomains(
            IReadOnlyList<ProviderSelectionOverride> overrides,
            List<ScopeId> scopes,
            List<TargetId> targets)
        {
            for (int i = 0; i < overrides.Count; i++)
            {
                ProviderSelectionOverride item = overrides[i];
                if (!item.AtTarget.IsDefault && !Contains(targets, item.AtTarget))
                {
                    targets.Add(item.AtTarget);
                }

                if (!item.AtScope.IsDefault && !Contains(scopes, item.AtScope))
                {
                    scopes.Add(item.AtScope);
                }
            }
        }

        private static bool Contains(List<ScopeId> scopes, ScopeId candidate)
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

        private static bool Contains(List<TargetId> targets, TargetId candidate)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
