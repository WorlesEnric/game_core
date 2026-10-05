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
using Saltmarsh.Narrative;
using Saltmarsh.UiAudio;
using UnityEngine;

namespace Saltmarsh.Boot
{
    public static class UiAudioBootstrap
    {
        public const string ResourcePath = "Saltmarsh/UiAudio";

        public static WorldBuildOptions Configure(WorldBuildOptions options, GameObject? host)
        {
            if (Resources.Load<ScriptableObject>(ResourcePath) is IGameplayWorldExtensionSource source)
            {
                source.Contribute(options, host);
            }
            else
            {
                Debug.LogWarning("[Saltmarsh] no UI/audio content at Resources/" + ResourcePath + "; running without UI and audio");
            }

            return options;
        }

        public static SaltmarshUiAudio? RigOf(GameObject host) => host != null ? host.GetComponentInChildren<SaltmarshUiAudio>() : null;

        public static void AssignViews(SaltmarshUiAudio? rig, SaltmarshNarrativeModules modules)
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

        public static int Wire(GameplayWorld world, PlayerSession? player, InteractionSession? interactions, NarrativeWorld? game, SaltmarshNarrativeModules? modules)
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
