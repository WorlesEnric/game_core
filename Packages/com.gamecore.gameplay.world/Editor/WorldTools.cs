// GameCore.Gameplay.World.Editor - world authoring operations (P1.1, Studio 03 s4/s5).
//
//   world.connectRegions  create a PortalDefinition asset connecting two regions of a world and list it on the world
//   world.addPortal       place one end of a portal (trigger + arrival pose) in a region scene
//   world.setSpawnPoint   set a region's spawn point (creates or moves its SpawnPoint child)
//
// Like the entity tools, every tool validates before it changes anything, records Undo, marks what it edited dirty and
// refuses with an ArgumentException whose message starts with the GP-* code.
#nullable enable
using System;
using System.IO;
using GameCore.Gameplay.Contracts;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.World.Editor
{
    /// <summary>The world.* authoring operations.</summary>
    public static class WorldTools
    {
        [AuthorOperation("world.connectRegions", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(WorldValidator), Requires = "world.definition",
            Doc = "Creates a portal connection between two regions of a world (a PortalDefinition asset beside the world).")]
        public static PortalDefinition ConnectRegions(
            WorldDefinition world,
            RegionDefinition regionA,
            RegionDefinition regionB,
            [AuthorArg(Required = false, Doc = "Asset path of the new portal; defaults to <world dir>/Portals/<A>_<B>.asset.")] string assetPath = "")
        {
            if (world == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldMissingStartRegion + ": a world is required");
            }

            if (regionA == null || regionB == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.PortalUnconnected + ": a portal connects two regions");
            }

            if (regionA == regionB)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.PortalTargetsOwnRegion + ": a portal cannot connect " + regionA.name + " to itself");
            }

            if (!Contains(world, regionA) || !Contains(world, regionB))
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": both regions must belong to world " + world.name);
            }

            string path = assetPath;
            if (string.IsNullOrEmpty(path))
            {
                string worldPath = AssetDatabase.GetAssetPath(world);
                if (string.IsNullOrEmpty(worldPath))
                {
                    throw new ArgumentException(GameplayDiagnosticCodes.WorldMissingStartRegion + ": the world is not an asset; pass an asset path");
                }

                string directory = Path.GetDirectoryName(worldPath)!.Replace('\\', '/') + "/Portals";
                EnsureFolder(directory);
                path = AssetDatabase.GenerateUniqueAssetPath(directory + "/" + regionA.name + "_" + regionB.name + ".asset");
            }

            PortalDefinition portal = ScriptableObject.CreateInstance<PortalDefinition>();
            portal.EnsureAuthoringId();
            portal.Connect(regionA, regionB);
            AssetDatabase.CreateAsset(portal, path);
            Undo.RegisterCreatedObjectUndo(portal, "world.connectRegions");
            Undo.RecordObject(world, "world.connectRegions");
            world.AddPortal(portal);
            EditorUtility.SetDirty(world);
            EditorUtility.SetDirty(portal);
            return portal;
        }

        [AuthorOperation("world.addPortal", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(WorldValidator), Requires = "world.region", RequiresOnTarget = "world.portal",
            Doc = "Places one end of a portal in a region scene: a trigger volume whose forward axis points into the region.")]
        public static RegionPortal AddPortal(
            AuthoredRegion region,
            PortalDefinition portal,
            [AuthorArg(Unit = "m", Doc = "Position of the portal end.")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading of the arrival direction around +Y.")] float yaw = 0f,
            [AuthorArg(Unit = "m", Required = false, Doc = "Arrival distance in front of the portal.")] float arrivalDistance = 2.5f)
        {
            if (region == null || region.Definition == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": a region marker with a definition is required");
            }

            if (portal == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.PortalUnconnected + ": a portal is required");
            }

            if (portal.RegionA != region.Definition && portal.RegionB != region.Definition)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.PortalUnconnected + ": portal " + portal.name + " does not connect " + region.Definition.name);
            }

            RegionDefinition? other = portal.Other(region.Definition);
            var end = new GameObject("Portal to " + (other != null ? other.DisplayName : portal.name));
            SceneManager.MoveGameObjectToScene(end, region.gameObject.scene);
            end.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            BoxCollider trigger = end.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(3f, 3f, 1f);
            RegionPortal component = end.AddComponent<RegionPortal>();
            component.Configure(portal, arrivalDistance);
            Undo.RegisterCreatedObjectUndo(end, "world.addPortal");
            EditorSceneManager.MarkSceneDirty(region.gameObject.scene);
            return component;
        }

        [AuthorOperation("world.setSpawnPoint", Tier = ToolTier.Compose, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(WorldValidator), Requires = "world.region",
            Doc = "Sets the region's spawn point: creates or moves its SpawnPoint child.")]
        public static Transform SetSpawnPoint(
            AuthoredRegion region,
            [AuthorArg(Unit = "m", Doc = "Spawn position (world space).")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Spawn heading around +Y.")] float yaw = 0f)
        {
            if (region == null || region.Definition == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": a region marker with a definition is required");
            }

            if (!region.Bounds.Contains(position))
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityOutsideRegion + ": the spawn point lies outside the region bounds");
            }

            Transform? spawn = region.transform.Find("SpawnPoint");
            if (spawn == null)
            {
                var created = new GameObject("SpawnPoint");
                Undo.RegisterCreatedObjectUndo(created, "world.setSpawnPoint");
                created.transform.SetParent(region.transform, false);
                spawn = created.transform;
            }
            else
            {
                Undo.RecordObject(spawn, "world.setSpawnPoint");
            }

            spawn.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Undo.RecordObject(region, "world.setSpawnPoint");
            region.Configure(region.Definition, spawn);
            EditorUtility.SetDirty(region);
            EditorSceneManager.MarkSceneDirty(region.gameObject.scene);
            return spawn;
        }

        private static bool Contains(WorldDefinition world, RegionDefinition region)
        {
            for (int i = 0; i < world.Regions.Count; i++)
            {
                if (world.Regions[i] == region)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Creates an asset folder (and its parents) when missing.</summary>
        public static void EnsureFolder(string folder)
        {
            string normalized = folder.Replace('\\', '/').TrimEnd('/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }

            int slash = normalized.LastIndexOf('/');
            if (slash <= 0)
            {
                return;
            }

            string parent = normalized.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, normalized.Substring(slash + 1));
        }
    }
}
