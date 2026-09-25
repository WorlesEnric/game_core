// GameCore.Validation.ProbeHost — the GC-013 narrative family adapter.
//
// The narrative slice's declared facts (07 section 3.1's chapter tree, GC-010's chapter providers) as the GC-013
// scenario's `IGc013Family`: the scenario owns the shared scripted sequence, and this file owns everything the
// narrative genre declares — its catalog declarations, its scope tree, its live targets and the composition edits the
// sequence submits.
//
// Two things the narrative fixture package deliberately does not provide are declared here, because no narrative
// scenario needed them before GC-013 and this project must not edit `Packages/**`:
//
//   * the O-02 scope-move and O-08 mode payloads of the task's sequence (`ScopeReparent`, `ModeSet`) and the two
//     neutral scope creations the sequence publishes through, in the exact shape `NarrativeMounts.Mount` uses;
//   * the overlapping exclusive pair of step D. The narrative vocabulary declares no `Exclusive` capability (every
//     chapter binding is `Replace`), so the pair declares its own capability of that name over the same reusable
//     villager recipe. The kernel path it exercises is the same one 07 s2.4's `cards.draw-policy` case uses:
//     `CompositionPolicy.Exclusive` with two eligible candidates, which the engine refuses with `CapabilityConflict`.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using GameCore.Gameplay.Narrative;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Narrative;
using GameCore.Unity.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;
using GameCore.Validation.Generated;
using GameCore.Validation.Probe;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-013 narrative host of the qualification project: it supplies the two catalogs and the declaration sets the
    /// narrative scenario is run against, exactly as <see cref="NarrativeScenarioHost"/> does for GC-010, and hands
    /// the resulting observations to the caller.
    /// </summary>
    public static partial class Gc013NarrativeHost
    {
        /// <summary>Family label every observation name of this family carries.</summary>
        public const string Label = "narrative";

        /// <summary>Stable name of the exclusive capability step D's pair declares (P-019).</summary>
        public const string ConflictCapabilityName = "gc013.narrative.conflict-policy";

        /// <summary>Stable name of that capability's payload schema.</summary>
        public const string ConflictSchemaName = "gc013.narrative.conflict-policy-binding";

        /// <summary>Suffix of the conflict providers' rule names, in the chapters' `<tag><suffix>` shape.</summary>
        public const string ConflictRuleSuffix = ".gc013-conflict";

        /// <summary>Stratum the conflict capability occupies; nothing depends on it (P-021).</summary>
        public const int ConflictStratum = 0;

        /// <summary>Value the moved target's live state slot is seeded with; the move must not change it (P-025).</summary>
        public const int MutableStateValue = 7;

        public const ulong SessionSalt = 0x4E41524743303133UL;

        /// <summary>The exclusive capability the conflict pair declares.</summary>
        public static readonly CapabilityId ConflictCapability = NarrativeIds.Capability(ConflictCapabilityName);

        /// <summary>Two scopes no live target lives in: the neutral publications the sequence publishes through.</summary>
        public static readonly ScopeId SpareScopeA = NarrativeIds.Scope("gc013.narrative.spare-scope-a");

        public static readonly ScopeId SpareScopeB = NarrativeIds.Scope("gc013.narrative.spare-scope-b");

        /// <summary>The future villager's owner scope: the grove branch the move leaves untouched.</summary>
        public static readonly ScopeId FutureBranchScope = NarrativeKeys.GroveScope;

        /// <summary>
        /// A plain villager already living in the future branch. It materializes the published
        /// `(VillagerRecipe, GroveScope)` rule a later villager spawn inherits (P-024), while the village move leaves
        /// the branch under chapter one.
        /// </summary>
        public static readonly TargetId GroveVillager = NarrativeIds.Target("gc013.narrative.grove-villager");

        /// <summary>The one target whose descriptor declares the complete explicit opt-in (P-013).</summary>
        public static readonly TargetId OptedInTarget = NarrativeIds.Target("gc013.narrative.opted-in-villager");

        /// <summary>The opted-in target's recipe: the villager recipe's selector schema under its own identity.</summary>
        public static readonly DefinitionRef OptedInVillagerRecipe = NarrativeIds.Recipe(
            NarrativeCompositionNames.VillagerRecipe + ".gc013-opted-in.definition",
            NarrativeCompositionNames.VillagerRecipe);

        /// <summary>Installation of the first conflict provider.</summary>
        public static readonly PluginInstanceId ConflictProviderInstance = NarrativeKeys.Instance(5UL);

        /// <summary>Installation of the second conflict provider.</summary>
        public static readonly PluginInstanceId ConflictSecondProviderInstance = NarrativeKeys.Instance(6UL);

        /// <summary>Scope the first conflict provider is mounted at: the chapter-one subtree's provider scope.</summary>
        public static readonly ScopeId ConflictProviderScope = NarrativeKeys.ChapterOneScope;

        /// <summary>Scope the second conflict provider is mounted at: the world root, so its reach overlaps the first.</summary>
        public static readonly ScopeId ConflictSecondProviderScope = NarrativeKeys.RootScope;

        /// <summary>O-02: move one scope subtree under a new parent (P-025).</summary>
        public static CompositionEditPayload ScopeReparent(ScopeId scope, ScopeId newParent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeReparent,
                scope,
                newParent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>O-08: set the world propagation mode; the scope must be default or the world root (P-013).</summary>
        public static CompositionEditPayload ModeSet(PropagationMode mode)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ModeSet,
                default(ScopeId),
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                mode);
        }

        /// <summary>
        /// O-02: create one scope under an existing parent, with no isolation, no exclusions and no grants. The
        /// sequence uses two of these as publications that change no live target's assembly (P-010).
        /// </summary>
        internal static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                null,
                null,
                null,
                default(PluginTypeId),
                default(PluginInstanceId),
                DefinitionRevision.Zero,
                ContentHash.Empty,
                null,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>Runs the GC-013 sequence against the committed generated catalog (GC-003 compiler output).</summary>
        public static Gc013ScenarioResult RunGeneratedCatalog()
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

            return Gc013Scenario.Run(new NarrativeFamily(
                catalog,
                Declarations(ProbeCatalog.FixturePluginKey, W1GateKeys.CatalogSchema),
                ProbeCatalog.CatalogFingerprint));
        }

        /// <summary>Runs the GC-013 sequence against the hand-written generated-style catalog in the fixture package.</summary>
        public static Gc013ScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = NarrativeScenarioCatalog.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style narrative catalog was rejected: " + build.Describe());
            }

            return Gc013Scenario.Run(new NarrativeFamily(
                build.Catalog,
                Declarations(NarrativeScenarioCatalog.PluginFactoryKey, NarrativeScenarioCatalog.RecordSchema),
                NarrativeScenarioCatalog.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs both catalogs and returns the combined observations: the generated-catalog steps keep their names, and
        /// the fixture-catalog steps are prefixed with <see cref="Gc013Scenario.FixtureRunPrefix"/> so no two collide.
        /// </summary>
        public static IReadOnlyList<Gc013Step> RunBoth(out Gc013ScenarioResult generated, out Gc013ScenarioResult fixture)
        {
            generated = RunGeneratedCatalog();
            fixture = RunFixtureCatalog();

            var combined = new List<Gc013Step>(generated.Steps.Count + fixture.Steps.Count);
            for (int i = 0; i < generated.Steps.Count; i++)
            {
                combined.Add(generated.Steps[i]);
            }

            for (int i = 0; i < fixture.Steps.Count; i++)
            {
                Gc013Step step = fixture.Steps[i];
                combined.Add(new Gc013Step(Gc013Scenario.FixtureRunPrefix + step.Name, step.Passed, step.Detail));
            }

            return combined;
        }

        /// <summary>
        /// The narrative declaration set one run mounts: the two chapter providers, the forward provider and the two
        /// providers of the exclusive pair. Every declaration resolves the catalog's registered plugin factory and
        /// configuration schema (P-009).
        /// </summary>
        internal static IReadOnlyList<CatalogPluginDeclaration> Declarations(FactoryKey factoryKey, SchemaRef configSchema)
        {
            return new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ChapterProvider(NarrativeKeys.PluginTypeId(1UL), factoryKey, configSchema),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ChapterTwoProvider(NarrativeKeys.PluginTypeId(2UL), factoryKey, configSchema),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    NarrativeDeclarations.ForwardProvider(NarrativeKeys.PluginTypeId(4UL), factoryKey, configSchema),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    ConflictManifest(
                        NarrativeKeys.PluginTypeId(5UL),
                        factoryKey,
                        configSchema,
                        "gc013-narrative-conflict-one",
                        true,
                        1),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    ConflictManifest(
                        NarrativeKeys.PluginTypeId(6UL),
                        factoryKey,
                        configSchema,
                        "gc013-narrative-conflict-two",
                        false,
                        2),
                    ConfigDocument.Empty),
            };
        }

        /// <summary>
        /// One provider of the exclusive pair. Exactly one of the two declares the capability contract, because a
        /// capability identity has one declaration per catalog revision: two differing declarations of one identity
        /// are refused as a catalog error (P-019, 02 section 4), and identical ones are deduplicated.
        /// </summary>
        private static PluginManifest ConflictManifest(
            PluginTypeId pluginType,
            FactoryKey factoryKey,
            SchemaRef configSchema,
            string ruleStableName,
            bool declaresContract,
            int value)
        {
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
                declaresContract
                    ? new List<CapabilityContract>
                    {
                        NarrativeDeclarations.Contract(
                            ConflictCapabilityName, ConflictSchemaName, ConflictStratum, "Exclusive"),
                    }
                    : new List<CapabilityContract>(),
                new List<DerivationRule>
                {
                    // The rule selects the reusable villager recipe, exactly like a chapter's dialogue rule, so its
                    // candidates are the same eligible targets and the two providers really overlap (P-013, P-015).
                    NarrativeDeclarations.Rule(
                        ruleStableName,
                        ConflictRuleSuffix,
                        ConflictCapabilityName,
                        ConflictSchemaName,
                        ConflictStratum,
                        NarrativeCompositionNames.VillagerRecipe,
                        string.Empty,
                        "Exclusive",
                        value),
                },
                null,
                null,
                null,
                null,
                null);
        }

        // The W4 integration gate's lifecycle and state-policy half lives in `W4GateNarrativeHost.cs`: the type is
        // partial so that file adds the `IW4GateFamily` surface without re-declaring anything here (P-011, P-032).
        public sealed partial class NarrativeFamily : IGc013Family
        {
            private readonly ICatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly SpawnRecipeCatalog recipes;
            private readonly MigrationRegistry migrations;
            private readonly IDerivationValueSource values;
            private readonly IReadOnlyList<CompositionEditPayload> spareScopes;
            private readonly IReadOnlyList<TargetId> automaticTargets;

            public NarrativeFamily(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                string catalogFingerprint)
            {
                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                this.declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
                CatalogFingerprint = catalogFingerprint ?? string.Empty;

                var applier = new NarrativeRecipeApplier();
                var catalogRecipes = new List<SpawnRecipe>(NarrativeRecipes.Catalog(applier).Recipes);
                catalogRecipes.Add(OptedInVillager(applier));
                recipes = new SpawnRecipeCatalog(catalogRecipes);

                migrations = new MigrationRegistry(new List<ISlotMigration>
                {
                    new NarrativeConversationNodeMigration(),
                    new NarrativeConversationStatusMigration(),
                });

                values = new FixtureValueSource().RegisterAlwaysPredicate(NarrativeCompositionNames.AlwaysPredicateName);

                spareScopes = new List<CompositionEditPayload>
                {
                    ScopeCreate(SpareScopeA, NarrativeKeys.RootScope),
                    ScopeCreate(SpareScopeB, NarrativeKeys.RootScope),
                };

                // The eligible existing targets that Automatic must reach with no import and no opt-in of their own
                // (P-013). Eligibility for the derived dialogue binding is selector-based: the villager recipe is the
                // set. Mara is in the branch the move relocates under the chapter-two provider, the grove villager is
                // the existing same-scope rule source the future villager inherits from, and Sailor lives in the
                // chapter-two harbor the move leaves alone. The gate and encounter targets are eligible for their own
                // capabilities, not for this one.
                automaticTargets = new List<TargetId>
                {
                    NarrativeKeys.Mara,
                    GroveVillager,
                    NarrativeKeys.Sailor,
                };
            }

            public string Label => Gc013NarrativeHost.Label;

            public string CatalogFingerprint { get; }

            public ulong SessionSalt => Gc013NarrativeHost.SessionSalt;

            public Id128 Issuer => NarrativeKeys.Issuer;

            public ICatalog Catalog => catalog;

            public IReadOnlyList<CatalogPluginDeclaration> Declarations => declarations;

            public ScopeId WorldRootScope => NarrativeKeys.RootScope;

            public CompositionLaneSeed LaneSeed =>
                CompositionLaneSeed.InitialAssembly.WithScopes(NarrativeScopes.DeclaredChildren());

            public IReadOnlyList<CompositionEditPayload> SetupEdits => Array.Empty<CompositionEditPayload>();

            public IReadOnlyList<CompositionEditPayload> SpareScopeEdits => spareScopes;

            public PipelineDescriptorReport CompilePipeline()
            {
                var kinds = new ScheduleDispatchKindTable()
                    .Add(NarrativeKeys.InputSystem, SystemDispatchKind.ManagedSystem)
                    .Add(NarrativeKeys.DialogueSystem, SystemDispatchKind.ManagedSystem)
                    .Add(NarrativeKeys.QuestSystem, SystemDispatchKind.ManagedSystem)
                    .Add(NarrativeKeys.GateSystem, SystemDispatchKind.ManagedSystem)
                    .Add(NarrativeKeys.EncounterSystem, SystemDispatchKind.ManagedSystem)
                    .Add(NarrativeKeys.OutputSystem, SystemDispatchKind.ManagedSystem);

                var manifests = new List<PluginManifest>(declarations.Count);
                for (int i = 0; i < declarations.Count; i++)
                {
                    manifests.Add(declarations[i].Manifest);
                }

                return OwnershipSchedulePipeline.Build(manifests, kinds, new NarrativeSlotMigrations());
            }

            public WorldCreateRequest CreateRequest(WorldId world, OperationId operation)
                => NarrativeRegistration.CommandDrivenRequest(world, operation, ContentHash.Empty);

            public UnityWorldRegistration CreateRegistration(ScheduleAdaptation adaptation)
                => NarrativeRegistration.Create(adaptation, NarrativeRegistration.Systems());

            public SpawnRecipeCatalog CreateRecipes() => recipes;

            public MigrationRegistry CreateMigrations() => migrations;

            public IDerivationValueSource CreateValues() => values;

            public bool SeedTargets(Gc013WorldContext context)
            {
                Seed(context, NarrativeKeys.Mara, NarrativeKeys.VillageScope, NarrativeKeys.VillagerRecipe);
                Seed(context, NarrativeKeys.GateEast, NarrativeKeys.VillageScope, NarrativeKeys.QuestGateRecipe);
                Seed(context, NarrativeKeys.EncounterOak, NarrativeKeys.GroveScope, NarrativeKeys.QuestEncounterRecipe);
                Seed(context, GroveVillager, NarrativeKeys.GroveScope, NarrativeKeys.VillagerRecipe);
                Seed(context, NarrativeKeys.CrowdProp, NarrativeKeys.VillageScope, NarrativeKeys.DecorativeCrowdRecipe);
                Seed(context, NarrativeKeys.Sailor, NarrativeKeys.HarborScope, NarrativeKeys.VillagerRecipe);
                Seed(context, NarrativeKeys.Display, NarrativeKeys.MuseumScope, NarrativeKeys.VillagerRecipe);

                // Real gameplay state, not derived data: the moved target's conversation node is seeded at the
                // version its descriptor declares, so no migration runs and the value is a plain live fact whose
                // survival across the move is a statement about state rather than about the derivation (P-025, P-032).
                if (!context.Seeder.TrySeedSlot(
                        NarrativeKeys.Mara,
                        NarrativeKeys.DialogueOwner,
                        NarrativeKeys.ConversationNodeSlot,
                        NarrativeKeys.ConversationDomain.Version,
                        MutableStateValue,
                        out DiagnosticCode code,
                        out string detail))
                {
                    throw new InvalidOperationException(
                        "seeding the moved target's live state slot was refused: " + code + ": " + detail);
                }

                return true;
            }

            public bool SeedOptedInTarget(Gc013WorldContext context)
            {
                Seed(context, OptedInTarget, FutureBranchScope, OptedInVillagerRecipe);
                return true;
            }

            public void PrepareSpawn()
            {
                // The narrative applier installs a villager's base layout from the recipe identity itself, so a
                // spawn needs no per-run state (04 section 6).
            }

            public CompositionEditPayload MountProvider() => NarrativeMounts.Mount(
                declarations[0].Manifest,
                NarrativeKeys.ChapterOneInstall,
                NarrativeKeys.ChapterOneScope,
                declarations[0].SchemaDefaults);

            public CompositionEditPayload MountSecondProvider() => NarrativeMounts.Mount(
                declarations[1].Manifest,
                NarrativeKeys.ChapterTwoInstall,
                NarrativeKeys.ChapterTwoScope,
                declarations[1].SchemaDefaults);

            public CompositionEditPayload ScopeReparent()
                => Gc013NarrativeHost.ScopeReparent(NarrativeKeys.VillageScope, NarrativeKeys.ChapterTwoScope);

            public CompositionEditPayload ModeSet(PropagationMode mode) => Gc013NarrativeHost.ModeSet(mode);

            public CompositionEditPayload MountConflictProvider() => NarrativeMounts.Mount(
                declarations[3].Manifest,
                ConflictProviderInstance,
                ConflictProviderScope,
                declarations[3].SchemaDefaults);

            public CompositionEditPayload MountConflictSecondProvider() => NarrativeMounts.Mount(
                declarations[4].Manifest,
                ConflictSecondProviderInstance,
                ConflictSecondProviderScope,
                declarations[4].SchemaDefaults);

            public ScopeId ProviderScope => NarrativeKeys.ChapterOneScope;

            public ScopeId MovedScope => NarrativeKeys.VillageScope;

            public ScopeId MoveDestination => NarrativeKeys.ChapterTwoScope;

            public PluginInstanceId SecondProviderInstance => NarrativeKeys.ChapterTwoInstall;

            public PluginInstanceId ConflictProviderInstance => Gc013NarrativeHost.ConflictProviderInstance;

            public PluginInstanceId ConflictSecondProviderInstance => Gc013NarrativeHost.ConflictSecondProviderInstance;

            public ScopeId ConflictProviderScope => Gc013NarrativeHost.ConflictProviderScope;

            public ScopeId ConflictSecondProviderScope => Gc013NarrativeHost.ConflictSecondProviderScope;

            public CapabilityId DerivedCapability => NarrativeKeys.DialogueBinding;

            public CapabilityId ConflictCapability => Gc013NarrativeHost.ConflictCapability;

            public int ProviderValue => NarrativeChapters.Get(NarrativeChapters.ChapterOneTag).BindingOrdinal;

            public int SecondProviderValue => NarrativeChapters.Get(NarrativeChapters.ChapterTwoTag).BindingOrdinal;

            public IReadOnlyList<TargetId> AutomaticTargets => automaticTargets;

            public TargetId MovedTarget => NarrativeKeys.Mara;

            public TargetId IsolatedTarget => NarrativeKeys.Display;

            public TargetId IneligibleTarget => NarrativeKeys.CrowdProp;

            public TargetId OptedInTarget => Gc013NarrativeHost.OptedInTarget;

            public TargetId FutureTarget => NarrativeKeys.FutureVillager;

            public DefinitionRef FutureRecipe => NarrativeKeys.VillagerRecipe;

            public ScopeId FutureScope => FutureBranchScope;

            public OwnerId MutableOwner => NarrativeKeys.DialogueOwner;

            public SlotId MutableSlot => NarrativeKeys.ConversationNodeSlot;

            public int MutableValue => MutableStateValue;

            /// <summary>
            /// The opted-in target's recipe: the villager recipe's own selector schema under a distinct definition
            /// identity, with a complete explicit opt-in naming the chapter-one installation (P-013).
            /// </summary>
            private static SpawnRecipe OptedInVillager(ISpawnApplier applier)
            {
                var schemas = new List<SchemaRef>
                {
                    NarrativeIds.SchemaRef(NarrativeCompositionNames.VillagerRecipe, 1U),
                };

                var descriptor = new TargetDescriptor(
                    OptedInVillagerRecipe,
                    schemas,
                    null,
                    null,
                    default(AssetAdapterDescriptor),
                    null,
                    null,
                    new List<TargetOptIn>
                    {
                        new TargetOptIn(
                            new ProviderInstallationId(NarrativeKeys.ChapterOneInstall.Value),
                            NarrativeKeys.DialogueBinding),
                    },
                    null);

                return new SpawnRecipe(OptedInVillagerRecipe, descriptor, schemas, applier);
            }

            private static void Seed(Gc013WorldContext context, TargetId target, ScopeId scope, DefinitionRef recipe)
            {
                if (!context.Seeder.TrySeed(
                        target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                {
                    throw new InvalidOperationException(
                        "target " + target.ToString() + " was refused: " + code + ": " + detail);
                }

                if (!context.Seeder.TryGetEntity(target, out Entity _))
                {
                    throw new InvalidOperationException(
                        "target " + target.ToString() + " was created but the registry cannot resolve it (P-005).");
                }
            }
        }
    }
}
