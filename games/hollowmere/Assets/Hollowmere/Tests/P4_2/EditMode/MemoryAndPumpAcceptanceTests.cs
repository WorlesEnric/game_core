#nullable enable
using System;
using System.Collections;
using System.IO;
using GameCore.Studio.UI;
using GameCore.Unity.App;
using Hollowmere.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.Profiling.Memory;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;

namespace Hollowmere.P4_2
{
    public sealed class MemoryAndPumpAcceptanceTests
    {
        private const string State = "P42.MemoryAndPump.";

        [UnityTest]
        [Explicit("P4.2 graphical ten-cycle acceptance with real native/managed snapshots")]
        [Timeout(1200000)]
        public IEnumerator R2_38_W_UI_04_W_GAME_08_TenCyclesWithSnapshotsAndOnePump()
        {
            if (!ViewportRenderer.CanRender) Assert.Ignore("P4.2 ten-cycle viewport acceptance requires a graphics device.");
            string repo = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string output = Environment.GetEnvironmentVariable("GAMECORE_P42_EVIDENCE") ??
                Path.Combine(repo, "artifacts", "studio", "verification", "W-GAME-08", "graphical-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
            string snapshots = Path.Combine(repo, ".evidence", "P4.2-memory-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"));
            Directory.CreateDirectory(output);
            Directory.CreateDirectory(snapshots);
            SessionState.SetString(State + "output", output);
            SessionState.SetString(State + "snapshots", snapshots);
            SessionState.SetString(State + "samples", "[]");
            SessionState.SetInt(State + "cycle", 1);
            EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity", OpenSceneMode.Single);
            EditorWindow.GetWindow<StudioViewportWindow>().Show();
            while (SessionState.GetInt(State + "cycle", 11) <= 10)
            {
                yield return new EnterPlayMode();
                DateTime deadline = DateTime.UtcNow.AddSeconds(90);
                while ((GameApplication.Current == null || UnityEngine.Object.FindAnyObjectByType<HollowmereGame>()?.Saves == null ||
                    UnityEngine.Object.FindAnyObjectByType<HollowmereGame>()?.World?.Streamer.IsSettled != true) && DateTime.UtcNow < deadline)
                    yield return null;
                Assert.That(GameApplication.Current, Is.Not.Null, "real Hollowmere boot");
                Assert.That(UnityEngine.Object.FindAnyObjectByType<HollowmereGame>()?.World?.Streamer.IsSettled, Is.True, "region load settled before sampling");
                var root = GameApplication.Current!;
                var viewport = EditorWindow.GetWindow<StudioViewportWindow>();
                viewport.SetMode(ViewportMode.Play);
                viewport.EnsureGui();
                for (int warm = 0; warm < 10; warm++) yield return null;
                int startFrame = Time.frameCount;
                int startPumps = root.PumpCounter.SanctionedPumps;
                for (int frame = 0; frame < 60; frame++)
                {
                    viewport.RenderNow();
                    yield return null;
                }
                int frames = Time.frameCount - startFrame;
                int pumps = root.PumpCounter.SanctionedPumps - startPumps;
                Assert.That(frames, Is.GreaterThanOrEqualTo(60));
                Assert.That(pumps, Is.EqualTo(frames), "viewport rendering must not add a pump");
                Assert.That(root.PumpCounter.Violations, Is.Zero);
                SessionState.SetInt(State + "frames", frames);
                SessionState.SetInt(State + "pumps", pumps);
                yield return new ExitPlayMode();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                EditorUtility.UnloadUnusedAssetsImmediate();
                yield return null;
                int cycle = SessionState.GetInt(State + "cycle", 1);
                long engine = Profiler.GetTotalAllocatedMemoryLong();
                long managed = GC.GetTotalMemory(false);
                JArray samples = JArray.Parse(SessionState.GetString(State + "samples", "[]"));
                samples.Add(new JObject
                {
                    ["cycle"] = cycle, ["engineAllocated"] = engine, ["managed"] = managed,
                    ["combined"] = engine + managed, ["reserved"] = Profiler.GetTotalReservedMemoryLong(),
                    ["frames"] = SessionState.GetInt(State + "frames", 0), ["pumps"] = SessionState.GetInt(State + "pumps", 0),
                });
                SessionState.SetString(State + "samples", samples.ToString());
                if (cycle == 1 || cycle == 10) yield return Snapshot(cycle);
                SessionState.SetInt(State + "cycle", cycle + 1);
            }
            JArray all = JArray.Parse(SessionState.GetString(State + "samples", "[]"));
            double growth = 100 * (all[9].Value<double>("combined") / all[0].Value<double>("combined") - 1);
            var report = new JObject
            {
                ["cycles"] = all, ["combinedGrowthPercent"] = growth, ["budgetPercent"] = 15,
                ["snapshotDirectory"] = SessionState.GetString(State + "snapshots", string.Empty),
                ["unity"] = Application.unityVersion, ["graphics"] = SystemInfo.graphicsDeviceName,
                ["utc"] = DateTime.UtcNow.ToString("o"),
            };
            File.WriteAllText(Path.Combine(SessionState.GetString(State + "output", string.Empty), "memory-and-pumps.json"), report.ToString());
            EditorWindow.GetWindow<StudioViewportWindow>().Close();
            Assert.That(growth, Is.LessThanOrEqualTo(15.0), "07 B-MEMORY, unchanged 15% ceiling");
        }

        private static IEnumerator Snapshot(int cycle)
        {
            string path = Path.Combine(SessionState.GetString(State + "snapshots", string.Empty), "cycle-" + cycle + ".snap");
            SessionState.SetBool(State + "snapshotDone", false);
            SessionState.SetBool(State + "snapshotOk", false);
            MemoryProfiler.TakeSnapshot(path, (_, ok) =>
            {
                SessionState.SetBool(State + "snapshotOk", ok);
                SessionState.SetBool(State + "snapshotDone", true);
            }, CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects | CaptureFlags.NativeAllocations);
            DateTime deadline = DateTime.UtcNow.AddSeconds(120);
            while (!SessionState.GetBool(State + "snapshotDone", false) && DateTime.UtcNow < deadline) yield return null;
            Assert.That(SessionState.GetBool(State + "snapshotOk", false), Is.True, "Memory Profiler snapshot " + cycle);
            Assert.That(File.Exists(path), Is.True);
        }
    }
}
