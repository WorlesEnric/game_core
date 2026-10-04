// GameCore.Gameplay.World.Editor - world validators with stable codes (P1.1).
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
            }

            if (world.StartRegion == null || !regions.Contains(world.StartRegion))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.WorldMissingStartRegion, world.AuthoringId,
                    world.name + " has no start region among its regions"));
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
