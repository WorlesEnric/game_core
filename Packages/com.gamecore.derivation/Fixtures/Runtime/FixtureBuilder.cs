// GameCore.Derivation fixtures — a declarative builder for fixture compositions (GC-006).
//
// The two reference compositions of 07 (chapter quest, card market) and the adversarial policy fixtures all need
// the same few things: scopes with isolation/exclusions/imports, targets with descriptors, capability contracts
// with slots and policies, installations with rules, and ordering keys. This builder keeps those declarations
// readable and keeps every identity derived from a documented stable name, so a later wave can rebuild the same
// composition object-for-object instead of copying literals.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Derivation.Fixtures
{
    /// <summary>Declarative builder of one fixture composition.</summary>
    public sealed class FixtureBuilder
    {
        private readonly WorldId world;
        private readonly List<DerivationScope> scopes = new List<DerivationScope>();
        private readonly List<DerivationInstall> installs = new List<DerivationInstall>();
        private readonly List<DerivationTarget> targets = new List<DerivationTarget>();
        private readonly List<CapabilityContract> contracts = new List<CapabilityContract>();
        private readonly List<DerivationRuleKeys> ruleKeys = new List<DerivationRuleKeys>();
        private readonly List<ProviderSelectionOverride> overrides = new List<ProviderSelectionOverride>();

        public FixtureBuilder(WorldId world)
        {
            this.world = world;
        }

        /// <summary>One scope with optional capability isolation, exclusions and Conservative imports (P-010, P-016, P-013).</summary>
        public FixtureBuilder Scope(
            string name,
            string? parent,
            bool isolateAllCapabilities = false,
            IReadOnlyList<string>? isolatedCapabilities = null,
            IReadOnlyList<ExclusionRule>? exclusions = null,
            IReadOnlyList<string>? importProviderPairs = null)
        {
            IsolationSet isolation;
            if (isolateAllCapabilities)
            {
                isolation = new IsolationSet(true, null);
            }
            else if (isolatedCapabilities != null && isolatedCapabilities.Count > 0)
            {
                List<Id128> ids = new List<Id128>(isolatedCapabilities.Count);
                for (int i = 0; i < isolatedCapabilities.Count; i++)
                {
                    ids.Add(FixtureIds.Capability(isolatedCapabilities[i]).Value);
                }

                isolation = new IsolationSet(false, ids);
            }
            else
            {
                isolation = new IsolationSet(false, null);
            }

            List<CapabilityImport>? imports = null;
            if (importProviderPairs != null)
            {
                if (importProviderPairs.Count % 2 != 0)
                {
                    throw new ArgumentException(
                        "Imports are capability/provider name pairs.", nameof(importProviderPairs));
                }

                imports = new List<CapabilityImport>(importProviderPairs.Count / 2);
                for (int i = 0; i < importProviderPairs.Count; i += 2)
                {
                    imports.Add(new CapabilityImport(
                        FixtureIds.Capability(importProviderPairs[i]),
                        FixtureIds.Installation(importProviderPairs[i + 1])));
                }
            }

            scopes.Add(new DerivationScope(
                FixtureIds.Scope(name),
                parent == null ? default(ScopeId) : FixtureIds.Scope(parent),
                isolation,
                exclusions,
                imports));
            return this;
        }

        /// <summary>One live target with its single owner scope and its immutable recipe descriptor (P-010, P-015).</summary>
        public FixtureBuilder Target(
            string name,
            string scope,
            string recipeSchema,
            uint recipeSchemaVersion = 1U,
            IReadOnlyList<string>? tags = null,
            IReadOnlyList<string>? nativelyProvidedCapabilities = null,
            IReadOnlyList<ExclusionRule>? exclusions = null,
            IReadOnlyList<string>? importProviderPairs = null,
            IReadOnlyList<string>? optInProviderPairs = null,
            string? assetAdapter = null)
        {
            List<SchemaRef> schemas = new List<SchemaRef>
            {
                FixtureIds.SchemaRef(recipeSchema, recipeSchemaVersion),
            };

            List<CapabilityRef> provided = new List<CapabilityRef>();
            if (nativelyProvidedCapabilities != null)
            {
                for (int i = 0; i < nativelyProvidedCapabilities.Count; i++)
                {
                    provided.Add(FixtureIds.CapabilityRef(nativelyProvidedCapabilities[i]));
                }
            }

            List<Id128> tagIds = new List<Id128>();
            if (tags != null)
            {
                for (int i = 0; i < tags.Count; i++)
                {
                    tagIds.Add(FixtureIds.Id(tags[i]));
                }
            }

            List<CapabilityImport>? imports = ToImports(importProviderPairs);
            List<TargetOptIn>? optIns = null;
            if (optInProviderPairs != null)
            {
                if (optInProviderPairs.Count % 2 != 0)
                {
                    throw new ArgumentException(
                        "Opt-ins are capability/provider name pairs.", nameof(optInProviderPairs));
                }

                optIns = new List<TargetOptIn>(optInProviderPairs.Count / 2);
                for (int i = 0; i < optInProviderPairs.Count; i += 2)
                {
                    optIns.Add(new TargetOptIn(
                        FixtureIds.Installation(optInProviderPairs[i + 1]),
                        FixtureIds.Capability(optInProviderPairs[i])));
                }
            }

            AssetAdapterDescriptor adapter = assetAdapter == null
                ? default(AssetAdapterDescriptor)
                : new AssetAdapterDescriptor(FixtureIds.Id(assetAdapter), 1U);

            targets.Add(new DerivationTarget(
                FixtureIds.Target(name),
                FixtureIds.Scope(scope),
                new TargetDescriptor(
                    FixtureIds.Recipe(recipeSchema + ".definition", recipeSchema, recipeSchemaVersion),
                    schemas,
                    provided,
                    tagIds,
                    adapter,
                    null,
                    imports,
                    optIns,
                    exclusions)));
            return this;
        }

        /// <summary>One capability contract with its slots, per-slot policy and reducer key (P-017, P-019).</summary>
        public FixtureBuilder Contract(
            string capability,
            int stratum,
            IReadOnlyList<FixtureSlot> slots,
            uint version = 1U,
            IReadOnlyList<string>? incompatibleCapabilities = null)
        {
            if (slots == null || slots.Count == 0)
            {
                throw new ArgumentException("A capability contract declares at least one output slot (P-017).", nameof(slots));
            }

            List<OutputSlotSchema> schemas = new List<OutputSlotSchema>(slots.Count);
            List<SlotCompositionPolicy> policies = new List<SlotCompositionPolicy>(slots.Count);
            for (int i = 0; i < slots.Count; i++)
            {
                FixtureSlot slot = slots[i];
                SlotId slotId = FixtureIds.Slot(capability + ".slot-" + i);
                schemas.Add(new OutputSlotSchema(slotId, FixtureIds.SchemaRef(slot.Schema, slot.SchemaVersion)));
                policies.Add(new SlotCompositionPolicy(slotId, slot.Policy, slot.Reducer));
            }

            IReadOnlyList<CapabilityId>? incompatible = null;
            if (incompatibleCapabilities != null)
            {
                List<CapabilityId> list = new List<CapabilityId>(incompatibleCapabilities.Count);
                for (int i = 0; i < incompatibleCapabilities.Count; i++)
                {
                    list.Add(FixtureIds.Capability(incompatibleCapabilities[i]));
                }

                incompatible = list;
            }

            contracts.Add(new CapabilityContract(
                FixtureIds.CapabilityRef(capability, version),
                stratum,
                schemas,
                policies,
                incompatible));
            return this;
        }

        /// <summary>One installation with its manifest rules, at an explicit priority (P-009, P-046).</summary>
        public FixtureBuilder Install(
            string name,
            string scope,
            int priority,
            IReadOnlyList<DerivationRule> rules,
            InstallationState state = InstallationState.Active,
            uint activationEpoch = 1U,
            ulong generation = 1UL)
        {
            PluginManifest manifest = new PluginManifest(
                FixtureIds.PluginType(name + ".type"),
                "1.0.0",
                new ContentHash(new byte[ContentHash.SizeInBytes]),
                new SupportedProtocolRange(1, 0, 0),
                null,
                FixtureIds.SchemaRef("gamecore.config"),
                FixtureIds.Key(name + ".factory"),
                null,
                null,
                null,
                rules,
                null,
                null,
                null,
                null,
                null);

            installs.Add(new DerivationInstall(
                new InstallRecord(
                    FixtureIds.Instance(name),
                    manifest.PluginTypeId,
                    FixtureIds.Scope(scope),
                    DefinitionRevision.First,
                    new ContentHash(new byte[ContentHash.SizeInBytes]),
                    priority,
                    new InstallationGeneration(generation),
                    new ActivationEpoch(activationEpoch)),
                state,
                manifest));

            return this;
        }

        /// <summary>
        /// One declared ordering key set for an <c>Ordered</c> rule (P-019). A rule declares its keys once, so a
        /// second call for the same rule replaces the first: the alternative would be a duplicate declaration,
        /// which the snapshot rejects rather than resolving.
        /// </summary>
        public FixtureBuilder RuleKey(
            string rule,
            string capability,
            string? selfKey,
            IReadOnlyList<FixtureOrderEdge>? before = null,
            IReadOnlyList<FixtureOrderEdge>? after = null)
        {
            List<OrderKeyEdge> beforeEdges = ToEdges(before);
            List<OrderKeyEdge> afterEdges = ToEdges(after);
            DerivationRuleKeys keys = new DerivationRuleKeys(
                FixtureIds.Rule(rule),
                FixtureIds.Capability(capability),
                selfKey == null ? Id128.Zero : FixtureIds.Id(selfKey),
                beforeEdges,
                afterEdges);

            for (int i = 0; i < ruleKeys.Count; i++)
            {
                if (ruleKeys[i].Rule.Equals(keys.Rule))
                {
                    ruleKeys[i] = keys;
                    return this;
                }
            }

            ruleKeys.Add(keys);
            return this;
        }

        /// <summary>Replaces one rule's immutable payload definition: the reconfigure case (P-017, REF-C04).</summary>
        public FixtureBuilder ReplaceRulePayload(string installName, string ruleName, FrozenPayload payload)
        {
            RuleId ruleId = FixtureIds.Rule(ruleName);
            for (int i = 0; i < installs.Count; i++)
            {
                if (!installs[i].Instance.Equals(FixtureIds.Instance(installName)))
                {
                    continue;
                }

                DerivationInstall install = installs[i];
                List<DerivationRule> rules = new List<DerivationRule>();
                IReadOnlyList<DerivationRule> declared = install.Manifest.DerivationRules;
                for (int r = 0; r < declared.Count; r++)
                {
                    DerivationRule rule = declared[r];
                    if (!rule.RuleId.Equals(ruleId))
                    {
                        rules.Add(rule);
                        continue;
                    }

                    rules.Add(RuleFor(
                        rule.RuleId,
                        rule.OutputCapability,
                        rule.OutputStratum,
                        rule.MaxOutputSlots,
                        rule.SelectorContracts,
                        rule.StaticPredicate,
                        rule.InputCapabilities,
                        rule.Reach,
                        rule.ExportToDescendants,
                        rule.Priority,
                        rule.Policy,
                        payload));
                }

                installs[i] = new DerivationInstall(
                    install.Record,
                    install.State,
                    ReplaceRules(install.Manifest, rules));
                break;
            }

            return this;
        }

        /// <summary>Adds a natively advertised capability to an existing target descriptor (P-015).</summary>
        public FixtureBuilder AddTargetCapability(string targetName, string capability) =>
            EditDescriptor(targetName, descriptor => new TargetDescriptor(
                descriptor.Recipe,
                descriptor.SupportedSchemas,
                Append(descriptor.SupportedCapabilities, FixtureIds.CapabilityRef(capability)),
                descriptor.Tags,
                descriptor.AssetAdapter,
                descriptor.LocalPatches,
                descriptor.Imports,
                descriptor.OptIns,
                descriptor.Exclusions));

        /// <summary>Adds one explicit Conservative capability import to an existing target descriptor (P-013).</summary>
        public FixtureBuilder AddTargetImport(string targetName, string capability, string provider) =>
            EditDescriptor(targetName, descriptor => new TargetDescriptor(
                descriptor.Recipe,
                descriptor.SupportedSchemas,
                descriptor.SupportedCapabilities,
                descriptor.Tags,
                descriptor.AssetAdapter,
                descriptor.LocalPatches,
                Append(descriptor.Imports, new CapabilityImport(FixtureIds.Capability(capability), FixtureIds.Installation(provider))),
                descriptor.OptIns,
                descriptor.Exclusions));

        /// <summary>Adds one complete target opt-in to an existing target descriptor (P-013).</summary>
        public FixtureBuilder AddTargetOptIn(string targetName, string capability, string provider) =>
            EditDescriptor(targetName, descriptor => new TargetDescriptor(
                descriptor.Recipe,
                descriptor.SupportedSchemas,
                descriptor.SupportedCapabilities,
                descriptor.Tags,
                descriptor.AssetAdapter,
                descriptor.LocalPatches,
                descriptor.Imports,
                Append(descriptor.OptIns, new TargetOptIn(FixtureIds.Installation(provider), FixtureIds.Capability(capability))),
                descriptor.Exclusions));

        /// <summary>Replaces one target's descriptor while keeping its identity and owner scope (P-015).</summary>
        public FixtureBuilder EditDescriptor(string targetName, Func<TargetDescriptor, TargetDescriptor> edit)
        {
            if (edit == null)
            {
                throw new ArgumentNullException(nameof(edit));
            }

            TargetId target = FixtureIds.Target(targetName);
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].Target.Equals(target))
                {
                    continue;
                }

                targets[i] = new DerivationTarget(targets[i].Target, targets[i].Scope, edit(targets[i].Descriptor));
                break;
            }

            return this;
        }

        private static List<T> Append<T>(IReadOnlyList<T>? source, T item)
        {
            List<T> combined = new List<T>();
            if (source != null)
            {
                for (int i = 0; i < source.Count; i++)
                {
                    combined.Add(source[i]);
                }
            }

            combined.Add(item);
            return combined;
        }

        private static PluginManifest ReplaceRules(PluginManifest manifest, IReadOnlyList<DerivationRule> rules) =>
            new PluginManifest(
                manifest.PluginTypeId,
                manifest.PackageVersion,
                manifest.PackageContentHash,
                manifest.ProtocolRange,
                manifest.RequiredFeatureIds,
                manifest.ConfigSchema,
                manifest.FactoryKey,
                manifest.ServiceExports,
                manifest.ServiceDependencies,
                manifest.CapabilityContracts,
                rules,
                manifest.TargetDescriptors,
                manifest.StateSlots,
                manifest.Stages,
                manifest.Buffers,
                manifest.Resources);

        /// <summary>
        /// One derivation rule from an explicit rule identity and capability reference. The name-based
        /// <see cref="Rule"/> overload is the declarative form; this one is what a fixture uses when it must keep
        /// an existing identity while changing something else, such as the immutable payload of a reconfigure.
        /// </summary>
        public static DerivationRule RuleFor(
            RuleId ruleId,
            CapabilityRef outputCapability,
            int stratum,
            uint maxOutputSlots,
            IReadOnlyList<SchemaRef>? selectors,
            FactoryKey predicate,
            IReadOnlyList<CapabilityRef>? inputs,
            PropagationReach reach,
            bool exportToDescendants,
            int priority,
            CompositionPolicy policy,
            FrozenPayload payload) =>
            new DerivationRule(
                ruleId,
                outputCapability,
                stratum,
                maxOutputSlots,
                selectors,
                predicate,
                inputs,
                reach,
                exportToDescendants,
                priority,
                policy,
                payload);

        /// <summary>One versioned provider selection override (P-018).</summary>
        public FixtureBuilder Select(
            string capability,
            string provider,
            string? atScope = null,
            string? atTarget = null,
            bool subtree = false)
        {
            overrides.Add(new ProviderSelectionOverride(
                atScope == null ? default(ScopeId) : FixtureIds.Scope(atScope),
                atTarget == null ? default(TargetId) : FixtureIds.Target(atTarget),
                FixtureIds.Capability(capability),
                FixtureIds.Installation(provider),
                subtree));
            return this;
        }

        /// <summary>Replaces the target descriptors of one target with descriptors carrying a different exclusion set.</summary>
        public FixtureBuilder ReplaceTarget(
            string name,
            string scope,
            string recipeSchema,
            IReadOnlyList<SchemaRef> supportedSchemas,
            IReadOnlyList<ExclusionRule>? exclusions,
            IReadOnlyList<string>? optInProviderPairs = null)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].Target.Equals(FixtureIds.Target(name)))
                {
                    continue;
                }

                List<TargetOptIn>? optIns = null;
                if (optInProviderPairs != null)
                {
                    optIns = new List<TargetOptIn>(optInProviderPairs.Count / 2);
                    for (int j = 0; j < optInProviderPairs.Count; j += 2)
                    {
                        optIns.Add(new TargetOptIn(
                            FixtureIds.Installation(optInProviderPairs[j + 1]),
                            FixtureIds.Capability(optInProviderPairs[j])));
                    }
                }

                targets[i] = new DerivationTarget(
                    FixtureIds.Target(name),
                    FixtureIds.Scope(scope),
                    new TargetDescriptor(
                        FixtureIds.Recipe(recipeSchema + ".definition", recipeSchema),
                        supportedSchemas,
                        null,
                        null,
                        default(AssetAdapterDescriptor),
                        null,
                        null,
                        optIns,
                        exclusions));
                break;
            }

            return this;
        }

        /// <summary>
        /// Moves one target to another scope: the P-025/REF-C05/REF-N04 reparent case. A target's owner scope is
        /// composition data, not a Transform position, so the descriptor and every other part of the target stay
        /// exactly as they were.
        /// </summary>
        public FixtureBuilder MoveTarget(string name, string newScope)
        {
            TargetId target = FixtureIds.Target(name);
            ScopeId scope = FixtureIds.Scope(newScope);
            for (int i = 0; i < targets.Count; i++)
            {
                if (!targets[i].Target.Equals(target))
                {
                    continue;
                }

                targets[i] = new DerivationTarget(targets[i].Target, scope, targets[i].Descriptor);
                break;
            }

            return this;
        }

        /// <summary>
        /// Adds one exclusion to an already-declared scope (P-016). A scope record is immutable, so this replaces
        /// it; the alternative — declaring the scope twice — would be a duplicate identity, not a variant.
        /// </summary>
        public FixtureBuilder AddScopeExclusion(string scopeName, ExclusionRule exclusion)
        {
            ScopeId scope = FixtureIds.Scope(scopeName);
            for (int i = 0; i < scopes.Count; i++)
            {
                if (!scopes[i].Scope.Equals(scope))
                {
                    continue;
                }

                List<ExclusionRule> combined = new List<ExclusionRule>(scopes[i].Exclusions) { exclusion };
                scopes[i] = new DerivationScope(
                    scopes[i].Scope,
                    scopes[i].Parent,
                    scopes[i].CapabilityIsolation,
                    combined,
                    scopes[i].Imports);
                break;
            }

            return this;
        }

        /// <summary>Replaces one scope's capability isolation set with a named-contract set (P-016).</summary>
        public FixtureBuilder AddScopeIsolation(string scopeName, string capability)
        {
            ScopeId scope = FixtureIds.Scope(scopeName);
            for (int i = 0; i < scopes.Count; i++)
            {
                if (!scopes[i].Scope.Equals(scope))
                {
                    continue;
                }

                List<Id128> contracts = new List<Id128>();
                IReadOnlyList<Id128> existing = scopes[i].CapabilityIsolation.Contracts;
                for (int c = 0; c < existing.Count; c++)
                {
                    contracts.Add(existing[c]);
                }

                contracts.Add(FixtureIds.Capability(capability).Value);
                scopes[i] = new DerivationScope(
                    scopes[i].Scope,
                    scopes[i].Parent,
                    new IsolationSet(false, contracts),
                    scopes[i].Exclusions,
                    scopes[i].Imports);
                break;
            }

            return this;
        }

        /// <summary>Adds one Conservative import to an already-declared scope (P-013).</summary>
        public FixtureBuilder AddScopeImport(string scopeName, string capability, string provider)
        {
            ScopeId scope = FixtureIds.Scope(scopeName);
            for (int i = 0; i < scopes.Count; i++)
            {
                if (!scopes[i].Scope.Equals(scope))
                {
                    continue;
                }

                List<CapabilityImport> combined = new List<CapabilityImport>(scopes[i].Imports)
                {
                    new CapabilityImport(FixtureIds.Capability(capability), FixtureIds.Installation(provider)),
                };

                scopes[i] = new DerivationScope(
                    scopes[i].Scope,
                    scopes[i].Parent,
                    scopes[i].CapabilityIsolation,
                    scopes[i].Exclusions,
                    combined);
                break;
            }

            return this;
        }

        /// <summary>Replaces one installation's lifecycle state, e.g. to model a retracted contribution (P-012).</summary>
        public FixtureBuilder ReplaceInstallState(string name, InstallationState state)
        {
            for (int i = 0; i < installs.Count; i++)
            {
                if (!installs[i].Instance.Equals(FixtureIds.Instance(name)))
                {
                    continue;
                }

                installs[i] = new DerivationInstall(installs[i].Record, state, installs[i].Manifest);
                break;
            }

            return this;
        }

        /// <summary>Removes one installation: the unmount case of REF-C04 and REF-N03.</summary>
        public FixtureBuilder RemoveInstall(string name)
        {
            for (int i = 0; i < installs.Count; i++)
            {
                if (installs[i].Instance.Equals(FixtureIds.Instance(name)))
                {
                    installs.RemoveAt(i);
                    break;
                }
            }

            return this;
        }

        /// <summary>Removes one target: the retire case used to check retraction of its support (P-017).</summary>
        public FixtureBuilder RemoveTarget(string name)
        {
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].Target.Equals(FixtureIds.Target(name)))
                {
                    targets.RemoveAt(i);
                    break;
                }
            }

            return this;
        }

        /// <summary>Removes one override: removing a provider must reveal the next-ranked candidate (P-018).</summary>
        public FixtureBuilder ClearOverrides()
        {
            overrides.Clear();
            return this;
        }

        /// <summary>The declared parts, ready to be permuted and turned into a snapshot (P-008).</summary>
        public FixtureComposition Build(
            PropagationMode mode,
            CompositionRevision revision,
            AssemblyEpoch epoch) =>
            new FixtureComposition(
                world,
                revision,
                epoch,
                mode,
                scopes,
                installs,
                targets,
                contracts,
                ruleKeys,
                overrides);

        /// <summary>One derivation rule with every declaration explicit (P-015, P-017, P-019, P-021).</summary>
        public static DerivationRule Rule(
            string name,
            string outputCapability,
            int stratum,
            uint maxOutputSlots,
            IReadOnlyList<SchemaRef>? selectors,
            FactoryKey predicate,
            IReadOnlyList<CapabilityRef>? inputs,
            PropagationReach reach,
            bool exportToDescendants,
            int priority,
            CompositionPolicy policy,
            FrozenPayload payload,
            uint outputVersion = 1U) =>
            new DerivationRule(
                FixtureIds.Rule(name),
                FixtureIds.CapabilityRef(outputCapability, outputVersion),
                stratum,
                maxOutputSlots,
                selectors,
                predicate,
                inputs,
                reach,
                exportToDescendants,
                priority,
                policy,
                payload);

        /// <summary>One selector schema reference list for a rule that selects a recipe (P-015).</summary>
        public static IReadOnlyList<SchemaRef> Selector(string recipeSchema, uint version = 1U) =>
            new List<SchemaRef> { FixtureIds.SchemaRef(recipeSchema, version) };

        /// <summary>One declared lower-stratum input list for a rule (P-021).</summary>
        public static IReadOnlyList<CapabilityRef> Inputs(params string[] capabilities)
        {
            List<CapabilityRef> refs = new List<CapabilityRef>(capabilities.Length);
            for (int i = 0; i < capabilities.Length; i++)
            {
                refs.Add(FixtureIds.CapabilityRef(capabilities[i]));
            }

            return refs;
        }

        private static List<CapabilityImport>? ToImports(IReadOnlyList<string>? pairs)
        {
            if (pairs == null)
            {
                return null;
            }

            if (pairs.Count % 2 != 0)
            {
                throw new ArgumentException("Imports are capability/provider name pairs.", nameof(pairs));
            }

            List<CapabilityImport> imports = new List<CapabilityImport>(pairs.Count / 2);
            for (int i = 0; i < pairs.Count; i += 2)
            {
                imports.Add(new CapabilityImport(FixtureIds.Capability(pairs[i]), FixtureIds.Installation(pairs[i + 1])));
            }

            return imports;
        }

        private static List<OrderKeyEdge> ToEdges(IReadOnlyList<FixtureOrderEdge>? edges)
        {
            if (edges == null)
            {
                return null!;
            }

            List<OrderKeyEdge> result = new List<OrderKeyEdge>(edges.Count);
            for (int i = 0; i < edges.Count; i++)
            {
                result.Add(new OrderKeyEdge(FixtureIds.Id(edges[i].Key), edges[i].Required));
            }

            return result;
        }
    }

    /// <summary>One declared output slot of a fixture capability contract.</summary>
    public readonly struct FixtureSlot
    {
        public readonly string Schema;
        public readonly uint SchemaVersion;
        public readonly CompositionPolicy Policy;
        public readonly FactoryKey Reducer;

        public FixtureSlot(string schema, CompositionPolicy policy, uint schemaVersion = 1U, FactoryKey reducer = default(FactoryKey))
        {
            Schema = schema;
            SchemaVersion = schemaVersion;
            Policy = policy;
            Reducer = reducer;
        }
    }

    /// <summary>One declared ordering edge of a fixture <c>Ordered</c> rule.</summary>
    public readonly struct FixtureOrderEdge
    {
        public readonly string Key;
        public readonly bool Required;

        public FixtureOrderEdge(string key, bool required)
        {
            Key = key;
            Required = required;
        }
    }
}
