// GameCore.Gameplay.Save.Editor - the save schema validator (P1.7b, B5): stable codes over SaveSchemaDefinition.Validate,
// plus the check that no two distinct owner/slot/schema names derive the same kernel key (StableNameKeyDerivation), which
// would make two slots share one checkpoint row.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Gameplay.Contracts;

namespace GameCore.Gameplay.Save
{
    /// <summary>Validates a save schema: its authoring id (GP-SAV-001) and its names, versions and migration chain (GP-SAV-002).</summary>
    [AuthorValidator("save.validator", Codes = new[]
    {
        AuthoringHardeningCodes.SaveSchemaMissingId,
        AuthoringHardeningCodes.SaveSchemaInvalid,
    })]
    public static class SaveValidator
    {
        public static IReadOnlyList<GameplayDiagnostic> Validate(SaveSchemaDefinition schema)
        {
            var diagnostics = new List<GameplayDiagnostic>();
            if (schema == null)
            {
                return diagnostics;
            }

            if (!AuthoringIds.IsValid(schema.AuthoringId))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.SaveSchemaMissingId, schema.name, schema.name + " has no authoring id"));
            }

            SaveSchemaValidation validation = schema.Validate();
            for (int i = 0; i < validation.Problems.Count; i++)
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.SaveSchemaInvalid, schema.AuthoringId, validation.Problems[i]));
            }

            var derived = new Dictionary<Id128, string>();
            for (int i = 0; i < schema.slotSchemas.Count; i++)
            {
                SaveSlotSchemaEntry entry = schema.slotSchemas[i];
                CheckDerivedKey(schema, derived, entry.owner, diagnostics);
                CheckDerivedKey(schema, derived, entry.slot, diagnostics);
                CheckDerivedKey(schema, derived, entry.schema, diagnostics);
            }

            return diagnostics;
        }

        private static void CheckDerivedKey(SaveSchemaDefinition schema, Dictionary<Id128, string> derived, string name, List<GameplayDiagnostic> diagnostics)
        {
            if (string.IsNullOrEmpty(name))
            {
                return;
            }

            Id128 key = StableNameKeyDerivation.Derive(name);
            if (derived.TryGetValue(key, out string? other) && !string.Equals(other, name, StringComparison.Ordinal))
            {
                diagnostics.Add(new GameplayDiagnostic(AuthoringHardeningCodes.SaveSchemaInvalid, schema.AuthoringId,
                    "'" + name + "' and '" + other + "' derive the same kernel key"));
                return;
            }

            derived[key] = name;
        }
    }
}
