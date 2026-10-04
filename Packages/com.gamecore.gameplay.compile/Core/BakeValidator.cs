// GameCore.Gameplay.Compile - validation of a baked world before anything is written (P1.1).
//
// A bake that fails validation writes nothing: the previous manifest, report and catalog stay in place, and every
// problem is reported with its stable GP-* code and the authoring id it is about.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;

namespace GameCore.Gameplay.Compile
{
    /// <summary>Validates a <see cref="BakedWorld"/>.</summary>
    public static class BakeValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(BakedWorld world)
        {
            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            var diagnostics = new List<GameplayDiagnostic>();
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            var keys = new Dictionary<int, string>();

            CheckId(world.WorldId, "world " + world.WorldName, ids, diagnostics);
            for (int i = 0; i < world.Definitions.Count; i++)
            {
                BakedDefinition definition = world.Definitions[i];
                CheckId(definition.AuthoringId, "definition " + definition.Name, ids, diagnostics);
                for (int v = 0; v < definition.VariantIds.Count; v++)
                {
                    CheckId(definition.VariantIds[v], "variant " + (v + 1) + " of " + definition.Name, ids, diagnostics);
                }

                if (definition.VariantCount < 1)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityVariantOutOfRange, definition.AuthoringId,
                        "definition " + definition.Name + " declares no base variant"));
                }

                if (definition.ContentHash.Length != 64)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.BakeStale, definition.AuthoringId,
                        "definition " + definition.Name + " has no content hash"));
                }
            }

            for (int i = 0; i < world.Regions.Count; i++)
            {
                BakedRegion region = world.Regions[i];
                if (CheckId(region.AuthoringId, "region " + region.Name, ids, diagnostics))
                {
                    CheckKey(region.AuthoringId, "region " + region.Name, keys, diagnostics);
                }

                if (string.IsNullOrEmpty(region.ScenePath))
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.RegionMissingScene, region.AuthoringId, "region " + region.Name + " has no scene"));
                }

                if (region.ExtentX <= 0 || region.ExtentY <= 0 || region.ExtentZ <= 0)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.RegionMissingBounds, region.AuthoringId, "region " + region.Name + " has empty bounds"));
                }
            }

            for (int i = 0; i < world.Portals.Count; i++)
            {
                BakedPortal portal = world.Portals[i];
                if (CheckId(portal.AuthoringId, "portal " + portal.Name, ids, diagnostics))
                {
                    CheckKey(portal.AuthoringId, "portal " + portal.Name, keys, diagnostics);
                }

                if (world.FindRegion(portal.RegionA) == null || world.FindRegion(portal.RegionB) == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.PortalUnconnected, portal.AuthoringId,
                        "portal " + portal.Name + " does not connect two baked regions"));
                }
                else if (string.Equals(portal.RegionA, portal.RegionB, StringComparison.Ordinal))
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.PortalTargetsOwnRegion, portal.AuthoringId,
                        "portal " + portal.Name + " connects a region to itself"));
                }
                else if (!portal.HasEndA || !portal.HasEndB)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.PortalUnconnected, portal.AuthoringId,
                        "portal " + portal.Name + " has no RegionPortal in " + (portal.HasEndA ? "region B" : "region A")));
                }
            }

            if (world.FindRegion(world.StartRegionId) == null)
            {
                diagnostics.Add(new GameplayDiagnostic(
                    GameplayDiagnosticCodes.WorldMissingStartRegion, world.WorldId,
                    "world " + world.WorldName + " names no baked start region"));
            }

            for (int i = 0; i < world.Entities.Count; i++)
            {
                BakedEntity entity = world.Entities[i];
                CheckId(entity.AuthoringId, "entity " + entity.Name, ids, diagnostics);
                BakedDefinition? definition = world.FindDefinition(entity.DefinitionId);
                if (definition == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityMissingDefinition, entity.AuthoringId,
                        "entity " + entity.Name + " has no baked definition"));
                }
                else if (entity.Variant < 0 || entity.Variant >= definition.VariantCount)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityVariantOutOfRange, entity.AuthoringId,
                        "entity " + entity.Name + " selects variant " + entity.Variant + " of " + definition.VariantCount));
                }
                else
                {
                    foreach (string field in entity.Overrides.Keys)
                    {
                        if (!definition.OverridableFields.Contains(field))
                        {
                            diagnostics.Add(new GameplayDiagnostic(
                                GameplayDiagnosticCodes.EntityUnknownOverride, entity.AuthoringId,
                                "entity " + entity.Name + " overrides undeclared field '" + field + "'"));
                        }
                    }
                }

                if (world.FindRegion(entity.RegionId) == null)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityOutsideRegion, entity.AuthoringId,
                        "entity " + entity.Name + " is not inside a baked region"));
                }

                if (entity.ScaleMilli < 1 || entity.ScaleMilli > 100000)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityScaleOutOfRange, entity.AuthoringId,
                        "entity " + entity.Name + " has scale " + entity.ScaleMilli + " milli"));
                }
            }

            if (!string.IsNullOrEmpty(world.FocusEntityId) && !ContainsEntity(world, world.FocusEntityId))
            {
                diagnostics.Add(new GameplayDiagnostic(
                    GameplayDiagnosticCodes.EntityMissingDefinition, world.FocusEntityId,
                    "the world's focus entity is not a baked entity"));
            }

            return diagnostics;
        }

        private static bool ContainsEntity(BakedWorld world, string id)
        {
            for (int i = 0; i < world.Entities.Count; i++)
            {
                if (string.Equals(world.Entities[i].AuthoringId, id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool CheckId(string id, string what, Dictionary<string, string> ids, List<GameplayDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(id))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.MissingAuthoringId, what, what + " has no authoring id"));
                return false;
            }

            if (!AuthoringIds.IsValid(id))
            {
                diagnostics.Add(new GameplayDiagnostic(
                    GameplayDiagnosticCodes.InvalidAuthoringId, id, what + " has a non-canonical authoring id"));
                return false;
            }

            if (ids.TryGetValue(id, out string? other))
            {
                diagnostics.Add(new GameplayDiagnostic(
                    GameplayDiagnosticCodes.DuplicateAuthoringId, id, what + " shares its authoring id with " + other));
                return false;
            }

            ids.Add(id, what);
            return true;
        }

        private static void CheckKey(string id, string what, Dictionary<int, string> keys, List<GameplayDiagnostic> diagnostics)
        {
            int key = AuthoringIds.StableKey(id);
            if (keys.TryGetValue(key, out string? other))
            {
                diagnostics.Add(new GameplayDiagnostic(
                    GameplayDiagnosticCodes.StableKeyCollision, id, what + " derives the same stable key as " + other));
                return;
            }

            keys.Add(key, what);
        }
    }
}
