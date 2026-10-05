// GameCore.Gameplay.Save.Editor - the save schema validator (P1.7b, B5): stable codes over SaveSchemaDefinition.Validate.
#nullable enable
using System.Collections.Generic;
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

            return diagnostics;
        }
    }
}
