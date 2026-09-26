// GameCore.Gameplay.Traversal.Fixtures — the traversal course's declared composition and its recipe catalog (GC-020).
//
// Normative sources: 07 s4.1's composition tree (`CourseWorld [TraversalRuntime, CheckpointRuntime, InputAdapter,
// CourseSensorAdapter]`, with `Valley [Tailwind]` holding `runners -> runner-a : RunnerRecipe`,
// `checkpoint-1 : CheckpointRecipe` and `showcase [CapabilityIsolation: traversal.acceleration] -> runner-display`,
// and `Ridge [Headwind]` holding `runners -> runner-b : RunnerRecipe` and `checkpoint-2 : CheckpointRecipe`),
// 07 s4.3's before/after operations (mount `Tailwind`, unmount it, reparent the runner subtree into `Ridge`, and run
// under both propagation modes), P-010 (one parent per scope, created top-down), P-016 (an isolation boundary blocks
// outside rules in either mode), P-024 (a precompiled `SpawnRecipe` carries the base layout and the descriptor a
// target is spawned from), P-042/P-043 (declared registration data, bounded ingress) and P-004 (every identity is
// derived from its stable name, never written as a literal).
//
// This is the fixture half of the course: it declares the scope tree, the plugin set and the recipes, and it
// installs a recipe's base layout through `TraversalAccess`, which is the same storage the gameplay package's own
// systems write. It contains no rule of its own: every identity is derived by `TraversalIdentity` and every
// declaration comes from `TraversalDeclarations`, so a fixture-derived identity and a package-derived one cannot
// disagree. The mount payloads mirror `TraversalPayloads` (the gameplay package's own payload builders, whose shape
// is `CardTablePayloads`'s) so a scenario can mount the course itself.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Rules.Traversal;
using GameCore.Unity.Runtime;
using Unity.Entities;

namespace GameCore.Gameplay.Traversal.Fixtures
{
    /// <summary>
    /// The traversal course's scope tree, its install identities and its mount plan: everything a scenario applies
    /// through the real control lane. The world root IS `traversal.course-world`, so `CourseRoot` is handed to the
    /// lane as the world's own root scope and only its descendants are created by `ScopeCreates` (P-010).
    /// </summary>
    public static class TraversalCourseComposition
    {
        /// <summary>The world root scope: the course. Every other course scope is created under it (P-010).</summary>
        public static ScopeId CourseRoot => TraversalIdentity.Scope(TraversalVocabulary.CourseWorld);

        /// <summary>The valley scope: the tailwind provider's installation scope.</summary>
        public static ScopeId ValleyScope => TraversalIdentity.Scope(TraversalVocabulary.Valley);

        /// <summary>The valley's runner scope, holding `runner-a`.</summary>
        public static ScopeId ValleyRunnersScope => TraversalIdentity.Scope(TraversalVocabulary.ValleyRunners);

        /// <summary>
        /// The showcase scope. Its isolation boundary names `traversal.acceleration`, so an acceleration modifier
        /// never reaches `runner-display` in either mode (P-016).
        /// </summary>
        public static ScopeId ShowcaseScope => TraversalIdentity.Scope(TraversalVocabulary.Showcase);

        /// <summary>The ridge scope: the headwind provider's installation scope and the reparent destination.</summary>
        public static ScopeId RidgeScope => TraversalIdentity.Scope(TraversalVocabulary.Ridge);

        /// <summary>The ridge's runner scope, which the reparent moves the valley runner subtree into.</summary>
        public static ScopeId RidgeRunnersScope => TraversalIdentity.Scope(TraversalVocabulary.RidgeRunners);

        /// <summary>Mount identity of the course runtime, the course's own state executor.</summary>
        public static PluginInstanceId RuntimeInstance => TraversalKeys.Instance(TraversalVocabulary.TraversalRuntime);

        /// <summary>Mount identity of the tailwind modifier; it survives a remount (P-005).</summary>
        public static PluginInstanceId TailwindInstance => TraversalKeys.Instance(TraversalVocabulary.Tailwind);

        /// <summary>Mount identity of the headwind modifier.</summary>
        public static PluginInstanceId HeadwindInstance => TraversalKeys.Instance(TraversalVocabulary.Headwind);

        /// <summary>Every installation this composition mounts, in the order the course declares them.</summary>
        public static IReadOnlyList<PluginInstanceId> Installations() =>
            new List<PluginInstanceId>
            {
                RuntimeInstance,
                TailwindInstance,
                HeadwindInstance,
            };

        /// <summary>
        /// Create-scope payloads for the whole course tree. Parents precede their children, and the scopes of one
        /// depth appear in canonical name order, because an edit's parent must already exist in the state it is
        /// planned against (P-010). Only `Showcase` carries a capability boundary.
        /// </summary>
        public static IReadOnlyList<CompositionEditPayload> ScopeCreates()
        {
            ScopeId root = CourseRoot;
            ScopeId valley = ValleyScope;
            ScopeId ridge = RidgeScope;
            return new List<CompositionEditPayload>
            {
                ScopeCreate(ridge, root, false),
                ScopeCreate(valley, root, false),
                ScopeCreate(RidgeRunnersScope, ridge, false),
                ScopeCreate(ShowcaseScope, valley, true),
                ScopeCreate(ValleyRunnersScope, valley, false),
            };
        }

        /// <summary>
        /// O-02: create one scope under an existing parent. <paramref name="isolateAcceleration"/> installs the
        /// capability boundary of 07 s4.1's `Showcase` scope.
        /// </summary>
        public static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent, bool isolateAcceleration)
        {
            IsolationSet capabilityIsolation = isolateAcceleration
                ? new IsolationSet(false, new[] { TraversalVocabulary.AccelerationCapability.Value })
                : new IsolationSet(false, null);

            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                null,
                capabilityIsolation,
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

        /// <summary>
        /// O-03: mount the course runtime at one scope. The course entity it serves is the state executor of the
        /// whole course, so it is mounted once, at the course root (07 s4.1).
        /// </summary>
        public static CompositionEditPayload MountRuntime(
            PluginManifest manifest,
            PluginInstanceId instance,
            ScopeId scope) => Mount(manifest, instance, scope);

        /// <summary>
        /// O-03: mount one acceleration modifier at its own scope. Mounting it makes every compatible existing
        /// runner's next integrated step apply the extra acceleration, and a runner created later inherits the same
        /// contribution automatically (07 s4.1, P-013, P-024).
        /// </summary>
        public static CompositionEditPayload MountModifier(
            PluginManifest manifest,
            PluginInstanceId instance,
            ScopeId scope) => Mount(manifest, instance, scope);

        /// <summary>
        /// O-02: move a scope subtree under a new parent (07 s4.3's "Reparent runner subtree into `Ridge`").
        /// Membership, contributions and bindings publish together (P-025).
        /// </summary>
        public static CompositionEditPayload ScopeReparent(ScopeId scope, ScopeId newParent) =>
            TraversalPayloads.ScopeReparent(scope, newParent);

        /// <summary>
        /// O-08: set the world propagation mode. The scope must be default or the world root, because the mode is
        /// one world-level setting (P-013, P-014).
        /// </summary>
        public static CompositionEditPayload ModeSet(PropagationMode mode) => TraversalPayloads.ModeSet(mode);

        /// <summary>O-07: unmount one installation (07 s4.3's "Unmount `Tailwind`").</summary>
        public static CompositionEditPayload Unmount(PluginInstanceId instance) => TraversalPayloads.Unmount(instance);

        /// <summary>
        /// O-03: mount one plugin instance at one scope. The declared configuration hash is the canonical hash of
        /// the effective configuration, exactly as the control lane's applier recomputes it (P-020).
        /// </summary>
        private static CompositionEditPayload Mount(PluginManifest manifest, PluginInstanceId instance, ScopeId scope)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            ConfigDocument effective = EffectiveConfiguration(manifest, instance);
            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(effective),
                ConfigDocument.Empty,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>The effective configuration a mount publishes: schema defaults, then the local declaration.</summary>
        private static ConfigDocument EffectiveConfiguration(PluginManifest manifest, PluginInstanceId instance)
        {
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(
                    ConfigLayerOrigin.SchemaDefaults,
                    manifest.ConfigSchema.Id.Value,
                    ConfigDocument.Empty),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, ConfigDocument.Empty),
            });

            return composed.Value;
        }
    }

    /// <summary>
    /// The traversal course's declarations: the course runtime and 07 s4.1's two optional acceleration modifiers.
    /// Each declaration resolves the traversal catalog's registered plugin factory and its registered
    /// configuration schema, and names its own stable plugin type (P-009).
    /// </summary>
    public static class TraversalCourseManifests
    {
        /// <summary>Declared plugin type of the course runtime.</summary>
        public static PluginTypeId CourseRuntimeType => TraversalKeys.PluginType(TraversalVocabulary.TraversalRuntime);

        /// <summary>Declared plugin type of the tailwind modifier.</summary>
        public static PluginTypeId TailwindType => TraversalKeys.PluginType(TraversalVocabulary.Tailwind);

        /// <summary>Declared plugin type of the headwind modifier.</summary>
        public static PluginTypeId HeadwindType => TraversalKeys.PluginType(TraversalVocabulary.Headwind);

        /// <summary>
        /// The course runtime's declaration: the five-stage plan of 07 s4.2, its six owned domains and its declared
        /// observation step buffer.
        /// </summary>
        public static PluginManifest CourseRuntimeManifest() =>
            TraversalDeclarations.CourseRuntime(
                CourseRuntimeType,
                TraversalKeys.PluginFactoryKey,
                TraversalKeys.ConfigSchema);

        /// <summary>The tailwind modifier's declaration: `(+2, 0, 0)` m/s² on the acceleration slot (07 s4.1).</summary>
        public static PluginManifest TailwindManifest() =>
            TraversalDeclarations.AccelerationModifier(
                TailwindType,
                TraversalKeys.PluginFactoryKey,
                TraversalKeys.ConfigSchema,
                TraversalVocabulary.Tailwind,
                new TraversalVector3i(TraversalVocabulary.TailwindMilli, 0, 0));

        /// <summary>The headwind modifier's declaration: `(-1, 0, 0)` m/s² on the acceleration slot (07 s4.1).</summary>
        public static PluginManifest HeadwindManifest() =>
            TraversalDeclarations.AccelerationModifier(
                HeadwindType,
                TraversalKeys.PluginFactoryKey,
                TraversalKeys.ConfigSchema,
                TraversalVocabulary.Headwind,
                new TraversalVector3i(TraversalVocabulary.HeadwindMilli, 0, 0));

        /// <summary>
        /// The course's declaration set as manifests, in declaration order: the runtime first, then the two
        /// modifiers (the order the tree of 07 s4.1 mounts them in).
        /// </summary>
        public static IReadOnlyList<PluginManifest> Manifests() =>
            new List<PluginManifest>
            {
                CourseRuntimeManifest(),
                TailwindManifest(),
                HeadwindManifest(),
            };
    }

    /// <summary>
    /// A runner recipe's base-layout applier, whatever materialized it (GC-025). The runtime recipe declares its
    /// own instance and GC-025's bake/runtime parity comparison materializes a second one from the Editor-baked
    /// artifact, so a recipe factory takes this seam rather than one concrete implementation. Both agree on the
    /// generated registration key, because the key identifies the registration and not the C# type (P-009).
    /// </summary>
    public interface ITraversalRunnerApplier : ISpawnApplier
    {
        /// <summary>Position a newly installed runner starts from, in millimetres.</summary>
        TraversalVector3i InitialPosition { get; set; }

        /// <summary>Velocity a newly installed runner starts with, in thousandths of a metre per second.</summary>
        TraversalVector3i InitialVelocity { get; set; }

        /// <summary>Runners whose base layout this applier installed.</summary>
        int AppliedCount { get; }
    }

    /// <summary>
    /// The runner recipe's base-layout applier (04 s6, P-024). It installs the runner's own pose, velocity, jump
    /// state and captured input; the derived binding rows are added by the publisher inside the publication fence,
    /// so a spawned runner is never visible half-assembled.
    /// </summary>
    public sealed class TraversalRunnerApplier : ITraversalRunnerApplier
    {
        /// <summary>Position a newly installed runner starts from, in millimetres.</summary>
        public TraversalVector3i InitialPosition { get; set; } = TraversalVector3i.Zero;

        /// <summary>
        /// Velocity a newly installed runner starts with, in thousandths of a metre per second. The reference seeds
        /// `1.00` m/s along x, so the tailwind step of 07 s4.3 starts from the same value it asserts about.
        /// </summary>
        public TraversalVector3i InitialVelocity { get; set; } =
            new TraversalVector3i(TraversalVocabulary.SeededVelocityMilli, 0, 0);

        /// <inheritdoc />
        public FactoryKey Key => TraversalIdentity.Key("traversal.recipe.applier.runner");

        /// <summary>Runners whose base layout this applier installed.</summary>
        public int AppliedCount { get; private set; }

        /// <inheritdoc />
        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            TraversalAccess.InstallRunnerStorage(entityManager, entity, InitialPosition, InitialVelocity);
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>
    /// The checkpoint recipe's base-layout applier (04 s6, P-024). A checkpoint volume is pure data — an ordinal,
    /// a centre and a radius (07 s4.2's "simple checkpoint volumes from pure data") — so the applier installs
    /// exactly that and nothing about progress.
    /// </summary>
    public sealed class TraversalVolumeApplier : ISpawnApplier
    {
        /// <summary>Order of the checkpoint volume a newly installed volume declares (07 s4.2).</summary>
        public uint NextOrdinal { get; set; }

        /// <summary>Centre of a newly installed checkpoint volume, in millimetres.</summary>
        public TraversalVector3i Center { get; set; } = TraversalVector3i.Zero;

        /// <summary>Radius of a newly installed checkpoint volume, in millimetres.</summary>
        public int Radius { get; set; } = TraversalKeys.CheckpointRadiusMilli;

        /// <inheritdoc />
        public FactoryKey Key => TraversalIdentity.Key("traversal.recipe.applier.checkpoint-volume");

        /// <summary>Volumes whose base layout this applier installed.</summary>
        public int AppliedCount { get; private set; }

        /// <inheritdoc />
        public void ApplyBaseLayout(EntityManager entityManager, Entity entity, SpawnRecipe recipe)
        {
            TraversalAccess.InstallVolumeStorage(entityManager, entity, NextOrdinal, Center, Radius);
            AppliedCount++;
            _ = recipe;
        }
    }

    /// <summary>
    /// The traversal course's precompiled spawn recipes and the closed catalog a publisher resolves them from.
    /// </summary>
    public static class TraversalCourseRecipes
    {
        /// <summary>
        /// The runner recipe: a compatible target of every `traversal.acceleration` rule (07 s4.1). It advertises
        /// the acceleration-target selector as well as its own, which is what makes a modifier rule select a runner
        /// and nothing else.
        /// </summary>
        public static SpawnRecipe Runner(ITraversalRunnerApplier applier)
        {
            return Recipe(
                TraversalKeys.RunnerRecipe,
                TraversalVocabulary.RunnerRecipe,
                new List<SchemaRef>
                {
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.RunnerRecipe),
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.AccelerationTarget),
                },
                applier);
        }

        /// <summary>
        /// The display runner recipe: the same two selectors as the runner recipe, so a modifier's rule does select
        /// it and the showcase scope's isolation boundary is what blocks the contribution, not ineligibility
        /// (P-015, P-016).
        /// </summary>
        public static SpawnRecipe DisplayRunner(ITraversalRunnerApplier applier)
        {
            return Recipe(
                TraversalKeys.DisplayRunnerRecipe,
                TraversalVocabulary.DisplayRunnerRecipe,
                new List<SchemaRef>
                {
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.RunnerRecipe),
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.AccelerationTarget),
                },
                applier);
        }

        /// <summary>
        /// The checkpoint recipe: it declares only its sensor contract, so no acceleration modifier selects it
        /// (07 s4.1: "a checkpoint declares this and nothing else").
        /// </summary>
        public static SpawnRecipe Volume(TraversalVolumeApplier applier)
        {
            return Recipe(
                TraversalKeys.CheckpointRecipe,
                TraversalVocabulary.CheckpointRecipe,
                new List<SchemaRef>
                {
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.CheckpointRecipe),
                    TraversalVocabulary.SelectorSchema(TraversalVocabulary.SensorTarget),
                },
                applier);
        }

        /// <summary>The course's closed recipe catalog over the given applier instances (P-015, P-024).</summary>
        public static SpawnRecipeCatalog Catalog(
            ITraversalRunnerApplier runnerApplier,
            TraversalVolumeApplier volumeApplier)
        {
            return new SpawnRecipeCatalog(new List<SpawnRecipe>
            {
                Runner(runnerApplier),
                Volume(volumeApplier),
                DisplayRunner(runnerApplier),
            });
        }

        /// <summary>
        /// One recipe with its immutable descriptor. The descriptor's supported schemas are the recipe's own
        /// selector schema plus the selector contracts a settlement addresses, which is exactly what a derivation
        /// rule's selector and a target's declared state both read (P-015).
        /// </summary>
        private static SpawnRecipe Recipe(
            DefinitionRef recipe,
            string recipeStableName,
            IReadOnlyList<SchemaRef> supportedSchemas,
            ISpawnApplier applier)
        {
            var descriptor = new TargetDescriptor(
                recipe,
                supportedSchemas,
                null,
                new List<Id128> { TraversalIdentity.Id(recipeStableName) },
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);

            return new SpawnRecipe(recipe, descriptor, supportedSchemas, applier);
        }
    }

    /// <summary>
    /// The stable target identities of the course's declared tree (07 s4.1). The course target itself is this
    /// fixture's own declaration: it is the entity that owns the whole course's progress and committed output.
    /// </summary>
    public static class TraversalCourseTargets
    {
        /// <summary>Stable name of the course's own target; declared here because no rule package names it.</summary>
        public const string CourseStableName = "traversal.course";

        /// <summary>The valley's first runner target, in `traversal.valley-runners`.</summary>
        public static TargetId RunnerA => TraversalIdentity.Target(TraversalVocabulary.RunnerA);

        /// <summary>The ridge's runner, which the reparent destination holds after the move.</summary>
        public static TargetId RunnerB => TraversalIdentity.Target(TraversalVocabulary.RunnerB);

        /// <summary>A runner target created after the composition was declared (P-024's future descendant).</summary>
        public static TargetId RunnerC => TraversalIdentity.Target(TraversalVocabulary.RunnerC);

        /// <summary>The display runner beneath the isolation boundary, in `traversal.showcase`.</summary>
        public static TargetId RunnerDisplay => TraversalIdentity.Target(TraversalVocabulary.RunnerDisplay);

        /// <summary>The first checkpoint volume, in the valley.</summary>
        public static TargetId CheckpointOne => TraversalIdentity.Target(TraversalVocabulary.CheckpointOne);

        /// <summary>The second checkpoint volume, in the ridge.</summary>
        public static TargetId CheckpointTwo => TraversalIdentity.Target(TraversalVocabulary.CheckpointTwo);

        /// <summary>The course's own target: it owns run progress and the committed crossing output (07 s4.2).</summary>
        public static TargetId Course => TraversalIdentity.Target(CourseStableName);
    }
}
