// GameCore.Validation.ProbeHost - the GC-022 lifecycle-stress card family adapter.
//
// `LifecycleStressFamily.cs` defines what the stress requires from one genre; `Gc013CardsHost.CardFamily` already
// declares the whole card slice - its catalog declarations, its market/league scope tree, its live seats and table,
// the providers it mounts, the composition edits every scenario submits, and (through `Gc019CardsHost.cs`) the card
// genre's own stage runtime. This part adds exactly three things and nothing else:
//
//   * the declaration that the card family satisfies `ILifecycleStressFamily`, so a family that lost the adapter half
//     fails to compile rather than at run time (P-001, P-002);
//   * the four manifests the stress mounts beyond the family's catalog declarations. They are declared here in the
//     card declaration shape (`CardLifecycleScenario`'s own test-only capability: one output slot, `Additive`, the
//     registered Int32 sum reducer and the always-accepting predicate, with the reusable seat recipe as the rule's
//     selector), so their contributions reach real seats and a provider loss retracts rows that are really attributed
//     to the installation (P-012, P-015, P-017, P-019). Nothing here reaches into `Packages/**`;
//   * the two catalog entry points the EditMode suite and the player probe call, mirroring
//     `RunGeneratedCatalogW5Gate`/`RunFixtureCatalogW5Gate` so the stress runs the *same* card market the earlier
//     gates run - the committed generated catalog and the hand-written generated-style catalog, nothing new - plus the
//     two frozen digest literals its runs must report (P-008, P-028).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.GeneratedCards;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013CardsHost
    {
        /// <summary>
        /// Digest the card stress run must report over its twelve named observations, all passing, over the committed
        /// generated catalog: `NarrativeDigest.OfLines` over `cards/lifecycle-stress-...=pass` lines in the frozen
        /// order. It is computed from the frozen name table, so a renamed, reordered, added or dropped observation
        /// changes the literal and the gate cannot silently shrink (P-008).
        /// </summary>
        public const string LifecycleStressGeneratedDigest =
            "bc9321062734d84582a7ff50ac3f0e18c24f77bfdd45103c2958a1991e849f23";

        /// <summary>
        /// The fixture-catalog run's literal, computed the same way over `fixture:cards/lifecycle-stress-...` names:
        /// the fixture run's observation names carry the prefix, so its table - and therefore its digest - differs
        /// from the generated run's.
        /// </summary>
        public const string LifecycleStressFixtureDigest =
            "f601e7378d5a800bf740441b626d323cd0dc6b3d4b3c53b43a0db3a20c7544b3";

        /// <summary>Runs the lifecycle stress against the committed generated catalog (GC-011 compiler output).</summary>
        public static LifecycleStressResult RunGeneratedCatalogLifecycleStress()
        {
            CatalogBuildResult build = CardCatalog.BuildVerifiedCatalog(out ContentHash _);
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the committed generated card catalog was rejected by the production catalog rules: "
                    + build.Describe());
            }

            ImmutableCatalog catalog = build.Catalog;
            if (!ContentHash.TryParseHex(CardCatalog.CatalogFingerprint, out ContentHash emittedFingerprint)
                || !catalog.Fingerprint.Equals(emittedFingerprint))
            {
                throw new InvalidOperationException(
                    "the generated card catalog's emitted fingerprint literal is not the catalog this run derived over (P-028).");
            }

            return LifecycleStressScenario.Run(new CardFamily(catalog, Declarations(), CardCatalog.CatalogFingerprint), false);
        }

        /// <summary>Runs the lifecycle stress against the hand-written generated-style catalog in the fixture package.</summary>
        public static LifecycleStressResult RunFixtureCatalogLifecycleStress()
        {
            CatalogBuildResult build = CardCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException("the hand-written card catalog was rejected: " + build.Describe());
            }

            return LifecycleStressScenario.Run(
                new CardFamily(build.Catalog, Declarations(), CardCatalogTable.Fingerprint().ToHex()),
                true);
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="LifecycleStressScenario.FixtureRunPrefix"/> so no two
        /// collide.
        /// </summary>
        public static IReadOnlyList<LifecycleStressStep> RunBothLifecycleStress(
            out LifecycleStressResult generated,
            out LifecycleStressResult fixture)
        {
            generated = RunGeneratedCatalogLifecycleStress();
            fixture = RunFixtureCatalogLifecycleStress();

            var combined = new List<LifecycleStressStep>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                LifecycleStressStep step = fixture.Steps[i];
                combined.Add(new LifecycleStressStep(
                    LifecycleStressScenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        /// <summary>
        /// The lifecycle-stress half of the card family: the four stress manifests, the per-cycle instance identity and
        /// the card slice's own mount/unmount payloads.
        /// </summary>
        public sealed partial class CardFamily : ILifecycleStressFamily
        {
            /// <summary>Stable contract identity of the required service the provider exports (P-011).</summary>
            private static readonly ContractRef StressService =
                new ContractRef(CardIdentity.Id("cards.lifecycle-stress.service"), 1U);

            /// <summary>Factory key the required service's export names (the exporter's registration identity).</summary>
            private static readonly FactoryKey StressServiceFactory =
                CardIdentity.Key("cards.lifecycle-stress.service-factory");

            /// <summary>The capability both stress providers of this family declare, one output slot each.</summary>
            private const int StressBonus = 7;

            private LifecycleStressDeclarations? stressDeclarations;

            public LifecycleStressDeclarations StressDeclarations =>
                stressDeclarations ??= CardStressDeclarations();

            public string GeneratedCatalogDigest => Gc013CardsHost.LifecycleStressGeneratedDigest;

            public string FixtureCatalogDigest => Gc013CardsHost.LifecycleStressFixtureDigest;

            /// <summary>
            /// One fresh mount identity of the card slice, from a stable name per ordinal, so a cycle never reuses the
            /// identity an earlier cycle disposed (P-004, P-005).
            /// </summary>
            public PluginInstanceId StressInstance(ulong ordinal) =>
                CardTableKeys.Instance("cards.lifecycle-stress.instance-" + ordinal.ToString(CultureInfo.InvariantCulture));

            /// <summary>The card slice's own O-03 mount payload for one manifest at one scope (P-020).</summary>
            public CompositionEditPayload StressMount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope) =>
                CardTablePayloads.Mount(manifest, instance, scope);

            /// <summary>The card slice's own O-07 unmount payload for one installation (P-046, P-048).</summary>
            public CompositionEditPayload StressUnmount(PluginInstanceId instance) =>
                CardTablePayloads.Unmount(instance);

            /// <summary>
            /// The four manifests this run mounts beyond the family's catalog declarations, each resolving the run's
            /// own catalog factory key and configuration schema (P-009). The consumer declares a required dependency on
            /// the provider's export and a capability of its own, so a provider loss retracts rows that are really
            /// attributed to it (P-012, P-017).
            /// </summary>
            private LifecycleStressDeclarations CardStressDeclarations()
            {
                CatalogPluginDeclaration reference = Declarations[0];
                FactoryKey factoryKey = reference.Manifest.FactoryKey;
                SchemaRef configSchema = reference.Manifest.ConfigSchema;

                return new LifecycleStressDeclarations(
                    installation: StressManifest(
                        CardTableKeys.PluginType("cards.lifecycle-stress.installation"),
                        factoryKey,
                        configSchema,
                        "cards.lifecycle-stress.installation",
                        "cards.lifecycle-stress.installation-rule",
                        StressBonus,
                        null,
                        null),
                    serviceConsumer: StressManifest(
                        CardTableKeys.PluginType("cards.lifecycle-stress.consumer"),
                        factoryKey,
                        configSchema,
                        "cards.lifecycle-stress.consumer",
                        "cards.lifecycle-stress.consumer-rule",
                        StressBonus,
                        null,
                        new List<ServiceDependency>
                        {
                            new ServiceDependency(
                                StressService,
                                new VersionRange(StressService.Version, StressService.Version),
                                true,
                                ServiceResolutionDomain.AncestorsAndSelf,
                                default(ProviderInstallationId),
                                default(FactoryKey)),
                        }),
                    serviceProvider: StressManifest(
                        CardTableKeys.PluginType("cards.lifecycle-stress.provider"),
                        factoryKey,
                        configSchema,
                        "cards.lifecycle-stress.provider",
                        "cards.lifecycle-stress.provider-rule",
                        StressBonus,
                        new List<ServiceExport>
                        {
                            new ServiceExport(
                                StressService,
                                StressServiceFactory,
                                ServiceVisibility.Private,
                                false,
                                ServiceBindingKind.Single),
                        },
                        null),
                    fenced: StressManifest(
                        CardTableKeys.PluginType("cards.lifecycle-stress.fenced"),
                        factoryKey,
                        configSchema,
                        "cards.lifecycle-stress.fenced",
                        "cards.lifecycle-stress.fenced-rule",
                        StressBonus,
                        null,
                        null),
                    stage: CardTableKeys.InputStage,
                    system: CardTableKeys.InputSystem);
            }

            /// <summary>
            /// One stress manifest: the test-only capability shape the card lifecycle scenario declares (one output
            /// slot under the registered Int32 sum reducer and the always-accepting predicate, one rule whose selector
            /// is the reusable seat recipe), plus the declared service surface of its role. It declares no state slot,
            /// stage or buffer, so it never enters the compiled schedule (P-019, P-043).
            /// </summary>
            private static PluginManifest StressManifest(
                PluginTypeId pluginType,
                FactoryKey factoryKey,
                SchemaRef configSchema,
                string capabilityStableName,
                string ruleStableName,
                int bonus,
                IReadOnlyList<ServiceExport>? exports,
                IReadOnlyList<ServiceDependency>? dependencies)
            {
                SlotId slot = CardIdentity.Slot(capabilityStableName + ".slot-0");
                CapabilityId capability = CardIdentity.CapabilityRef(capabilityStableName);

                return new PluginManifest(
                    pluginType,
                    "1.0.0",
                    ContentHash.Empty,
                    new SupportedProtocolRange(1, 0, 0),
                    null,
                    configSchema,
                    factoryKey,
                    exports,
                    dependencies,
                    new List<CapabilityContract>
                    {
                        new CapabilityContract(
                            capability,
                            CardVocabulary.BonusStratum,
                            new List<OutputSlotSchema>
                            {
                                new OutputSlotSchema(
                                    slot, CardIdentity.SchemaRef(capabilityStableName + "-value")),
                            },
                            new List<SlotCompositionPolicy>
                            {
                                new SlotCompositionPolicy(slot, CompositionPolicy.Additive, CardVocabulary.BonusReducerKey),
                            },
                            null),
                    },
                    new List<DerivationRule>
                    {
                        new DerivationRule(
                            CardIdentity.Rule(ruleStableName),
                            capability,
                            CardVocabulary.BonusStratum,
                            1U,
                            new List<SchemaRef> { CardVocabulary.SelectorSchema(CardVocabulary.CardSeatRecipe) },
                            CardVocabulary.AlwaysPredicateKey,
                            null,
                            PropagationReach.SelfAndDescendants,
                            true,
                            0,
                            CompositionPolicy.Additive,
                            CardTableDeclarations.WriteInt32(bonus)),
                    },
                    null,
                    null,
                    null,
                    null,
                    null);
            }
        }
    }
}
