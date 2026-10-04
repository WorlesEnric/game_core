// GameCore.Studio.Edit - value checks for field-addressed generic tools (docs/studio/packets/P0.3 "left open": the
// model validator has no value checks for set/assign whose argument names an object field dynamically).
//
// Convention (decided here): `set` takes { "field": name, "value": v } or { "fields": { name: v, ... } }. The catalog
// declares `value` with the type name "fieldValue": its real type is the target field's FieldSpec. The engine checks
// each value against that FieldSpec with exactly the model validator's rules, by validating a one-argument synthetic
// change set, so inspectors, agents and the engine agree on what is valid.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    public static class FieldValueChecker
    {
        /// <summary>Catalog type of a field-addressed value argument.</summary>
        public const string FieldValueType = "fieldValue";

        private const string CheckToolId = "studio.fieldValueCheck";

        private static readonly string CheckChangeSetId = "cs_" + new string('0', 26);

        /// <summary>Problems of <paramref name="value"/> against <paramref name="spec"/>; empty when valid.</summary>
        public static IReadOnlyList<string> Check(ValueSpec spec, JToken? value, SemanticIndex? index = null)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            List<string> problems = new List<string>();
            if (value == null || value.Type == JTokenType.Null)
            {
                if (spec.Required && !ValueTypes.IsArray(spec.Type))
                {
                    problems.Add("'" + spec.Name + "' is required and cannot be cleared");
                }

                return problems;
            }

            ArgSpec arg = new ArgSpec("value", spec.Type, false, spec.Unit, spec.Min, spec.Max, spec.Step, spec.Category, spec.Doc, spec.EnumValues);
            ToolEntry tool = new ToolEntry(CheckToolId, ToolTier.Configure, RuntimeApply.Live, false, new[] { arg });
            ToolCatalog catalog = new ToolCatalog(Array.Empty<ObjectTypeEntry>(), new[] { tool });
            ChangeSet probe = new ChangeSet(
                CheckChangeSetId,
                ChangeSet.SchemaId,
                new Intent("field value check", IntentOrigin.Manual),
                new[] { new Operation("check", CheckToolId, null, new JObject { ["value"] = value.DeepClone() }) });
            ChangeSetValidator validator = new ChangeSetValidator(catalog, index, new ChangeSetValidationOptions { CheckStamps = false, RequireTargetsInIndex = false });
            string prefix = "Argument 'value' of '" + CheckToolId + "': ";
            foreach (Diagnostic diagnostic in validator.Validate(probe))
            {
                if (!string.Equals(diagnostic.Code, DiagnosticCodes.InvalidArgs, StringComparison.Ordinal))
                {
                    continue;
                }

                string message = diagnostic.Message.StartsWith(prefix, StringComparison.Ordinal) ? diagnostic.Message.Substring(prefix.Length) : diagnostic.Message;
                problems.Add("'" + spec.Name + "': " + message.TrimEnd('.'));
            }

            return problems;
        }
    }
}
