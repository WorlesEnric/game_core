// GameCore.Gameplay.Compile.Editor - reads authored assets and region scenes into the bake model (P1.1).
//
// Region scenes are opened additively through EditorSceneManager when they are not already open and closed again
// without saving, so a bake never leaves a scene loaded or dirty. A scene already open in the editor is read as it is in
// memory and left open. A scene that becomes dirty merely by being opened (an entity minted a missing authoring id on
// load) is refused with GP-CMP-003: its ids would not survive the bake, so the scene must be opened and saved first.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using GameCore.Gameplay.Entities;
using GameCore.Gameplay.World;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.Compile
{
    /// <summary>What reading the authored world produced: the model, the definition assets and the problems.</summary>
    public sealed class WorldReadResult
    {
        public WorldReadResult(BakedWorld world)
        {
            World = world;
        }

        public BakedWorld World { get; }

        /// <summary>Definition assets by authoring id (the manifest references them for presentation).</summary>
        public Dictionary<string, EntityDefinition> Definitions { get; } = new Dictionary<string, EntityDefinition>(StringComparer.Ordinal);

        public List<GameplayDiagnostic> Diagnostics { get; } = new List<GameplayDiagnostic>();
    }

    /// <summary>Reads a WorldDefinition and its region scenes.</summary>
    public static class WorldReader
    {
        public static WorldReadResult Read(WorldDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var world = new BakedWorld
            {
                WorldId = definition.AuthoringId,
                WorldName = definition.name,
                StartRegionId = definition.StartRegion != null ? definition.StartRegion.AuthoringId : string.Empty,
                FocusEntityId = definition.FocusEntityId,
                PreloadNeighbours = definition.PreloadNeighbours,
            };
            var result = new WorldReadResult(world);

            var portalsById = new Dictionary<string, BakedPortal>(StringComparer.Ordinal);
            for (int i = 0; i < definition.Portals.Count; i++)
            {
                PortalDefinition portal = definition.Portals[i];
                if (portal == null)
                {
                    continue;
                }

                var baked = new BakedPortal
                {
                    AuthoringId = portal.AuthoringId,
                    Name = portal.name,
                    RegionA = portal.RegionA != null ? portal.RegionA.AuthoringId : string.Empty,
                    RegionB = portal.RegionB != null ? portal.RegionB.AuthoringId : string.Empty,
                    ConditionRef = portal.ConditionRef,
                };
                world.Portals.Add(baked);
                portalsById[portal.AuthoringId] = baked;
            }

            for (int i = 0; i < definition.Regions.Count; i++)
            {
                RegionDefinition region = definition.Regions[i];
                if (region == null)
                {
                    continue;
                }

                ReadRegion(region, world, portalsById, result);
            }

            // P1.7b: a portal end that names a spawn point of its arrival region arrives there, not at the end's own pose.
            for (int i = 0; i < definition.Portals.Count; i++)
            {
                PortalDefinition portal = definition.Portals[i];
                if (portal == null || !portalsById.TryGetValue(portal.AuthoringId, out BakedPortal? baked))
                {
                    continue;
                }

                if (portal.RegionA != null && portal.SpawnPointA.Length > 0 && portal.RegionA.TryGetSpawnPoint(portal.SpawnPointA, out RegionSpawnPoint arriveA))
                {
                    baked.ArrivalA = PoseOf(arriveA);
                }

                if (portal.RegionB != null && portal.SpawnPointB.Length > 0 && portal.RegionB.TryGetSpawnPoint(portal.SpawnPointB, out RegionSpawnPoint arriveB))
                {
                    baked.ArrivalB = PoseOf(arriveB);
                }
            }

            world.Canonicalize();
            return result;
        }

        private static BakedPose PoseOf(RegionSpawnPoint point)
        {
            return new BakedPose(
                GameplayUnits.ToMillimetres(point.position.x),
                GameplayUnits.ToMillimetres(point.position.y),
                GameplayUnits.ToMillimetres(point.position.z),
                GameplayUnits.NormalizeYaw(GameplayUnits.DegreesToMilliradians(point.yaw)));
        }

        private static void ReadRegion(RegionDefinition region, BakedWorld world, Dictionary<string, BakedPortal> portals, WorldReadResult result)
        {
            var baked = new BakedRegion
            {
                AuthoringId = region.AuthoringId,
                Name = region.DisplayName,
                ScenePath = region.ScenePath,
            };
            world.Regions.Add(baked);
            if (string.IsNullOrEmpty(region.ScenePath))
            {
                return;
            }

            Scene scene = SceneManager.GetSceneByPath(region.ScenePath);
            bool opened = false;
            if (!scene.IsValid() || !scene.isLoaded)
            {
                try
                {
                    scene = EditorSceneManager.OpenScene(region.ScenePath, OpenSceneMode.Additive);
                    opened = true;
                }
                catch (ArgumentException error)
                {
                    result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeSceneUnreadable, region.AuthoringId,
                        "region scene " + region.ScenePath + " could not be opened: " + error.Message));
                    return;
                }
            }

            try
            {
                if (opened && scene.isDirty)
                {
                    result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.BakeSceneUnreadable, region.AuthoringId,
                        "region scene " + region.ScenePath + " changes when opened (an authoring id was minted); open and save it before baking"));
                }

                ReadScene(scene, region, baked, world, portals, result);
            }
            finally
            {
                if (opened)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void ReadScene(Scene scene, RegionDefinition region, BakedRegion baked, BakedWorld world, Dictionary<string, BakedPortal> portals, WorldReadResult result)
        {
            GameObject[] roots = scene.GetRootGameObjects();
            int regionMarkers = 0;
            for (int r = 0; r < roots.Length; r++)
            {
                foreach (AuthoredRegion marker in roots[r].GetComponentsInChildren<AuthoredRegion>(true))
                {
                    regionMarkers++;
                    if (marker.Definition != region)
                    {
                        result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.WorldUnknownRegion, region.AuthoringId,
                            "scene " + scene.path + " marks region " + (marker.Definition != null ? marker.Definition.name : "none")
                            + " but the world lists it as " + region.name));
                    }

                    Transform spawn = marker.SpawnPoint;
                    baked.SpawnX = GameplayUnits.ToMillimetres(spawn.position.x);
                    baked.SpawnY = GameplayUnits.ToMillimetres(spawn.position.y);
                    baked.SpawnZ = GameplayUnits.ToMillimetres(spawn.position.z);
                    baked.SpawnYaw = Yaw(spawn);
                    Bounds bounds = marker.Bounds.WorldBounds;
                    baked.BoundsX = GameplayUnits.ToMillimetres(bounds.center.x);
                    baked.BoundsY = GameplayUnits.ToMillimetres(bounds.center.y);
                    baked.BoundsZ = GameplayUnits.ToMillimetres(bounds.center.z);
                    baked.ExtentX = GameplayUnits.ToMillimetres(bounds.extents.x);
                    baked.ExtentY = GameplayUnits.ToMillimetres(bounds.extents.y);
                    baked.ExtentZ = GameplayUnits.ToMillimetres(bounds.extents.z);
                }

                foreach (RegionPortal end in roots[r].GetComponentsInChildren<RegionPortal>(true))
                {
                    if (end.Portal == null || !portals.TryGetValue(end.Portal.AuthoringId, out BakedPortal? portal))
                    {
                        result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.PortalUnconnected, region.AuthoringId,
                            "portal end " + end.name + " in " + scene.path + " names no portal of the world"));
                        continue;
                    }

                    Vector3 arrival = end.ArrivalPosition;
                    var pose = new BakedPose(
                        GameplayUnits.ToMillimetres(arrival.x),
                        GameplayUnits.ToMillimetres(arrival.y),
                        GameplayUnits.ToMillimetres(arrival.z),
                        GameplayUnits.NormalizeYaw(GameplayUnits.DegreesToMilliradians(end.ArrivalYaw)));
                    if (string.Equals(portal.RegionA, region.AuthoringId, StringComparison.Ordinal))
                    {
                        portal.ArrivalA = pose;
                        portal.HasEndA = true;
                    }
                    else if (string.Equals(portal.RegionB, region.AuthoringId, StringComparison.Ordinal))
                    {
                        portal.ArrivalB = pose;
                        portal.HasEndB = true;
                    }
                    else
                    {
                        result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.PortalUnconnected, end.Portal.AuthoringId,
                            "portal " + end.Portal.name + " has an end in " + region.name + ", which it does not connect"));
                    }
                }

                foreach (AuthoredEntity entity in roots[r].GetComponentsInChildren<AuthoredEntity>(true))
                {
                    world.Entities.Add(ReadEntity(entity, region, result));
                }
            }

            if (regionMarkers != 1)
            {
                result.Diagnostics.Add(new GameplayDiagnostic(
                    regionMarkers == 0 ? GameplayDiagnosticCodes.RegionMissingBounds : GameplayDiagnosticCodes.RegionMultipleInScene,
                    region.AuthoringId,
                    "scene " + scene.path + " holds " + regionMarkers + " AuthoredRegion markers; exactly one is required"));
            }
        }

        private static BakedEntity ReadEntity(AuthoredEntity entity, RegionDefinition region, WorldReadResult result)
        {
            Transform transform = entity.transform;
            var baked = new BakedEntity
            {
                AuthoringId = entity.AuthoringId,
                Name = entity.gameObject.name,
                RegionId = region.AuthoringId,
                DefinitionId = entity.Definition != null ? entity.Definition.AuthoringId : string.Empty,
                X = GameplayUnits.ToMillimetres(transform.position.x),
                Y = GameplayUnits.ToMillimetres(transform.position.y),
                Z = GameplayUnits.ToMillimetres(transform.position.z),
                Yaw = Yaw(transform),
                Variant = entity.Variant,
            };

            EntityDefinition? definition = entity.Definition;
            if (definition != null)
            {
                AddDefinition(definition, result);
                baked.ScaleMilli = definition.DefaultScaleMilli;
                baked.Visible = definition.StartsVisible;
                baked.Alive = definition.StartsAlive;
            }

            IAuthoredRegion? owner = entity.Region;
            if (owner == null || !owner.ContainsPoint(transform.position.x, transform.position.y, transform.position.z))
            {
                result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.EntityOutsideRegion, entity.AuthoringId,
                    "entity " + entity.name + " lies outside the bounds of region " + region.name));
            }

            for (int i = 0; i < entity.Overrides.Entries.Count; i++)
            {
                OverrideEntry entry = entity.Overrides.Entries[i];
                baked.Overrides[entry.Field] = entry.Value;
                if (!OverrideFields.IsValidValue(entry.Field, entry.Value, out string error))
                {
                    result.Diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.EntityUnknownOverride, entity.AuthoringId,
                        "entity " + entity.name + ": " + error));
                    continue;
                }

                switch (entry.Field)
                {
                    case OverrideSet.ScaleMilli:
                        if (OverrideFields.TryInt(entry.Value, out int scale))
                        {
                            baked.ScaleMilli = scale;
                        }

                        break;
                    case OverrideSet.Visible:
                        baked.Visible = entry.Value == "true";
                        break;
                    case OverrideSet.Alive:
                        baked.Alive = entry.Value == "true";
                        break;
                }
            }

            return baked;
        }

        private static void AddDefinition(EntityDefinition definition, WorldReadResult result)
        {
            if (result.Definitions.ContainsKey(definition.AuthoringId) || !AuthoringIds.IsValid(definition.AuthoringId))
            {
                return;
            }

            result.Definitions.Add(definition.AuthoringId, definition);
            var baked = new BakedDefinition
            {
                AuthoringId = definition.AuthoringId,
                Name = definition.name,
                ContentHash = DefinitionCanonicalizer.ContentStamp(definition),
                VariantCount = definition.VariantCount,
            };
            for (int i = 0; i < definition.Variants.Count; i++)
            {
                baked.VariantIds.Add(definition.Variants[i] != null ? definition.Variants[i].AuthoringId : string.Empty);
            }

            for (int i = 0; i < definition.OverridableFields.Count; i++)
            {
                baked.OverridableFields.Add(definition.OverridableFields[i]);
            }

            result.World.Definitions.Add(baked);
        }

        private static int Yaw(Transform transform) =>
            GameplayUnits.NormalizeYaw(GameplayUnits.DegreesToMilliradians(transform.eulerAngles.y));
    }
}
