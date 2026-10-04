#nullable enable
// Hollowmere P2.4 PlayMode - W-MECH-01: the pressure plate mechanism composed into the baked Hollowmere world.
//
// Builds the real Hollowmere world from its baked manifest and generated catalog, extends the application definition
// with the pressure plate mechanism (composite catalog, plate plugin, route, reader, recipe, mount), boots it through
// GameApplication.Boot with the PlayerLoop pump, places a plate in Thornwick Village and has the focus traveller step on
// and off it. Asserts the committed plate slots, the PlatePressed / PlateReleased events, the one-pump rule and the
// catalog-set fingerprint. Compiled only when the com.hollowmere.mechanism.pressureplate package is installed
// (versionDefines -> HOLLOWMERE_PRESSUREPLATE), so the game builds without it.
using System.Collections;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Gameplay.World;
using GameCore.Unity.App;
using Hollowmere.Mechanism.PressurePlate;
using Hollowmere.Mechanism.PressurePlate.Generated;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.P2_4.PlayMode.Tests
{
    public sealed class PressurePlateInHollowmere
    {
        private const string ManifestPath = "Assets/Hollowmere/World/Hollowmere.manifest.asset";
        private const int MaxFrames = 120;

        private GameApplicationRoot? root;

        [TearDown]
        public void TearDown()
        {
            if (root != null && root.State != GameApplicationState.Stopped)
            {
                root.Stop("W-MECH-01 teardown");
            }

            root = null;
        }

        [UnityTest]
        [Timeout(120000)]
        public IEnumerator PlacedInTheVillageAndPressed()
        {
#if UNITY_EDITOR
            RegionManifest? manifest = UnityEditor.AssetDatabase.LoadAssetAtPath<RegionManifest>(ManifestPath);
            Assert.That(manifest, Is.Not.Null, "the Hollowmere world is baked (" + ManifestPath + ")");
            Assert.That(
                GameplayCatalog.TryBuild(manifest!.CatalogTypeName, out ICatalog? catalog, out ContentHash hollowmereFingerprint, out string detail),
                Is.True,
                detail);

            WorldBuildPlan plan = WorldBuilder.Build(manifest, catalog!, hollowmereFingerprint, null);
            GameApplicationDefinition extended = PressurePlateMechanism.Extend(plan.Definition);
            root = GameApplication.Boot(extended, new GameApplicationBootOptions());
            GameplayWorld world = WorldBuilder.Attach(root, plan);
            world.UseSceneLoader(new ImmediateSceneLoader());
            PressurePlateWorld plates = PressurePlateMechanism.Attach(root);
            Assert.That(root.Start().Outcome, Is.Not.EqualTo(Outcome.Rejected), "the extended world starts");

            string expectedSet = CatalogSet.Combine(hollowmereFingerprint.ToHex(), new[] { PressurePlateCatalog.CatalogFingerprint });
            Assert.That(root.CatalogHash.ToHex(), Is.EqualTo(expectedSet), "the root runs the catalog set world + plate");

            RegionRecord village = Region(world, "Thornwick Village");
            TargetId traveller = world.Focus;
            Assert.That(traveller.IsDefault, Is.False, "the world names a focus traveller");
            var plate = new TargetId(StableNameKeyDerivation.Derive("hollowmere.pressureplate.w-mech-01.village-plate"));
            Assert.That(plates.Place(plate, village.Scope, 1, 2), Is.True, plates.LastDetail);

            int startFrame = Time.frameCount;
            int startPumps = root.PumpCounter.SanctionedPumps;

            Assert.That(plates.Press(plate, traveller, true).Admitted, Is.True, "plate.press (on) is admitted");
            int frames = 0;
            while (!plates.IsPressed(plate) && frames++ < MaxFrames)
            {
                yield return null;
            }

            plates.Poll();
            Assert.That(plates.Pressed(plate), Is.EqualTo(1), "plate.pressed");
            Assert.That(plates.Weight(plate), Is.EqualTo(1), "plate.weight");
            Assert.That(plates.CountEvents(PlateEventKind.Pressed, plate), Is.EqualTo(1), "one PlatePressed");
            Assert.That(plates.CountEvents(PlateEventKind.Released, plate), Is.EqualTo(0));
            int pressFrames = frames;

            Assert.That(plates.Press(plate, traveller, false).Admitted, Is.True, "plate.press (off) is admitted");
            frames = 0;
            while (plates.IsPressed(plate) && frames++ < MaxFrames)
            {
                yield return null;
            }

            plates.Poll();
            Assert.That(plates.Pressed(plate), Is.EqualTo(0));
            Assert.That(plates.Weight(plate), Is.EqualTo(0));
            Assert.That(plates.CountEvents(PlateEventKind.Released, plate), Is.EqualTo(1), "one PlateReleased");
            Assert.That(plates.Events[0].Actor, Is.EqualTo(traveller), "the event names the traveller");
            Assert.That(plates.Module.Refused, Is.EqualTo(0));
            Assert.That(plates.Module.Malformed, Is.EqualTo(0));

            int elapsedFrames = Time.frameCount - startFrame;
            int pumps = root.PumpCounter.SanctionedPumps - startPumps;
            Debug.Log("[W-MECH-01] plate placed in " + village.Name + "; pressed after "
                + pressFrames.ToString(CultureInfo.InvariantCulture) + " frame(s), released after "
                + frames.ToString(CultureInfo.InvariantCulture) + " frame(s); frames="
                + elapsedFrames.ToString(CultureInfo.InvariantCulture) + " sanctionedPumps="
                + pumps.ToString(CultureInfo.InvariantCulture) + " violations="
                + root.PumpCounter.Violations.ToString(CultureInfo.InvariantCulture) + " catalogSet=" + root.CatalogHash.ToHex()
                + " world=" + hollowmereFingerprint.ToHex() + " plate=" + PressurePlateCatalog.CatalogFingerprint);
            Assert.That(pumps, Is.EqualTo(elapsedFrames).Within(1), "one sanctioned pump per frame");
            Assert.That(root.PumpCounter.Violations, Is.EqualTo(0), root.PumpCounter.LastViolation);

            world.Shutdown();
            root.Stop("W-MECH-01 done");
            Assert.That(root.State, Is.EqualTo(GameApplicationState.Stopped));
#else
            Assert.Ignore("W-MECH-01 loads the baked manifest through the AssetDatabase and runs in the Editor only");
            yield break;
#endif
        }

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
    }
}
