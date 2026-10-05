// GameCore.Studio.Views - W-VIEW-03 Quests (GameCore/Studio/Quests, F5 / SR-5.x).
// A quest board on the canvas: one column per stage (header card plus its objective cards), branch objectives linked
// from their stage with the branch name, `next` links between stage headers, and a completion card whose badges are
// the rewards (with their branch). In Play Mode the running quest's status, stage and branch and every objective's
// done flag overlay the board. Inline edits and the add forms make change sets of quest.* tools and set. The Simulate
// panel runs quest.simulate for a branch choice and shows the resulting facts and inventory. The Rules sidebar lists
// the rules that reference the quest (directly or through a condition/action set), their logic.explain text, and the
// running world's last explain entries for them.
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
    public sealed class QuestsView : StudioViewBase
    {
        public const float ColumnWidth = 260f;
        public const string CompleteId = "complete";

        private readonly GraphCanvas _canvas;
        private readonly PopupField<string> _questPicker;
        private readonly List<string> _questKeys = new List<string>();
        private readonly VisualElement _inspector;
        private readonly PopupField<string> _branch;
        private readonly TextField _path;
        private readonly Label _simulation;
        private readonly VisualElement _rules;
        private string? _questKey;
        private string? _selectedId;

        public QuestsView(StudioViewContext context)
            : base(context, StudioViewIds.Quests)
        {
            _questPicker = new PopupField<string>(new List<string> { "(no quest)" }, 0);
            _questPicker.style.width = 220f;
            _questPicker.RegisterValueChangedCallback(_ =>
            {
                int index = _questPicker.index - 1;
                if (index >= 0 && index < _questKeys.Count)
                {
                    _questKey = _questKeys[index];
                    _selectedId = null;
                    Refresh();
                }
            });
            Toolbar.Add(_questPicker);
            AddButton("Add stage", () => AddStage("New stage"), "quest.addStage");
            AddSpacer();
            _canvas = new GraphCanvas();
            _canvas.SelectionChanged += card =>
            {
                _selectedId = card?.Id;
                BuildInspector();
            };
            Body.Add(_canvas);
            VisualElement side = new VisualElement();
            side.style.width = 360f;
            side.style.flexShrink = 0f;
            ScrollView scroll = new ScrollView();
            scroll.style.flexGrow = 1f;
            _inspector = Panel("Inspector", 360f);
            scroll.Add(_inspector);
            VisualElement simulate = Panel("Simulate (quest.simulate)", 360f);
            _branch = new PopupField<string>("Branch", new List<string> { "always" }, 0);
            _branch.RegisterValueChangedCallback(_ => _path!.value = Document == null ? string.Empty : QuestEdits.PathFor(Document, _branch!.index, Context.Graph()));
            simulate.Add(_branch);
            _path = new TextField("Path") { multiline = true };
            _path.style.whiteSpace = WhiteSpace.Normal;
            simulate.Add(_path);
            simulate.Add(new Button(() => RunSimulation()) { text = "Simulate" });
            _simulation = Text(string.Empty, 10f);
            _simulation.selection.isSelectable = true;
            simulate.Add(_simulation);
            scroll.Add(simulate);
            _rules = Panel("Rules", 360f);
            scroll.Add(_rules);
            side.Add(scroll);
            Body.Add(side);
        }

        public QuestDocument? Document { get; private set; }

        public GraphCanvas Canvas => _canvas;

        public QuestSimulation? LastSimulation { get; private set; }

        /// <summary>The rule keys the sidebar lists.</summary>
        public IReadOnlyList<string> RuleKeys { get; private set; } = Array.Empty<string>();

        public void ShowQuest(AuthoringRef reference)
        {
            _questKey = reference.IdentityKey;
            _selectedId = null;
            Refresh();
        }

        public ApplyReport? AddStage(string title)
        {
            return Document == null ? null : ApplyEdit(ViewEdits.Build("Quest: add stage", new[] { QuestEdits.AddStage(Document, title) }));
        }

        /// <summary>Simulates the chosen branch (or <paramref name="branch"/>) along the derived path.</summary>
        public QuestSimulation? RunSimulation(int? branch = null)
        {
            if (Document == null)
            {
                return null;
            }

            int chosen = branch ?? _branch.index;
            string path = branch.HasValue || _path.value.Length == 0 ? QuestEdits.PathFor(Document, chosen, Context.Graph()) : _path.value;
            _path.SetValueWithoutNotify(path);
            LastSimulation = QuestEdits.Simulate(Context.Tools, Document, path, chosen);
            _simulation.text = QuestEdits.Describe(LastSimulation) + "\n\n" + LastSimulation.Text;
            return LastSimulation;
        }

        protected override void OnSelectionChanged()
        {
            IndexGraph graph = Context.Graph();
            foreach (AuthoringRef selected in Context.Selection.Current)
            {
                IndexNode? node = graph.Node(selected.IdentityKey);
                if (node != null && node.Type == QuestDocument.Type && node.Ref.IdentityKey != _questKey)
                {
                    ShowQuest(node.Ref);
                    return;
                }
            }
        }

        protected override void OnRefresh()
        {
            IndexGraph graph = Context.Graph();
            _questKeys.Clear();
            List<string> labels = new List<string> { "(choose a quest)" };
            foreach (IndexNode node in graph.OfType(QuestDocument.Type))
            {
                _questKeys.Add(node.Ref.IdentityKey);
                labels.Add(graph.NameOf(node.Ref.IdentityKey));
            }

            if (_questKey == null && _questKeys.Count > 0)
            {
                _questKey = _questKeys[0];
            }

            _questPicker.choices = labels;
            _questPicker.SetValueWithoutNotify(labels[(_questKey == null ? -1 : _questKeys.IndexOf(_questKey)) + 1]);
            AuthoringRef? reference = _questKey == null ? null : graph.RefOf(_questKey);
            Document = reference == null ? null : QuestDocument.Load(Context.Runtime, reference);
            if (Document == null)
            {
                _canvas.SetGraph(Array.Empty<CanvasNode>(), Array.Empty<CanvasEdge>());
                _inspector.Clear();
                _rules.Clear();
                SetStatus("No quest.");
                return;
            }

            List<string> branches = new List<string> { "always (branch 0)" };
            for (int b = 1; b <= Document.BranchCount; b++)
            {
                branches.Add(b + ": " + Document.BranchName(b));
            }

            int branchIndex = Math.Min(_branch.index, branches.Count - 1);
            _branch.choices = branches;
            _branch.SetValueWithoutNotify(branches[Math.Max(0, branchIndex)]);
            if (_path.value.Length == 0)
            {
                _path.SetValueWithoutNotify(QuestEdits.PathFor(Document, _branch.index, graph));
            }

            ShowBoard(graph);
            BuildInspector();
            BuildRules(graph);
        }

        private void ShowBoard(IndexGraph graph)
        {
            QuestDocument quest = Document!;
            bool live = Context.IsPlaying && Context.Gameplay.HasNarrative && quest.AuthoringId.Length > 0;
            QuestLiveState state = default;
            bool hasState = live && Context.Gameplay.TryReadQuest(quest.AuthoringId, out state);
            List<CanvasNode> cards = new List<CanvasNode>();
            List<CanvasEdge> edges = new List<CanvasEdge>();
            for (int s = 0; s < quest.Stages.Count; s++)
            {
                QuestStage stage = quest.Stages[s];
                CanvasNode header = new CanvasNode(StageId(s), "Stage " + s + ": " + stage.Title)
                {
                    Subtitle = AuthoredData.Short(stage.Description, 40),
                    Accent = ViewPalette.Warn,
                    Position = new Vector2(s * ColumnWidth, 0f),
                    HasPosition = true,
                    Size = new Vector2(ColumnWidth - 30f, 58f),
                    Payload = stage,
                };
                if (hasState && state.Stage == s && state.Status == 1)
                {
                    header.Badges.Add(new CanvasBadge("current", ViewPalette.Good));
                    header.Highlighted = true;
                }

                cards.Add(header);
                int next = stage.NextIndex;
                edges.Add(new CanvasEdge(StageId(s), next < quest.Stages.Count ? StageId(next) : CompleteId, stage.Next >= 0 ? "next " + stage.Next : string.Empty) { Color = ViewPalette.Warn });
                float y = 80f;
                foreach (QuestObjective objective in quest.ObjectivesOf(s))
                {
                    CanvasNode card = new CanvasNode(ObjectiveId(objective.Index), objective.Kind + " " + TargetText(objective, graph))
                    {
                        Subtitle = objective.Text,
                        Accent = objective.Branch == 0 ? ViewPalette.Info : BranchColor(objective.Branch),
                        Position = new Vector2(s * ColumnWidth + 12f, y),
                        HasPosition = true,
                        Size = new Vector2(ColumnWidth - 42f, 62f),
                        Payload = objective,
                    };
                    if (objective.Required > 1)
                    {
                        card.Badges.Add(new CanvasBadge("x" + objective.Required, ViewPalette.Neutral));
                    }

                    if (objective.Branch > 0)
                    {
                        card.Badges.Add(new CanvasBadge(quest.BranchName(objective.Branch), BranchColor(objective.Branch)));
                    }

                    if (live && Context.Gameplay.TryReadObjectiveDone(quest.AuthoringId, objective.Index, out bool done))
                    {
                        card.Badges.Add(new CanvasBadge(done ? "done" : "open", done ? ViewPalette.Good : ViewPalette.Muted));
                    }

                    cards.Add(card);
                    edges.Add(new CanvasEdge(StageId(s), card.Id, objective.Branch > 0 ? quest.BranchName(objective.Branch) : string.Empty)
                    {
                        Color = objective.Branch > 0 ? BranchColor(objective.Branch) : new Color(0.5f, 0.55f, 0.6f, 0.5f),
                        Width = objective.Branch > 0 ? 2f : 1f,
                    });
                    y += 74f;
                }
            }

            CanvasNode complete = new CanvasNode(CompleteId, "Complete")
            {
                Subtitle = quest.Rewards.Count + " rewards",
                Accent = ViewPalette.Good,
                Position = new Vector2(quest.Stages.Count * ColumnWidth, 0f),
                HasPosition = true,
                Size = new Vector2(ColumnWidth - 30f, 58f + 16f * ((quest.Rewards.Count + 1) / 2)),
            };
            foreach (QuestReward reward in quest.Rewards)
            {
                string text = (reward.Target == null ? "?" : AuthoredData.Display(StudioJson.ToToken(reward.Target), graph))
                    + (string.Equals(reward.Kind, "Item", StringComparison.OrdinalIgnoreCase) ? " x" + reward.Value : " = " + reward.Value)
                    + (reward.Branch > 0 ? " [" + quest.BranchName(reward.Branch) + "]" : string.Empty);
                complete.Badges.Add(new CanvasBadge(text, reward.Branch > 0 ? BranchColor(reward.Branch) : ViewPalette.Good));
            }

            if (hasState)
            {
                complete.Badges.Add(new CanvasBadge("live: " + state.StatusName + (state.Branch > 0 ? " via " + quest.BranchName(state.Branch) : string.Empty), state.Status == 2 ? ViewPalette.Good : ViewPalette.Info));
                complete.Highlighted = state.Status == 2;
            }

            cards.Add(complete);
            _canvas.SetGraph(cards, edges, null, keepPositions: false, frame: true);
            if (_selectedId != null)
            {
                _canvas.Select(_selectedId);
            }

            _canvas.StatusText = quest.Name + ": " + quest.Stages.Count + " stages, " + quest.Objectives.Count + " objectives, " + quest.Rewards.Count + " rewards" + (hasState ? " - live " + state.StatusName + " stage " + state.Stage : string.Empty);
        }

        private void BuildInspector()
        {
            _inspector.Clear();
            QuestDocument? quest = Document;
            if (quest == null)
            {
                return;
            }

            TextField title = new TextField("Title") { value = quest.Title };
            title.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (Document != null && title.value != Document.Title)
                {
                    ApplyEdit(ViewEdits.Build("Quest: title", new[] { QuestEdits.SetTitle(Document, title.value) }));
                }
            });
            _inspector.Add(title);
            CanvasNode? card = _canvas.Selected;
            if (card?.Payload is QuestStage stage)
            {
                _inspector.Add(Text("Stage " + stage.Index, 12f, FontStyle.Bold));
                AddElementField("stages", stage.Index, "title", "Title", stage.Title);
                AddElementField("stages", stage.Index, "description", "Description", stage.Description);
                AddElementInt("stages", stage.Index, "next", "Next (-1 = following)", stage.Next);
                BuildAddObjective(stage.Index);
            }
            else if (card?.Payload is QuestObjective objective)
            {
                _inspector.Add(Text("Objective " + objective.Index + " (" + objective.Kind + ")", 12f, FontStyle.Bold));
                AddElementField("objectives", objective.Index, "text", "Text", objective.Text);
                AddElementInt("objectives", objective.Index, "required", "Required", objective.Required);
                AddElementInt("objectives", objective.Index, "branch", "Branch", objective.Branch);
            }
            else if (card?.Id == CompleteId)
            {
                for (int i = 0; i < quest.Rewards.Count; i++)
                {
                    QuestReward reward = quest.Rewards[i];
                    _inspector.Add(Text("Reward " + i + ": " + reward.Kind + " " + (reward.Target == null ? "?" : IndexGraph.LeafOf(reward.Target)), 11f, FontStyle.Bold));
                    AddElementInt("rewards", i, "value", "Value", reward.Value);
                    AddElementInt("rewards", i, "branch", "Branch", reward.Branch);
                }

                BuildLinkReward();
            }
            else
            {
                _inspector.Add(Text("Select a stage, an objective or the completion card. Arrow keys move between cards."));
            }
        }

        private void AddElementField(string list, int index, string member, string label, string current)
        {
            TextField field = new TextField(label) { value = current };
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (Document != null && field.value != current)
                {
                    ApplyEdit(ViewEdits.Build("Quest: " + list + "[" + index + "]." + member, new[] { QuestEdits.SetElement(Document, list, index, member, field.value) }));
                }
            });
            _inspector.Add(field);
        }

        private void AddElementInt(string list, int index, string member, string label, int current)
        {
            IntegerField field = new IntegerField(label) { value = current };
            field.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (Document != null && field.value != current)
                {
                    ApplyEdit(ViewEdits.Build("Quest: " + list + "[" + index + "]." + member, new[] { QuestEdits.SetElement(Document, list, index, member, field.value) }));
                }
            });
            _inspector.Add(field);
        }

        private void BuildAddObjective(int stage)
        {
            IndexGraph graph = Context.Graph();
            _inspector.Add(Text("Add objective", 11f, FontStyle.Bold));
            List<string> kinds = new List<string> { "Talk", "Collect", "Reach", "Interact", "Fact" };
            PopupField<string> kind = new PopupField<string>("Kind", kinds, 4);
            SubjectPicker target = new SubjectPicker(graph, "Target", "dialogue.graph", "inventory.item", "world.region", "narrative.fact");
            TextField entity = new TextField("Entity id (interact)");
            IntegerField required = new IntegerField("Required") { value = 1 };
            IntegerField branch = new IntegerField("Branch") { value = 0 };
            TextField text = new TextField("Text");
            _inspector.Add(kind);
            _inspector.Add(target.Field);
            _inspector.Add(entity);
            _inspector.Add(required);
            _inspector.Add(branch);
            _inspector.Add(text);
            _inspector.Add(new Button(() =>
            {
                if (Document != null)
                {
                    ApplyEdit(ViewEdits.Build("Quest: add objective", new[] { QuestEdits.AddObjective(Document, stage, kind.value, target.Selected, entity.value, required.value, branch.value, text.value) }));
                }
            }) { text = "Add objective" });
        }

        private void BuildLinkReward()
        {
            IndexGraph graph = Context.Graph();
            _inspector.Add(Text("Link reward", 11f, FontStyle.Bold));
            PopupField<string> kind = new PopupField<string>("Kind", new List<string> { "Item", "Fact" }, 0);
            SubjectPicker target = new SubjectPicker(graph, "Target", "inventory.item", "narrative.fact");
            IntegerField value = new IntegerField("Value") { value = 1 };
            IntegerField branch = new IntegerField("Branch") { value = 0 };
            _inspector.Add(kind);
            _inspector.Add(target.Field);
            _inspector.Add(value);
            _inspector.Add(branch);
            _inspector.Add(new Button(() =>
            {
                if (Document != null && target.Selected != null)
                {
                    ApplyEdit(ViewEdits.Build("Quest: link reward", new[] { QuestEdits.LinkReward(Document, kind.value, target.Selected, value.value, branch.value) }));
                }
            }) { text = "Link reward" });
        }

        private void BuildRules(IndexGraph graph)
        {
            _rules.Clear();
            QuestDocument quest = Document!;
            List<string> rules = QuestRules(graph, quest.Ref.IdentityKey);
            RuleKeys = rules;
            if (rules.Count == 0)
            {
                _rules.Add(Text("No rule references this quest."));
            }

            HashSet<string> ruleRefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (string key in rules)
            {
                IndexNode? rule = graph.Node(key);
                if (rule == null)
                {
                    continue;
                }

                ruleRefs.Add(rule.Ref.AuthoringId ?? string.Empty);
                ruleRefs.Add(graph.NameOf(key));
                Foldout foldout = new Foldout { text = graph.NameOf(key), value = false };
                Label explain = Text(string.Empty, 10f);
                explain.selection.isSelectable = true;
                foldout.Add(new Button(() =>
                {
                    explain.text = Context.Tools.Invoke("logic.explain", rule.Ref, new JObject()).Text;
                }) { text = "logic.explain" });
                foldout.Add(explain);
                foldout.Add(new Button(() => Context.Selection.Select(new[] { rule.Ref })) { text = "Select" });
                _rules.Add(foldout);
            }

            if (Context.IsPlaying)
            {
                _rules.Add(Text("Last decisions (running world)", 11f, FontStyle.Bold));
                int shown = 0;
                IReadOnlyList<ExplainEntry> recent = Context.Gameplay.RecentExplain(64);
                for (int i = recent.Count - 1; i >= 0 && shown < 12; i--)
                {
                    ExplainEntry entry = recent[i];
                    if (ruleRefs.Contains(entry.RuleRef) || ruleRefs.Contains(entry.RuleName))
                    {
                        _rules.Add(Text(entry.ToString(), 10f));
                        shown++;
                    }
                }

                if (shown == 0)
                {
                    _rules.Add(Text(Context.Gameplay.HasNarrative ? "No decisions for these rules yet." : "The running world has no narrative plugins (" + Context.Gameplay.Describe + ").", 10f));
                }
            }
        }

        /// <summary>The logic.rule nodes referencing <paramref name="questKey"/> directly or through a condition/action set.</summary>
        public static List<string> QuestRules(IndexGraph graph, string questKey)
        {
            List<string> rules = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (GraphLink link in graph.Incoming(questKey))
            {
                string from = link.FromKey;
                if (graph.TypeOf(from) == "logic.rule")
                {
                    if (seen.Add(from))
                    {
                        rules.Add(from);
                    }

                    continue;
                }

                string type = graph.TypeOf(from);
                if (type == "logic.conditionSet" || type == "logic.actionSet")
                {
                    foreach (GraphLink second in graph.Incoming(from))
                    {
                        if (graph.TypeOf(second.FromKey) == "logic.rule" && seen.Add(second.FromKey))
                        {
                            rules.Add(second.FromKey);
                        }
                    }
                }
            }

            return rules;
        }

        private static string TargetText(QuestObjective objective, IndexGraph graph)
        {
            if (string.Equals(objective.Kind, "Interact", StringComparison.OrdinalIgnoreCase))
            {
                string key = "auth:" + objective.TargetEntityId;
                return graph.Contains(key) ? graph.NameOf(key) : objective.TargetEntityId;
            }

            return objective.Target == null ? "?" : AuthoredData.Display(StudioJson.ToToken(objective.Target), graph);
        }

        private static Color BranchColor(int branch)
        {
            switch (branch % 4)
            {
                case 1: return ViewPalette.Purple;
                case 2: return ViewPalette.Teal;
                case 3: return ViewPalette.Bad;
                default: return ViewPalette.Info;
            }
        }

        private static string StageId(int index) => "s" + index.ToString(CultureInfo.InvariantCulture);

        private static string ObjectiveId(int index) => "o" + index.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>A popup over the index nodes of some types (subject pickers in forms).</summary>
    public sealed class SubjectPicker
    {
        private readonly List<AuthoringRef?> _refs = new List<AuthoringRef?> { null };

        public SubjectPicker(IndexGraph graph, string label, params string[] types)
        {
            List<string> labels = new List<string> { "(none)" };
            foreach (string type in types)
            {
                foreach (IndexNode node in graph.OfType(type))
                {
                    _refs.Add(node.Ref);
                    labels.Add(graph.NameOf(node.Ref.IdentityKey) + "  (" + type + ")");
                }
            }

            Field = new PopupField<string>(label, labels, 0);
        }

        public PopupField<string> Field { get; }

        public AuthoringRef? Selected => Field.index >= 0 && Field.index < _refs.Count ? _refs[Field.index] : null;
    }

    public sealed class QuestsWindow : StudioViewWindow
    {
        protected override string ViewTitle => "Quests";

        [MenuItem(StudioViewIds.QuestsMenu, false, 2102)]
        public static void OpenWindow() => Open<QuestsWindow>();

        protected override StudioViewBase CreateView(StudioViewContext context) => new QuestsView(context);
    }
}
