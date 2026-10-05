// Hollowmere P1.5 PlayMode - BootWiring: Boot.unity boots the narrative world with the UI and audio wired to P1.3/P1.4.
//
// Boots Boot/Boot.unity (GameBoot: P1.3 extensions + UiAudioBootstrap + HollowmereNarrative.Boot/Wire) and checks:
//   * the world holds the ui and audio extensions next to player, npc and interaction;
//   * P1.3's sessions present through the UI and audio runtimes (prompt, UI intents, footsteps, interaction feedback);
//   * the narrative runtime's feedback and message sinks and the UI's dialogue/inventory input are wired;
//   * the main menu pauses gameplay: the player's movement intents are dropped while Pause still reaches the UI; New Game
//     shows the HUD and movement passes again;
//   * a real conversation (Maren, through P1.4's DialogueRunner) reaches the dialogue panel through LineShown ->
//     DialoguePresenter -> IDialogueView, and the UI's advance/choose goes back through IDialogueInput;
//   * a P1.3 footstep plays the footstep clip.
#nullable enable
using System.Collections;
using System.Diagnostics;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Logic;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using Hollowmere.Boot;
using Hollowmere.Narrative;
using Hollowmere.UiAudio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.P1_5.PlayMode.Tests
{
    public sealed class BootWiring
    {
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const int MaxFrames = 300;

        /// <summary>Destroys Boot.unity's GameBoot (its OnDestroy shuts the narrative world down).</summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            GameBoot? boot = Object.FindAnyObjectByType<GameBoot>();
            if (boot != null)
            {
                Object.Destroy(boot.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator BootSceneWiresUiAndAudioIntoTheP13AndP14Sessions()
        {
#if UNITY_EDITOR
            var clock = Stopwatch.StartNew();
            AsyncOperation? load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(BootScene, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null, "Boot.unity must exist");
            while (!load!.isDone)
            {
                yield return null;
            }

            GameBoot? boot = Object.FindAnyObjectByType<GameBoot>();
            Assert.That(boot, Is.Not.Null);
            int frames = 0;
            while (boot!.World == null && boot.Failure.Length == 0 && frames++ < MaxFrames)
            {
                yield return null;
            }

            Assert.That(boot.Failure, Is.Empty);
            GameplayWorld world = boot.World!;
            NarrativeWorld game = boot.Narrative!;
            HollowmereUiAudio? rig = UiAudioBootstrap.RigOf(boot.gameObject);
            Assert.That(rig, Is.Not.Null, "UiAudioBootstrap.Configure created the rig under GameBoot");
            UiRuntime ui = rig!.Ui;
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < world.Plan.Extensions.Count; i++)
            {
                names.Add(world.Plan.Extensions[i].Name);
            }

            Assert.That(names, Is.EqualTo(new[] { "player", "npc", "interaction", "ui", "audio" }));
            Assert.That(boot.PresentationSeams, Is.EqualTo(11), "prompt, intents, footsteps, pause gate, feedback, narrative feedback, messages, dialogue input, inventory input");

            PresentationServices services = world.Presentation;
            PlayerSession player = boot.Player!;
            Assert.That(player.Focus.Prompts, Is.SameAs(ui));
            Assert.That(player.Input.UiIntents, Is.SameAs(ui));
            Assert.That(player.Locomotion.Footsteps, Is.SameAs(rig.Audio.Sfx));
            Assert.That(boot.Interactions!.Dispatcher.Feedback, Is.SameAs(rig.Audio.Sfx));
            Assert.That(game.Runtime.Feedback, Is.SameAs(rig.Audio.Sfx));
            Assert.That(game.Runtime.Messages, Is.SameAs(ui));
            Assert.That(boot.Modules!.Dialogue.View, Is.SameAs(ui));
            Assert.That(boot.Modules.Quest.View, Is.SameAs(ui));
            Assert.That(boot.Modules.Inventory.View, Is.SameAs(ui));
            Assert.That(boot.Modules.Dialogue.VoicePlayer, Is.SameAs(rig.Audio.Voice));
            Assert.That(services.Get<IDialogueInput>(), Is.Not.Null);
            Assert.That(services.Get<IInventoryInput>(), Is.Not.Null);

            // The main menu pauses gameplay input; UI intents still pass.
            Assert.That(ui.Screen, Is.EqualTo(UiScreen.Menu));
            Assert.That(ui.GameplayPaused, Is.True, "the main menu pauses gameplay");
            Assert.That(player.Input.Source, Is.InstanceOf<UiAudioBootstrap.PauseGatedIntentSource>());
            var gate = (UiAudioBootstrap.PauseGatedIntentSource)player.Input.Source;
            var scripted = new FixedIntent(new PlayerIntent { Move = new Vector2(0f, 1f), Jump = true, Pause = true });
            player.Input.Source = new UiAudioBootstrap.PauseGatedIntentSource(scripted, ui);
            PlayerIntent gated = player.Input.Source.Sample();
            Assert.That(gated.Move, Is.EqualTo(Vector2.zero), "movement is dropped while paused");
            Assert.That(gated.Jump, Is.False);
            Assert.That(gated.Pause, Is.True, "Pause still reaches the UI");
            Assert.That(ui.Dispatcher.Dispatch("newgame").Accepted, Is.True);
            frames = 0;
            while (ui.Screen != UiScreen.Hud && frames++ < MaxFrames)
            {
                yield return null;
            }

            Assert.That(ui.Screen, Is.EqualTo(UiScreen.Hud), "new game on the freshly booted world shows the HUD");
            Assert.That(player.Input.Source.Sample().Move, Is.EqualTo(new Vector2(0f, 1f)), "movement passes on the HUD");
            player.Input.Source = gate;

            // A real conversation reaches the dialogue panel through P1.4's LineShown -> DialoguePresenter -> IDialogueView.
            int received = ui.Models.Dialogue.Received;
            ConversationStart started = game.Conversations.TryStart(HollowmereNarrative.MarenId, HollowmereNarrative.MarenGraphRef);
            Assert.That(started.Started, Is.True, started.Detail);
            frames = 0;
            while (!ui.Models.Dialogue.Visible && frames++ < MaxFrames)
            {
                yield return null;
            }

            Assert.That(ui.Models.Dialogue.Visible, Is.True, "LineShown reached the dialogue panel");
            Assert.That(ui.Models.Dialogue.Received, Is.GreaterThan(received));
            Assert.That(ui.Models.Dialogue.Text, Is.Not.Empty);
            int serial = ui.Models.Dialogue.Serial;
            string first = ui.Models.Dialogue.Text;
            UiDispatchResult next = ui.Dispatcher.Dispatch(ui.Models.Dialogue.HasChoices ? "choose.selected" : "advance");
            Assert.That(next.Accepted, Is.True, next.Detail);
            frames = 0;
            while (ui.Models.Dialogue.Visible && ui.Models.Dialogue.Serial == serial && frames++ < MaxFrames)
            {
                yield return null;
            }

            Assert.That(!ui.Models.Dialogue.Visible || ui.Models.Dialogue.Serial != serial, Is.True, "the UI's advance/choose moved the conversation on");

            // A P1.3 footstep plays the footstep clip.
            SfxPool sfx = rig.Audio.Sfx;
            int played = sfx.Played;
            player.Locomotion.Footsteps.OnFootstep(new FootstepEvent("player", 0, 0, 0, 0, false));
            Assert.That(sfx.Played, Is.EqualTo(played + 1), "a P1.3 footstep plays the footstep clip");

            UnityEngine.Debug.Log("P1.5-BOOT seams=" + boot.PresentationSeams + " extensions=" + string.Join(",", names)
                + " screen=" + ui.Screen + " firstLine=\"" + first + "\" dialoguePushes=" + ui.Models.Dialogue.Received
                + " ms=" + clock.ElapsedMilliseconds);
#else
            Assert.Ignore("Boot.unity loads through the Editor scene manager");
            yield break;
#endif
        }

        private sealed class FixedIntent : IPlayerIntentSource
        {
            private readonly PlayerIntent intent;

            public FixedIntent(PlayerIntent intent)
            {
                this.intent = intent;
            }

            public PlayerIntent Sample() => intent;
        }
    }
}
