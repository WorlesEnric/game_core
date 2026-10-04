// GameCore.Gameplay.Entities.Editor - entity validators with stable codes (P1.1).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Gameplay.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameCore.Gameplay.Entities.Editor
{
    /// <summary>Validates placed entities, their scenes and entity definitions.</summary>
    [AuthorValidator("entity.validator", Codes = new[]
    {
        GameplayDiagnosticCodes.MissingAuthoringId,
        GameplayDiagnosticCodes.InvalidAuthoringId,
        GameplayDiagnosticCodes.DuplicateAuthoringId,
        GameplayDiagnosticCodes.EntityMissingDefinition,
        GameplayDiagnosticCodes.EntityOutsideRegion,
        GameplayDiagnosticCodes.EntityVariantOutOfRange,
        GameplayDiagnosticCodes.EntityUnknownOverride,
        GameplayDiagnosticCodes.DefinitionMissingPrefab,
    })]
    public static class EntityValidator
    {
        /// <summary>Problems of one placed entity (not including cross-entity duplicates; see <see cref="ValidateScene"/>).</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(AuthoredEntity entity)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (entity == null)
            {
                return diagnostics;
            }

            string subject = string.IsNullOrEmpty(entity.AuthoringId) ? entity.name : entity.AuthoringId;
            if (string.IsNullOrEmpty(entity.AuthoringId))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.MissingAuthoringId, subject, entity.name + " has no authoring id"));
            }
            else if (!AuthoringIds.IsValid(entity.AuthoringId))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.InvalidAuthoringId, subject, entity.name + " has a non-canonical authoring id"));
            }

            EntityDefinition? definition = entity.Definition;
            if (definition == null)
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.EntityMissingDefinition, subject, entity.name + " has no definition"));
            }
            else
            {
                diagnostics.AddRange(Validate(definition));
                if (entity.Variant < 0 || entity.Variant >= definition.VariantCount)
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityVariantOutOfRange, subject,
                        entity.name + " selects variant " + entity.Variant + " of " + definition.VariantCount));
                }

                for (int i = 0; i < entity.Overrides.Entries.Count; i++)
                {
                    OverrideEntry entry = entity.Overrides.Entries[i];
                    if (!definition.IsOverridable(entry.Field) || !OverrideFields.IsValidValue(entry.Field, entry.Value, out string _))
                    {
                        diagnostics.Add(new GameplayDiagnostic(
                            GameplayDiagnosticCodes.EntityUnknownOverride, subject,
                            entity.name + " overrides '" + entry.Field + "' with '" + entry.Value + "'"));
                    }
                }
            }

            IAuthoredRegion? region = entity.Region;
            Vector3 position = entity.transform.position;
            if (region == null || !region.ContainsPoint(position.x, position.y, position.z))
            {
                diagnostics.Add(new GameplayDiagnostic(
                    GameplayDiagnosticCodes.EntityOutsideRegion, subject,
                    entity.name + (region == null ? " is in a scene without an authored region" : " lies outside its region's bounds")));
            }

            return diagnostics;
        }

        /// <summary>Problems of one entity definition.</summary>
        public static IReadOnlyList<GameplayDiagnostic> Validate(EntityDefinition definition)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (definition == null)
            {
                return diagnostics;
            }

            if (!AuthoringIds.IsValid(definition.AuthoringId))
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.MissingAuthoringId, definition.name, "definition " + definition.name + " has no valid authoring id"));
            }

            if (definition.Prefab == null)
            {
                diagnostics.Add(new GameplayDiagnostic(GameplayDiagnosticCodes.DefinitionMissingPrefab, definition.AuthoringId, "definition " + definition.name + " has no prefab"));
            }

            for (int i = 0; i < definition.Variants.Count; i++)
            {
                VariantDefinition variant = definition.Variants[i];
                if (variant == null || !AuthoringIds.IsValid(variant.AuthoringId))
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.MissingAuthoringId, definition.AuthoringId,
                        "variant " + (i + 1) + " of " + definition.name + " is missing or has no authoring id"));
                }
            }

            for (int i = 0; i < definition.OverridableFields.Count; i++)
            {
                if (!OverrideFields.IsKnown(definition.OverridableFields[i]))
                {
                    diagnostics.Add(new GameplayDiagnostic(
                        GameplayDiagnosticCodes.EntityUnknownOverride, definition.AuthoringId,
                        "definition " + definition.name + " declares unknown overridable field '" + definition.OverridableFields[i] + "'"));
                }
            }

            return diagnostics;
        }

        /// <summary>Problems of every placed entity in a scene, including duplicate authoring ids.</summary>
        public static IReadOnlyList<GameplayDiagnostic> ValidateScene(Scene scene)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return diagnostics;
            }

            var seen = new Dictionary<string, string>(StringComparer.Ordinal);
            GameObject[] roots = scene.GetRootGameObjects();
            for (int r = 0; r < roots.Length; r++)
            {
                AuthoredEntity[] entities = roots[r].GetComponentsInChildren<AuthoredEntity>(true);
                for (int i = 0; i < entities.Length; i++)
                {
                    AuthoredEntity entity = entities[i];
                    diagnostics.AddRange(Validate(entity));
                    if (!AuthoringIds.IsValid(entity.AuthoringId))
                    {
                        continue;
                    }

                    if (seen.TryGetValue(entity.AuthoringId, out string? other))
                    {
                        diagnostics.Add(new GameplayDiagnostic(
                            GameplayDiagnosticCodes.DuplicateAuthoringId, entity.AuthoringId,
                            entity.name + " shares its authoring id with " + other + "; use entity.duplicate to copy entities"));
                        continue;
                    }

                    seen.Add(entity.AuthoringId, entity.name);
                }
            }

            return diagnostics;
        }
    }
}
