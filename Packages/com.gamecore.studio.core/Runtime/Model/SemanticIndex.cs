// GameCore.Studio.Model - semantic index projection (docs/studio/03-authoring-contracts.md s3, 02 boundary B).
// The index is a traceable projection of Unity scenes, prefabs, definition assets and GameCore identities. It is
// never a second source of truth: every node carries the provenance it was projected from.
#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Model
{
    /// <summary>A revisioned projection (or bounded slice) of the project's authored content (03 s3). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SemanticIndex
    {
        [JsonConstructor]
        public SemanticIndex(
            long revision,
            string project,
            IReadOnlyList<IndexNode> nodes,
            IReadOnlyList<IndexEdge>? edges = null,
            IReadOnlyList<ScopeEntry>? scopes = null)
        {
            Revision = revision;
            Project = ModelLists.NotEmpty(project, nameof(project));
            Nodes = ModelLists.Required(nodes, nameof(nodes));
            Edges = ModelLists.Optional(edges, nameof(edges));
            Scopes = ModelLists.Optional(scopes, nameof(scopes));
        }

        [JsonProperty("revision", Required = Required.Always)]
        public long Revision { get; }

        [JsonProperty("project", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Project { get; }

        [JsonProperty("nodes", Required = Required.Always)]
        public IReadOnlyList<IndexNode> Nodes { get; }

        [JsonProperty("edges", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<IndexEdge>? Edges { get; }

        [JsonProperty("scopes", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<ScopeEntry>? Scopes { get; }

        /// <summary>The node naming the same authored thing as <paramref name="target"/>, or null (linear scan).</summary>
        public IndexNode? FindNode(AuthoringRef target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            for (int i = 0; i < Nodes.Count; i++)
            {
                if (Nodes[i].Ref.SameTarget(target))
                {
                    return Nodes[i];
                }
            }

            return null;
        }

        /// <summary>The first node whose ref carries <paramref name="definition"/> (<c>name@revision</c>), or null.</summary>
        public IndexNode? FindByDefinition(string definition)
        {
            for (int i = 0; i < Nodes.Count; i++)
            {
                if (string.Equals(Nodes[i].Ref.Definition, definition, StringComparison.Ordinal))
                {
                    return Nodes[i];
                }
            }

            return null;
        }
    }

    /// <summary>One authored thing in the index (03 s3). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class IndexNode
    {
        [JsonConstructor]
        public IndexNode(
            AuthoringRef @ref,
            string type,
            string? name = null,
            IReadOnlyDictionary<string, IndexField>? fields = null,
            IReadOnlyList<IndexRef>? refs = null,
            IReadOnlyList<string>? capabilities = null,
            Provenance? provenance = null)
        {
            Ref = ModelLists.NotNull(@ref, nameof(@ref));
            Type = ModelLists.NotEmpty(type, nameof(type));
            Name = name;
            Fields = ModelLists.OptionalMap(fields, nameof(fields));
            Refs = ModelLists.Optional(refs, nameof(refs));
            Capabilities = ModelLists.Optional(capabilities, nameof(capabilities));
            Provenance = provenance;
        }

        [JsonProperty("ref", Required = Required.Always)]
        public AuthoringRef Ref { get; }

        /// <summary>Authorable type id of the node (the <c>[Authorable]</c> typeId, e.g. <c>npc.definition</c>).</summary>
        [JsonProperty("type", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Type { get; }

        [JsonProperty("name", NullValueHandling = NullValueHandling.Ignore)]
        public string? Name { get; }

        /// <summary>Current field values keyed by field name.</summary>
        [JsonProperty("fields", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyDictionary<string, IndexField>? Fields { get; }

        /// <summary>Outgoing references held in fields.</summary>
        [JsonProperty("refs", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<IndexRef>? Refs { get; }

        /// <summary>Capability ids the node provides (e.g. <c>dialogue.speaker</c>).</summary>
        [JsonProperty("capabilities", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? Capabilities { get; }

        [JsonProperty("provenance", NullValueHandling = NullValueHandling.Ignore)]
        public Provenance? Provenance { get; }

        /// <summary>True when the node's type equals <paramref name="typeOrCapability"/> or it lists it as a capability.</summary>
        public bool Provides(string typeOrCapability)
        {
            if (string.Equals(Type, typeOrCapability, StringComparison.Ordinal))
            {
                return true;
            }

            if (Capabilities == null)
            {
                return false;
            }

            for (int i = 0; i < Capabilities.Count; i++)
            {
                if (string.Equals(Capabilities[i], typeOrCapability, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A field value as projected into the index, with its declared unit/range/type. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class IndexField
    {
        [JsonConstructor]
        public IndexField(string type, JToken? value = null, string? unit = null, IReadOnlyList<double>? range = null)
        {
            Type = ModelLists.NotEmpty(type, nameof(type));
            Value = value?.DeepClone();
            Unit = unit;
            Range = ModelLists.Optional(range, nameof(range));
        }

        /// <summary>Any JSON value; treat as read-only.</summary>
        [JsonProperty("value", NullValueHandling = NullValueHandling.Ignore)]
        public JToken? Value { get; }

        [JsonProperty("unit", NullValueHandling = NullValueHandling.Ignore)]
        public string? Unit { get; }

        /// <summary>[min, max] of the declared range.</summary>
        [JsonProperty("range", NullValueHandling = NullValueHandling.Ignore)]
        [SchemaHint(MinItems = 2, MaxItems = 2)]
        public IReadOnlyList<double>? Range { get; }

        /// <summary>Value type name from the field-type vocabulary of <see cref="ValueTypes"/>.</summary>
        [JsonProperty("type", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Type { get; }
    }

    /// <summary>A reference held in a named field of a node. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class IndexRef
    {
        [JsonConstructor]
        public IndexRef(string field, AuthoringRef to)
        {
            Field = ModelLists.NotEmpty(field, nameof(field));
            To = ModelLists.NotNull(to, nameof(to));
        }

        [JsonProperty("field", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Field { get; }

        [JsonProperty("to", Required = Required.Always)]
        public AuthoringRef To { get; }
    }

    /// <summary>A typed relationship between two authored things. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class IndexEdge
    {
        [JsonConstructor]
        public IndexEdge(AuthoringRef from, AuthoringRef to, EdgeKind kind)
        {
            From = ModelLists.NotNull(from, nameof(from));
            To = ModelLists.NotNull(to, nameof(to));
            Kind = kind;
        }

        [JsonProperty("from", Required = Required.Always)]
        public AuthoringRef From { get; }

        [JsonProperty("to", Required = Required.Always)]
        public AuthoringRef To { get; }

        [JsonProperty("kind", Required = Required.Always)]
        public EdgeKind Kind { get; }
    }

    /// <summary>A GameCore scope bound to a region, and the plugin installations it carries. Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class ScopeEntry
    {
        [JsonConstructor]
        public ScopeEntry(string scope, AuthoringRef? region = null, IReadOnlyList<string>? installs = null)
        {
            Scope = ModelLists.NotEmpty(scope, nameof(scope));
            Region = region;
            Installs = ModelLists.Optional(installs, nameof(installs));
        }

        /// <summary>Scope path (e.g. <c>world/marsh</c>).</summary>
        [JsonProperty("scope", Required = Required.Always)]
        [SchemaHint(MinLength = 1)]
        public string Scope { get; }

        [JsonProperty("region", NullValueHandling = NullValueHandling.Ignore)]
        public AuthoringRef? Region { get; }

        /// <summary>Installed plugin types as <c>name@version</c>.</summary>
        [JsonProperty("installs", NullValueHandling = NullValueHandling.Ignore)]
        public IReadOnlyList<string>? Installs { get; }
    }

    /// <summary>Where a node was projected from (asset path and line, when meaningful). Immutable.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class Provenance
    {
        [JsonConstructor]
        public Provenance(string? asset = null, int? line = null)
        {
            Asset = asset;
            Line = line;
        }

        [JsonProperty("asset", NullValueHandling = NullValueHandling.Ignore)]
        public string? Asset { get; }

        [JsonProperty("line", NullValueHandling = NullValueHandling.Ignore)]
        public int? Line { get; }
    }
}
