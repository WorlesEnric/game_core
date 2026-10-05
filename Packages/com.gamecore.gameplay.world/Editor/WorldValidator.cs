// GameCore.Gameplay.World.Editor - world validators with stable codes (P1.1; region, portal and start-point rules by P1.7b).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.World.Editor
{
    /// <summary>Validates world definitions and region scenes.</summary>
    [AuthorValidator("world.validator", Codes = new[]
    {
        GameplayDiagnosticCodes.MissingAuthoringId,
        GameplayDiagnosticCodes.DuplicateAuthoringId,
        GameplayDiagnosticCodes.RegionMissingScene,
        GameplayDiagnosticCodes.RegionMissingBounds,
        GameplayDiagnosticCodes.RegionMultipleInScene,
        GameplayDiagnosticCodes.PortalUnconnected,
        GameplayDiagnosticCodes.PortalTargetsOwnRegion,
        GameplayDiagnosticCodes.WorldMissingStartRegion,
        GameplayDiagnosticCodes.WorldUnknownRegion,
        AuthoringHardeningCodes.PortalSpawnPointMissing,
        AuthoringHardeningCodes.RegionNeighbourUnconnected,
        AuthoringHardeningCodes.RegionBoundsInvalid,
        AuthoringHardeningCodes.RegionSpawnPointDuplicate,
        AuthoringHardeningCodes.WorldStartPointMissing,
        AuthoringHardeningCodes.WrongReferenceCategory,
    })]
    public static class WorldValidator
    {
        /// <summary>Problems of a world definition (its regions and portals; not their scenes).</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(WorldDefinition world)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (world == null)
            {
                return diagnostics;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            Check(world.AuthoringId, world.name, ids, diagnostics);
            var regions = new HashSet<RegionDefinition>();
            for (int i = 0; i < world.Regions.Count; i++)
            {
                RegionDefinition region = world.Regions[i];
                if (region == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.WorldUnknownRegion, world.AuthoringId, world.name + " lists a missing region"));
                    continue;
                }

                regions.Add(region);
                Check(region.AuthoringId, region.name, ids, diagnostics);
                if (string.IsNullOrEmpty(region.ScenePath))
                {
                    diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.RegionMissingScene, region.AuthoringId, region.name + " has no scene"));
                }

                diagnostics.AddRange(Validate(region));
            }

            if (world.StartRegion == null || !regions.Contains(world.StartRegion))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.WorldMissingStartRegion, world.AuthoringId,
                    world.name + " has no start region among its regions"));
            }
            else if (!string.IsNullOrEmpty(world.StartSpawnPoint) && !world.StartRegion.TryGetSpawnPoint(world.StartSpawnPoint, out RegionSpawnPoint _))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WorldStartPointMissing, world.AuthoringId,
                    world.name + " starts at spawn point '" + world.StartSpawnPoint + "', which " + world.StartRegion.name + " does not declare"));
            }

            if (world.FocusEntityId.Length > 0 && !AuthoringIds.IsValid(world.FocusEntityId))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.EntityReferenceInvalid, world.AuthoringId,
                    world.name + " follows '" + world.FocusEntityId + "', which is not an authoring id"));
            }

            var connected = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < world.Portals.Count; i++)
            {
                PortalDefinition portal = world.Portals[i];
                if (portal != null && portal.RegionA != null && portal.RegionB != null)
                {
                    connected.Add(portal.RegionA.AuthoringId + ">" + portal.RegionB.AuthoringId);
                    connected.Add(portal.RegionB.AuthoringId + ">" + portal.RegionA.AuthoringId);
                }
            }

            foreach (RegionDefinition region in regions)
            {
                for (int n = 0; n < region.Neighbours.Count; n++)
                {
                    RegionDefinition neighbour = region.Neighbours[n];
                    if (neighbour == null || !regions.Contains(neighbour) || !connected.Contains(region.AuthoringId + ">" + neighbour.AuthoringId))
                    {
                        diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.RegionNeighbourUnconnected, region.AuthoringId,
                            region.name + " lists neighbour " + (neighbour != null ? neighbour.name : "(missing)") + ", which no portal of " + world.name + " connects"));
                    }
                }
            }

            for (int i = 0; i < world.Portals.Count; i++)
            {
                PortalDefinition portal = world.Portals[i];
                if (portal == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.PortalUnconnected, world.AuthoringId, world.name + " lists a missing portal"));
                    continue;
                }

                Check(portal.AuthoringId, portal.name, ids, diagnostics);
                diagnostics.AddRange(Validate(portal, regions));
            }

            return diagnostics;
        }

        /// <summary>Problems of one region definition on its own: bounds, spawn point names, ambience category.</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(RegionDefinition region)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (region == null)
            {
                return diagnostics;
            }

            Vector3 size = region.Bounds.size;
            bool unset = size == Vector3.zero;
            if (!unset && (size.x <= 0f || size.y <= 0f || size.z <= 0f))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.RegionBoundsInvalid, region.AuthoringId, region.name + " has bounds of size " + size));
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < region.SpawnPoints.Count; i++)
            {
                RegionSpawnPoint point = region.SpawnPoints[i];
                if (point == null || string.IsNullOrEmpty(point.name) || !names.Add(point.name))
                {
                    diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.RegionSpawnPointDuplicate, region.AuthoringId,
                        region.name + " spawn point " + i + " has an empty or repeated name"));
                }
            }

            if (region.Ambience != null && !IsOfType(region.Ambience, "audio.ambience"))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WrongReferenceCategory, region.AuthoringId, region.name + "'s ambience is not an audio.ambience"));
            }

            return diagnostics;
        }

        /// <summary>True when <paramref name="value"/>'s type carries [Authorable] with <paramref name="typeId"/>.</summary>
        public static bool IsOfType(UnityEngine.Object value, string typeId)
        {
            if (value == null)
            {
                return false;
            }

            object[] attributes = value.GetType().GetCustomAttributes(typeof(AuthorableAttribute), false);
            return attributes.Length == 1 && string.Equals(((AuthorableAttribute)attributes[0]).ObjectTypeId, typeId, StringComparison.Ordinal);
        }

        /// <summary>Problems of one portal against the regions of its world.</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(PortalDefinition portal, ICollection<RegionDefinition> regions)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (portal.RegionA == null || portal.RegionB == null)
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.PortalUnconnected, portal.AuthoringId, portal.name + " does not name two regions"));
                return diagnostics;
            }

            if (portal.RegionA == portal.RegionB)
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.PortalTargetsOwnRegion, portal.AuthoringId, portal.name + " connects a region to itself"));
            }

            if (!regions.Contains(portal.RegionA) || !regions.Contains(portal.RegionB))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.WorldUnknownRegion, portal.AuthoringId, portal.name + " connects a region outside the world"));
            }

            if (portal.SpawnPointA.Length > 0 && !portal.RegionA.TryGetSpawnPoint(portal.SpawnPointA, out RegionSpawnPoint _))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.PortalSpawnPointMissing, portal.AuthoringId,
                    portal.name + " arrives at '" + portal.SpawnPointA + "', which " + portal.RegionA.name + " does not declare"));
            }

            if (portal.SpawnPointB.Length > 0 && !portal.RegionB.TryGetSpawnPoint(portal.SpawnPointB, out RegionSpawnPoint _))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.PortalSpawnPointMissing, portal.AuthoringId,
                    portal.name + " arrives at '" + portal.SpawnPointB + "', which " + portal.RegionB.name + " does not declare"));
            }

            if (portal.Condition != null && !IsOfType(portal.Condition, "logic.conditionSet"))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.WrongReferenceCategory, portal.AuthoringId, portal.name + "'s condition is not a logic.conditionSet"));
            }

            return diagnostics;
        }

        /// <summary>Problems of one loaded region scene: exactly one region marker, bounds, portal ends.</summary>
        public static IReadOnlyList<GameplayDiagnostic> ValidateScene(Scene scene)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return diagnostics;
            }

            var markers = new List<AuthoredRegion>();
            var ends = new List<RegionPortal>();
            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                markers.AddRange(roots[i].GetComponentsInChildren<AuthoredRegion>(true));
                ends.AddRange(roots[i].GetComponentsInChildren<RegionPortal>(true));
            }

            if (markers.Count != 1)
            {
                diagnostics.Add(new GameplayDiagnostic(
                    markers.Count == 0 ? GameplayDiagnosticCodes.RegionMissingBounds : GameplayDiagnosticCodes.RegionMultipleInScene,
                    scene.path,
                    scene.path + " holds " + markers.Count + " region markers; exactly one is required"));
                return diagnostics;
            }

            AuthoredRegion marker = markers[0];
            if (marker.Definition == null)
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.WorldUnknownRegion, scene.path, "the region marker of " + scene.path + " has no definition"));
                return diagnostics;
            }

            if (!string.Equals(marker.Definition.ScenePath, scene.path, StringComparison.Ordinal))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.RegionMissingScene, marker.AuthoringId,
                    marker.Definition.name + " names scene " + marker.Definition.ScenePath + " but its marker is in " + scene.path));
            }

            Vector3 size = marker.Bounds.Size;
            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.RegionMissingBounds, marker.AuthoringId, marker.Definition.name + " has empty bounds"));
            }

            for (int i = 0; i < ends.Count; i++)
            {
                PortalDefinition? portal = ends[i].Portal;
                if (portal == null || (portal.RegionA != marker.Definition && portal.RegionB != marker.Definition))
                {
                    diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.PortalUnconnected, marker.AuthoringId,
                        "portal end " + ends[i].name + " does not belong to a portal of " + marker.Definition.name));
                }
            }

            return diagnostics;
        }

        private static void Check(string id, string name, HashSet<string> seen, List<GameplayDiagnostic> diagnostics)
        {
            if (!AuthoringIds.IsValid(id))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.MissingAuthoringId, name, name + " has no valid authoring id"));
            }
            else if (!seen.Add(id))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.DuplicateAuthoringId, id, name + " repeats authoring id " + id));
            }
        }
    }
}
