// Hollowmere P1.1 EditMode - authoring identity and tool round trips, in temporary assets and scenes.
#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Entities.Editor;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using Hollowmere.WorldAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.P1_1.EditMode.Tests
{
    [TestFixture]
    public sealed class AuthoringToolTests
    {
        private const string Temp = "Assets/Hollowmere/Tests/P1_1/Temp";

        [SetUp]
        public void SetUp()
        {
            WorldTools.EnsureFolder(Temp);
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.path.StartsWith(Temp) && SceneManager.sceneCount > 1)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }

            AssetDatabase.DeleteAsset(Temp);
        }

        [Test]
        public void AuthoredEntity_IdIsStableOnPrefabInstantiation_AndThePrefabCarriesNone()
        {
            EntityDefinition definition = TempDefinition("Probe");
            GameObject source = new GameObject("ProbeView");
            // Adding the component to a scene object mints an id (Reset); a prefab is saved without one (ClearAuthoringId).
            source.AddComponent<AuthoredEntity>().ClearAuthoringId();
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(source, Temp + "/ProbeWithEntity.prefab");
            Object.DestroyImmediate(source);
            Assert.That(prefab.GetComponent<AuthoredEntity>().AuthoringId, Is.Empty, "a prefab asset never carries an authoring id");

            HollowmereWorldAuthoring.PrepareScenes();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, Temp + "/Identity.unity");
            var a = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            var b = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            AuthoredEntity ea = a.GetComponent<AuthoredEntity>();
            AuthoredEntity eb = b.GetComponent<AuthoredEntity>();
            ea.EnsureAuthoringId();
            eb.EnsureAuthoringId();
            ea.SetDefinition(definition);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ea);
            PrefabUtility.RecordPrefabInstancePropertyModifications(eb);
            string idA = ea.AuthoringId;
            string idB = eb.AuthoringId;
            Assert.That(AuthoringIds.IsValid(idA) && AuthoringIds.IsValid(idB), Is.True);
            Assert.That(idA, Is.Not.EqualTo(idB), "two instances of one prefab are two entities");
            Assert.That(ea.EnsureAuthoringId(), Is.False, "a minted id is never re-minted");
            Assert.That(ea.TargetId, Is.EqualTo(AuthoringIds.TargetIdFor(idA)));

            EditorSceneManager.SaveScene(scene);
            EditorSceneManager.CloseScene(scene, true);
            Scene reopened = EditorSceneManager.OpenScene(Temp + "/Identity.unity", OpenSceneMode.Additive);
            var ids = new HashSet<string>();
            foreach (GameObject root in reopened.GetRootGameObjects())
            {
                foreach (AuthoredEntity entity in root.GetComponentsInChildren<AuthoredEntity>(true))
                {
                    ids.Add(entity.AuthoringId);
                }
            }

            Assert.That(ids, Is.EquivalentTo(new[] { idA, idB }), "ids survive save and reload of the scene");
            Assert.That(reopened.isDirty, Is.False, "reopening mints nothing");
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(Temp + "/ProbeWithEntity.prefab").GetComponent<AuthoredEntity>().AuthoringId,
                Is.Empty, "instantiation never writes an id back into the prefab asset");
            EditorSceneManager.CloseScene(reopened, true);
        }

        [Test]
        public void EntityTools_RoundTrip()
        {
            EntityDefinition crate = TempDefinition("Crate");
            EntityDefinition stone = TempDefinition("Stone");
            VariantDefinition dark = ScriptableObject.CreateInstance<VariantDefinition>();
            dark.EnsureAuthoringId();
            AssetDatabase.CreateAsset(dark, Temp + "/Dark.asset");
            crate.SetVariants(new[] { dark });

            Scene scene = TempRegionScene("Tools", out AuthoredRegion region);
            AuthoredEntity placed = EntityTools.PlaceIn(scene, crate, new Vector3(1f, 0f, 2f), 90f, "Placed");
            Assert.That(AuthoringIds.IsValid(placed.AuthoringId), Is.True);
            Assert.That(placed.Region, Is.SameAs(region), "the entity detects its scene's region");
            Assert.That(EntityValidator.Validate(placed), Is.Empty);

            string id = placed.AuthoringId;
            EntityTools.SetVariant(placed, 1);
            Assert.That(placed.Variant, Is.EqualTo(1));
            EntityTools.SetVariant(placed, 0);
            Assert.That(placed.Variant, Is.EqualTo(0), "setVariant round trip");
            Assert.Throws<System.ArgumentException>(() => EntityTools.SetVariant(placed, 2));

            EntityTools.ApplyOverride(placed, OverrideSet.ScaleMilli, "1500");
            Assert.That(placed.Overrides.TryGet(OverrideSet.ScaleMilli, out string scale) && scale == "1500", Is.True);
            EntityTools.ApplyOverride(placed, OverrideSet.ScaleMilli, string.Empty);
            Assert.That(placed.Overrides.Count, Is.EqualTo(0), "an empty value clears the override");
            Assert.Throws<System.ArgumentException>(() => EntityTools.ApplyOverride(placed, "speed", "3"));

            AuthoredEntity copy = EntityTools.Duplicate(placed, new Vector3(2f, 0f, 0f));
            Assert.That(copy.AuthoringId, Is.Not.EqualTo(id), "a duplicate is a new entity");
            Assert.That(copy.Definition, Is.SameAs(crate));

            EntityTools.SetVariant(placed, 1);
            AuthoredEntity replaced = EntityTools.ReplaceDefinition(placed, stone);
            Assert.That(replaced.AuthoringId, Is.EqualTo(id), "replaceDefinition keeps the identity");
            Assert.That(replaced.Definition, Is.SameAs(stone));
            Assert.That(replaced.Variant, Is.EqualTo(0), "the variant is clamped to the new definition");
            AuthoredEntity back = EntityTools.ReplaceDefinition(replaced, crate);
            Assert.That(back.AuthoringId, Is.EqualTo(id), "replaceDefinition round trip keeps the identity");

            IReadOnlyList<AuthoredEntity> ring = EntityTools.LayoutRingIn(scene, stone, Vector3.zero, 5f, 6, 0f);
            IReadOnlyList<AuthoredEntity> line = EntityTools.LayoutLineIn(scene, stone, new Vector3(-5f, 0f, -5f), new Vector3(5f, 0f, -5f), 4);
            Assert.That(ring.Count, Is.EqualTo(6));
            Assert.That(line.Count, Is.EqualTo(4));
            Assert.That(Vector3.Distance(ring[0].transform.position, Vector3.zero), Is.EqualTo(5f).Within(0.001f));
            Assert.That(EntityValidator.ValidateScene(scene), Is.Empty, "no duplicate ids after the tools ran");
        }

        [Test]
        public void WorldTools_RoundTrip()
        {
            WorldDefinition world = ScriptableObject.CreateInstance<WorldDefinition>();
            world.EnsureAuthoringId();
            RegionDefinition a = TempRegion("A");
            RegionDefinition b = TempRegion("B");
            world.AddRegion(a);
            world.AddRegion(b);
            world.SetStartRegion(a);
            AssetDatabase.CreateAsset(world, Temp + "/World.asset");

            PortalDefinition portal = WorldTools.ConnectRegions(world, a, b, string.Empty);
            Assert.That(world.Portals, Does.Contain(portal));
            Assert.That(portal.Other(a), Is.SameAs(b));
            Assert.That(AssetDatabase.GetAssetPath(portal), Does.StartWith(Temp + "/Portals/"));
            Assert.Throws<System.ArgumentException>(() => WorldTools.ConnectRegions(world, a, a, string.Empty));

            Scene scene = TempRegionScene("A", out AuthoredRegion region, a);
            RegionPortal end = WorldTools.AddPortal(region, portal, new Vector3(20f, 0f, 0f), -90f, 2.5f);
            Assert.That(end.Portal, Is.SameAs(portal));
            Assert.That(region.Bounds.Contains(end.ArrivalPosition), Is.True, "the arrival pose points into the region");
            Assert.That(end.GetComponent<BoxCollider>().isTrigger, Is.True);

            Transform spawn = WorldTools.SetSpawnPoint(region, new Vector3(0f, 0f, -5f), 0f);
            Transform again = WorldTools.SetSpawnPoint(region, new Vector3(1f, 0f, -6f), 45f);
            Assert.That(again, Is.SameAs(spawn), "setSpawnPoint moves the one spawn point");
            Assert.That(region.SpawnPoint, Is.SameAs(spawn));
            Assert.Throws<System.ArgumentException>(() => WorldTools.SetSpawnPoint(region, new Vector3(500f, 0f, 0f), 0f));

            Assert.That(WorldValidator.Validate(world), Is.Empty);
            Assert.That(WorldValidator.ValidateScene(scene), Is.Empty);
        }

        [Test]
        public void HollowmereScenes_PassTheValidators()
        {
            WorldDefinition world = AuthoringBakeTests.RequireWorld();
            Assert.That(WorldValidator.Validate(world), Is.Empty);
            for (int i = 0; i < world.Regions.Count; i++)
            {
                Scene scene = EditorSceneManager.OpenScene(world.Regions[i].ScenePath, OpenSceneMode.Additive);
                try
                {
                    Assert.That(WorldValidator.ValidateScene(scene), Is.Empty, scene.path);
                    Assert.That(EntityValidator.ValidateScene(scene), Is.Empty, scene.path);
                    Assert.That(scene.isDirty, Is.False, "opening a region scene changes nothing");
                }
                finally
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static EntityDefinition TempDefinition(string name)
        {
            GameObject view = GameObject.CreatePrimitive(PrimitiveType.Cube);
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(view, Temp + "/" + name + ".prefab");
            Object.DestroyImmediate(view);
            EntityDefinition definition = ScriptableObject.CreateInstance<EntityDefinition>();
            definition.EnsureAuthoringId();
            definition.Configure(prefab, 1000, true, true);
            AssetDatabase.CreateAsset(definition, Temp + "/" + name + ".asset");
            return definition;
        }

        private static RegionDefinition TempRegion(string name)
        {
            RegionDefinition region = ScriptableObject.CreateInstance<RegionDefinition>();
            region.EnsureAuthoringId();
            region.Configure(name, Temp + "/" + name + ".unity");
            AssetDatabase.CreateAsset(region, Temp + "/Region" + name + ".asset");
            return region;
        }

        private static Scene TempRegionScene(string name, out AuthoredRegion region, RegionDefinition? definition = null)
        {
            HollowmereWorldAuthoring.PrepareScenes();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var marker = new GameObject("Region " + name);
            SceneManager.MoveGameObjectToScene(marker, scene);
            region = marker.AddComponent<AuthoredRegion>();
            region.Bounds.Configure(new Vector3(0f, 5f, 0f), new Vector3(60f, 20f, 60f));
            region.Configure(definition ?? TempRegion(name + "Def"), null);
            EditorSceneManager.SaveScene(scene, Temp + "/" + name + ".unity");
            return scene;
        }
    }
}
