// GameCore.Studio.Model - tool catalog (docs/studio/03-authoring-contracts.md s4/s5, schema tool-catalog.schema.json).
// The catalog is the agents' only view of what the project can do: supported object types and their fields,
// operations with argument specs, prerequisites, allowed scopes, runtime applicability and validators.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model
{
    /// <summary>Supported objects and operations exported by one plugin, or merged for a project (03 s4). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ToolCatalog
    {
        /// <summary>Schema identifier of this document shape.</summary>
        public const string SchemaId = "gamecore.studio.toolcatalog/1";

        [JsonConstructor]
        public ToolCatalog(
            string schema,
            IReadOnlyList<ObjectTypeEntry> objectTypes,
            IReadOnlyList<ToolEntry> tools,
            string? plugin = null,
            string? revision = null)
        {
            Schema = ModelLists.NotEmpty(schema, nameof(schema));
            ObjectTypes = ModelLists.Required(objectTypes, nameof(objectTypes));
            Tools = ModelLists.Required(tools, nameof(tools));
            Plugin = plugin;
            Revision = revision;
        }

        /// <summary>A catalog with the current schema id and no revision (see <see cref="WithRevision"/>).</summary>
        public ToolCatalog(IReadOnlyList<ObjectTypeEntry> objectTypes, IReadOnlyList<ToolEntry> tools, string? plugin = null)
            : this(SchemaId, objectTypes, tools, plugin)
        {
        }

        [JsonProperty("schema", Required = Required.Always)]
        [SchemaHint(Const = SchemaId)]
        public string Schema { get; }

        /// <summary>
        /// Content revision: lowercase sha256 hex of the catalog's canonical JSON with this member omitted
        /// (<see cref="ComputeRevision"/>). Minted by the tool registry (<see cref="Merge"/>,
        /// <see cref="ToolCatalogBuilder.Build"/>); requests carry it as <c>toolCatalogRevision</c> and a candidate
        /// built against another revision is StaleContext (03 s9).
        /// </summary>
        [JsonProperty("revision", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(Pattern = StudioPatterns.Sha256Hex)]
        public string? Revision { get; }

        /// <summary>
        /// The sha256 (lowercase hex) of <see cref="StudioJson.Canonical"/> of this catalog without <c>revision</c>:
        /// keys sorted ordinally at every level, no whitespace, numbers and strings as Newtonsoft writes them, UTF-8.
        /// </summary>
        public string ComputeRevision()
        {
            JObject content = (JObject)StudioJson.ToToken(this);
            content.Remove("revision");
            return ContentStamp.Sha256Hex(Encoding.UTF8.GetBytes(StudioJson.Canonical(content)));
        }

        /// <summary>A copy carrying its computed <see cref="Revision"/>.</summary>
        public ToolCatalog WithRevision() => new ToolCatalog(Schema, ObjectTypes, Tools, Plugin, ComputeRevision());

        /// <summary>True when <see cref="Revision"/> is present and equals <see cref="ComputeRevision"/>.</summary>
        public bool HasValidRevision() => Revision != null && string.Equals(Revision, ComputeRevision(), StringComparison.Ordinal);

        /// <summary>Exporting plugin (package name) or null for a merged project catalog.</summary>
        [JsonProperty("plugin", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Plugin { get; }

        [JsonProperty("objectTypes", Required = Required.Always)]
        public IReadOnlyList<ObjectTypeEntry> ObjectTypes { get; }

        [JsonProperty("tools", Required = Required.Always)]
        public IReadOnlyList<ToolEntry> Tools { get; }

        /// <summary>The tool with <paramref name="toolId"/>, or null.</summary>
        public ToolEntry? FindTool(string toolId)
        {
            for (int i = 0; i < Tools.Count; i++)
            {
                if (string.Equals(Tools[i].Id, toolId, StringComparison.Ordinal))
                {
                    return Tools[i];
                }
            }

            return null;
        }

        /// <summary>The object type with <paramref name="typeId"/>, or null.</summary>
        public ObjectTypeEntry? FindObjectType(string typeId)
        {
            for (int i = 0; i < ObjectTypes.Count; i++)
            {
                if (string.Equals(ObjectTypes[i].TypeId, typeId, StringComparison.Ordinal))
                {
                    return ObjectTypes[i];
                }
            }

            return null;
        }

        /// <summary>
        /// Merges plugin catalogs into one project catalog (plugin = null, revision minted), sorted by id. A tool id or
        /// type id declared by two catalogs is a conflict and throws: two plugins may not claim the same tool.
        /// </summary>
        public static ToolCatalog Merge(IEnumerable<ToolCatalog> catalogs)
        {
            if (catalogs == null)
            {
                throw new ArgumentNullException(nameof(catalogs));
            }

            SortedDictionary<string, ObjectTypeEntry> types = new SortedDictionary<string, ObjectTypeEntry>(StringComparer.Ordinal);
            SortedDictionary<string, ToolEntry> tools = new SortedDictionary<string, ToolEntry>(StringComparer.Ordinal);
            foreach (ToolCatalog catalog in catalogs)
            {
                if (!string.Equals(catalog.Schema, SchemaId, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException("Cannot merge a catalog with schema '" + catalog.Schema + "'.");
                }

                foreach (ObjectTypeEntry type in catalog.ObjectTypes)
                {
                    if (types.ContainsKey(type.TypeId))
                    {
                        throw new InvalidOperationException("Object type '" + type.TypeId + "' is declared by more than one catalog.");
                    }

                    types.Add(type.TypeId, type);
                }

                foreach (ToolEntry tool in catalog.Tools)
                {
                    if (tools.ContainsKey(tool.Id))
                    {
                        throw new InvalidOperationException("Tool '" + tool.Id + "' is declared by more than one catalog.");
                    }

                    tools.Add(tool.Id, tool);
                }
            }

            return new ToolCatalog(new List<ObjectTypeEntry>(types.Values), new List<ToolEntry>(tools.Values)).WithRevision();
        }
    }

    /// <summary>An authorable object type (from <c>[Authorable]</c>) and its editable fields. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ObjectTypeEntry
    {
        [JsonConstructor]
        public ObjectTypeEntry(
            string typeId,
            RuntimeApply runtimeApply,
            IReadOnlyList<FieldSpec> fields,
            string? displayName = null,
            string? doc = null,
            IReadOnlyList<AuthorScope>? scopes = null)
        {
            TypeId = ModelLists.NotEmpty(typeId, nameof(typeId));
            RuntimeApply = runtimeApply;
            Fields = ModelLists.Required(fields, nameof(fields));
            DisplayName = displayName;
            Doc = doc;
            Scopes = ModelLists.Optional(scopes, nameof(scopes));
        }

        [JsonProperty("typeId", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string TypeId { get; }

        [JsonProperty("displayName", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? DisplayName { get; }

        [JsonProperty("doc", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Doc { get; }

        /// <summary>Edit scopes the type supports; null means unrestricted.</summary>
        [JsonProperty("scopes", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<AuthorScope>? Scopes { get; }

        /// <summary>How a field change of this type reaches a running world.</summary>
        [JsonProperty("runtimeApply", Required = Required.Always)]
        public RuntimeApply RuntimeApply { get; }

        [JsonProperty("fields", Required = Required.Always)]
        public IReadOnlyList<FieldSpec> Fields { get; }
    }

    /// <summary>
    /// The value constraints shared by object fields and tool arguments. <see cref="Type"/> uses the vocabulary of
    /// <see cref="ValueTypes"/>; a trailing <c>[]</c> means an array of that type.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    public abstract class ValueSpec
    {
        protected ValueSpec(
            string name,
            string type,
            bool required,
            string? unit,
            double? min,
            double? max,
            double? step,
            string? category,
            string? doc,
            IReadOnlyList<string>? enumValues)
        {
            Name = ModelLists.NotEmpty(name, nameof(name));
            Type = ModelLists.NotEmpty(type, nameof(type));
            Required = required;
            Unit = unit;
            Min = min;
            Max = max;
            Step = step;
            Category = category;
            Doc = doc;
            EnumValues = ModelLists.Optional(enumValues, nameof(enumValues));
        }

        [JsonProperty("name", Required = Newtonsoft.Json.Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Name { get; }

        [JsonProperty("type", Required = Newtonsoft.Json.Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Type { get; }

        /// <summary>A required field must be set (non-null); a required argument must be present.</summary>
        [JsonProperty("required", Required = Newtonsoft.Json.Required.Always)]
        public bool Required { get; }

        [JsonProperty("unit", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Unit { get; }

        [JsonProperty("min", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double? Min { get; }

        [JsonProperty("max", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double? Max { get; }

        [JsonProperty("step", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public double? Step { get; }

        /// <summary>Reference category for <c>ref</c> values (an authorable type id or capability, e.g. <c>dialogue.graph</c>).</summary>
        [JsonProperty("category", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Category { get; }

        [JsonProperty("doc", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Doc { get; }

        /// <summary>Accepted names for <c>enum</c> values.</summary>
        [JsonProperty("enumValues", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? EnumValues { get; }
    }

    /// <summary>An editable field of an object type (<c>[AuthorField]</c> / <c>[AuthorRef]</c>). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class FieldSpec : ValueSpec
    {
        [JsonConstructor]
        public FieldSpec(
            string name,
            string type,
            bool required,
            string? unit = null,
            double? min = null,
            double? max = null,
            double? step = null,
            string? category = null,
            string? doc = null,
            IReadOnlyList<string>? enumValues = null,
            bool? structural = null)
            : base(name, type, required, unit, min, max, step, category, doc, enumValues)
        {
            // Only true is written: a tuning field omits the member (null policy, 03 s9), so catalogs without
            // structural fields keep their canonical text and revision.
            Structural = structural == true ? true : (bool?)null;
        }

        /// <summary>
        /// True when the field shapes what the runtime builds (prefab, slot layout, variant set, kind): changing it needs
        /// a new recipe revision (a Rebuild). Absent for tuning fields.
        /// </summary>
        [JsonProperty("structural", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public bool? Structural { get; }
    }

    /// <summary>An argument of a tool (<c>[AuthorArg]</c>). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ArgSpec : ValueSpec
    {
        [JsonConstructor]
        public ArgSpec(
            string name,
            string type,
            bool required,
            string? unit = null,
            double? min = null,
            double? max = null,
            double? step = null,
            string? category = null,
            string? doc = null,
            IReadOnlyList<string>? enumValues = null)
            : base(name, type, required, unit, min, max, step, category, doc, enumValues)
        {
        }
    }

    /// <summary>One tool: an operation agents and inspectors can put into a change set (03 s5). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ToolEntry
    {
        [JsonConstructor]
        public ToolEntry(
            string id,
            ToolTier tier,
            RuntimeApply runtimeApply,
            bool targetRequired,
            IReadOnlyList<ArgSpec> args,
            string? doc = null,
            string? targetType = null,
            IReadOnlyList<AuthoringKind>? targetKinds = null,
            IReadOnlyList<AuthorScope>? scopes = null,
            IReadOnlyList<Prerequisite>? prerequisites = null,
            IReadOnlyList<ValidatorRef>? validators = null)
        {
            Id = ModelLists.NotEmpty(id, nameof(id));
            Tier = tier;
            RuntimeApply = runtimeApply;
            TargetRequired = targetRequired;
            Args = ModelLists.Required(args, nameof(args));
            Doc = doc;
            TargetType = targetType;
            TargetKinds = ModelLists.Optional(targetKinds, nameof(targetKinds));
            Scopes = ModelLists.Optional(scopes, nameof(scopes));
            Prerequisites = ModelLists.Optional(prerequisites, nameof(prerequisites));
            Validators = ModelLists.Optional(validators, nameof(validators));
        }

        /// <summary>Tool id (e.g. <c>npc.setPatrol</c>).</summary>
        [JsonProperty("id", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Id { get; }

        [JsonProperty("tier", Required = Required.Always)]
        public ToolTier Tier { get; }

        [JsonProperty("doc", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Doc { get; }

        /// <summary>Authorable type id the target must have; null means any type.</summary>
        [JsonProperty("targetType", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? TargetType { get; }

        /// <summary>Authoring kinds the target may have; null means any kind.</summary>
        [JsonProperty("targetKinds", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<AuthoringKind>? TargetKinds { get; }

        /// <summary>True when an operation must name a target; false when the tool takes none (e.g. <c>project.save</c>).</summary>
        [JsonProperty("targetRequired", Required = Required.Always)]
        public bool TargetRequired { get; }

        /// <summary>Edit scopes the tool may be applied in; null means unrestricted.</summary>
        [JsonProperty("scopes", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<AuthorScope>? Scopes { get; }

        [JsonProperty("args", Required = Required.Always)]
        public IReadOnlyList<ArgSpec> Args { get; }

        [JsonProperty("prerequisites", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<Prerequisite>? Prerequisites { get; }

        [JsonProperty("runtimeApply", Required = Required.Always)]
        public RuntimeApply RuntimeApply { get; }

        [JsonProperty("validators", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<ValidatorRef>? Validators { get; }

        /// <summary>The argument spec named <paramref name="name"/>, or null.</summary>
        public ArgSpec? FindArg(string name)
        {
            for (int i = 0; i < Args.Count; i++)
            {
                if (string.Equals(Args[i].Name, name, StringComparison.Ordinal))
                {
                    return Args[i];
                }
            }

            return null;
        }
    }

    /// <summary>A type or capability that must exist before a tool can apply (e.g. <c>world.region</c>). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Prerequisite
    {
        [JsonConstructor]
        public Prerequisite(string requires, PrerequisiteSubject on, string? doc = null)
        {
            Requires = ModelLists.NotEmpty(requires, nameof(requires));
            On = on;
            Doc = doc;
        }

        [JsonProperty("requires", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Requires { get; }

        [JsonProperty("on", Required = Required.Always)]
        public PrerequisiteSubject On { get; }

        [JsonProperty("doc", Required = Newtonsoft.Json.Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public string? Doc { get; }
    }

    /// <summary>A validator a tool runs during staging, and the diagnostic codes it can raise. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ValidatorRef
    {
        [JsonConstructor]
        public ValidatorRef(string id, IReadOnlyList<string> codes)
        {
            Id = ModelLists.NotEmpty(id, nameof(id));
            Codes = ModelLists.Required(codes, nameof(codes));
        }

        [JsonProperty("id", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Id { get; }

        [JsonProperty("codes", Required = Required.Always)]
        public IReadOnlyList<string> Codes { get; }
    }

    /// <summary>
    /// The value-type vocabulary of <see cref="ValueSpec.Type"/> and <see cref="IndexField.Type"/>. A trailing
    /// <see cref="ArraySuffix"/> denotes an array of the element type (e.g. <c>vector3[]</c>).
    /// </summary>
    public static class ValueTypes
    {
        public const string Bool = "bool";
        public const string Int = "int";
        public const string Float = "float";
        public const string String = "string";
        public const string Enum = "enum";
        public const string Vector2 = "vector2";
        public const string Vector3 = "vector3";
        public const string Vector4 = "vector4";
        public const string Quaternion = "quaternion";
        public const string Color = "color";
        /// <summary>A reference: an AuthoringRef object, an authoring id, or a DefinitionRef <c>name@revision</c>.</summary>
        public const string Ref = "ref";
        /// <summary>A retained artifact: <c>{ "artifact": "sha256:..." }</c>.</summary>
        public const string Artifact = "artifact";
        /// <summary>A free-form JSON object.</summary>
        public const string Object = "object";
        public const string ArraySuffix = "[]";

        /// <summary>Every scalar type name, in documentation order.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            Bool, Int, Float, String, Enum, Vector2, Vector3, Vector4, Quaternion, Color, Ref, Artifact, Object,
        };

        /// <summary>True for a known scalar name or a known scalar name followed by <c>[]</c>.</summary>
        public static bool IsKnown(string? type)
        {
            if (string.IsNullOrEmpty(type))
            {
                return false;
            }

            string element = IsArray(type!) ? ElementOf(type!) : type!;
            for (int i = 0; i < All.Count; i++)
            {
                if (string.Equals(All[i], element, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsArray(string type) => type.EndsWith(ArraySuffix, StringComparison.Ordinal);

        public static string ElementOf(string type) => IsArray(type) ? type.Substring(0, type.Length - ArraySuffix.Length) : type;
    }

    /// <summary>Ids of the built-in tools that are always present (03 s5). Their entries are registered by the edit engine.</summary>
    public static class BuiltInToolIds
    {
        public const string InspectDescribe = "inspect.describe";
        public const string InspectExplain = "inspect.explain";
        public const string QueryReferences = "query.references";
        public const string QueryImpact = "query.impact";
        public const string PreviewStage = "preview.stage";
        public const string PreviewCompare = "preview.compare";
        public const string HistoryUndo = "history.undo";
        public const string HistoryRedo = "history.redo";
        public const string ProjectSave = "project.save";
        public const string ProjectReload = "project.reload";
        public const string ProjectBuild = "project.build";
        public const string ProjectLaunch = "project.launch";
        public const string AssetImport = "asset.import";
        public const string AssetGenerate = "asset.generate";
        public const string MechanismPropose = "mechanism.propose";

        public static readonly IReadOnlyList<string> All = new[]
        {
            InspectDescribe, InspectExplain, QueryReferences, QueryImpact, PreviewStage, PreviewCompare,
            HistoryUndo, HistoryRedo, ProjectSave, ProjectReload, ProjectBuild, ProjectLaunch, AssetImport,
            AssetGenerate, MechanismPropose,
        };
    }
}
