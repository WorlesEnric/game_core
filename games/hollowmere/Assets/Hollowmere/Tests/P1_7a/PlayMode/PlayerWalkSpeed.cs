// Hollowmere P1.7a PlayMode - PlayerWalkSpeed: the real boot (Boot.unity / GameBoot with P1.5's UI and audio), W held.
//
// Coordinator follow-up to P2.1's graphical evidence ("W held 2.5 s moved the player 0.25 m"). Boots Boot.unity,
// waits for the village, then:
//   A. main menu (the boot screen, where P2.1's evidence pressed W): W through the Input System (a queued keyboard
//      state, as the evidence did) -> P1.5's PauseGatedIntentSource drops the movement; the player stays put;
//   B. HUD (after "newgame"): W through the Input System for 60 frames (diagnostic: what the real source samples);
//   C. HUD: W through a scripted source behind the real pause gate for 150 fixed steps (input frame time pinned to the
//      world step), the CharacterController resolver on -> distance >= 90 % of walk speed x time;
//   D. the same with the kinematic resolver (the kernel's own acceptance, no collision) -> within 10 %;
//   E. a 5.5 fps frame time (182 ms, P2.1's mean frame) -> the per-move clamp (speed x moveWindow) holds.
// Logs [P1.7a-walk] lines with distances, frames and the pause state.
#nullable enable
using System.Collections;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Player;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Player;
using GameCore.Rules.Gameplay.Ui;
using Hollowmere.Boot;
using Hollowmere.UiAudio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.P1_7a.PlayMode.Tests
{
    public sealed class PlayerWalkSpeed
    {
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const string VillageId = "11e8dd95-6622-43d0-8b48-5e17b72f0bb8";
        private const int MaxFrames = 1200;
        private const int FixedSteps = 150;

        private Keyboard? addedKeyboard;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (addedKeyboard != null)
            {
                InputSystem.RemoveDevice(addedKeyboard);
                addedKeyboard = null;
            }

            GameBoot? boot = Object.FindAnyObjectByType<GameBoot>();
            if (boot != null)
            {
                Object.Destroy(boot.gameObject);
                yield return null;
            }
        }

        [UnityTest]
        [Timeout(600000)]
        public IEnumerator HoldingW_OnTheHud_WalksAtTheAuthoredSpeed()
        {
#if UNITY_EDITOR
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
            frames = 0;
            while (!(world.Streamer.IsSettled && world.Streamer.ResidencyOf(VillageId) == RegionResidency.Resident) && frames++ < MaxFrames)
            {
                yield return null;
            }

            HollowmereUiAudio? rig = UiAudioBootstrap.RigOf(boot.gameObject);
            Assert.That(rig, Is.Not.Null, "the UI rig");
            UiRuntime ui = rig!.Ui;
            PlayerSession player = boot.Player!;
            TargetId target = boot.PlayerExtension!.Player;
            PlayerTuning tuning = boot.PlayerExtension.Module!.Tuning;
            float walk = tuning.WalkMillimetresPerSecond / 1000f;
            float window = tuning.MoveWindowMilliseconds / 1000f;

            // A. The main menu, W through the Input System (P2.1's evidence setup).
            Assert.That(ui.Screen, Is.EqualTo(UiScreen.Menu), "the boot screen is the main menu");
            Assert.That(ui.GameplayPaused, Is.True);
            Assert.That(player.Input.Source, Is.InstanceOf<UiAudioBootstrap.PauseGatedIntentSource>(), "P1.5 gates the player's intents on the pause query");
            Keyboard keyboard = Keyboard.current ?? (addedKeyboard = InputSystem.AddDevice<Keyboard>());
            Vector2 start = Planar(world, target);
            int movesBefore = player.Input.MovesSubmitted;
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            Vector2 innerMove = Vector2.zero;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                innerMove = Vector2.Max(innerMove, ((UiAudioBootstrap.PauseGatedIntentSource)player.Input.Source).Inner.Sample().Move);
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            float menuDistance = Vector2.Distance(start, Planar(world, target));
            Log("A menu: W via Input System 60 frames -> " + Metres(menuDistance) + " (inner source move " + innerMove + ", gated move "
                + player.Input.LastIntent.Move + ", moves submitted " + (player.Input.MovesSubmitted - movesBefore) + ", paused " + ui.GameplayPaused + ")");
            Assert.That(player.Input.LastIntent.Move, Is.EqualTo(Vector2.zero), "the menu drops movement (P1.5 design)");
            Assert.That(menuDistance, Is.LessThan(0.05f), "no walking while the main menu is open");

            // HUD.
            Assert.That(ui.Dispatcher.Dispatch("newgame").Accepted, Is.True);
            frames = 0;
            while (ui.Screen != UiScreen.Hud && frames++ < MaxFrames)
            {
                yield return null;
            }

            Assert.That(ui.Screen, Is.EqualTo(UiScreen.Hud));
            Assert.That(ui.GameplayPaused, Is.False);
            player = boot.Player!;
            world = boot.World!;
            target = boot.PlayerExtension!.Player;

            // B. HUD, W through the Input System (diagnostic: batchmode has no focused game view).
            start = Planar(world, target);
            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
            innerMove = Vector2.zero;
            float elapsed = 0f;
            for (int i = 0; i < 60; i++)
            {
                yield return null;
                elapsed += Mathf.Min(Time.deltaTime, window);
                innerMove = Vector2.Max(innerMove, player.Input.LastIntent.Move);
            }

            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            yield return null;
            float inputSystemDistance = Vector2.Distance(start, Planar(world, target));
            Log("B hud: W via Input System 60 frames -> " + Metres(inputSystemDistance) + " (expected " + Metres(walk * elapsed) + " if the keyboard reaches the action; sampled move "
                + innerMove + ", focused " + Application.isFocused + ")");

            // C. HUD, scripted W behind the real pause gate, fixed steps, CharacterController resolver.
            IPlayerIntentSource gate = player.Input.Source;
            System.Func<float> frameTime = player.Input.DeltaTime;
            System.Func<float?>? cameraYaw = player.Input.CameraYaw;
            IPlayerMotionResolver? resolver = player.Input.Resolver;
            float step = tuning.StepMilliseconds / 1000f;
            player.Input.Source = new UiAudioBootstrap.PauseGatedIntentSource(new HeldW(), ui);
            player.Input.DeltaTime = () => step;
            player.Input.CameraYaw = null;
            int resolutions = player.Locomotion.Resolutions;
            int ungrounded = player.Locomotion.UngroundedResolutions;
            start = Planar(world, target);
            for (int i = 0; i < FixedSteps; i++)
            {
                yield return null;
            }

            yield return null;
            float resolved = Vector2.Distance(start, Planar(world, target));
            float expected = walk * step * FixedSteps;
            Log("C hud: scripted W " + FixedSteps + " steps of " + step.ToString("0.000", CultureInfo.InvariantCulture) + " s, CharacterController -> " + Metres(resolved)
                + " of " + Metres(expected) + " (resolutions " + (player.Locomotion.Resolutions - resolutions) + ", ungrounded " + (player.Locomotion.UngroundedResolutions - ungrounded)
                + ", refused " + boot.PlayerExtension.Module!.Refused + ")");

            // D. The same with the kinematic resolver: the kernel's acceptance alone.
            player.Input.Resolver = null;
            start = Planar(world, target);
            for (int i = 0; i < FixedSteps; i++)
            {
                yield return null;
            }

            yield return null;
            float kinematic = Vector2.Distance(start, Planar(world, target));
            Log("D hud: scripted W " + FixedSteps + " steps, kinematic -> " + Metres(kinematic) + " of " + Metres(expected));

            // E. P2.1's mean frame (182 ms): each move is clamped to speed x moveWindow.
            const int SlowFrames = 14;
            const float SlowFrame = 0.182f;
            player.Input.DeltaTime = () => SlowFrame;
            start = Planar(world, target);
            for (int i = 0; i < SlowFrames; i++)
            {
                yield return null;
            }

            yield return null;
            float slow = Vector2.Distance(start, Planar(world, target));
            float clamped = walk * Mathf.Min(SlowFrame, window) * SlowFrames;
            Log("E hud: scripted W " + SlowFrames + " frames of 182 ms, kinematic -> " + Metres(slow) + " (clamped contract " + Metres(clamped)
                + ", wall-clock speed would be " + Metres(walk * SlowFrame * SlowFrames) + ")");

            player.Input.Source = gate;
            player.Input.DeltaTime = frameTime;
            player.Input.CameraYaw = cameraYaw;
            player.Input.Resolver = resolver;

            Assert.That(kinematic, Is.EqualTo(expected).Within(expected * 0.1f), "the kernel accepts walk speed x time on the HUD");
            Assert.That(resolved, Is.GreaterThanOrEqualTo(expected * 0.9f), "the CharacterController resolver walks at the authored speed on the HUD");
            Assert.That(slow, Is.EqualTo(clamped).Within(clamped * 0.1f), "a slow frame moves speed x moveWindow");
#else
            Assert.Ignore("PlayerWalkSpeed loads scenes by path and runs in the Editor only");
            yield break;
#endif
        }

        private sealed class HeldW : IPlayerIntentSource
        {
            public PlayerIntent Sample() => new PlayerIntent { Move = new Vector2(0f, 1f) };
        }

        private static Vector2 Planar(GameplayWorld world, TargetId target) =>
            new Vector2(
                world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0) / 1000f,
                world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0) / 1000f);

        private static string Metres(float metres) => metres.ToString("0.000", CultureInfo.InvariantCulture) + " m";

        private static void Log(string line) => Debug.Log("[P1.7a-walk] " + line);
    }
}
