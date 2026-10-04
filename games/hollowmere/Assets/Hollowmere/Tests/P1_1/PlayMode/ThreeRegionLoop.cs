// Hollowmere P1.1 PlayMode - ThreeRegionLoop: the real boot scene, real additive region scenes, the PlayerLoop pump.
//
// Loads Boot/Boot.unity (GameBoot boots the baked world through GameApplication.Boot), then travels the focus traveller
// village -> marsh -> belfry -> village with world.travel and asserts, at every arrival: the destination scene is loaded
// and every other region scene is unloaded, the committed residency slots (2 for the destination, 0 elsewhere), the
// visit counters, a moved entity's pose surviving its region's unload and reload, and the one-pump rule (exactly one
// sanctioned pump per frame, no violation). Timings are logged as B-REGION lines.
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Hollowmere.Boot;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Hollowmere.P1_1.PlayMode.Tests
{
    public sealed class ThreeRegionLoop
    {
        private const string BootScene = "Assets/Hollowmere/Boot/Boot.unity";
        private const int MaxFramesPerLeg = 1200;

        [UnityTest]
        [Timeout(300000)]
        public IEnumerator TravelsVillageMarshBelfryVillage()
        {
#if UNITY_EDITOR
            var bootClock = Stopwatch.StartNew();
            AsyncOperation? load = UnityEditor.SceneManagement.EditorSceneManager.LoadSceneAsyncInPlayMode(
                BootScene, new LoadSceneParameters(LoadSceneMode.Single));
            Assert.That(load, Is.Not.Null, "Boot.unity must exist (run the P1.1 authoring test first)");
            while (!load!.isDone)
            {
                yield return null;
            }

            GameBoot? boot = Object.FindAnyObjectByType<GameBoot>();
            Assert.That(boot, Is.Not.Null);
            int waited = 0;
            while (boot!.World == null && boot.Failure.Length == 0 && waited++ < 300)
            {
                yield return null;
            }

            Assert.That(boot.Failure, Is.Empty);
            GameplayWorld world = boot.World!;
            GameApplicationRoot root = world.Root;
            Assert.That(GameApplication.Current, Is.SameAs(root));
            TargetId traveller = world.Focus;
            Assert.That(traveller.IsDefault, Is.False, "the world names a focus traveller");

            RegionRecord village = Region(world, "Thornwick Village");
            RegionRecord marsh = Region(world, "Blackmere Marsh");
            RegionRecord belfry = Region(world, "Drowned Belfry");

            int frames = 0;
            while (!(world.Streamer.IsSettled && world.Streamer.ResidencyOf(village.AuthoringId) == RegionResidency.Resident) && frames++ < MaxFramesPerLeg)
            {
                yield return null;
            }

            Log("boot", bootClock.ElapsedMilliseconds, frames);
            AssertResidentOnly(world, village);

            // Move a village crate; its pose must survive the village unloading and loading again.
            TargetId crate = EntityNamed(world, "Market Crate");
            int x = Slot(world, crate, GameplaySlots.PosX) + 1200;
            Assert.That(world.Commands.Place(crate, x, 0, Slot(world, crate, GameplaySlots.PosZ), 0).Admitted, Is.True);
            frames = 0;
            while (Slot(world, crate, GameplaySlots.PosX) != x && frames++ < 60)
            {
                yield return null;
            }

            Assert.That(Slot(world, crate, GameplaySlots.PosX), Is.EqualTo(x));

            int startFrame = Time.frameCount;
            int startPumps = root.PumpCounter.SanctionedPumps;
            var legs = new[] { marsh, belfry, village };
            var from = village;
            foreach (RegionRecord next in legs)
            {
                int visits = Slot(world, next.Target, GameplaySlots.Visits);
                var clock = Stopwatch.StartNew();
                Assert.That(world.Commands.Travel(traveller, next.AuthoringId).Admitted, Is.True);
                frames = 0;
                while (!(Slot(world, traveller, GameplaySlots.Region) == next.Key && world.Streamer.IsSettled) && frames++ < MaxFramesPerLeg)
                {
                    yield return null;
                }

                Log(from.Name + " -> " + next.Name, clock.ElapsedMilliseconds, frames);
                Assert.That(Slot(world, traveller, GameplaySlots.Region), Is.EqualTo(next.Key), "arrived in " + next.Name);
                Assert.That(Slot(world, next.Target, GameplaySlots.Visits), Is.EqualTo(visits + 1));
                AssertResidentOnly(world, next);
                from = next;
            }

            int elapsedFrames = Time.frameCount - startFrame;
            int pumps = root.PumpCounter.SanctionedPumps - startPumps;
            UnityEngine.Debug.Log("[B-REGION] loop frames=" + elapsedFrames.ToString(CultureInfo.InvariantCulture)
                + " sanctionedPumps=" + pumps.ToString(CultureInfo.InvariantCulture) + " " + root.PumpCounter);
            Assert.That(pumps, Is.EqualTo(elapsedFrames).Within(1), "one sanctioned pump per frame");
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);
            Assert.That(Slot(world, crate, GameplaySlots.PosX), Is.EqualTo(x), "the moved crate kept its pose across unload and reload");
            Assert.That(world.Streamer.LoadFailures, Is.EqualTo(0));
            Assert.That(world.Worlds.Refused, Is.EqualTo(0));

            Object.Destroy(boot.gameObject);
            yield return null;
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Stopped));
#else
            Assert.Ignore("ThreeRegionLoop loads scenes by path and runs in the Editor only");
            yield break;
#endif
        }

        private static void AssertResidentOnly(GameplayWorld world, RegionRecord resident)
        {
            for (int i = 0; i < world.Worlds.Regions.Count; i++)
            {
                RegionRecord region = world.Worlds.Regions[i];
                bool expected = region.Key == resident.Key;
                Assert.That(world.Streamer.ResidencyOf(region.AuthoringId), Is.EqualTo(expected ? RegionResidency.Resident : RegionResidency.Unloaded), region.Name);
                Scene scene = SceneManager.GetSceneByPath(region.ScenePath);
                Assert.That(scene.IsValid() && scene.isLoaded, Is.EqualTo(expected), region.Name + " scene loaded");
            }
        }

        private static int Slot(GameplayWorld world, TargetId target, SlotId slot) =>
            world.Slots.ReadOrDefault(target, GameplaySlots.WorldOwner, slot, int.MinValue);

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

        private static TargetId EntityNamed(GameplayWorld world, string name)
        {
            IReadOnlyList<ManifestEntity> entities = world.Manifest.Entities;
            for (int i = 0; i < entities.Count; i++)
            {
                if (entities[i].name == name)
                {
                    return AuthoringIds.TargetIdFor(entities[i].authoringId);
                }
            }

            Assert.Fail("no entity " + name);
            return default(TargetId);
        }

        private static void Log(string leg, long milliseconds, int frames)
        {
            UnityEngine.Debug.Log("[B-REGION] " + leg + ": " + milliseconds.ToString(CultureInfo.InvariantCulture) + " ms, "
                + frames.ToString(CultureInfo.InvariantCulture) + " frames");
        }
    }
}
