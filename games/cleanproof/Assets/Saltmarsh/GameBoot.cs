#nullable enable
using System;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Saltmarsh.Narrative;
using UnityEngine;

namespace Saltmarsh.Boot
{
    [DisallowMultipleComponent]
    public sealed class GameBoot : MonoBehaviour
    {
        [SerializeField] private RegionManifest? manifest;

        [Tooltip("The baked narrative content (Rules/SaltmarshContent.content.asset).")]
        [SerializeField] private GameplayContentManifest? content;

        [Tooltip("Keep the neighbours of the focus region resident (overrides the world's setting when enabled).")]
        [SerializeField] private bool preloadNeighbours;

        [SerializeField] private PlayerDefinition? player;

        [SerializeField] private NpcRoster? npcs;

        [SerializeField] private InteractionRoster? interactions;

        [Tooltip("The orbit camera (the scene's main camera when empty).")]
        [SerializeField] private Camera? playerCamera;

        public SaveService? Saves { get; private set; }

        public GameplayWorld? World { get; private set; }

        public NarrativeWorld? Narrative { get; private set; }

        public SaltmarshNarrativeModules? Modules { get; private set; }

        public string Failure { get; private set; } = string.Empty;

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

        public int Reattachments { get; private set; }

        public bool ConfigureSaves(SaveServiceOptions options)
        {
            if (options == null || Narrative == null)
            {
                return false;
            }

            NarrativeDelivery.Configure(options, Narrative);
            return true;
        }

        public void Configure(RegionManifest regionManifest, bool preload)
        {
            manifest = regionManifest;
            preloadNeighbours = preload;
        }

        public void ConfigureNarrative(GameplayContentManifest? contentManifest)
        {
            content = contentManifest;
        }

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
                Refuse("GameBoot has no narrative content manifest; run the Saltmarsh narrative bake (Rules/SaltmarshContent)");
                return;
            }

            var playerExtension = new PlayerWorldExtension(player);
            var npcExtension = new NpcWorldExtension(npcs);
            var interactionExtension = new InteractionWorldExtension(interactions);
            var build = new WorldBuildOptions { Name = "Saltmarsh", MaxEventsPerStep = 64, MaxRetainedEvents = 1024 };
            build.Extensions.Add(playerExtension);
            build.Extensions.Add(npcExtension);
            build.Extensions.Add(interactionExtension);
            UiAudioBootstrap.Configure(build, gameObject);
            var modules = new SaltmarshNarrativeModules();
            UiAudioBootstrap.AssignViews(UiAudioBootstrap.RigOf(gameObject), modules);

            if (!NarrativeComposer.TryBoot(manifest, content, modules.All, new GameApplicationBootOptions(), build, false, out NarrativeWorld? booted, out string failure)
                || booted == null)
            {
                Refuse(failure);
                return;
            }

            NarrativeWorld game = booted;
            PlayerExtension = playerExtension;
            NpcExtension = npcExtension;
            InteractionExtension = interactionExtension;
            Modules = modules;
            try
            {
                Install(game);
            }
            catch (Exception refused) when (refused is InvalidOperationException || refused is ArgumentException)
            {
                Narrative = null;
                World = null;
                Player?.Dispose();
                game.Shutdown();
                Refuse(refused.Message);
                return;
            }

            GameplayWorldBehaviour holder = gameObject.AddComponent<GameplayWorldBehaviour>();
            holder.World = game.World;
            if (!Saltmarsh.Persistence.SaltmarshCheckpointCodecs.TryBuild(out _, out var codecs, out string codecFailure))
                throw new InvalidOperationException(codecFailure);
            var saveOptions = new SaveServiceOptions("saltmarsh", codecs!)
            {
                Directory = System.IO.Path.Combine(Application.persistentDataPath, "SaltmarshSaves"),
                RegionId = () => string.Empty,
            };
            ConfigureSaves(saveOptions);
            Saves = new SaveService(game.Root, saveOptions);
            UseSaves(Saves);
            game.Root.Start();
        }

        private void Install(NarrativeWorld game)
        {
            GameplayWorld world = game.World;
            if (preloadNeighbours)
            {
                world.Streamer.PreloadNeighbours = true;
            }

            world.Streamer.Observe(destroyCancellationToken);
            world.CreateViews(transform);
            Camera? orbit = playerCamera != null ? playerCamera : Camera.main;
            Player = PlayerSession.Install(world, PlayerExtension!, orbit);
            Npcs = NpcSession.Install(world, NpcExtension!);
            Interactions = InteractionSession.Install(world, InteractionExtension!, PlayerExtension!.Player, PlayerExtension.PlayerKey);
            Player.Focus.AddSource(Npcs.Candidates);
            Player.Focus.AddSource(Interactions.Candidates);
            SaltmarshNarrative.Wire(game, InteractionExtension, Interactions, Npcs);
            PresentationSeams = UiAudioBootstrap.Wire(world, Player, Interactions, game, Modules);
            Narrative = game;
            World = world;
        }

        public void UseSaves(SaveService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            Saltmarsh.UiAudio.SaltmarshUiAudio? rig = UiAudioBootstrap.RigOf(gameObject);
            if (rig != null)
            {
                rig.UseSaves(service, PrepareRestored(service));
                return;
            }

            service.RootChanged += (previous, restored) =>
            {
                NarrativeWorld? old = Narrative;
                if (old != null)
                {
                    old.World.Shutdown();
                    Reattach(service, WorldBuilder.Attach(restored, old.World.Plan, false));
                }
            };
        }

        public Action<GameplayWorld> PrepareRestored(SaveService service) => next => Reattach(service, next);

        public void Reattach(SaveService service, GameplayWorld next)
        {
            NarrativeWorld? old = Narrative;
            if (service == null || next == null || old == null || content == null || Modules == null)
            {
                return;
            }

            Player?.Dispose();
            old.Delivery.Dispose();
            NarrativeWorld game = NarrativeComposer.AttachRestored(service, next, content, Modules.All);
            Install(game);
            GameplayWorldBehaviour? holder = GetComponent<GameplayWorldBehaviour>();
            if (holder != null)
            {
                holder.World = next;
            }

            Reattachments++;
        }

        private void Refuse(string failure)
        {
            Failure = failure;
            Debug.LogError("[Saltmarsh] " + Failure);
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
