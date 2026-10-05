// Hollowmere P1.5 PlayMode - BootWiring: Boot.unity with the UiAudioBootstrap hook composes the UI and audio with P1.3.
//
// Boots Boot/Boot.unity. When GameBoot carries the integration hook (UiAudioBootstrap.Configure(options, gameObject)
// after the P1.3 extensions are added) the world holds the ui and audio extensions next to player, npc and interaction,
// and UiAudioBootstrap hands P1.3's sessions the world's presentation services: the prompt presenter, the UI intent
// sink, the footstep sink and the interaction feedback sink are the UI and audio runtimes; the main menu pauses
// gameplay, so the player's movement intents are dropped while Pause still reaches the UI. Without the hook the test is
// ignored (GameBoot is P1.3's file; the integrator adds the line).
#nullable enable
using System.Collections;
using System.Diagnostics;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using Hollowmere.Boot;
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

        /// <summary>Destroys Boot.unity's GameBoot (its OnDestroy stops the root), also when the test was ignored.</summary>
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
        public IEnumerator BootSceneWiresUiAndAudioIntoTheP13Sessions()
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
            UiAudioBootstrap? bootstrap = boot.GetComponent<UiAudioBootstrap>();
            if (bootstrap == null)
            {
                Assert.Ignore("GameBoot has no UI/audio hook: add UiAudioBootstrap.Configure(options, gameObject) after the P1.3 extensions");
            }

            while (bootstrap!.WiredWorld == null && frames++ < MaxFrames)
            {
                yield return null;
            }

            GameplayWorld world = boot.World!;
            Assert.That(bootstrap.WiredWorld, Is.SameAs(world));
            var names = new System.Collections.Generic.List<string>();
            for (int i = 0; i < world.Plan.Extensions.Count; i++)
            {
                names.Add(world.Plan.Extensions[i].Name);
            }

            Assert.That(names, Is.SupersetOf(new[] { "ui", "audio" }));

            PresentationServices services = world.Presentation;
            PlayerSession player = boot.Player!;
            Assert.That(player.Focus.Prompts, Is.SameAs(services.Get<IPromptPresenter>()));
            Assert.That(player.Input.UiIntents, Is.SameAs(services.Get<IUiIntentSink>()));
            Assert.That(player.Locomotion.Footsteps, Is.SameAs(services.Get<IFootstepSink>()));
            Assert.That(boot.Interactions!.Dispatcher.Feedback, Is.SameAs(services.Get<IFeedbackSink>()));
            Assert.That(player.Focus.Prompts, Is.Not.InstanceOf<NullPromptPresenter>());

            IGameplayPauseQuery pause = services.Get<IGameplayPauseQuery>()!;
            Assert.That(pause.GameplayPaused, Is.True, "the main menu pauses gameplay");
            var scripted = new FixedIntent(new PlayerIntent { Move = new Vector2(0f, 1f), Jump = true, Pause = true });
            player.Input.Source = scripted;
            bootstrap.Wire(boot);
            PlayerIntent gated = player.Input.Source.Sample();
            Assert.That(gated.Move, Is.EqualTo(Vector2.zero), "movement is dropped while paused");
            Assert.That(gated.Jump, Is.False);
            Assert.That(gated.Pause, Is.True, "Pause still reaches the UI");

            var sfx = (SfxPool)services.Get<IFootstepSink>()!;
            int played = sfx.Played;
            player.Locomotion.Footsteps.OnFootstep(new FootstepEvent("player", 0, 0, 0, 0, false));
            Assert.That(sfx.Played, Is.EqualTo(played + 1), "a P1.3 footstep plays the footstep clip");

            UnityEngine.Debug.Log("P1.5-BOOT wired=" + bootstrap.Wirings + " extensions=" + string.Join(",", names)
                + " screen=" + (UiScreen)world.Slots.ReadOrDefault(PresentationSlots.UiSessionTarget(world.Manifest.WorldId), PresentationSlots.UiOwner, PresentationSlots.Screen, 0)
                + " ms=" + clock.ElapsedMilliseconds + " frames=" + frames);
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
