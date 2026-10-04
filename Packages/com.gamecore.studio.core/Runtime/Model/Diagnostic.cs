// GameCore.Studio.Model - diagnostics (docs/studio/03-authoring-contracts.md s9).
// Every refusal and validation failure is {code, message, hint, where: AuthoringRef|opId}, with the same code
// whether raised by an inspector, a validator, the kernel bridge or an agent candidate.
#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model
{
    /// <summary>One refusal or validation failure (03 s9). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Diagnostic
    {
        [JsonConstructor]
        public Diagnostic(string code, string message, string? hint = null, DiagnosticWhere? where = null)
        {
            Code = ModelLists.NotEmpty(code, nameof(code));
            Message = ModelLists.NotNull(message, nameof(message));
            Hint = hint;
            Where = where;
        }

        /// <summary>A registered code (<see cref="DiagnosticCodes"/>).</summary>
        [JsonProperty("code", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Code { get; }

        [JsonProperty("message", Required = Required.Always)]
        public string Message { get; }

        [JsonProperty("hint", NullValueHandling = NullValueHandling.Ignore)]
        public string? Hint { get; }

        /// <summary>The authored thing or the operation the diagnostic is about; absent for change-set-wide findings.</summary>
        [JsonProperty("where", NullValueHandling = NullValueHandling.Ignore)]
        public DiagnosticWhere? Where { get; }

        public static Diagnostic AtOperation(string code, string opId, string message, string? hint = null) =>
            new Diagnostic(code, message, hint, DiagnosticWhere.Operation(opId));

        public static Diagnostic AtRef(string code, AuthoringRef target, string message, string? hint = null) =>
            new Diagnostic(code, message, hint, DiagnosticWhere.At(target));

        public override string ToString() =>
            Code + ": " + Message + (Where == null ? string.Empty : " @ " + Where.ToString());
    }

    /// <summary>
    /// The location of a diagnostic: either an operation id (JSON string) or an <see cref="AuthoringRef"/> (JSON object).
    /// Exactly one of <see cref="OpId"/> and <see cref="Ref"/> is set. Immutable.
    /// </summary>
    [JsonConverter(typeof(DiagnosticWhereConverter))]
    public sealed class DiagnosticWhere
    {
        private DiagnosticWhere(string? opId, AuthoringRef? @ref)
        {
            OpId = opId;
            Ref = @ref;
        }

        public string? OpId { get; }

        public AuthoringRef? Ref { get; }

        public static DiagnosticWhere Operation(string opId) => new DiagnosticWhere(ModelLists.NotEmpty(opId, nameof(opId)), null);

        public static DiagnosticWhere At(AuthoringRef target) => new DiagnosticWhere(null, ModelLists.NotNull(target, nameof(target)));

        public override string ToString() => OpId != null ? "op " + OpId : Ref!.ToString();
    }

    /// <summary>Reads and writes <see cref="DiagnosticWhere"/> as the 03 s9 union <c>AuthoringRef | opId</c>.</summary>
    public sealed class DiagnosticWhereConverter : JsonConverter<DiagnosticWhere>
    {
        public override void WriteJson(JsonWriter writer, DiagnosticWhere? value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            if (value.OpId != null)
            {
                writer.WriteValue(value.OpId);
                return;
            }

            serializer.Serialize(writer, value.Ref);
        }

        public override DiagnosticWhere? ReadJson(JsonReader reader, Type objectType, DiagnosticWhere? existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonToken.String)
            {
                string? opId = (string?)reader.Value;
                if (string.IsNullOrEmpty(opId))
                {
                    throw new JsonSerializationException("'where' as an operation id must be a non-empty string.");
                }

                return DiagnosticWhere.Operation(opId!);
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                AuthoringRef? target = serializer.Deserialize<AuthoringRef>(reader);
                if (target == null)
                {
                    throw new JsonSerializationException("'where' as an AuthoringRef could not be read.");
                }

                return DiagnosticWhere.At(target);
            }

            throw new JsonSerializationException("'where' must be an operation id string or an AuthoringRef object.");
        }
    }

    /// <summary>
    /// The registry of diagnostic codes (03 s9). A code is a stable PascalCase string; the same code is used by every
    /// layer that can raise it. Plugin validators declare the codes they raise in <see cref="ValidatorRef.Codes"/>.
    /// </summary>
    public static class DiagnosticCodes
    {
        /// <summary>The target's content stamp changed since planning (or the target no longer exists).</summary>
        public const string StaleTarget = "StaleTarget";
        /// <summary>A read dependency or concurrently edited object differs from what the plan expected (03 s7).</summary>
        public const string Conflict = "Conflict";
        /// <summary>The operation names a tool the catalog does not contain.</summary>
        public const string UnknownTool = "UnknownTool";
        /// <summary>An argument or target is missing, unknown, of the wrong type, or out of range.</summary>
        public const string InvalidArgs = "InvalidArgs";
        /// <summary>A tool prerequisite (type or capability) is not present.</summary>
        public const string MissingPrerequisite = "MissingPrerequisite";
        /// <summary>The tool or object type does not allow the chosen edit scope.</summary>
        public const string ScopeNotAllowed = "ScopeNotAllowed";
        /// <summary>A tool validator or validation scenario failed.</summary>
        public const string ValidationFailed = "ValidationFailed";
        /// <summary>A layer refused the operation (e.g. a kernel lane refusal) without it being malformed.</summary>
        public const string Refused = "Refused";
        /// <summary>A change set is structurally invalid (ids, schema, dependencies, artifacts, requirements).</summary>
        public const string CandidateInvalid = "CandidateInvalid";
        /// <summary>The selection or index slice the request was made from is out of date.</summary>
        public const string StaleContext = "StaleContext";
        /// <summary>Staging (preview objects, dry-run, compile in a staging slot) failed.</summary>
        public const string StageFailed = "StageFailed";
        /// <summary>The companion's durable ledger disagrees with the request (e.g. same id, different content).</summary>
        public const string LedgerConflict = "LedgerConflict";
        /// <summary>A required service, provider or credential is not configured.</summary>
        public const string NotConfigured = "NotConfigured";
        /// <summary>A provider or task outcome is unknown (lost reply); it is surfaced, never auto-repeated.</summary>
        public const string OutcomeUnknown = "OutcomeUnknown";
        /// <summary>The capability is blocked on an external prerequisite (e.g. a missing provider credential).</summary>
        public const string Blocked = "Blocked";

        /// <summary>Every registered code, in registry order.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            StaleTarget, Conflict, UnknownTool, InvalidArgs, MissingPrerequisite, ScopeNotAllowed, ValidationFailed,
            Refused, CandidateInvalid, StaleContext, StageFailed, LedgerConflict, NotConfigured, OutcomeUnknown, Blocked,
        };

        /// <summary>True when <paramref name="code"/> is a registered code.</summary>
        public static bool IsRegistered(string? code)
        {
            if (code == null)
            {
                return false;
            }

            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i], code, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A JSON array of the registry (for documentation generators and the companion mirror).</summary>
        public static JArray ToJson()
        {
            return new JArray(All);
        }
    }
}
