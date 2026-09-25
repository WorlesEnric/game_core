// GameCore.Derivation — the provider/contribution and reverse-capability indexes (GC-013, P-017/P-023).
//
// P-023 requires "provider/rule-to-contributions, capability reverse dependencies" and P-025 requires a subtree
// move to diff "the union of old and new ancestor rule/service sets". Both need the same two facts:
//
//   * which installation declares which rules, and which capability each rule outputs (the provider index);
//   * for one capability, which active rules anywhere in the world can supply it, and which provider scopes they
//     are installed at (the reverse-capability index).
//
// The reverse index is what turns "capability X became (un)available at scope S" into an affected set without
// scanning every rule: the closure looks up X and walks only the providers whose reach can cover S. It also
// resolves the provider set behind a scope's Conservative imports, which is the "provider closure" P-025 diffs.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>One active rule together with the installation that declares it (the invalidation unit).</summary>
    public readonly struct IndexedRule
    {
        public readonly DerivationInstall Install;
        public readonly DerivationRule Rule;

        public IndexedRule(DerivationInstall install, DerivationRule rule)
        {
            Install = install;
            Rule = rule;
        }

        public ProviderInstallationId Provider => new ProviderInstallationId(Install.Instance.Value);

        /// <summary>The capability this rule outputs, i.e. the reverse-dependency key of the index.</summary>
        public CapabilityId Output => Rule.OutputCapability.Capability;

        public override string ToString() =>
            "Rule(" + Rule.RuleId.ToString() + "@" + Install.Instance.ToString() + ")";
    }

    /// <summary>Active rules by output capability and by provider installation (P-017, P-023).</summary>
    public sealed class ProviderContributionIndex
    {
        private readonly DerivationSnapshot snapshot;
        private readonly ScopeMembershipIndex membership;
        private readonly List<IndexedRule> rules = new List<IndexedRule>();
        private readonly Dictionary<Id128, List<IndexedRule>> byCapability;
        private readonly Dictionary<Id128, List<IndexedRule>> byProvider;
        private readonly Dictionary<Id128, List<DerivationRule>> rulesOfProvider;
        private readonly Dictionary<Id128, List<Id128>> outputsOfProvider;
        private readonly Dictionary<Id128, List<IndexedRule>> byRule;

        private ProviderContributionIndex(DerivationSnapshot snapshot, ScopeMembershipIndex membership)
        {
            this.snapshot = snapshot;
            this.membership = membership;
            byCapability = new Dictionary<Id128, List<IndexedRule>>();
            byProvider = new Dictionary<Id128, List<IndexedRule>>();
            rulesOfProvider = new Dictionary<Id128, List<DerivationRule>>();
            outputsOfProvider = new Dictionary<Id128, List<Id128>>();
            byRule = new Dictionary<Id128, List<IndexedRule>>();

            IReadOnlyList<DerivationInstall> installs = snapshot.ActiveInstalls;
            for (int i = 0; i < installs.Count; i++)
            {
                DerivationInstall install = installs[i];
                IReadOnlyList<DerivationRule> declared = install.Manifest.DerivationRules;
                List<DerivationRule> providerRules = new List<DerivationRule>(declared.Count);
                List<Id128> outputs = new List<Id128>(declared.Count);
                for (int r = 0; r < declared.Count; r++)
                {
                    IndexedRule indexed = new IndexedRule(install, declared[r]);
                    rules.Add(indexed);
                    providerRules.Add(declared[r]);
                    if (!outputs.Contains(indexed.Output.Value))
                    {
                        outputs.Add(indexed.Output.Value);
                    }

                    Add(byCapability, indexed.Output.Value, indexed);
                    Add(byProvider, install.Instance.Value, indexed);
                    Add(byRule, indexed.Rule.RuleId.Value, indexed);
                }

                rulesOfProvider[install.Instance.Value] = providerRules;
                outputsOfProvider[install.Instance.Value] = outputs;
            }
        }

        /// <summary>Builds the provider/contribution index of one snapshot.</summary>
        public static ProviderContributionIndex Build(DerivationSnapshot snapshot) =>
            Build(snapshot, ScopeMembershipIndex.Build(snapshot));

        /// <summary>Builds the index reusing an already-built membership index (the closure builds both once).</summary>
        public static ProviderContributionIndex Build(DerivationSnapshot snapshot, ScopeMembershipIndex membership)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (membership == null)
            {
                throw new ArgumentNullException(nameof(membership));
            }

            return new ProviderContributionIndex(snapshot, membership);
        }

        /// <summary>Every active rule in canonical (installation, rule) order.</summary>
        public IReadOnlyList<IndexedRule> Rules => rules;

        /// <summary>Active rules that output one capability, canonical order (P-023 reverse dependencies).</summary>
        public IReadOnlyList<IndexedRule> RulesOutputting(CapabilityId capability) =>
            byCapability.TryGetValue(capability.Value, out List<IndexedRule>? list)
                ? list
                : (IReadOnlyList<IndexedRule>)Array.Empty<IndexedRule>();

        /// <summary>Active rules declared by one installation, canonical rule-identity order.</summary>
        public IReadOnlyList<IndexedRule> RulesOf(PluginInstanceId instance) =>
            byProvider.TryGetValue(instance.Value, out List<IndexedRule>? list)
                ? list
                : (IReadOnlyList<IndexedRule>)Array.Empty<IndexedRule>();

        /// <summary>Manifest rules of one installation, or empty when it is absent or inactive.</summary>
        public IReadOnlyList<DerivationRule> ManifestRulesOf(PluginInstanceId instance) =>
            rulesOfProvider.TryGetValue(instance.Value, out List<DerivationRule>? list)
                ? list
                : (IReadOnlyList<DerivationRule>)Array.Empty<DerivationRule>();

        /// <summary>Capabilities one installation's active rules output, in first-declaration order.</summary>
        public IReadOnlyList<Id128> OutputsOf(PluginInstanceId instance) =>
            outputsOfProvider.TryGetValue(instance.Value, out List<Id128>? list)
                ? list
                : (IReadOnlyList<Id128>)Array.Empty<Id128>();


        /// <summary>
        /// Active rules declared with one rule identity, across every installation. Rule identities are declared
        /// once per installation, so this is the lookup an ordering-key edit uses to find what it affects (P-019).
        /// </summary>
        public IReadOnlyList<IndexedRule> RulesWithId(RuleId rule) =>
            byRule.TryGetValue(rule.Value, out List<IndexedRule>? list)
                ? list
                : (IReadOnlyList<IndexedRule>)Array.Empty<IndexedRule>();

        /// <summary>
        /// Active rules whose reach from their own scope covers <paramref name="targetScope"/> and whose rule
        /// outputs <paramref name="capability"/>. This is the provider closure a subtree move diffs (P-025).
        /// </summary>
        public IReadOnlyList<IndexedRule> RulesCovering(CapabilityId capability, ScopeId targetScope)
        {
            IReadOnlyList<IndexedRule> candidates = RulesOutputting(capability);
            if (candidates.Count == 0)
            {
                return candidates;
            }

            List<IndexedRule> covering = new List<IndexedRule>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++)
            {
                IndexedRule rule = candidates[i];
                if (membership.IsInReach(rule.Install.Scope, rule.Rule.Reach, targetScope))
                {
                    covering.Add(rule);
                }
            }

            return covering.AsReadOnly();
        }

        /// <summary>
        /// Capabilities that any active provider could reach <paramref name="targetScope"/> with: the reverse map
        /// from a scope to the capabilities whose eligibility that scope can take part in. A move of this scope
        /// only ever changes these capabilities (P-025 "the union of old and new ancestor rule sets").
        /// </summary>
        public IReadOnlyList<CapabilityId> CapabilitiesReaching(ScopeId targetScope)
        {
            List<CapabilityId> capabilities = new List<CapabilityId>();
            HashSet<Id128> seen = new HashSet<Id128>();
            for (int i = 0; i < rules.Count; i++)
            {
                IndexedRule rule = rules[i];
                if (!seen.Add(rule.Output.Value))
                {
                    continue;
                }

                if (membership.IsInReach(rule.Install.Scope, rule.Rule.Reach, targetScope))
                {
                    capabilities.Add(rule.Output);
                }
            }

            capabilities.Sort(CompareCapabilities);
            return capabilities.AsReadOnly();
        }

        /// <summary>
        /// The provider installations named by a scope chain's Conservative imports, nearest scope first. Imports
        /// are stored data (P-013), so this resolves the declared providers of the path rather than guessing.
        /// </summary>
        public IReadOnlyList<PluginInstanceId> ImportedProviders(ScopeId targetScope)
        {
            IReadOnlyList<ScopeId> chain = membership.Snapshot.SelfAndAncestors(targetScope);
            List<PluginInstanceId> providers = new List<PluginInstanceId>();
            HashSet<Id128> seen = new HashSet<Id128>();
            for (int s = 0; s < chain.Count; s++)
            {
                if (!snapshot.TryGetScope(chain[s], out DerivationScope? scope) || scope == null)
                {
                    continue;
                }

                IReadOnlyList<CapabilityImport> imports = scope.Imports;
                for (int i = 0; i < imports.Count; i++)
                {
                    if (seen.Add(imports[i].ProviderInstallationId.Value))
                    {
                        providers.Add(new PluginInstanceId(imports[i].ProviderInstallationId.Value));
                    }
                }
            }

            return providers.AsReadOnly();
        }

        private static int CompareCapabilities(CapabilityId left, CapabilityId right) =>
            left.Value.CompareTo(right.Value);

        private static void Add(Dictionary<Id128, List<IndexedRule>> index, Id128 key, IndexedRule rule)
        {
            if (!index.TryGetValue(key, out List<IndexedRule>? list))
            {
                list = new List<IndexedRule>();
                index.Add(key, list);
            }

            list.Add(rule);
        }
    }
}
