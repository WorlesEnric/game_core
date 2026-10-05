// Hollowmere - GameBoot: boots the baked Hollowmere world with the player, NPC and interaction plugins (P1.1, P1.3).
//
// Boot.unity holds one GameBoot. On Start it resolves the generated catalog the manifest was baked with, builds the
// gameplay application definition with the three P1.3 world extensions (player, npc, interaction), boots it with
// GameApplication.Boot, attaches the gameplay world (slots, modules, presentation frame), creates the view binders under
// its own transform, installs the player loop (input adapter, locomotion, focus, portal probe, orbit camera), the NPC
// loop (talk dispatcher, NavMesh/animator/bubble binders) and the interaction loop (dispatcher, proximity interactor,
// interactable binder), and starts the world. Streaming, input and presentation then run inside the application's one
// pump; GameBoot itself has no Update.
//
// Integration points for the later packets (all null objects until set): Npcs.Conversations.Conversations
// (IConversationStarter, P1.4), Interactions.Dispatcher.Actions / Feedback and InteractionExtension.Module.Conditions
// (P1.4/P1.5), Player.Focus.Prompts (IPromptPresenter, P1.5), Player.Input.UiIntents (IUiIntentSink, P1.5) and
// Player.Locomotion.Footsteps (IFootstepSink, P1.5).
#nullable enable
using GameCore.Contracts;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Boots the Hollowmere world from its baked region manifest and runs the player loop.</summary>
    [DisallowMultipleComponent]
    public sealed class GameBoot : MonoBehaviour
    {
        [SerializeField] private RegionManifest? manifest;

        [Tooltip("Keep the neighbours of the focus region resident (overrides the world's setting when enabled).")]
        [SerializeField] private bool preloadNeighbours;

        [SerializeField] private PlayerDefinition? player;

        [SerializeField] private NpcRoster? npcs;

        [SerializeField] private InteractionRoster? interactions;

        [Tooltip("The orbit camera (the scene's main camera when empty).")]
        [SerializeField] private Camera? playerCamera;

        /// <summary>The running gameplay world; null before Start or when the boot refused.</summary>
        public GameplayWorld? World { get; private set; }

        /// <summary>The refusal of the last boot attempt; empty when it booted.</summary>
        public string Failure { get; private set; } = string.Empty;

        public RegionManifest? Manifest => manifest;

        public PlayerDefinition? PlayerDefinition => player;

        public NpcRoster? NpcRoster => npcs;

        public InteractionRoster? InteractionRoster => interactions;

        public PlayerWorldExtension? PlayerExtension { get; private set; }

        public NpcWorldExtension? NpcExtension { get; private set; }

        public InteractionWorldExtension? InteractionExtension { get; private set; }

        public PlayerSession? Player { get; private set; }

        public NpcSession? Npcs { get; private set; }

        public InteractionSession? Interactions { get; private set; }

        public void Configure(RegionManifest regionManifest, bool preload)
        {
            manifest = regionManifest;
            preloadNeighbours = preload;
        }

        /// <summary>The P1.3 content GameBoot composes into the world.</summary>
        public void ConfigureGameplay(PlayerDefinition? playerDefinition, NpcRoster? npcRoster, InteractionRoster? interactionRoster, Camera? orbitCamera)
        {
            player = playerDefinition;
            npcs = npcRoster;
            interactions = interactionRoster;
            playerCamera = orbitCamera;
        }

        private void Start()
        {
            if (manifest == null)
            {
                Failure = "GameBoot has no region manifest; run GameCore/Gameplay/Bake World";
                Debug.LogError("[Hollowmere] " + Failure);
                return;
            }

            if (!GameplayCatalog.TryBuild(manifest.CatalogTypeName, out ICatalog? catalog, out ContentHash fingerprint, out string detail) || catalog == null)
            {
                Failure = detail;
                Debug.LogError("[Hollowmere] " + Failure);
                return;
            }

            var playerExtension = new PlayerWorldExtension(player);
            var npcExtension = new NpcWorldExtension(npcs);
            var interactionExtension = new InteractionWorldExtension(interactions);
            var options = new WorldBuildOptions { Name = "Hollowmere", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };
            options.Extensions.Add(playerExtension);
            options.Extensions.Add(npcExtension);
            options.Extensions.Add(interactionExtension);

            WorldBuildPlan plan;
            try
            {
                plan = WorldBuilder.Build(manifest, catalog, fingerprint, options);
            }
            catch (System.InvalidOperationException refused)
            {
                Failure = refused.Message;
                Debug.LogError("[Hollowmere] " + Failure);
                return;
            }

            GameApplicationRoot root = GameApplication.Boot(plan.Definition, new GameApplicationBootOptions());
            GameplayWorld world = WorldBuilder.Attach(root, plan);
            if (preloadNeighbours)
            {
                world.Streamer.PreloadNeighbours = true;
            }

            world.Streamer.Observe(destroyCancellationToken);
            world.CreateViews(transform);
            PlayerExtension = playerExtension;
            NpcExtension = npcExtension;
            InteractionExtension = interactionExtension;
            Camera? orbit = playerCamera != null ? playerCamera : Camera.main;
            Player = PlayerSession.Install(world, playerExtension, orbit);
            Npcs = NpcSession.Install(world, npcExtension);
            Interactions = InteractionSession.Install(world, interactionExtension, playerExtension.Player, playerExtension.PlayerKey);
            Player.Focus.AddSource(Npcs.Candidates);
            Player.Focus.AddSource(Interactions.Candidates);

            GameplayWorldBehaviour holder = gameObject.AddComponent<GameplayWorldBehaviour>();
            holder.World = world;
            root.Start();
            World = world;
        }

        private void OnDestroy()
        {
            GameplayWorld? world = World;
            World = null;
            Player?.Dispose();
            if (world == null)
            {
                return;
            }

            world.Shutdown();
            if (world.Root.State != GameApplicationState.Stopped)
            {
                world.Root.Stop("GameBoot destroyed");
            }
        }
    }
}
