// GameCore.Studio.Views - lazy layered layout for the graph canvases.
//
// The layout runs in time slices (Step(budgetMs)) so a 2,000-node neighbourhood never blocks the editor for more than
// one budget per frame: phase 1 assigns layers by breadth-first search from the roots (or keeps the layer hint a view
// gave), phase 2 orders every layer by the barycentre of its neighbours in the previous layer (one sweep), phase 3
// places cards on a grid of columns. Nodes that already have a position (kept across refreshes) are not moved, so an
// incremental update only places what is new.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace GameCore.Studio.Views.Canvas
{
    public sealed class GraphLayout
    {
        public const float ColumnGap = 90f;
        public const float RowGap = 26f;

        /// <summary>Cards per lane before a layer wraps into another lane.</summary>
        public const int MaxPerLane = 40;

        private readonly List<CanvasNode> _nodes;
        private readonly Dictionary<string, CanvasNode> _byId = new Dictionary<string, CanvasNode>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, List<string>> _previous = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        private readonly IReadOnlyList<CanvasEdge> _edges;
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly HashSet<string> _roots;
        private readonly bool _vertical;
        private IEnumerator<bool>? _work;

        /// <param name="vertical">Layers top to bottom (dialogue) instead of left to right.</param>
        public GraphLayout(IReadOnlyList<CanvasNode> nodes, IReadOnlyList<CanvasEdge> edges, IEnumerable<string>? roots = null, bool vertical = false)
        {
            _nodes = new List<CanvasNode>(nodes ?? throw new ArgumentNullException(nameof(nodes)));
            _edges = edges ?? Array.Empty<CanvasEdge>();
            _roots = new HashSet<string>(roots ?? Array.Empty<string>(), StringComparer.Ordinal);
            _vertical = vertical;
        }

        /// <summary>True once every node has a position.</summary>
        public bool Done { get; private set; }

        /// <summary>Slices run so far.</summary>
        public int Steps { get; private set; }

        /// <summary>The longest slice in milliseconds.</summary>
        public double MaxStepMilliseconds { get; private set; }

        public double TotalMilliseconds { get; private set; }

        /// <summary>Runs the layout for at most about <paramref name="budgetMs"/>; true when finished.</summary>
        public bool Step(double budgetMs)
        {
            if (Done)
            {
                return true;
            }

            _work ??= Run().GetEnumerator();
            _watch.Restart();
            while (_watch.Elapsed.TotalMilliseconds < budgetMs)
            {
                if (!_work.MoveNext())
                {
                    Done = true;
                    break;
                }
            }

            double elapsed = _watch.Elapsed.TotalMilliseconds;
            Steps++;
            TotalMilliseconds += elapsed;
            MaxStepMilliseconds = Math.Max(MaxStepMilliseconds, elapsed);
            return Done;
        }

        /// <summary>Runs to completion (small graphs, tests).</summary>
        public void RunToEnd()
        {
            while (!Step(double.MaxValue))
            {
            }
        }

        private IEnumerable<bool> Run()
        {
            // Building adjacency is part of the sliced workload, not constructor work.
            int prepared = 0;
            foreach (CanvasNode node in _nodes)
            {
                _byId[node.Id] = node;
                if (++prepared % 64 == 0) yield return false;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (CanvasEdge edge in _edges)
            {
                if (++prepared % 64 == 0) yield return false;
                if (!_byId.ContainsKey(edge.From) || !_byId.ContainsKey(edge.To) || edge.From == edge.To)
                {
                    continue;
                }

                if (!seen.Add(edge.From + "\n" + edge.To))
                {
                    continue;
                }

                Link(_next, edge.From, edge.To);
                Link(_previous, edge.To, edge.From);
            }

            // Phase 1: layers.
            Dictionary<string, int> layer = new Dictionary<string, int>(StringComparer.Ordinal);
            Queue<string> queue = new Queue<string>();
            foreach (CanvasNode node in _nodes)
            {
                if (node.Layer >= 0)
                {
                    layer[node.Id] = node.Layer;
                }
                else if (_roots.Contains(node.Id) || (_roots.Count == 0 && !_previous.ContainsKey(node.Id)))
                {
                    layer[node.Id] = 0;
                    queue.Enqueue(node.Id);
                }
            }

            if (layer.Count == 0 && _nodes.Count > 0)
            {
                layer[_nodes[0].Id] = 0;
                queue.Enqueue(_nodes[0].Id);
            }

            int work = 0;
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                int next = layer[current] + 1;
                if (_next.TryGetValue(current, out List<string>? targets))
                {
                    foreach (string target in targets)
                    {
                        if (!layer.ContainsKey(target))
                        {
                            layer[target] = next;
                            queue.Enqueue(target);
                        }
                    }
                }

                if (++work % 64 == 0)
                {
                    yield return false;
                }
            }

            int maxLayer = 0;
            foreach (int value in layer.Values)
            {
                maxLayer = Math.Max(maxLayer, value);
            }

            // Unreached nodes (they only point at the roots' side: an impact or "used by" view) go one layer before the
            // earliest neighbour they point at, repeatedly, so a chain of referrers fans out to the left (layers may go
            // negative; positions only use their order). Whatever is still unreached is isolated: a last column.
            bool placed = true;
            while (placed)
            {
                placed = false;
                foreach (CanvasNode node in _nodes)
                {
                    if (layer.ContainsKey(node.Id) || !_next.TryGetValue(node.Id, out List<string>? outs))
                    {
                        continue;
                    }

                    int best = int.MaxValue;
                    foreach (string target in outs)
                    {
                        if (layer.TryGetValue(target, out int targetLayer))
                        {
                            best = Math.Min(best, targetLayer - 1);
                        }
                    }

                    if (best != int.MaxValue)
                    {
                        layer[node.Id] = best;
                        placed = true;
                    }

                    if (++work % 64 == 0)
                    {
                        yield return false;
                    }
                }
            }

            foreach (CanvasNode node in _nodes)
            {
                if (!layer.ContainsKey(node.Id))
                {
                    layer[node.Id] = maxLayer + 1;
                }
            }

            // Phase 2: order within layers by the barycentre of previous-layer neighbours.
            SortedDictionary<int, List<CanvasNode>> layers = new SortedDictionary<int, List<CanvasNode>>();
            foreach (CanvasNode node in _nodes)
            {
                int value = layer[node.Id];
                if (!layers.TryGetValue(value, out List<CanvasNode>? list))
                {
                    list = new List<CanvasNode>();
                    layers.Add(value, list);
                }

                list.Add(node);
            }

            Dictionary<string, float> order = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (KeyValuePair<int, List<CanvasNode>> pair in layers)
            {
                List<CanvasNode> list = pair.Value;
                for (int i = 0; i < list.Count; i++)
                {
                    float sum = 0f;
                    int count = 0;
                    if (_previous.TryGetValue(list[i].Id, out List<string>? sources))
                    {
                        foreach (string source in sources)
                        {
                            if (order.TryGetValue(source, out float position))
                            {
                                sum += position;
                                count++;
                            }
                        }
                    }

                    order[list[i].Id] = count > 0 ? sum / count : i;
                }

                list.Sort((left, right) =>
                {
                    int compare = order[left.Id].CompareTo(order[right.Id]);
                    return compare != 0 ? compare : string.CompareOrdinal(left.Id, right.Id);
                });
                for (int i = 0; i < list.Count; i++)
                {
                    order[list[i].Id] = i;
                }

                yield return false;
            }

            // Phase 3: positions (cards that already have one stay where they are).
            float columnWidth = 0f;
            float rowHeight = 0f;
            foreach (CanvasNode node in _nodes)
            {
                columnWidth = Math.Max(columnWidth, node.Size.x);
                rowHeight = Math.Max(rowHeight, node.Size.y);
            }

            // A layer wider than MaxPerLane cards wraps into several adjacent lanes, so a 1,800-card layer stays a
            // block instead of a strip too tall to frame.
            List<int> keys = new List<int>(layers.Keys);
            keys.Sort();
            float alongStep = (_vertical ? columnWidth : rowHeight) + (_vertical ? ColumnGap * 0.4f : RowGap);
            float acrossStep = (_vertical ? rowHeight : columnWidth) + (_vertical ? RowGap * 2.5f : ColumnGap);
            int lane = 0;
            foreach (int key in keys)
            {
                List<CanvasNode> list = layers[key];
                int lanes = Math.Max(1, (list.Count + MaxPerLane - 1) / MaxPerLane);
                int perLane = (list.Count + lanes - 1) / Math.Max(1, lanes);
                float extent = Math.Min(list.Count, Math.Max(1, perLane)) * alongStep;
                for (int i = 0; i < list.Count; i++)
                {
                    CanvasNode node = list[i];
                    if (!node.HasPosition)
                    {
                        int slot = perLane == 0 ? 0 : i % perLane;
                        int sub = perLane == 0 ? 0 : i / perLane;
                        float along = slot * alongStep - extent * 0.5f;
                        float across = (lane + sub) * acrossStep;
                        node.Position = _vertical ? new Vector2(along, across) : new Vector2(across, along);
                        node.HasPosition = true;
                    }

                    if (++work % 128 == 0)
                    {
                        yield return false;
                    }
                }

                lane += lanes;
            }

            yield return true;
        }

        private static void Link(Dictionary<string, List<string>> map, string from, string to)
        {
            if (!map.TryGetValue(from, out List<string>? list))
            {
                list = new List<string>();
                map.Add(from, list);
            }

            list.Add(to);
        }
    }
}
