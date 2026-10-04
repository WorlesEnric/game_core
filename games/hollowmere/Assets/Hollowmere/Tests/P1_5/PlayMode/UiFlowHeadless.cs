// Hollowmere P1.5 PlayMode - UiFlowHeadless: the UI and audio rig on the real PlayerLoop pump, headless-safe.
//
// Boots the baked Hollowmere world through GameplayBoot with the PlayerLoop node installed (one sanctioned pump per
// frame) and the P1.5 rig's extensions, exactly as UiAudioBootstrap does for GameBoot, with an ImmediateSceneLoader (no
// region scenes). Host actions run in UiHostDriver.LateUpdate. Then:
//   menu -> new game -> HUD; pause -> resume;
//   pause -> save screen -> save.capture into slot-1 -> the slot is listed -> load screen -> save.restore -> HUD on the
//   restored world (the UI and audio extensions re-attach to the restored root);
//   the dialogue view receives a view model (as P1.4's presenter does on LineShown);
//   the ambience zone follows RegionEntered;
//   one sanctioned pump per frame, no violation.
// The save service uses the validation project's generated checkpoint codecs (copied under Tests/P1_5/Checkpoint),
// because Hollowmere does not ship its own codecs yet. Counts and durations are logged as P1.5-UIFLOW lines.
#nullable enable
using System.Collections;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.App;
using GameCore.Validation.ProbeHost;
using Hollowmere.UiAudio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P1_5.PlayMode.Tests
{
    public sealed class UiFlowHeadless
    {
        private const string ManifestPath = "Assets/Hollowmere/World/Hollowmere.manifest.asset";
        private const int MaxFrames = 600;

        private GameplayWorld? world;
        private HollowmereUiAudio? rig;
        private string saveDirectory = string.Empty;

        [TearDown]
        public void TearDown()
        {
            if (rig != null)
            {
                GameplayWorld? current = rig.Ui.World ?? world;
                if (current != null)
                {
                    current.Shutdown();
                    if (current.Root.State != GameApplicationState.Stopped)
                    {
                        current.Root.Stop("UiFlowHeadless teardown");
                    }
                }

                Object.Destroy(rig.gameObject);
                rig = null;
            }

            if (saveDirectory.Length > 0 && Directory.Exists(saveDirectory))
            {
                Directory.Delete(saveDirectory, true);
            }
        }

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator MenuNewGamePauseSaveLoadDialogueAmbience()
        {
#if UNITY_EDITOR
            var total = Stopwatch.StartNew();
            RegionManifest? manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionManifest>(ManifestPath);
            HollowmereUiAudioContent? content = Resources.Load<HollowmereUiAudioContent>(HollowmereUiAudioContent.ResourcePath);
            if (manifest == null || content == null || content.Flow == null)
            {
                Assert.Ignore("the Hollowmere world or the UI/audio content is not authored yet (run the P1.1 and P1.5 EditMode tests first)");
            }

            rig = HollowmereUiAudio.Create(content!, null, UiScreen.Menu);
            var build = new WorldBuildOptions { Name = "Hollowmere" };
            rig.AddTo(build);
            world = GameplayBoot.Boot(manifest!, new GameApplicationBootOptions { StartImmediately = false }, build, false);
            world.UseSceneLoader(new ImmediateSceneLoader());
            Assert.That(world.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
            UiRuntime ui = rig.Ui;
            Assert.That(rig.Root == null, Is.EqualTo(Application.isBatchMode && SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null),
                "the UI root exists exactly when a graphics device exists");

            Assert.That(Gc018CheckpointCodecs.TryBuild(out _, out CheckpointCodecSet? codecs, out string codecDetail), Is.True, codecDetail);
            saveDirectory = Path.Combine(Path.GetTempPath(), "gamecore-p1_5-saves-" + System.Guid.NewGuid().ToString("N"));
            var service = new SaveService(world.Root, new SaveServiceOptions("hollowmere.p1_5", codecs!)
            {
                Directory = saveDirectory,
                RegionId = () => rig != null && rig.Ui.World != null ? StartRegion(rig.Ui.World) : string.Empty,
            });
            rig.UseSaves(service, restored => restored.UseSceneLoader(new ImmediateSceneLoader()));

            int frames = 0;
            yield return Until(() => ui.Presents > 0 && ui.Screen == UiScreen.Menu, "menu", f => frames = f);
            Log("boot", total.ElapsedMilliseconds, frames);

            // menu -> new game -> HUD; pause / resume.
            var leg = Stopwatch.StartNew();
            Assert.That(ui.Dispatcher.Dispatch("newgame").Accepted, Is.True);
            yield return Until(() => ui.Screen == UiScreen.Hud, "new game -> HUD", f => frames = f);
            Log("menu -> hud", leg.ElapsedMilliseconds, frames);
            ui.Raise(UiIntent.Pause);
            yield return Until(() => ui.Screen == UiScreen.Pause && ui.GameplayPaused, "pause", f => frames = f);
            Assert.That(ui.Dispatcher.Dispatch("resume").Accepted, Is.True);
            yield return Until(() => ui.Screen == UiScreen.Hud && !ui.GameplayPaused, "resume", f => frames = f);

            // Pump discipline over the plain flow (the restore replaces the root, so it is measured before).
            GameApplicationRoot firstRoot = world.Root;
            int startFrame = Time.frameCount;
            int startPumps = firstRoot.PumpCounter.SanctionedPumps;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            int pumpFrames = Time.frameCount - startFrame;
            int pumps = firstRoot.PumpCounter.SanctionedPumps - startPumps;
            Assert.That(pumps, Is.EqualTo(pumpFrames).Within(1), "one sanctioned pump per frame");
            Assert.That(firstRoot.PumpCounter.Violations, Is.EqualTo(0), firstRoot.PumpCounter.LastViolation);

            // Dialogue view model, as P1.4's presenter delivers it on LineShown.
            IDialogueView? dialogue = world.Presentation.Get<IDialogueView>();
            Assert.That(dialogue, Is.Not.Null);
            dialogue!.Show(new DialogueViewModel
            {
                SpeakerId = "ferryman",
                SpeakerName = "The Ferryman",
                Text = "The marsh keeps what it takes.",
                Choices = new[] { "Then I will take it back.", "Goodbye." },
                ChoiceDisabled = new[] { 0, 0 },
            });
            Assert.That(ui.Models.Dialogue.Received, Is.EqualTo(1));
            Assert.That(ui.Models.Dialogue.Text, Is.EqualTo("The marsh keeps what it takes."));
            ui.Navigate(0, -1);
            Assert.That(ui.Models.Dialogue.Selected, Is.EqualTo(1));
            dialogue.Hide();

            // Ambience follows RegionEntered.
            leg = Stopwatch.StartNew();
            RegionRecord marsh = Region(world, "Blackmere Marsh");
            Assert.That(world.Commands.Travel(world.Focus, marsh.AuthoringId).Admitted, Is.True);
            yield return Until(() => rig.Audio.AmbienceZone == marsh.Key, "ambience -> marsh", f => frames = f);
            Assert.That(rig.Audio.Ambience.CurrentZone, Is.EqualTo(marsh.Key));
            Log("travel -> marsh ambience", leg.ElapsedMilliseconds, frames);

            // Save: pause -> save screen -> slot 1.
            leg = Stopwatch.StartNew();
            ui.Raise(UiIntent.Pause);
            yield return Until(() => ui.Screen == UiScreen.Pause, "pause before save", f => frames = f);
            Assert.That(ui.Dispatcher.Dispatch("open.save").Accepted, Is.True);
            yield return Until(() => ui.Screen == UiScreen.Save, "save screen", f => frames = f);
            Assert.That(ui.Models.SaveLoad.Available, Is.True);
            Assert.That(ui.Dispatcher.Dispatch("select.0").Accepted, Is.True);
            Assert.That(ui.Dispatcher.Dispatch("slot.selected").Accepted, Is.True);
            yield return Until(() => ui.Models.SaveLoad.Status.Length > 0, "save outcome", f => frames = f);
            Assert.That(ui.Models.SaveLoad.RefusalCode, Is.Empty, ui.Models.SaveLoad.Status);
            Assert.That(ui.SlotCatalog!.TryGet("slot-1", out SaveSlotHeader? header) && header != null, Is.True, "slot-1 is listed");
            Assert.That(ui.Models.SaveLoad.Rows[0].Exists, Is.True);
            Log("save slot-1", leg.ElapsedMilliseconds, frames);

            // Load: save screen -> load screen -> slot 1 -> HUD on the restored world.
            leg = Stopwatch.StartNew();
            GameplayWorld before = world;
            int zoneBefore = rig.Audio.AmbienceZone;
            Assert.That(ui.Dispatcher.Dispatch("open.load").Accepted, Is.True);
            yield return Until(() => ui.Screen == UiScreen.Load, "load screen", f => frames = f);
            Assert.That(ui.Dispatcher.Dispatch("slot.selected").Accepted, Is.True);
            yield return Until(() => ui.World != before && ui.Screen == UiScreen.Hud, "restore -> HUD", f => frames = f);
            world = ui.World;
            Assert.That(ui.Attaches, Is.EqualTo(2), "the UI re-attached to the restored root");
            Assert.That(rig.Audio.Attaches, Is.EqualTo(2), "the audio re-attached to the restored root");
            Assert.That(rig.Audio.World, Is.SameAs(world));
            Assert.That(rig.Audio.AmbienceZone, Is.EqualTo(zoneBefore), "the ambience zone is restored from the save");
            Assert.That(world!.Presentation.Get<IUiIntentSink>(), Is.SameAs(ui));
            Assert.That(service.ActiveRoot, Is.SameAs(world.Root));
            Log("load slot-1 -> hud", leg.ElapsedMilliseconds, frames);

            GameApplicationRoot restoredRoot = world.Root;
            startFrame = Time.frameCount;
            startPumps = restoredRoot.PumpCounter.SanctionedPumps;
            for (int i = 0; i < 30; i++)
            {
                yield return null;
            }

            pumpFrames = Time.frameCount - startFrame;
            pumps = restoredRoot.PumpCounter.SanctionedPumps - startPumps;
            Assert.That(pumps, Is.EqualTo(pumpFrames).Within(1), "one sanctioned pump per frame on the restored root");
            Assert.That(restoredRoot.PumpCounter.Violations, Is.EqualTo(0), restoredRoot.PumpCounter.LastViolation);

            UnityEngine.Debug.Log("[P1.5-UIFLOW] total=" + total.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " ms screenChanges="
                + ui.ScreenChanges + " hostActions=" + ui.HostActionsRun + " presents=" + ui.Presents + " audioPresents=" + rig.Audio.Presents
                + " zoneRequests=" + rig.Audio.ZoneRequests + " pumps=" + pumps + "/" + pumpFrames + " headless=" + (rig.Root == null));
#else
            Assert.Ignore("UiFlowHeadless loads the baked manifest through the AssetDatabase and runs in the Editor only");
            yield break;
#endif
        }

        private static IEnumerator Until(System.Func<bool> condition, string what, System.Action<int> frames)
        {
            int n = 0;
            while (!condition() && n < MaxFrames)
            {
                n++;
                yield return null;
            }

            frames(n);
            Assert.That(condition(), Is.True, "not reached within " + MaxFrames + " frames: " + what);
        }

        private static string StartRegion(GameplayWorld world) => world.Manifest.StartRegionId;

        private static RegionRecord Region(GameplayWorld world, string name)
        {
            for (int i = 0; i < world.Worlds.Regions.Count; i++)
            {
                if (world.Worlds.Regions[i].Name == name)
                {
                    return world.Worlds.Regions[i];
                }
            }

            Assert.Fail("no region " + name);
            return null!;
        }

        private static void Log(string leg, long milliseconds, int frames)
        {
            UnityEngine.Debug.Log("[P1.5-UIFLOW] " + leg + ": " + milliseconds.ToString(CultureInfo.InvariantCulture) + " ms, "
                + frames.ToString(CultureInfo.InvariantCulture) + " frames");
        }
    }
}
