// GameCore.Derivation — the indexed immutable derivation snapshot (GC-006).
//
// Input of one derivation is a frozen snapshot, never live composition state: scopes, active installations with
// their manifests, targets with their descriptors and the capability-contract table. Construction validates the
// structure (one rooted acyclic scope tree, resolvable references, unique identities) and builds the indexes the
// runtime engine walks — scope ancestry/reach, targets by scope/contract/tag and rules bucketed by stratum
// (P-023). Indexes are derived data: the reference oracle ignores every one of them.
//
// Catalog-level problems (stratum order, policy agreement, slot bounds, unknown contracts, missing reducers)
// are not structural, so they are reported by DerivationValidation as a whole-proposal rejection (P-028) rather
// than thrown here.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>One active rule plus the installation that declares it: the unit of candidate enumeration.</summary>
    internal readonly struct RuleSource
    {
        public readonly DerivationInstall Install;
        public readonly DerivationRule Rule;

        public RuleSource(DerivationInstall install, DerivationRule rule)
        {
            Install = install;
            Rule = rule;
        }
    }

    /// <summary>The scope domain of one reach selector, with its membership index and target population.</summary>
    internal sealed class ReachDomain
    {
        private readonly HashSet<Id128> lookup;

        public ReachDomain(IReadOnlyList<ScopeId> scopes, int targetCount)
        {
            Scopes = scopes;
            TargetCount = targetCount;
            lookup = new HashSet<Id128>();
            for (int i = 0; i < scopes.Count; i++)
            {
                lookup.Add(scopes[i].Value);
            }
        }

        public IReadOnlyList<ScopeId> Scopes { get; }

        /// <summary>How many targets live in this domain; the engine walks the smaller of two candidate lists.</summary>
        public int TargetCount { get; }

        public bool Contains(ScopeId scope) => lookup.Contains(scope.Value);
    }

    /// <summary>Indexed immutable derivation input (P-023, P-015).</summary>
    public sealed class DerivationSnapshot
    {
        /// <summary>P-021 assigns capability contracts strata 0..31.</summary>
        public const int StratumCount = 32;

        private readonly DerivationScope[] scopes;
        private readonly DerivationInstall[] installs;
        private readonly DerivationTarget[] targets;
        private readonly CapabilityCatalog contracts;
        private readonly DerivationRuleKeys[] ruleKeys;
        private readonly ProviderSelectionOverride[] overrides;

        private readonly Dictionary<Id128, int> scopeIndex;
        private readonly Dictionary<Id128, int> targetIndex;
        private readonly Dictionary<Id128, int> installIndex;
        private readonly Dictionary<Id128, int> ruleKeyIndex;
        private readonly Dictionary<Id128, List<DerivationTarget>> targetsByScope;
        private readonly Dictionary<Id128, List<DerivationTarget>> targetsByCapability;
        private readonly Dictionary<Id128, List<DerivationTarget>> targetsBySchema;
        private readonly Dictionary<Id128, List<DerivationTarget>> targetsByTag;
        private readonly DerivationInstall[] activeInstalls;
        private readonly RuleSource[][] rulesByStratum;
        private readonly RuleSource[] outOfRangeRules;
        private readonly Dictionary<Id128, ReachDomain> selfAndDescendants;
        private readonly Dictionary<Id128, ReachDomain> descendantsOnly;
        private readonly Dictionary<Id128, ReachDomain> localOnly;

        public DerivationSnapshot(
            WorldId world,
            CompositionRevision revision,
            AssemblyEpoch epoch,
            PropagationMode mode,
            IReadOnlyList<DerivationScope>? scopes,
            IReadOnlyList<DerivationInstall>? installs,
            IReadOnlyList<DerivationTarget>? targets,
            IReadOnlyList<CapabilityContract>? capabilityContracts,
            IReadOnlyList<DerivationRuleKeys>? ruleKeys,
            IReadOnlyList<ProviderSelectionOverride>? overrides)
        {
            World = world;
            Revision = revision;
            Epoch = epoch;
            Mode = mode;
            contracts = new CapabilityCatalog(capabilityContracts);

            this.scopes = SortedScopes(scopes);
            this.installs = SortedInstalls(installs);
            this.targets = SortedTargets(targets);
            this.ruleKeys = SortedRuleKeys(ruleKeys);
            this.overrides = SortedOverrides(overrides);

            scopeIndex = IndexScopes(this.scopes);
            targetIndex = IndexTargets(this.targets);
            installIndex = IndexInstalls(this.installs);
            ruleKeyIndex = IndexRuleKeys(this.ruleKeys);

            targetsByScope = new Dictionary<Id128, List<DerivationTarget>>();
            targetsByCapability = new Dictionary<Id128, List<DerivationTarget>>();
            targetsBySchema = new Dictionary<Id128, List<DerivationTarget>>();
            targetsByTag = new Dictionary<Id128, List<DerivationTarget>>();
            for (int i = 0; i < this.targets.Length; i++)
            {
                DerivationTarget target = this.targets[i];
                AddIndex(targetsByScope, target.Scope.Value, target);
                IReadOnlyList<CapabilityRef> capabilities = target.Descriptor.SupportedCapabilities;
                for (int c = 0; c < capabilities.Count; c++)
                {
                    AddIndex(targetsByCapability, capabilities[c].Capability.Value, target);
                }

                IReadOnlyList<SchemaRef> schemas = target.Descriptor.SupportedSchemas;
                for (int s = 0; s < schemas.Count; s++)
                {
                    AddIndex(targetsBySchema, schemas[s].Id.Value, target);
                }

                IReadOnlyList<Id128> tags = target.Descriptor.Tags;
                for (int t = 0; t < tags.Count; t++)
                {
                    AddIndex(targetsByTag, tags[t], target);
                }
            }

            List<DerivationInstall> active = new List<DerivationInstall>();
            for (int i = 0; i < this.installs.Length; i++)
            {
                if (this.installs[i].IsActive)
                {
                    active.Add(this.installs[i]);
                }
            }

            activeInstalls = active.ToArray();
            rulesByStratum = new RuleSource[StratumCount][];
            for (int i = 0; i < StratumCount; i++)
            {
                rulesByStratum[i] = Array.Empty<RuleSource>();
            }

            List<RuleSource> outOfRange = new List<RuleSource>();
            List<RuleSource>[] buckets = new List<RuleSource>[StratumCount];
            for (int i = 0; i < activeInstalls.Length; i++)
            {
                DerivationInstall install = activeInstalls[i];
                IReadOnlyList<DerivationRule> rules = install.Manifest.DerivationRules;
                List<DerivationRule> sortedRules = new List<DerivationRule>(rules.Count);
                for (int r = 0; r < rules.Count; r++)
                {
                    sortedRules.Add(rules[r]);
                }

                sortedRules.Sort(CompareRules);
                for (int r = 1; r < sortedRules.Count; r++)
                {
                    if (sortedRules[r].RuleId.Equals(sortedRules[r - 1].RuleId))
                    {
                        throw new ArgumentException(
                            "One installation declares a rule identity once (P-004): " + sortedRules[r].RuleId.ToString(),
                            nameof(installs));
                    }
                }

                for (int r = 0; r < sortedRules.Count; r++)
                {
                    RuleSource source = new RuleSource(install, sortedRules[r]);
                    int stratum = sortedRules[r].OutputStratum;
                    if (stratum < 0 || stratum >= StratumCount)
                    {
                        // Bucketed separately: DerivationValidation rejects the declaration, and no stratum loop
                        // ever evaluates it (P-021).
                        outOfRange.Add(source);
                        continue;
                    }

                    if (buckets[stratum] == null)
                    {
                        buckets[stratum] = new List<RuleSource>();
                    }

                    buckets[stratum].Add(source);
                }
            }

            for (int s = 0; s < StratumCount; s++)
            {
                if (buckets[s] != null)
                {
                    rulesByStratum[s] = buckets[s].ToArray();
                }
            }

            outOfRangeRules = outOfRange.ToArray();
            selfAndDescendants = new Dictionary<Id128, ReachDomain>();
            descendantsOnly = new Dictionary<Id128, ReachDomain>();
            localOnly = new Dictionary<Id128, ReachDomain>();
            SnapshotHash = ContentHash.Compute(Encoding.UTF8.GetBytes(BuildCanonicalText()));
        }

        public WorldId World { get; }

        public CompositionRevision Revision { get; }

        public AssemblyEpoch Epoch { get; }

        /// <summary>The one world-level propagation mode (P-013).</summary>
        public PropagationMode Mode { get; }

        /// <summary>Content hash of the canonical input text: evidence for explanations and diagnostics.</summary>
        public ContentHash SnapshotHash { get; }

        public IReadOnlyList<DerivationScope> Scopes => scopes;

        /// <summary>Every installation of the snapshot, active or not; only active ones contribute (P-012).</summary>
        public IReadOnlyList<DerivationInstall> Installs => installs;

        public IReadOnlyList<DerivationTarget> Targets => targets;

        public CapabilityCatalog Contracts => contracts;

        public IReadOnlyList<DerivationRuleKeys> RuleKeys => ruleKeys;

        public IReadOnlyList<ProviderSelectionOverride> Overrides => overrides;

        /// <summary>Root scope: the only scope without a parent (P-010).</summary>
        public ScopeId Root => scopes.Length == 0 ? default(ScopeId) : FindRoot();

        /// <summary>Depth of a scope from the root, or -1 when this snapshot does not contain it.</summary>
        public int Depth(ScopeId scope) => scopeIndex.TryGetValue(scope.Value, out int index) ? DepthOf(index) : -1;

        public bool TryGetScope(ScopeId scope, out DerivationScope? found)
        {
            if (scopeIndex.TryGetValue(scope.Value, out int index))
            {
                found = scopes[index];
                return true;
            }

            found = null;
            return false;
        }

        public bool TryGetTarget(TargetId target, out DerivationTarget? found)
        {
            if (targetIndex.TryGetValue(target.Value, out int index))
            {
                found = targets[index];
                return true;
            }

            found = null;
            return false;
        }

        public bool TryGetInstall(PluginInstanceId instance, out DerivationInstall? found)
        {
            if (installIndex.TryGetValue(instance.Value, out int index))
            {
                found = installs[index];
                return true;
            }

            found = null;
            return false;
        }

        /// <summary>Declared ordering keys of one rule, or null when the rule declares none (P-019).</summary>
        public bool TryGetRuleKeys(RuleId rule, out DerivationRuleKeys? found)
        {
            if (ruleKeyIndex.TryGetValue(rule.Value, out int index))
            {
                found = ruleKeys[index];
                return true;
            }

            found = null;
            return false;
        }

        /// <summary>Default means the scope has no parent: the world root, or an unknown scope.</summary>
        public ScopeId ParentOf(ScopeId scope) =>
            scopeIndex.TryGetValue(scope.Value, out int index) ? scopes[index].Parent : default(ScopeId);

        /// <summary>Ancestor chain from the parent up to the root, nearest first; sibling scopes are never visited.</summary>
        public IReadOnlyList<ScopeId> Ancestors(ScopeId scope)
        {
            List<ScopeId> chain = new List<ScopeId>();
            ScopeId current = ParentOf(scope);
            while (!current.IsDefault)
            {
                chain.Add(current);
                current = ParentOf(current);
            }

            return chain.AsReadOnly();
        }

        /// <summary>Self plus ancestors, nearest first: the effective import/denial domain of one target scope.</summary>
        public IReadOnlyList<ScopeId> SelfAndAncestors(ScopeId scope)
        {
            List<ScopeId> chain = new List<ScopeId>();
            if (!scopeIndex.ContainsKey(scope.Value))
            {
                return chain;
            }

            chain.Add(scope);
            chain.AddRange(Ancestors(scope));
            return chain.AsReadOnly();
        }

        /// <summary>Target scope path from the scope up to the root; the P-026 explanation evidence.</summary>
        public IReadOnlyList<ScopeId> ScopePath(ScopeId scope) => SelfAndAncestors(scope);

        /// <summary>True when <paramref name="scope"/> is the target scope itself or one of its ancestors.</summary>
        public bool IsSelfOrAncestor(ScopeId scope, ScopeId candidate)
        {
            ScopeId current = candidate;
            while (!current.IsDefault)
            {
                if (current.Equals(scope))
                {
                    return true;
                }

                current = ParentOf(current);
            }

            return false;
        }

        /// <summary>Every descendant of a scope in canonical order; the scope itself is excluded (P-010).</summary>
        public IReadOnlyList<ScopeId> Descendants(ScopeId scope)
        {
            List<ScopeId> result = new List<ScopeId>();
            if (!scopeIndex.ContainsKey(scope.Value))
            {
                return result;
            }

            AddDescendants(scope, result);
            result.Sort(CanonicalDerivationOrder.CompareScopeIds);
            return result.AsReadOnly();
        }

        /// <summary>Self plus every descendant, canonical order: the `SelfAndDescendants` selector domain (P-013).</summary>
        public IReadOnlyList<ScopeId> SelfAndDescendants(ScopeId scope)
        {
            List<ScopeId> result = new List<ScopeId>();
            if (!scopeIndex.ContainsKey(scope.Value))
            {
                return result;
            }

            result.Add(scope);
            AddDescendants(scope, result);
            result.Sort(CanonicalDerivationOrder.CompareScopeIds);
            return result.AsReadOnly();
        }

        /// <summary>Targets owned directly by one scope, canonical order (may be empty).</summary>
        public IReadOnlyList<DerivationTarget> TargetsInScope(ScopeId scope) =>
            targetsByScope.TryGetValue(scope.Value, out List<DerivationTarget>? list)
                ? list
                : (IReadOnlyList<DerivationTarget>)Array.Empty<DerivationTarget>();

        /// <summary>Targets advertising one capability identity at any version, canonical order (P-015).</summary>
        public IReadOnlyList<DerivationTarget> TargetsDeclaringCapability(CapabilityId capability) =>
            targetsByCapability.TryGetValue(capability.Value, out List<DerivationTarget>? list)
                ? list
                : (IReadOnlyList<DerivationTarget>)Array.Empty<DerivationTarget>();

        /// <summary>Targets advertising one schema identity at any version, canonical order (P-015).</summary>
        public IReadOnlyList<DerivationTarget> TargetsDeclaringSchema(SchemaId schema) =>
            targetsBySchema.TryGetValue(schema.Value, out List<DerivationTarget>? list)
                ? list
                : (IReadOnlyList<DerivationTarget>)Array.Empty<DerivationTarget>();

        /// <summary>Targets advertising one immutable tag, canonical order (P-015).</summary>
        public IReadOnlyList<DerivationTarget> TargetsDeclaringTag(Id128 tag) =>
            targetsByTag.TryGetValue(tag, out List<DerivationTarget>? list)
                ? list
                : (IReadOnlyList<DerivationTarget>)Array.Empty<DerivationTarget>();

        /// <summary>Active installations in canonical order (P-012, P-046).</summary>
        public IReadOnlyList<DerivationInstall> ActiveInstalls => activeInstalls;

        /// <summary>Active rule sources declared with a stratum outside 0..31; validation rejects each one (P-021).</summary>
        internal IReadOnlyList<RuleSource> OutOfRangeRules => outOfRangeRules;

        /// <summary>Active rule sources bucketed at one stratum in canonical (installation, rule) order.</summary>
        internal IReadOnlyList<RuleSource> RulesAtStratum(int stratum) =>
            stratum < 0 || stratum >= StratumCount ? Array.Empty<RuleSource>() : rulesByStratum[stratum];


        /// Candidate targets of one rule in the stable indexed order of P-023: the reach domain intersected with
        /// the targets that advertise either an accepted selector contract or the rule's output capability.
        ///
        /// The selector half is what makes an ineligible target explainable at all: a target advertising a
        /// selector schema but failing the mode, isolation, exclusion or predicate checks must appear with a
        /// rejection reason (P-015 "get an `Ineligible` explanation"). The output-capability half covers a target
        /// that natively advertises the derived capability and so must be told why the rule refused it. Targets
        /// advertising neither are outside every rule's candidate population and stay untouched, which is exactly
        /// what P-015 requires for an unrecognized target.
        ///
        /// When the rule declares no selector at all, every target in the reach domain is a candidate; the walk
        /// then uses the scope index rather than a target scan, so a broad rule never rescans the whole world.
        /// </summary>
        internal IReadOnlyList<DerivationTarget> TargetsInReach(
            ScopeId source,
            PropagationReach reach,
            IReadOnlyList<SchemaRef> selectors,
            CapabilityId capability)
        {
            ReachDomain domain = Reach(source, reach);
            if (domain.TargetCount == 0)
            {
                return Array.Empty<DerivationTarget>();
            }

            Dictionary<Id128, DerivationTarget> population = new Dictionary<Id128, DerivationTarget>();
            for (int i = 0; i < selectors.Count; i++)
            {
                IReadOnlyList<DerivationTarget> declared = TargetsDeclaringSchema(selectors[i].Id);
                for (int t = 0; t < declared.Count; t++)
                {
                    if (domain.Contains(declared[t].Scope))
                    {
                        population[declared[t].Target.Value] = declared[t];
                    }
                }
            }

            if (selectors.Count > 0)
            {
                IReadOnlyList<DerivationTarget> advertising = TargetsDeclaringCapability(capability);
                for (int t = 0; t < advertising.Count; t++)
                {
                    if (domain.Contains(advertising[t].Scope))
                    {
                        population[advertising[t].Target.Value] = advertising[t];
                    }
                }
            }
            else
            {
                // No selector: the target population is the whole reach domain, taken from the scope index.
                IReadOnlyList<ScopeId> domainScopes = domain.Scopes;
                for (int s = 0; s < domainScopes.Count; s++)
                {
                    IReadOnlyList<DerivationTarget> inScope = TargetsInScope(domainScopes[s]);
                    for (int t = 0; t < inScope.Count; t++)
                    {
                        population[inScope[t].Target.Value] = inScope[t];
                    }
                }
            }

            if (population.Count == 0)
            {
                return Array.Empty<DerivationTarget>();
            }

            List<DerivationTarget> candidates = new List<DerivationTarget>(population.Count);
            foreach (KeyValuePair<Id128, DerivationTarget> pair in population)
            {
                candidates.Add(pair.Value);
            }

            candidates.Sort(CanonicalDerivationOrder.CompareTargets);
            return candidates.AsReadOnly();
        }

        /// <summary>The reach domain of one rule source (P-013 selector plus scope membership, P-010).</summary>
        internal ReachDomain Reach(ScopeId source, PropagationReach reach)
        {
            Dictionary<Id128, ReachDomain> cache;
            switch (reach)
            {
                case PropagationReach.DescendantsOnly:
                    cache = descendantsOnly;
                    break;
                case PropagationReach.LocalOnly:
                    cache = localOnly;
                    break;
                default:
                    cache = selfAndDescendants;
                    break;
            }

            if (cache.TryGetValue(source.Value, out ReachDomain? cached))
            {
                return cached;
            }

            IReadOnlyList<ScopeId> domainScopes;
            switch (reach)
            {
                case PropagationReach.DescendantsOnly:
                    domainScopes = Descendants(source);
                    break;
                case PropagationReach.LocalOnly:
                    domainScopes = scopeIndex.ContainsKey(source.Value)
                        ? new List<ScopeId>(1) { source }.AsReadOnly()
                        : (IReadOnlyList<ScopeId>)Array.Empty<ScopeId>();
                    break;
                default:
                    domainScopes = SelfAndDescendants(source);
                    break;
            }

            int population = 0;
            for (int i = 0; i < domainScopes.Count; i++)
            {
                population += TargetsInScope(domainScopes[i]).Count;
            }

            ReachDomain domain = new ReachDomain(domainScopes, population);
            cache.Add(source.Value, domain);
            return domain;
        }

        /// <summary>Canonical Projection of the input; two snapshots with equal text are the same derivation input.</summary>
        public string CanonicalText() => BuildCanonicalText();

        private void AddDescendants(ScopeId scope, List<ScopeId> result)
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                if (scopes[i].Parent.Equals(scope))
                {
                    result.Add(scopes[i].Scope);
                    AddDescendants(scopes[i].Scope, result);
                }
            }
        }

        private int DepthOf(int index)
        {
            int depth = 0;
            ScopeId current = scopes[index].Parent;
            while (!current.IsDefault)
            {
                depth++;
                if (!scopeIndex.TryGetValue(current.Value, out int parentIndex))
                {
                    return depth;
                }

                current = scopes[parentIndex].Parent;
            }

            return depth;
        }

        private ScopeId FindRoot()
        {
            for (int i = 0; i < scopes.Length; i++)
            {
                if (scopes[i].IsRoot)
                {
                    return scopes[i].Scope;
                }
            }

            return default(ScopeId);
        }

        private static void AddIndex(
            Dictionary<Id128, List<DerivationTarget>> index,
            Id128 key,
            DerivationTarget target)
        {
            if (!index.TryGetValue(key, out List<DerivationTarget>? list))
            {
                list = new List<DerivationTarget>();
                index.Add(key, list);
            }

            list.Add(target);
        }

        private static DerivationScope[] SortedScopes(IReadOnlyList<DerivationScope>? source)
        {
            IReadOnlyList<DerivationScope> sorted = CanonicalDerivationOrder.Sorted(source, CanonicalDerivationOrder.CompareScopes);
            DerivationScope[] array = new DerivationScope[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                array[i] = sorted[i];
            }

            return array;
        }

        private static DerivationInstall[] SortedInstalls(IReadOnlyList<DerivationInstall>? source)
        {
            IReadOnlyList<DerivationInstall> sorted = CanonicalDerivationOrder.Sorted(source, CanonicalDerivationOrder.CompareInstalls);
            DerivationInstall[] array = new DerivationInstall[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                array[i] = sorted[i];
            }

            return array;
        }

        private static DerivationTarget[] SortedTargets(IReadOnlyList<DerivationTarget>? source)
        {
            IReadOnlyList<DerivationTarget> sorted = CanonicalDerivationOrder.Sorted(source, CanonicalDerivationOrder.CompareTargets);
            DerivationTarget[] array = new DerivationTarget[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                array[i] = sorted[i];
            }

            return array;
        }

        private static DerivationRuleKeys[] SortedRuleKeys(IReadOnlyList<DerivationRuleKeys>? source)
        {
            IReadOnlyList<DerivationRuleKeys> sorted = CanonicalDerivationOrder.Sorted(source, CanonicalDerivationOrder.CompareRuleKeys);
            DerivationRuleKeys[] array = new DerivationRuleKeys[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                array[i] = sorted[i];
            }

            return array;
        }

        private static ProviderSelectionOverride[] SortedOverrides(IReadOnlyList<ProviderSelectionOverride>? source)
        {
            IReadOnlyList<ProviderSelectionOverride> sorted = CanonicalDerivationOrder.SortedUnique(
                source,
                CanonicalDerivationOrder.CompareOverrides,
                CanonicalDerivationOrder.OverridesEqual);
            ProviderSelectionOverride[] array = new ProviderSelectionOverride[sorted.Count];
            for (int i = 0; i < sorted.Count; i++)
            {
                ProviderSelectionOverride item = sorted[i];
                if (item.Capability.IsDefault || item.Provider.IsDefault)
                {
                    throw new ArgumentException(
                        "A selection override names a capability and a provider installation (P-018).",
                        nameof(source));
                }

                if (!item.HasScope && !item.HasTarget)
                {
                    throw new ArgumentException(
                        "A selection override is addressable: it names a scope or a target (P-018).",
                        nameof(source));
                }

                array[i] = item;
            }

            return array;
        }

        private static Dictionary<Id128, int> IndexScopes(DerivationScope[] source)
        {
            Dictionary<Id128, int> index = new Dictionary<Id128, int>();
            int roots = 0;
            for (int i = 0; i < source.Length; i++)
            {
                if (index.ContainsKey(source[i].Scope.Value))
                {
                    throw new ArgumentException("A scope identity appears once in one world (P-004).", nameof(source));
                }

                index.Add(source[i].Scope.Value, i);
                if (source[i].IsRoot)
                {
                    roots++;
                }
            }

            if (roots != 1)
            {
                throw new ArgumentException(
                    "A composition snapshot holds exactly one rooted scope tree (P-010); found " + roots + " roots.",
                    nameof(source));
            }

            for (int i = 0; i < source.Length; i++)
            {
                if (!source[i].IsRoot && !index.ContainsKey(source[i].Parent.Value))
                {
                    throw new ArgumentException(
                        "A scope's parent must be in the same snapshot (P-010): " + source[i].Scope.ToString(),
                        nameof(source));
                }

                int hops = 0;
                ScopeId current = source[i].Parent;
                while (!current.IsDefault)
                {
                    hops++;
                    if (hops > source.Length)
                    {
                        throw new ArgumentException(
                            "Scope membership is one acyclic tree (P-010); a cycle was found at " + source[i].Scope.ToString(),
                            nameof(source));
                    }

                    current = source[index[current.Value]].Parent;
                }
            }

            return index;
        }

        private static Dictionary<Id128, int> IndexTargets(DerivationTarget[] source)
        {
            Dictionary<Id128, int> index = new Dictionary<Id128, int>();
            for (int i = 0; i < source.Length; i++)
            {
                if (index.ContainsKey(source[i].Target.Value))
                {
                    throw new ArgumentException("A target identity appears once in one world (P-004).", nameof(source));
                }

                index.Add(source[i].Target.Value, i);
            }

            return index;
        }

        private static Dictionary<Id128, int> IndexInstalls(DerivationInstall[] source)
        {
            Dictionary<Id128, int> index = new Dictionary<Id128, int>();
            for (int i = 0; i < source.Length; i++)
            {
                if (index.ContainsKey(source[i].Instance.Value))
                {
                    throw new ArgumentException(
                        "An installation identity appears once in one world (P-004): " + source[i].Instance.ToString(),
                        nameof(source));
                }

                index.Add(source[i].Instance.Value, i);
            }

            return index;
        }

        private static Dictionary<Id128, int> IndexRuleKeys(DerivationRuleKeys[] source)
        {
            Dictionary<Id128, int> index = new Dictionary<Id128, int>();
            for (int i = 0; i < source.Length; i++)
            {
                if (index.ContainsKey(source[i].Rule.Value))
                {
                    throw new ArgumentException(
                        "A rule declares its ordering keys once (P-019): " + source[i].Rule.ToString(),
                        nameof(source));
                }

                index.Add(source[i].Rule.Value, i);
            }

            return index;
        }

        private static int CompareRules(DerivationRule left, DerivationRule right) =>
            left.RuleId.Value.CompareTo(right.RuleId.Value);

        private string BuildCanonicalText()
        {
            StringBuilder text = new StringBuilder();
            text.Append("world=").Append(World.Session.ToString()).Append('\n');
            text.Append("revision=").Append(Revision.Value).Append('\n');
            text.Append("epoch=").Append(Epoch.Value).Append('\n');
            text.Append("mode=").Append(Mode.ToString()).Append('\n');

            for (int i = 0; i < scopes.Length; i++)
            {
                DerivationScope scope = scopes[i];
                text.Append("scope=").Append(scope.Scope.ToString())
                    .Append(";parent=").Append(scope.Parent.ToString())
                    .Append(";capIso=").Append(DescribeIsolation(scope.CapabilityIsolation)).Append('\n');
                for (int e = 0; e < scope.Exclusions.Count; e++)
                {
                    text.Append("  scopeExclusion=").Append(DescribeExclusion(scope.Exclusions[e])).Append('\n');
                }

                for (int m = 0; m < scope.Imports.Count; m++)
                {
                    text.Append("  scopeImport=").Append(scope.Imports[m].CapabilityId.ToString())
                        .Append('/').Append(scope.Imports[m].ProviderInstallationId.ToString()).Append('\n');
                }
            }

            for (int i = 0; i < contracts.Count; i++)
            {
                CapabilityContract contract = contracts.Contracts[i];
                text.Append("contract=").Append(contract.Capability.ToString())
                    .Append(";stratum=").Append(contract.Stratum).Append('\n');
                for (int s = 0; s < contract.OutputSlots.Count; s++)
                {
                    text.Append("  slot=").Append(contract.OutputSlots[s].Slot.ToString())
                        .Append('/').Append(contract.OutputSlots[s].Schema.ToString()).Append('\n');
                }

                for (int s = 0; s < contract.SlotPolicies.Count; s++)
                {
                    SlotCompositionPolicy policy = contract.SlotPolicies[s];
                    text.Append("  policy=").Append(policy.Slot.ToString())
                        .Append('/').Append(policy.Policy.ToString())
                        .Append('/').Append(policy.Reducer.RegistrationKey.ToString())
                        .Append('@').Append(policy.Reducer.KeyVersion).Append('\n');
                }

                for (int s = 0; s < contract.IncompatibleCapabilities.Count; s++)
                {
                    text.Append("  incompatible=").Append(contract.IncompatibleCapabilities[s].ToString()).Append('\n');
                }
            }

            for (int i = 0; i < installs.Length; i++)
            {
                DerivationInstall install = installs[i];
                text.Append("install=").Append(install.Instance.ToString())
                    .Append(";pluginType=").Append(install.Record.PluginType.ToString())
                    .Append(";scope=").Append(install.Scope.ToString())
                    .Append(";state=").Append(install.State.ToString())
                    .Append(";priority=").Append(install.Record.Priority)
                    .Append(";configRevision=").Append(install.Record.ConfigRevision.Value)
                    .Append(";configHash=").Append(install.Record.ConfigHash.ToHex())
                    .Append(";generation=").Append(install.Record.Generation.Value)
                    .Append(";activation=").Append(install.Record.ActivationEpoch.Value).Append('\n');
                IReadOnlyList<DerivationRule> rules = install.Manifest.DerivationRules;
                for (int r = 0; r < rules.Count; r++)
                {
                    DerivationRule rule = rules[r];
                    text.Append("  rule=").Append(rule.RuleId.ToString())
                        .Append(";output=").Append(rule.OutputCapability.ToString())
                        .Append(";stratum=").Append(rule.OutputStratum)
                        .Append(";slots=").Append(rule.MaxOutputSlots)
                        .Append(";reach=").Append(rule.Reach.ToString())
                        .Append(";export=").Append(rule.ExportToDescendants ? "1" : "0")
                        .Append(";priority=").Append(rule.Priority)
                        .Append(";policy=").Append(rule.Policy.ToString())
                        .Append(";predicate=").Append(rule.StaticPredicate.RegistrationKey.ToString())
                        .Append(";payload=").Append(PayloadCodec.HashOf(rule.PayloadDefinition).ToHex()).Append('\n');
                    for (int s = 0; s < rule.SelectorContracts.Count; s++)
                    {
                        text.Append("    selector=").Append(rule.SelectorContracts[s].ToString()).Append('\n');
                    }

                    for (int s = 0; s < rule.InputCapabilities.Count; s++)
                    {
                        text.Append("    input=").Append(rule.InputCapabilities[s].ToString()).Append('\n');
                    }
                }
            }

            for (int i = 0; i < targets.Length; i++)
            {
                DerivationTarget target = targets[i];
                TargetDescriptor descriptor = target.Descriptor;
                text.Append("target=").Append(target.Target.ToString())
                    .Append(";scope=").Append(target.Scope.ToString())
                    .Append(";recipe=").Append(descriptor.Recipe.ToString())
                    .Append(";adapter=").Append(descriptor.AssetAdapter.ToString()).Append('\n');
                for (int s = 0; s < descriptor.SupportedSchemas.Count; s++)
                {
                    text.Append("  supportedSchema=").Append(descriptor.SupportedSchemas[s].ToString()).Append('\n');
                }

                for (int s = 0; s < descriptor.SupportedCapabilities.Count; s++)
                {
                    text.Append("  supportedCapability=").Append(descriptor.SupportedCapabilities[s].ToString()).Append('\n');
                }

                for (int s = 0; s < descriptor.Tags.Count; s++)
                {
                    text.Append("  tag=").Append(descriptor.Tags[s].ToString()).Append('\n');
                }

                for (int s = 0; s < descriptor.LocalPatches.Count; s++)
                {
                    text.Append("  patch=").Append(descriptor.LocalPatches[s].ToString()).Append('\n');
                }

                for (int s = 0; s < descriptor.Imports.Count; s++)
                {
                    text.Append("  import=").Append(descriptor.Imports[s].CapabilityId.ToString())
                        .Append('/').Append(descriptor.Imports[s].ProviderInstallationId.ToString()).Append('\n');
                }

                for (int s = 0; s < descriptor.OptIns.Count; s++)
                {
                    text.Append("  optIn=").Append(descriptor.OptIns[s].CapabilityId.ToString())
                        .Append('/').Append(descriptor.OptIns[s].ProviderInstallationId.ToString()).Append('\n');
                }

                for (int s = 0; s < descriptor.Exclusions.Count; s++)
                {
                    text.Append("  targetExclusion=").Append(DescribeExclusion(descriptor.Exclusions[s])).Append('\n');
                }
            }

            for (int i = 0; i < ruleKeys.Length; i++)
            {
                DerivationRuleKeys keys = ruleKeys[i];
                text.Append("ruleKeys=").Append(keys.Rule.ToString())
                    .Append('/').Append(keys.Capability.ToString())
                    .Append(";self=").Append(keys.SelfKey.ToString()).Append('\n');
                for (int e = 0; e < keys.Before.Count; e++)
                {
                    text.Append("  before=").Append(keys.Before[e].Key.ToString())
                        .Append(keys.Before[e].Required ? "!" : "?").Append('\n');
                }

                for (int e = 0; e < keys.After.Count; e++)
                {
                    text.Append("  after=").Append(keys.After[e].Key.ToString())
                        .Append(keys.After[e].Required ? "!" : "?").Append('\n');
                }
            }

            for (int i = 0; i < overrides.Length; i++)
            {
                ProviderSelectionOverride item = overrides[i];
                text.Append("override=").Append(item.Capability.ToString())
                    .Append("->").Append(item.Provider.ToString())
                    .Append(";scope=").Append(item.AtScope.ToString())
                    .Append(";target=").Append(item.AtTarget.ToString())
                    .Append(";subtree=").Append(item.AppliesToSubtree ? "1" : "0").Append('\n');
            }

            return text.ToString();
        }

        private static string DescribeIsolation(IsolationSet isolation)
        {
            if (isolation.AllContracts)
            {
                return "*";
            }

            IReadOnlyList<Id128> contracts = CanonicalDerivationOrder.Sorted(isolation.Contracts, Id128Codec.CompareBigEndian);
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < contracts.Count; i++)
            {
                if (i > 0)
                {
                    text.Append(',');
                }

                text.Append(contracts[i].ToString());
            }

            return text.ToString();
        }

        private static string DescribeExclusion(ExclusionRule exclusion) =>
            exclusion.Kind.ToString()
            + ':' + exclusion.TargetId.ToString()
            + "@scope=" + exclusion.AtScope.ToString()
            + ";target=" + exclusion.AtTarget.ToString()
            + ";subtree=" + (exclusion.AppliesToSubtree ? "1" : "0");
    }
}
