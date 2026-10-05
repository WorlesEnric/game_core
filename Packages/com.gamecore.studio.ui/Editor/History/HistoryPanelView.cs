// GameCore.Studio.UI - the history panel (GameCore/Studio/History; SR-3.3, SADR-009, W-EDIT-03/05, W-REC-01): journal
// entries newest first (time, origin agent/manual/voice/replay, summary, state), Undo/Redo bound to HistoryService (an
// admission entry, mechanism.admit, is undone through StageAdmission.Undo: package removed, recompiled, catalog hash checked),
// Interrupted entries highlighted with Resume/Rollback, a target filter, open-in-journal (reveals the entry's JSON file)
// and the retained artifact list with sizes and a "retained" marker (referenced by a journal entry).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>One history row (read from the journal, cached by id and state).</summary>
    public sealed class HistoryRowInfo
    {
        public HistoryRowInfo(JournalRecord record, ChangeSet? entry, DateTime time)
        {
            Record = record;
            Entry = entry;
            Time = time;
        }

        public JournalRecord Record { get; }

        public ChangeSet? Entry { get; }

        public DateTime Time { get; }

        public string Id => Record.Id;

        public ChangeSetState State => Record.State;

        public string Origin => Entry == null ? "?" : Entry.Intent.Origin.ToString().ToLowerInvariant();

        public string Summary => Entry == null ? Record.Intent : Entry.Intent.Text + " (" + CandidateRequirements.Summary(Entry) + ")";

        /// <summary>True when any operation's target matches the filter (name/path/id substring, case-insensitive).</summary>
        public bool Matches(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter))
            {
                return true;
            }

            if (Summary.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (Entry == null)
            {
                return false;
            }

            foreach (Operation operation in Entry.Operations)
            {
                AuthoringRef? target = operation.Target;
                if (target == null)
                {
                    continue;
                }

                if ((target.Path?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                    || (target.AuthoringId?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0
                    || (target.Definition?.IndexOf(filter, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>The history panel.</summary>
    public sealed class HistoryPanelView : VisualElement
    {
        private readonly StudioUiContext _context;
        private readonly Dictionary<string, HistoryRowInfo> _cache = new Dictionary<string, HistoryRowInfo>(StringComparer.Ordinal);
        private readonly ScrollView _list;
        private readonly ScrollView _details;
        private readonly TextField _filter;
        private readonly Button _undo;
        private readonly Button _redo;
        private readonly Label _status;
        private string? _selected;
        private bool _attached;
        private bool _dirty;

        public HistoryPanelView(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            name = "history-panel";
            AddToClassList("gcs-history");

            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("gcs-row");
            toolbar.AddToClassList("gcs-toolbar");
            _undo = new Button(() => Undo(null)) { name = "history-undo", text = "Undo" };
            _redo = new Button(() => Redo(null)) { name = "history-redo", text = "Redo" };
            toolbar.Add(_undo);
            toolbar.Add(_redo);
            _filter = new TextField { name = "history-filter" };
            _filter.AddToClassList("gcs-grow");
            _filter.tooltip = "Filter by target (name, path, authoring id, definition) or intent text";
            _filter.RegisterValueChangedCallback(_ => Rebuild());
            toolbar.Add(new Label("Filter"));
            toolbar.Add(_filter);
            toolbar.Add(new Button(() =>
            {
                _context.Runtime.Journal.Rescan();
                _cache.Clear();
                Rebuild();
            }) { name = "history-rescan", text = "Rescan" });
            Add(toolbar);

            _status = new Label { name = "history-status" };
            _status.AddToClassList("gcs-status");
            Add(_status);

            VisualElement body = new VisualElement();
            body.AddToClassList("gcs-split");
            Add(body);
            _list = new ScrollView { name = "history-rows" };
            _list.AddToClassList("gcs-split__list");
            body.Add(_list);
            _details = new ScrollView { name = "history-details" };
            _details.AddToClassList("gcs-split__details");
            body.Add(_details);
            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        public string? SelectedId => _selected;

        /// <summary>The rows shown by the last rebuild (after the filter), newest first.</summary>
        public IReadOnlyList<HistoryRowInfo> Shown { get; private set; } = Array.Empty<HistoryRowInfo>();

        public string Filter
        {
            get => _filter.value ?? string.Empty;
            set
            {
                _filter.SetValueWithoutNotify(value ?? string.Empty);
                Rebuild();
            }
        }

        public string StatusText => _status.text ?? string.Empty;

        /// <summary>All history lifecycle dispatch belongs to HistoryService.</summary>
        public HistoryResult Undo(string? changeSetId)
        {
            HistoryResult result = _context.Runtime.History.Undo(changeSetId);
            _status.text = StudioStyles.Safe(Describe("Undo", result));
            Rebuild();
            return result;
        }

        public HistoryResult Redo(string? changeSetId)
        {
            HistoryResult result = _context.Runtime.History.Redo(changeSetId);
            _status.text = StudioStyles.Safe(Describe("Redo", result));
            Rebuild();
            return result;
        }

        public void Select(string changeSetId)
        {
            _selected = changeSetId;
            Rebuild();
        }

        /// <summary>Re-reads the journal listing and re-renders.</summary>
        public void Rebuild()
        {
            _dirty = false;
            Journal journal = _context.Runtime.Journal;
            HistoryService history = _context.Runtime.History;
            string? nextUndo = history.NextUndo;
            string? nextRedo = history.NextRedo;
            _undo.SetEnabled(nextUndo != null);
            _redo.SetEnabled(nextRedo != null);
            _undo.tooltip = StudioStyles.Safe(nextUndo != null ? "Undo " + nextUndo : "Nothing to undo");
            _redo.tooltip = StudioStyles.Safe(nextRedo != null ? "Redo " + nextRedo : "Nothing to redo");

            IReadOnlyList<JournalRecord> records = journal.List();
            List<HistoryRowInfo> shown = new List<HistoryRowInfo>();
            for (int i = records.Count - 1; i >= 0; i--)
            {
                HistoryRowInfo row = Info(records[i]);
                if (row.Matches(Filter))
                {
                    shown.Add(row);
                }
            }

            Shown = shown;
            _list.Clear();
            foreach (HistoryRowInfo row in shown)
            {
                _list.Add(BuildRow(row));
            }

            if (shown.Count == 0)
            {
                _list.Add(StudioStyles.Text(records.Count == 0 ? "The journal is empty. Every applied change set (manual or agent) appears here." : "No entry matches the filter.", "gcs-muted"));
            }

            BuildDetails();
        }

        private HistoryRowInfo Info(JournalRecord record)
        {
            if (_cache.TryGetValue(record.Id, out HistoryRowInfo? cached) && cached.State == record.State)
            {
                return cached;
            }

            ChangeSet? entry = null;
            try
            {
                entry = _context.Runtime.Journal.Read(record.Id);
            }
            catch (JsonException)
            {
                entry = null;
            }

            HistoryRowInfo info = new HistoryRowInfo(record, entry, Journal.TimeOf(record.Id));
            _cache[record.Id] = info;
            return info;
        }

        private VisualElement BuildRow(HistoryRowInfo row)
        {
            VisualElement element = new VisualElement { name = "history-" + row.Id, focusable = true };
            element.AddToClassList("gcs-tray__row");
            element.EnableInClassList("gcs-tray__row--selected", row.Id == _selected);
            element.EnableInClassList("gcs-history__row--interrupted", row.State == ChangeSetState.Interrupted);
            element.Add(new Label(StudioStyles.Safe(row.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture))));
            element.Add(StudioStyles.Badge(row.Origin, row.Origin));
            element.Add(StudioStyles.Badge(row.State.ToString(), row.State.ToString().ToLowerInvariant()));
            Label summary = new Label(StudioStyles.Safe(row.Summary)) { tooltip = StudioStyles.Safe(row.Summary) };
            summary.AddToClassList("gcs-tray__intent");
            element.Add(summary);
            if (row.State == ChangeSetState.Interrupted)
            {
                string id = row.Id;
                element.Add(new Button(() => Recover(id, true)) { text = "Resume" });
                element.Add(new Button(() => Recover(id, false)) { text = "Rollback" });
            }

            string selectId = row.Id;
            element.RegisterCallback<PointerDownEvent>(_ => Select(selectId));
            element.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                {
                    Select(selectId);
                }
            });
            return element;
        }

        public void Recover(string id, bool resume)
        {
            HistoryResult result = resume ? _context.Runtime.History.ResumeInterrupted(id) : _context.Runtime.History.RollbackInterrupted(id);
            _status.text = StudioStyles.Safe(Describe(resume ? "Resume" : "Rollback", result));
            _cache.Remove(id);
            Rebuild();
        }

        private void BuildDetails()
        {
            _details.Clear();
            if (_selected == null || !_cache.TryGetValue(_selected, out HistoryRowInfo? row))
            {
                BuildArtifacts();
                return;
            }

            ChangeSet? entry = row.Entry;
            _details.Add(StudioStyles.Header(row.Id));
            _details.Add(StudioStyles.Text(row.Summary));
            _details.Add(StudioStyles.Text("State " + row.State + " · origin " + row.Origin + " · " + row.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            VisualElement buttons = new VisualElement();
            buttons.AddToClassList("gcs-row");
            Button undo = new Button(() => Undo(row.Id)) { text = "Undo this" };
            undo.SetEnabled(row.State == ChangeSetState.Applied);
            buttons.Add(undo);
            Button redo = new Button(() => Redo(row.Id)) { text = "Redo this" };
            redo.SetEnabled(row.State == ChangeSetState.Undone);
            buttons.Add(redo);
            string path = row.Record.Path;
            buttons.Add(new Button(() => EditorUtility.RevealInFinder(path)) { name = "history-open-journal", text = "Open in journal", tooltip = StudioStyles.Safe(path) });
            _details.Add(buttons);
            if (entry != null)
            {
                if (entry.Links?.EtosTasks != null && entry.Links.EtosTasks.Count > 0)
                {
                    _details.Add(StudioStyles.Text("etos tasks: " + string.Join(", ", entry.Links.EtosTasks)));
                }

                if (entry.Links?.GameCoreOps != null && entry.Links.GameCoreOps.Count > 0)
                {
                    _details.Add(StudioStyles.Text("GameCore ops: " + string.Join(", ", entry.Links.GameCoreOps)));
                }

                foreach (OperationOutcome outcome in entry.Outcomes ?? Array.Empty<OperationOutcome>())
                {
                    _details.Add(StudioStyles.Text(outcome.OpId + ": " + outcome.Status + (outcome.Code != null ? " " + outcome.Code : string.Empty) + (outcome.Detail != null ? " - " + outcome.Detail : string.Empty)));
                }

                TextField json = new TextField { name = "history-json", multiline = true, isReadOnly = true, value = StudioStyles.Safe(StudioJson.Serialize(entry)) };
                json.AddToClassList("gcs-code");
                _details.Add(json);
            }

            BuildArtifacts();
        }

        private void BuildArtifacts()
        {
            ArtifactStore store = _context.Runtime.Artifacts;
            IReadOnlyList<ArtifactManifestEntry> entries = store.Entries;
            HashSet<string> retained = new HashSet<string>(StringComparer.Ordinal);
            foreach (HistoryRowInfo info in _cache.Values)
            {
                if (info.Entry?.Artifacts == null)
                {
                    continue;
                }

                foreach (ArtifactRef artifact in info.Entry.Artifacts)
                {
                    retained.Add(artifact.Sha256);
                }
            }

            Foldout foldout = new Foldout { name = "history-artifacts", text = StudioStyles.Safe("Artifacts (" + entries.Count + ")"), value = _selected == null };
            foreach (ArtifactManifestEntry artifact in entries)
            {
                string label = artifact.Sha256.Substring(0, 12) + "  " + (artifact.Name ?? "-") + "  " + Bytes(artifact.Bytes) + "  " + (artifact.MediaType ?? string.Empty)
                    + (retained.Contains(artifact.Sha256) ? "  [retained]" : string.Empty) + (artifact.Large == true ? "  [large, not in git]" : string.Empty);
                foldout.Add(StudioStyles.Text(label));
            }

            _details.Add(foldout);
        }

        private static string Bytes(long bytes)
        {
            if (bytes >= 1024 * 1024)
            {
                return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MiB";
            }

            return bytes >= 1024 ? (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KiB" : bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        private static string Describe(string action, HistoryResult result)
        {
            string text = action + (result.Ok ? " done" : " refused") + (result.ChangeSetId != null ? " (" + result.ChangeSetId + ")" : string.Empty);
            if (result.Report != null)
            {
                text += " in " + result.Report.Milliseconds.ToString("0.0", CultureInfo.InvariantCulture) + " ms";
            }

            if (result.Diagnostics.Count > 0)
            {
                text += ": " + result.Diagnostics[0].Code + " " + result.Diagnostics[0].Message;
            }

            return text;
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.Runtime.Journal.Written += OnWritten;
            schedule.Execute(() =>
            {
                if (_dirty)
                {
                    Rebuild();
                }
            }).Every(250);
            Rebuild();
        }

        private void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _context.Runtime.Journal.Written -= OnWritten;
        }

        private void OnWritten(string id, ChangeSetState state)
        {
            _cache.Remove(id);
            _dirty = true;
        }
    }
}
