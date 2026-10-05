// GameCore.Gameplay.World.Editor - world authoring operations (P1.1, Studio 03 s4/s5).
//
//   world.connectRegions  create a PortalDefinition asset connecting two regions of a world and list it on the world;
//                         the two regions become each other's neighbours; optional arrival spawn points and condition
//   world.addPortal       place one end of a portal (trigger + arrival pose) in the open scene of one of its regions
//   world.setSpawnPoint   set a region's spawn point (creates or moves its SpawnPoint child when the region scene is
//                         open) and record it as a named spawn point of the region definition
//   world.configurePortal set a portal's arrival spawn points and the condition travel needs (P1.7b)
//   world.setRegionBounds set a region definition's bounds (P1.7b)
//
// Like the entity tools, every tool validates before it changes anything, records Undo, marks what it edited dirty and
// refuses with an ArgumentException whose message starts with the GP-* code.
//
// Engine binding (P1.7b): a tool's target is its first [Authorable] parameter and every other parameter is an
// [AuthorArg], so ChangeSetEngine/ToolRegistry can bind all of them. world.addPortal targets the PortalDefinition and
// world.setSpawnPoint the RegionDefinition; both find the region's scene marker (AuthoredRegion, a non-authorable
// component) among the open scenes. The AuthoredRegion overloads stay for authoring scripts and tests.
#nullable enable
using System;
using System.Collections.Generic;
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
            Doc = "Places one end of the target portal in the open scene of one of the two regions it connects: a trigger volume whose forward axis points into the region.")]
        public static RegionPortal AddPortalEnd(
            PortalDefinition portal,
            [AuthorArg(Unit = "m", Doc = "Position of the portal end.")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Heading of the arrival direction around +Y.")] float yaw = 0f,
            [AuthorArg(Unit = "m", Required = false, Doc = "Arrival distance in front of the portal.")] float arrivalDistance = 2.5f,
            [AuthorArg(Category = "world.region", Required = false, Doc = "Region whose scene receives the end (empty: the portal's region whose scene is open; when both are, the one whose bounds hold the position).")] RegionDefinition? region = null)
        {
            if (portal == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.PortalUnconnected + ": a portal is required");
            }

            return AddPortal(MarkerFor(portal, region, position), portal, position, yaw, arrivalDistance);
        }

        /// <summary>Places one end of <paramref name="portal"/> in the scene of <paramref name="region"/> (authoring scripts).</summary>
        public static RegionPortal AddPortal(
            AuthoredRegion region,
            PortalDefinition portal,
            Vector3 position,
            float yaw = 0f,
            float arrivalDistance = 2.5f)
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
            Doc = "Sets a spawn point of the target region: recorded as a named spawn point of the region definition; when the region scene is open, the default one also creates or moves the scene's SpawnPoint child (a named one its 'SpawnPoint <name>' marker).")]
        public static void SetRegionSpawnPoint(
            RegionDefinition region,
            [AuthorArg(Unit = "m", Doc = "Spawn position (world space).")] Vector3 position,
            [AuthorArg(Unit = "deg", Required = false, Doc = "Spawn heading around +Y.")] float yaw = 0f,
            [AuthorArg(Required = false, Doc = "Spawn point name (empty: default, the region's own spawn).")] string name = "")
        {
            if (region == null)
            {
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": a region is required");
            }

            AuthoredRegion? marker = OpenMarker(region);
            if (marker != null)
            {
                SetSpawnPoint(marker, position, yaw, name);
                return;
            }

            if (region.HasBounds && !region.Bounds.Contains(position))
            {
                throw new ArgumentException(GameplayDiagnosticCodes.EntityOutsideRegion + ": the spawn point lies outside the region bounds");
            }

            Undo.RecordObject(region, "world.setSpawnPoint");
            region.SetSpawnPoint(string.IsNullOrEmpty(name) ? RegionDefinition.DefaultSpawnPoint : name, position, yaw);
            EditorUtility.SetDirty(region);
        }

        /// <summary>The scene marker of <paramref name="region"/> among the open scenes, or null when its scene is closed.</summary>
        public static AuthoredRegion? OpenMarker(RegionDefinition region)
        {
            foreach (AuthoredRegion marker in OpenMarkers())
            {
                if (marker.Definition == region)
                {
                    return marker;
                }
            }

            return null;
        }

        /// <summary>
        /// The open scene marker receiving an end of <paramref name="portal"/>: the named region's, else the one region
        /// of the portal whose scene is open, else (both open) the one whose bounds hold <paramref name="position"/>.
        /// </summary>
        public static AuthoredRegion MarkerFor(PortalDefinition portal, RegionDefinition? region, Vector3 position)
        {
            var candidates = new List<AuthoredRegion>();
            foreach (AuthoredRegion marker in OpenMarkers())
            {
                RegionDefinition? definition = marker.Definition;
                bool wanted = region != null ? definition == region : definition != null && (definition == portal.RegionA || definition == portal.RegionB);
                if (wanted)
                {
                    candidates.Add(marker);
                }
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (candidates.Count == 0)
            {
                string which = region != null ? region.name : (portal.RegionA != null ? portal.RegionA.name : "?") + " or " + (portal.RegionB != null ? portal.RegionB.name : "?");
                throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": no open scene holds the marker of " + which + "; open the region scene first");
            }

            foreach (AuthoredRegion candidate in candidates)
            {
                if (candidate.Bounds != null && candidate.Bounds.Contains(position))
                {
                    return candidate;
                }
            }

            throw new ArgumentException(GameplayDiagnosticCodes.WorldUnknownRegion + ": both regions of " + portal.name + " are open and neither holds the position; name the region");
        }

        private static IEnumerable<AuthoredRegion> OpenMarkers()
        {
            for (int s = 0; s < SceneManager.sceneCount; s++)
            {
                Scene scene = SceneManager.GetSceneAt(s);
                if (!scene.isLoaded)
                {
                    continue;
                }

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (AuthoredRegion marker in root.GetComponentsInChildren<AuthoredRegion>(true))
                    {
                        yield return marker;
                    }
                }
            }
        }

        /// <summary>Sets the spawn point of the region scene <paramref name="region"/> (authoring scripts).</summary>
        public static Transform SetSpawnPoint(
            AuthoredRegion region,
            Vector3 position,
            float yaw = 0f,
            string name = "")
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
