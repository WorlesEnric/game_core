// Hollowmere P1.5 EditMode - one booted Hollowmere world with the UI and audio rig, without scenes.
//
// Same boot as the P1.1 EditMode harness (GameplayBoot with the PlayerLoop node off, a test-driven frame clock, an
// ImmediateSceneLoader) plus the P1.5 rig: the UI and audio extensions are added to the world build options exactly
// as UiAudioBootstrap does for GameBoot. Host actions run between frames through RunHostActions, as UiHostDriver
// does in a player.
#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Ui;
using GameCore.Gameplay.World;
using GameCore.Rules.Gameplay.Ui;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using Hollowmere.UiAudio;
using Hollowmere.UiAudioAuthoring;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hollowmere.P1_5.EditMode.Tests
{
    internal sealed class UiAudioHarness : IDisposable
    {
        private long frame = 70000L;

        private UiAudioHarness()
        {
        }

        public GameplayWorld World { get; private set; } = null!;

        public HollowmereUiAudio Rig { get; private set; } = null!;

        public UiRuntime Ui => Rig.Ui;

        public AudioRuntime Audio => Rig.Audio;

        public ImmediateSceneLoader Loader { get; } = new ImmediateSceneLoader();

        public static HollowmereUiAudioContent Content()
        {
            HollowmereUiAudioContent? content = AssetDatabase.LoadAssetAtPath<HollowmereUiAudioContent>(HollowmereUiAudioAuthoring.ContentPath);
            if (content == null || content.Flow == null || content.Audio == null)
            {
                UiAudioAuthoringReport report = HollowmereUiAudioAuthoring.Author();
                Assert.That(report.Ok, Is.True, report.ToString());
                content = AssetDatabase.LoadAssetAtPath<HollowmereUiAudioContent>(HollowmereUiAudioAuthoring.ContentPath);
            }

            Assert.That(content, Is.Not.Null);
            return content!;
        }

        public static UiAudioHarness Boot(UiScreen startScreen)
        {
            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(HollowmereWorldAuthoring.ManifestPath);
            if (manifest == null)
            {
                Assert.Ignore("the Hollowmere world is not baked yet (run the P1.1 AuthoringBakeTests first)");
            }

            if (!GameplayCatalog.TryBuild(manifest!.CatalogTypeName, out ICatalog? _, out ContentHash _, out string detail))
            {
                Assert.Ignore("the generated catalog is not compiled yet: " + detail);
            }

            HollowmereUiAudioContent content = Content();
            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
            var harness = new UiAudioHarness();
            harness.Rig = HollowmereUiAudio.Create(content, null, startScreen);
            var build = new WorldBuildOptions { Name = "Hollowmere" };
            harness.Rig.AddTo(build);
            var options = new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                FrameClock = () => harness.frame,
            };

            harness.World = GameplayBoot.Boot(manifest, options, build, false);
            harness.World.UseSceneLoader(harness.Loader);
            Assert.That(harness.World.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
            harness.Pump(1);
            return harness;
        }

        public void Pump(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                frame++;
                GameCoreApplicationPump.PumpFrame();
                Ui.RunHostActions();
            }
        }

        public int PumpUntil(Func<bool> condition, string what, int maxFrames = 120)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return i;
                }

                Pump(1);
            }

            Assert.Fail("not reached within " + maxFrames + " frames: " + what);
            return maxFrames;
        }

        /// <summary>Dispatches a UI command and pumps until the screen is <paramref name="expected"/>.</summary>
        public void Expect(string command, UiScreen expected)
        {
            UiDispatchResult result = Ui.Dispatcher.Dispatch(command);
            Assert.That(result.Accepted, Is.True, command + ": " + result);
            PumpUntil(() => Ui.Screen == expected, command + " -> " + expected);
        }

        public RegionRecord Region(string name)
        {
            for (int i = 0; i < World.Worlds.Regions.Count; i++)
            {
                if (World.Worlds.Regions[i].Name == name)
                {
                    return World.Worlds.Regions[i];
                }
            }

            Assert.Fail("no region " + name);
            return null!;
        }

        public void Dispose()
        {
            if (World != null)
            {
                World.Shutdown();
                if (World.Root.State != GameApplicationState.Stopped)
                {
                    World.Root.Stop("test teardown");
                }
            }

            if (Rig != null)
            {
                UnityEngine.Object.DestroyImmediate(Rig.gameObject);
            }
        }
    }
}
