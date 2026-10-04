// Hollowmere - authors the three-region P1.1 world through the gameplay authoring tools, then bakes it.
//
// Everything is created with the same tools Studio drives (entity.place / layoutRing / layoutLine / applyOverride /
// setVariant, world.connectRegions / addPortal / setSpawnPoint), so the content doubles as a tool exercise. The script is
// idempotent: assets and scenes that already exist are kept as they are (their GUIDs and authoring ids stay stable),
// only missing pieces are created, and the bake is re-run (byte-identical when nothing changed).
//
//   Assets/Hollowmere/World/Prefabs/*.prefab          primitive view prefabs
//   Assets/Hollowmere/World/Definitions/*.asset       entity definitions and variants
//   Assets/Hollowmere/World/Regions/*.asset           region definitions
//   Assets/Hollowmere/World/Portals/*.asset           portal connections (village-marsh, marsh-belfry, belfry-village)
//   Assets/Hollowmere/World/Hollowmere.asset          the WorldDefinition (start region Thornwick Village, focus = the traveller)
//   Assets/Hollowmere/Regions/*.unity                 Thornwick Village, Blackmere Marsh, Drowned Belfry
//   Assets/Hollowmere/Boot/Boot.unity                 camera, light, GameBoot, debug controls
//   bake outputs                                      World/Catalog/*, World/Generated/*, World/Hollowmere.manifest.asset
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Compile;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.Entities.Editor;
using GameCore.Gameplay.World;
using GameCore.Gameplay.World.Editor;
using Hollowmere.Boot;
using Hollowmere.Boot.Debug;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Hollowmere.WorldAuthoring
{
    /// <summary>Creates (when missing) and bakes the Hollowmere P1.1 world.</summary>
    public static class HollowmereWorldAuthoring
    {
        public const string Root = "Assets/Hollowmere/World";
        public const string WorldPath = Root + "/Hollowmere.asset";
        public const string ManifestPath = Root + "/Hollowmere.manifest.asset";
        public const string BootScenePath = "Assets/Hollowmere/Boot/Boot.unity";
        public const string VillageScene = "Assets/Hollowmere/Regions/ThornwickVillage.unity";
        public const string MarshScene = "Assets/Hollowmere/Regions/BlackmereMarsh.unity";
        public const string BelfryScene = "Assets/Hollowmere/Regions/DrownedBelfry.unity";
        public const string TravellerName = "Traveller";
        public const string MovableCrateName = "Market Crate";

        private const float Half = 40f;

        private sealed class RegionPlan
        {
            public RegionPlan(string name, string scene, Vector3 center)
            {
                Name = name;
                Scene = scene;
                Center = center;
            }

            public string Name { get; }

            public string Scene { get; }

            public Vector3 Center { get; }

            public RegionDefinition? Definition { get; set; }
        }

        private sealed class Definitions
        {
            public EntityDefinition Traveller = null!;
            public EntityDefinition Lantern = null!;
            public EntityDefinition Crate = null!;
            public EntityDefinition Stone = null!;
            public EntityDefinition Signpost = null!;
            public EntityDefinition Bell = null!;
            public EntityDefinition Reed = null!;
        }

        [MenuItem("Hollowmere/Author And Bake World")]
        public static void AuthorAndBakeMenu()
        {
            BakeResult result = AuthorAndBake();
            if (result.Succeeded)
            {
                Debug.Log("[Hollowmere] " + result);
            }
            else
            {
                Debug.LogError("[Hollowmere] " + result);
            }
        }

        /// <summary>Creates whatever is missing, bakes (without importing the generated C#) and returns the bake result.</summary>
        public static BakeResult AuthorAndBake()
        {
            WorldDefinition world = EnsureWorld();
            BakeResult result = Entry.Bake(world, BakePaths.ConventionFor(WorldPath), false);
            if (result.Succeeded)
            {
                EnsureBootScene();
            }

            return result;
        }

        /// <summary>The world asset, creating the whole world when it does not exist yet.</summary>
        public static WorldDefinition EnsureWorld()
        {
            WorldDefinition? existing = AssetDatabase.LoadAssetAtPath<WorldDefinition>(WorldPath);
            if (existing != null)
            {
                return existing;
            }

            WorldTools.EnsureFolder(Root + "/Prefabs");
            WorldTools.EnsureFolder(Root + "/Definitions");
            WorldTools.EnsureFolder(Root + "/Regions");
            WorldTools.EnsureFolder("Assets/Hollowmere/Regions");

            Definitions definitions = CreateDefinitions();
            var village = new RegionPlan("Thornwick Village", VillageScene, new Vector3(0f, 0f, 0f));
            var marsh = new RegionPlan("Blackmere Marsh", MarshScene, new Vector3(200f, 0f, 0f));
            var belfry = new RegionPlan("Drowned Belfry", BelfryScene, new Vector3(100f, 0f, 200f));
            var plans = new[] { village, marsh, belfry };

            WorldDefinition world = ScriptableObject.CreateInstance<WorldDefinition>();
            world.EnsureAuthoringId();
            for (int i = 0; i < plans.Length; i++)
            {
                RegionDefinition region = ScriptableObject.CreateInstance<RegionDefinition>();
                region.EnsureAuthoringId();
                region.Configure(plans[i].Name, plans[i].Scene);
                AssetDatabase.CreateAsset(region, Root + "/Regions/" + plans[i].Name.Replace(" ", string.Empty) + ".asset");
                plans[i].Definition = region;
                world.AddRegion(region);
            }

            world.SetStartRegion(village.Definition);
            world.SetPreloadNeighbours(false);
            AssetDatabase.CreateAsset(world, WorldPath);

            PortalDefinition villageMarsh = WorldTools.ConnectRegions(world, village.Definition!, marsh.Definition!, string.Empty);
            PortalDefinition marshBelfry = WorldTools.ConnectRegions(world, marsh.Definition!, belfry.Definition!, string.Empty);
            PortalDefinition belfryVillage = WorldTools.ConnectRegions(world, belfry.Definition!, village.Definition!, string.Empty);

            string travellerId = BuildVillage(village, definitions, marsh, belfry, villageMarsh, belfryVillage);
            BuildMarsh(marsh, definitions, village, belfry, villageMarsh, marshBelfry);
            BuildBelfry(belfry, definitions, marsh, village, marshBelfry, belfryVillage);

            world.SetFocusEntity(travellerId);
            EditorUtility.SetDirty(world);
            AssetDatabase.SaveAssets();
            return world;
        }

        /// <summary>Creates Boot.unity (camera, light, GameBoot bound to the manifest, debug controls) when missing.</summary>
        public static void EnsureBootScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(BootScenePath) != null)
            {
                return;
            }

            RegionManifest? manifest = AssetDatabase.LoadAssetAtPath<RegionManifest>(ManifestPath);
            if (manifest == null)
            {
                throw new InvalidOperationException("the world must be baked before the boot scene is created");
            }

            Scene scene = NewAuthoringScene();
            var light = new GameObject("Directional Light");
            Light sun = light.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            SceneManager.MoveGameObjectToScene(light, scene);

            var boot = new GameObject("GameBoot");
            GameBoot gameBoot = boot.AddComponent<GameBoot>();
            gameBoot.Configure(manifest, false);
            DebugTravelKeys keys = boot.AddComponent<DebugTravelKeys>();
            keys.Configure(gameBoot);
            SceneManager.MoveGameObjectToScene(boot, scene);

            var cameraObject = new GameObject("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            cameraObject.transform.SetPositionAndRotation(new Vector3(0f, 25f, -45f), Quaternion.Euler(28f, 0f, 0f));
            DebugFlyCamera fly = cameraObject.AddComponent<DebugFlyCamera>();
            fly.Configure(gameBoot);
            SceneManager.MoveGameObjectToScene(cameraObject, scene);

            EditorSceneManager.SaveScene(scene, BootScenePath);
            CloseAuthoringScene(scene);
        }

        /// <summary>
        /// A new empty scene that replaces whatever is open (Single mode). Unity refuses NewScene(Additive) while an
        /// untitled scene is open, and every authored scene is saved before the next one is created, so nothing is lost.
        /// </summary>
        public static Scene NewAuthoringScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        /// <summary>Closes a saved authoring scene unless it is the last loaded scene (which Unity cannot close).</summary>
        public static void CloseAuthoringScene(Scene scene)
        {
            if (SceneManager.sceneCount > 1 && scene.IsValid())
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Definitions CreateDefinitions()
        {
            var made = new Definitions
            {
                Traveller = Definition("Traveller", Prefab("Traveller", PrimitiveType.Capsule, new Vector3(1f, 1f, 1f), new Color(0.9f, 0.85f, 0.7f)), 1000),
                Lantern = Definition("Lantern", Prefab("Lantern", PrimitiveType.Cylinder, new Vector3(0.4f, 1.2f, 0.4f), new Color(0.35f, 0.3f, 0.25f)), 1000),
                Crate = Definition("Crate", Prefab("Crate", PrimitiveType.Cube, new Vector3(1.2f, 1.2f, 1.2f), new Color(0.55f, 0.4f, 0.25f)), 1000),
                Stone = Definition("Stone", Prefab("Stone", PrimitiveType.Sphere, new Vector3(1.4f, 0.9f, 1.2f), new Color(0.45f, 0.45f, 0.5f)), 1000),
                Signpost = Definition("Signpost", Prefab("Signpost", PrimitiveType.Cube, new Vector3(0.2f, 2.2f, 1.4f), new Color(0.5f, 0.35f, 0.2f)), 1000),
                Bell = Definition("Bell", Prefab("Bell", PrimitiveType.Cylinder, new Vector3(1.6f, 1.4f, 1.6f), new Color(0.6f, 0.5f, 0.2f)), 1500),
                Reed = Definition("Reed", Prefab("Reed", PrimitiveType.Capsule, new Vector3(0.25f, 1.6f, 0.25f), new Color(0.3f, 0.5f, 0.25f)), 1000),
            };

            made.Signpost.SetInteractionKind("read");
            made.Lantern.SetVariants(new[] { Variant("LanternLit", new Color(1f, 0.85f, 0.4f)) });
            made.Crate.SetVariants(new[] { Variant("CrateDark", new Color(0.3f, 0.2f, 0.12f)) });
            made.Bell.SetVariants(new[] { Variant("BellRusted", new Color(0.55f, 0.3f, 0.15f)) });
            made.Traveller.SetOverridableFields(new[] { OverrideSet.ScaleMilli, OverrideSet.Visible });
            EditorUtility.SetDirty(made.Signpost);
            EditorUtility.SetDirty(made.Lantern);
            EditorUtility.SetDirty(made.Crate);
            EditorUtility.SetDirty(made.Bell);
            EditorUtility.SetDirty(made.Traveller);
            AssetDatabase.SaveAssets();
            return made;
        }

        private static GameObject Prefab(string name, PrimitiveType shape, Vector3 scale, Color color)
        {
            string path = Root + "/Prefabs/" + name + ".prefab";
            GameObject? existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null)
            {
                return existing;
            }

            GameObject root = new GameObject(name);
            GameObject body = GameObject.CreatePrimitive(shape);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = scale;
            body.transform.localPosition = new Vector3(0f, shape == PrimitiveType.Sphere ? scale.y * 0.5f : scale.y, 0f);
            if (shape == PrimitiveType.Cube)
            {
                body.transform.localPosition = new Vector3(0f, scale.y * 0.5f, 0f);
            }

            Renderer renderer = body.GetComponent<Renderer>();
            Material? template = renderer.sharedMaterial;
            if (template != null)
            {
                var material = new Material(template) { name = name };
                if (material.HasProperty("_BaseColor"))
                {
                    material.SetColor("_BaseColor", color);
                }

                if (material.HasProperty("_Color"))
                {
                    material.SetColor("_Color", color);
                }

                AssetDatabase.CreateAsset(material, Root + "/Prefabs/" + name + ".mat");
                renderer.sharedMaterial = material;
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return saved;
        }

        private static EntityDefinition Definition(string name, GameObject prefab, int scaleMilli)
        {
            EntityDefinition definition = ScriptableObject.CreateInstance<EntityDefinition>();
            definition.EnsureAuthoringId();
            definition.Configure(prefab, scaleMilli, true, true);
            AssetDatabase.CreateAsset(definition, Root + "/Definitions/" + name + ".asset");
            return definition;
        }

        private static VariantDefinition Variant(string name, Color tint)
        {
            VariantDefinition variant = ScriptableObject.CreateInstance<VariantDefinition>();
            variant.EnsureAuthoringId();
            variant.Configure(null, tint);
            AssetDatabase.CreateAsset(variant, Root + "/Definitions/" + name + ".asset");
            return variant;
        }

        private static string BuildVillage(RegionPlan village, Definitions d, RegionPlan marsh, RegionPlan belfry, PortalDefinition toMarsh, PortalDefinition toBelfry)
        {
            Scene scene = NewRegionScene(village, out AuthoredRegion region);
            Vector3 c = village.Center;
            AddPortalTowards(region, toMarsh, village, marsh);
            AddPortalTowards(region, toBelfry, village, belfry);
            WorldTools.SetSpawnPoint(region, c + new Vector3(0f, 0f, -12f), 0f);
            Decor(scene, "Well", PrimitiveType.Cylinder, c + new Vector3(0f, 0.5f, 0f), new Vector3(3f, 1f, 3f));
            Decor(scene, "Cottage", PrimitiveType.Cube, c + new Vector3(-14f, 2.5f, 10f), new Vector3(8f, 5f, 6f));
            Decor(scene, "Barn", PrimitiveType.Cube, c + new Vector3(14f, 3f, 12f), new Vector3(10f, 6f, 8f));

            AuthoredEntity traveller = EntityTools.PlaceIn(scene, d.Traveller, c + new Vector3(0f, 0f, -12f), 0f, TravellerName);
            EntityTools.PlaceIn(scene, d.Signpost, c + new Vector3(4f, 0f, -16f), 30f, "Village Signpost");
            AuthoredEntity litLantern = EntityTools.PlaceIn(scene, d.Lantern, c + new Vector3(-4f, 0f, -6f), 0f, "Lantern West");
            EntityTools.SetVariant(litLantern, 1);
            EntityTools.PlaceIn(scene, d.Lantern, c + new Vector3(4f, 0f, -6f), 0f, "Lantern East");
            IReadOnlyList<AuthoredEntity> crates = EntityTools.LayoutLineIn(scene, d.Crate, c + new Vector3(-10f, 0f, -2f), c + new Vector3(-10f, 0f, 6f), 3);
            crates[0].gameObject.name = MovableCrateName;
            EntityTools.ApplyOverride(crates[2], OverrideSet.ScaleMilli, "1500");
            EntityTools.LayoutRingIn(scene, d.Stone, c, 7f, 5, 0f);
            Save(scene);
            return traveller.AuthoringId;
        }

        private static void BuildMarsh(RegionPlan marsh, Definitions d, RegionPlan village, RegionPlan belfry, PortalDefinition toVillage, PortalDefinition toBelfry)
        {
            Scene scene = NewRegionScene(marsh, out AuthoredRegion region);
            Vector3 c = marsh.Center;
            AddPortalTowards(region, toVillage, marsh, village);
            AddPortalTowards(region, toBelfry, marsh, belfry);
            WorldTools.SetSpawnPoint(region, c + new Vector3(-10f, 0f, 0f), 90f);
            Decor(scene, "Pool", PrimitiveType.Cylinder, c + new Vector3(6f, 0.05f, 6f), new Vector3(14f, 0.1f, 10f));
            Decor(scene, "Dead Tree", PrimitiveType.Cylinder, c + new Vector3(-8f, 3f, 12f), new Vector3(0.8f, 3f, 0.8f));
            EntityTools.LayoutRingIn(scene, d.Reed, c + new Vector3(6f, 0f, 6f), 8f, 6, 15f);
            IReadOnlyList<AuthoredEntity> stones = EntityTools.LayoutLineIn(scene, d.Stone, c + new Vector3(-14f, 0f, -10f), c + new Vector3(-2f, 0f, -14f), 3);
            EntityTools.ApplyOverride(stones[1], OverrideSet.Visible, "false");
            EntityTools.PlaceIn(scene, d.Lantern, c + new Vector3(-6f, 0f, -4f), 0f, "Marsh Lantern");
            Save(scene);
        }

        private static void BuildBelfry(RegionPlan belfry, Definitions d, RegionPlan marsh, RegionPlan village, PortalDefinition toMarsh, PortalDefinition toVillage)
        {
            Scene scene = NewRegionScene(belfry, out AuthoredRegion region);
            Vector3 c = belfry.Center;
            AddPortalTowards(region, toMarsh, belfry, marsh);
            AddPortalTowards(region, toVillage, belfry, village);
            WorldTools.SetSpawnPoint(region, c + new Vector3(0f, 0f, -14f), 0f);
            Decor(scene, "Tower", PrimitiveType.Cube, c + new Vector3(0f, 8f, 8f), new Vector3(8f, 16f, 8f));
            Decor(scene, "Flooded Floor", PrimitiveType.Cube, c + new Vector3(0f, 0.05f, -2f), new Vector3(20f, 0.1f, 14f));
            AuthoredEntity bell = EntityTools.PlaceIn(scene, d.Bell, c + new Vector3(0f, 0f, -2f), 0f, "Drowned Bell");
            EntityTools.SetVariant(bell, 1);
            EntityTools.LayoutRingIn(scene, d.Stone, c + new Vector3(0f, 0f, -2f), 6f, 4, 45f);
            EntityTools.LayoutLineIn(scene, d.Crate, c + new Vector3(8f, 0f, -10f), c + new Vector3(12f, 0f, -10f), 2);
            EntityTools.PlaceIn(scene, d.Lantern, c + new Vector3(-8f, 0f, -10f), 0f, "Belfry Lantern");
            Save(scene);
        }

        private static Scene NewRegionScene(RegionPlan plan, out AuthoredRegion region)
        {
            Scene scene = NewAuthoringScene();
            var regionObject = new GameObject("Region " + plan.Name);
            SceneManager.MoveGameObjectToScene(regionObject, scene);
            regionObject.transform.position = plan.Center;
            region = regionObject.AddComponent<AuthoredRegion>();
            region.Bounds.Configure(new Vector3(0f, 10f, 0f), new Vector3(Half * 2f, 30f, Half * 2f));
            region.Configure(plan.Definition!, null);

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            SceneManager.MoveGameObjectToScene(ground, scene);
            ground.transform.position = plan.Center;
            ground.transform.localScale = new Vector3(Half * 2f / 10f, 1f, Half * 2f / 10f);

            // Saved once now so the scene has its path before tools reference it.
            EditorSceneManager.SaveScene(scene, plan.Scene);
            return scene;
        }

        private static void AddPortalTowards(AuthoredRegion region, PortalDefinition portal, RegionPlan from, RegionPlan to)
        {
            Vector3 direction = to.Center - from.Center;
            direction.y = 0f;
            direction.Normalize();
            Vector3 position = from.Center + direction * (Half - 5f);
            float yaw = Mathf.Atan2(-direction.x, -direction.z) * Mathf.Rad2Deg;
            WorldTools.AddPortal(region, portal, position, yaw, 2.5f);
        }

        private static void Decor(Scene scene, string name, PrimitiveType shape, Vector3 position, Vector3 scale)
        {
            GameObject decor = GameObject.CreatePrimitive(shape);
            decor.name = name;
            SceneManager.MoveGameObjectToScene(decor, scene);
            decor.transform.position = position;
            decor.transform.localScale = scale;
            decor.isStatic = true;
        }

        private static void Save(Scene scene)
        {
            EditorSceneManager.SaveScene(scene);
            CloseAuthoringScene(scene);
        }
    }
}
