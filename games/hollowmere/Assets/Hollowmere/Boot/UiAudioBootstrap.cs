// Hollowmere - how GameBoot composes the UI and audio with the player (P1.3) and narrative (P1.4) halves (P1.5).
//
// GameBoot calls, in order:
//   Configure(build, gameObject)          before the world is built: loads the UI/audio content asset
//                                         (Resources/Hollowmere/UiAudio, an IGameplayWorldExtensionSource), adds the ui
//                                         and audio world extensions to the build options and creates the rig under
//                                         the GameBoot object (UiRoot and AudioSources only with a graphics device);
//   AssignViews(rig, modules)             before the narrative boot: P1.4's modules push their view models into the UI
//                                         runtime (IDialogueView, IJournalView, IInventoryView) and voice lines into the
//                                         audio runtime (IVoiceLinePlayer);
//   Wire(world, player, interactions, game, modules)
//                                         after the P1.3 sessions are installed: hands them the world's presentation
//                                         services (prompt presenter, UI intent sink, footstep sink, interaction feedback
//                                         sink), gates the player's intent source on IGameplayPauseQuery (movement,
//                                         look, run, jump and interact are dropped while a modal screen is open; Pause,
//                                         Journal and Inventory still reach the UI), gives the narrative runtime the
//                                         feedback and message sinks (playAudio, showMessage), and registers the UI's
//                                         IDialogueInput and IInventoryInput over P1.4's DialogueRunner and
//                                         InventoryCommands.
// Without the content asset Configure leaves the options unchanged and the others do nothing: the game runs with the
// P1.3/P1.4 null presenters.
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Gameplay.Dialogue;
using GameCore.Gameplay.Interaction;
using GameCore.Gameplay.Inventory;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using Hollowmere.Narrative;
using Hollowmere.UiAudio;
using UnityEngine;

namespace Hollowmere.Boot
{
    /// <summary>Composes the Hollowmere UI and audio into GameBoot's world.</summary>
    public static class UiAudioBootstrap
    {
        public const string ResourcePath = "Hollowmere/UiAudio";

        /// <summary>
        /// Contributes the UI and audio to <paramref name="options"/> (rig built under <paramref name="host"/>); returns the
        /// options.
        /// </summary>
        public static WorldBuildOptions Configure(WorldBuildOptions options, GameObject? host)
        {
            if (Resources.Load<ScriptableObject>(ResourcePath) is IGameplayWorldExtensionSource source)
            {
                source.Contribute(options, host);
            }
            else
            {
                Debug.LogWarning("[Hollowmere] no UI/audio content at Resources/" + ResourcePath + "; running without UI and audio");
            }

            return options;
        }

        /// <summary>The rig Configure created under <paramref name="host"/>, or null.</summary>
        public static HollowmereUiAudio? RigOf(GameObject host) => host != null ? host.GetComponentInChildren<HollowmereUiAudio>() : null;

        /// <summary>Points P1.4's presenters at the UI and audio runtimes (call before the narrative boot).</summary>
        public static void AssignViews(HollowmereUiAudio? rig, HollowmereNarrativeModules modules)
        {
            if (rig == null || modules == null)
            {
                return;
            }

            modules.Dialogue.View = rig.Ui;
            modules.Quest.View = rig.Ui;
            modules.Inventory.View = rig.Ui;
            modules.Dialogue.VoicePlayer = rig.Audio.Voice;
        }

        /// <summary>
        /// Hands the world's presentation services to the P1.3 sessions and the narrative runtime, and registers the UI's
        /// dialogue and inventory input; returns how many seams were wired.
        /// </summary>
        public static int Wire(GameplayWorld world, PlayerSession? player, InteractionSession? interactions, NarrativeWorld? game, HollowmereNarrativeModules? modules)
        {
            if (world == null)
            {
                return 0;
            }

            PresentationServices services = world.Presentation;
            int wired = 0;
            IFeedbackSink? feedback = services.Get<IFeedbackSink>();
            if (player != null)
            {
                IPromptPresenter? prompts = services.Get<IPromptPresenter>();
                if (prompts != null)
                {
                    player.Focus.Prompts = prompts;
                    wired++;
                }

                IUiIntentSink? intents = services.Get<IUiIntentSink>();
                if (intents != null)
                {
                    player.Input.UiIntents = intents;
                    wired++;
                }

                IFootstepSink? footsteps = services.Get<IFootstepSink>();
                if (footsteps != null)
                {
                    player.Locomotion.Footsteps = footsteps;
                    wired++;
                }

                IGameplayPauseQuery? pause = services.Get<IGameplayPauseQuery>();
                if (pause != null && !(player.Input.Source is PauseGatedIntentSource))
                {
                    player.Input.Source = new PauseGatedIntentSource(player.Input.Source, pause);
                    wired++;
                }
            }

            if (feedback != null && interactions != null)
            {
                interactions.Dispatcher.Feedback = feedback;
                wired++;
            }

            if (game != null)
            {
                if (feedback != null)
                {
                    game.Runtime.UseFeedback(feedback);
                    wired++;
                }

                INarrativeMessageSink? messages = services.Get<INarrativeMessageSink>();
                if (messages != null)
                {
                    game.Runtime.UseMessages(messages);
                    wired++;
                }

                if (modules != null && modules.Dialogue.Runner != null)
                {
                    services.Register<IDialogueInput>(new DialogueInput(modules.Dialogue.Runner));
                    wired++;
                }

                if (modules != null && modules.Inventory.Commands != null)
                {
                    services.Register<IInventoryInput>(new InventoryInput(modules.Inventory.Commands, game.Runtime.Content));
                    wired++;
                }
            }

            return wired;
        }

        /// <summary>Drops movement, look, run, jump and interact while a modal UI screen pauses gameplay; UI intents pass through.</summary>
        public sealed class PauseGatedIntentSource : IPlayerIntentSource
        {
            private readonly IPlayerIntentSource inner;
            private readonly IGameplayPauseQuery pause;

            public PauseGatedIntentSource(IPlayerIntentSource inner, IGameplayPauseQuery pause)
            {
                this.inner = inner;
                this.pause = pause;
            }

            public IPlayerIntentSource Inner => inner;

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

        /// <summary>The UI's dialogue input over P1.4's DialogueRunner (dialogue.choose / dialogue.advance).</summary>
        private sealed class DialogueInput : IDialogueInput
        {
            private readonly DialogueRunner runner;

            public DialogueInput(DialogueRunner runner)
            {
                this.runner = runner;
            }

            public void Choose(int option) => runner.Choose(option);

            public void Advance() => runner.Advance();
        }

        /// <summary>The UI's inventory input over P1.4's InventoryCommands (consume / drop one of an item, by its stable key).</summary>
        private sealed class InventoryInput : IInventoryInput
        {
            private readonly InventoryCommands commands;
            private readonly Dictionary<int, string> refs = new Dictionary<int, string>();

            public InventoryInput(InventoryCommands commands, GameplayContentManifest content)
            {
                this.commands = commands;
                IReadOnlyList<ContentEntry> entries = content.Entries;
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i].key != 0 && !refs.ContainsKey(entries[i].key))
                    {
                        refs.Add(entries[i].key, entries[i].authoringId);
                    }
                }
            }

            public void Use(int itemKey)
            {
                if (refs.TryGetValue(itemKey, out string? item))
                {
                    commands.Consume(item, 1);
                }
            }

            public void Drop(int itemKey)
            {
                if (refs.TryGetValue(itemKey, out string? item))
                {
                    commands.Drop(item, 1);
                }
            }
        }
    }
}
