// Hollowmere - GameBoot: boots the baked Hollowmere world with the player, NPC and interaction plugins (P1.3), the
// narrative plugins (P1.4) and the UI and audio (P1.5).
//
// Boot.unity holds one GameBoot. On Start HollowmereComposition builds the base world, narrative, UI/audio and the
// reviewed additive extensions. The original generated catalog must match the baked manifest before any additive
// catalog set is composed. GameApplication.TryBoot creates the one root (left Ready), then NarrativeComposer.Attach
// installs the world and narrative modules. It then creates the view
// binders, installs the player loop (input adapter, locomotion, focus, portal probe, orbit camera), the NPC loop and the
// interaction loop, hands P1.3's seams their P1.4 implementations (HollowmereNarrative.Wire) and their P1.5
// presenters (UiAudioBootstrap.Wire), and starts the root. Streaming, input, narrative delivery and presentation then
// run inside the application's one pump; GameBoot itself has no Update.
//
// The UI starts on the main menu, which pauses gameplay input; New Game shows the HUD. Saving needs Hollowmere's
// checkpoint codecs (P3.1); until then the save and load screens answer GP-UI-014.
//
// P1.7a (A2/A11): shared composition and GameApplication.TryBoot refuse invalid worlds; a later failure (views, sessions,
// wiring) shuts the narrative world down, which stops the root. Install is the per-root half (streaming options, views,
// P1.3 sessions, P1.4/P1.5 wiring) and runs again for a restored root: UseSaves(service) connects the save service
// (through the UI runtime's restore path when the UI rig exists, else RootChanged directly) and every restore re-attaches
// the narrative layer on the restored root's delivery owner (NarrativeComposer.Attach) and reinstalls the sessions.
// ConfigureSaves sets the narrative delivery owner and declares only the composed definition's exact base catalog
// compatible for additive restore; it never adds an arbitrary capture fingerprint to the compatibility set.
//
// P3.1 (A11, additive): in the player, HollowmereApplication registers Hollowmere's definition at SubsystemRegistration,
// so the application bootstrap composes the game root before this scene loads. Start then adopts that root (Adopted):
// the UI/audio rig, plan, extensions and modules come from the registration, NarrativeComposer.Attach attaches the
// narrative layer, and Install / Start run as for a booted root. Without an adoptable registration (Editor Play Mode,
// -noRegister, New Game / Restart reloads) GameBoot boots its own root as before.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
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
        private HollowmereComposition? ownedComposition;

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

        /// <summary>The save service of the last <see cref="UseSaves"/> call (null before it).</summary>
        public SaveService? Saves { get; private set; }

        /// <summary>
        /// Raised at the start of every <see cref="UseSaves"/> (before the UI-rig early return), so a trusted Editor
        /// integration can bind Studio admission (R2-G request 4: capture, session readiness, live smoke) to the new
        /// service. An instance event: nothing subscribes in a player build, and GameBoot references no Editor assembly.
        /// </summary>
        public event Action<GameBoot, SaveService>? SavesInstalled;

        /// <summary>
        /// The admission session readiness of <paramref name="service"/> (R2-G2's exact lambda): the world and its narrative
        /// layer exist and the world's root is both the save service's active root and the application's current root.
        /// Follows World and Narrative after every re-attach; a restore makes the restored root current (SADR-021, APP-1).
        /// </summary>
        public bool AdmissionReady(SaveService service) =>
            service != null && World != null && Narrative != null
            && ReferenceEquals(World.Root, service.ActiveRoot) && ReferenceEquals(World.Root, GameApplication.Current);

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

        /// <summary>Restores this boot re-attached the narrative layer and the sessions for (P1.7a).</summary>
        public int Reattachments { get; private set; }

        /// <summary>
        /// Sets <paramref name="options"/>' DeliveryFactory (P1.7a, A1): the narrative delivery's own owner for the booted
        /// root, a new narrative owner for a restored one. False before the boot.
        /// </summary>
        public bool ConfigureSaves(SaveServiceOptions options)
        {
            if (options == null || Narrative == null)
            {
                return false;
            }

            NarrativeDelivery.Configure(options, Narrative);
            // Only the reviewed additive composition declares its exact unchanged base catalog compatible.
            if (ownedComposition != null && !ownedComposition.BaseCatalogHash.Equals(Narrative.Root.CatalogHash))
            {
                var compatible = new List<ContentHash>(options.CompatibleCatalogs);
                if (!compatible.Contains(ownedComposition.BaseCatalogHash)) compatible.Add(ownedComposition.BaseCatalogHash);
                options.CompatibleCatalogs = compatible.AsReadOnly();
            }
            return true;
        }

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

        private UnityEngine.ThreadPriority previousLoadingPriority;

        public static void ConfigureFramePacing(string[] args)
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-frameVsync" && int.TryParse(args[i + 1], out int value) && (value == 0 || value == 1))
                    QualitySettings.vSyncCount = value;
        }

        private void Awake()
        {
            ConfigureFramePacing(Environment.GetCommandLineArgs());
            previousLoadingPriority = Application.backgroundLoadingPriority;
            Application.backgroundLoadingPriority = UnityEngine.ThreadPriority.Low;
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

            HollowmereApplication? registered = FindAnyObjectByType<HollowmereApplication>();
            if (registered != null && Adopt(registered))
            {
                return;
            }

            if (!HollowmereComposition.TryCompose(manifest, content, player, npcs, interactions, gameObject,
                out HollowmereComposition? composition, out string failure) || composition == null)
            {
                Refuse(failure);
                return;
            }
            ownedComposition = composition;
            if (!GameApplication.TryBoot(composition.Definition, new GameApplicationBootOptions { StartImmediately = false },
                out GameApplicationRoot? root, out GameApplicationBootFailed? bootFailure) || root == null)
            {
                composition.ReleaseManifest();
                ownedComposition = null;
                Refuse("the Hollowmere world refused to boot: " + bootFailure);
                return;
            }

            NarrativeWorld? game = null;
            PlayerExtension = composition.PlayerExtension;
            NpcExtension = composition.NpcExtension;
            InteractionExtension = composition.InteractionExtension;
            Modules = composition.Modules;
            try
            {
                game = NarrativeComposer.Attach(root, composition.Plan, content, composition.Modules.All, true);
                Install(game);
            }
            catch (Exception refused) when (refused is InvalidOperationException || refused is ArgumentException)
            {
                // A11: a refusal after the root booted stops it (no half-wired world keeps running).
                Narrative = null;
                World = null;
                Player?.Dispose();
                if (game != null) game.Shutdown();
                else root.Stop("Hollowmere attach failed");
                composition.ReleaseManifest();
                ownedComposition = null;
                Refuse(refused.Message);
                return;
            }

            GameplayWorldBehaviour holder = gameObject.AddComponent<GameplayWorldBehaviour>();
            holder.World = game.World;
            game.Root.Start();
        }

        /// <summary>True when this boot adopted the root registered at SubsystemRegistration (HollowmereApplication, A11).</summary>
        public bool Adopted { get; private set; }

        /// <summary>
        /// Adopts the registered root (P3.1, A11): true when the boot is handled here (adopted, or refused after the
        /// hand-over); false when the registration is not adoptable and was discarded, so Start boots its own root.
        /// </summary>
        private bool Adopt(HollowmereApplication registered)
        {
            GameApplicationRoot? root = registered.AdoptableRoot(manifest, content, player, npcs, interactions, out string reason);
            HollowmereComposition? composition = registered.Composition;
            if (root == null || composition == null || content == null)
            {
                registered.Discard(reason.Length > 0 ? reason : "GameBoot has no content manifest");
                return false;
            }

            registered.HandOver(transform);
            ownedComposition = composition;
            PlayerExtension = composition.PlayerExtension;
            NpcExtension = composition.NpcExtension;
            InteractionExtension = composition.InteractionExtension;
            Modules = composition.Modules;
            try
            {
                NarrativeWorld game = NarrativeComposer.Attach(root, composition.Plan, content, composition.Modules.All, true);
                Install(game);
            }
            catch (Exception refused) when (refused is InvalidOperationException || refused is ArgumentException)
            {
                Narrative = null;
                World = null;
                Player?.Dispose();
                root.Stop("Hollowmere could not adopt the registered root");
                Refuse("adopting the registered root failed: " + refused.Message);
                return true;
            }

            GameplayWorldBehaviour holder = gameObject.AddComponent<GameplayWorldBehaviour>();
            holder.World = World!;
            root.Start();
            Adopted = true;
            Debug.Log("[Hollowmere] adopted the application root registered at SubsystemRegistration");
            return true;
        }

        /// <summary>The per-root half of the boot: streaming options, views, the P1.3 sessions and their wiring.</summary>
        private void Install(NarrativeWorld game)
        {
            GameplayWorld world = game.World;
            if (preloadNeighbours)
            {
                world.Streamer.PreloadNeighbours = true;
            }

            Hollowmere.UiAudio.HollowmereUiAudio? rig = UiAudioBootstrap.RigOf(gameObject);
            world.UseSceneLoader(new DeferredRegionLoader(new UnitySceneLoader(), () => Time.frameCount, Time.frameCount + 3,
                () => rig == null || rig.CanLoadFirstRegion));
            world.Streamer.Observe(destroyCancellationToken);
            world.CreateViews(transform);
            Camera? orbit = playerCamera != null ? playerCamera : Camera.main;
            Player = PlayerSession.Install(world, PlayerExtension!, orbit);
            Npcs = NpcSession.Install(world, NpcExtension!);
            Interactions = InteractionSession.Install(world, InteractionExtension!, PlayerExtension!.Player, PlayerExtension.PlayerKey);
            Player.Focus.AddSource(Npcs.Candidates);
            Player.Focus.AddSource(Interactions.Candidates);
            HollowmereNarrative.Wire(game, InteractionExtension, Interactions, Npcs);
            PresentationSeams = UiAudioBootstrap.Wire(world, Player, Interactions, game, Modules);
            Narrative = game;
            World = world;
        }

        /// <summary>
        /// Connects a save service (P1.7a, A2): through the UI runtime's restore path when the UI rig exists (it attaches
        /// the base world; PrepareRestored adds the rest), else straight on RootChanged.
        /// </summary>
        public void UseSaves(SaveService service)
        {
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            Saves = service;
            SavesInstalled?.Invoke(this, service);
            Hollowmere.UiAudio.HollowmereUiAudio? rig = UiAudioBootstrap.RigOf(gameObject);
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

        /// <summary>The UI runtime's prepareRestoredWorld callback: re-attaches the narrative layer and the sessions.</summary>
        public Action<GameplayWorld> PrepareRestored(SaveService service) => next => Reattach(service, next);

        /// <summary>
        /// Re-attaches the narrative layer to a restored base world (the save service's restored delivery owner holds the
        /// reinstated obligations) and reinstalls the sessions and presentation wiring (P1.7a, A2).
        /// </summary>
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
            Debug.LogError("[Hollowmere] " + Failure);
        }

        private void OnDestroy()
        {
            Application.backgroundLoadingPriority = previousLoadingPriority;
            NarrativeWorld? game = Narrative;
            Narrative = null;
            World = null;
            Player?.Dispose();
            ownedComposition?.ReleaseManifest();
            ownedComposition = null;
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
