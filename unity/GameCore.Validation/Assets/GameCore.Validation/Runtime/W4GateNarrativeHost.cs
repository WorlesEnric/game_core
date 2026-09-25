// GameCore.Validation.ProbeHost — the W4 integration-gate narrative family adapter.
//
// `W4GateFamily.cs` defines what one genre must declare for the Wave 4 exit gate; `Gc013NarrativeHost.cs` already
// implements the half GC-013's own sequence needed (`IGc013Family`: catalog, scope tree, live targets, the provider
// to derive from, the branch to move, the two mode edits, the neutral scope creations). This file is the other half:
// `Gc013NarrativeHost.NarrativeFamily` is a partial type, and the part declared here adds exactly the two surfaces
// `IGc013Family` does not have —
//
//   * the lifecycle half (P-011, P-012, P-046, P-047, P-048): the installation the gate suspends and resumes (the
//     family's own chapter-one provider, mounted from `declarations[0]`), the required-service pair whose provider
//     loss makes its consumer wait and whose compatible return resumes it, the fourth installation the sequence
//     unloads through the P-048 reverse-acquisition order, and the mount/suspend/resume/unmount payload builders.
//     Nothing here models a service relationship: each installation is a real `PluginManifest` with a real
//     `ServiceExport`/`ServiceDependency`, mounted by the real control lane and resolved by the real `ServiceResolver`;
//   * the state-policy half (P-020, P-025, P-029, P-032, P-033, P-034): one mounted provider whose five declared
//     state slots carry the four declared last-support policies plus the explicit, manifest-supported reset, the
//     initialization registry a declared reset reads its value from, the cases to seed, and the registered
//     migrations the revision already declares.
//
// Three deliberate decisions, all forced by the existing declarations rather than chosen:
//
//   * the five policy slots all declare `NarrativeKeys.DialogueOwner`, and each gets its own domain, its own
//     physical layout key and its own field key. P-033 allows one physical owner and one field mapping per layout,
//     and `ComponentOwnershipMap.TryBuild` refuses two layouts of one component schema under different keys, so
//     "five slots, five domains, five layouts" is the only shape in which five independently-policied slots are
//     storable. The domains are new identities, so nothing the narrative revision already stores is touched;
//   * `LastSupportPolicy` has no `Preserve` member (05 section 3 declares `RemoveDerived`, `PreserveDormant` and
//     `TransferTo` only) and `SlotPolicyValidator.ValidateDeclaration` rejects anything else. P-032 makes `Preserve`
//     the compatibility default rather than a declaration, so the "preserve" case's slot is declared
//     `PreserveDormant`: an explicit `Preserve` request is legal against any declaration, and the declaration stays
//     a declaration the validator accepts. That the slot is *dormant-capable* is what makes a preserve/preserve-
//     dormant pair of cases meaningfully different;
//   * the state-policy provider is mounted at `NarrativeKeys.RootScope`, not at a chapter scope. `PolicyTargets` is
//     `Mara` (at `village`, beneath `chapter-one`) and `QuestLedger` (at the world root), and the state-policy half
//     requires the provider's scope to be an ancestor of every target the policy pass acts on. The world root is the
//     only declared scope that is an ancestor of both.
//
// The two engine-free entry points mirror `Gc013NarrativeHost.RunGeneratedCatalog`/`RunFixtureCatalog` exactly, so
// the same runner drives both catalogs of this genre; the only difference is that each run's declaration list also
// carries the state-policy provider, because its slots must belong to the compiled ownership surface of the revision
// the policy pass validates against (P-009, P-032).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Planning;
using GameCore.Planning.StatePolicies;
using GameCore.Rules.Narrative;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.Generated;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// The narrative half of the W4 integration gate: the two entry points the runner is invoked through, the stable
    /// identities of the gate's own lifecycle and state-policy declarations, and those declarations themselves.
    /// </summary>
    public static class W4GateNarrativeHost
    {
        /// <summary>Family label every observation name of this family carries.</summary>
        public const string Label = "narrative";

        /// <summary>
        /// Reason the reset slot's manifest records and the reset case's proposal carries (P-032). The two are
        /// independent fields by design — the declaration says why the slot may ever be reset, the proposal says why
        /// this publication does — and this run keeps them identical so the step detail can name one string.
        /// </summary>
        public const string ResetReason = "w4-gate narrative content repair";

        /// <summary>
        /// Value the reset slot's declared initialization policy resolves to (P-032). It is deliberately non-zero and
        /// different from every seeded case value, so "the reset wrote the declared initialization value" cannot be
        /// satisfied by an implicit zero initialization.
        /// </summary>
        public const int ResetSlotInitialValue = 5;

        /// <summary>Value the preserve-family slots' declared initialization policies resolve to (P-032).</summary>
        public const int PreserveSlotInitialValue = 0;

        // ------------------------------------------------------------------ the state-policy slots (GC-015)

        /// <summary>Slot of the explicit `Preserve` case: the value must not move (P-020, P-032).</summary>
        public static readonly SlotId PreserveSlot = NarrativeIds.Slot("gc.w4gate.narrative.policy.preserve");

        /// <summary>Slot of the `PreserveDormant` case: the value is retained with no active writer (P-032).</summary>
        public static readonly SlotId DormantSlot = NarrativeIds.Slot("gc.w4gate.narrative.policy.dormant");

        /// <summary>Slot of the `RemoveDerived` case: disposable derived state loses its row (P-032, P-033).</summary>
        public static readonly SlotId DerivedSlot = NarrativeIds.Slot("gc.w4gate.narrative.policy.derived");

        /// <summary>Slot of the `TransferTo` case: the value moves to the named available owner (P-025, P-032).</summary>
        public static readonly SlotId TransferSlot = NarrativeIds.Slot("gc.w4gate.narrative.policy.transfer");

        /// <summary>
        /// Slot of the explicit reset case: the one slot whose manifest carries GC-012's `resetSupported` field with
        /// its recorded reason, so the declared reset is authorized by a manifest field and not by a caller (P-032).
        /// </summary>
        public static readonly SlotId ResetSlot = NarrativeIds.Slot("gc.w4gate.narrative.policy.reset");

        /// <summary>Authoritative domain of the preserve slot; a new identity nothing else in the revision stores.</summary>
        public static readonly SchemaRef PreserveDomain =
            NarrativeIds.SchemaRef("gc.w4gate.narrative.policy.preserve.domain", 1U);

        /// <summary>Authoritative domain of the dormant slot.</summary>
        public static readonly SchemaRef DormantDomain =
            NarrativeIds.SchemaRef("gc.w4gate.narrative.policy.dormant.domain", 1U);

        /// <summary>Authoritative domain of the disposable derived slot.</summary>
        public static readonly SchemaRef DerivedDomain =
            NarrativeIds.SchemaRef("gc.w4gate.narrative.policy.derived.domain", 1U);

        /// <summary>Authoritative domain of the transferable slot.</summary>
        public static readonly SchemaRef TransferDomain =
            NarrativeIds.SchemaRef("gc.w4gate.narrative.policy.transfer.domain", 1U);

        /// <summary>Authoritative domain of the resettable slot.</summary>
        public static readonly SchemaRef ResetDomain =
            NarrativeIds.SchemaRef("gc.w4gate.narrative.policy.reset.domain", 1U);

        /// <summary>Generated physical layout key of the preserve slot: one component, one owner (P-033).</summary>
        public static readonly FactoryKey PreserveLayout = NarrativeKeys.Key("gc.w4gate.narrative.policy.preserve.layout");

        /// <summary>Generated physical layout key of the dormant slot.</summary>
        public static readonly FactoryKey DormantLayout = NarrativeKeys.Key("gc.w4gate.narrative.policy.dormant.layout");

        /// <summary>Generated physical layout key of the disposable derived slot.</summary>
        public static readonly FactoryKey DerivedLayout = NarrativeKeys.Key("gc.w4gate.narrative.policy.derived.layout");

        /// <summary>Generated physical layout key of the transferable slot.</summary>
        public static readonly FactoryKey TransferLayout = NarrativeKeys.Key("gc.w4gate.narrative.policy.transfer.layout");

        /// <summary>Generated physical layout key of the resettable slot.</summary>
        public static readonly FactoryKey ResetLayout = NarrativeKeys.Key("gc.w4gate.narrative.policy.reset.layout");

        /// <summary>Field key the preserve slot owns inside its own physical layout (P-033).</summary>
        public static readonly FactoryKey PreserveField = NarrativeKeys.Key("gc.w4gate.narrative.policy.preserve.field");

        /// <summary>Field key the dormant slot owns inside its own physical layout.</summary>
        public static readonly FactoryKey DormantField = NarrativeKeys.Key("gc.w4gate.narrative.policy.dormant.field");

        /// <summary>Field key the disposable derived slot owns inside its own physical layout.</summary>
        public static readonly FactoryKey DerivedField = NarrativeKeys.Key("gc.w4gate.narrative.policy.derived.field");

        /// <summary>Field key the transferable slot owns inside its own physical layout.</summary>
        public static readonly FactoryKey TransferField = NarrativeKeys.Key("gc.w4gate.narrative.policy.transfer.field");

        /// <summary>Field key the resettable slot owns inside its own physical layout.</summary>
        public static readonly FactoryKey ResetField = NarrativeKeys.Key("gc.w4gate.narrative.policy.reset.field");

        /// <summary>Declared initialization policy of the preserve slot (P-032).</summary>
        public static readonly FactoryKey PreserveInit = NarrativeKeys.Key("gc.w4gate.narrative.policy.preserve.init");

        /// <summary>Declared initialization policy of the dormant slot.</summary>
        public static readonly FactoryKey DormantInit = NarrativeKeys.Key("gc.w4gate.narrative.policy.dormant.init");

        /// <summary>Declared initialization policy of the disposable derived slot.</summary>
        public static readonly FactoryKey DerivedInit = NarrativeKeys.Key("gc.w4gate.narrative.policy.derived.init");

        /// <summary>Declared initialization policy of the transferable slot.</summary>
        public static readonly FactoryKey TransferInit = NarrativeKeys.Key("gc.w4gate.narrative.policy.transfer.init");

        /// <summary>
        /// Declared initialization policy of the resettable slot: the key the executed reset resolves its value
        /// through. A reset never invents a zero, so an unregistered policy here would refuse the case (P-032).
        /// </summary>
        public static readonly FactoryKey ResetInit = NarrativeKeys.Key("gc.w4gate.narrative.policy.reset.init");

        /// <summary>Declared configuration-update policy of the preserve slot (P-020).</summary>
        public static readonly FactoryKey PreserveConfigChange =
            NarrativeKeys.Key("gc.w4gate.narrative.policy.preserve.config-change");

        /// <summary>Declared configuration-update policy of the dormant slot.</summary>
        public static readonly FactoryKey DormantConfigChange =
            NarrativeKeys.Key("gc.w4gate.narrative.policy.dormant.config-change");

        /// <summary>Declared configuration-update policy of the disposable derived slot.</summary>
        public static readonly FactoryKey DerivedConfigChange =
            NarrativeKeys.Key("gc.w4gate.narrative.policy.derived.config-change");

        /// <summary>Declared configuration-update policy of the transferable slot.</summary>
        public static readonly FactoryKey TransferConfigChange =
            NarrativeKeys.Key("gc.w4gate.narrative.policy.transfer.config-change");

        /// <summary>Declared configuration-update policy of the resettable slot.</summary>
        public static readonly FactoryKey ResetConfigChange =
            NarrativeKeys.Key("gc.w4gate.narrative.policy.reset.config-change");

        /// <summary>
        /// Registered owner-transfer policy of the transferable slot (P-032): a declaration that says `TransferTo`
        /// without a real transfer-policy key is refused, so the key is part of the declaration.
        /// </summary>
        public static readonly FactoryKey TransferPolicy = NarrativeKeys.Key("gc.w4gate.narrative.policy.transfer-policy");

        // ------------------------------------------------------------------ the required-service surface (GC-014)

        /// <summary>
        /// The contract the pair's consumer requires and both providers export, under this gate's own identity word
        /// (`W4GATENA`), so no slice declaration can be mistaken for it (P-011).
        /// </summary>
        public static readonly ContractRef RequiredService = new ContractRef(new Id128(0x5734474154454E41UL, 1UL), 1U);

        /// <summary>Factory key the required service's export names: the exporter's registration identity (P-011).</summary>
        public static readonly FactoryKey RequiredServiceFactory =
            NarrativeKeys.Key("gc.w4gate.narrative.lifecycle.service-factory");

        /// <summary>Installation of the required service's consumer (P-011).</summary>
        public static readonly PluginInstanceId RequiredConsumerInstall = NarrativeKeys.Instance(21UL);

        /// <summary>Installation of the required service's provider, whose removal makes the consumer wait (P-012).</summary>
        public static readonly PluginInstanceId RequiredProviderInstall = NarrativeKeys.Instance(22UL);

        /// <summary>A compatible provider of the same contract: its return resumes the waiting consumer (P-012).</summary>
        public static readonly PluginInstanceId RequiredProviderReplacementInstall = NarrativeKeys.Instance(23UL);

        /// <summary>
        /// Installation whose unload the sequence observes through the P-048 reverse-acquisition order (P-047,
        /// P-048). GC-014's own scenario mounts the same shape of installation at the world root.
        /// </summary>
        public static readonly PluginInstanceId UnloadInstall = NarrativeKeys.Instance(25UL);

        /// <summary>Stable name of the consumer's Replace capability over the reusable villager recipe (P-017).</summary>
        public const string RequiredConsumerCapabilityName = "gc.w4gate.narrative.lifecycle.consumer";

        /// <summary>Stable name of the payload schema of the consumer's capability.</summary>
        public const string RequiredConsumerSchemaName = "gc.w4gate.narrative.lifecycle.consumer-binding";

        /// <summary>Stable name of the provider's Replace capability over the reusable villager recipe.</summary>
        public const string RequiredProviderCapabilityName = "gc.w4gate.narrative.lifecycle.provider";

        /// <summary>Stable name of the payload schema of the provider's capability.</summary>
        public const string RequiredProviderSchemaName = "gc.w4gate.narrative.lifecycle.provider-binding";

        /// <summary>Stable name of the compatible provider's Replace capability.</summary>
        public const string ReplacementCapabilityName = "gc.w4gate.narrative.lifecycle.provider-alt";

        /// <summary>Stable name of the payload schema of the compatible provider's capability.</summary>
        public const string ReplacementSchemaName = "gc.w4gate.narrative.lifecycle.provider-alt-binding";

        /// <summary>Stable name of the unload installation's Replace capability over the villager recipe.</summary>
        public const string UnloadCapabilityName = "gc.w4gate.narrative.lifecycle.unload";

        /// <summary>Stable name of the payload schema of the unload installation's capability.</summary>
        public const string UnloadSchemaName = "gc.w4gate.narrative.lifecycle.unload-binding";

        /// <summary>Stable name of the state-policy provider's Replace capability over the villager recipe.</summary>
        public const string StatePolicyCapabilityName = "gc.w4gate.narrative.policy.host";

        /// <summary>Stable name of the payload schema of the state-policy provider's capability.</summary>
        public const string StatePolicySchemaName = "gc.w4gate.narrative.policy.host-binding";

        // ------------------------------------------------------------------ the state-policy installation (GC-015)

        /// <summary>Installation of the provider that declares this gate's slot policies.</summary>
        public static readonly PluginInstanceId StatePolicyInstall = NarrativeKeys.Instance(24UL);

        /// <summary>Stable plugin-type identity of that provider.</summary>
        public static readonly PluginTypeId StatePolicyPluginType = NarrativeKeys.PluginTypeId(24UL);

        /// <summary>
        /// One neutral scope per slot case, in the order of `SlotCases`: each policy pass advances the lane with its
        /// own scope creation, because a scope is created once per composition and each case must ride a separate
        /// publication to stay attributable (P-006, P-010).
        /// </summary>
        public static readonly ScopeId PreserveNeutralScope = NarrativeIds.Scope("gc.w4gate.narrative.policy.scope.preserve");

        /// <summary>Neutral scope of the preserve-dormant case.</summary>
        public static readonly ScopeId DormantNeutralScope = NarrativeIds.Scope("gc.w4gate.narrative.policy.scope.dormant");

        /// <summary>Neutral scope of the remove-derived case.</summary>
        public static readonly ScopeId DerivedNeutralScope = NarrativeIds.Scope("gc.w4gate.narrative.policy.scope.derived");

        /// <summary>Neutral scope of the transfer case.</summary>
        public static readonly ScopeId TransferNeutralScope = NarrativeIds.Scope("gc.w4gate.narrative.policy.scope.transfer");

        /// <summary>Neutral scope of the reset case.</summary>
        public static readonly ScopeId ResetNeutralScope = NarrativeIds.Scope("gc.w4gate.narrative.policy.scope.reset");

        // ------------------------------------------------------------------ the two entry points

        /// <summary>Runs the W4 gate for this family against the committed generated catalog (GC-003 compiler output).</summary>
        public static W4GateScenarioResult RunGeneratedCatalog()
        {
            CatalogBuildResult build = ProbeCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated catalog was rejected by the production catalog rules: " + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(ProbeCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return W4GateScenario.Run(new Gc013NarrativeHost.NarrativeFamily(
                catalog,
                Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                ProbeCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the W4 gate for this family against the hand-written generated-style fixture catalog.</summary>
        public static W4GateScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return W4GateScenario.Run(new Gc013NarrativeHost.NarrativeFamily(
                build.Catalog,
                Declarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                NarrativeScenarioCatalog.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="W4GateScenario.FixtureRunPrefix"/> so no two collide.
        /// </summary>
        public static IReadOnlyList<W4GateStep> RunBoth(out W4GateScenarioResult generated, out W4GateScenarioResult fixture)
        {
            generated = RunGeneratedCatalog();
            fixture = RunFixtureCatalog();

            var combined = new List<W4GateStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                W4GateStep step = fixture.Steps[i];
                combined.Add(new W4GateStep(W4GateScenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        // ------------------------------------------------------------------ declarations

        /// <summary>
        /// One run's narrative declaration set: the chapter providers, the forward provider and the exclusive pair
        /// the GC-013 half declares, plus this gate's state-policy provider. The state-policy provider is a
        /// declaration and not only an extra manifest, because its declared slots must belong to the compiled
        /// ownership surface of the revision the policy pass is validated against (P-009, P-032).
        /// </summary>
        private static IReadOnlyList<CatalogPluginDeclaration> Declarations(FactoryKey factoryKey, SchemaRef configSchema)
        {
            var declarations = new List<CatalogPluginDeclaration>(Gc013NarrativeHost.Declarations(factoryKey, configSchema));
            declarations.Add(new CatalogPluginDeclaration(
                StatePolicyProviderManifest(StatePolicyPluginType, factoryKey, configSchema),
                ConfigDocument.Empty));
            return declarations;
        }

        /// <summary>
        /// The state-policy provider: one Replace capability over the reusable villager recipe, so a mounted
        /// installation carries attributed rows like the slice's own providers, plus the five declared state slots
        /// every declared policy of this gate is read from (P-017, P-032, P-033).
        /// </summary>
        public static PluginManifest StatePolicyProviderManifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema)
        {
            var contracts = new List<CapabilityContract>
            {
                NarrativeDeclarations.Contract(
                    StatePolicyCapabilityName,
                    StatePolicySchemaName,
                    0,
                    NarrativeDerivationPlan.ReplacePolicy),
            };

            var rules = new List<DerivationRule>
            {
                NarrativeDeclarations.Rule(
                    "gc.w4gate.narrative.policy",
                    ".host",
                    StatePolicyCapabilityName,
                    StatePolicySchemaName,
                    0,
                    NarrativeCompositionNames.VillagerRecipe,
                    string.Empty,
                    NarrativeDerivationPlan.ReplacePolicy,
                    1),
            };

            return new PluginManifest(
                pluginType,
                NarrativeDeclarations.PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                null,
                null,
                contracts,
                rules,
                null,
                PolicySlots(),
                null,
                null,
                null);
        }

        /// <summary>
        /// The five declared policy slots, in the order of `SlotCases`: `Preserve` (declared `PreserveDormant`),
        /// `PreserveDormant`, `RemoveDerived`, `TransferTo` with its registered transfer policy, and the reset slot
        /// whose manifest records GC-012's `resetSupported` field and the reason that permits the reset (P-032).
        /// </summary>
        private static IReadOnlyList<StateSlotSpec> PolicySlots()
        {
            return new List<StateSlotSpec>
            {
                PolicySlot(
                    PreserveSlot,
                    PreserveDomain,
                    PreserveLayout,
                    PreserveField,
                    PreserveInit,
                    PreserveConfigChange,
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    false,
                    null),
                PolicySlot(
                    DormantSlot,
                    DormantDomain,
                    DormantLayout,
                    DormantField,
                    DormantInit,
                    DormantConfigChange,
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    false,
                    null),
                PolicySlot(
                    DerivedSlot,
                    DerivedDomain,
                    DerivedLayout,
                    DerivedField,
                    DerivedInit,
                    DerivedConfigChange,
                    LastSupportPolicy.RemoveDerived,
                    default(FactoryKey),
                    false,
                    null),
                PolicySlot(
                    TransferSlot,
                    TransferDomain,
                    TransferLayout,
                    TransferField,
                    TransferInit,
                    TransferConfigChange,
                    LastSupportPolicy.TransferTo,
                    TransferPolicy,
                    false,
                    null),
                PolicySlot(
                    ResetSlot,
                    ResetDomain,
                    ResetLayout,
                    ResetField,
                    ResetInit,
                    ResetConfigChange,
                    LastSupportPolicy.PreserveDormant,
                    default(FactoryKey),
                    true,
                    ResetReason),
            };
        }

        /// <summary>
        /// One policy slot: one owner (`NarrativeKeys.DialogueOwner`), one domain, one physical layout key and one
        /// field mapping, which is exactly the shape P-033 allows for several independently-policied slots. The
        /// reset fields go through GC-012's explicit overload rather than a hand-built options value, so "the
        /// manifest authorizes the reset" is a declared fact (P-032).
        /// </summary>
        private static StateSlotSpec PolicySlot(
            SlotId slot,
            SchemaRef domain,
            FactoryKey layout,
            FactoryKey field,
            FactoryKey init,
            FactoryKey configChange,
            LastSupportPolicy lastSupport,
            FactoryKey transferPolicy,
            bool resetSupported,
            string? resetReason)
        {
            return new StateSlotSpec(
                slot,
                NarrativeKeys.DialogueOwner,
                domain,
                layout,
                new List<FieldOwnership> { new FieldOwnership(domain, field.RegistrationKey) },
                init,
                configChange,
                default(FactoryKey),
                lastSupport,
                transferPolicy,
                null,
                resetSupported,
                resetReason);
        }

        /// <summary>
        /// The four lifecycle-shaped manifests this gate installs: the required service's consumer, its provider,
        /// the compatible provider whose return resumes the waiting consumer, and the installation the sequence
        /// unloads. Each declares one Replace capability over the reusable villager recipe and no stage, buffer,
        /// state slot or resource, so none of them enters the compiled schedule (P-011, P-047).
        /// </summary>
        public static IReadOnlyList<PluginManifest> LifecycleManifests(FactoryKey factoryKey, SchemaRef configSchema)
        {
            return new List<PluginManifest>
            {
                LifecycleShapedManifest(
                    NarrativeKeys.PluginTypeId(21UL),
                    factoryKey,
                    configSchema,
                    "gc.w4gate.narrative.lifecycle.consumer",
                    "-rule",
                    RequiredConsumerCapabilityName,
                    RequiredConsumerSchemaName,
                    null,
                    new List<ServiceDependency>
                    {
                        new ServiceDependency(
                            RequiredService,
                            new VersionRange(RequiredService.Version, RequiredService.Version),
                            true,
                            ServiceResolutionDomain.AncestorsAndSelf,
                            default(ProviderInstallationId),
                            default(FactoryKey)),
                    }),
                LifecycleShapedManifest(
                    NarrativeKeys.PluginTypeId(22UL),
                    factoryKey,
                    configSchema,
                    "gc.w4gate.narrative.lifecycle.provider",
                    "-rule",
                    RequiredProviderCapabilityName,
                    RequiredProviderSchemaName,
                    new List<ServiceExport>
                    {
                        new ServiceExport(
                            RequiredService,
                            RequiredServiceFactory,
                            ServiceVisibility.Private,
                            false,
                            ServiceBindingKind.Single),
                    },
                    null),
                LifecycleShapedManifest(
                    NarrativeKeys.PluginTypeId(23UL),
                    factoryKey,
                    configSchema,
                    "gc.w4gate.narrative.lifecycle.provider-alt",
                    "-rule",
                    ReplacementCapabilityName,
                    ReplacementSchemaName,
                    new List<ServiceExport>
                    {
                        new ServiceExport(
                            RequiredService,
                            RequiredServiceFactory,
                            ServiceVisibility.Private,
                            false,
                            ServiceBindingKind.Single),
                    },
                    null),
                LifecycleShapedManifest(
                    NarrativeKeys.PluginTypeId(25UL),
                    factoryKey,
                    configSchema,
                    "gc.w4gate.narrative.lifecycle.unload",
                    "-rule",
                    UnloadCapabilityName,
                    UnloadSchemaName,
                    null,
                    null),
            };
        }

        /// <summary>
        /// One lifecycle-shaped manifest: the same declaration shape the narrative chapters use (one capability
        /// contract with one output slot and one derivation rule over the reusable villager recipe), plus the
        /// declared service surface of its role. It declares no stage, no buffer, no state slot and no resource.
        /// </summary>
        private static PluginManifest LifecycleShapedManifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            string chapterTag,
            string ruleSuffix,
            string capabilityName,
            string schemaName,
            IReadOnlyList<ServiceExport>? exports,
            IReadOnlyList<ServiceDependency>? dependencies)
        {
            var contracts = new List<CapabilityContract>
            {
                NarrativeDeclarations.Contract(capabilityName, schemaName, 0, NarrativeDerivationPlan.ReplacePolicy),
            };

            var rules = new List<DerivationRule>
            {
                NarrativeDeclarations.Rule(
                    chapterTag,
                    ruleSuffix,
                    capabilityName,
                    schemaName,
                    0,
                    NarrativeCompositionNames.VillagerRecipe,
                    string.Empty,
                    NarrativeDerivationPlan.ReplacePolicy,
                    1),
            };

            return new PluginManifest(
                pluginType,
                NarrativeDeclarations.PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                configSchema,
                factoryKey,
                exports,
                dependencies,
                contracts,
                rules,
                null,
                null,
                null,
                null,
                null);
        }

        /// <summary>
        /// The declared initialization policies of this gate: the narrative slice's own registrations (so a version
        /// change of the slice's slots resolves through a declared value) plus one per gate slot, including the
        /// value the reset slot's declared reset writes (P-032).
        /// </summary>
        public static IInitializationPolicyRegistry Initializations()
        {
            var registry = new InitializationPolicyRegistry();
            registry.Register(PreserveInit, PreserveDomain, PreserveSlotInitialValue);
            registry.Register(DormantInit, DormantDomain, PreserveSlotInitialValue);
            registry.Register(DerivedInit, DerivedDomain, PreserveSlotInitialValue);
            registry.Register(TransferInit, TransferDomain, PreserveSlotInitialValue);
            registry.Register(ResetInit, ResetDomain, ResetSlotInitialValue);

            registry.Register(NarrativeKeys.ConversationInit, NarrativeKeys.ConversationDomain, NarrativeConversationStatus.Idle);
            registry.Register(NarrativeKeys.QuestInit, NarrativeKeys.QuestDomain, RulesNarrativeFacts.InitialValue);
            registry.Register(NarrativeKeys.GateInit, NarrativeKeys.GateDomain, 0);
            registry.Register(NarrativeKeys.TrailInit, NarrativeKeys.TrailDomain, 0);
            registry.Register(NarrativeKeys.EncounterInit, NarrativeKeys.EncounterDomain, 0);
            return registry;
        }

        /// <summary>
        /// The declared slot cases, in the order the runner reports them: an explicit `Preserve`, then the three
        /// last-support outcomes, then the explicit reset. Every value is non-default and different from its
        /// neighbours, so a policy that loses the value cannot pass by accident (P-032).
        /// </summary>
        public static IReadOnlyList<W4GateSlotCase> Cases()
        {
            return new List<W4GateSlotCase>
            {
                new W4GateSlotCase(
                    W4GateSlotPolicy.Preserve,
                    new StateSlotKey(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, PreserveSlot),
                    1U,
                    23,
                    default(OwnerId),
                    default(TargetId),
                    string.Empty),
                new W4GateSlotCase(
                    W4GateSlotPolicy.PreserveDormant,
                    new StateSlotKey(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, DormantSlot),
                    1U,
                    11,
                    default(OwnerId),
                    default(TargetId),
                    string.Empty),
                new W4GateSlotCase(
                    W4GateSlotPolicy.RemoveDerived,
                    new StateSlotKey(NarrativeKeys.QuestLedger, NarrativeKeys.DialogueOwner, DerivedSlot),
                    1U,
                    13,
                    default(OwnerId),
                    default(TargetId),
                    string.Empty),
                // The transfer's destination target is `Sailor`, deliberately outside `PolicyTargets`: a later pass
                // reads every live slot row of the policy targets, and `SlotStatePolicySet.TryFind` refuses a live row
                // whose owner differs from the slot's declaration (P-034). The moved row therefore lands where no
                // later pass re-reads it, and the destination owner is `GateOwner`, which this revision declares.
                new W4GateSlotCase(
                    W4GateSlotPolicy.TransferTo,
                    new StateSlotKey(NarrativeKeys.QuestLedger, NarrativeKeys.DialogueOwner, TransferSlot),
                    1U,
                    17,
                    NarrativeKeys.GateOwner,
                    NarrativeKeys.Sailor,
                    string.Empty),
                new W4GateSlotCase(
                    W4GateSlotPolicy.Reset,
                    new StateSlotKey(NarrativeKeys.Mara, NarrativeKeys.DialogueOwner, ResetSlot),
                    1U,
                    19,
                    default(OwnerId),
                    default(TargetId),
                    ResetReason),
            };
        }

        /// <summary>
        /// The policy targets and the scope each of them lives in. The narrative run seeds both: `Mara` is one of the
        /// family's own declared targets, and the world-level ledger the transfer and removal cases act on is seeded
        /// by `SeedSlotCase` through the same `LiveTargetSeeder`, so the policy pass acts on state the world really
        /// owns rather than on a value the assertion invented (P-032).
        /// </summary>
        public static IReadOnlyList<TargetId> Targets()
        {
            return new List<TargetId>
            {
                NarrativeKeys.Mara,
                NarrativeKeys.QuestLedger,
            };
        }

        /// <summary>
        /// The neutral edits: one scope creation per case, in the same order as the cases. Each policy pass advances
        /// the composition lane with its own neutral publication, so "this disposition caused this storage change" is
        /// checkable per case (P-006, P-010).
        /// </summary>
        public static IReadOnlyList<CompositionEditPayload> NeutralEdits()
        {
            return new List<CompositionEditPayload>
            {
                Gc013NarrativeHost.ScopeCreate(PreserveNeutralScope, NarrativeKeys.RootScope),
                Gc013NarrativeHost.ScopeCreate(DormantNeutralScope, NarrativeKeys.RootScope),
                Gc013NarrativeHost.ScopeCreate(DerivedNeutralScope, NarrativeKeys.RootScope),
                Gc013NarrativeHost.ScopeCreate(TransferNeutralScope, NarrativeKeys.RootScope),
                Gc013NarrativeHost.ScopeCreate(ResetNeutralScope, NarrativeKeys.RootScope),
            };
        }

        /// <summary>
        /// The scope and recipe one policy target lives under. Resolution is by exact identity, never by a name
        /// prefix: a target this gate does not declare is refused (P-015).
        /// </summary>
        public static bool TryTargetPlacement(TargetId target, out ScopeId scope, out DefinitionRef recipe)
        {
            if (target.Equals(NarrativeKeys.Mara))
            {
                scope = NarrativeKeys.VillageScope;
                recipe = NarrativeKeys.VillagerRecipe;
                return true;
            }

            if (target.Equals(NarrativeKeys.QuestLedger))
            {
                scope = NarrativeKeys.RootScope;
                recipe = NarrativeKeys.QuestLedgerRecipe;
                return true;
            }

            scope = default(ScopeId);
            recipe = default(DefinitionRef);
            return false;
        }
    }

    // The W4 integration gate's half of the narrative family adapter. `Gc013NarrativeHost` declares the same type as
    // its own partial part and implements `IGc013Family`; this part adds the lifecycle and state-policy surface
    // `IW4GateFamily` requires, and nothing else: the runner owns the ordering, the publications and the
    // observations (P-001).
    public static partial class Gc013NarrativeHost
    {
        /// <summary>
        /// One narrative run's declared facts for the W4 integration gate: the GC-013 half plus this gate's five
        /// lifecycle installations and the state-policy provider whose declared slots carry the five slot policies.
        /// </summary>
        public sealed partial class NarrativeFamily : IW4GateFamily
        {
            private IReadOnlyList<PluginManifest>? lifecycleManifests;
            private IReadOnlyList<PluginManifest>? extraManifests;
            private IReadOnlyList<W4GateSlotCase>? slotCases;
            private IReadOnlyList<TargetId>? policyTargets;
            private IReadOnlyList<CompositionEditPayload>? neutralEdits;
            private IInitializationPolicyRegistry? initialValues;

            /// <summary>
            /// Diagnostic text of the last refused case seed, or empty when every seed of this call succeeded. A
            /// `bool` cannot carry the seeder's own code, so the refusal's code and detail are kept here rather than
            /// swallowed (P-052).
            /// </summary>
            public string LastSlotSeedFailure { get; private set; } = string.Empty;

            // ------------------------------------------------------------------ the lifecycle half (GC-014)

            /// <summary>The chapter-one installation: the provider whose activation carries the slice's own rows.</summary>
            public PluginInstanceId LifecycleProviderInstall => NarrativeKeys.ChapterOneInstall;

            public ScopeId LifecycleProviderScope => NarrativeKeys.ChapterOneScope;

            /// <summary>The run's own chapter-one declaration, resolved rather than re-derived (P-009).</summary>
            public PluginManifest LifecycleProviderManifest => declarations[0].Manifest;

            public PluginInstanceId RequiredConsumerInstall => W4GateNarrativeHost.RequiredConsumerInstall;

            /// <summary>
            /// The consumer and both providers mount at the world root, exactly as GC-014's own scenario does: the
            /// dependency resolves in `AncestorsAndSelf`, so one scope makes the loss and the return the same
            /// resolution question (P-011, P-012).
            /// </summary>
            public ScopeId RequiredConsumerScope => NarrativeKeys.RootScope;

            public PluginManifest RequiredConsumerManifest => LifecycleManifests[0];

            public PluginInstanceId RequiredProviderInstall => W4GateNarrativeHost.RequiredProviderInstall;

            public ScopeId RequiredProviderScope => NarrativeKeys.RootScope;

            public PluginManifest RequiredProviderManifest => LifecycleManifests[1];

            public PluginInstanceId RequiredProviderReplacementInstall => W4GateNarrativeHost.RequiredProviderReplacementInstall;

            public PluginManifest RequiredProviderReplacementManifest => LifecycleManifests[2];

            public ContractRef RequiredService => W4GateNarrativeHost.RequiredService;

            public PluginInstanceId UnloadInstall => W4GateNarrativeHost.UnloadInstall;

            public ScopeId UnloadScope => NarrativeKeys.RootScope;

            public PluginManifest UnloadManifest => LifecycleManifests[3];

            /// <summary>O-03: the mount payload of the narrative vocabulary, which declares the effective hash (P-020).</summary>
            public CompositionEditPayload MountInstall(PluginManifest manifest, PluginInstanceId instance, ScopeId scope)
                => NarrativeLifecyclePayloads.Mount(manifest, instance, scope);

            /// <summary>O-06: suspend, retaining the definition and the configuration (P-046).</summary>
            public CompositionEditPayload SuspendInstall(PluginInstanceId instance)
                => NarrativeLifecyclePayloads.Suspend(instance);

            /// <summary>O-04: resume an explicitly suspended installation.</summary>
            public CompositionEditPayload ResumeInstall(PluginInstanceId instance)
                => NarrativeLifecyclePayloads.Resume(instance);

            /// <summary>O-07: unmount; the publication carries the removal (P-048).</summary>
            public CompositionEditPayload UnmountInstall(PluginInstanceId instance)
                => NarrativeLifecyclePayloads.Unmount(instance);

            // ------------------------------------------------------------------ the state-policy half (GC-015)

            public PluginInstanceId StatePolicyInstall => W4GateNarrativeHost.StatePolicyInstall;

            /// <summary>
            /// The world root, and that choice is forced rather than preferred: the policy targets are `Mara` (at
            /// `village`, beneath `chapter-one`) and the world-level `QuestLedger` (at the root itself), and the
            /// provider's scope must be an ancestor of every target the pass acts on. The root is the only declared
            /// scope that is an ancestor of both (P-010, P-032).
            /// </summary>
            public ScopeId StatePolicyScope => NarrativeKeys.RootScope;

            /// <summary>
            /// The state-policy provider's manifest, resolved out of this run's own declaration list rather than
            /// rebuilt: the declaration that carries its slots into the compiled ownership surface is the very one
            /// the mount resolves (P-009, P-032).
            /// </summary>
            public PluginManifest StatePolicyManifest
            {
                get
                {
                    for (int i = 0; i < declarations.Count; i++)
                    {
                        if (declarations[i].Manifest.PluginTypeId.Value.Equals(W4GateNarrativeHost.StatePolicyPluginType.Value))
                        {
                            return declarations[i].Manifest;
                        }
                    }

                    throw new InvalidOperationException(
                        "this run's declarations do not carry the state-policy provider, so its slots are not part of"
                        + " the compiled ownership surface the policy pass is validated against (P-009, P-032).");
                }
            }

            /// <summary>O-03: mount the state-policy provider whose declared slots the policy pass reads.</summary>
            public CompositionEditPayload MountStatePolicyHost() => NarrativeMounts.Mount(
                StatePolicyManifest,
                W4GateNarrativeHost.StatePolicyInstall,
                NarrativeKeys.RootScope,
                ConfigDocument.Empty);

            public IReadOnlyList<W4GateSlotCase> SlotCases
                => slotCases ??= W4GateNarrativeHost.Cases();

            public IReadOnlyList<TargetId> PolicyTargets
                => policyTargets ??= W4GateNarrativeHost.Targets();

            /// <summary>
            /// The registry the family already publishes through, so a migration identity is declared once and the
            /// version-change policy this pass validates is the one the world's own scenario registers (P-029, P-054).
            /// </summary>
            public MigrationRegistry PolicyMigrations => migrations;

            public IInitializationPolicyRegistry InitialValues
                => initialValues ??= W4GateNarrativeHost.Initializations();

            public IReadOnlyList<CompositionEditPayload> NeutralEdits
                => neutralEdits ??= W4GateNarrativeHost.NeutralEdits();

            public IReadOnlyList<PluginManifest> ExtraManifests => extraManifests ??= BuildExtraManifests();

            /// <summary>
            /// Seeds one case's slot at its declared schema version with a non-default value, creating the case's
            /// target first when the GC-013 sequence does not seed it. A refusal keeps the seeder's own code and
            /// detail in <see cref="LastSlotSeedFailure"/> instead of being swallowed (P-032, P-052).
            /// </summary>
            public bool SeedSlotCase(W4GateSlotCase slotCase, LiveTargetSeeder seeder)
            {
                if (seeder == null)
                {
                    throw new ArgumentNullException(nameof(seeder));
                }

                LastSlotSeedFailure = string.Empty;
                if (!EnsurePolicyTarget(slotCase.Slot.Target, seeder, out string placementFailure))
                {
                    LastSlotSeedFailure = placementFailure;
                    return false;
                }

                if (!seeder.TrySeedSlot(
                        slotCase.Slot.Target,
                        slotCase.Slot.Owner,
                        slotCase.Slot.Slot,
                        slotCase.SchemaVersion,
                        slotCase.Value,
                        out DiagnosticCode code,
                        out string detail))
                {
                    LastSlotSeedFailure = "seeding " + slotCase.Slot.ToString() + " was refused: "
                        + DiagnosticCodeText.Of(code) + ": " + detail;
                    return false;
                }

                return true;
            }

            /// <summary>
            /// The four lifecycle-shaped manifests, built from this run's own factory key and configuration schema,
            /// because the lane resolves a mount's declaration and its configuration-schema defaults through the
            /// manifest source of the same revision (P-009).
            /// </summary>
            private IReadOnlyList<PluginManifest> LifecycleManifests
                => lifecycleManifests ??= W4GateNarrativeHost.LifecycleManifests(
                    declarations[0].Manifest.FactoryKey,
                    declarations[0].Manifest.ConfigSchema);

            private IReadOnlyList<PluginManifest> BuildExtraManifests()
            {
                var manifests = new List<PluginManifest>
                {
                    RequiredConsumerManifest,
                    RequiredProviderManifest,
                    RequiredProviderReplacementManifest,
                    UnloadManifest,
                    StatePolicyManifest,
                };

                return manifests;
            }

            /// <summary>
            /// Creates a policy target the GC-013 sequence does not seed, so the state-policy cases act on a target
            /// that exists rather than on a value an assertion injected. It is a no-op for a target that is already
            /// live, so the family's own seeding order stays the authority (P-010, P-032).
            /// </summary>
            private static bool EnsurePolicyTarget(TargetId target, LiveTargetSeeder seeder, out string failure)
            {
                failure = string.Empty;
                if (seeder.Index.Contains(target))
                {
                    return true;
                }

                if (!W4GateNarrativeHost.TryTargetPlacement(target, out ScopeId scope, out DefinitionRef recipe))
                {
                    failure = "no declared scope and recipe cover the policy target " + target.ToString() + " (P-015).";
                    return false;
                }

                if (!seeder.TrySeed(target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                {
                    failure = "policy target " + target.ToString() + " was refused: "
                        + DiagnosticCodeText.Of(code) + ": " + detail;
                    return false;
                }

                return true;
            }
        }
    }
}
