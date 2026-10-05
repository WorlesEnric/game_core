// GameCore.Studio.Views - W-VIEW-02 Dialogue (GameCore/Studio/Dialogue, F5 / SR-5.x).
// A dialogue graph on the canvas: line, choice, branch, action and end nodes laid out top to bottom from the entry;
// edges are the ports (next, else, option n; a port to -1 ends at the END card); badges show conditions, actions, the
// entry and, in Play Mode, the running world's visited bits. The inspector edits through DialogueEdits (change sets
// of dialogue.* tools and set); Ctrl/Cmd-drag between cards connects the first free port. The Preview panel runs
// dialogue.preview over the facts editor's overrides and highlights the nodes the preview reaches. "Play from here"
// asks the running game to start the graph through IGameplayCommandBridge (dialogue.start; the graph starts at its
// entry - there is no start-at-node command, see PACKET.md).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    public sealed class DialogueView : StudioViewBase
    {
        public const string EndId = "end";

        private readonly GraphCanvas _canvas;
        private readonly PopupField<string> _graphPicker;
        private readonly List<string> _graphKeys = new List<string>();
        private readonly VisualElement _inspector;
        private readonly VisualElement _preview;
        private readonly VisualElement _factsEditor;
        private readonly Label _previewOutput;
        private readonly Dictionary<string, int> _factOverrides = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly HashSet<int> _previewHits = new HashSet<int>();
        private string? _graphKey;
        private int _selectedNode = -1;

        public DialogueView(StudioViewContext context)
            : base(context, StudioViewIds.Dialogue)
        {
            _graphPicker = new PopupField<string>(new List<string> { "(no graph)" }, 0);
            _graphPicker.style.width = 220f;
            _graphPicker.RegisterValueChangedCallback(evt =>
            {
                int index = _graphPicker.index - 1;
                if (index >= 0 && index < _graphKeys.Count)
                {
                    _graphKey = _graphKeys[index];
                    _selectedNode = -1;
                    _previewHits.Clear();
                    Refresh();
                }
            });
            Toolbar.Add(_graphPicker);
            AddButton("Add line", () => AddLine("New line", string.Empty), "dialogue.addLine after the selected node.");
            AddButton("Add choice", () => AddChoice(new[] { "Yes", "No" }, string.Empty), "dialogue.addChoice after the selected node.");
            AddButton("Remove node", () => RemoveSelected(), "Removes the selected node (set nodes/edges).");
            AddSpacer();
            AddButton("Play from here", () => PlayFromHere(), "Play Mode: dialogue.start on the running game (starts at the graph's entry).");
            AddButton("Delete graph", () => DeleteGraph(), "delete the graph asset (journaled; undo restores it).");
            _canvas = new GraphCanvas();
            _canvas.SelectionChanged += card =>
            {
                _selectedNode = card?.Payload is int index ? index : -1;
                BuildInspector();
            };
            _canvas.NodeActivated += card =>
            {
                if (card.Payload is int)
                {
                    _inspector.Q<TextField>("node-text")?.Focus();
                }
            };
            _canvas.ConnectRequested += (from, to) =>
            {
                if (Document != null && from.Payload is int source)
                {
                    Document.DefaultPort(source, out string port, out int option);
                    Connect(source, port, option, to.Payload is int target ? target : -1);
                }
            };
            _canvas.DeleteRequested += card =>
            {
                if (card.Payload is int)
                {
                    RemoveSelected();
                }
            };
            Body.Add(_canvas);
            VisualElement side = new VisualElement();
            side.style.width = 340f;
            side.style.flexShrink = 0f;
            _inspector = Panel("Node", 340f);
            _inspector.style.flexGrow = 1f;
            ScrollView inspectorScroll = new ScrollView();
            inspectorScroll.style.flexGrow = 1f;
            inspectorScroll.Add(_inspector);
            side.Add(inspectorScroll);
            _preview = Panel("Preview (dialogue.preview)", 340f);
            _preview.style.height = 300f;
            _factsEditor = new ScrollView();
            _factsEditor.style.maxHeight = 120f;
            _preview.Add(_factsEditor);
            Button run = new Button(() => RunPreview()) { text = "Preview" };
            _preview.Add(run);
            ScrollView output = new ScrollView();
            output.style.flexGrow = 1f;
            _previewOutput = Text(string.Empty, 10f);
            _previewOutput.selection.isSelectable = true;
            output.Add(_previewOutput);
            _preview.Add(output);
            side.Add(_preview);
            Body.Add(side);
        }

        public DialogueDocument? Document { get; private set; }

        public GraphCanvas Canvas => _canvas;

        public string PreviewText => _previewOutput.text;

        public IReadOnlyDictionary<string, int> FactOverrides => _factOverrides;

        /// <summary>Shows the graph behind <paramref name="reference"/>.</summary>
        public void ShowGraph(AuthoringRef reference)
        {
            _graphKey = reference.IdentityKey;
            _selectedNode = -1;
            _previewHits.Clear();
            Refresh();
        }

        public void SelectNode(int index)
        {
            _canvas.Select(NodeId(index), true);
            _selectedNode = index;
            BuildInspector();
        }

        public void SetFact(string factName, int value)
        {
            _factOverrides[factName] = value;
        }

        public void ClearFacts() => _factOverrides.Clear();

        public ApplyReport? AddLine(string text, string speaker)
        {
            return Document == null ? null : ApplyEdit(ViewEdits.Build("Dialogue: add line", new[] { DialogueEdits.AddLine(Document, text, speaker, _selectedNode) }));
        }

        public ApplyReport? AddChoice(IReadOnlyList<string> options, string prompt)
        {
            return Document == null ? null : ApplyEdit(ViewEdits.Build("Dialogue: add choice", new[] { DialogueEdits.AddChoice(Document, options, null, _selectedNode, prompt) }));
        }

        public ApplyReport? Connect(int from, string port, int option, int to)
        {
            return Document == null ? null : ApplyEdit(ViewEdits.Build("Dialogue: connect " + from + " " + port + " -> " + to, new[] { DialogueEdits.Connect(Document, from, port, option, to) }));
        }

        public ApplyReport? RemoveSelected()
        {
            if (Document == null || _selectedNode < 0 || _selectedNode >= Document.Nodes.Count)
            {
                return null;
            }

            int node = _selectedNode;
            _selectedNode = -1;
            return ApplyEdit(ViewEdits.Build("Dialogue: remove node " + node, new[] { DialogueEdits.RemoveNode(Document, node) }));
        }

        /// <summary>Runs dialogue.preview with the facts editor's overrides; returns the output.</summary>
        public ToolInvocation? RunPreview()
        {
            if (Document == null)
            {
                return null;
            }

            JObject args = new JObject { ["facts"] = DialogueEdits.FactsText(_factOverrides) };
            ToolInvocation result = Context.Tools.Invoke("dialogue.preview", Document.Ref, args);
            _previewOutput.text = result.Text;
            _previewHits.Clear();
            if (result.Ok)
            {
                foreach (int index in DialogueDocument.NodesNamedIn(result.Text))
                {
                    _previewHits.Add(index);
                }
            }

            ShowCanvas(Context.Graph());
            return result;
        }

        public GameplayCommandResult PlayFromHere()
        {
            if (Document == null)
            {
                return new GameplayCommandResult(GameplayCommandStatus.Refused, "no graph");
            }

            string graph = Document.NpcGraphRef.Length > 0 ? Document.NpcGraphRef : Document.AuthoringId;
            string speaker = _selectedNode >= 0 && _selectedNode < Document.Nodes.Count && Document.Nodes[_selectedNode].SpeakerEntityId.Length > 0
                ? Document.Nodes[_selectedNode].SpeakerEntityId
                : Document.SpeakerEntityId;
            GameplayCommandResult result = Context.Gameplay.StartDialogue(graph, speaker);
            SetStatus("dialogue.start: " + result, result.Ok ? ViewPalette.Good : ViewPalette.Warn);
            return result;
        }

        protected override void OnSelectionChanged()
        {
            foreach (AuthoringRef selected in Context.Selection.Current)
            {
                IndexGraph graph = Context.Graph();
                IndexNode? node = graph.Node(selected.IdentityKey);
                if (node != null && string.Equals(node.Type, DialogueDocument.Type, StringComparison.Ordinal) && !string.Equals(_graphKey, node.Ref.IdentityKey, StringComparison.Ordinal))
                {
                    ShowGraph(node.Ref);
                    return;
                }
            }
        }

        protected override void OnRefresh()
        {
            IndexGraph graph = Context.Graph();
            _graphKeys.Clear();
            List<string> labels = new List<string> { "(choose a dialogue graph)" };
            foreach (IndexNode node in graph.OfType(DialogueDocument.Type))
            {
                _graphKeys.Add(node.Ref.IdentityKey);
                labels.Add(graph.NameOf(node.Ref.IdentityKey));
            }

            if (_graphKey == null && _graphKeys.Count > 0)
            {
                _graphKey = _graphKeys[0];
            }

            _graphPicker.choices = labels;
            int selected = _graphKey == null ? -1 : _graphKeys.IndexOf(_graphKey);
            _graphPicker.SetValueWithoutNotify(labels[selected + 1]);
            AuthoringRef? reference = _graphKey == null ? null : graph.RefOf(_graphKey);
            Document = reference == null ? null : DialogueDocument.Load(Context.Runtime, reference);
            if (Document == null)
            {
                _canvas.SetGraph(Array.Empty<CanvasNode>(), Array.Empty<CanvasEdge>());
                BuildInspector();
                BuildFacts(graph);
                SetStatus(graph.OfType(DialogueDocument.Type).Count == 0 ? "No dialogue graphs in the index." : "Choose a graph.");
                return;
            }

            if (_selectedNode >= Document.Nodes.Count)
            {
                _selectedNode = -1;
            }

            ShowCanvas(graph);
            BuildInspector();
            BuildFacts(graph);
        }

        private void ShowCanvas(IndexGraph graph)
        {
            DialogueDocument? document = Document;
            if (document == null)
            {
                return;
            }

            bool playing = Context.IsPlaying && Context.Gameplay.HasNarrative;
            List<CanvasNode> cards = new List<CanvasNode>();
            bool needsEnd = false;
            foreach (DialogueNode node in document.Nodes)
            {
                CanvasNode card = new CanvasNode(NodeId(node.Index), "[" + node.Index.ToString(CultureInfo.InvariantCulture) + "] " + node.Kind)
                {
                    Subtitle = node.Speaker.Length > 0 ? node.Speaker : (node.Is("Line") ? document.Speaker : string.Empty),
                    Accent = KindColor(node.Kind),
                    Payload = node.Index,
                    Size = new Vector2(250f, node.Options.Count > 0 ? 70f + 14f * Math.Min(node.Options.Count, 4) : 84f),
                    Highlighted = _previewHits.Contains(node.Index),
                };
                if (node.Text.Length > 0)
                {
                    card.Details.Add(AuthoredData.Short(node.Text, 44));
                }

                foreach (DialogueOption option in node.Options)
                {
                    card.Details.Add((option.Index + 1).ToString(CultureInfo.InvariantCulture) + ". " + AuthoredData.Short(option.Text, 30) + (option.Condition != null ? "  [if " + AuthoredData.Display(StudioJson.ToToken(option.Condition), graph) + "]" : string.Empty));
                }

                if (node.Index == document.Entry)
                {
                    card.Badges.Add(new CanvasBadge("entry", ViewPalette.Info));
                }

                if (node.Condition != null)
                {
                    card.Badges.Add(new CanvasBadge("if " + AuthoredData.Short(AuthoredData.Display(StudioJson.ToToken(node.Condition), graph), 20), ViewPalette.Warn));
                }

                if (node.Actions != null)
                {
                    card.Badges.Add(new CanvasBadge("do " + AuthoredData.Short(AuthoredData.Display(StudioJson.ToToken(node.Actions), graph), 20), ViewPalette.Teal));
                }

                if (playing && document.AuthoringId.Length > 0 && Context.Gameplay.TryReadVisited(document.AuthoringId, node.Index, out bool visited))
                {
                    card.Badges.Add(new CanvasBadge(visited ? "visited" : "not visited", visited ? ViewPalette.Good : ViewPalette.Muted));
                }

                cards.Add(card);
            }

            List<CanvasEdge> edges = new List<CanvasEdge>();
            foreach (DialogueEdgeInfo edge in document.Edges)
            {
                string to = edge.To >= 0 && edge.To < document.Nodes.Count ? NodeId(edge.To) : EndId;
                needsEnd |= to == EndId;
                edges.Add(new CanvasEdge(NodeId(edge.From), to, edge.Label)
                {
                    Color = string.Equals(edge.Port, DialogueDocument.PortElse, StringComparison.Ordinal) ? ViewPalette.Bad
                        : string.Equals(edge.Port, DialogueDocument.PortOption, StringComparison.Ordinal) ? ViewPalette.Purple : ViewPalette.Edge,
                    Highlighted = _previewHits.Contains(edge.From) && (edge.To < 0 || _previewHits.Contains(edge.To)),
                    Payload = edge,
                });
            }

            if (needsEnd)
            {
                cards.Add(new CanvasNode(EndId, "END") { Accent = ViewPalette.Muted, Size = new Vector2(120f, 40f) });
            }

            _canvas.SetGraph(cards, edges, new[] { NodeId(document.Entry) }, keepPositions: true, vertical: true);
            if (_selectedNode >= 0)
            {
                _canvas.Select(NodeId(_selectedNode));
            }

            _canvas.StatusText = document.Name + ": " + document.Nodes.Count + " nodes, " + document.Edges.Count + " edges" + (playing ? " (live)" : string.Empty);
        }

        private void BuildInspector()
        {
            _inspector.Clear();
            DialogueDocument? document = Document;
            if (document == null)
            {
                _inspector.Add(Text("No graph."));
                return;
            }

            _inspector.Add(Text(document.Name + "  (" + document.Nodes.Count + " nodes)", 12f, FontStyle.Bold));
            if (_selectedNode < 0 || _selectedNode >= document.Nodes.Count)
            {
                _inspector.Add(Text("Select a node (click, arrows, [ and ]). Add line/choice appends after the selection; Ctrl/Cmd-drag connects; Delete removes."));
                return;
            }

            DialogueNode node = document.Nodes[_selectedNode];
            int index = node.Index;
            _inspector.Add(Text("[" + index + "] " + node.Kind, 12f, FontStyle.Bold));
            TextField speaker = new TextField("Speaker") { value = node.Speaker };
            speaker.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (Document != null && speaker.value != node.Speaker)
                {
                    ApplyEdit(ViewEdits.Build("Dialogue: speaker of node " + index, new[] { DialogueEdits.SetSpeaker(Document, index, speaker.value) }));
                }
            });
            _inspector.Add(speaker);
            TextField text = new TextField("Text") { value = node.Text, multiline = true, name = "node-text" };
            text.style.whiteSpace = WhiteSpace.Normal;
            text.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (Document != null && text.value != node.Text)
                {
                    ApplyEdit(ViewEdits.Build("Dialogue: text of node " + index, new[] { DialogueEdits.SetText(Document, index, text.value) }));
                }
            });
            _inspector.Add(text);
            foreach (DialogueOption option in node.Options)
            {
                int optionIndex = option.Index;
                TextField optionField = new TextField("Option " + (optionIndex + 1)) { value = option.Text };
                optionField.RegisterCallback<FocusOutEvent>(_ =>
                {
                    if (Document != null && optionField.value != option.Text)
                    {
                        ApplyEdit(ViewEdits.Build("Dialogue: rename option " + (optionIndex + 1) + " of node " + index, new[] { DialogueEdits.RenameOption(Document, index, optionIndex, optionField.value) }));
                    }
                });
                _inspector.Add(optionField);
            }

            if (node.Is("Branch") || node.Options.Count > 0)
            {
                BuildConditionEditor(node);
            }

            _inspector.Add(Text("Ports", 11f, FontStyle.Bold));
            foreach (DialogueEdgeInfo edge in document.EdgesFrom(index))
            {
                VisualElement row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.Add(Text(edge.Label + " -> " + (edge.To < 0 ? "END" : "[" + edge.To + "]")));
                int edgeIndex = edge.Index;
                row.Add(new Button(() =>
                {
                    if (Document != null)
                    {
                        ApplyEdit(ViewEdits.Build("Dialogue: disconnect " + edge.Label + " of node " + index, new[] { DialogueEdits.Disconnect(Document, edgeIndex) }));
                    }
                }) { text = "x", tooltip = "Disconnect" });
                _inspector.Add(row);
            }

            List<string> ports = new List<string> { DialogueDocument.PortNext, DialogueDocument.PortElse, DialogueDocument.PortOption };
            document.DefaultPort(index, out string defaultPort, out int defaultOption);
            PopupField<string> port = new PopupField<string>("Port", ports, ports.IndexOf(defaultPort));
            IntegerField optionNumber = new IntegerField("Option #") { value = defaultOption + 1 };
            IntegerField target = new IntegerField("To node (-1 = end)") { value = -1 };
            _inspector.Add(port);
            _inspector.Add(optionNumber);
            _inspector.Add(target);
            _inspector.Add(new Button(() => Connect(index, port.value, Math.Max(0, optionNumber.value - 1), target.value)) { text = "Connect" });
        }

        private void BuildConditionEditor(DialogueNode node)
        {
            IndexGraph graph = Context.Graph();
            List<string> keys = new List<string> { string.Empty };
            List<string> labels = new List<string> { "(none)" };
            foreach (IndexNode condition in graph.OfType("logic.conditionSet"))
            {
                keys.Add(condition.Ref.IdentityKey);
                labels.Add(graph.NameOf(condition.Ref.IdentityKey));
            }

            int index = node.Index;
            if (node.Is("Branch"))
            {
                int current = node.Condition == null ? 0 : Math.Max(0, keys.IndexOf(node.Condition.IdentityKey));
                PopupField<string> picker = new PopupField<string>("Condition", labels, current);
                picker.RegisterValueChangedCallback(_ => LinkCondition(index, keys[picker.index], -1, graph));
                _inspector.Add(picker);
            }

            foreach (DialogueOption option in node.Options)
            {
                int current = option.Condition == null ? 0 : Math.Max(0, keys.IndexOf(option.Condition.IdentityKey));
                PopupField<string> picker = new PopupField<string>("If (option " + (option.Index + 1) + ")", labels, current);
                int optionIndex = option.Index;
                picker.RegisterValueChangedCallback(_ => LinkCondition(index, keys[picker.index], optionIndex, graph));
                _inspector.Add(picker);
            }
        }

        private void LinkCondition(int node, string conditionKey, int option, IndexGraph graph)
        {
            if (Document == null)
            {
                return;
            }

            AuthoringRef? condition = conditionKey.Length == 0 ? null : graph.RefOf(conditionKey);
            ApplyEdit(ViewEdits.Build("Dialogue: condition of node " + node, new[] { DialogueEdits.LinkCondition(Document, node, condition, option) }));
        }

        private void BuildFacts(IndexGraph graph)
        {
            _factsEditor.Clear();
            foreach (IndexNode fact in graph.OfType("narrative.fact"))
            {
                string? name = IndexGraph.StringField(fact, "factName");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                int initial = AuthoredData.Int(IndexGraph.Field(fact, "initialValue"));
                IntegerField field = new IntegerField(name) { value = _factOverrides.TryGetValue(name!, out int value) ? value : initial };
                if (Context.IsPlaying && Context.Gameplay.TryReadFact(name!, out int live))
                {
                    field.label = name + " (live " + live + ")";
                }

                string factName = name!;
                field.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue == initial)
                    {
                        _factOverrides.Remove(factName);
                    }
                    else
                    {
                        _factOverrides[factName] = evt.newValue;
                    }
                });
                _factsEditor.Add(field);
            }
        }

        private void DeleteGraph()
        {
            if (Document == null)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Delete dialogue graph", "Delete " + Document.Name + "? The change set is journaled; Undo restores it.", "Delete", "Cancel"))
            {
                return;
            }

            ApplyEdit(ViewEdits.Build("Dialogue: delete graph " + Document.Name, new[] { DialogueEdits.DeleteGraph(Document) }));
        }

        private static string NodeId(int index) => "n" + index.ToString(CultureInfo.InvariantCulture);

        public static Color KindColor(string kind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "line": return ViewPalette.Info;
                case "choice": return ViewPalette.Purple;
                case "branch": return ViewPalette.Warn;
                case "action": return ViewPalette.Teal;
                case "end": return ViewPalette.Muted;
                default: return ViewPalette.Neutral;
            }
        }
    }

    public sealed class DialogueWindow : StudioViewWindow
    {
        protected override string ViewTitle => "Dialogue";

        [MenuItem(StudioViewIds.DialogueMenu, false, 2101)]
        public static void OpenWindow() => Open<DialogueWindow>();

        protected override StudioViewBase CreateView(StudioViewContext context) => new DialogueView(context);
    }
}
