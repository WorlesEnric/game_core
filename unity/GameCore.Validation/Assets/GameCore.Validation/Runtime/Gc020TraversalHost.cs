// GameCore.Validation.ProbeHost — the GC-020 traversal family host.
//
// `Gc020Family.IGc020Family` adds the fixed-step surface to GC-013's `IGc013Family`; this file owns the one family
// that answers it. The traversal genre is NEW in GC-020, so unlike `Gc013NarrativeHost` and `Gc013CardsHost` this is
// not a `partial` part of a host some earlier gate already declared: it is its own static class with its own nested
// family class, mirroring that shape (`Label`, the two catalog runners, `RunBoth`, and a nested family implementing
// the whole family interface) without the `partial` keyword or the earlier gates' edit sequences.
//
// WHAT THIS FILE DECLARES, AND WHY.
//   * the catalog: the traversal fixture's own generated-style declaration table, `TraversalCatalogTable`, built and
//     fingerprinted exactly as `Gc013CardsHost.RunFixtureCatalog` builds the card one (P-028, P-009);
//   * the lane seed: the course's declared scope tree, carried as the world definition's own tree exactly the way
//     `Gc013NarrativeHost.NarrativeFamily` carries the chapter tree, because a scope is not an assembly and
//     publishing creates for scopes the seed already declares would advance the composition counter with no matching
//     assembly publication (P-010, P-006) — so `SetupEdits` (the fixture's own `ScopeCreates()`, the same tree as an
//     edit sequence) is declared for interface parity and deliberately NOT published by the GC-020 runner;
//   * the live targets: the course, the valley runner at x = 0 with the seeded 1.00 m/s velocity, checkpoint 1 in the
//     valley, the isolated display runner in the showcase, the ridge runner and checkpoint 2 — plus the one target
//     whose descriptor carries a COMPLETE explicit opt-in naming the tailwind installation and the acceleration
//     capability, which is what keeps its binding in Conservative mode (P-013);
//   * the traversal surface: the five stage ids, the five dispatch keys and `traversal.acceleration`, so the card and
//     narrative declarations can be walked and asserted free of them (P-001, P-059).
//
// THE ONE CATALOG.  There is no committed generated traversal catalog: `Assets/GameCore.Validation/GeneratedTraversal`
// does not exist, so `RunGeneratedCatalog` is deliberately not written. The gate runs the fixture catalog ONCE and
// says so in its observation details and label; `RunBoth` therefore runs that single catalog and hands the same
// result back under both out-parameters, documented at the method rather than faked with a renamed second run.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.Planning;
using GameCore.Planning.Ownership;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Time;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>
    /// GC-020 traversal host of the qualification project: it supplies the catalog, the declaration set and the
    /// family the fixed-step scenario is run against, and hands the resulting observations to the caller.
    /// </summary>
    public static partial class Gc020TraversalHost
    {
        /// <summary>Family label every observation name of this family carries.</summary>
        public const string Label = "traversal";

        /// <summary>
        /// True when a committed generated traversal catalog is on disk. It is false: GC-003 has emitted no
        /// traversal catalog for GC-020, so this gate runs the single hand-written generated-style catalog.
        /// </summary>
        public const bool GeneratedCatalogPresent = false;

        /// <summary>Where a committed generated traversal catalog would live, for the report and the diagnostics.</summary>
        public const string GeneratedCatalogDirectory = "Assets/GameCore.Validation/GeneratedTraversal";

        /// <summary>Package version every traversal declaration carries (mirrors the gameplay package's own).</summary>
        private const string PackageVersion = "1.0.0";

        /// <summary>Stable name of the first provider of the gate-declared exclusive pair (P-019).</summary>
        private const string ConflictProviderStableName = "gc020.traversal.conflict-one";

        /// <summary>Stable name of the second provider of that pair.</summary>
        private const string ConflictSecondProviderStableName = "gc020.traversal.conflict-two";

        /// <summary>Stable name of the gate-declared exclusive capability itself.</summary>
        public const string ConflictCapabilityName = "gc020.traversal.conflict-policy";

        /// <summary>Stable name of that capability's payload schema.</summary>
        public const string ConflictSchemaName = "gc020.traversal.conflict-policy-binding";

        /// <summary>Suffix of the pair's rule identities.</summary>
        public const string ConflictRuleSuffix = ".gc020-conflict";

        /// <summary>Stratum the gate-declared exclusive capability occupies; nothing depends on it (P-021).</summary>
        public const int ConflictStratum = 0;

        public const ulong SessionSalt = 0x5452415645525341UL;

        /// <summary>
        /// The exclusive capability of the gate-declared conflict pair. The traversal vocabulary declares exactly one
        /// capability (`traversal.acceleration`), and its contract composes `Additive`, so the traversal package
        /// cannot express an exclusive conflict at all: an `Exclusive` pair over `traversal.acceleration` would
        /// contradict the package's own declaration. The pair therefore declares its own `Exclusive` capability,
        /// exactly as `Gc013NarrativeHost.ConflictManifest` does for the narrative genre, and no GC-020 observation
        /// publishes it. It is declared, not invented as something the traversal package claims.
        /// </summary>
        public static readonly CapabilityId ConflictCapability = TraversalIdentity.Capability(ConflictCapabilityName);

        /// <summary>Two scopes no live target lives in: the neutral publications a longer sequence could publish through.</summary>
        public static readonly ScopeId SpareScopeA = TraversalIdentity.Scope("gc020.traversal.spare-scope-a");

        public static readonly ScopeId SpareScopeB = TraversalIdentity.Scope("gc020.traversal.spare-scope-b");

        /// <summary>The one target whose descriptor declares the complete explicit opt-in (P-013).</summary>
        public static readonly TargetId OptedInTarget = TraversalIdentity.Target("gc020.traversal.opted-in-runner");

        /// <summary>
        /// The opted-in target's recipe: the runner recipe's own selectors under a distinct definition identity, with
        /// a complete explicit opt-in naming the tailwind installation and the acceleration capability, so Automatic
        /// and Conservative agree about it and it never loses its row (P-013).
        /// </summary>
        public static readonly DefinitionRef OptedInRunnerRecipe = TraversalIdentity.Recipe(
            TraversalVocabulary.RunnerRecipe + ".gc020-opted-in.definition",
            TraversalVocabulary.RunnerRecipe);

        /// <summary>
        /// The course entity's own recipe. The fixture declares the course TARGET but no recipe for it, and a target
        /// a seeder cannot resolve a recipe for cannot be seeded at all (P-015), so the gate declares the one recipe
        /// that installs the course entity's storage — the same `TraversalAccess.InstallCourseStorage` a package
        /// system reads.
        /// </summary>
        public static readonly DefinitionRef CourseRecipe = TraversalIdentity.Recipe(
            TraversalCourseTargets.CourseStableName + ".definition",
            TraversalCourseTargets.CourseStableName);

        /// <summary>Installation of the first conflict provider: the valley, the tailwind provider's own scope.</summary>
        public static readonly PluginInstanceId ConflictProviderInstance =
            TraversalKeys.Instance(ConflictProviderStableName);

        /// <summary>Installation of the second conflict provider: the course root, so its reach overlaps the first's.</summary>
        public static readonly PluginInstanceId ConflictSecondProviderInstance =
            TraversalKeys.Instance(ConflictSecondProviderStableName);

        /// <summary>Scope the first conflict provider is mounted at.</summary>
        public static readonly ScopeId ConflictProviderScope = TraversalCourseComposition.ValleyScope;

        /// <summary>Scope the second conflict provider is mounted at.</summary>
        public static readonly ScopeId ConflictSecondProviderScope = TraversalCourseComposition.CourseRoot;

        /// <summary>
        /// Runs the GC-020 sequence against the one traversal catalog the project has: the hand-written
        /// generated-style table in the fixture package. There is no committed generated traversal catalog, so this
        /// runs that single catalog and the observations report it (`generatedCatalog=absent`).
        /// </summary>
        public static Gc020ScenarioResult RunFixtureCatalog()
        {
            CatalogBuildResult build = TraversalCatalogTable.Build();
            if (build.Catalog == null)
            {
                throw new InvalidOperationException(
                    "the hand-written generated-style traversal catalog was rejected: " + build.Describe());
            }

            return Gc020Scenario.Run(new CourseFamily(
                build.Catalog,
                Declarations(),
                TraversalCatalogTable.Fingerprint().ToHex()));
        }

        /// <summary>
        /// Runs the gate's single catalog and returns its steps. `generated` is the SAME run, not a second one: no
        /// committed generated traversal catalog exists (`GeneratedCatalogPresent` is false), and a renamed copy of
        /// one run reported as two catalogs would be a dishonest observation. Both out-parameters therefore name the
        /// one real run, under the one real label.
        /// </summary>
        public static IReadOnlyList<Gc020Step> RunBoth(
            out Gc020ScenarioResult generated,
            out Gc020ScenarioResult fixture)
        {
            fixture = RunFixtureCatalog();
            generated = fixture;
            return fixture.Steps;
        }

        /// <summary>
        /// The traversal declaration set one run mounts: the course runtime, both acceleration modifiers and the two
        /// providers of the gate-declared exclusive pair. Every declaration resolves the traversal catalog's
        /// registered plugin factory and configuration schema (P-009), and the pair's capability is declared by the
        /// gate because the traversal package declares no exclusive capability (see <see cref="ConflictCapability"/>).
        /// </summary>
        internal static IReadOnlyList<CatalogPluginDeclaration> Declarations()
        {
            return new List<CatalogPluginDeclaration>
            {
                new CatalogPluginDeclaration(TraversalCourseManifests.CourseRuntimeManifest(), ConfigDocument.Empty),
                new CatalogPluginDeclaration(TraversalCourseManifests.TailwindManifest(), ConfigDocument.Empty),
                new CatalogPluginDeclaration(TraversalCourseManifests.HeadwindManifest(), ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    ConflictManifest(
                        TraversalKeys.PluginType(ConflictProviderStableName),
                        ConflictProviderStableName,
                        true,
                        TraversalVocabulary.TailwindMilli),
                    ConfigDocument.Empty),
                new CatalogPluginDeclaration(
                    ConflictManifest(
                        TraversalKeys.PluginType(ConflictSecondProviderStableName),
                        ConflictSecondProviderStableName,
                        false,
                        TraversalVocabulary.HeadwindMilli),
                    ConfigDocument.Empty),
            };
        }

        /// <summary>
        /// One provider of the gate-declared exclusive pair. Exactly one of the two declares the capability contract,
        /// because one capability identity has one declaration per catalog revision: two differing declarations of one
        /// identity are refused as a catalog error and identical ones are deduplicated (P-019, 02 s4).
        /// </summary>
        private static PluginManifest ConflictManifest(
            PluginTypeId pluginType,
            string ruleStableName,
            bool declaresContract,
            int value)
        {
            SlotId slot = TraversalIdentity.Slot(ConflictCapabilityName + ".slot");
            var contract = new CapabilityContract(
                TraversalIdentity.CapabilityRef(ConflictCapabilityName, 1U),
                ConflictStratum,
                new List<OutputSlotSchema>
                {
                    new OutputSlotSchema(slot, TraversalIdentity.SchemaRef(ConflictSchemaName, 1U)),
                },
                new List<SlotCompositionPolicy>
                {
                    new SlotCompositionPolicy(slot, CompositionPolicy.Exclusive, default(FactoryKey)),
                },
                null);

            // The rule selects the reusable runner recipe, exactly like an acceleration rule, so the two providers'
            // candidates are the same runners and the pair really overlaps (P-013, P-015, P-019).
            var rule = new DerivationRule(
                TraversalIdentity.Rule(ruleStableName + ConflictRuleSuffix),
                TraversalIdentity.CapabilityRef(ConflictCapabilityName, 1U),
                ConflictStratum,
                1U,
                new List<SchemaRef> { TraversalVocabulary.SelectorSchema(TraversalVocabulary.RunnerRecipe) },
                TraversalVocabulary.AlwaysPredicateKey,
                null,
                PropagationReach.SelfAndDescendants,
                true,
                0,
                CompositionPolicy.Exclusive,
                TraversalPayloadCodec.WriteAcceleration(value));

            return new PluginManifest(
                pluginType,
                PackageVersion,
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                TraversalKeys.ConfigSchema,
                TraversalKeys.PluginFactoryKey,
                null,
                null,
                declaresContract
                    ? new List<CapabilityContract> { contract }
                    : new List<CapabilityContract>(),
                new List<DerivationRule> { rule },
                null,
                null,
                null,
                null,
                null);
        }

        /// <summary>
        /// The traversal course's declared facts as the GC-020 scenario's `IGc020Family`: its catalog data, its scope
        /// tree, its live targets, its two acceleration modifiers and its fixed-step identities.
        /// </summary>
        public sealed partial class CourseFamily : IGc020Family
        {
            private readonly ICatalog catalog;
            private readonly IReadOnlyList<CatalogPluginDeclaration> declarations;
            private readonly SpawnRecipeCatalog recipes;
            private readonly MigrationRegistry migrations;
            private readonly IDerivationValueSource values;
            private readonly IReadOnlyList<CompositionEditPayload> spareScopes;
            private readonly IReadOnlyList<TargetId> courseTargets;
            private readonly IReadOnlyList<ScopeRecord> declaredScopes;
            private readonly ITraversalRunnerApplier runnerApplier;
            private readonly TraversalVolumeApplier volumeApplier;

            private ulong physicsDeclaredSession;

            /// <summary>The course over the runtime-declared recipe catalog, under the family's fixed session salt.</summary>
            public CourseFamily(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                string catalogFingerprint)
                : this(catalog, declarations, catalogFingerprint, RuntimeCatalogCoverageRecipeSource.Instance,
                    Gc020TraversalHost.SessionSalt)
            {
            }

            /// <summary>
            /// The course over one recipe source (GC-025's bake/runtime parity comparison): the runtime-declared
            /// recipes, or the same recipes materialized from the Editor-baked artifact. The course itself is
            /// unchanged; only where its recipe data comes from differs.
            /// </summary>
            public CourseFamily(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                string catalogFingerprint,
                ICatalogCoverageRecipeSource recipeSource)
                : this(catalog, declarations, catalogFingerprint, recipeSource, Gc020TraversalHost.SessionSalt)
            {
            }

            /// <summary>
            /// The course under an explicit session salt (GC-025). `Gc020Host.SessionSalt` makes a gate run
            /// reproducible; a caller that needs two provably different sessions calls this overload, because a
            /// session identity is derived from the salt and is never reused (P-004).
            /// </summary>
            public CourseFamily(
                ImmutableCatalog catalog,
                IReadOnlyList<CatalogPluginDeclaration> declarations,
                string catalogFingerprint,
                ICatalogCoverageRecipeSource recipeSource,
                ulong sessionSalt)
            {
                if (recipeSource == null)
                {
                    throw new ArgumentNullException(nameof(recipeSource));
                }

                this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
                this.declarations = declarations ?? throw new ArgumentNullException(nameof(declarations));
                CatalogFingerprint = catalogFingerprint ?? string.Empty;
                RecipeSourceLabel = recipeSource.Label;
                SessionSalt = sessionSalt;

                runnerApplier = recipeSource.CreateRunnerApplier();
                volumeApplier = new TraversalVolumeApplier();

                // The recipe source's own closed catalog plus the two recipes no fixture declares: the course
                // entity's storage recipe and the explicitly opted-in runner. Both are ordinary precompiled recipes
                // of this gate's family, so a seeded or spawned target resolves one (P-015, P-024).
                var allRecipes = new List<SpawnRecipe>(recipeSource.Catalog(runnerApplier, volumeApplier).Recipes)
                {
                    CourseRecipeOf(new CourseApplier()),
                    OptedInRunner(runnerApplier),
                };
                recipes = new SpawnRecipeCatalog(allRecipes);

                migrations = new MigrationRegistry(null);
                values = TraversalDerivationValueSource.Default();

                spareScopes = new List<CompositionEditPayload>
                {
                    TraversalCourseComposition.ScopeCreate(SpareScopeA, TraversalCourseComposition.CourseRoot, false),
                    TraversalCourseComposition.ScopeCreate(SpareScopeB, TraversalCourseComposition.CourseRoot, false),
                };

                // The runners the course owns, in canonical target order. `runner-c` does not exist until the spawn
                // publication of observation 9, so a reader of this list checks liveness before reading a target.

                courseTargets = new List<TargetId>
                {
                    TraversalCourseTargets.RunnerA,
                    TraversalCourseTargets.RunnerB,
                    TraversalCourseTargets.RunnerC,
                    TraversalCourseTargets.RunnerDisplay,
                };

                declaredScopes = CourseScopeRecords();
            }

            /// <summary>Diagnostic label of the recipe source this course materialized its recipes from (GC-025).</summary>
            public string RecipeSourceLabel { get; }

            public string Label => Gc020TraversalHost.Label;

            public string CatalogFingerprint { get; }

            public ulong SessionSalt { get; }

            public Id128 Issuer => TraversalKeys.Issuer;

            public ICatalog Catalog => catalog;

            public IReadOnlyList<CatalogPluginDeclaration> Declarations => declarations;

            public ScopeId WorldRootScope => TraversalCourseComposition.CourseRoot;

            public CompositionLaneSeed LaneSeed =>
                CompositionLaneSeed.InitialAssembly.WithScopes(declaredScopes);

            /// <summary>
            /// The fixture's own scope-create sequence for the course tree. The GC-020 runner does NOT publish it: the
            /// tree is already the world definition's declared tree in <see cref="LaneSeed"/> (exactly the way
            /// `Gc013NarrativeHost` carries the chapter tree), so publishing its creates would be a duplicate create
            /// for scopes the seed already declares (P-010, P-006).
            /// </summary>
            public IReadOnlyList<CompositionEditPayload> SetupEdits => TraversalCourseComposition.ScopeCreates();

            public IReadOnlyList<CompositionEditPayload> SpareScopeEdits => spareScopes;

            public PipelineDescriptorReport CompilePipeline()
            {
                var manifests = new List<PluginManifest>(declarations.Count);
                for (int i = 0; i < declarations.Count; i++)
                {
                    manifests.Add(declarations[i].Manifest);
                }

                return OwnershipSchedulePipeline.Build(
                    manifests,
                    TraversalRegistration.DispatchKinds(),
                    new SlotMigrationRegistry());
            }

            /// <summary>
            /// The world definition of a fixed-step traversal course: the reference's configured 20 ms step with the
            /// declared four-step catch-up bound (07 s4.1/s4.2, P-036). The catalog hash is the run's own catalog
            /// fingerprint, so the world is created against the catalog this family was built over (P-028).
            /// </summary>
            public WorldCreateRequest CreateRequest(WorldId world, OperationId operation)
            {
                ContentHash hash = ContentHash.TryParseHex(CatalogFingerprint, out ContentHash parsed)
                    ? parsed
                    : ContentHash.Empty;
                return TraversalRegistration.FixedStepRequest(world, operation, hash);
            }

            public UnityWorldRegistration CreateRegistration(ScheduleAdaptation adaptation)
                => TraversalRegistration.Create(adaptation, TraversalRegistration.Systems());

            public SpawnRecipeCatalog CreateRecipes() => recipes;

            public MigrationRegistry CreateMigrations() => migrations;

            public IDerivationValueSource CreateValues() => values;

            public bool SeedTargets(Gc013WorldContext context)
            {
                // The course entity: the state executor that owns progress, crossings and the committed image. Its
                // recipe installs exactly that storage, which is what the checkpoint and output stages read.
                Seed(context, CourseTarget, WorldRootScope, CourseRecipe);

                // The valley runner the one-step numeric assertion reads: position zero, seeded 1.00 m/s (07 s4.3).
                runnerApplier.InitialPosition = new TraversalVector3i(0, 0, 0);
                runnerApplier.InitialVelocity = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);
                Seed(context, VelocityAssertedTarget, ValleyRunnerScope, TraversalKeys.RunnerRecipe);

                // Checkpoint 1 sits on the valley runner's start point, so the first integrated step really produces
                // a crossing the checkpoint owner can accept (07 s4.2's course definition is pure data).
                volumeApplier.NextOrdinal = TraversalVocabulary.CheckpointOneOrdinal;
                volumeApplier.Center = new TraversalVector3i(TraversalKeys.CheckpointOneXMilli, 0, 0);
                volumeApplier.Radius = TraversalKeys.CheckpointRadiusMilli;
                Seed(context, CheckpointOne, TraversalCourseComposition.ValleyScope, TraversalKeys.CheckpointRecipe);

                // The isolated display runner: the same two selectors as a runner, so the showcase scope's
                // acceleration boundary is what blocks its contribution rather than ineligibility (P-015, P-016). It
                // starts far from both volumes so its motion adds no crossing to the course's evidence.
                runnerApplier.InitialPosition = new TraversalVector3i(TraversalKeys.CheckpointTwoXMilli * 20, 0, 0);
                runnerApplier.InitialVelocity = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);
                Seed(context, IsolatedTarget, TraversalCourseComposition.ShowcaseScope, TraversalKeys.DisplayRunnerRecipe);

                // The ridge runner, also away from both volumes: the ridge is the reparent destination and hosts the
                // second provider (07 s4.1).
                runnerApplier.InitialPosition = new TraversalVector3i(TraversalKeys.CheckpointTwoXMilli * 2, 0, 0);
                runnerApplier.InitialVelocity = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);
                Seed(context, TraversalCourseTargets.RunnerB, RidgeRunnerScope, TraversalKeys.RunnerRecipe);

                volumeApplier.NextOrdinal = TraversalVocabulary.CheckpointTwoOrdinal;
                volumeApplier.Center = new TraversalVector3i(TraversalKeys.CheckpointTwoXMilli, 0, 0);
                Seed(context, SecondCheckpoint, TraversalCourseComposition.RidgeScope, TraversalKeys.CheckpointRecipe);

                // Real gameplay state, not derived data: the moved runner's motion slot is seeded at the declared
                // domain version, so the value is a plain live fact whose survival across the reparent is a statement
                // about state rather than about the derivation (P-025, P-032).
                if (!context.Seeder.TrySeedSlot(
                        VelocityAssertedTarget,
                        TraversalKeys.MotionOwner,
                        TraversalKeys.MotionSlot,
                        TraversalKeys.MotionDomain.Version,
                        MutableValue,
                        out DiagnosticCode code,
                        out string detail))
                {
                    throw new InvalidOperationException(
                        "seeding the moved runner's live motion slot was refused: " + code + ": " + detail);
                }

                return true;
            }

            public bool SeedOptedInTarget(Gc013WorldContext context)
            {
                Seed(context, OptedInTarget, TraversalCourseComposition.ValleyScope, OptedInRunnerRecipe);
                return true;
            }

            /// <summary>
            /// Prepares the future runner's spawn: the runner applier installs a spawned runner's base layout from
            /// its own current position and velocity, so the values the future runner starts from are set here rather
            /// than left over from the last seed (04 s6, P-024).
            /// </summary>
            public void PrepareSpawn()
            {
                runnerApplier.InitialPosition = new TraversalVector3i(0, 0, 0);
                runnerApplier.InitialVelocity = new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);
            }

            public CompositionEditPayload MountProvider() => TraversalCourseComposition.MountModifier(
                declarations[1].Manifest,
                TraversalCourseComposition.TailwindInstance,
                ProviderScope);

            public CompositionEditPayload MountSecondProvider() => TraversalCourseComposition.MountModifier(
                declarations[2].Manifest,
                TraversalCourseComposition.HeadwindInstance,
                TraversalCourseComposition.RidgeScope);

            public CompositionEditPayload ScopeReparent()
                => TraversalCourseComposition.ScopeReparent(ValleyRunnerScope, RidgeRunnerScope);

            public CompositionEditPayload ModeSet(PropagationMode mode) => TraversalCourseComposition.ModeSet(mode);

            /// <summary>
            /// The first provider of the gate-declared exclusive pair. The traversal package declares no exclusive
            /// capability, so this pair is declared by the gate and no GC-020 observation publishes it; the scenario
            /// reports the pair as skipped rather than pretending a conflict the package cannot express (P-019).
            /// </summary>
            public CompositionEditPayload MountConflictProvider() => TraversalCourseComposition.MountModifier(
                declarations[3].Manifest,
                ConflictProviderInstance,
                ConflictProviderScope);

            public CompositionEditPayload MountConflictSecondProvider() => TraversalCourseComposition.MountModifier(
                declarations[4].Manifest,
                ConflictSecondProviderInstance,
                ConflictSecondProviderScope);

            public ScopeId ProviderScope => TraversalCourseComposition.ValleyScope;

            public ScopeId MovedScope => ValleyRunnerScope;

            public ScopeId MoveDestination => RidgeRunnerScope;

            public PluginInstanceId SecondProviderInstance => TraversalCourseComposition.HeadwindInstance;

            public PluginInstanceId ConflictProviderInstance => Gc020TraversalHost.ConflictProviderInstance;

            public PluginInstanceId ConflictSecondProviderInstance => Gc020TraversalHost.ConflictSecondProviderInstance;

            public ScopeId ConflictProviderScope => Gc020TraversalHost.ConflictProviderScope;

            public ScopeId ConflictSecondProviderScope => Gc020TraversalHost.ConflictSecondProviderScope;

            public CapabilityId DerivedCapability => TraversalVocabulary.AccelerationCapability;

            public CapabilityId ConflictCapability => Gc020TraversalHost.ConflictCapability;

            public int ProviderValue => TraversalVocabulary.TailwindMilli;

            public int SecondProviderValue => TraversalVocabulary.HeadwindMilli;

            public IReadOnlyList<TargetId> AutomaticTargets => AutomaticRunnerTargets;

            public TargetId MovedTarget => VelocityAssertedTarget;

            public TargetId IsolatedTarget => TraversalCourseTargets.RunnerDisplay;

            public TargetId IneligibleTarget => CheckpointOne;

            public TargetId OptedInTarget => Gc020TraversalHost.OptedInTarget;

            public TargetId FutureTarget => TraversalCourseTargets.RunnerC;

            public DefinitionRef FutureRecipe => TraversalKeys.RunnerRecipe;

            public ScopeId FutureScope => TraversalCourseComposition.ValleyRunnersScope;

            public OwnerId MutableOwner => TraversalKeys.MotionOwner;

            public SlotId MutableSlot => TraversalKeys.MotionSlot;

            public int MutableValue => TraversalVocabulary.SeededVelocityMilli;

            // ---------------------------------------------------------------- the GC-020 surface

            public RouteId MovementRoute => TraversalKeys.CommandRoute;

            public SchemaRef MovementSchema => TraversalKeys.CommandSchema;

            public FrozenPayload MovementPayload(int horizontalMilli, byte jumpPressed) =>
                TraversalCommandCodec.WriteInput(horizontalMilli, 0, jumpPressed);

            public TargetId VelocityAssertedTarget => TraversalCourseTargets.RunnerA;

            public TargetId ReparentedTarget => VelocityAssertedTarget;

            public IReadOnlyList<TargetId> CourseTargets => courseTargets;

            public TargetId CourseTarget => TraversalCourseTargets.Course;

            public TargetId SecondCheckpoint => TraversalCourseTargets.CheckpointTwo;

            public ScopeId ValleyRunnerScope => TraversalCourseComposition.ValleyRunnersScope;

            public ScopeId RidgeRunnerScope => TraversalCourseComposition.RidgeRunnersScope;

            public int ExpectedAcceleratedVelocityMilli => TraversalVocabulary.VelocityAfterTailwindMilli;

            public int ExpectedReparentedVelocityMilli => TraversalVocabulary.VelocityAfterHeadwindMilli;

            public int SeededVelocityMilli => TraversalVocabulary.SeededVelocityMilli;

            public ulong StepDurationTicks => TraversalKeys.StepDurationTicks;

            public uint MaxStepsPerPump => TraversalKeys.MaxStepsPerPump;

            public int StepMilliseconds => TraversalKeys.StepMilliseconds;

            public TraversalCourseSurface ActionSurface() => new TraversalCourseSurface(
                new List<StageId>
                {
                    TraversalKeys.InputStage,
                    TraversalKeys.IntegrateStage,
                    TraversalKeys.SenseStage,
                    TraversalKeys.CheckpointStage,
                    TraversalKeys.OutputStage,
                },
                TraversalKeys.SystemKeys,
                TraversalVocabulary.AccelerationCapability);

            public Gc020StageRuntime AttachStageRuntime(
                UnityWorldHost host,
                PipelineDescriptorReport descriptor,
                LiveTargetIndex targets,
                LiveTargetSeeder seeder) =>
                Gc020StageRuntime.Attach(
                    Label,
                    host,
                    CourseTarget,
                    descriptor,
                    targets,
                    seeder,
                    installPhysics: true);

            public void ConfigurePhysics(WorldId world) => physicsDeclaredSession = world.Session.Low;

            /// <summary>True when <see cref="ConfigurePhysics"/> declared the named world's course physics-installing.</summary>
            public bool PhysicsDeclaredFor(WorldId world) => physicsDeclaredSession == world.Session.Low;

            /// <summary>The first checkpoint volume, in the valley: the ineligible target no acceleration rule selects.</summary>
            private static TargetId CheckpointOne => TraversalCourseTargets.CheckpointOne;

            /// <summary>
            /// The targets Automatic propagation alone must reach with the acceleration capability. With the tailwind
            /// provider at the valley and the headwind provider at the ridge, those are the two ridge/valley runners;
            /// the display runner is eligible by selector but isolated, and a checkpoint recipe has no selector at all
            /// (P-013, P-015, P-016).
            /// </summary>
            private IReadOnlyList<TargetId> AutomaticRunnerTargets => new List<TargetId>
            {
                VelocityAssertedTarget,
                TraversalCourseTargets.RunnerB,
            };

            /// <summary>
            /// The course tree as the world definition's declared scopes: depth-one scopes first, then depth two in
            /// canonical order, with `showcase` carrying the `traversal.acceleration` capability boundary (P-010, P-016).
            /// </summary>
            private static IReadOnlyList<ScopeRecord> CourseScopeRecords()
            {
                IsolationSet none = new IsolationSet(false, null);
                IsolationSet noAcceleration = new IsolationSet(
                    false,
                    new[] { TraversalVocabulary.AccelerationCapability.Value });

                return new List<ScopeRecord>
                {
                    new ScopeRecord(
                        TraversalCourseComposition.RidgeScope,
                        TraversalCourseComposition.CourseRoot,
                        1,
                        none,
                        none,
                        null,
                        null),
                    new ScopeRecord(
                        TraversalCourseComposition.ValleyScope,
                        TraversalCourseComposition.CourseRoot,
                        1,
                        none,
                        none,
                        null,
                        null),
                    new ScopeRecord(
                        TraversalCourseComposition.RidgeRunnersScope,
                        TraversalCourseComposition.RidgeScope,
                        2,
                        none,
                        none,
                        null,
                        null),
                    new ScopeRecord(
                        TraversalCourseComposition.ShowcaseScope,
                        TraversalCourseComposition.ValleyScope,
                        2,
                        none,
                        noAcceleration,
                        null,
                        null),
                    new ScopeRecord(
                        TraversalCourseComposition.ValleyRunnersScope,
                        TraversalCourseComposition.ValleyScope,
                        2,
                        none,
                        none,
                        null,
                        null),
                };
            }

            /// <summary>
            /// The course entity's recipe. Its supported schema is the course's own selector identity and it installs
            /// exactly the course storage (observations, progress, crossings and the committed image), which is what
            /// the checkpoint and output stages read (07 s4.2, P-024).
            /// </summary>
            private static SpawnRecipe CourseRecipeOf(ISpawnApplier applier)
            {
                var schemas = new List<SchemaRef>
                {
                    TraversalIdentity.SchemaRef(TraversalCourseTargets.CourseStableName, 1U),
                };

                var descriptor = new TargetDescriptor(
                    CourseRecipe,
                    schemas,
                    null,
                    null,
                    default(AssetAdapterDescriptor),
                    null,
                    null,
                    null,
                    null);

                return new SpawnRecipe(CourseRecipe, descriptor, schemas, applier);
            }

            /// <summary>
            /// The opted-in runner's recipe: the runner recipe's own two selectors under a distinct definition
            /// identity, with a complete explicit opt-in naming the tailwind installation and the acceleration
            /// capability (P-013).
            /// </summary>
            private static SpawnRecipe OptedInRunner(ISpawnApplier applier)
            {
                var schemas = new List<SchemaRef>
                {
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.RunnerRecipe),
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.AccelerationTarget),
                };

                var descriptor = new TargetDescriptor(
                    OptedInRunnerRecipe,
                    schemas,
                    null,
                    null,
                    default(AssetAdapterDescriptor),
                    null,
                    null,
                    new List<TargetOptIn>
                    {
                        new TargetOptIn(
                            new ProviderInstallationId(TraversalCourseComposition.TailwindInstance.Value),
                            TraversalVocabulary.AccelerationCapability),
                    },
                    null);

                return new SpawnRecipe(OptedInRunnerRecipe, descriptor, schemas, applier);
            }

            private void Seed(Gc013WorldContext context, TargetId target, ScopeId scope, DefinitionRef recipe)
            {
                if (!context.Seeder.TrySeed(
                        target, scope, recipe, out TargetHandle _, out DiagnosticCode code, out string detail))
                {
                    throw new InvalidOperationException(
                        "target " + target.ToString() + " was refused: " + code + ": " + detail);
                }

                if (!context.Seeder.TryGetEntity(target, out Entity entity))
                {
                    throw new InvalidOperationException(
                        "target " + target.ToString() + " was created but the registry cannot resolve it (P-005).");
                }

                if (!context.Host.EntityWorld.IsCreated)
                {
                    throw new InvalidOperationException("the traversal world was disposed while seeding " + target.ToString());
                }

                // TrySeed installs generic target rows; the selected precompiled recipe installs gameplay storage.
                if (!recipes.TryResolve(recipe, out SpawnRecipe? selected, out DiagnosticCode recipeCode)
                    || selected == null)
                {
                    throw new InvalidOperationException("missing traversal recipe " + recipe + ": " + recipeCode);
                }

                selected.Applier.ApplyBaseLayout(context.Host.EntityWorld.EntityManager, entity, selected);
            }

            /// <summary>The course entity's base-layout applier: the storage the whole course's state lives in.</summary>
            private sealed class CourseApplier : ISpawnApplier
            {
                public FactoryKey Key => TraversalIdentity.Key("traversal.recipe.applier.course");

                public int AppliedCount { get; private set; }

                public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
                {
                    TraversalAccess.InstallCourseStorage(entityManager, entity);
                    AppliedCount++;
                    _ = recipe;
                }
            }
        }
    }
}
