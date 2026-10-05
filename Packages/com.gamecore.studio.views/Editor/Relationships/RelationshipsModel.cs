// GameCore.Studio.Views - W-VIEW-01 model: the neighbourhood of a selection in the semantic index, impact of deleting,
// and sub-graph export (JSON, Mermaid). Pure over an IndexGraph so 2,000-node synthetic graphs are testable without UI.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Views
{
    /// <summary>One node of a neighbourhood.</summary>
    public sealed class NeighbourNode
    {
        public NeighbourNode(string key, string type, string name, int depth, bool external)
        {
            Key = key;
            Type = type;
            Name = name;
            Depth = depth;
            External = external;
        }

        public string Key { get; }

        public string Type { get; }

        public string Name { get; }

        /// <summary>Hops from the nearest root (0 = a root).</summary>
        public int Depth { get; }

        /// <summary>Not an index node (an asset, an unloaded scene object, an unresolved name).</summary>
        public bool External { get; }

        /// <summary>The containing region's key, when known.</summary>
        public string? RegionKey { get; set; }

        public string RegionName { get; set; } = string.Empty;

        /// <summary>Residency of the region (Resident, Unloaded...), or empty.</summary>
        public string Residency { get; set; } = string.Empty;

        /// <summary>True when the indexed version is behind the object, or the reference no longer resolves.</summary>
        public bool Stale { get; set; }

        /// <summary>Short facts for the card (e.g. "2 patrol points").</summary>
        public List<string> Details { get; } = new List<string>();
    }

    /// <summary>A sub-graph around one or more roots.</summary>
    public sealed class Neighbourhood
    {
        public Neighbourhood(IReadOnlyList<string> roots, int depth, IReadOnlyList<NeighbourNode> nodes, IReadOnlyList<GraphLink> links, bool truncated, double milliseconds)
        {
            Roots = roots;
            Depth = depth;
            Nodes = nodes;
            Links = links;
            Truncated = truncated;
            Milliseconds = milliseconds;
        }

        public IReadOnlyList<string> Roots { get; }

        public int Depth { get; }

        public IReadOnlyList<NeighbourNode> Nodes { get; }

        public IReadOnlyList<GraphLink> Links { get; }

        /// <summary>True when the node cap cut the expansion.</summary>
        public bool Truncated { get; }

        public double Milliseconds { get; }

        public NeighbourNode? Find(string key)
        {
            foreach (NeighbourNode node in Nodes)
            {
                if (string.Equals(node.Key, key, StringComparison.Ordinal))
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>Nodes of one type id.</summary>
        public IReadOnlyList<NeighbourNode> OfType(string type)
        {
            List<NeighbourNode> found = new List<NeighbourNode>();
            foreach (NeighbourNode node in Nodes)
            {
                if (string.Equals(node.Type, type, StringComparison.Ordinal))
                {
                    found.Add(node);
                }
            }

            return found;
        }
    }

    /// <summary>One row of an impact report.</summary>
    public sealed class ImpactRow
    {
        public ImpactRow(string key, string type, string name, int depth, string relation, string via, string field)
        {
            Key = key;
            Type = type;
            Name = name;
            Depth = depth;
            Relation = relation;
            Via = via;
            Field = field;
        }

        public string Key { get; }

        public string Type { get; }

        public string Name { get; }

        public int Depth { get; }

        public string Relation { get; }

        public string Via { get; }

        public string Field { get; }

        public override string ToString() => Type + " " + Name + " (" + Relation + (Field.Length > 0 ? " " + Field : string.Empty) + ", depth " + Depth + ")";
    }

    /// <summary>What deleting a thing would affect: the index's ImpactOf plus the contributed nested references.</summary>
    public sealed class ImpactAnalysis
    {
        public ImpactAnalysis(string targetKey, IReadOnlyList<ImpactRow> rows)
        {
            TargetKey = targetKey;
            Rows = rows;
            SortedDictionary<string, int> counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (ImpactRow row in rows)
            {
                counts.TryGetValue(row.Type, out int count);
                counts[row.Type] = count + 1;
            }

            CountsByType = counts;
        }

        public string TargetKey { get; }

        public IReadOnlyList<ImpactRow> Rows { get; }

        public IReadOnlyDictionary<string, int> CountsByType { get; }

        public bool Affects(string key)
        {
            foreach (ImpactRow row in Rows)
            {
                if (string.Equals(row.Key, key, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        public IReadOnlyList<ImpactRow> OfType(string type)
        {
            List<ImpactRow> rows = new List<ImpactRow>();
            foreach (ImpactRow row in Rows)
            {
                if (string.Equals(row.Type, type, StringComparison.Ordinal))
                {
                    rows.Add(row);
                }
            }

            return rows;
        }

        /// <summary>
        /// The impact of deleting <paramref name="targetKey"/>: every node with a reference into it, transitively (a
        /// referrer of a referrer is affected through it), plus everything it contains. <paramref name="indexImpact"/>
        /// (SemanticIndexService.ImpactOf) is merged first so its relation names win where both agree.
        /// </summary>
        public static ImpactAnalysis Of(IndexGraph graph, string targetKey, ImpactReport? indexImpact = null, int maxDepth = 8)
        {
            Dictionary<string, ImpactRow> rows = new Dictionary<string, ImpactRow>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            if (indexImpact != null)
            {
                foreach (ImpactItem item in indexImpact.Items)
                {
                    string key = Canonical(graph, item.Ref);
                    if (string.Equals(key, targetKey, StringComparison.Ordinal) || rows.ContainsKey(key))
                    {
                        continue;
                    }

                    rows[key] = new ImpactRow(key, graph.TypeOf(key), graph.NameOf(key), item.Depth, item.Relation, graph.NameOf(Canonical(graph, item.Via)), item.Field ?? string.Empty);
                    order.Add(key);
                }
            }

            Queue<KeyValuePair<string, int>> queue = new Queue<KeyValuePair<string, int>>();
            HashSet<string> visited = new HashSet<string>(StringComparer.Ordinal) { targetKey };
            queue.Enqueue(new KeyValuePair<string, int>(targetKey, 0));
            while (queue.Count > 0)
            {
                KeyValuePair<string, int> current = queue.Dequeue();
                if (current.Value >= maxDepth)
                {
                    continue;
                }

                int depth = current.Value + 1;
                foreach (GraphLink link in graph.Incoming(current.Key))
                {
                    if (link.Kind != EdgeKind.Contains)
                    {
                        Visit(graph, rows, order, queue, visited, link.FromKey, depth, "references", current.Key, link.Label);
                    }
                }

                foreach (GraphLink link in graph.Outgoing(current.Key))
                {
                    if (link.Kind == EdgeKind.Contains)
                    {
                        Visit(graph, rows, order, queue, visited, link.ToKey, depth, "contained", current.Key, link.Label);
                    }
                }
            }

            List<ImpactRow> result = new List<ImpactRow>(order.Count);
            foreach (string key in order)
            {
                result.Add(rows[key]);
            }

            return new ImpactAnalysis(targetKey, result);
        }

        private static void Visit(IndexGraph graph, Dictionary<string, ImpactRow> rows, List<string> order, Queue<KeyValuePair<string, int>> queue, HashSet<string> visited, string key, int depth, string relation, string via, string? label)
        {
            if (!visited.Add(key))
            {
                return;
            }

            queue.Enqueue(new KeyValuePair<string, int>(key, depth));
            if (!rows.ContainsKey(key))
            {
                rows[key] = new ImpactRow(key, graph.TypeOf(key), graph.NameOf(key), depth, relation, graph.NameOf(via), label ?? string.Empty);
                order.Add(key);
            }
        }

        private static string Canonical(IndexGraph graph, AuthoringRef reference)
        {
            string key = reference.IdentityKey;
            if (graph.Node(key) != null)
            {
                return key;
            }

            foreach (IndexNode node in graph.Nodes)
            {
                if (node.Ref.SameTarget(reference))
                {
                    return node.Ref.IdentityKey;
                }
            }

            return key;
        }
    }

    /// <summary>Neighbourhood expansion and export.</summary>
    public static class RelationshipsModel
    {
        public const int DefaultMaxNodes = 5000;

        /// <summary>
        /// The nodes within <paramref name="depth"/> hops of <paramref name="roots"/>, following links of
        /// <paramref name="kinds"/> in both directions, capped at <paramref name="maxNodes"/>.
        /// </summary>
        public static Neighbourhood Build(IndexGraph graph, IReadOnlyList<string> roots, int depth, ICollection<EdgeKind> kinds, int maxNodes = DefaultMaxNodes)
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            depth = Math.Max(0, depth);
            Dictionary<string, int> depthOf = new Dictionary<string, int>(StringComparer.Ordinal);
            List<string> order = new List<string>();
            Queue<string> queue = new Queue<string>();
            bool truncated = false;
            List<string> validRoots = new List<string>();
            foreach (string root in roots)
            {
                if (!depthOf.ContainsKey(root))
                {
                    depthOf[root] = 0;
                    order.Add(root);
                    queue.Enqueue(root);
                    validRoots.Add(root);
                }
            }

            while (queue.Count > 0 && !truncated)
            {
                string current = queue.Dequeue();
                int next = depthOf[current] + 1;
                if (next > depth)
                {
                    continue;
                }

                foreach (string neighbour in Neighbours(graph, current, kinds))
                {
                    if (depthOf.ContainsKey(neighbour))
                    {
                        continue;
                    }

                    if (order.Count >= maxNodes)
                    {
                        truncated = true;
                        break;
                    }

                    depthOf[neighbour] = next;
                    order.Add(neighbour);
                    queue.Enqueue(neighbour);
                }
            }

            List<NeighbourNode> nodes = new List<NeighbourNode>(order.Count);
            foreach (string key in order)
            {
                IndexNode? indexNode = graph.Node(key);
                NeighbourNode node = new NeighbourNode(key, graph.TypeOf(key), graph.NameOf(key), depthOf[key], indexNode == null);
                string? region = indexNode != null && indexNode.Provides("world.region") ? key : graph.RegionOf(key);
                if (region != null)
                {
                    node.RegionKey = region;
                    node.RegionName = graph.NameOf(region);
                }

                node.Stale = string.Equals(node.Type, "unresolved", StringComparison.Ordinal);
                if (indexNode != null)
                {
                    Describe(indexNode, node.Details);
                }

                nodes.Add(node);
            }

            List<GraphLink> links = new List<GraphLink>();
            foreach (string key in order)
            {
                foreach (GraphLink link in graph.Outgoing(key))
                {
                    if (kinds.Contains(link.Kind) && depthOf.ContainsKey(link.ToKey))
                    {
                        links.Add(link);
                    }
                }
            }

            return new Neighbourhood(validRoots, depth, nodes, links, truncated, watch.Elapsed.TotalMilliseconds);
        }

        /// <summary>The sub-graph as JSON: {roots, depth, nodes:[{key,type,name,depth,region,residency,stale}], links:[{from,to,kind,label}]}.</summary>
        public static JObject ToJson(Neighbourhood neighbourhood)
        {
            JArray nodes = new JArray();
            foreach (NeighbourNode node in neighbourhood.Nodes)
            {
                JObject item = new JObject
                {
                    ["key"] = node.Key,
                    ["type"] = node.Type,
                    ["name"] = node.Name,
                    ["depth"] = node.Depth,
                };
                if (node.RegionKey != null)
                {
                    item["region"] = node.RegionName;
                }

                if (node.Residency.Length > 0)
                {
                    item["residency"] = node.Residency;
                }

                if (node.Stale)
                {
                    item["stale"] = true;
                }

                nodes.Add(item);
            }

            JArray links = new JArray();
            foreach (GraphLink link in neighbourhood.Links)
            {
                JObject item = new JObject { ["from"] = link.FromKey, ["to"] = link.ToKey, ["kind"] = link.KindName };
                if (!string.IsNullOrEmpty(link.Label))
                {
                    item["label"] = link.Label;
                }

                links.Add(item);
            }

            return new JObject
            {
                ["schema"] = "gamecore.studio.views.subgraph/1",
                ["roots"] = new JArray(neighbourhood.Roots),
                ["depth"] = neighbourhood.Depth,
                ["truncated"] = neighbourhood.Truncated,
                ["nodes"] = nodes,
                ["links"] = links,
            };
        }

        /// <summary>The sub-graph as a Mermaid flowchart (left to right; one id per node; edge labels are the kinds).</summary>
        public static string ToMermaid(Neighbourhood neighbourhood)
        {
            StringBuilder text = new StringBuilder();
            text.Append("flowchart LR\n");
            Dictionary<string, string> ids = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < neighbourhood.Nodes.Count; i++)
            {
                NeighbourNode node = neighbourhood.Nodes[i];
                string id = "n" + i.ToString(CultureInfo.InvariantCulture);
                ids[node.Key] = id;
                text.Append("  ").Append(id).Append("[\"").Append(Escape(node.Name)).Append("<br/><small>").Append(Escape(node.Type)).Append("</small>\"]\n");
            }

            foreach (GraphLink link in neighbourhood.Links)
            {
                if (ids.TryGetValue(link.FromKey, out string? from) && ids.TryGetValue(link.ToKey, out string? to))
                {
                    text.Append("  ").Append(from).Append(" -->|").Append(link.KindName).Append("| ").Append(to).Append('\n');
                }
            }

            foreach (string root in neighbourhood.Roots)
            {
                if (ids.TryGetValue(root, out string? id))
                {
                    text.Append("  style ").Append(id).Append(" stroke-width:3px\n");
                }
            }

            return text.ToString();
        }

        /// <summary>Card facts from the index projection: list-valued fields as counts (patrol points, stages...).</summary>
        public static void Describe(IndexNode node, List<string> details)
        {
            if (node.Fields == null)
            {
                return;
            }

            foreach (KeyValuePair<string, IndexField> field in node.Fields)
            {
                if (field.Value.Value is JArray array && array.Count > 0 && details.Count < 3 && !(array[0] is JValue && array.Count <= 4 && field.Value.Type.StartsWith("vector", StringComparison.Ordinal)))
                {
                    details.Add(array.Count + " " + Words(field.Key));
                }
            }
        }

        /// <summary>"patrolPoints" -> "patrol points".</summary>
        public static string Words(string member)
        {
            StringBuilder text = new StringBuilder();
            foreach (char character in member)
            {
                if (char.IsUpper(character) && text.Length > 0)
                {
                    text.Append(' ');
                }

                text.Append(char.ToLowerInvariant(character));
            }

            return text.ToString();
        }

        private static IEnumerable<string> Neighbours(IndexGraph graph, string key, ICollection<EdgeKind> kinds)
        {
            foreach (GraphLink link in graph.Outgoing(key))
            {
                if (kinds.Contains(link.Kind))
                {
                    yield return link.ToKey;
                }
            }

            foreach (GraphLink link in graph.Incoming(key))
            {
                if (kinds.Contains(link.Kind))
                {
                    yield return link.FromKey;
                }
            }
        }

        private static string Escape(string text) => (text ?? string.Empty).Replace("\"", "'").Replace("<", "&lt;").Replace(">", "&gt;");
    }
}
