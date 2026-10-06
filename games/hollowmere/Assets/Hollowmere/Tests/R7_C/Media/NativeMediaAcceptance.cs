#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameCore.Gameplay.Audio;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using Hollowmere.Boot;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Unity.Profiling.Memory;
using Object = UnityEngine.Object;

namespace Hollowmere.R7_C
{
    public sealed class NativeMediaAcceptance
    {
        [Test]
        public void W_PLUG_11_CompletedFadeReleasesOutgoingSourceClip()
        {
            var root = new GameObject("R7 crossfade");
            AudioClip outgoing = AudioClip.Create("outgoing", 44100, 1, 44100, false);
            AudioClip incoming = AudioClip.Create("incoming", 44100, 1, 44100, false);
            try
            {
                AudioSource a = root.AddComponent<AudioSource>();
                AudioSource b = root.AddComponent<AudioSource>();
                var fade = new LoopCrossfader("acceptance");
                fade.AttachSources(a, b);
                fade.FadeTo(1, outgoing, 1, null, 100, 0);
                fade.Tick(100);
                fade.FadeTo(2, incoming, 1, null, 100, 100);
                fade.Tick(150);
                Assert.That(a.clip, Is.SameAs(outgoing), "outgoing clip remains available throughout overlap");
                Assert.That(a.volume, Is.GreaterThan(0));
                Assert.That(b.volume, Is.GreaterThan(0));
                fade.Tick(200);
                Assert.That(a.clip, Is.Null, "completed fade must not pin the outgoing native clip");
                Assert.That(b.clip, Is.SameAs(incoming));
                Assert.That(b.volume, Is.EqualTo(1).Within(0.001));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(outgoing);
                Object.DestroyImmediate(incoming);
            }
        }

        [UnityTest, Timeout(600000)]
        public IEnumerator W_PLUG_01_TravelReleasesRegionNativeMediaWithinFivePercentPeak()
        {
            yield return new UnityEngine.TestTools.EnterPlayMode();
            string output = Environment.GetEnvironmentVariable("GAMECORE_R7_MEDIA_OUTPUT")
                ?? Path.GetFullPath("../../artifacts/studio/verification/W-PLUG-01/native-media");
            Directory.CreateDirectory(output);
            var load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                "Assets/Hollowmere/Boot/Boot.unity", new LoadSceneParameters(LoadSceneMode.Single));
            while (!load.isDone) yield return null;
            GameBoot boot = Object.FindAnyObjectByType<GameBoot>();
            Assert.That(boot, Is.Not.Null);
            for (int i = 0; i < 1200 && (boot.World == null || !boot.World.Streamer.IsSettled); i++) yield return null;
            Assert.That(boot.Failure, Is.Empty);
            GameplayWorld world = boot.World!;
            RegionRecord[] regions = world.Worlds.Regions.OrderBy(r => r.Name == "Thornwick Village" ? 0 : r.Name == "Blackmere Marsh" ? 1 : 2).ToArray();
            Assert.That(regions.Length, Is.EqualTo(3));
            var failures = new List<string>();
            var lines = new List<string> { "region,peakNativeMediaBytes,retainedNativeMediaBytes,retainedPercent,travelMs" };
            try
            {
                for (int leg = 0; leg < regions.Length; leg++)
                {
                    RegionRecord from = regions[leg];
                    RegionRecord to = regions[(leg + 1) % regions.Length];
                    var exclusive = new HashSet<string>(AssetDatabase.GetDependencies(from.ScenePath, true), StringComparer.Ordinal);
                    foreach (RegionRecord other in regions)
                        if (other != from) exclusive.ExceptWith(AssetDatabase.GetDependencies(other.ScenePath, true));
                    var media = Resources.FindObjectsOfTypeAll<Texture>().Cast<Object>()
                        .Concat(Resources.FindObjectsOfTypeAll<AudioClip>()).Where(o => exclusive.Contains(AssetDatabase.GetAssetPath(o)))
                        .ToDictionary(o => o.GetInstanceID(), o => Profiler.GetRuntimeMemorySizeLong(o));
                    int textureCount = Resources.FindObjectsOfTypeAll<Texture>().Count(o => exclusive.Contains(AssetDatabase.GetAssetPath(o)));
                    int audioCount = Resources.FindObjectsOfTypeAll<AudioClip>().Count(o => exclusive.Contains(AssetDatabase.GetAssetPath(o)));
                    if (textureCount == 0 || audioCount == 0)
                        failures.Add(from.Name + ": missing region-exclusive population: textures=" + textureCount + ", audio=" + audioCount);
                    long peak = media.Values.Sum();
                    yield return Snapshot(Path.Combine(output, "leg-" + leg + "-peak.snap"));
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Assert.That(world.Commands.Travel(world.Focus, to.AuthoringId).Admitted, Is.True);
                    for (int i = 0; i < 1200 && (world.Slots.ReadOrDefault(world.Focus, GameplaySlots.WorldOwner, GameplaySlots.Region, 0) != to.Key || !world.Streamer.IsSettled); i++) yield return null;
                    long travelMs = clock.ElapsedMilliseconds;
                    Assert.That(world.Streamer.ResidencyOf(from.AuthoringId), Is.EqualTo(RegionResidency.Unloaded));
                    Assert.That(world.Streamer.ResidencyOf(to.AuthoringId), Is.EqualTo(RegionResidency.Resident));
                    // Allow production fades to finish; do not force unload on the product's behalf.
                    yield return new WaitForSecondsRealtime(4);
                    yield return Snapshot(Path.Combine(output, "leg-" + leg + "-released.snap"));
                    long retained = Resources.FindObjectsOfTypeAll<Texture>().Cast<Object>().Concat(Resources.FindObjectsOfTypeAll<AudioClip>())
                        .Where(o => media.ContainsKey(o.GetInstanceID())).Sum(o => Profiler.GetRuntimeMemorySizeLong(o));
                    double percent = peak == 0 ? 0 : 100.0 * retained / peak;
                    lines.Add(from.Name + "," + peak + "," + retained + "," + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + travelMs);
                    if (peak == 0) failures.Add(from.Name + ": no loaded region-exclusive texture/audio population; cannot establish native release");
                    else if (percent > 5) failures.Add(from.Name + ": retained " + percent + "% of native media peak (limit 5%)");
                }
                File.WriteAllLines(Path.Combine(output, "native-media.csv"), lines);
                Assert.That(failures, Is.Empty, string.Join("; ", failures));
            }
            finally { Object.Destroy(boot.gameObject); }
            yield return new UnityEngine.TestTools.ExitPlayMode();
        }

        private static IEnumerator Snapshot(string path)
        {
            bool done = false;
            bool success = false;
            MemoryProfiler.TakeSnapshot(path, (_, ok) => { success = ok; done = true; },
                CaptureFlags.ManagedObjects | CaptureFlags.NativeObjects | CaptureFlags.NativeAllocations);
            double until = Time.realtimeSinceStartupAsDouble + 120;
            while (!done && Time.realtimeSinceStartupAsDouble < until) yield return null;
            Assert.That(success, Is.True, "native Memory Profiler capture " + path);
        }
    }
}
