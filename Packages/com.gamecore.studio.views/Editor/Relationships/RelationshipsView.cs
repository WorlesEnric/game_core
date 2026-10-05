// GameCore.Studio.Views - W-VIEW-01 Relationships (GameCore/Studio/Relationships, SR-2.2/SR-11.x).
// The selection's neighbourhood in the semantic index on the graph canvas: depth 1-3, edge-kind filter, node cards with
// type id, name, stale badge and region/residency. Click selects through the selection bridge, double-click (or Enter)
// focuses. "Impact of deleting" lists what removing the selected node affects, with counts by type. The sub-graph
// exports as JSON or Mermaid. The view is read-only: it makes no change sets.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    public sealed class RelationshipsView : StudioViewBase
    {
        public const int StaleProbeLimit = 300;

        private readonly GraphCanvas _canvas;
        private readonly SliderInt _depth;
        private readonly Dictionary<EdgeKind, ToolbarToggle> _kindToggles = new Dictionary<EdgeKind, ToolbarToggle>();
        private readonly ToolbarToggle _follow;
        private readonly ToolbarToggle _impactMode;
        private readonly VisualElement _side;
        private readonly Label _details;
        private readonly VisualElement _impactCounts;
        private readonly ListView _impactList;
        private readonly List<ImpactRow> _impactRows = new List<ImpactRow>();
        private readonly List<string> _roots = new List<string>();
        private string _search = string.Empty;

        public RelationshipsView(StudioViewContext context)
            : base(context, StudioViewIds.Relationships)
        {
            _follow = new ToolbarToggle { text = "Follow selection", value = true, tooltip = "Re-root on every selection change." };
            _follow.RegisterValueChangedCallback(_ => Refresh());
            Toolbar.Add(_follow);
            _depth = new SliderInt("Depth", 1, 3) { value = 1, showInputField = true };
            _depth.style.width = 170f;
            _depth.RegisterValueChangedCallback(_ => Refresh());
            Toolbar.Add(_depth);
            foreach (EdgeKind kind in EdgeKinds.All)
            {
                ToolbarToggle toggle = new ToolbarToggle { text = EdgeKinds.Name(kind), value = true };
                toggle.RegisterValueChangedCallback(_ => Refresh());
                _kindToggles[kind] = toggle;
                Toolbar.Add(toggle);
            }

            _impactMode = new ToolbarToggle { text = "Impact of deleting", value = false };
            _impactMode.RegisterValueChangedCallback(_ => Refresh());
            Toolbar.Add(_impactMode);
            AddSpacer();
            AddSearch(text =>
            {
                _search = text;
                Refresh();
            });
            AddButton("JSON", () => Export(false), "Export the sub-graph as JSON (also copied to the clipboard).");
            AddButton("Mermaid", () => Export(true), "Export the sub-graph as a Mermaid flowchart (also copied to the clipboard).");
            _canvas = new GraphCanvas();
            _canvas.SelectionChanged += OnCardSelected;
            _canvas.NodeActivated += OnCardActivated;
            Body.Add(_canvas);
            _side = Panel("Details", 300f);
            _details = Text(string.Empty);
            _side.Add(_details);
            _impactCounts = new VisualElement();
            _impactCounts.style.flexDirection = FlexDirection.Row;
            _impactCounts.style.flexWrap = Wrap.Wrap;
            _impactCounts.style.marginTop = 6f;
            _side.Add(_impactCounts);
            _impactList = new ListView(_impactRows, 34f, MakeImpactRow, BindImpactRow)
            {
                selectionType = SelectionType.Single,
            };
            _impactList.style.flexGrow = 1f;
            _impactList.selectionChanged += items =>
            {
                foreach (object item in items)
                {
                    if (item is ImpactRow row)
                    {
                        SelectKey(row.Key, false);
                    }
                }
            };
            _side.Add(_impactList);
            Body.Add(_side);
        }

        /// <summary>The neighbourhood on the canvas.</summary>
        public Neighbourhood? Current { get; private set; }

        /// <summary>The impact report while Impact mode is on.</summary>
        public ImpactAnalysis? Impact { get; private set; }

        public GraphCanvas Canvas => _canvas;

        public int Depth
        {
            get => _depth.value;
            set => _depth.value = Mathf.Clamp(value, 1, 3);
        }

        public bool ImpactMode
        {
            get => _impactMode.value;
            set => _impactMode.value = value;
        }

        public void SetKindEnabled(EdgeKind kind, bool enabled) => _kindToggles[kind].value = enabled;

        /// <summary>Exports the current sub-graph (JSON or Mermaid) to a file the user picks and the clipboard.</summary>
        public string? Export(bool mermaid)
        {
            if (Current == null)
            {
                return null;
            }

            string content = mermaid ? RelationshipsModel.ToMermaid(Current) : RelationshipsModel.ToJson(Current).ToString();
            ViewSupport.Export("Export sub-graph", mermaid ? "relationships.mmd" : "relationships.json", mermaid ? "mmd" : "json", content);
            return content;
        }

        /// <summary>Roots the view at explicit keys (stops following the selection).</summary>
        public void SetRoots(IEnumerable<string> keys)
        {
            _follow.SetValueWithoutNotify(false);
            _roots.Clear();
            _roots.AddRange(keys);
            Refresh();
        }

        protected override void OnSelectionChanged()
        {
            if (_follow.value)
            {
                Refresh();
            }
        }

        protected override void OnRefresh()
        {
            IndexGraph graph = Context.Graph();
            if (_follow.value)
            {
                _roots.Clear();
                foreach (AuthoringRef selected in Context.Selection.Current)
                {
                    string? key = KeyOf(graph, selected);
                    if (key != null)
                    {
                        _roots.Add(key);
                    }
                }
            }

            if (_search.Length > 0)
            {
                _roots.Clear();
                foreach (IndexNode node in graph.Nodes)
                {
                    if ((node.Name ?? string.Empty).IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        _roots.Add(node.Ref.IdentityKey);
                        if (_roots.Count >= 8)
                        {
                            break;
                        }
                    }
                }
            }

            HashSet<EdgeKind> kinds = new HashSet<EdgeKind>();
            foreach (KeyValuePair<EdgeKind, ToolbarToggle> pair in _kindToggles)
            {
                if (pair.Value.value)
                {
                    kinds.Add(pair.Key);
                }
            }

            Neighbourhood neighbourhood = RelationshipsModel.Build(graph, _roots, _depth.value, kinds);
            bool probe = neighbourhood.Nodes.Count <= StaleProbeLimit;
            foreach (NeighbourNode node in neighbourhood.Nodes)
            {
                if (node.RegionKey != null)
                {
                    node.Residency = ViewSupport.Residency(Context, graph, node.RegionKey);
                }

                AuthoringRef? reference = graph.RefOf(node.Key);
                if (probe && reference != null && !node.Stale)
                {
                    node.Stale = ViewSupport.StaleReason(Context.Runtime, reference) != null;
                }
            }

            Current = neighbourhood;
            Impact = null;
            if (_impactMode.value && _roots.Count > 0)
            {
                AuthoringRef? target = graph.RefOf(_roots[0]);
                ImpactReport? report = target != null && graph.Node(_roots[0]) != null ? Context.Runtime.Index.ImpactOf(target) : null;
                Impact = ImpactAnalysis.Of(graph, _roots[0], report);
            }

            RelationshipsCanvas.Show(_canvas, neighbourhood, Impact);
            ShowImpact();
            ShowDetails(_canvas.Selected);
            _canvas.StatusText = neighbourhood.Nodes.Count + " nodes, " + neighbourhood.Links.Count + " links" + (neighbourhood.Truncated ? " (truncated)" : string.Empty) + ", built in " + neighbourhood.Milliseconds.ToString("0.0") + " ms";
            if (_roots.Count == 0)
            {
                SetStatus("Select an authored object (scene, project, a Studio view) or search by name to show its relationships.");
            }
        }

        private void ShowImpact()
        {
            _impactRows.Clear();
            _impactCounts.Clear();
            bool visible = Impact != null;
            _impactList.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _impactCounts.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (Impact == null)
            {
                _impactList.RefreshItems();
                return;
            }

            _impactRows.AddRange(Impact.Rows);
            _impactCounts.Add(Text("Deleting affects " + Impact.Rows.Count + ":", 11f, FontStyle.Bold));
            foreach (KeyValuePair<string, int> count in Impact.CountsByType)
            {
                _impactCounts.Add(Chip(count.Key + " x" + count.Value, ViewPalette.ForType(count.Key)));
            }

            _impactList.RefreshItems();
        }

        private void ShowDetails(CanvasNode? card)
        {
            if (card == null || Current == null || !(card.Payload is string key))
            {
                _details.text = Current == null || Current.Roots.Count == 0 ? "Nothing selected." : "Roots: " + Current.Roots.Count + ". Click a card for details.";
                return;
            }

            IndexGraph graph = Context.Graph();
            NeighbourNode? node = Current.Find(key);
            System.Text.StringBuilder text = new System.Text.StringBuilder();
            text.Append(graph.NameOf(key)).Append('\n').Append(graph.TypeOf(key)).Append('\n').Append(key).Append('\n');
            if (node != null && node.RegionKey != null)
            {
                text.Append("region ").Append(node.RegionName).Append(node.Residency.Length > 0 ? " (" + node.Residency + ")" : string.Empty).Append('\n');
            }

            text.Append('\n');
            int shown = 0;
            foreach (GraphLink link in graph.Outgoing(key))
            {
                if (shown++ >= 12)
                {
                    break;
                }

                text.Append("-> ").Append(link.KindName).Append(' ').Append(graph.NameOf(link.ToKey)).Append(string.IsNullOrEmpty(link.Label) ? string.Empty : "  [" + link.Label + "]").Append('\n');
            }

            shown = 0;
            foreach (GraphLink link in graph.Incoming(key))
            {
                if (shown++ >= 12)
                {
                    break;
                }

                text.Append("<- ").Append(link.KindName).Append(' ').Append(graph.NameOf(link.FromKey)).Append(string.IsNullOrEmpty(link.Label) ? string.Empty : "  [" + link.Label + "]").Append('\n');
            }

            _details.text = text.ToString();
        }

        private void OnCardSelected(CanvasNode? card)
        {
            ShowDetails(card);
            if (card?.Payload is string key && !_follow.value)
            {
                SelectKey(key, false);
            }
        }

        private void OnCardActivated(CanvasNode card)
        {
            if (card.Payload is string key)
            {
                SelectKey(key, true);
            }
        }

        private void SelectKey(string key, bool focus)
        {
            AuthoringRef? reference = Context.Graph().RefOf(key);
            if (reference == null)
            {
                return;
            }

            bool follow = _follow.value;
            _follow.SetValueWithoutNotify(false);
            try
            {
                if (focus)
                {
                    Context.Selection.Focus(reference);
                }
                else
                {
                    Context.Selection.Select(new[] { reference });
                }
            }
            finally
            {
                _follow.SetValueWithoutNotify(follow);
            }
        }

        private static string? KeyOf(IndexGraph graph, AuthoringRef reference)
        {
            string key = reference.IdentityKey;
            if (graph.Contains(key))
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

            return null;
        }

        private static VisualElement MakeImpactRow()
        {
            VisualElement row = new VisualElement();
            row.style.paddingTop = 2f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            Label detail = new Label { name = "detail" };
            detail.style.fontSize = 10f;
            row.Add(title);
            row.Add(detail);
            return row;
        }

        private void BindImpactRow(VisualElement element, int index)
        {
            ImpactRow row = _impactRows[index];
            element.Q<Label>("title").text = row.Name + "  (" + row.Type + ")";
            element.Q<Label>("detail").text = row.Relation + (row.Field.Length > 0 ? " " + row.Field : string.Empty) + " via " + row.Via + ", depth " + row.Depth;
        }
    }

    /// <summary>Turns a neighbourhood into canvas cards (shared with the synthetic 2,000-node test).</summary>
    public static class RelationshipsCanvas
    {
        public static void Show(GraphCanvas canvas, Neighbourhood neighbourhood, ImpactAnalysis? impact = null)
        {
            List<CanvasNode> nodes = new List<CanvasNode>(neighbourhood.Nodes.Count);
            HashSet<string> roots = new HashSet<string>(neighbourhood.Roots, StringComparer.Ordinal);
            foreach (NeighbourNode node in neighbourhood.Nodes)
            {
                CanvasNode card = new CanvasNode(node.Key, node.Name)
                {
                    Subtitle = node.Type,
                    Accent = ViewPalette.ForType(node.Type),
                    Payload = node.Key,
                    Highlighted = roots.Contains(node.Key) || (impact != null && impact.Affects(node.Key)),
                    Dimmed = node.External,
                    Size = new Vector2(210f, 64f),
                };
                if (node.Stale)
                {
                    card.Badges.Add(new CanvasBadge("stale", ViewPalette.Bad));
                }

                if (node.RegionKey != null)
                {
                    card.Badges.Add(new CanvasBadge(node.RegionName + (node.Residency.Length > 0 ? " · " + node.Residency : string.Empty), ViewSupport.ResidencyColor(node.Residency)));
                }

                if (node.External)
                {
                    card.Badges.Add(new CanvasBadge(node.Type == "unresolved" ? "unresolved" : "external", ViewPalette.Muted));
                }

                foreach (string detail in node.Details)
                {
                    card.Details.Add(detail);
                }

                nodes.Add(card);
            }

            List<CanvasEdge> edges = new List<CanvasEdge>(neighbourhood.Links.Count);
            foreach (GraphLink link in neighbourhood.Links)
            {
                edges.Add(new CanvasEdge(link.FromKey, link.ToKey, link.Kind == EdgeKind.References ? (link.Label ?? string.Empty) : link.KindName)
                {
                    Color = EdgeColor(link.Kind),
                    Payload = link,
                });
            }

            canvas.SetGraph(nodes, edges, neighbourhood.Roots);
        }

        public static Color EdgeColor(EdgeKind kind)
        {
            switch (kind)
            {
                case EdgeKind.Contains: return new Color(0.35f, 0.7f, 0.7f, 0.9f);
                case EdgeKind.Spawns: return new Color(0.6f, 0.75f, 0.35f, 0.9f);
                case EdgeKind.BindsUi: return new Color(0.75f, 0.5f, 0.9f, 0.9f);
                case EdgeKind.Triggers: return new Color(0.95f, 0.6f, 0.3f, 0.9f);
                default: return ViewPalette.Edge;
            }
        }
    }

    public sealed class RelationshipsWindow : StudioViewWindow
    {
        protected override string ViewTitle => "Relationships";

        [MenuItem(StudioViewIds.RelationshipsMenu, false, 2100)]
        public static void OpenWindow() => Open<RelationshipsWindow>();

        protected override StudioViewBase CreateView(StudioViewContext context) => new RelationshipsView(context);
    }
}
