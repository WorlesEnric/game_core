// GameCore.App tests — the smallest real game an application root can boot (P0.4: SADR-010, SADR-011, SADR-013).
//
// One generated-style catalog (the W1 gate's), two plugin declarations and one world:
//
//   * plugin A ("probe") declares one Exclusive capability, one rule that binds it on every target of the probe
//     recipe, and the world's only stage with one managed system that reads the probe target's binding row. The
//     catalog binds rule A's payload to configuration field K (SADR-013), whose schema default is 1000;
//   * plugin B ("rival") declares the same capability with a second Exclusive rule: mounting it next to A is a
//     capability conflict the world refuses, which is the refused-world-plan case of SADR-011;
//   * the boot script seeds one probe target under the root scope and mounts A over it.
//
// The reader system records the value it reads into a static recorder, so a test can tell what a system observed in
// a step - which is what "install config reaches systems" has to prove.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using GameCore.Unity.App;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.App.Tests
{
    /// <summary>What the probe reader system saw in the most recent step it ran.</summary>
    public static class AppProbeRecorder
    {
        public static Entity Target { get; set; } = Entity.Null;

        public static int Runs { get; set; }

        public static bool Found { get; set; }

        public static int Value { get; set; }

        public static void Reset()
        {
            Target = Entity.Null;
            Runs = 0;
            Found = false;
            Value = 0;
        }
    }

    /// <summary>The probe stage's one system: it reads the probe target's binding row and records the value.</summary>
    [DisableAutoCreation]
    public partial class AppProbeReaderSystem : SystemBase
    {
        protected override void OnUpdate()
        {
            AppProbeRecorder.Runs++;
            Entity target = AppProbeRecorder.Target;
            EntityManager entityManager = EntityManager;
            if (target == Entity.Null || !entityManager.Exists(target) || !entityManager.HasBuffer<CapabilityBinding>(target))
            {
                AppProbeRecorder.Found = false;
                return;
            }

            DynamicBuffer<CapabilityBinding> bindings = entityManager.GetBuffer<CapabilityBinding>(target);
            if (AssemblyStorage.TryFindBinding(bindings, AppProbe.Capability, 0U, out CapabilityBinding row) && row.IsActive)
            {
                AppProbeRecorder.Found = true;
                AppProbeRecorder.Value = row.Value;
                return;
            }

            AppProbeRecorder.Found = false;
        }
    }

    /// <summary>Base layout of a probe target: an empty state-slot buffer; derived rows come from the publisher.</summary>
    public sealed class AppProbeApplier : ISpawnApplier
    {
        public FactoryKey Key => AppProbe.ApplierKey;

        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            entityManager.AddBuffer<TargetSlotState>(entity);
            _ = recipe;
        }
    }

    /// <summary>Vocabulary, declarations and definitions of the probe game.</summary>
    public static class AppProbe
    {
        public const string CapabilityName = "app.probe.capability";
        public const string BindingSchemaName = "app.probe.binding";
        public const string RecipeSchemaName = "app.probe.recipe";
        public const string AlwaysPredicateName = "app.probe.predicate.always";

        /// <summary>Value of rule A's frozen manifest payload: what a system would see without SADR-013.</summary>
        public const int ManifestValue = 7;

        /// <summary>Schema default of configuration field K.</summary>
        public const int DefaultConfiguredValue = 1000;

        public static readonly CapabilityId Capability = FixtureIds.Capability(CapabilityName);
        public static readonly RuleId ProbeRule = FixtureIds.Rule("app.probe.rule.a");
        public static readonly RuleId RivalRule = FixtureIds.Rule("app.probe.rule.b");
        public static readonly Id128 ConfigField = FixtureIds.Id("app.probe.config.k");
        public static readonly FactoryKey AlwaysPredicate = FixtureIds.Key(AlwaysPredicateName);
        public static readonly FactoryKey ApplierKey = FixtureIds.Key("app.probe.applier");
        public static readonly FactoryKey ReaderSystem = FixtureIds.Key("app.probe.system.reader");
        public static readonly StageId ReaderStage = new StageId(FixtureIds.Id("app.probe.stage.reader"));
        public static readonly SchemaRef ReadDomain = FixtureIds.SchemaRef("app.probe.domain");
        public static readonly Id128 OwnerPackage = FixtureIds.Id("app.probe.package");
        public static readonly ScopeId RootScope = FixtureIds.Scope("app.probe.root");
        public static readonly TargetId Target = FixtureIds.Target("app.probe.target");
        public static readonly DefinitionRef Recipe = FixtureIds.Recipe("app.probe.recipe.definition", RecipeSchemaName);
        public static readonly PluginInstanceId ProbeInstance = FixtureIds.Instance("app.probe.instance.a");
        public static readonly PluginInstanceId RivalInstance = FixtureIds.Instance("app.probe.instance.b");
        public static readonly WorldDefinitionId WorldDefinition = new WorldDefinitionId(FixtureIds.Id("app.probe.world"));
        public static readonly Id128 Issuer = FixtureIds.Id("app.probe.issuer");

        public static ConfigDocument SchemaDefaults() =>
            ConfigDocument.Of(new ConfigField(ConfigField, ConfigFieldValue.OfInt32(DefaultConfiguredValue)));

        public static PluginManifest ProbeManifest() => Manifest(
            FixtureIds.PluginType("app.probe.plugin.a"),
            ProbeRule,
            new List<StageSpec> { ReaderStageSpec() });

        public static PluginManifest RivalManifest() => Manifest(
            FixtureIds.PluginType("app.probe.plugin.b"),
            RivalRule,
            null);

        public static CatalogPluginDeclaration ProbeDeclaration() => new CatalogPluginDeclaration(ProbeManifest(), SchemaDefaults());

        public static CatalogPluginDeclaration RivalDeclaration() => new CatalogPluginDeclaration(RivalManifest(), SchemaDefaults());

        /// <summary>The probe game's definition over the W1 gate catalog with its real fingerprint.</summary>
        public static GameApplicationDefinition.Builder Definition()
        {
            CatalogBuildResult build = W1GateCatalog.Build();
            ICatalog catalog = build.Catalog ?? throw new InvalidOperationException("the probe catalog was rejected: " + build.Describe());
            return Definition(catalog, W1GateCatalog.Fingerprint());
        }

        public static GameApplicationDefinition.Builder Definition(ICatalog catalog, ContentHash declaredFingerprint)
        {
            CatalogPluginDeclaration probe = ProbeDeclaration();
            return new GameApplicationDefinition.Builder("GameCoreAppProbe")
                .WithCatalog(catalog, declaredFingerprint)
                .AddPlugin(probe)
                .AddPlugin(RivalDeclaration())
                .WithWorld(WorldDefinition, TemporalModel.CommandDriven)
                .WithPropagation(PropagationMode.Automatic)
                .WithRootScope(RootScope)
                .AddSystem(new ManagedSystemRegistration<AppProbeReaderSystem>(ReaderSystem, ReaderStage, "AppProbeReaderSystem"))
                .WithDispatchKinds(new ScheduleDispatchKindTable().Add(ReaderSystem, SystemDispatchKind.ManagedSystem))
                .WithRecipes(new SpawnRecipeCatalog(new List<SpawnRecipe> { RecipeOf(new AppProbeApplier()) }))
                .WithValues(new FixtureValueSource().RegisterAlwaysPredicate(AlwaysPredicateName))
                .BindRuleToConfig(ProbeRule, ConfigField)
                .WithIssuer(Issuer)
                .AddBootStep(GameApplicationBootStep.Seed("seed-probe-target", Target, RootScope, Recipe))
                .AddBootStep(GameApplicationBootStep.Apply(
                    "mount-probe",
                    W1GatePayloads.Mount(probe.Manifest, ProbeInstance, RootScope, probe.SchemaDefaults)));
        }

        /// <summary>The O-03 mount of the rival provider: a second Exclusive rule over the same capability.</summary>
        public static CompositionEditPayload RivalMount()
        {
            CatalogPluginDeclaration rival = RivalDeclaration();
            return W1GatePayloads.Mount(rival.Manifest, RivalInstance, RootScope, rival.SchemaDefaults);
        }

        /// <summary>
        /// The O-05 reconfigure of plugin A that sets K to <paramref name="value"/> at configuration revision
        /// <paramref name="revision"/>, with the hash of the composed configuration the lane recomputes (P-020).
        /// </summary>
        public static CompositionEditPayload Reconfigure(CompositionHost lane, int value, ulong revision)
        {
            if (!lane.Committed.TryGetInstall(ProbeInstance, out InstallEntry? found) || found == null)
            {
                throw new InvalidOperationException("plugin A is not installed");
            }

            InstallEntry entry = found;
            ConfigDocument patch = ConfigDocument.Of(new ConfigField(ConfigField, ConfigFieldValue.OfInt32(value)));
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, entry.Manifest.ConfigSchema.Id.Value, SchemaDefaults()),
                new ConfigLayer(ConfigLayerOrigin.InheritedContribution, entry.Instance.Value, entry.Config),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, entry.Instance.Value, patch),
            });

            return new CompositionEditPayload(
                CompositionEditSubject.InstallReconfigure,
                entry.Record.Scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                entry.Record.PluginType,
                entry.Instance,
                new DefinitionRevision(revision),
                ConfigDocumentCodec.HashOf(composed.Value),
                patch,
                0,
                null,
                PropagationMode.Automatic);
        }

        private static SpawnRecipe RecipeOf(ISpawnApplier applier)
        {
            var descriptor = new TargetDescriptor(
                Recipe,
                new List<SchemaRef> { FixtureIds.SchemaRef(RecipeSchemaName) },
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);
            return new SpawnRecipe(Recipe, descriptor, new List<SchemaRef> { FixtureIds.SchemaRef(RecipeSchemaName) }, applier);
        }

        private static StageSpec ReaderStageSpec()
        {
            var read = new AccessSet(new[] { new AccessDeclaration(ReadDomain, AccessMode.Read, default(Id128)) });
            return new StageSpec(
                ReaderStage,
                1U,
                OwnerPackage,
                HostAffinity.ManagedMain,
                null,
                null,
                read,
                null,
                null,
                null,
                null,
                new List<SystemSpec>
                {
                    new SystemSpec(ReaderSystem, SystemMultiplicity.World, read, null, null, null, null),
                },
                null);
        }

        private static PluginManifest Manifest(PluginTypeId pluginType, RuleId rule, IReadOnlyList<StageSpec>? stages)
        {
            SlotId slot = FixtureIds.Slot(CapabilityName + ".slot");
            var contract = new CapabilityContract(
                FixtureIds.CapabilityRef(CapabilityName),
                0,
                new List<OutputSlotSchema> { new OutputSlotSchema(slot, FixtureIds.SchemaRef(BindingSchemaName)) },
                new List<SlotCompositionPolicy> { new SlotCompositionPolicy(slot, CompositionPolicy.Exclusive, default(FactoryKey)) },
                null);
            var derivation = new DerivationRule(
                rule,
                FixtureIds.CapabilityRef(CapabilityName),
                0,
                1U,
                new List<SchemaRef> { FixtureIds.SchemaRef(RecipeSchemaName) },
                AlwaysPredicate,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Exclusive,
                IntegrationSlotValues.WriteInt32(ManifestValue));

            return new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                W1GateKeys.CatalogSchema,
                W1GateCatalog.PluginFactoryKey,
                null,
                null,
                new List<CapabilityContract> { contract },
                new List<DerivationRule> { derivation },
                null,
                null,
                stages,
                null,
                null);
        }
    }
}
