// Hollowmere - HollowmereGame: the game half that sits next to GameBoot on the Boot object (P3.1).
//
// GameBoot (P1.3-P1.5) boots the world and installs the plugins; HollowmereGame runs after it (execution order 100) and
// adds what makes Hollowmere a finished game rather than a plugin showcase:
//   * saves: the checkpoint codecs of Assets/Hollowmere/Save (Gc018 catalog) behind a SaveService created through
//     GameBoot.ConfigureSaves (P1.7a: the narrative delivery owns the save's delivery) and connected with
//     GameBoot.UseSaves (the UI's save/load screens; a restore replaces the root and GameBoot re-attaches the narrative
//     layer with NarrativeComposer.AttachRestored and reinstalls the sessions); on SaveService.Restored this component
//     picks up GameBoot's re-attached narrative world and sessions and re-attaches the director;
//   * the director (HollowmereDirector): endings, music and ambience consequences, the interim fact-request bridge,
//     portraits, world-item presence and region atmosphere, all from the authored HollowmereDirectorDefinition;
//   * the player command line: -frameLog, -autoplay, -saveDir, -quitAfterFrames (HollowmerePersistentSession keeps the
//     frame log and the autoplay runner alive across the scene reloads of New Game and Restart).
// Nothing here holds content: every Hollowmere-specific value is data on the director asset or the baked manifests.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Npc;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using GameCore.Validation.ProbeHost;
using Hollowmere.Boot;
using Hollowmere.UiAudio;
using UnityEngine;

namespace Hollowmere.Game
{
    /// <summary>Saves, restore re-attach, the director and the player command line of Hollowmere.</summary>
    [Authorable("hollowmere.game", DisplayName = "Hollowmere Game", Scope = AuthorScope.Instance, RuntimeApplicability = RuntimeApply.Rebuild,
        Doc = "The game half next to GameBoot: saves and restore re-attach, the director, the player command line (frame log, autoplay).")]
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(GameBoot))]
    public sealed class HollowmereGame : MonoBehaviour, IAuthoredObject
    {
        public const string DefaultGameId = "hollowmere";
        public const string SaveFolder = "saves";

        [SerializeField] private string authoringId = string.Empty;

        [AuthorRef(Category = "hollowmere.director", Doc = "The authored presentation data (Assets/Hollowmere/Game/HollowmereDirector.asset).")]
        [SerializeField] private HollowmereDirectorDefinition? director;

        [AuthorField(Doc = "The save game id (slot headers and the save folder).")]
        [SerializeField] private string gameId = DefaultGameId;

        private GameBoot? boot;
        private HollowmereUiAudio? rig;
        private AutoplayIntentSource? autoplayIntents;

        public string AuthoringId => authoringId;

        /// <summary>The director asset (null: the game runs without endings and consequences).</summary>
        public HollowmereDirectorDefinition? DirectorDefinition => director;

        /// <summary>The running save service; null before Start or when the codecs did not build.</summary>
        public SaveService? Saves { get; private set; }

        /// <summary>The current narrative world (GameBoot's, or the re-attached one after a restore).</summary>
        public NarrativeWorld? Narrative { get; private set; }

        public GameplayWorld? World => Narrative?.World;

        public PlayerSession? Player { get; private set; }

        public NpcSession? Npcs { get; private set; }

        public InteractionSession? Interactions { get; private set; }

        public HollowmereDirector? Director { get; private set; }

        public HollowmereUiAudio? Rig => rig;

        public GameBoot? Boot => boot;

        public HollowmereCommandLine CommandLine { get; private set; } = HollowmereCommandLine.Empty();

        public HollowmerePersistentSession? Session { get; private set; }

        /// <summary>How many restores re-attached the world.</summary>
        public int Restores { get; private set; }

        /// <summary>Why the game half did not start (empty when it did).</summary>
        public string Failure { get; private set; } = string.Empty;

        /// <summary>The last restore re-attach problem (empty when it succeeded).</summary>
        public string RestoreFailure { get; private set; } = string.Empty;

        /// <summary>The build revision (revision.txt next to the player's data folder; "editor" in the Editor).</summary>
        public static string Revision()
        {
            if (Application.isEditor)
            {
                return "editor";
            }

            try
            {
                string path = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "revision.txt");
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "unknown";
            }
            catch (IOException)
            {
                return "unknown";
            }
        }

        /// <summary>Points the game at a director asset (authoring and tests).</summary>
        public void Configure(HollowmereDirectorDefinition? directorDefinition, string saveGameId)
        {
            director = directorDefinition;
            gameId = string.IsNullOrEmpty(saveGameId) ? DefaultGameId : saveGameId;
        }

        private void Reset() => authoringId = AuthoringIdField.Ensure(authoringId);

        private void OnValidate()
        {
            if (!AuthoringIds.IsValid(authoringId))
            {
                authoringId = AuthoringIdField.Ensure(authoringId);
            }
        }

        private void Start()
        {
            boot = GetComponent<GameBoot>();
            CommandLine = HollowmereCommandLine.Parse(Environment.GetCommandLineArgs());
            Session = HollowmerePersistentSession.Ensure(CommandLine);
            if (boot == null || boot.World == null || boot.Narrative == null || boot.Modules == null)
            {
                Failure = "GameBoot did not boot" + (boot != null && boot.Failure.Length > 0 ? ": " + boot.Failure : string.Empty);
                Debug.LogError("[Hollowmere] " + Failure);
                Session?.Attach(this);
                return;
            }

            rig = UiAudioBootstrap.RigOf(gameObject);
            Narrative = boot.Narrative;
            Player = boot.Player;
            Npcs = boot.Npcs;
            Interactions = boot.Interactions;
            InstallAutoplayIntents();
            Director = new HollowmereDirector(this, director);
            Director.Attach(boot.Narrative, boot.Modules);
            UseSaves();
            Session?.Attach(this);
        }

        private void LateUpdate()
        {
            Director?.Tick();
        }

        // ------------------------------------------------------------------ saves

        private void UseSaves()
        {
            GameplayWorld? world = World;
            if (world == null)
            {
                return;
            }

            if (!Gc018CheckpointCodecs.TryBuild(out _, out CheckpointCodecSet? codecs, out string detail) || codecs == null)
            {
                Debug.LogError("[Hollowmere] the checkpoint codecs did not build, saving is unavailable: " + detail);
                return;
            }

            string directory = CommandLine.SaveDirectory ?? Path.Combine(Application.persistentDataPath, SaveFolder);
            var options = new SaveServiceOptions(gameId, codecs)
            {
                Directory = directory,
                RegionId = CurrentRegionId,
                PlayTimeSeconds = () => Time.realtimeSinceStartupAsDouble,
            };
            if (boot == null || !boot.ConfigureSaves(options))
            {
                Debug.LogError("[Hollowmere] GameBoot could not configure the save service's delivery; saving is unavailable");
                return;
            }

            Saves = new SaveService(world.Root, options);
            Saves.Restored += OnRestored;
            boot.UseSaves(Saves);
        }

        /// <summary>After a restore GameBoot has re-attached the narrative layer and the sessions; follow it.</summary>
        private void OnRestored(SaveResult result)
        {
            if (boot == null || boot.Narrative == null || boot.Modules == null)
            {
                RestoreFailure = "GameBoot has no narrative world after the restore of " + result.Slot;
                Debug.LogError("[Hollowmere] restore: " + RestoreFailure);
                return;
            }

            if (ReferenceEquals(boot.Narrative, Narrative))
            {
                RestoreFailure = "GameBoot did not re-attach the restored world of " + result.Slot;
                Debug.LogError("[Hollowmere] restore: " + RestoreFailure);
                return;
            }

            Narrative = boot.Narrative;
            Player = boot.Player;
            Npcs = boot.Npcs;
            Interactions = boot.Interactions;
            RestoreFailure = string.Empty;
            Restores++;
            InstallAutoplayIntents();
            Director?.Attach(boot.Narrative, boot.Modules);
            Session?.Mark("restore");
        }

        /// <summary>The authoring id of the region the player is in (the save header's region).</summary>
        public string CurrentRegionId()
        {
            ManifestRegion? region = CurrentRegion();
            return region != null ? region.authoringId : string.Empty;
        }

        /// <summary>The manifest region the player is in, or null.</summary>
        public ManifestRegion? CurrentRegion()
        {
            GameplayWorld? world = World;
            PlayerSession? player = Player;
            if (world == null || player == null)
            {
                return null;
            }

            int key = world.Slots.ReadOrDefault(player.Extension.Player, PlayerSlots.Owner, PlayerSlots.RegionKey, 0);
            for (int i = 0; i < world.Manifest.Regions.Count; i++)
            {
                if (world.Manifest.Regions[i].key == key)
                {
                    return world.Manifest.Regions[i];
                }
            }

            return null;
        }

        /// <summary>The player's committed position in metres.</summary>
        public Vector3 PlayerPosition()
        {
            GameplayWorld? world = World;
            PlayerSession? player = Player;
            if (world == null || player == null)
            {
                return Vector3.zero;
            }

            TargetId target = player.Extension.Player;
            return new Vector3(
                (float)GameplayUnits.ToMetres(world.Slots.ReadOrDefault(target, PlayerSlots.Owner, PlayerSlots.PosX, 0)),
                (float)GameplayUnits.ToMetres(world.Slots.ReadOrDefault(target, PlayerSlots.Owner, PlayerSlots.PosY, 0)),
                (float)GameplayUnits.ToMetres(world.Slots.ReadOrDefault(target, PlayerSlots.Owner, PlayerSlots.PosZ, 0)));
        }

        /// <summary>The scripted intents (autoplay and tests): an override of the player's input source.</summary>
        public AutoplayIntentSource? Intents => autoplayIntents;

        private void InstallAutoplayIntents()
        {
            PlayerSession? player = Player;
            if (player == null)
            {
                return;
            }

            if (player.Input.Source is AutoplayIntentSource existing)
            {
                autoplayIntents = existing;
                return;
            }

            autoplayIntents = new AutoplayIntentSource(player.Input.Source);
            player.Input.Source = autoplayIntents;
            GameplayWorld? world = World;
            PlayerInputAdapter input = player.Input;
            TargetId target = player.Extension.Player;
            autoplayIntents.Heading = () =>
            {
                float? camera = input.CameraYaw?.Invoke();
                if (camera.HasValue)
                {
                    return camera.Value;
                }

                return world == null ? 0f : (float)GameplayUnits.MilliradiansToDegrees(world.Slots.ReadOrDefault(target, PlayerSlots.Owner, PlayerSlots.Yaw, 0));
            };
            autoplayIntents.Paused = () => world != null && world.Presentation.Get<IGameplayPauseQuery>() is IGameplayPauseQuery pause && pause.GameplayPaused;
        }

        private void OnDestroy()
        {
            // GameBoot owns the (possibly restored) world and shuts it down; the director only lets go of it.
            if (Saves != null)
            {
                Saves.Restored -= OnRestored;
            }

            Director?.Dispose();
            Director = null;
            Narrative = null;
        }

        /// <summary>Formats a float for logs and checks.</summary>
        internal static string F(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
