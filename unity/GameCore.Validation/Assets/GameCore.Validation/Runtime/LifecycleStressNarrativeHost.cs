// GameCore.Validation.ProbeHost - the GC-022 lifecycle-stress narrative family adapter.
//
// `LifecycleStressFamily.cs` defines what the stress requires from one genre; `Gc013NarrativeHost.NarrativeFamily`
// already declares the whole narrative slice - its catalog declarations, its chapter scope tree, its live villagers,
// the providers it mounts, the composition edits every scenario submits, and (through `Gc019NarrativeHost.cs`) the
// narrative genre's own stage runtime. This part adds exactly three things and nothing else:
//
//   * the declaration that the narrative family satisfies `ILifecycleStressFamily`, so a family that lost the adapter
//     half fails to compile rather than at run time (P-001, P-002);
//   * the four manifests the stress mounts beyond the family's catalog declarations. They are declared here, in the
//     slice's own declaration shape (`NarrativeDeclarations.Contract`/`Rule` over the reusable villager recipe), with
//     the run's own plugin types, factory keys, capability names and rule identities, so nothing in this wave reaches
//     into `Packages/**` (the same reason `NarrativeLifecycleScenario` declares its own service pair). The consumer's
//     dependency on the provider's export is a real `ServiceDependency` with `Required = true` resolved by the real
//     `ServiceResolver`, and the provider's export is a real `ServiceExport` (P-011, P-012);
//   * the two catalog entry points the EditMode suite and the player probe call, mirroring
//     `RunGeneratedCatalogW5Gate`/`RunFixtureCatalogW5Gate` so the stress runs the *same* narrative world the earlier
//     gates run - the committed generated catalog and the hand-written generated-style catalog, nothing new - plus the
//     two frozen digest literals its runs must report (P-008, P-028).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime.Integration;
using GameCore.Validation.Generated;

namespace GameCore.Validation.ProbeHost
{
    public static partial class Gc013NarrativeHost
    {
        /// <summary>
        /// Digest the narrative stress run must report over its twelve named observations, all passing, over the
        /// committed generated catalog: `NarrativeDigest.OfLines` over `narrative/lifecycle-stress-...=pass` lines in
        /// the frozen order. It is computed from the frozen name table, so a renamed, reordered, added or dropped
        /// observation changes the literal and the gate cannot silently shrink (P-008).
        /// </summary>
        public const string LifecycleStressGeneratedDigest =
            "c743b4503dff2cf719bda1055964ec225307100bfb828a94aaa26af39639c4af";

        /// <summary>
        /// The fixture-catalog run's literal, computed the same way over `fixture:narrative/lifecycle-stress-...`
        /// names: the fixture run's observation names carry the prefix, so its table - and therefore its digest -
        /// differs from the generated run's.
        /// </summary>
        public const string LifecycleStressFixtureDigest =
            "dfe2e14f66e912febed6c2e32d0697f4f981c1c09467f500242f06a838ee3652";

        /// <summary>Runs the lifecycle stress against the committed generated catalog (GC-003 compiler output).</summary>
        public static LifecycleStressResult RunGeneratedCatalogLifecycleStress()
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

            return LifecycleStressScenario.Run(
                new NarrativeFamily(
                    catalog,
                    Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                    ProbeCatalog.CatalogFingerprint),
                false);
        }

        /// <summary>Runs the lifecycle stress against the hand-written generated-style catalog in the fixture package.</summary>
        public static LifecycleStressResult RunFixtureCatalogLifecycleStress()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return LifecycleStressScenario.Run(
                new NarrativeFamily(
                    build.Catalog,
                    Declarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                    NarrativeScenarioCatalog.Fingerprint().ToHex()),
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
        /// The lifecycle-stress half of the narrative family: the four stress manifests, the per-cycle instance
        /// identity and the slice's own mount/unmount payloads.
        /// </summary>
        public sealed partial class NarrativeFamily : ILifecycleStressFamily
        {
            /// <summary>Stable contract identity of the required service the provider exports (P-011).</summary>
            private static readonly ContractRef StressService = new ContractRef(new Id128(0x4E4C535452535356UL, 1UL), 1U);

            /// <summary>Factory key the required service's export names (the exporter's registration identity).</summary>
            private static readonly FactoryKey StressServiceFactory =
                NarrativeKeys.Key("narrative.lifecycle-stress.service-factory");

            private LifecycleStressDeclarations? stressDeclarations;

            public LifecycleStressDeclarations StressDeclarations =>
                stressDeclarations ??= NarrativeStressDeclarations();

            public string GeneratedCatalogDigest => Gc013NarrativeHost.LifecycleStressGeneratedDigest;

            public string FixtureCatalogDigest => Gc013NarrativeHost.LifecycleStressFixtureDigest;

            /// <summary>
            /// One fresh installation identity of the narrative slice, from a high ordinal base so no identity the
            /// slice's own scenarios use can collide with a stress cycle (P-004, P-005).
            /// </summary>
            public PluginInstanceId StressInstance(ulong ordinal) => NarrativeKeys.Instance(0x400UL + ordinal);

            /// <summary>The narrative slice's own O-03 mount payload, with the declared configuration hash (P-020).</summary>
            public CompositionEditPayload StressMount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope) =>
                NarrativeLifecyclePayloads.Mount(manifest, instance, scope);

            /// <summary>The narrative slice's own O-07 unmount payload (P-046, P-048).</summary>
            public CompositionEditPayload StressUnmount(PluginInstanceId instance) =>
                NarrativeLifecyclePayloads.Unmount(instance);

            /// <summary>
            /// The four manifests this run mounts beyond the family's catalog declarations, each in the slice's own
            /// chapter declaration shape and each resolving the run's own catalog factory key and configuration schema
            /// (P-009). The consumer declares a required dependency on the provider's export and one capability of its
            /// own, so a provider loss retracts rows that are really attributed to it (P-012, P-017).
            /// </summary>
            private LifecycleStressDeclarations NarrativeStressDeclarations()
            {
                CatalogPluginDeclaration reference = Declarations[0];
                FactoryKey factoryKey = reference.Manifest.FactoryKey;
                SchemaRef configSchema = reference.Manifest.ConfigSchema;

                return new LifecycleStressDeclarations(
                    installation: StressManifest(
                        NarrativeKeys.PluginTypeId(0x101UL),
                        factoryKey,
                        configSchema,
                        "narrative.lifecycle-stress.installation",
                        "narrative.lifecycle-stress.installation-binding",
                        "narrative.lifecycle-stress.installation-binding-schema",
                        "narrative.lifecycle-stress.installation-rule",
                        null,
                        null),
                    serviceConsumer: StressManifest(
                        NarrativeKeys.PluginTypeId(0x102UL),
                        factoryKey,
                        configSchema,
                        "narrative.lifecycle-stress.consumer",
                        "narrative.lifecycle-stress.consumer-binding",
                        "narrative.lifecycle-stress.consumer-binding-schema",
                        "narrative.lifecycle-stress.consumer-rule",
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
                        NarrativeKeys.PluginTypeId(0x103UL),
                        factoryKey,
                        configSchema,
                        "narrative.lifecycle-stress.provider",
                        "narrative.lifecycle-stress.provider-binding",
                        "narrative.lifecycle-stress.provider-binding-schema",
                        "narrative.lifecycle-stress.provider-rule",
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
                        NarrativeKeys.PluginTypeId(0x104UL),
                        factoryKey,
                        configSchema,
                        "narrative.lifecycle-stress.fenced",
                        "narrative.lifecycle-stress.fenced-binding",
                        "narrative.lifecycle-stress.fenced-binding-schema",
                        "narrative.lifecycle-stress.fenced-rule",
                        null,
                        null),
                    stage: NarrativeKeys.InputStage,
                    system: NarrativeKeys.InputSystem);
            }

            /// <summary>
            /// One stress manifest: the same declaration shape a narrative chapter uses (one capability contract with
            /// one output slot and one derivation rule over the reusable villager recipe), plus the declared service
            /// surface of its role. It declares no stage, no buffer, no state slot and no resource of its own, so it
            /// never enters the compiled schedule (P-019, P-043).
            /// </summary>
            private static PluginManifest StressManifest(
                PluginTypeId pluginType,
                FactoryKey factoryKey,
                SchemaRef configSchema,
                string chapterTag,
                string capabilityName,
                string schemaName,
                string ruleSuffix,
                IReadOnlyList<ServiceExport>? exports,
                IReadOnlyList<ServiceDependency>? dependencies)
            {
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
                    new List<CapabilityContract>
                    {
                        NarrativeDeclarations.Contract(
                            capabilityName, schemaName, 0, NarrativeDerivationPlan.ReplacePolicy),
                    },
                    new List<DerivationRule>
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
