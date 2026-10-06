#nullable enable
using System.IO;
using System.Reflection;
using Hollowmere.Game;
using Hollowmere.Boot;
using GameCore.Gameplay.World;
using NUnit.Framework;
using UnityEngine;

namespace Hollowmere.P3_1.EditMode.Tests
{
    public sealed class P31dFrameTests
    {
        [Test]
        public void P31d_PACING_DefaultIsUncappedAndExplicitVsyncIsHonored()
        {
            int vsync = QualitySettings.vSyncCount;
            int target = Application.targetFrameRate;
            try
            {
                GameBoot.ConfigureFramePacing(System.Array.Empty<string>());
                Assert.That(QualitySettings.vSyncCount, Is.Zero);
                Assert.That(Application.targetFrameRate, Is.EqualTo(-1));
                GameBoot.ConfigureFramePacing(new[] { "-frameVsync", "1" });
                Assert.That(QualitySettings.vSyncCount, Is.EqualTo(1));
                GameBoot.ConfigureFramePacing(new[] { "-frameVsync", "0" });
                Assert.That(QualitySettings.vSyncCount, Is.Zero);
            }
            finally { QualitySettings.vSyncCount = vsync; Application.targetFrameRate = target; }
        }

        [Test]
        public void P31d_BOOT_RegionWaitsForPresentedMenu()
        {
            int frame = 10;
            bool presented = false;
            var scenes = new ImmediateSceneLoader();
            var loader = new DeferredRegionLoader(scenes, () => frame, 3, () => presented);
            ISceneOperation load = loader.Load("village");
            Assert.That(load.IsDone, Is.False, "elapsed frames alone cannot authorize loading");
            Assert.That(scenes.Loads, Is.Zero);
            presented = true;
            Assert.That(load.IsDone, Is.True);
            Assert.That(scenes.Loads, Is.EqualTo(1));
            Assert.That(loader.IsLoaded("village"), Is.True);
        }

        [Test]
        public void P31d_BOOT_AttachDoesNotClaimAnUnreadyMenu()
        {
            string path = Path.Combine(Path.GetTempPath(), "hollowmere-p31d-" + System.Guid.NewGuid().ToString("N") + ".csv");
            var host = new GameObject("unready game");
            var sessionHost = new GameObject("session");
            try
            {
                HollowmereGame game = host.AddComponent<HollowmereGame>();
                HollowmerePersistentSession session = sessionHost.AddComponent<HollowmerePersistentSession>();
                typeof(HollowmerePersistentSession).GetMethod("Begin", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(session, new object[] { HollowmereCommandLine.Parse(new[] { "-frameLog", path }) });
                session.Attach(game);
                Assert.That(session.Ready, Is.False);
                typeof(FrameLogRecorder).GetMethod("LateUpdate", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(session.FrameLog!, null);
                session.FrameLog.Flush();
                Assert.That(File.ReadAllText(path), Does.Not.Contain(",ready"), "attachment is not proof of a visible, responsive menu");
            }
            finally
            {
                Object.DestroyImmediate(sessionHost);
                Object.DestroyImmediate(host);
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
