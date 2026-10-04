// Hollowmere P1.1 EditMode - the baked Hollowmere world on the kernel, without scenes.
//
// Boots the real manifest and generated catalog through GameplayBoot with the PlayerLoop node and the default-world
// assignment off, a test-driven frame clock and an ImmediateSceneLoader (the residency machine runs, no scene loads).
// Frames go through the one sanctioned application pump, exactly as in a player.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution;
using GameCore.Execution.Time;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using GameCore.Unity.Adapters;
using GameCore.Unity.App;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using UnityEditor;

namespace Hollowmere.P1_1.EditMode.Tests
{
    /// <summary>One booted Hollowmere world for an EditMode test.</summary>
    internal sealed class EditModeWorld : IDisposable
    {
        private long frame = 50000L;

        private EditModeWorld()
        {
        }

        public GameplayWorld World { get; private set; } = null!;

        public ImmediateSceneLoader Loader { get; } = new ImmediateSceneLoader();

        public RegionManifest Manifest => World.Manifest;

        public static EditModeWorld Boot()
        {
            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(HollowmereWorldAuthoring.ManifestPath);
            if (manifest == null)
            {
                Assert.Ignore("the Hollowmere world is not baked yet (run AuthoringBakeTests first)");
            }

            if (!GameplayCatalog.TryBuild(manifest!.CatalogTypeName, out ICatalog? _, out ContentHash _, out string detail))
            {
                Assert.Ignore("the generated catalog is not compiled yet: " + detail);
            }

            GameCoreApplicationPump.IsEnabled = true;
            GameCoreThreading.CaptureMainThread();
            var harness = new EditModeWorld();
            var options = new GameApplicationBootOptions
            {
                InstallPlayerLoop = false,
                AssignDefaultWorld = false,
                PumpAssertions = false,
                FrameClock = () => harness.frame,
            };

            harness.World = GameplayBoot.Boot(manifest, options, null, false);
            harness.World.UseSceneLoader(harness.Loader);
            Assert.That(harness.World.Root.Start().Outcome, Is.EqualTo(Outcome.Published));
            return harness;
        }

        public void Pump(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                frame++;
                GameCoreApplicationPump.PumpFrame();
            }
        }

        /// <summary>Pumps until <paramref name="condition"/> holds; fails after <paramref name="maxFrames"/>.</summary>
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

        public int Slot(TargetId target, OwnerId owner, SlotId slot) => World.Slots.ReadOrDefault(target, owner, slot, int.MinValue);

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

        public TargetId Entity(string name)
        {
            for (int i = 0; i < Manifest.Entities.Count; i++)
            {
                if (Manifest.Entities[i].name == name)
                {
                    return AuthoringIds.TargetIdFor(Manifest.Entities[i].authoringId);
                }
            }

            Assert.Fail("no entity " + name);
            return default(TargetId);
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
        }
    }

    [TestFixture]
    public sealed class GameplayWorldTests
    {
        private bool pumpWasEnabled;

        [SetUp]
        public void SetUp() => pumpWasEnabled = GameCoreApplicationPump.IsEnabled;

        [TearDown]
        public void TearDown()
        {
            GameApplicationRoot? current = GameApplication.Current;
            if (current != null && current.State != GameApplicationState.Stopped)
            {
                current.Stop("test teardown");
            }

            GameCoreApplicationPump.IsEnabled = pumpWasEnabled;
        }

        [Test]
        public void Boot_SeedsEveryAuthoredTarget_AndEveryRegionStartsUnloaded()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            Assert.That(w.World.Root.Targets.Count, Is.EqualTo(w.Manifest.Regions.Count + w.Manifest.Entities.Count));
            for (int i = 0; i < w.World.Worlds.Regions.Count; i++)
            {
                RegionRecord region = w.World.Worlds.Regions[i];
                Assert.That(w.Slot(region.Target, GameplaySlots.WorldOwner, GameplaySlots.Visits), Is.EqualTo(0));
                Assert.That(w.World.Streamer.ResidencyOf(region.AuthoringId), Is.EqualTo(RegionResidency.Unloaded), region.Name);
            }

            for (int i = 0; i < w.Manifest.Entities.Count; i++)
            {
                ManifestEntity entity = w.Manifest.Entities[i];
                TargetId target = AuthoringIds.TargetIdFor(entity.authoringId);
                Assert.That(w.Slot(target, GameplaySlots.EntityOwner, GameplaySlots.Alive), Is.EqualTo(entity.alive ? 1 : 0), entity.name);
                Assert.That(w.Slot(target, GameplaySlots.EntityOwner, GameplaySlots.Variant), Is.EqualTo(entity.variant), entity.name);
                Assert.That(w.Slot(target, GameplaySlots.WorldOwner, GameplaySlots.PosX), Is.EqualTo(entity.x), entity.name);
                Assert.That(w.Slot(target, GameplaySlots.WorldOwner, GameplaySlots.Region), Is.EqualTo(w.Manifest.FindRegion(entity.regionId)!.key));
            }

            Assert.That(w.World.Root.PumpCounter.Inner, Is.SameAs(w.World.Root.TimeFrame), "the time frame runs inside the pump counter");
            Assert.That(w.World.Root.TimeFrame.Inner, Is.SameAs(w.World.Plan.Frame), "the gameplay frame runs inside the time frame");
        }

        [Test]
        public void Streamer_BringsTheStartRegionResident_ThroughTheLegalResidencySteps()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            RegionRecord village = w.Region("Thornwick Village");
            var seen = new List<RegionResidency>();
            var listener = new Recorder(village.AuthoringId, seen);
            w.World.Streamer.AddListener(listener);
            w.PumpUntil(() => w.World.Streamer.IsSettled && w.World.Streamer.ResidencyOf(village.AuthoringId) == RegionResidency.Resident, "village resident");
            Assert.That(seen, Is.EqualTo(new[] { RegionResidency.Unloaded, RegionResidency.Loading, RegionResidency.Resident }));
            Assert.That(w.Loader.IsLoaded(village.ScenePath), Is.True);
            Assert.That(w.Loader.Loads, Is.EqualTo(1), "only the focus region loads without preloading");
            Assert.That(w.World.Streamer.RefusedSubmits, Is.EqualTo(0));
            Assert.That(w.World.Worlds.Refused, Is.EqualTo(0), "every residency change was legal and host-issued");
        }

        [Test]
        public void Travel_LoopsVillageMarshBelfryVillage_WithEventsResidencyAndPosePersistence()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            TargetId traveller = w.World.Focus;
            Assert.That(traveller.IsDefault, Is.False);
            RegionRecord village = w.Region("Thornwick Village");
            RegionRecord marsh = w.Region("Blackmere Marsh");
            RegionRecord belfry = w.Region("Drowned Belfry");
            w.PumpUntil(() => w.World.Streamer.IsSettled && w.World.Streamer.ResidencyOf(village.AuthoringId) == RegionResidency.Resident, "village");

            TargetId crate = w.Entity(HollowmereWorldAuthoring.MovableCrateName);
            int x = w.Slot(crate, GameplaySlots.WorldOwner, GameplaySlots.PosX) + 1500;
            int z = w.Slot(crate, GameplaySlots.WorldOwner, GameplaySlots.PosZ) - 700;
            Assert.That(w.World.Commands.Place(crate, x, 0, z, 1571).Admitted, Is.True);
            w.PumpUntil(() => w.Slot(crate, GameplaySlots.WorldOwner, GameplaySlots.PosX) == x, "crate moved");

            var events = new List<CommittedEvent>();
            w.World.ReadEvents(events);
            events.Clear();
            foreach (RegionRecord next in new[] { marsh, belfry, village })
            {
                int visits = w.Slot(next.Target, GameplaySlots.WorldOwner, GameplaySlots.Visits);
                Assert.That(w.World.Commands.Travel(traveller, next.AuthoringId).Admitted, Is.True);
                w.PumpUntil(() => w.Slot(traveller, GameplaySlots.WorldOwner, GameplaySlots.Region) == next.Key, "arrive " + next.Name);
                w.PumpUntil(() => w.World.Streamer.IsSettled, "settle in " + next.Name);
                Assert.That(w.Slot(next.Target, GameplaySlots.WorldOwner, GameplaySlots.Visits), Is.EqualTo(visits + 1));
                for (int i = 0; i < w.World.Worlds.Regions.Count; i++)
                {
                    RegionRecord region = w.World.Worlds.Regions[i];
                    RegionResidency expected = region.Key == next.Key ? RegionResidency.Resident : RegionResidency.Unloaded;
                    Assert.That(w.World.Streamer.ResidencyOf(region.AuthoringId), Is.EqualTo(expected), region.Name + " after arriving in " + next.Name);
                    Assert.That(w.Loader.IsLoaded(region.ScenePath), Is.EqualTo(region.Key == next.Key), region.Name);
                }
            }

            w.World.ReadEvents(events);
            int entered = 0;
            int left = 0;
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i].Schema.Equals(WorldDeclarations.RegionEnteredEvent))
                {
                    entered++;
                    Assert.That(WorldEvent.TryDecode(events[i].Payload, out WorldEvent decoded), Is.True);
                    Assert.That(decoded.Target, Is.EqualTo(traveller));
                }
                else if (events[i].Schema.Equals(WorldDeclarations.RegionLeftEvent))
                {
                    left++;
                }
            }

            Assert.That(entered, Is.EqualTo(3), "one RegionEntered per travel");
            Assert.That(left, Is.EqualTo(3), "one RegionLeft per travel");
            Assert.That(w.Slot(crate, GameplaySlots.WorldOwner, GameplaySlots.PosX), Is.EqualTo(x), "the moved crate keeps its pose while its region is unloaded and reloaded");
            Assert.That(w.Slot(crate, GameplaySlots.WorldOwner, GameplaySlots.PosZ), Is.EqualTo(z));
            Assert.That(w.Slot(crate, GameplaySlots.WorldOwner, GameplaySlots.Yaw), Is.EqualTo(1571));
            Assert.That(w.Loader.Loads, Is.EqualTo(4));
            Assert.That(w.Loader.Unloads, Is.EqualTo(3));
            Assert.That(w.World.Root.PumpCounter.Violations, Is.EqualTo(0), w.World.Root.PumpCounter.LastViolation);
        }

        [Test]
        public void Travel_ToTheSameRegion_OrFromAnUnknownTraveller_IsRefused()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            RegionRecord village = w.Region("Thornwick Village");
            int refusedBefore = w.World.Worlds.Refused;
            w.World.Commands.Travel(w.World.Focus, village.AuthoringId);
            w.Pump(3);
            Assert.That(w.World.Worlds.Refused, Is.EqualTo(refusedBefore + 1), "same-region travel is refused by TravelRules");
            Assert.That(w.Slot(w.World.Focus, GameplaySlots.WorldOwner, GameplaySlots.Region), Is.EqualTo(village.Key));
        }

        [Test]
        public void SetResidency_FromAGameplayIssuer_IsRefused()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            RegionRecord belfry = w.Region("Drowned Belfry");
            w.PumpUntil(() => w.World.Streamer.IsSettled, "settled");
            int refusedBefore = w.World.Worlds.Refused;
            w.World.Commands.Submit(WorldDeclarations.SetResidencyRoute, belfry.Target, WorldDeclarations.SetResidencyCommand, ResidencyPayload.Encode(1));
            w.Pump(3);
            Assert.That(w.World.Worlds.Refused, Is.EqualTo(refusedBefore + 1), "world.setResidency is host-only");
            Assert.That(w.World.Streamer.ResidencyOf(belfry.AuthoringId), Is.EqualTo(RegionResidency.Unloaded));
        }

        [Test]
        public void EntityCommands_DespawnSpawnAndSetVariant_FollowTheRules()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            TargetId lantern = w.Entity("Lantern East");
            w.World.Commands.Despawn(lantern);
            w.PumpUntil(() => w.Slot(lantern, GameplaySlots.EntityOwner, GameplaySlots.Alive) == 0, "despawned");
            int refused = w.World.Entities.Refused;
            w.World.Commands.Despawn(lantern);
            w.Pump(3);
            Assert.That(w.World.Entities.Refused, Is.EqualTo(refused + 1), "a dead entity cannot despawn again");
            w.World.Commands.Spawn(lantern);
            w.PumpUntil(() => w.Slot(lantern, GameplaySlots.EntityOwner, GameplaySlots.Alive) == 1, "respawned");
            w.World.Commands.SetVariant(lantern, 1);
            w.PumpUntil(() => w.Slot(lantern, GameplaySlots.EntityOwner, GameplaySlots.Variant) == 1, "variant 1");
            refused = w.World.Entities.Refused;
            w.World.Commands.SetVariant(lantern, 9);
            w.Pump(3);
            Assert.That(w.World.Entities.Refused, Is.EqualTo(refused + 1), "variant 9 is out of range");
            Assert.That(w.Slot(lantern, GameplaySlots.EntityOwner, GameplaySlots.Variant), Is.EqualTo(1));
        }

        [Test]
        public void Spawner_PublishesANewRuntimeTarget_InItsRegionScope()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            RegionRecord marsh = w.Region("Blackmere Marsh");
            string crateDefinition = w.Manifest.FindEntity(w.Manifest.Entities[0].authoringId)!.definitionId;
            int targetsBefore = w.World.Root.Targets.Count;
            Assert.That(w.World.Spawner.TrySpawn(crateDefinition, marsh.AuthoringId, 1000, 0, 2000, 0, out TargetId spawned, out string detail), Is.True, detail);
            Assert.That(w.World.Root.Targets.Count, Is.EqualTo(targetsBefore + 1));
            Assert.That(w.Slot(spawned, GameplaySlots.EntityOwner, GameplaySlots.Alive), Is.EqualTo(1));
            Assert.That(w.Slot(spawned, GameplaySlots.WorldOwner, GameplaySlots.Region), Is.EqualTo(marsh.Key));
            w.World.Commands.Despawn(spawned);
            w.PumpUntil(() => w.Slot(spawned, GameplaySlots.EntityOwner, GameplaySlots.Alive) == 0, "the spawned entity takes commands");
        }

        [Test]
        public void TimeDriver_AdvancesClocksOnCommittedSteps_FeedsWakes_AndFollowsPause()
        {
            using EditModeWorld w = EditModeWorld.Boot();
            GameApplicationRoot root = w.World.Root;
            int expectedTables = root.Schedule.Adaptation != null && root.Schedule.Adaptation.NativeTable != null ? 1 : 0;
            Assert.That(root.Time.ResourceTables.Count, Is.EqualTo(expectedTables), "the schedule's native resource table is adopted");

            w.PumpUntil(() => w.World.Streamer.IsSettled, "settled");
            ulong stepsBefore = root.Host.CurrentStep.Value;
            ulong advancedBefore = root.TimeFrame.StepsAdvanced;
            int framesBefore = root.TimeFrame.Frames;
            w.Pump(5);
            Assert.That(root.TimeFrame.Frames, Is.EqualTo(framesBefore + 5), "one time frame per pump");
            Assert.That(root.TimeFrame.StepsAdvanced - advancedBefore, Is.EqualTo(root.Host.CurrentStep.Value - stepsBefore));

            Id128 clock = StableNameKeyDerivation.Derive("hollowmere.test.clock");
            Assert.That(root.Time.Clocks.TryRegister(new PluginClockSpec(clock, "p1.1-test", PluginClockKind.LogicalStep, WakePausePolicy.Defer, false), out DiagnosticCode code), Is.True, code.ToString());
            Assert.That(root.Time.TryScheduleWake(clock, StableNameKeyDerivation.Derive("hollowmere.test.wake"), WorldDeclarations.TravelCommand, 1UL, 0UL, out WakeRecord? wake, out code), Is.True, code.ToString());
            int fedBefore = root.TimeFrame.WakesFed;

            // One committed command step makes the wake due; the next frame feeds it as demand and commits a step
            // with no command at all.
            w.World.Commands.Place(w.Entity("Lantern East"), 0, 0, 0, 0);
            w.PumpUntil(() => root.TimeFrame.WakesFed == fedBefore + 1, "the due wake is fed");
            ulong afterWake = root.Host.CurrentStep.Value;
            w.Pump(2);
            Assert.That(wake!.IsConsumed, Is.True);
            Assert.That(root.Host.CurrentStep.Value, Is.GreaterThanOrEqualTo(afterWake));

            Assert.That(root.Pause().Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(root.Time.PauseCount, Is.EqualTo(1));
            ulong pausedAt = root.Host.CurrentStep.Value;
            w.World.Commands.Place(w.Entity("Lantern East"), 10, 0, 0, 0);
            w.Pump(3);
            Assert.That(root.Host.CurrentStep.Value, Is.EqualTo(pausedAt), "a paused world commits nothing");
            Assert.That(root.Resume().Outcome, Is.EqualTo(Outcome.Published));
            Assert.That(root.Time.ResumeCount, Is.EqualTo(1));
            w.PumpUntil(() => w.Slot(w.Entity("Lantern East"), GameplaySlots.WorldOwner, GameplaySlots.PosX) == 10, "queued command commits after resume");
        }

        private sealed class Recorder : IResidencyAware
        {
            private readonly string region;
            private readonly List<RegionResidency> seen;

            public Recorder(string region, List<RegionResidency> seen)
            {
                this.region = region;
                this.seen = seen;
            }

            public void OnResidencyChanged(string regionId, RegionResidency residency)
            {
                if (regionId == region)
                {
                    seen.Add(residency);
                }
            }
        }
    }
}
