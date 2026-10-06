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
                var fade = new LoopCrossfader("acceptance", false);
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

        [Test]
        public void W_PLUG_11_SharedClipLeasePreventsUnloadingAndUnsharedClipReleasesData()
        {
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Hollowmere/Audio/Generated/ambience_village.wav");
            Assert.That(clip.LoadAudioData(), Is.True);
            var root = new GameObject("R7 shared audio lease");
            try
            {
                AudioSource a = root.AddComponent<AudioSource>();
                AudioSource b = root.AddComponent<AudioSource>();
                AudioSource shared = root.AddComponent<AudioSource>();
                shared.clip = clip;
                var fade = new LoopCrossfader("regional", true);
                fade.AttachSources(a, b);
                fade.FadeTo(1, clip, 1, null, 0, 0);
                fade.Tick(0);
                fade.FadeTo(0, null, 1, null, 0, 1);
                fade.Tick(1);
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded), "another source retains the native data lease, even while paused");
                shared.clip = null;
                fade.FadeTo(1, clip, 1, null, 0, 2);
                fade.Tick(2);
                fade.FadeTo(0, null, 1, null, 0, 3);
                fade.Tick(3);
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Unloaded), "unshared region clip native sample data is released");
                fade.FadeTo(1, clip, 1, null, 0, 4);
                fade.Tick(4);
                Assert.That(clip.loadState, Is.EqualTo(AudioDataLoadState.Loaded));
            }
            finally { Object.DestroyImmediate(root); }
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
            AudioRuntime audio = Object.FindAnyObjectByType<AudioEngineHost>().Runtime!;
            Assert.That(audio.Ambience.Loops.HasSources, Is.True);
            ManifestEntity npc = world.Manifest.Entities.Single(entity => entity.name == "Odd");
            var npcTarget = AuthoringIds.TargetIdFor(npc.authoringId);
            int npcX = world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0) + 1500;
            int npcY = world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosY, 0);
            int npcZ = world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosZ, 0);
            Assert.That(world.Commands.Place(npcTarget, npcX, npcY, npcZ, 0).Admitted, Is.True);
            for (int i = 0; i < 120 && world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0) != npcX; i++) yield return null;
            Assert.That(world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0), Is.EqualTo(npcX));
            int textureSamples = 0;
            var failures = new List<string>();
            var lines = new List<string> { "region,peakNativeMediaBytes,retainedNativeMediaBytes,retainedPercent,travelMs,loadMs,unloadMs,textures,audio" };
            try
            {
                for (int leg = 0; leg < regions.Length; leg++)
                {
                    RegionRecord from = regions[leg];
                    RegionRecord to = regions[(leg + 1) % regions.Length];
                    var exclusive = new HashSet<string>(AssetDatabase.GetDependencies(from.ScenePath, true), StringComparer.Ordinal);
                    foreach (RegionRecord other in regions)
                        if (other != from) exclusive.ExceptWith(AssetDatabase.GetDependencies(other.ScenePath, true));
                    AmbienceDefinition ambience = audio.Set!.FindAmbience(from.AuthoringId)!;
                    Assert.That(ambience, Is.Not.Null, "production region ambience");
                    Assert.That(audio.Set.Bank!.TryGet(ambience.ClipId, out AudioBankEntry? entry), Is.True);
                    Assert.That(entry!.Clip, Is.Not.Null);
                    exclusive.Add(AssetDatabase.GetAssetPath(entry.Clip));
                    // The bank owns regional clips, so scene dependencies alone cannot identify them.
                    yield return new WaitForSecondsRealtime(4);
                    var media = Resources.FindObjectsOfTypeAll<Texture>().Cast<Object>()
                        .Concat(Resources.FindObjectsOfTypeAll<AudioClip>()).Where(o => exclusive.Contains(AssetDatabase.GetAssetPath(o)))
                        .ToDictionary(o => o.GetInstanceID(), o => Profiler.GetRuntimeMemorySizeLong(o));
                    int textureCount = Resources.FindObjectsOfTypeAll<Texture>().Count(o => exclusive.Contains(AssetDatabase.GetAssetPath(o)));
                    int audioCount = Resources.FindObjectsOfTypeAll<AudioClip>().Count(o => exclusive.Contains(AssetDatabase.GetAssetPath(o)));
                    textureSamples += textureCount;
                    if (audioCount == 0) failures.Add(from.Name + ": no native region ambience clip");
                    long peak = media.Values.Sum();
                    yield return Snapshot(Path.Combine(output, "leg-" + leg + "-peak.snap"));
                    var clock = System.Diagnostics.Stopwatch.StartNew();
                    Assert.That(world.Commands.Travel(world.Focus, to.AuthoringId).Admitted, Is.True);
                    long loadStarted = -1, unloadStarted = -1, loadMs = -1, unloadMs = -1;
                    for (int i = 0; i < 1200; i++)
                    {
                        long elapsed = clock.ElapsedMilliseconds;
                        RegionResidency loading = world.Streamer.ResidencyOf(to.AuthoringId);
                        RegionResidency unloading = world.Streamer.ResidencyOf(from.AuthoringId);
                        if (loading == RegionResidency.Loading && loadStarted < 0) loadStarted = elapsed;
                        if (unloading == RegionResidency.Unloading && unloadStarted < 0) unloadStarted = elapsed;
                        if (loading == RegionResidency.Resident && loadStarted >= 0 && loadMs < 0) loadMs = elapsed - loadStarted;
                        if (unloading == RegionResidency.Unloaded && unloadStarted >= 0 && unloadMs < 0) unloadMs = elapsed - unloadStarted;
                        if (world.Slots.ReadOrDefault(world.Focus, GameplaySlots.WorldOwner, GameplaySlots.Region, 0) == to.Key && world.Streamer.IsSettled) break;
                        yield return null;
                    }
                    long travelMs = clock.ElapsedMilliseconds;
                    Assert.That(world.Streamer.ResidencyOf(from.AuthoringId), Is.EqualTo(RegionResidency.Unloaded));
                    Assert.That(world.Streamer.ResidencyOf(to.AuthoringId), Is.EqualTo(RegionResidency.Resident));
                    Assert.That(world.Views!.ViewCountIn(from.AuthoringId), Is.Zero, "unloaded region has zero presentation views");
                    // Allow production fades to finish; do not force unload on the product's behalf.
                    yield return new WaitForSecondsRealtime(4);
                    yield return Snapshot(Path.Combine(output, "leg-" + leg + "-released.snap"));
                    Assert.That(audio.Ambience.CurrentRegionId, Is.EqualTo(to.AuthoringId));
                    Assert.That(world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0), Is.EqualTo(npcX), "moved NPC pose survives travel");
                    long retained = Resources.FindObjectsOfTypeAll<Texture>().Cast<Object>().Concat(Resources.FindObjectsOfTypeAll<AudioClip>())
                        .Where(o => media.ContainsKey(o.GetInstanceID())).Sum(o => Profiler.GetRuntimeMemorySizeLong(o));
                    double percent = peak == 0 ? 0 : 100.0 * retained / peak;
                    lines.Add(from.Name + "," + peak + "," + retained + "," + percent.ToString(System.Globalization.CultureInfo.InvariantCulture) + "," + travelMs + "," + loadMs + "," + unloadMs + "," + textureCount + "," + audioCount);
                    if (loadMs < 0 || loadMs > 2000 || unloadMs < 0 || unloadMs > 1000)
                        failures.Add(from.Name + ": load=" + loadMs + "ms (limit 2000), unload=" + unloadMs + "ms (limit 1000)");
                    if (Resources.FindObjectsOfTypeAll<AudioSource>().Any(source => source.clip == entry.Clip))
                        failures.Add(from.Name + ": outgoing ambience remains attached to an AudioSource");
                    if (entry.Clip.loadState != AudioDataLoadState.Unloaded)
                        failures.Add(from.Name + ": outgoing native audio data remains " + entry.Clip.loadState);
                    if (peak == 0) failures.Add(from.Name + ": no loaded region-exclusive texture/audio population; cannot establish native release");
                    else if (percent > 5) failures.Add(from.Name + ": retained " + percent + "% of native media peak (limit 5%)");
                }
                File.WriteAllLines(Path.Combine(output, "native-media.csv"), lines);
                Assert.That(textureSamples, Is.GreaterThan(0), "the loop must measure actual regional textures");
                Assert.That(world.Slots.ReadOrDefault(npcTarget, GameplaySlots.WorldOwner, GameplaySlots.PosX, 0), Is.EqualTo(npcX), "moved NPC survives the complete region loop");
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
