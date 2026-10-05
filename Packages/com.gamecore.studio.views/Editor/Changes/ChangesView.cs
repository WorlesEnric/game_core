// GameCore.Studio.Views - W-VIEW-06 Changes (GameCore/Studio/Changes, F11 / SR-11.x).
// Four tabs over the edit engine and the project:
//   Pending       the pending change sets (staged previews, journaled candidates); the selected one is inspected as an
//                 op tree (tool, target, args, current stamp, per-op diagnostics with a "go to" for their where, and
//                 data {expected, actual} for conflicts) next to a dependsOn graph of its operations;
//   Diagnostics   the validators' results (ValidatorConsole), the inspected change set's and the last apply report's
//                 diagnostics, filterable by source and text; "go to" selects the where;
//   Dependencies  the com.gamecore.* package graph by layer (kernel, rules, unity, gameplay, studio) with
//                 check_package_metadata problems (PackageGraph) drawn in red;
//   Journal       the journal timeline grouped by day with an origin filter and artifact sizes; selecting two entries
//                 shows their before/after stamp diff.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using GameCore.Studio.Views.Canvas;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.Views
{
    public sealed class ChangesView : StudioViewBase
    {
        public const string PendingTab = "Pending";
        public const string DiagnosticsTab = "Diagnostics";
        public const string DependenciesTab = "Dependencies";
        public const string JournalTab = "Journal";

        private static readonly IReadOnlyList<string> TabNames = new[] { PendingTab, DiagnosticsTab, DependenciesTab, JournalTab };

        private readonly Dictionary<string, ToolbarToggle> _tabs = new Dictionary<string, ToolbarToggle>(StringComparer.Ordinal);
        private readonly Dictionary<string, VisualElement> _pages = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly List<ChangeSet> _pending = new List<ChangeSet>();
        private readonly ListView _pendingList;
        private readonly ScrollView _opTree;
        private readonly GraphCanvas _dependsOn;
        private readonly List<DiagnosticRow> _diagnostics = new List<DiagnosticRow>();
        private readonly List<DiagnosticRow> _visibleDiagnostics = new List<DiagnosticRow>();
        private readonly ListView _diagnosticList;
        private readonly PopupField<string> _diagnosticSource;
        private string _diagnosticFilter = string.Empty;
        private readonly GraphCanvas _packages;
        private readonly ListView _packageProblems;
        private readonly List<string> _problemLines = new List<string>();
        private readonly List<TimelineEntry> _timeline = new List<TimelineEntry>();
        private readonly List<object> _timelineRows = new List<object>();
        private readonly ListView _journalList;
        private readonly PopupField<string> _origin;
        private readonly ScrollView _diff;
        private IReadOnlyList<DiagnosticRow> _validatorRows = Array.Empty<DiagnosticRow>();
        private bool _validatorsRan;
        private string _tab = PendingTab;

        public ChangesView(StudioViewContext context)
            : base(context, StudioViewIds.Changes)
        {
            foreach (string tab in TabNames)
            {
                string name = tab;
                ToolbarToggle toggle = new ToolbarToggle { text = tab, value = tab == _tab };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        ShowTab(name);
                    }
                    else if (_tab == name)
                    {
                        toggle.SetValueWithoutNotify(true);
                    }
                });
                _tabs[tab] = toggle;
                Toolbar.Add(toggle);
            }

            AddSpacer();
            AddButton("Run validators", () =>
            {
                RunValidators();
                ShowTab(DiagnosticsTab);
            });

            // Pending.
            VisualElement pending = Page(PendingTab);
            _pendingList = new ListView(_pending, 36f, () => TwoLineRow(), (element, index) =>
            {
                ChangeSet changeSet = _pending[index];
                element.Q<Label>("title").text = changeSet.Intent.Text;
                element.Q<Label>("detail").text = changeSet.Id + "  " + changeSet.EffectiveState + "  " + changeSet.Operations.Count + " ops  " + changeSet.Intent.Origin;
            }) { selectionType = SelectionType.Single };
            _pendingList.style.width = 300f;
            _pendingList.style.flexShrink = 0f;
            _pendingList.selectionChanged += items =>
            {
                foreach (object item in items)
                {
                    if (item is ChangeSet changeSet)
                    {
                        Inspect(changeSet);
                    }
                }
            };
            pending.Add(_pendingList);
            _opTree = new ScrollView();
            _opTree.style.flexGrow = 1f;
            _opTree.style.paddingLeft = 6f;
            pending.Add(_opTree);
            _dependsOn = new GraphCanvas();
            _dependsOn.style.width = 300f;
            _dependsOn.style.flexGrow = 0f;
            pending.Add(_dependsOn);

            // Diagnostics.
            VisualElement diagnostics = Page(DiagnosticsTab);
            diagnostics.style.flexDirection = FlexDirection.Column;
            Toolbar filter = new Toolbar();
            _diagnosticSource = new PopupField<string>(new List<string> { "all sources" }, 0);
            _diagnosticSource.style.width = 200f;
            _diagnosticSource.RegisterValueChangedCallback(_ => FilterDiagnostics());
            filter.Add(_diagnosticSource);
            ToolbarSearchField search = new ToolbarSearchField();
            search.RegisterValueChangedCallback(evt =>
            {
                _diagnosticFilter = evt.newValue ?? string.Empty;
                FilterDiagnostics();
            });
            filter.Add(search);
            diagnostics.Add(filter);
            _diagnosticList = new ListView(_visibleDiagnostics, 36f, () => TwoLineRow(), (element, index) =>
            {
                DiagnosticRow row = _visibleDiagnostics[index];
                element.Q<Label>("title").text = row.Code + (row.OpId != null ? "  (op " + row.OpId + ")" : string.Empty) + "  [" + row.Source + "]";
                element.Q<Label>("detail").text = row.Message + (row.IsConflict ? "  expected " + row.Expected + ", actual " + row.Actual : string.Empty);
            }) { selectionType = SelectionType.Single };
            _diagnosticList.style.flexGrow = 1f;
            _diagnosticList.itemsChosen += items =>
            {
                foreach (object item in items)
                {
                    if (item is DiagnosticRow row)
                    {
                        GoTo(row);
                    }
                }
            };
            diagnostics.Add(_diagnosticList);

            // Dependencies.
            VisualElement dependencies = Page(DependenciesTab);
            _packages = new GraphCanvas();
            dependencies.Add(_packages);
            _packageProblems = new ListView(_problemLines, 20f, () => new Label(), (element, index) => ((Label)element).text = _problemLines[index]);
            _packageProblems.style.width = 380f;
            _packageProblems.style.flexShrink = 0f;
            dependencies.Add(_packageProblems);

            // Journal.
            VisualElement journal = Page(JournalTab);
            VisualElement left = new VisualElement();
            left.style.width = 460f;
            left.style.flexShrink = 0f;
            _origin = new PopupField<string>("Origin", new List<string> { "all", "Manual", "Agent", "Voice", "Replay" }, 0);
            _origin.RegisterValueChangedCallback(_ => LoadTimeline());
            left.Add(_origin);
            _journalList = new ListView(_timelineRows, 34f, () => TwoLineRow(), BindTimelineRow) { selectionType = SelectionType.Multiple };
            _journalList.style.flexGrow = 1f;
            _journalList.selectionChanged += _ => ShowDiff();
            left.Add(_journalList);
            journal.Add(left);
            _diff = new ScrollView();
            _diff.style.flexGrow = 1f;
            _diff.style.paddingLeft = 6f;
            journal.Add(_diff);
            ShowTab(_tab);
        }

        /// <summary>The inspected change set.</summary>
        public ChangeSetInspection? Inspection { get; private set; }

        public IReadOnlyList<ChangeSet> PendingChangeSets => _pending;

        public IReadOnlyList<DiagnosticRow> Diagnostics => _diagnostics;

        public PackageGraph? Packages { get; private set; }

        public IReadOnlyList<TimelineEntry> Timeline => _timeline;

        public IReadOnlyList<StampDiffRow> LastDiff { get; private set; } = Array.Empty<StampDiffRow>();

        public GraphCanvas DependsOnCanvas => _dependsOn;

        public string CurrentTab => _tab;

        /// <summary>Every label text of the op tree (tests read what the inspector rendered).</summary>
        public string RenderedOpTree
        {
            get
            {
                StringBuilder text = new StringBuilder();
                _opTree.Query<Label>().ForEach(label => text.Append(label.text).Append('\n'));
                _opTree.Query<Foldout>().ForEach(foldout => text.Append(foldout.text).Append('\n'));
                return text.ToString();
            }
        }

        public void ShowTab(string tab)
        {
            _tab = tab;
            foreach (KeyValuePair<string, ToolbarToggle> pair in _tabs)
            {
                pair.Value.SetValueWithoutNotify(pair.Key == tab);
            }

            foreach (KeyValuePair<string, VisualElement> page in _pages)
            {
                page.Value.style.display = page.Key == tab ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (tab == DependenciesTab && Packages == null)
            {
                LoadPackages();
            }
            else if (tab == JournalTab)
            {
                LoadTimeline();
            }
            else if (tab == DiagnosticsTab && !_validatorsRan)
            {
                RunValidators();
            }
        }

        /// <summary>Inspects a change set (stages it without writing anything) and renders the op tree.</summary>
        public ChangeSetInspection Inspect(ChangeSet changeSet)
        {
            Inspection = ChangeSetInspection.Inspect(Context.Runtime, changeSet);
            RenderInspection();
            MergeDiagnostics();
            return Inspection;
        }

        public void RunValidators()
        {
            _validatorRows = ValidatorConsole.Run(Context.Runtime, Context.Graph());
            _validatorsRan = true;
            MergeDiagnostics();
        }

        public void LoadPackages()
        {
            Packages = PackageGraph.ForProject();
            ShowPackages(Packages);
        }

        /// <summary>Shows a package graph (the project's, or one built from explicit directories in tests).</summary>
        public void ShowPackages(PackageGraph graph)
        {
            Packages = graph;
            List<CanvasNode> cards = new List<CanvasNode>();
            List<CanvasEdge> edges = new List<CanvasEdge>();
            int[] rows = new int[PackageGraph.LayerNames.Count];
            _problemLines.Clear();
            foreach (PackageNode package in graph.Packages)
            {
                int layer = Mathf.Clamp(package.Layer, 0, rows.Length - 1);
                CanvasNode card = new CanvasNode(package.Name, package.Name.Replace("com.gamecore.", string.Empty))
                {
                    Subtitle = PackageGraph.LayerNames[layer] + "  " + package.Version,
                    Accent = package.Problems.Count > 0 ? ViewPalette.Bad : LayerColor(layer),
                    Position = new Vector2(layer * 300f, rows[layer]++ * 70f),
                    HasPosition = true,
                    Size = new Vector2(240f, 54f),
                    Payload = package,
                };
                if (package.Problems.Count > 0)
                {
                    card.Badges.Add(new CanvasBadge(package.Problems.Count + " problem(s)", ViewPalette.Bad));
                }

                cards.Add(card);
                foreach (string problem in package.Problems)
                {
                    _problemLines.Add(package.Name + ": " + problem);
                }
            }

            foreach (PackageNode package in graph.Packages)
            {
                foreach (string dependency in package.Declared)
                {
                    PackageNode? target = graph.Find(dependency);
                    if (target == null)
                    {
                        continue;
                    }

                    bool upward = target.Layer > package.Layer && package.Layer == 0;
                    bool extra = !package.Derived.Contains(dependency);
                    edges.Add(new CanvasEdge(package.Name, dependency) { Color = upward || extra ? ViewPalette.Bad : new Color(0.55f, 0.6f, 0.68f, 0.45f), Width = upward || extra ? 2.5f : 1f });
                }

                foreach (string missing in package.Derived)
                {
                    if (graph.Find(missing) != null && !Contains(package.Declared, missing))
                    {
                        edges.Add(new CanvasEdge(package.Name, missing, "missing") { Color = ViewPalette.Bad, Width = 2.5f });
                    }
                }
            }

            if (_problemLines.Count == 0)
            {
                _problemLines.Add("No package metadata problems (" + graph.Packages.Count + " packages).");
            }

            _packages.SetGraph(cards, edges, null, keepPositions: false, frame: true);
            _packages.StatusText = graph.Packages.Count + " packages, " + graph.ProblemCount + " problems; layers left to right: " + string.Join(", ", PackageGraph.LayerNames);
            _packageProblems.RefreshItems();
        }

        public void LoadTimeline()
        {
            _timeline.Clear();
            _timeline.AddRange(JournalTimeline.Load(Context.Runtime, _origin.index <= 0 ? null : _origin.value));
            _timelineRows.Clear();
            string? day = null;
            foreach (TimelineEntry entry in _timeline)
            {
                if (entry.Day != day)
                {
                    day = entry.Day;
                    _timelineRows.Add(day);
                }

                _timelineRows.Add(entry);
            }

            _journalList.RefreshItems();
        }

        /// <summary>The stamp diff between two timeline entries (also rendered).</summary>
        public IReadOnlyList<StampDiffRow> Diff(TimelineEntry a, TimelineEntry b)
        {
            LastDiff = JournalTimeline.Diff(a, b);
            _diff.Clear();
            _diff.Add(Text("A " + a.Id + " (" + a.Record.Intent + ")\nB " + b.Id + " (" + b.Record.Intent + ")", 11f, FontStyle.Bold));
            foreach (StampDiffRow row in LastDiff)
            {
                Label line = Text(row.Key + "\n  A " + Short(row.BeforeA) + " -> " + Short(row.AfterA) + "   B " + Short(row.BeforeB) + " -> " + Short(row.AfterB) + (row.ChangedBetween ? "   (changed between A and B)" : string.Empty), 10f);
                if (row.ChangedBetween)
                {
                    line.style.color = ViewPalette.Warn;
                }

                _diff.Add(line);
            }

            if (LastDiff.Count == 0)
            {
                _diff.Add(Text("Neither entry records target stamps."));
            }

            return LastDiff;
        }

        protected override void OnRefresh()
        {
            _pending.Clear();
            _pending.AddRange(ChangeSetInspection.Pending(Context.Runtime));
            _pendingList.RefreshItems();
            if (_tab == JournalTab)
            {
                LoadTimeline();
            }

            MergeDiagnostics();
            if (_pending.Count == 0 && Inspection == null)
            {
                _opTree.Clear();
                _opTree.Add(Text("No pending change sets. Candidates staged by the Studio (or journaled as Candidate) appear here; Journal shows applied ones."));
            }
        }

        private void RenderInspection()
        {
            _opTree.Clear();
            ChangeSetInspection? inspection = Inspection;
            if (inspection == null)
            {
                return;
            }

            ChangeSet changeSet = inspection.ChangeSet;
            _opTree.Add(Text(changeSet.Intent.Text, 13f, FontStyle.Bold));
            _opTree.Add(Text(changeSet.Id + "  " + changeSet.EffectiveState + "  policy " + changeSet.EffectivePolicy + "  origin " + changeSet.Intent.Origin + "  " + (inspection.Ok ? "stages cleanly" : inspection.Diagnostics.Count + " diagnostic(s), " + inspection.ConflictCount + " conflict(s)"), 10f));
            foreach (DiagnosticRow row in inspection.Diagnostics)
            {
                if (row.OpId == null)
                {
                    _opTree.Add(DiagnosticElement(row));
                }
            }

            foreach (InspectedOperation operation in inspection.Operations)
            {
                Foldout foldout = new Foldout { text = operation.OpId + "  " + operation.Tool + "  ->  " + operation.TargetName + (operation.Diagnostics.Count > 0 ? "  (" + operation.Diagnostics.Count + " diagnostic(s))" : string.Empty), value = true };
                if (operation.HasConflict)
                {
                    foldout.style.color = ViewPalette.Bad;
                }

                if (operation.Operation.Target != null)
                {
                    foldout.Add(Text("target " + operation.Operation.Target.IdentityKey + (operation.Operation.Target.Stamp != null ? "  stamp " + operation.Operation.Target.Stamp : string.Empty), 10f));
                }

                if (operation.CurrentStamp != null)
                {
                    foldout.Add(Text("current stamp " + operation.CurrentStamp, 10f));
                }

                if (operation.DependsOn.Count > 0)
                {
                    foldout.Add(Text("dependsOn " + string.Join(", ", operation.DependsOn), 10f));
                }

                if (operation.Operation.Args != null)
                {
                    Label args = Text("args " + AuthoredData.Short(operation.Operation.Args.ToString(Newtonsoft.Json.Formatting.None), 400), 10f);
                    args.selection.isSelectable = true;
                    foldout.Add(args);
                }

                foreach (DiagnosticRow row in operation.Diagnostics)
                {
                    foldout.Add(DiagnosticElement(row));
                }

                _opTree.Add(foldout);
            }

            List<CanvasNode> cards = new List<CanvasNode>();
            List<CanvasEdge> edges = new List<CanvasEdge>();
            foreach (InspectedOperation operation in inspection.Operations)
            {
                CanvasNode card = new CanvasNode(operation.OpId, operation.OpId + " " + operation.Tool)
                {
                    Subtitle = operation.TargetName,
                    Accent = operation.HasConflict ? ViewPalette.Bad : (operation.Diagnostics.Count > 0 ? ViewPalette.Warn : ViewPalette.Good),
                    Size = new Vector2(200f, 50f),
                    Payload = operation,
                };
                cards.Add(card);
                foreach (string dependency in operation.DependsOn)
                {
                    edges.Add(new CanvasEdge(dependency, operation.OpId, "dependsOn"));
                }
            }

            _dependsOn.SetGraph(cards, edges, null, keepPositions: false, vertical: true, frame: true);
            _dependsOn.StatusText = inspection.Operations.Count + " ops";
        }

        private VisualElement DiagnosticElement(DiagnosticRow row)
        {
            VisualElement element = new VisualElement();
            element.style.flexDirection = FlexDirection.Row;
            element.style.marginLeft = 8f;
            element.style.marginTop = 2f;
            Label chip = Chip(row.Code, row.IsConflict ? ViewPalette.Bad : ViewPalette.Warn);
            element.Add(chip);
            string text = row.Message + (row.IsConflict ? "\ndata {expected " + row.Expected + ", actual " + row.Actual + "}" : string.Empty) + (row.Diagnostic.Hint != null ? "\nhint: " + row.Diagnostic.Hint : string.Empty);
            Label message = Text(text, 10f);
            message.style.flexShrink = 1f;
            element.Add(message);
            if (row.Where != null)
            {
                element.Add(new Button(() => GoTo(row)) { text = "go to", tooltip = row.Where.ToString() });
            }

            return element;
        }

        private void GoTo(DiagnosticRow row)
        {
            if (row.Where != null)
            {
                Context.Selection.Focus(row.Where);
            }
        }

        private void MergeDiagnostics()
        {
            _diagnostics.Clear();
            _diagnostics.AddRange(_validatorRows);
            if (Inspection != null)
            {
                _diagnostics.AddRange(Inspection.Diagnostics);
            }

            ApplyReport? last = Context.Edits.LastReport;
            if (last != null)
            {
                foreach (Diagnostic diagnostic in last.Diagnostics)
                {
                    _diagnostics.Add(new DiagnosticRow("last apply", diagnostic, null) { Where = diagnostic.Where?.Ref });
                }
            }

            SortedSet<string> sources = new SortedSet<string>(StringComparer.Ordinal);
            foreach (DiagnosticRow row in _diagnostics)
            {
                sources.Add(row.Source);
            }

            List<string> choices = new List<string> { "all sources" };
            choices.AddRange(sources);
            string current = _diagnosticSource.value;
            _diagnosticSource.choices = choices;
            _diagnosticSource.SetValueWithoutNotify(choices.Contains(current) ? current : choices[0]);
            FilterDiagnostics();
        }

        private void FilterDiagnostics()
        {
            _visibleDiagnostics.Clear();
            string source = _diagnosticSource.index <= 0 ? string.Empty : _diagnosticSource.value;
            foreach (DiagnosticRow row in _diagnostics)
            {
                if (source.Length > 0 && row.Source != source)
                {
                    continue;
                }

                if (_diagnosticFilter.Length > 0 && (row.Code + " " + row.Message).IndexOf(_diagnosticFilter, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                _visibleDiagnostics.Add(row);
            }

            _diagnosticList.RefreshItems();
            if (_tab == DiagnosticsTab)
            {
                SetStatus(_visibleDiagnostics.Count + " of " + _diagnostics.Count + " diagnostics");
            }
        }

        private void BindTimelineRow(VisualElement element, int index)
        {
            object row = _timelineRows[index];
            Label title = element.Q<Label>("title");
            Label detail = element.Q<Label>("detail");
            if (row is string day)
            {
                title.text = day;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                detail.text = string.Empty;
                return;
            }

            TimelineEntry entry = (TimelineEntry)row;
            title.style.unityFontStyleAndWeight = FontStyle.Normal;
            title.text = entry.Time.ToString("HH:mm:ss") + "  " + entry.Record.Intent;
            detail.text = entry.Record.State + "  " + entry.Origin + "  " + entry.OperationCount + " ops  journal " + JournalTimeline.Bytes(entry.JournalBytes) + (entry.ArtifactBytes > 0 ? "  artifacts " + JournalTimeline.Bytes(entry.ArtifactBytes) : string.Empty) + "  " + entry.Id;
        }

        private void ShowDiff()
        {
            List<TimelineEntry> chosen = new List<TimelineEntry>();
            foreach (object item in _journalList.selectedItems)
            {
                if (item is TimelineEntry entry)
                {
                    chosen.Add(entry);
                }
            }

            if (chosen.Count == 2)
            {
                chosen.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                Diff(chosen[0], chosen[1]);
            }
            else if (chosen.Count == 1 && chosen[0].Entry != null)
            {
                _diff.Clear();
                _diff.Add(new Button(() => Inspect(chosen[0].Entry!)) { text = "Inspect this change set" });
                _diff.Add(Text("Select a second entry (Ctrl/Cmd-click) to compare stamps.", 10f));
            }
        }

        private VisualElement Page(string tab)
        {
            VisualElement page = new VisualElement();
            page.style.flexGrow = 1f;
            page.style.flexDirection = FlexDirection.Row;
            _pages[tab] = page;
            Body.Add(page);
            return page;
        }

        private static VisualElement TwoLineRow()
        {
            VisualElement row = new VisualElement();
            row.style.paddingLeft = 4f;
            Label title = new Label { name = "title" };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.overflow = Overflow.Hidden;
            Label detail = new Label { name = "detail" };
            detail.style.fontSize = 10f;
            detail.style.overflow = Overflow.Hidden;
            row.Add(title);
            row.Add(detail);
            return row;
        }

        private static Color LayerColor(int layer)
        {
            switch (layer)
            {
                case 0: return ViewPalette.Info;
                case 1: return ViewPalette.Teal;
                case 2: return ViewPalette.Neutral;
                case 3: return ViewPalette.Good;
                default: return ViewPalette.Purple;
            }
        }

        private static bool Contains(IReadOnlyList<string> list, string value)
        {
            foreach (string item in list)
            {
                if (item == value)
                {
                    return true;
                }
            }

            return false;
        }

        private static string Short(string? stamp) => stamp == null ? "-" : (stamp.Length > 14 ? stamp.Substring(0, 14) : stamp);
    }

    public sealed class ChangesWindow : StudioViewWindow
    {
        protected override string ViewTitle => "Changes";

        [MenuItem(StudioViewIds.ChangesMenu, false, 2105)]
        public static void OpenWindow() => Open<ChangesWindow>();

        protected override StudioViewBase CreateView(StudioViewContext context) => new ChangesView(context);
    }
}
