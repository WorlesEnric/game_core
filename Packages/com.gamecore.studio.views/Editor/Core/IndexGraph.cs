// GameCore.Studio.Views - an adjacency view over one semantic index revision (docs/studio/03 s3).
//
// Every view reads the index through this graph: nodes by identity key, outgoing and incoming links per node (all edge
// kinds, the plugin contributors' edges included), the field label of a link when one is known, and endpoints that
// are not index nodes (prefab assets, audio clips, unloaded scene objects) as "external" entries. Name references
// written by NestedReferenceContributor (an AuthoringRef whose path is "byname:<category>:<value>", e.g. P1.3's
// NpcDefinition.dialogueGraph = "dialogue.maren") are resolved here against the nodes of that category: a node whose
// name, definition name or any string field equals the value. The graph is immutable and built once per revision
// (O(nodes + edges)); views cache it by revision.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    /// <summary>One directed link between two identity keys.</summary>
    public sealed class GraphLink
    {
        public GraphLink(string fromKey, string toKey, EdgeKind kind, string? label)
        {
            FromKey = fromKey;
            ToKey = toKey;
            Kind = kind;
            Label = label;
        }

        public string FromKey { get; }

        public string ToKey { get; }

        public EdgeKind Kind { get; }

        /// <summary>The field (path) holding the reference, when known (e.g. <c>rewards[0].target</c>).</summary>
        public string? Label { get; }

        public string KindName => EdgeKinds.Name(Kind);

        public override string ToString() => FromKey + " -" + KindName + "-> " + ToKey;
    }

    /// <summary>Edge-kind names and sets.</summary>
    public static class EdgeKinds
    {
        public static readonly IReadOnlyList<EdgeKind> All = new[] { EdgeKind.References, EdgeKind.Contains, EdgeKind.Spawns, EdgeKind.BindsUi, EdgeKind.Triggers };

        public static string Name(EdgeKind kind)
        {
            switch (kind)
            {
                case EdgeKind.References: return "references";
                case EdgeKind.Contains: return "contains";
                case EdgeKind.Spawns: return "spawns";
                case EdgeKind.BindsUi: return "bindsUi";
                case EdgeKind.Triggers: return "triggers";
                default: return kind.ToString();
            }
        }

        /// <summary>The key of a link for label lookup and de-duplication.</summary>
        public static string Key(string fromKey, string toKey, EdgeKind kind) => fromKey + ">" + toKey + ">" + Name(kind);
    }

    /// <summary>Adjacency over one index revision.</summary>
    public sealed class IndexGraph
    {
        public const string ByNamePrefix = "byname:";
        public const string ExternalType = "external";

        private static readonly IReadOnlyList<GraphLink> NoLinks = Array.Empty<GraphLink>();

        private readonly Dictionary<string, IndexNode> _nodes = new Dictionary<string, IndexNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, AuthoringRef> _external = new Dictionary<string, AuthoringRef>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<GraphLink>> _out = new Dictionary<string, List<GraphLink>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<GraphLink>> _in = new Dictionary<string, List<GraphLink>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<IndexNode>> _byType = new Dictionary<string, List<IndexNode>>(StringComparer.Ordinal);
        private readonly List<GraphLink> _links = new List<GraphLink>();
        private readonly Dictionary<string, string> _resolved = new Dictionary<string, string>(StringComparer.Ordinal);

        private IndexGraph(SemanticIndex index)
        {
            Index = index;
        }

        public SemanticIndex Index { get; }

        public long Revision => Index.Revision;

        public IReadOnlyList<IndexNode> Nodes => Index.Nodes;

        public IReadOnlyList<GraphLink> Links => _links;

        public int NodeCount => _nodes.Count;

        /// <summary>
        /// Builds the graph. <paramref name="labels"/> maps <see cref="EdgeKinds.Key"/> to a field path (from the nested
        /// reference contributor); the index's own <c>refs</c> label direct references.
        /// </summary>
        public static IndexGraph Build(SemanticIndex index, IReadOnlyDictionary<string, string>? labels = null)
        {
            if (index == null)
            {
                throw new ArgumentNullException(nameof(index));
            }

            IndexGraph graph = new IndexGraph(index);
            Dictionary<string, string> fieldOf = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (IndexNode node in index.Nodes)
            {
                string key = node.Ref.IdentityKey;
                if (!graph._nodes.ContainsKey(key))
                {
                    graph._nodes.Add(key, node);
                }

                if (!graph._byType.TryGetValue(node.Type, out List<IndexNode>? list))
                {
                    list = new List<IndexNode>();
                    graph._byType.Add(node.Type, list);
                }

                list.Add(node);
                if (node.Refs != null)
                {
                    foreach (IndexRef reference in node.Refs)
                    {
                        string linkKey = EdgeKinds.Key(key, reference.To.IdentityKey, EdgeKind.References);
                        if (!fieldOf.ContainsKey(linkKey))
                        {
                            fieldOf.Add(linkKey, reference.Field);
                        }
                    }
                }
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            if (index.Edges != null)
            {
                foreach (IndexEdge edge in index.Edges)
                {
                    string from = graph.Resolve(edge.From);
                    string to = graph.Resolve(edge.To);
                    if (string.Equals(from, to, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string rawKey = EdgeKinds.Key(edge.From.IdentityKey, edge.To.IdentityKey, edge.Kind);
                    string linkKey = EdgeKinds.Key(from, to, edge.Kind);
                    if (!seen.Add(linkKey))
                    {
                        continue;
                    }

                    string? label = null;
                    if (labels != null && labels.TryGetValue(rawKey, out string? nested))
                    {
                        label = nested;
                    }
                    else if (fieldOf.TryGetValue(rawKey, out string? field))
                    {
                        label = field;
                    }

                    graph.Add(new GraphLink(from, to, edge.Kind, label));
                }
            }

            return graph;
        }

        public bool Contains(string key) => _nodes.ContainsKey(key) || _external.ContainsKey(key);

        public bool IsExternal(string key) => !_nodes.ContainsKey(key) && _external.ContainsKey(key);

        public IndexNode? Node(string key) => _nodes.TryGetValue(key, out IndexNode? node) ? node : null;

        public AuthoringRef? RefOf(string key)
        {
            if (_nodes.TryGetValue(key, out IndexNode? node))
            {
                return node.Ref;
            }

            return _external.TryGetValue(key, out AuthoringRef? reference) ? reference : null;
        }

        public string TypeOf(string key)
        {
            if (_nodes.TryGetValue(key, out IndexNode? node))
            {
                return node.Type;
            }

            if (_external.TryGetValue(key, out AuthoringRef? reference))
            {
                return reference.Path != null && reference.Path.StartsWith(ByNamePrefix, StringComparison.Ordinal) ? "unresolved" : ExternalType;
            }

            return ExternalType;
        }

        public string NameOf(string key)
        {
            if (_nodes.TryGetValue(key, out IndexNode? node))
            {
                return node.Name ?? LeafOf(node.Ref);
            }

            return _external.TryGetValue(key, out AuthoringRef? reference) ? LeafOf(reference) : key;
        }

        public IReadOnlyList<GraphLink> Outgoing(string key) => _out.TryGetValue(key, out List<GraphLink>? links) ? links : NoLinks;

        public IReadOnlyList<GraphLink> Incoming(string key) => _in.TryGetValue(key, out List<GraphLink>? links) ? links : NoLinks;

        /// <summary>Nodes of one authorable type id (in index order).</summary>
        public IReadOnlyList<IndexNode> OfType(string type) => _byType.TryGetValue(type, out List<IndexNode>? list) ? list : (IReadOnlyList<IndexNode>)Array.Empty<IndexNode>();

        /// <summary>Every type id present, sorted.</summary>
        public IReadOnlyList<string> Types
        {
            get
            {
                List<string> types = new List<string>(_byType.Keys);
                types.Sort(StringComparer.Ordinal);
                return types;
            }
        }

        /// <summary>
        /// The key of the region holding <paramref name="key"/>: a node providing <c>world.region</c> with a
        /// <c>contains</c> link into it or one of its containers (up to four levels), else null.
        /// </summary>
        public string? RegionOf(string key)
        {
            string current = key;
            for (int depth = 0; depth < 4; depth++)
            {
                string? container = null;
                foreach (GraphLink link in Incoming(current))
                {
                    if (link.Kind != EdgeKind.Contains)
                    {
                        continue;
                    }

                    IndexNode? from = Node(link.FromKey);
                    if (from != null && from.Provides("world.region"))
                    {
                        return link.FromKey;
                    }

                    container ??= link.FromKey;
                }

                if (container == null)
                {
                    return null;
                }

                current = container;
            }

            return null;
        }

        /// <summary>The string value of a node field, or null.</summary>
        public static string? StringField(IndexNode node, string field)
        {
            if (node.Fields != null && node.Fields.TryGetValue(field, out IndexField? value) && value.Value != null && value.Value.Type == JTokenType.String)
            {
                return value.Value.Value<string>();
            }

            return null;
        }

        /// <summary>The value of a node field, or null.</summary>
        public static JToken? Field(IndexNode node, string field)
        {
            return node.Fields != null && node.Fields.TryGetValue(field, out IndexField? value) ? value.Value : null;
        }

        /// <summary>The target of the first index ref held in <paramref name="field"/>, or null.</summary>
        public static AuthoringRef? RefField(IndexNode node, string field)
        {
            if (node.Refs == null)
            {
                return null;
            }

            foreach (IndexRef reference in node.Refs)
            {
                if (string.Equals(reference.Field, field, StringComparison.Ordinal))
                {
                    return reference.To;
                }
            }

            return null;
        }

        /// <summary>Every index ref target held in <paramref name="field"/>.</summary>
        public static IReadOnlyList<AuthoringRef> RefsField(IndexNode node, string field)
        {
            List<AuthoringRef> refs = new List<AuthoringRef>();
            if (node.Refs != null)
            {
                foreach (IndexRef reference in node.Refs)
                {
                    if (string.Equals(reference.Field, field, StringComparison.Ordinal))
                    {
                        refs.Add(reference.To);
                    }
                }
            }

            return refs;
        }

        /// <summary>The last path or name segment of a ref, for display.</summary>
        public static string LeafOf(AuthoringRef reference)
        {
            string? path = reference.Path;
            if (string.IsNullOrEmpty(path))
            {
                return reference.AuthoringId ?? reference.IdentityKey;
            }

            if (path!.StartsWith(ByNamePrefix, StringComparison.Ordinal))
            {
                int colon = path.LastIndexOf(':');
                return colon >= 0 ? path.Substring(colon + 1) : path;
            }

            int hash = path.LastIndexOf('/');
            string leaf = hash >= 0 ? path.Substring(hash + 1) : path;
            int dot = leaf.LastIndexOf('.');
            return dot > 0 && path.IndexOf('#') < 0 ? leaf.Substring(0, dot) : leaf;
        }

        private void Add(GraphLink link)
        {
            _links.Add(link);
            if (!_out.TryGetValue(link.FromKey, out List<GraphLink>? outgoing))
            {
                outgoing = new List<GraphLink>();
                _out.Add(link.FromKey, outgoing);
            }

            outgoing.Add(link);
            if (!_in.TryGetValue(link.ToKey, out List<GraphLink>? incoming))
            {
                incoming = new List<GraphLink>();
                _in.Add(link.ToKey, incoming);
            }

            incoming.Add(link);
        }

        private string Resolve(AuthoringRef reference)
        {
            string key = reference.IdentityKey;
            if (_nodes.ContainsKey(key))
            {
                return key;
            }

            if (_resolved.TryGetValue(key, out string? known))
            {
                return known;
            }

            string resolved = ResolveSlow(reference, key);
            _resolved[key] = resolved;
            return resolved;
        }

        private string ResolveSlow(AuthoringRef reference, string key)
        {

            string? path = reference.Path;
            if (path != null && path.StartsWith(ByNamePrefix, StringComparison.Ordinal))
            {
                IndexNode? named = ResolveByName(path.Substring(ByNamePrefix.Length));
                if (named != null)
                {
                    return named.Ref.IdentityKey;
                }
            }

            foreach (IndexNode node in Index.Nodes)
            {
                if (node.Ref.SameTarget(reference))
                {
                    return node.Ref.IdentityKey;
                }
            }

            if (!_external.ContainsKey(key))
            {
                _external.Add(key, reference);
            }

            return key;
        }

        private IndexNode? ResolveByName(string categoryAndValue)
        {
            int colon = categoryAndValue.IndexOf(':');
            string category = colon >= 0 ? categoryAndValue.Substring(0, colon) : string.Empty;
            string value = colon >= 0 ? categoryAndValue.Substring(colon + 1) : categoryAndValue;
            if (value.Length == 0)
            {
                return null;
            }

            IEnumerable<IndexNode> candidates = category.Length > 0 && _byType.TryGetValue(category, out List<IndexNode>? typed) ? typed : (IEnumerable<IndexNode>)Index.Nodes;
            IndexNode? fieldMatch = null;
            foreach (IndexNode node in candidates)
            {
                if (string.Equals(node.Name, value, StringComparison.Ordinal)
                    || string.Equals(node.Ref.AuthoringId, value, StringComparison.Ordinal)
                    || (node.Ref.Definition != null && node.Ref.Definition.StartsWith(value + "@", StringComparison.Ordinal)))
                {
                    return node;
                }

                if (fieldMatch == null && node.Fields != null)
                {
                    foreach (KeyValuePair<string, IndexField> field in node.Fields)
                    {
                        JToken? token = field.Value.Value;
                        if (token != null && token.Type == JTokenType.String && string.Equals(token.Value<string>(), value, StringComparison.Ordinal))
                        {
                            fieldMatch = node;
                            break;
                        }
                    }
                }
            }

            return fieldMatch;
        }
    }
}
