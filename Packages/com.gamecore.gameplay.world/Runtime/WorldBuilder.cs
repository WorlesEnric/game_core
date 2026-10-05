// GameCore.Gameplay.World - WorldBuilder: the application definition of a baked world (P1.1, SADR-010).
//
// From one RegionManifest and the generated catalog it builds the GameApplicationDefinition the application root
// boots:
//
//   * the world scope (from the world's authoring id) is the root scope, and every region gets one composition scope
//     under it (scope seeds, so creating them publishes nothing);
//   * every region becomes a region target (recipe gameplay.world.region-recipe) in its own scope, and every authored
//     entity of every region becomes an entity target in its region's scope, seeded by its definition's recipe, whose
//     exact revision is the definition's baked content stamp;
//   * the entities and world plugins are mounted at the world scope, so every target - including a region whose scene
//     is not loaded - is a live kernel target from the first committed boundary;
//   * the two command systems, their routes, lanes and readers are registered; the world is command-driven;
//   * every IGameplayWorldExtension of the options adds its plugins (mounted at the world scope after the two above),
//     systems, routes, lanes and readers (P1.3 seam); an extension that also implements IGameplayWorldTargets adds its
//     recipes and seeds its world-scope session targets (P1.5, see WorldExtensions.cs).
//
// After boot, Attach seeds every target's slots from the manifest (entities: alive/variant/scale/visible; placement:
// region/pose; regions: residency = Unloaded, visits = 0) while the world is still paused, and hands each command
// system its per-world module. Nothing here is static mutable state.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Execution.Messages;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using GameCore.Unity.Runtime.Time;

namespace GameCore.Gameplay.World
{
    /// <summary>The derivation value source of a gameplay world: no reducers or predicates (no derived capabilities).</summary>
    public sealed class GameplayValueSource : IDerivationValueSource
    {
        public bool IsReductionRegistered(FactoryKey reducer) => false;

        public bool IsPredicateRegistered(FactoryKey predicate) => false;

        public bool TryReduce(FactoryKey reducer, IReadOnlyList<FrozenPayload> inputs, out FrozenPayload? result)
        {
            result = null;
            return false;
        }

        public bool TryEvaluate(FactoryKey predicate, DerivationPredicateContext context, out bool result)
        {
            result = false;
            return false;
        }
    }

    /// <summary>Options of one gameplay world.</summary>
    public sealed class WorldBuildOptions
    {
        /// <summary>Application name (the world's diagnostic name).</summary>
        public string Name { get; set; } = "GameCoreGameplayWorld";

        /// <summary>Target capacity of the registry: baked targets plus room for runtime spawns.</summary>
        public int TargetCapacity { get; set; } = 256;

        /// <summary>Refuse to build when the catalog fingerprint differs from the one the manifest was baked with.</summary>
        public bool RequireMatchingCatalog { get; set; } = true;

        /// <summary>Further gameplay plugins composed into the world (P1.3 seam; see IGameplayWorldExtension).</summary>
        public List<IGameplayWorldExtension> Extensions { get; } = new List<IGameplayWorldExtension>();

        /// <summary>Committed events one step may stage (P-045); extensions with per-step events raise it.</summary>
        public int MaxEventsPerStep { get; set; } = 32;

        /// <summary>Committed events the world retains for readers (P-045).</summary>
        public int MaxRetainedEvents { get; set; } = 512;

        /// <summary>
        /// The presentation service registry of the world (P1.5). It belongs to the plan, so it survives a root
        /// replacement after a restore: services registered once stay registered for the restored world.
        /// </summary>
        public PresentationServices Presentation { get; set; } = new PresentationServices();
    }

    /// <summary>A built (not yet booted) gameplay world: the application definition and what Attach needs.</summary>
    public sealed class WorldBuildPlan
    {
        internal WorldBuildPlan(
            RegionManifest manifest,
            GameApplicationDefinition definition,
            GameplayPresentationFrame frame,
            Id128 issuer,
            IReadOnlyList<IGameplayWorldExtension> extensions,
            PresentationServices presentation)
        {
            Manifest = manifest;
            Definition = definition;
            Frame = frame;
            Issuer = issuer;
            Extensions = extensions;
            Presentation = presentation;
        }

        /// <summary>The extensions composed into this world, in composition order.</summary>
        public IReadOnlyList<IGameplayWorldExtension> Extensions { get; }

        /// <summary>The world's presentation service registry (P1.5).</summary>
        public PresentationServices Presentation { get; }

        public RegionManifest Manifest { get; }

        public GameApplicationDefinition Definition { get; }

        /// <summary>The adapter frame the root installs (presentation and streaming run in its Present).</summary>
        public GameplayPresentationFrame Frame { get; }

        /// <summary>The root's issuer: the host issuer of world.setResidency.</summary>
        public Id128 Issuer { get; }
    }

    /// <summary>Builds and attaches gameplay worlds from baked manifests.</summary>
    public static class WorldBuilder
    {
        /// <summary>The world-definition id of a world authoring id.</summary>
        public static WorldDefinitionId WorldDefinitionOf(string worldId) =>
            new WorldDefinitionId(StableNameKeyDerivation.Derive("gameplay.world." + worldId));

        /// <summary>The root (world) scope of a world authoring id.</summary>
        public static ScopeId RootScopeOf(string worldId) => AuthoringIds.ScopeIdFor(worldId);

        /// <summary>The application root's issuer of a world authoring id.</summary>
        public static Id128 IssuerOf(string worldId) => StableNameKeyDerivation.Derive("gameplay.issuer.host." + worldId);

        /// <summary>Validates the manifest against the catalog and builds the application definition.</summary>
        public static WorldBuildPlan Build(RegionManifest manifest, ICatalog catalog, ContentHash catalogFingerprint, WorldBuildOptions? options)
        {
            if (manifest == null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            WorldBuildOptions settings = options ?? new WorldBuildOptions();
            if (!string.Equals(manifest.FormatId, RegionManifest.Format, StringComparison.Ordinal) || !AuthoringIds.IsValid(manifest.WorldId))
            {
                throw new InvalidOperationException(GameplayDiagnosticCodes.BakeStale + ": the region manifest is not a baked " + RegionManifest.Format);
            }

            if (settings.RequireMatchingCatalog && !string.Equals(manifest.CatalogFingerprint, catalogFingerprint.ToHex(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    GameplayDiagnosticCodes.CatalogStale + ": the manifest was baked with catalog " + manifest.CatalogFingerprint
                    + " but the game runs catalog " + catalogFingerprint.ToHex() + "; re-run GameCore.Gameplay.Compile.Entry.Bake");
            }

            for (int i = 0; i < manifest.Definitions.Count; i++)
            {
                ManifestDefinition definition = manifest.Definitions[i];
                FactoryKey recipeKey = GameplayIds.Key(GameplayCatalogNames.DefinitionRecipePrefix + definition.authoringId);
                if (!catalog.Lookup(recipeKey).Found)
                {
                    throw new InvalidOperationException(
                        GameplayDiagnosticCodes.CatalogStale + ": the catalog registers no recipe for definition " + definition.name);
                }
            }

            ScopeId root = RootScopeOf(manifest.WorldId);
            Id128 issuer = IssuerOf(manifest.WorldId);
            var frame = new GameplayPresentationFrame();
            var entityApplier = new EntityRecipeApplier();
            var recipes = new List<SpawnRecipe>
            {
                RegionRecipe(),
            };
            var recipeByDefinition = new Dictionary<string, DefinitionRef>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.Definitions.Count; i++)
            {
                ManifestDefinition definition = manifest.Definitions[i];
                DefinitionRef recipe = EntityRecipes.RecipeOf(definition.authoringId, definition.Revision);
                recipeByDefinition[definition.authoringId] = recipe;
                recipes.Add(EntityRecipes.Create(recipe, entityApplier));
            }

            var extensions = new List<IGameplayWorldExtension>(settings.Extensions);
            var routes = Concat(EntityDeclarations.Routes(), WorldDeclarations.Routes());
            var lanes = Concat(EntityDeclarations.Lanes(), WorldDeclarations.Lanes());
            for (int i = 0; i < extensions.Count; i++)
            {
                extensions[i].Validate(manifest);
                routes.AddRange(extensions[i].Routes);
                lanes.AddRange(extensions[i].Lanes);
                if (extensions[i] is IGameplayWorldTargets withTargets)
                {
                    recipes.AddRange(withTargets.Recipes());
                }
            }

            var plane = new MessagePlaneRegistration(
                routes,
                lanes,
                null,
                maxPendingRequests: 64,
                maxRetainedResults: 64,
                maxRetainedEvents: settings.MaxRetainedEvents,
                maxEventsPerStep: settings.MaxEventsPerStep,
                nextStepCapacity: 4);
            var readers = new CommandPayloadReaders();
            EntityReaders.BindInto(readers);
            WorldReaders.BindInto(readers);
            for (int i = 0; i < extensions.Count; i++)
            {
                extensions[i].BindReaders(readers);
            }

            PluginManifest entities = EntityDeclarations.Manifest();
            PluginManifest worldPlugin = WorldDeclarations.Manifest();
            var entitiesDeclaration = new CatalogPluginDeclaration(entities, ConfigDocument.Empty);
            var worldDeclaration = new CatalogPluginDeclaration(worldPlugin, ConfigDocument.Empty);

            var dispatchKinds = new ScheduleDispatchKindTable()
                .Add(EntityDeclarations.CommandSystem, SystemDispatchKind.ManagedSystem)
                .Add(WorldDeclarations.CommandSystem, SystemDispatchKind.ManagedSystem);
            for (int i = 0; i < extensions.Count; i++)
            {
                IReadOnlyList<GameplaySystem> systems = extensions[i].Systems;
                for (int s = 0; s < systems.Count; s++)
                {
                    dispatchKinds.Add(systems[s].Key, SystemDispatchKind.ManagedSystem);
                }
            }

            GameApplicationDefinition.Builder builder = new GameApplicationDefinition.Builder(settings.Name)
                .WithCatalog(catalog, catalogFingerprint)
                .AddPlugin(entitiesDeclaration)
                .AddPlugin(worldDeclaration)
                .WithWorld(WorldDefinitionOf(manifest.WorldId), TemporalModel.CommandDriven)
                .WithPropagation(PropagationMode.Automatic)
                .WithRootScope(root)
                .AddSystem(EntityDeclarations.CommandSystemRegistration())
                .AddSystem(WorldDeclarations.CommandSystemRegistration())
                .WithDispatchKinds(dispatchKinds)
                .WithMessages(plane, readers)
                .WithRecipes(new SpawnRecipeCatalog(recipes))
                .WithValues(new GameplayValueSource())
                .WithTargetCapacity(settings.TargetCapacity)
                .WithIssuer(issuer)
                .WithAdapterFrame(app => frame.BindRoot(app));

            var regions = new List<ManifestRegion>(manifest.Regions);
            regions.Sort((l, r) => string.CompareOrdinal(l.authoringId, r.authoringId));
            for (int i = 0; i < regions.Count; i++)
            {
                ScopeId scope = AuthoringIds.ScopeIdFor(regions[i].authoringId);
                builder.AddScopeSeed(new ScopeRecord(scope, root, 1, new IsolationSet(false, null), new IsolationSet(false, null), null, null));
            }

            for (int i = 0; i < regions.Count; i++)
            {
                ManifestRegion region = regions[i];
                builder.AddBootStep(GameApplicationBootStep.Seed(
                    "seed-region:" + region.name,
                    AuthoringIds.TargetIdFor(region.authoringId),
                    AuthoringIds.ScopeIdFor(region.authoringId),
                    WorldDeclarations.RegionRecipe));
            }

            var placed = new List<ManifestEntity>(manifest.Entities);
            placed.Sort((l, r) => string.CompareOrdinal(l.authoringId, r.authoringId));
            for (int i = 0; i < placed.Count; i++)
            {
                ManifestEntity entity = placed[i];
                if (!recipeByDefinition.TryGetValue(entity.definitionId, out DefinitionRef recipe))
                {
                    throw new InvalidOperationException(
                        GameplayDiagnosticCodes.EntityMissingDefinition + ": entity " + entity.name + " has no baked definition");
                }

                builder.AddBootStep(GameApplicationBootStep.Seed(
                    "seed-entity:" + entity.name,
                    AuthoringIds.TargetIdFor(entity.authoringId),
                    AuthoringIds.ScopeIdFor(entity.regionId),
                    recipe));
            }

            for (int i = 0; i < extensions.Count; i++)
            {
                if (!(extensions[i] is IGameplayWorldTargets withTargets))
                {
                    continue;
                }

                IReadOnlyList<GameplayExtensionTarget> targets = withTargets.Targets(manifest);
                for (int t = 0; t < targets.Count; t++)
                {
                    builder.AddBootStep(GameApplicationBootStep.Seed(
                        "seed-" + extensions[i].Name + ":" + targets[t].Name, targets[t].Target, root, targets[t].Recipe));
                }
            }

            builder.AddBootStep(GameApplicationBootStep.Apply("mount-entities", Mount(entitiesDeclaration, EntityDeclarations.Instance, root)));
            builder.AddBootStep(GameApplicationBootStep.Apply("mount-world", Mount(worldDeclaration, WorldDeclarations.Instance, root)));
            for (int i = 0; i < extensions.Count; i++)
            {
                IGameplayWorldExtension extension = extensions[i];
                for (int p = 0; p < extension.Plugins.Count; p++)
                {
                    builder.AddPlugin(extension.Plugins[p].Declaration);
                }

                for (int s = 0; s < extension.Systems.Count; s++)
                {
                    builder.AddSystem(extension.Systems[s].Registration);
                }

                for (int p = 0; p < extension.Plugins.Count; p++)
                {
                    GameplayPluginMount mount = extension.Plugins[p];
                    builder.AddBootStep(GameApplicationBootStep.Apply(
                        "mount-" + extension.Name + (extension.Plugins.Count > 1 ? "-" + p : string.Empty),
                        Mount(mount.Declaration, mount.Instance, root)));
                }
            }

            return new WorldBuildPlan(manifest, builder.Build(), frame, issuer, extensions.AsReadOnly(), settings.Presentation);
        }

        /// <summary>
        /// Seeds every target's slots from the manifest, attaches the per-world modules to the command systems and returns
        /// the running gameplay world. Call once, right after boot, while the world is still Ready (paused).
        /// </summary>
        public static GameplayWorld Attach(GameApplicationRoot root, WorldBuildPlan plan) => Attach(root, plan, true);

        /// <summary>
        /// <see cref="Attach(GameApplicationRoot, WorldBuildPlan)"/> with the slot seeding optional. Pass
        /// <paramref name="seedSlots"/> false for a root composed by a restore (<c>SaveService.RootChanged</c>, P1.2): its
        /// slots already hold the checkpoint's committed values, and seeding would overwrite them. The previous
        /// <see cref="GameplayWorld"/> must be shut down first; a gameplay world is bound to exactly one root.
        /// </summary>
        public static GameplayWorld Attach(GameApplicationRoot root, WorldBuildPlan plan, bool seedSlots)
        {
            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            RegionManifest manifest = plan.Manifest;
            var entityModule = new EntityModule(root.Host, root.Registry);
            var worldModule = new WorldModule(root.Host, root.Registry, plan.Issuer, manifest);

            for (int i = 0; i < manifest.Regions.Count; i++)
            {
                ManifestRegion region = manifest.Regions[i];
                if (!seedSlots)
                {
                    continue;
                }

                TargetId target = AuthoringIds.TargetIdFor(region.authoringId);
                Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.Residency, (int)RegionResidency.Unloaded);
                Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.Visits, 0);
            }

            for (int i = 0; i < manifest.Entities.Count; i++)
            {
                ManifestEntity entity = manifest.Entities[i];
                ManifestDefinition? definition = manifest.FindDefinition(entity.definitionId);
                ManifestRegion? region = manifest.FindRegion(entity.regionId);
                TargetId target = AuthoringIds.TargetIdFor(entity.authoringId);
                entityModule.Add(new EntityRecord(target, entity.authoringId, entity.definitionId, definition != null ? definition.variantCount : 1));
                if (!seedSlots)
                {
                    continue;
                }

                SeedEntity(root, target, entity.alive, entity.variant, entity.scaleMilli, entity.visible);
                SeedPlacement(root, target, region != null ? region.key : 0, entity.x, entity.y, entity.z, entity.yaw);
            }

            global::Unity.Entities.World entityWorld = root.Host.EntityWorld;
            EntityCommandSystem? entitySystem = entityWorld.GetExistingSystemManaged<EntityCommandSystem>();
            WorldCommandSystem? worldSystem = entityWorld.GetExistingSystemManaged<WorldCommandSystem>();
            if (entitySystem == null || worldSystem == null)
            {
                throw new InvalidOperationException("the gameplay command systems are not registered in world " + root.Host.DiagnosticName);
            }

            entitySystem.Module = entityModule;
            worldSystem.Module = worldModule;
            var world = new GameplayWorld(root, plan, entityModule, worldModule);
            for (int i = 0; i < plan.Extensions.Count; i++)
            {
                plan.Extensions[i].Attach(world, seedSlots);
            }

            plan.Frame.Attach(world);
            return world;
        }

        internal static void SeedEntity(GameApplicationRoot root, TargetId target, bool alive, int variant, int scaleMilli, bool visible)
        {
            Seed(root, target, GameplaySlots.EntityOwner, GameplaySlots.Alive, alive ? 1 : 0);
            Seed(root, target, GameplaySlots.EntityOwner, GameplaySlots.Variant, variant);
            Seed(root, target, GameplaySlots.EntityOwner, GameplaySlots.ScaleMilli, scaleMilli);
            Seed(root, target, GameplaySlots.EntityOwner, GameplaySlots.Visible, visible ? 1 : 0);
        }

        internal static void SeedPlacement(GameApplicationRoot root, TargetId target, int regionKey, int x, int y, int z, int yaw)
        {
            Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.Region, regionKey);
            Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.PosX, x);
            Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.PosY, y);
            Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, z);
            Seed(root, target, GameplaySlots.WorldOwner, GameplaySlots.Yaw, yaw);
        }

        private static void Seed(GameApplicationRoot root, TargetId target, OwnerId owner, SlotId slot, int value)
        {
            if (!root.Seeder.TrySeedSlot(target, owner, slot, GameplaySlots.SchemaVersion, value, out DiagnosticCode code, out string detail))
            {
                throw new InvalidOperationException("seeding " + target + " failed: " + code + ": " + detail);
            }
        }

        private static SpawnRecipe RegionRecipe()
        {
            var schemas = new List<SchemaRef> { WorldDeclarations.RegionRecipeSchema };
            var descriptor = new TargetDescriptor(
                WorldDeclarations.RegionRecipe,
                schemas,
                null,
                null,
                default(AssetAdapterDescriptor),
                null,
                null,
                null,
                null);
            return new SpawnRecipe(WorldDeclarations.RegionRecipe, descriptor, schemas, new RegionRecipeApplier());
        }

        /// <summary>The install-mount edit of one plugin at a scope (schema defaults, no local patch).</summary>
        public static CompositionEditPayload Mount(CatalogPluginDeclaration declaration, PluginInstanceId instance, ScopeId scope)
        {
            ConfigDocument local = ConfigDocument.Empty;
            ConfigComposeResult composed = ConfigComposer.Compose(new[]
            {
                new ConfigLayer(ConfigLayerOrigin.SchemaDefaults, declaration.Manifest.ConfigSchema.Id.Value, declaration.SchemaDefaults),
                new ConfigLayer(ConfigLayerOrigin.LocalPatch, instance.Value, local),
            });

            return new CompositionEditPayload(
                CompositionEditSubject.InstallMount,
                scope,
                default(ScopeId),
                false,
                null,
                null,
                null,
                null,
                declaration.Manifest.PluginTypeId,
                instance,
                DefinitionRevision.First,
                ConfigDocumentCodec.HashOf(composed.Value),
                local,
                0,
                null,
                PropagationMode.Automatic);
        }

        /// <summary>The scope-create edit of a child scope (runtime spawns).</summary>
        public static CompositionEditPayload ScopeCreate(ScopeId scope, ScopeId parent)
        {
            return new CompositionEditPayload(
                CompositionEditSubject.ScopeCreate,
                scope,
                parent,
                false,
                new IsolationSet(false, null),
                new IsolationSet(false, null),
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

        private static List<T> Concat<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
        {
            var all = new List<T>(first.Count + second.Count);
            all.AddRange(first);
            all.AddRange(second);
            return all;
        }
    }
}
