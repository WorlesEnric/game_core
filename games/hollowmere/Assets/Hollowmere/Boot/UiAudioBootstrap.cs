// Hollowmere - the one call GameBoot makes to add the UI and audio (P1.5).
//
// GameBoot.Start composes the P1.3 extensions into `options` and then builds the world. The integration hook is one line
// after the three `options.Extensions.Add(...)` calls:
//
//     UiAudioBootstrap.Configure(options, gameObject);
//
// Configure loads the UI/audio content asset (Resources/Hollowmere/UiAudio) as an IGameplayWorldExtensionSource, so the
// boot assembly needs no reference to the UI or audio packages: the source adds the UI and audio world extensions to the
// options and builds its presentation rig under the host. Configure also adds this component to the host. Once
// GameBoot has installed the P1.3 sessions (same Start), the component hands them the world's presentation services:
// Player.Focus.Prompts (IPromptPresenter), Player.Input.UiIntents (IUiIntentSink), Player.Locomotion.Footsteps
// (IFootstepSink) and Interactions.Dispatcher.Feedback (IFeedbackSink); and it wraps the player's intent source so that
// movement, jump and interact are dropped while a modal screen pauses gameplay (IGameplayPauseQuery) while Pause,
// Journal and Inventory still reach the UI. Without the asset the options are returned unchanged and the game runs
// without UI and audio, with P1.3's null presenters.
#nullable enable
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Adds the Hollowmere UI and audio to a world build and wires them to GameBoot's player and interaction sessions.</summary>
    [DisallowMultipleComponent]
    public sealed class UiAudioBootstrap : MonoBehaviour
    {
        public const string ResourcePath = "Hollowmere/UiAudio";

        private GameBoot? boot;
        private GameplayWorld? wiredWorld;

        /// <summary>The world whose presentation services were last handed to the P1.3 sessions; null before wiring.</summary>
        public GameplayWorld? WiredWorld => wiredWorld;

        /// <summary>How many times the sessions were (re)wired.</summary>
        public int Wirings { get; private set; }

        /// <summary>
        /// Contributes the UI and audio to <paramref name="options"/> (presentation built under <paramref name="host"/>) and
        /// adds the session wiring to the host; returns the options.
        /// </summary>
        public static WorldBuildOptions Configure(WorldBuildOptions options, GameObject? host)
        {
            if (Resources.Load<ScriptableObject>(ResourcePath) is IGameplayWorldExtensionSource source)
            {
                source.Contribute(options, host);
                if (host != null && host.GetComponent<UiAudioBootstrap>() == null)
                {
                    host.AddComponent<UiAudioBootstrap>();
                }
            }
            else
            {
                Debug.LogWarning("[Hollowmere] no UI/audio content at Resources/" + ResourcePath + "; running without UI and audio");
            }

            return options;
        }

        /// <summary>Hands the world's presentation services to the P1.3 sessions of <paramref name="gameBoot"/>; false before boot.</summary>
        public bool Wire(GameBoot gameBoot)
        {
            GameplayWorld? world = gameBoot != null ? gameBoot.World : null;
            if (world == null)
            {
                return false;
            }

            PresentationServices services = world.Presentation;
            PlayerSession? player = gameBoot!.Player;
            if (player != null)
            {
                IPromptPresenter? prompts = services.Get<IPromptPresenter>();
                if (prompts != null)
                {
                    player.Focus.Prompts = prompts;
                }

                IUiIntentSink? intents = services.Get<IUiIntentSink>();
                if (intents != null)
                {
                    player.Input.UiIntents = intents;
                }

                IFootstepSink? footsteps = services.Get<IFootstepSink>();
                if (footsteps != null)
                {
                    player.Locomotion.Footsteps = footsteps;
                }

                IGameplayPauseQuery? pause = services.Get<IGameplayPauseQuery>();
                if (pause != null && !(player.Input.Source is PauseGatedIntentSource))
                {
                    player.Input.Source = new PauseGatedIntentSource(player.Input.Source, pause);
                }
            }

            IFeedbackSink? feedback = services.Get<IFeedbackSink>();
            if (feedback != null && gameBoot.Interactions != null)
            {
                gameBoot.Interactions.Dispatcher.Feedback = feedback;
            }

            wiredWorld = world;
            Wirings++;
            return true;
        }

        private void Update()
        {
            if (boot == null)
            {
                boot = GetComponent<GameBoot>();
                if (boot == null)
                {
                    return;
                }
            }

            if (boot.World != null && !ReferenceEquals(boot.World, wiredWorld))
            {
                Wire(boot);
            }
        }

        /// <summary>Drops movement, jump and interact while a modal UI screen pauses gameplay; UI intents pass through.</summary>
        private sealed class PauseGatedIntentSource : IPlayerIntentSource
        {
            private readonly IPlayerIntentSource inner;
            private readonly IGameplayPauseQuery pause;

            public PauseGatedIntentSource(IPlayerIntentSource inner, IGameplayPauseQuery pause)
            {
                this.inner = inner;
                this.pause = pause;
            }

            public PlayerIntent Sample()
            {
                PlayerIntent intent = inner.Sample();
                if (!pause.GameplayPaused)
                {
                    return intent;
                }

                intent.Move = Vector2.zero;
                intent.Look = Vector2.zero;
                intent.Run = false;
                intent.Jump = false;
                intent.Interact = false;
                return intent;
            }
        }
    }
}
