// GameCore.Validation.ProbeHost — the two recipe sources GC-025's bake/runtime parity check compares.
//
// 04 section 6 makes baking an Editor workflow and requires the player to instantiate baked entities or invoke
// precompiled factories rather than invoke a baker; TEST-020 requires equivalent targets created "through editor
// baking and through the generated runtime recipe catalog", compared by normalized schemas, definitions and
// behaviour rather than by chunk layout or native entity indices.
//
// A recipe source is therefore the seam that decides where a course's recipe data comes from:
//
//   * `RuntimeCatalogCoverageRecipeSource` — the traversal fixture's own declared recipes, exactly what every other
//     gate runs (`TraversalCourseRecipes.Catalog`).
//   * `BakedCatalogCoverageRecipeSource` — the same recipes materialized from the committed Editor-baked artifact
//     (`GameCore.Validation.Generated.CatalogCoverageBaked`), with the base-layout applier the bake names.
//
// Both produce a `SpawnRecipeCatalog` whose fingerprint and whose course observations must agree: that agreement is
// what parity means here, and it is a comparison of two materialized catalogs rather than of two documents.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Traversal;
using GameCore.Gameplay.Traversal.Fixtures;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using GameCore.Validation.Generated;
using Unity.Entities;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>Where one course run's precompiled recipes come from.</summary>
    public interface ICatalogCoverageRecipeSource
    {
        /// <summary>Diagnostic label recorded in the probe's step details; never an identity (P-004).</summary>
        string Label { get; }

        /// <summary>The runner base-layout applier this source's recipes install runners with.</summary>
        ITraversalRunnerApplier CreateRunnerApplier();

        /// <summary>
        /// The closed recipe catalog this source declares. It must contain the runner, display-runner and checkpoint
        /// recipes the course seeds and spawns, with the definitions the runtime declares (P-024).
        /// </summary>
        SpawnRecipeCatalog Catalog(ITraversalRunnerApplier runnerApplier, TraversalVolumeApplier volumeApplier);
    }

    /// <summary>
    /// The runtime recipe source: the traversal fixture's own declared recipes. This is the source every other gate
    /// runs, so it is also the baseline the baked source is compared against.
    /// </summary>
    public sealed class RuntimeCatalogCoverageRecipeSource : ICatalogCoverageRecipeSource
    {
        /// <summary>The one instance; the source holds no per-run state.</summary>
        public static readonly RuntimeCatalogCoverageRecipeSource Instance = new RuntimeCatalogCoverageRecipeSource();

        private RuntimeCatalogCoverageRecipeSource()
        {
        }

        /// <inheritdoc />
        public string Label => "runtime-recipe";

        /// <inheritdoc />
        public ITraversalRunnerApplier CreateRunnerApplier() => new TraversalRunnerApplier();

        /// <inheritdoc />
        public SpawnRecipeCatalog Catalog(ITraversalRunnerApplier runnerApplier, TraversalVolumeApplier volumeApplier)
            => TraversalCourseRecipes.Catalog(runnerApplier, volumeApplier);
    }

    /// <summary>
    /// The runner base-layout applier the Editor bake names. It installs exactly the storage the runtime applier
    /// installs — both call the course's own `TraversalAccess.InstallRunnerStorage` — from the values the baked
    /// artifact carries, and it registers under the same generated key: the key identifies the registration, not the
    /// C# type that implements it (P-009).
    /// </summary>
    public sealed class BakedRunnerApplier : ITraversalRunnerApplier
    {
        public BakedRunnerApplier()
        {
            InitialPosition = new TraversalVector3i(
                CatalogCoverageBaked.RunnerInitialPositionXMilli,
                CatalogCoverageBaked.RunnerInitialPositionYMilli,
                CatalogCoverageBaked.RunnerInitialPositionZMilli);
            InitialVelocity = new TraversalVector3i(
                CatalogCoverageBaked.RunnerInitialVelocityXMilli,
                CatalogCoverageBaked.RunnerInitialVelocityYMilli,
                CatalogCoverageBaked.RunnerInitialVelocityZMilli);
        }

        /// <inheritdoc />
        public TraversalVector3i InitialPosition { get; set; }

        /// <inheritdoc />
        public TraversalVector3i InitialVelocity { get; set; }

        /// <inheritdoc />
        public int AppliedCount { get; private set; }

        /// <inheritdoc />
        public FactoryKey Key => new FactoryKey(
            TraversalIdentity.Id(CatalogCoverageBaked.RunnerApplierStableName),
            CatalogCoverageBaked.RunnerApplierKeyVersion);

        /// <inheritdoc />
        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            TraversalAccess.InstallRunnerStorage(entityManager, entity, InitialPosition, InitialVelocity);
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>
    /// The baked recipe source: the course's recipes materialized from the committed Editor-baked artifact. Every
    /// identity is derived from the artifact's stable names with the production rule, so the materialized catalog
    /// carries exactly the definitions, schemas, tags and applier registration the runtime catalog declares.
    /// </summary>
    public sealed class BakedCatalogCoverageRecipeSource : ICatalogCoverageRecipeSource
    {
        /// <summary>The one instance; the source holds no per-run state.</summary>
        public static readonly BakedCatalogCoverageRecipeSource Instance = new BakedCatalogCoverageRecipeSource();

        private BakedCatalogCoverageRecipeSource()
        {
        }

        /// <inheritdoc />
        public string Label => "editor-baked-recipe";

        /// <inheritdoc />
        public ITraversalRunnerApplier CreateRunnerApplier() => new BakedRunnerApplier();

        /// <inheritdoc />
        public SpawnRecipeCatalog Catalog(ITraversalRunnerApplier runnerApplier, TraversalVolumeApplier volumeApplier)
        {
            // The runner role is the one the bake declares, so its definition, base layout and descriptor tag come
            // from the artifact. The display runner shares the baked base layout under its own course-declared
            // definition and tag, which is exactly how the runtime catalog declares it (P-015, P-024).
            //
            // The declaration order is the runtime catalog's own (runner, volume, display runner), because a recipe
            // catalog's fingerprint walks its recipes in insertion order: parity here has to hold for the whole
            // table, not only for the runner entry.
            return new SpawnRecipeCatalog(new List<SpawnRecipe>
            {
                BakedRunnerRecipe(
                    runnerApplier,
                    BakedRunnerDefinition(),
                    CatalogCoverageBaked.RunnerDescriptorTagStableNames[0]),
                TraversalCourseRecipes.Volume(volumeApplier),
                BakedRunnerRecipe(
                    runnerApplier,
                    TraversalKeys.DisplayRunnerRecipe,
                    TraversalVocabulary.DisplayRunnerRecipe),
            });
        }

        /// <summary>
        /// The baked runner definition: its immutable id, its schema and version and its revision all come from the
        /// artifact's declared stable names, derived at this use site with the production rule (P-004).
        /// </summary>
        private static DefinitionRef BakedRunnerDefinition() =>
            new DefinitionRef(
                TraversalIdentity.Definition(CatalogCoverageBaked.RunnerRecipeDefinitionStableName),
                TraversalIdentity.SchemaRef(
                    CatalogCoverageBaked.RunnerRecipeSchemaStableName,
                    CatalogCoverageBaked.RunnerRecipeSchemaVersion),
                new DefinitionRevision(CatalogCoverageBaked.RunnerRecipeRevision));

        /// <summary>
        /// Materializes one runner recipe from the baked artifact's base layout: the selector schemas and the
        /// descriptor tag come from the artifact, and the applier installs the artifact's own values.
        /// </summary>
        private static SpawnRecipe BakedRunnerRecipe(
            ITraversalRunnerApplier applier,
            DefinitionRef definition,
            string descriptorTagStableName)
        {
            var schemas = new List<SchemaRef>(CatalogCoverageBaked.RunnerBaseLayoutSchemaStableNames.Length);
            for (int i = 0; i < CatalogCoverageBaked.RunnerBaseLayoutSchemaStableNames.Length; i++)
            {
                schemas.Add(TraversalIdentity.SchemaRef(CatalogCoverageBaked.RunnerBaseLayoutSchemaStableNames[i]));
            }

            var tags = new List<Id128> { TraversalIdentity.Id(descriptorTagStableName) };

            var descriptor = new TargetDescriptor(
                definition,
                schemas,
                null,
                tags,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(definition, descriptor, schemas, applier);
        }
    }
}
