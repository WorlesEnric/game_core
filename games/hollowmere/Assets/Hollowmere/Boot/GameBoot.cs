// Hollowmere - GameBoot: boots the baked Hollowmere world with the player, NPC and interaction plugins (P1.3), the
// narrative plugins (P1.4) and the UI and audio (P1.5).
//
// Boot.unity holds one GameBoot. On Start it builds the world options with the three P1.3 world extensions, lets
// UiAudioBootstrap add the ui and audio extensions and create the UI/audio rig under its own transform, points P1.4's
// presenters at that rig, and boots the narrative world (HollowmereNarrative.Boot = NarrativeComposer.Boot: the baked
// world plan plus the logic, inventory, quest and dialogue plugins; the root is left Ready). It then creates the view
// binders, installs the player loop (input adapter, locomotion, focus, portal probe, orbit camera), the NPC loop and the
// interaction loop, hands P1.3's seams their P1.4 implementations (HollowmereNarrative.Wire) and their P1.5
// presenters (UiAudioBootstrap.Wire), and starts the root. Streaming, input, narrative delivery and presentation then
// run inside the application's one pump; GameBoot itself has no Update.
//
// The UI starts on the main menu, which pauses gameplay input; New Game shows the HUD. Saving needs Hollowmere's
// checkpoint codecs (P3.1); until then the save and load screens answer GP-UI-014.
#nullable enable
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Hollowmere.Narrative;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Boots the Hollowmere world from its baked region and content manifests and runs the player loop.</summary>
    [DisallowMultipleComponent]
    public sealed class GameBoot : MonoBehaviour
    {
        [SerializeField] private RegionManifest? manifest;

        [Tooltip("The baked narrative content (Rules/HollowmereContent.content.asset).")]
        [SerializeField] private GameplayContentManifest? content;

        [Tooltip("Keep the neighbours of the focus region resident (overrides the world's setting when enabled).")]
        [SerializeField] private bool preloadNeighbours;

        [SerializeField] private PlayerDefinition? player;

        [SerializeField] private NpcRoster? npcs;

        [SerializeField] private InteractionRoster? interactions;

        [Tooltip("The orbit camera (the scene's main camera when empty).")]
        [SerializeField] private Camera? playerCamera;

        /// <summary>The running gameplay world; null before Start or when the boot refused.</summary>
        public GameplayWorld? World { get; private set; }

        /// <summary>The narrative world around <see cref="World"/>; null before Start or when the boot refused.</summary>
        public NarrativeWorld? Narrative { get; private set; }

        /// <summary>The narrative modules of the world (their presenters, runner and commands).</summary>
        public HollowmereNarrativeModules? Modules { get; private set; }

        /// <summary>The refusal of the last boot attempt; empty when it booted.</summary>
        public string Failure { get; private set; } = string.Empty;

        /// <summary>How many presentation seams UiAudioBootstrap.Wire connected (0 without the UI/audio content).</summary>
        public int PresentationSeams { get; private set; }

        public RegionManifest? Manifest => manifest;

        public GameplayContentManifest? Content => content;

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

        /// <summary>The narrative content GameBoot composes into the world.</summary>
        public void ConfigureNarrative(GameplayContentManifest? contentManifest)
        {
            content = contentManifest;
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
                Refuse("GameBoot has no region manifest; run GameCore/Gameplay/Bake World");
                return;
            }

            if (content == null)
            {
                Refuse("GameBoot has no narrative content manifest; run the Hollowmere narrative bake (Rules/HollowmereContent)");
                return;
            }

            var playerExtension = new PlayerWorldExtension(player);
            var npcExtension = new NpcWorldExtension(npcs);
            var interactionExtension = new InteractionWorldExtension(interactions);
            var build = new WorldBuildOptions { Name = "Hollowmere", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };
            build.Extensions.Add(playerExtension);
            build.Extensions.Add(npcExtension);
            build.Extensions.Add(interactionExtension);
            UiAudioBootstrap.Configure(build, gameObject);
            var modules = new HollowmereNarrativeModules();
            UiAudioBootstrap.AssignViews(UiAudioBootstrap.RigOf(gameObject), modules);

            NarrativeWorld game;
            try
            {
                game = HollowmereNarrative.Boot(manifest, content, modules, new GameApplicationBootOptions(), build, false);
            }
            catch (System.InvalidOperationException refused)
            {
                Refuse(refused.Message);
                return;
            }

            GameplayWorld world = game.World;
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
            HollowmereNarrative.Wire(game, interactionExtension, Interactions, Npcs);
            PresentationSeams = UiAudioBootstrap.Wire(world, Player, Interactions, game, modules);

            GameplayWorldBehaviour holder = gameObject.AddComponent<GameplayWorldBehaviour>();
            holder.World = world;
            Modules = modules;
            Narrative = game;
            World = world;
            game.Root.Start();
        }

        private void Refuse(string failure)
        {
            Failure = failure;
            Debug.LogError("[Hollowmere] " + Failure);
        }

        private void OnDestroy()
        {
            NarrativeWorld? game = Narrative;
            Narrative = null;
            World = null;
            Player?.Dispose();
            if (game == null)
            {
                return;
            }

            if (game.Root.State == GameApplicationState.Stopped)
            {
                game.Delivery.Dispose();
                game.World.Shutdown();
                return;
            }

            game.Shutdown();
        }
    }
}
