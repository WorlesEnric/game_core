// GameCore.Derivation — the derived-recipe variant cache and its inheritance fingerprint (GC-013, P-024/P-023).
//
// P-024: "Cache derived variants by recipe revision, scope inheritance fingerprint, mode, and catalog hash... a
// stale variant is recomputed or rejected `StalePlan`, never published half-assembled." 02 s3 lists the same cache
// as an index.
//
// What is cached here is the *resolved inheritance* of one (recipe, scope) pair: the ordered set of rules that can
// reach a target with that recipe at that scope. Every P-013 reach selector selects targets inside the provider's
// own subtree, so the complete reaching set is exactly the active rules of the installations at the target's scope
// or one of its ancestors — a walk of the ancestor chain, which this cache turns into one dictionary lookup.
//
// The fingerprint is what makes reuse safe: it is computed from every input that can change the resolved set or
// the composed value for such a target — catalog hash, mode, the recipe revision, every scope fact on the path
// (boundary, exclusions, imports), the descriptor facts, the installations on the path with their declared rules,
// the ordering keys of those rules and the selection overrides that address the path. Two targets with equal
// fingerprint are indistinguishable to derivation for this purpose; a reparent, a boundary edit, a provider
// mount or a payload reconfiguration all change it, so the stale entry is recomputed instead of reused.
//
// The cache is a performance structure with no authority: it holds no state a caller could read as gameplay, and
// clearing it changes nothing but the counters (P-023 "the indexes are derived data and rebuildable").
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>The resolved inheritance of one (recipe, scope) pair: the rules that can reach it (P-024).</summary>
    public sealed class DerivedRecipeVariant
    {
        public DerivedRecipeVariant(
            ContentHash fingerprint,
            DefinitionRef recipe,
            ScopeId scope,
            IReadOnlyList<IndexedRule>? reachingRules)
        {
            Fingerprint = fingerprint;
            Recipe = recipe;
            Scope = scope;
            ReachingRules = ContractCollections.Freeze(reachingRules);
        }

        /// <summary>Inheritance fingerprint this variant was resolved under; the cache reuses it only for an equal one.</summary>
        public ContentHash Fingerprint { get; }

        public DefinitionRef Recipe { get; }

        public ScopeId Scope { get; }

        /// <summary>Rules the resolution found, in canonical (installation, rule) order.</summary>
        public IReadOnlyList<IndexedRule> ReachingRules { get; }

        public override string ToString() =>
            "Variant(" + Recipe.ToString() + "@" + Scope.ToString() + ", rules="
            + ReachingRules.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Canonical inheritance fingerprint of one target's recipe and scope path (P-023, P-024).</summary>
    public static class ScopeInheritanceFingerprint
    {
        /// <summary>Hash of every input that can change what derives on a target (see the file header).</summary>
        public static ContentHash Compute(
            DerivationSnapshot snapshot,
            DerivationIndexSet indexes,
            DerivationTarget target,
            ContentHash catalogHash)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (indexes == null)
            {
                throw new ArgumentNullException(nameof(indexes));
            }

            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return ContentHash.Compute(Encoding.UTF8.GetBytes(Text(snapshot, indexes, target, catalogHash)));
        }

        /// <summary>Canonical text of the same inputs; the audit form of the fingerprint (P-008).</summary>
        public static string Text(
            DerivationSnapshot snapshot,
            DerivationIndexSet indexes,
            DerivationTarget target,
            ContentHash catalogHash)
        {
            StringBuilder text = new StringBuilder();
            text.Append("catalog=").Append(catalogHash.ToHex()).Append('\n');
            text.Append("mode=").Append(snapshot.Mode.ToString()).Append('\n');
            text.Append("recipe=").Append(target.Descriptor.Recipe.ToString()).Append('\n');

            IReadOnlyList<ScopeId> path = snapshot.SelfAndAncestors(target.Scope);
            for (int i = 0; i < path.Count; i++)
            {
                if (!snapshot.TryGetScope(path[i], out DerivationScope? scope) || scope == null)
                {
                    continue;
                }

                text.Append("scope=").Append(scope.Scope.ToString()).Append('\n');
                text.Append("  isolation=").Append(scope.CapabilityIsolation.AllContracts ? "*" : "-");
                IReadOnlyList<Id128> contracts = scope.CapabilityIsolation.Contracts;
                for (int c = 0; c < contracts.Count; c++)
                {
                    text.Append(',').Append(contracts[c].ToString());
                }

                text.Append('\n');
                for (int e = 0; e < scope.Exclusions.Count; e++)
                {
                    text.Append("  exclusion=").Append(scope.Exclusions[e].ToString())
                        .Append('/').Append(scope.Exclusions[e].AppliesToSubtree ? "subtree" : "self")
                        .Append('\n');
                }

                for (int m = 0; m < scope.Imports.Count; m++)
                {
                    text.Append("  import=").Append(scope.Imports[m].CapabilityId.ToString())
                        .Append('@').Append(scope.Imports[m].ProviderInstallationId.ToString())
                        .Append('\n');
                }
            }

            AppendDescriptor(text, target.Descriptor);

            IReadOnlyList<DerivationInstall> installs = indexes.InstallsOnPath(target.Scope);
            for (int i = 0; i < installs.Count; i++)
            {
                DerivationInstall install = installs[i];
                text.Append("install=").Append(install.Instance.ToString())
                    .Append(";scope=").Append(install.Scope.ToString())
                    .Append(";priority=").Append(PayloadCodec.PriorityText(install.Record.Priority))
                    .Append(";state=").Append(install.State.ToString())
                    .Append(";generation=").Append(install.Record.Generation.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append(";activation=").Append(install.Record.ActivationEpoch.Value.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('\n');
                if (!install.IsActive)
                {
                    continue;
                }

                IReadOnlyList<DerivationRule> rules = install.Manifest.DerivationRules;
                for (int r = 0; r < rules.Count; r++)
                {
                    AppendRule(text, snapshot, rules[r]);
                }
            }

            IReadOnlyList<ProviderSelectionOverride> overrides = snapshot.Overrides;
            for (int i = 0; i < overrides.Count; i++)
            {
                ProviderSelectionOverride item = overrides[i];
                bool addresses = (item.AtTarget.IsDefault == false && item.AtTarget.Equals(target.Target))
                    || (item.AtScope.IsDefault == false
                        && (item.AtScope.Equals(target.Scope)
                            || (item.AppliesToSubtree && snapshot.IsSelfOrAncestor(item.AtScope, target.Scope))));
                if (!addresses)
                {
                    continue;
                }

                text.Append("override=").Append(item.AtScope.ToString()).Append('/').Append(item.AtTarget.ToString())
                    .Append('/').Append(item.Capability.ToString()).Append('@').Append(item.Provider.ToString())
                    .Append('/').Append(item.AppliesToSubtree ? "subtree" : "self").Append('\n');
            }

            return text.ToString();
        }

        private static void AppendDescriptor(StringBuilder text, TargetDescriptor descriptor)
        {
            text.Append("descriptor=").Append(descriptor.Recipe.ToString())
                .Append(";adapter=").Append(descriptor.AssetAdapter.AdapterId.ToString())
                .Append('@').Append(descriptor.AssetAdapter.Version.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append('\n');
            IReadOnlyList<SchemaRef> schemas = descriptor.SupportedSchemas;
            for (int s = 0; s < schemas.Count; s++)
            {
                text.Append("  schema=").Append(schemas[s].ToString()).Append('\n');
            }

            IReadOnlyList<CapabilityRef> capabilities = descriptor.SupportedCapabilities;
            for (int c = 0; c < capabilities.Count; c++)
            {
                text.Append("  capability=").Append(capabilities[c].ToString()).Append('\n');
            }

            IReadOnlyList<Id128> tags = descriptor.Tags;
            for (int t = 0; t < tags.Count; t++)
            {
                text.Append("  tag=").Append(tags[t].ToString()).Append('\n');
            }

            IReadOnlyList<DefinitionRef> patches = descriptor.LocalPatches;
            for (int p = 0; p < patches.Count; p++)
            {
                text.Append("  patch=").Append(patches[p].ToString()).Append('\n');
            }

            IReadOnlyList<CapabilityImport> imports = descriptor.Imports;
            for (int i = 0; i < imports.Count; i++)
            {
                text.Append("  import=").Append(imports[i].CapabilityId.ToString())
                    .Append('@').Append(imports[i].ProviderInstallationId.ToString()).Append('\n');
            }

            IReadOnlyList<TargetOptIn> optIns = descriptor.OptIns;
            for (int o = 0; o < optIns.Count; o++)
            {
                text.Append("  optIn=").Append(optIns[o].CapabilityId.ToString())
                    .Append('@').Append(optIns[o].ProviderInstallationId.ToString()).Append('\n');
            }

            IReadOnlyList<ExclusionRule> exclusions = descriptor.Exclusions;
            for (int e = 0; e < exclusions.Count; e++)
            {
                text.Append("  exclusion=").Append(exclusions[e].ToString())
                    .Append('/').Append(exclusions[e].AppliesToSubtree ? "subtree" : "self").Append('\n');
            }
        }

        private static void AppendRule(StringBuilder text, DerivationSnapshot snapshot, DerivationRule rule)
        {
            text.Append("  rule=").Append(rule.RuleId.ToString())
                .Append(";output=").Append(rule.OutputCapability.ToString())
                .Append(";stratum=").Append(PayloadCodec.PriorityText(rule.OutputStratum))
                .Append(";maxSlots=").Append(rule.MaxOutputSlots.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .Append(";reach=").Append(rule.Reach.ToString())
                .Append(";export=").Append(rule.ExportToDescendants ? "1" : "0")
                .Append(";priority=").Append(PayloadCodec.PriorityText(rule.Priority))
                .Append(";policy=").Append(rule.Policy.ToString())
                .Append(";predicate=").Append(rule.StaticPredicate.RegistrationKey.ToString())
                .Append(";payload=").Append(PayloadCodec.HashOf(rule.PayloadDefinition).ToHex())
                .Append('\n');
            IReadOnlyList<SchemaRef> selectors = rule.SelectorContracts;
            for (int s = 0; s < selectors.Count; s++)
            {
                text.Append("    selector=").Append(selectors[s].ToString()).Append('\n');
            }

            IReadOnlyList<CapabilityRef> inputs = rule.InputCapabilities;
            for (int i = 0; i < inputs.Count; i++)
            {
                text.Append("    input=").Append(inputs[i].ToString()).Append('\n');
            }

            if (snapshot.TryGetRuleKeys(rule.RuleId, out DerivationRuleKeys? keys) && keys != null)
            {
                text.Append("    keys=").Append(keys.Capability.ToString())
                    .Append('/').Append(keys.SelfKey.ToString()).Append('\n');
                for (int b = 0; b < keys.Before.Count; b++)
                {
                    text.Append("      before=").Append(keys.Before[b].Key.ToString())
                        .Append('/').Append(keys.Before[b].Required ? "required" : "optional").Append('\n');
                }

                for (int a = 0; a < keys.After.Count; a++)
                {
                    text.Append("      after=").Append(keys.After[a].Key.ToString())
                        .Append('/').Append(keys.After[a].Required ? "required" : "optional").Append('\n');
                }
            }
        }
    }

    /// <summary>
    /// Cache of resolved inheritance variants, keyed by recipe revision plus inheritance fingerprint (P-024).
    /// A lookup whose fingerprint changed is a stale recomputation, not a hit.
    /// </summary>
    public sealed class DerivedRecipeCache : ITelemetryOwner
    {
        string ITelemetryOwner.TelemetryOwner => "gamecore.derivation.cache";

        private readonly Dictionary<Id128, List<Entry>> byRecipe = new Dictionary<Id128, List<Entry>>();

        public DerivedRecipeCache(ContentHash catalogHash, int capacity = 256)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "A cache needs a positive capacity.");
            }

            CatalogHash = catalogHash;
            Capacity = capacity;
        }

        /// <summary>The catalog hash every fingerprint is built with (P-024).</summary>
        public ContentHash CatalogHash { get; }

        /// <summary>Entries the cache holds before it is cleared (a bounded cache, never an unbounded one).</summary>
        public int Capacity { get; }

        /// <summary>Entries currently cached.</summary>
        public int Count { get; private set; }

        /// <summary>Lookups answered by an existing variant with an equal fingerprint.</summary>
        public int Hits { get; private set; }

        /// <summary>Lookups that had to resolve the inheritance.</summary>
        public int Misses { get; private set; }

        /// <summary>Lookups that found an entry whose fingerprint no longer matched: a stale variant (P-024).</summary>
        public int StaleRecomputations { get; private set; }

        /// <summary>Entries discarded because the cache reached its capacity.</summary>
        public int Evictions { get; private set; }

        /// <summary>
        /// Bytes this cache accounts for: one documented per-entry estimate plus the resolved rule references each
        /// entry holds. The "cache" half of TEST-023's memory split, reported separately from leases, retained
        /// events and quarantine (GC-023).
        /// </summary>
        public long RetainedBytes
        {
            get
            {
                long bytes = 0L;
                foreach (KeyValuePair<Id128, List<Entry>> pair in byRecipe)
                {
                    for (int i = 0; i < pair.Value.Count; i++)
                    {
                        Entry entry = pair.Value[i];
                        bytes += CacheEntryBytes
                            + (entry.Variant.ReachingRules.Count * CacheRuleBytes);
                    }
                }

                return bytes;
            }
        }

        /// <summary>Documented per-entry cache bookkeeping: key, fingerprint, recipe and list header.</summary>
        public const int CacheEntryBytes = 128;

        /// <summary>Documented per-cached-rule reference: the rule identity and its install reference.</summary>
        public const int CacheRuleBytes = 32;

        /// <summary>Writes this cache's counters through the fixed compact schema (GC-023, TEST-023).</summary>
        public void WriteTelemetry(TelemetryCounterSet into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            into.ObserveMax(TelemetryCounter.CacheEntries, Count);
            into.ObserveMax(TelemetryCounter.CacheBytes, RetainedBytes);
            into.Add(TelemetryCounter.StaleResults, StaleRecomputations);
        }

        /// <summary>
        /// Resolves the inheritance of one target: the rules that can reach it, in canonical order. The
        /// fingerprint the resolution was made under is returned so a caller can store it against a prepared
        /// spawn and detect staleness later (P-024).
        /// </summary>
        public IReadOnlyList<IndexedRule> Resolve(
            DerivationSnapshot snapshot,
            DerivationIndexSet indexes,
            TargetId target,
            out ContentHash fingerprint)
        {
            if (snapshot == null)
            {
                throw new ArgumentNullException(nameof(snapshot));
            }

            if (indexes == null)
            {
                throw new ArgumentNullException(nameof(indexes));
            }

            if (!snapshot.TryGetTarget(target, out DerivationTarget? found) || found == null)
            {
                fingerprint = ContentHash.Empty;
                return Array.Empty<IndexedRule>();
            }

            fingerprint = ScopeInheritanceFingerprint.Compute(snapshot, indexes, found, CatalogHash);
            IReadOnlyList<IndexedRule> reaching = Find(indexes, found);
            Id128 key = found.Descriptor.Recipe.Id.Value;
            List<Entry>? entries = null;
            if (byRecipe.TryGetValue(key, out List<Entry>? existing))
            {
                entries = existing;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].Variant.Scope.Equals(found.Scope))
                    {
                        if (entries[i].Variant.Fingerprint.Equals(fingerprint))
                        {
                            // The same inputs: the cached resolution is reused rather than recomputed.
                            Hits++;
                            return entries[i].Variant.ReachingRules;
                        }

                        StaleRecomputations++;
                        Entry replacement = new Entry(found.Descriptor.Recipe, found.Scope, fingerprint, reaching);
                        entries[i] = replacement;
                        return replacement.Variant.ReachingRules;
                    }
                }
            }

            Misses++;
            if (Count >= Capacity)
            {
                Evictions += Count;
                byRecipe.Clear();
                Count = 0;
                entries = null;
            }

            Entry entry = new Entry(found.Descriptor.Recipe, found.Scope, fingerprint, reaching);
            if (entries == null)
            {
                entries = new List<Entry>();
                byRecipe.Add(key, entries);
            }

            entries.Add(entry);
            Count++;
            return entry.Variant.ReachingRules;
        }

        /// <summary>True when a variant resolved earlier is still current under the given fingerprint (P-024).</summary>
        public static bool IsCurrent(DerivedRecipeVariant variant, ContentHash fingerprint)
        {
            if (variant == null)
            {
                throw new ArgumentNullException(nameof(variant));
            }

            return variant.Fingerprint.Equals(fingerprint);
        }

        /// <summary>Discards every entry; the counters stay as evidence of what the run did.</summary>
        public void Clear()
        {
            byRecipe.Clear();
            Count = 0;
        }

        /// <summary>Canonical text of the cache's cost, for diagnostics and evidence files (02 s9).</summary>
        public string Describe() =>
            "recipeCache{entries=" + Count.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";capacity=" + Capacity.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";hits=" + Hits.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";misses=" + Misses.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";stale=" + StaleRecomputations.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + ";evictions=" + Evictions.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";

        public override string ToString() => Describe();

        private static IReadOnlyList<IndexedRule> Find(DerivationIndexSet indexes, DerivationTarget target)
        {
            IReadOnlyList<DerivationInstall> path = indexes.InstallsOnPath(target.Scope);
            List<IndexedRule> rules = new List<IndexedRule>();
            for (int i = 0; i < path.Count; i++)
            {
                DerivationInstall install = path[i];
                if (!install.IsActive)
                {
                    continue;
                }

                IReadOnlyList<IndexedRule> ofInstall = indexes.Providers.RulesOf(install.Instance);
                for (int r = 0; r < ofInstall.Count; r++)
                {
                    if (indexes.Membership.IsInReach(ofInstall[r].Install.Scope, ofInstall[r].Rule.Reach, target.Scope))
                    {
                        rules.Add(ofInstall[r]);
                    }
                }
            }

            rules.Sort(DerivationIndexSet.CompareRules);
            return rules.AsReadOnly();
        }

        private readonly struct Entry
        {
            public readonly DerivedRecipeVariant Variant;

            public Entry(DefinitionRef recipe, ScopeId scope, ContentHash fingerprint, IReadOnlyList<IndexedRule> rules)
            {
                Variant = new DerivedRecipeVariant(fingerprint, recipe, scope, rules);
            }
        }
    }
}
