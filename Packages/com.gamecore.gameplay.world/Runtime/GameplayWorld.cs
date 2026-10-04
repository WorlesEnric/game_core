// GameCore.Gameplay.World - the running gameplay world: commands, presentation frame, spawner (P1.1).
//
// GameplayWorld is what P1.3-P1.6 hold on to: the application root, the two plugin modules, the region streamer, the
// view binders, a command issuer for gameplay commands (separate from the root's host issuer) and the spawner of new
// runtime targets. The presentation frame is the world's adapter frame (installed inside the root's one-pump counter):
// after each host pump it streams regions and presents committed state; it never advances a step (P-045).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using GameCore.Unity.Runtime;
using GameCore.Unity.Runtime.Integration;
using GameCore.Unity.Runtime.Messages;
using UnityEngine;

namespace GameCore.Gameplay.World
{
    /// <summary>Submits gameplay commands with one issuer and a strictly increasing sequence (P-050).</summary>
    public sealed class GameplayCommands
    {
        private readonly GameplayWorld world;
        private ulong sequence;

        public GameplayCommands(GameplayWorld world, Id128 issuer)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            Issuer = issuer;
        }

        public Id128 Issuer { get; }

        public int Submitted { get; private set; }

        public int Refused { get; private set; }

        public CommandAdmissionReceipt Submit(RouteId route, TargetId target, SchemaRef schema, FrozenPayload payload)
        {
            sequence++;
            var envelope = new CommandEnvelope(new OperationId(world.Root.World, Issuer, sequence), route, target, schema, null, payload);
            CommandAdmissionReceipt receipt = world.Root.Host.Submit(envelope);
            if (receipt.Admitted)
            {
                Submitted++;
            }
            else
            {
                Refused++;
            }

            return receipt;
        }

        /// <summary>world.travel: moves <paramref name="traveller"/> to a neighbouring region (any connecting portal).</summary>
        public CommandAdmissionReceipt Travel(TargetId traveller, string regionId, string portalId = "")
        {
            int destination = world.Worlds.TryRegion(regionId, out RegionRecord? region) && region != null ? region.Key : 0;
            int portal = portalId.Length > 0 && world.Worlds.TryPortal(portalId, out PortalRecord? record) && record != null ? record.Key : 0;
            return Submit(WorldDeclarations.TravelRoute, traveller, WorldDeclarations.TravelCommand, TravelPayload.Encode(destination, portal));
        }

        /// <summary>world.place: sets a target's pose (mm, mrad) within its region.</summary>
        public CommandAdmissionReceipt Place(TargetId target, int x, int y, int z, int yaw) =>
            Submit(WorldDeclarations.PlaceRoute, target, WorldDeclarations.PlaceCommand, PlacePayload.Encode(x, y, z, yaw));

        public CommandAdmissionReceipt Spawn(TargetId target) =>
            Submit(EntityDeclarations.SpawnRoute, target, EntityDeclarations.SpawnCommand, EntityCommand.Encode(0));

        public CommandAdmissionReceipt Despawn(TargetId target) =>
            Submit(EntityDeclarations.DespawnRoute, target, EntityDeclarations.DespawnCommand, EntityCommand.Encode(0));

        public CommandAdmissionReceipt SetVariant(TargetId target, int variant) =>
            Submit(EntityDeclarations.SetVariantRoute, target, EntityDeclarations.SetVariantCommand, EntityCommand.Encode(variant));
    }

    /// <summary>The world's adapter frame: streaming and presentation after each host pump.</summary>
    public sealed class GameplayPresentationFrame : IAdapterFrame
    {
        private GameApplicationRoot? root;
        private GameplayWorld? world;

        public WorldId World => root != null ? root.Host.World : default(WorldId);

        public int PresentCount { get; private set; }

        public GameplayWorld? Attached => world;

        /// <summary>Called by the application root's frame factory, before boot.</summary>
        public IAdapterFrame BindRoot(GameApplicationRoot app)
        {
            root = app ?? throw new ArgumentNullException(nameof(app));
            return this;
        }

        internal void Attach(GameplayWorld attached) => world = attached;

        internal void Detach() => world = null;

        public AdapterFrameReport CollectInput()
        {
            GameplayWorld? current = world;
            if (current == null)
            {
                return AdapterFrameReport.Completed("gameplay", 0, 0, "not attached");
            }

            return AdapterFrameReport.Completed("gameplay", current.CollectInput(), 0, "input");
        }

        public AdapterFrameReport Present()
        {
            GameplayWorld? current = world;
            if (current == null)
            {
                return AdapterFrameReport.Completed("gameplay", 0, 0, "not attached");
            }

            PresentCount++;
            int views = current.Present();
            return AdapterFrameReport.Completed("gameplay", current.Streamer.Regions.Count, views, "presented");
        }
    }

    /// <summary>A source of per-frame gameplay input (P1.3's player controller registers one).</summary>
    public interface IGameplayInputSource
    {
        /// <summary>Samples input and submits commands; returns the number of commands submitted.</summary>
        int Collect(GameplayWorld world);
    }

    /// <summary>The running gameplay world of one application root.</summary>
    public sealed class GameplayWorld
    {
        private readonly List<IPresentationBinder> binders = new List<IPresentationBinder>();
        private readonly List<IGameplayInputSource> inputs = new List<IGameplayInputSource>();
        private readonly Dictionary<TargetId, int> pendingPortalTravel = new Dictionary<TargetId, int>();
        private EventCursor eventCursor;
        private int frame;

        internal GameplayWorld(GameApplicationRoot root, WorldBuildPlan plan, EntityModule entities, WorldModule worldModule)
        {
            Root = root;
            Plan = plan;
            Entities = entities;
            Worlds = worldModule;
            Slots = new WorldSlotReader(root.Host.EntityWorld, root.Registry);
            Commands = new GameplayCommands(this, StableNameKeyDerivation.Derive("gameplay.issuer.player." + plan.Manifest.WorldId));
            Spawner = new EntitySpawner(this);
            eventCursor = new EventCursor(root.World, EventSequence.Zero);
            TargetId focus = AuthoringIds.IsValid(plan.Manifest.FocusEntityId)
                ? AuthoringIds.TargetIdFor(plan.Manifest.FocusEntityId)
                : default(TargetId);
            Streamer = new RegionStreamer(this, focus, plan.Manifest.StartRegionId, plan.Manifest.PreloadNeighbours, new UnitySceneLoader());
        }

        public GameApplicationRoot Root { get; }

        public WorldBuildPlan Plan { get; }

        public RegionManifest Manifest => Plan.Manifest;

        public EntityModule Entities { get; }

        /// <summary>The world plugin's module (regions, portals, graph).</summary>
        public WorldModule Worlds { get; }

        public WorldSlotReader Slots { get; }

        public GameplayCommands Commands { get; }

        public RegionStreamer Streamer { get; private set; }

        public EntitySpawner Spawner { get; }

        public PrefabViewBinder? Views { get; private set; }

        public IReadOnlyList<IPresentationBinder> Binders => binders;

        /// <summary>The focus traveller the streamer follows (default when the world names none).</summary>
        public TargetId Focus => Streamer.Focus;

        /// <summary>Replaces the scene loader (EditMode tests use an immediate loader; the default streams real scenes).</summary>
        public void UseSceneLoader(ISceneLoader loader)
        {
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            RegionStreamer previous = Streamer;
            Streamer = new RegionStreamer(this, previous.Focus, Manifest.StartRegionId, previous.PreloadNeighbours, loader);
            for (int i = 0; i < binders.Count; i++)
            {
                if (binders[i] is IResidencyAware aware)
                {
                    Streamer.AddListener(aware);
                }
            }
        }

        /// <summary>
        /// Creates the standard binders (prefab views, animator, audio hook, interaction stub) under <paramref name="parent"/>.
        /// Headless (batchmode, no graphics) binders report inactive and touch nothing.
        /// </summary>
        public PrefabViewBinder CreateViews(Transform parent)
        {
            var regionByKey = new Dictionary<int, string>();
            var regionNames = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < Manifest.Regions.Count; i++)
            {
                regionByKey[Manifest.Regions[i].key] = Manifest.Regions[i].authoringId;
                regionNames[Manifest.Regions[i].authoringId] = Manifest.Regions[i].name;
            }

            var views = new PrefabViewBinder(parent, regionByKey, regionNames);
            for (int i = 0; i < Manifest.Entities.Count; i++)
            {
                ManifestEntity entity = Manifest.Entities[i];
                ManifestDefinition? definition = Manifest.FindDefinition(entity.definitionId);
                if (definition == null || definition.definition == null)
                {
                    continue;
                }

                views.Add(new EntityViewSpec(AuthoringIds.TargetIdFor(entity.authoringId), entity.authoringId, entity.name, definition.definition, OverridesOf(entity)));
            }

            AddBinder(views);
            AddBinder(new AnimatorBinder(views));
            AddBinder(new AudioSourceBinder(views));
            AddBinder(new InteractionTargetBinder(views));
            Views = views;
            return views;
        }

        public void AddBinder(IPresentationBinder binder)
        {
            if (binder == null)
            {
                throw new ArgumentNullException(nameof(binder));
            }

            binders.Add(binder);
            if (binder is IResidencyAware aware)
            {
                Streamer.AddListener(aware);
            }
        }

        public void AddInput(IGameplayInputSource input) => inputs.Add(input ?? throw new ArgumentNullException(nameof(input)));

        /// <summary>
        /// Appends the committed events published since the last call (entity and world events, in commit order) and
        /// returns how many were read. The world keeps one read cursor; use the host's plane directly for more readers.
        /// </summary>
        public int ReadEvents(List<CommittedEvent> into, int maxEvents = 256)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            WorldMessagePlane? plane = Root.Host.Messages;
            if (plane == null)
            {
                return 0;
            }

            CommittedEventPage page = plane.ReadEvents(eventCursor, maxEvents);
            into.AddRange(page.Events);
            eventCursor = page.NextCursor;
            return page.Events.Count;
        }

        /// <summary>Requests travel through a portal for a traveller standing in one of its regions (portal triggers).</summary>
        public bool RequestPortalTravel(TargetId traveller, string portalId, string fromScenePath)
        {
            if (!Worlds.TryPortal(portalId, out PortalRecord? portal) || portal == null)
            {
                return false;
            }

            if (pendingPortalTravel.TryGetValue(traveller, out int requestedAt) && frame - requestedAt < 30)
            {
                return false;
            }

            if (!Slots.TryRead(traveller, GameplaySlots.WorldOwner, GameplaySlots.Region, out int current) || !portal.Connects(current))
            {
                return false;
            }

            if (!Worlds.TryRegion(portal.Other(current), out RegionRecord? destination) || destination == null)
            {
                return false;
            }

            pendingPortalTravel[traveller] = frame;
            return Commands.Travel(traveller, destination.AuthoringId, portal.AuthoringId).Admitted;
        }

        internal int CollectInput()
        {
            int submitted = 0;
            for (int i = 0; i < inputs.Count; i++)
            {
                submitted += inputs[i].Collect(this);
            }

            return submitted;
        }

        internal int Present()
        {
            frame++;
            Streamer.Tick();
            int touched = 0;
            for (int i = 0; i < binders.Count; i++)
            {
                if (binders[i].IsActive)
                {
                    touched += binders[i].Present(Slots);
                }
            }

            return touched;
        }

        /// <summary>Detaches presentation and clears views; the root itself is stopped by its owner.</summary>
        public void Shutdown()
        {
            Plan.Frame.Detach();
            for (int i = Plan.Extensions.Count - 1; i >= 0; i--)
            {
                Plan.Extensions[i].Detach(this);
            }

            Streamer.Cancel();
            if (Views != null)
            {
                Views.Clear();
            }
        }

        internal void AddRuntimeEntity(TargetId target, string definitionId, string name, IReadOnlyDictionary<string, string>? overrides)
        {
            ManifestDefinition? definition = Manifest.FindDefinition(definitionId);
            Entities.Add(new EntityRecord(target, string.Empty, definitionId, definition != null ? definition.variantCount : 1));
            if (Views != null && definition != null && definition.definition != null)
            {
                Views.Add(new EntityViewSpec(target, string.Empty, name, definition.definition, overrides));
            }
        }

        private static IReadOnlyDictionary<string, string> OverridesOf(ManifestEntity entity)
        {
            var overrides = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < entity.overrides.Count; i++)
            {
                overrides[entity.overrides[i].Field] = entity.overrides[i].Value;
            }

            return overrides;
        }
    }

    /// <summary>
    /// Spawns new runtime targets (not authored) at the composition boundary: one child scope per spawn under the region
    /// scope is a composition publication with no target change, and the spawn is that publication's assembly (P-024).
    /// </summary>
    public sealed class EntitySpawner
    {
        private readonly GameplayWorld world;
        private int spawned;

        public EntitySpawner(GameplayWorld world)
        {
            this.world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public int SpawnedCount => spawned;

        /// <summary>Spawns one entity of a baked definition in a region at a pose (mm, mrad); alive and visible.</summary>
        public bool TrySpawn(string definitionId, string regionId, int x, int y, int z, int yaw, out TargetId target, out string detail)
        {
            target = default(TargetId);
            ManifestDefinition? definition = world.Manifest.FindDefinition(definitionId);
            if (definition == null)
            {
                detail = GameplayDiagnosticCodes.EntityMissingDefinition + ": no baked definition " + definitionId;
                return false;
            }

            if (!world.Worlds.TryRegion(regionId, out RegionRecord? region) || region == null)
            {
                detail = GameplayDiagnosticCodes.WorldUnknownRegion + ": no baked region " + regionId;
                return false;
            }

            GameApplicationRoot root = world.Root;
            int ordinal = spawned + 1;
            string suffix = world.Manifest.WorldId + "." + ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var candidate = new TargetId(StableNameKeyDerivation.Derive("gameplay.runtime-target." + suffix));
            var scope = new ScopeId(StableNameKeyDerivation.Derive("gameplay.runtime-scope." + suffix));
            DefinitionRef recipe = EntityRecipes.RecipeOf(definition.authoringId, definition.Revision);

            CompositionHost lane = root.Lane;
            EditAdmission admission = lane.SubmitEdit(WorldBuilder.ScopeCreate(scope, region.Scope), root.NextOperation(), lane.Committed.Revision);
            if (!admission.Staged)
            {
                detail = "the spawn scope was refused: " + admission.Kind + "/" + admission.Code;
                return false;
            }

            IReadOnlyList<PublishedOperation> published = lane.Drain();
            if (published.Count == 0 || published[0].Outcome == Outcome.Rejected)
            {
                detail = "the spawn scope publication was refused";
                return false;
            }

            DerivedAssemblyReport report = root.Pipeline.PublishSpawn(root.NextOperation(), candidate, recipe, scope);
            if (report.Outcome != DerivedAssemblyOutcome.Published)
            {
                detail = "the spawn was refused: " + report.Describe();
                return false;
            }

            if (!root.Targets.TryRegister(candidate, scope, recipe, out DiagnosticCode code, out string registered))
            {
                detail = "the spawned target could not be indexed: " + code + ": " + registered;
                return false;
            }

            spawned = ordinal;
            WorldBuilder.SeedEntity(root, candidate, true, 0, GameplayUnits.ScaleOne, true);
            WorldBuilder.SeedPlacement(root, candidate, region.Key, x, y, z, yaw);
            world.AddRuntimeEntity(candidate, definitionId, (definition.definition != null ? definition.definition.name : "Spawned") + " #" + ordinal, null);
            target = candidate;
            detail = string.Empty;
            return true;
        }
    }
}
