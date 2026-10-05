// GameCore.Gameplay.World.Editor - world authoring operations (P1.1, Studio 03 s4/s5).
//
//   world.connectRegions  create a PortalDefinition asset connecting two regions of a world and list it on the world;
//                         the two regions become each other's neighbours; optional arrival spawn points and condition
//   world.addPortal       place one end of a portal (trigger + arrival pose) in a region scene
//   world.setSpawnPoint   set a region's spawn point (creates or moves its SpawnPoint child) and record it as a named
//                         spawn point of the region definition
//   world.configurePortal set a portal's arrival spawn points and the condition travel needs (P1.7b)
//   world.setRegionBounds set a region definition's bounds (P1.7b)
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
            [AuthorArg(Category = "world.region", Doc = "First region.")] RegionDefinition regionA,
            [AuthorArg(Category = "world.region", Doc = "Second region.")] RegionDefinition regionB,
            [AuthorArg(Required = false, Doc = "Asset path of the new portal; defaults to <world dir>/Portals/<A>_<B>.asset.")] string assetPath = "",
            [AuthorArg(Required = false, Doc = "Spawn point of region A travellers arrive at (empty: the portal end's arrival pose).")] string spawnPointA = "",
            [AuthorArg(Required = false, Doc = "Spawn point of region B travellers arrive at (empty: the portal end's arrival pose).")] string spawnPointB = "",
            [AuthorArg(Category = "logic.conditionSet", Required = false, Doc = "Condition set travel needs (empty: always open).")] ScriptableObject? condition = null)
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

            if (!string.IsNullOrEmpty(spawnPointA) && !regionA.TryGetSpawnPoint(spawnPointA, out RegionSpawnPoint _))
            {
                throw new ArgumentException(AuthoringHardeningCodes.PortalSpawnPointMissing + ": " + regionA.name + " declares no spawn point '" + spawnPointA + "'");
            }

            if (!string.IsNullOrEmpty(spawnPointB) && !regionB.TryGetSpawnPoint(spawnPointB, out RegionSpawnPoint _))
            {
                throw new ArgumentException(AuthoringHardeningCodes.PortalSpawnPointMissing + ": " + regionB.name + " declares no spawn point '" + spawnPointB + "'");
            }

            if (condition != null && !WorldValidator.IsOfType(condition, "logic.conditionSet"))
            {
                throw new ArgumentException(AuthoringHardeningCodes.WrongReferenceCategory + ": the portal condition must be a logic.conditionSet");
            }

            PortalDefinition portal = ScriptableObject.CreateInstance<PortalDefinition>();
            portal.EnsureAuthoringId();
            portal.Connect(regionA, regionB);
            portal.SetSpawnPoints(spawnPointA, spawnPointB);
            portal.SetCondition(condition);
            AssetDatabase.CreateAsset(portal, path);
            Undo.RegisterCreatedObjectUndo(portal, "world.connectRegions");
            Undo.RecordObject(world, "world.connectRegions");
            world.AddPortal(portal);
            Undo.RecordObject(regionA, "world.connectRegions");
            Undo.RecordObject(regionB, "world.connectRegions");
            regionA.AddNeighbour(regionB);
            regionB.AddNeighbour(regionA);
            EditorUtility.SetDirty(regionA);
            EditorUtility.SetDirty(regionB);
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
            Doc = "Sets a region spawn point: the default one creates or moves the scene's SpawnPoint child; every one is recorded as a named spawn point of the region definition.")]
        public static Transform SetSpawnPoint(
            AuthoredRegion region,
            [AuthorArg(Unit = "m", Doc = "Spawn position (world space).")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Spawn heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Spawn point name (empty: default, the region's own spawn).")] string name = "")
        {
            if (region == null || region.Definition == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": a region marker with a definition is required");
            }

            if (!region.Bounds.Contains(position))
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityOutsideRegion + ": the spawn point lies outside the region bounds");
            }

            string pointName = string.IsNullOrEmpty(name) ? RegionDefinition.DefaultSpawnPoint : name;
            Undo.RecordObject(region.Definition, "world.setSpawnPoint");
            region.Definition.SetSpawnPoint(pointName, position, yaw);
            EditorUtility.SetDirty(region.Definition);
            if (!string.Equals(pointName, RegionDefinition.DefaultSpawnPoint, StringComparison.Ordinal))
            {
                Transform? named = region.transform.Find("SpawnPoint " + pointName);
                if (named == null)
                {
                    var marker = new GameObject("SpawnPoint " + pointName);
                    Undo.RegisterCreatedObjectUndo(marker, "world.setSpawnPoint");
                    marker.transform.SetParent(region.transform, false);
                    named = marker.transform;
                }
                else
                {
                    Undo.RecordObject(named, "world.setSpawnPoint");
                }

                named.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                EditorSceneManager.MarkSceneDirty(region.gameObject.scene);
                return named;
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

        [AuthorOperation("world.configurePortal", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(WorldValidator), Requires = "world.portal",
            Doc = "Sets the spawn point travellers arrive at on each side of a portal and the condition set travel needs (e.g. 'the ferryman's favour OR the repaired punt': an Any-mode condition set).")]
        public static void ConfigurePortal(
            PortalDefinition portal,
            [AuthorArg(Category = "logic.conditionSet", Required = false, Doc = "Condition set travel needs (empty: always open).")] ScriptableObject? condition,
            [AuthorArg(Required = false, Doc = "Spawn point of region A (empty: the portal end's arrival pose).")] string spawnPointA = "",
            [AuthorArg(Required = false, Doc = "Spawn point of region B (empty: the portal end's arrival pose).")] string spawnPointB = "")
        {
            if (portal == null || portal.RegionA == null || portal.RegionB == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": a portal connecting two regions is required");
            }

            if (condition != null && !WorldValidator.IsOfType(condition, "logic.conditionSet"))
            {
                throw new ArgumentException(AuthoringHardeningCodes.WrongReferenceCategory + ": the portal condition must be a logic.conditionSet");
            }

            if (!string.IsNullOrEmpty(spawnPointA) && !portal.RegionA.TryGetSpawnPoint(spawnPointA, out RegionSpawnPoint _))
            {
                throw new ArgumentException(AuthoringHardeningCodes.PortalSpawnPointMissing + ": " + portal.RegionA.name + " declares no spawn point '" + spawnPointA + "'");
            }

            if (!string.IsNullOrEmpty(spawnPointB) && !portal.RegionB.TryGetSpawnPoint(spawnPointB, out RegionSpawnPoint _))
            {
                throw new ArgumentException(AuthoringHardeningCodes.PortalSpawnPointMissing + ": " + portal.RegionB.name + " declares no spawn point '" + spawnPointB + "'");
            }

            Undo.RecordObject(portal, "world.configurePortal");
            portal.SetCondition(condition);
            portal.SetSpawnPoints(spawnPointA, spawnPointB);
            EditorUtility.SetDirty(portal);
        }

        [AuthorOperation("world.setRegionBounds", Tier = ToolTier.Configure, RuntimeApplicability = RuntimeApply.Rebuild,
            Validator = typeof(WorldValidator), Requires = "world.region",
            Doc = "Sets a region definition's bounds (world space, m); a zero size hands the bounds back to the scene's RegionBounds.")]
        public static void SetRegionBounds(
            RegionDefinition region,
            [AuthorArg(Unit = "m", Doc = "Centre.")] Vector3 center,
            [AuthorArg(Unit = "m", Doc = "Size (all positive, or all zero).")] Vector3 size)
        {
            if (region == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": a region definition is required");
            }

            bool zero = size == Vector3.zero;
            if (!zero && (size.x <= 0f || size.y <= 0f || size.z <= 0f))
            {
                throw new ArgumentException(AuthoringHardeningCodes.RegionBoundsInvalid + ": bounds sizes are positive (or all zero)");
            }

            Undo.RecordObject(region, "world.setRegionBounds");
            region.SetBounds(center, size);
            EditorUtility.SetDirty(region);
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
