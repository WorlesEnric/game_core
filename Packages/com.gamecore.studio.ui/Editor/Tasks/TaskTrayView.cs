// GameCore.Studio.UI - the task tray (SR-4.4, SR-8.3, B-AGENT-UX): one row per request with its state chip (the
// companion's vocabulary), elapsed time, worker, cost when reported and a cancel button; selecting a row shows the
// details (intent, selection, diagnostics, etos task ids, links) and, for needs_clarification, an inline answer that
// re-submits with parent = the original change set. Rows come from the TaskLedger (persisted across domain reloads) and
// are re-fetched from the gateway when the tray attaches.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.UIElements;

namespace GameCore.Studio.UI
{
    /// <summary>The task tray.</summary>
    public sealed class TaskTrayView : VisualElement
    {
        private readonly StudioUiContext _context;
        private readonly ScrollView _list;
        private readonly VisualElement _details;
        private readonly Label _header;
        private string? _selected;
        private bool _attached;
        private double _lastTick;

        public TaskTrayView(StudioUiContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            name = "task-tray";
            AddToClassList("gcs-tray");

            VisualElement toolbar = new VisualElement();
            toolbar.AddToClassList("gcs-row");
            toolbar.AddToClassList("gcs-toolbar");
            _header = new Label("Tasks");
            _header.AddToClassList("gcs-section__title");
            toolbar.Add(_header);
            toolbar.Add(new Button(() => _ = RefreshFromGateway()) { name = "tasks-refresh", text = "Refresh", tooltip = "Re-fetch requests from the Studio companion" });
            toolbar.Add(new Button(() => _context.Tasks.ClearClosed()) { name = "tasks-clear", text = "Clear closed" });
            Add(toolbar);

            VisualElement body = new VisualElement();
            body.AddToClassList("gcs-split");
            Add(body);
            _list = new ScrollView { name = "task-rows" };
            _list.AddToClassList("gcs-split__list");
            body.Add(_list);
            _details = new ScrollView { name = "task-details" };
            _details.AddToClassList("gcs-split__details");
            body.Add(_details);

            RegisterCallback<AttachToPanelEvent>(_ => Attach());
            RegisterCallback<DetachFromPanelEvent>(_ => Detach());
            Rebuild();
        }

        /// <summary>The selected row's change-set id.</summary>
        public string? SelectedId => _selected;

        /// <summary>Rows rendered by the last rebuild.</summary>
        public int RenderedRows { get; private set; }

        /// <summary>Re-fetches rows from the gateway (domain reload recovery); the number of requests reported, -1 on failure.</summary>
        public async Task<int> RefreshFromGateway()
        {
            int reported;
            try
            {
                reported = await _context.Tasks.Refresh(_context.Gateway);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Debug.LogWarning("GameCore Studio: task refresh failed: " + error.Message);
                reported = -1;
            }

            Rebuild();
            return reported;
        }

        public void Select(string changeSetId)
        {
            _selected = changeSetId;
            Rebuild();
        }

        /// <summary>Cancels a request through the gateway; the row follows the companion's answer.</summary>
        public async Task Cancel(TaskRow row)
        {
            if (row.requestId.Length == 0)
            {
                return;
            }

            AgentRequestInfo? info = await _context.Gateway.Cancel(row.requestId);
            if (info != null)
            {
                _context.Tasks.Apply(info);
            }

            Rebuild();
        }

        /// <summary>Answers a clarification: a new request with parent = the row's change set.</summary>
        public async Task<RequestHandle?> Answer(TaskRow row, string answer)
        {
            if (string.IsNullOrWhiteSpace(answer))
            {
                return null;
            }

            SelectionSnapshot selection = row.selectionJson.Length > 0
                ? StudioJson.Deserialize<SelectionSnapshot>(row.selectionJson)
                : _context.Selection.Capture(row.mode == "Play" ? SelectionMode.Play : SelectionMode.Edit);
            string text = row.intent + "\nAnswer to \"" + row.question + "\": " + answer.Trim();
            AgentRequest request = _context.Requests.Build(text, selection, row.mode == "Play" ? AgentRequestMode.Play : AgentRequestMode.Edit, IntentOrigin.Agent, null, null, row.changeSetId);
            RequestHandle handle = await _context.Submit(request);
            _selected = handle.ChangeSetId;
            Rebuild();
            return handle;
        }

        /// <summary>Re-renders the rows and the details.</summary>
        public void Rebuild()
        {
            _list.Clear();
            DateTime now = DateTime.UtcNow;
            int open = 0;
            foreach (TaskRow row in _context.Tasks.Rows)
            {
                if (AgentRequestStates.IsOpen(row.State))
                {
                    open++;
                }

                _list.Add(BuildRow(row, now));
            }

            RenderedRows = _context.Tasks.Rows.Count;
            _header.text = "Tasks (" + open.ToString(CultureInfo.InvariantCulture) + " open)";
            if (_context.Tasks.Rows.Count == 0)
            {
                _list.Add(StudioStyles.Text("No requests yet. Type an intent in the viewport's prompt bar and press Ctrl+Enter.", "gcs-muted"));
            }

            BuildDetails();
        }

        private VisualElement BuildRow(TaskRow row, DateTime now)
        {
            VisualElement element = new VisualElement { name = "task-" + row.changeSetId, focusable = true };
            element.AddToClassList("gcs-tray__row");
            element.EnableInClassList("gcs-tray__row--selected", row.changeSetId == _selected);
            element.Add(StudioStyles.StateChip(row.StateLabel, row.State));
            Label intent = new Label(row.intent) { tooltip = row.intent };
            intent.AddToClassList("gcs-tray__intent");
            element.Add(intent);
            element.Add(new Label(row.ElapsedText(now)) { name = "elapsed", tooltip = "elapsed" });
            if (row.worker.Length > 0)
            {
                element.Add(new Label(row.worker) { tooltip = "worker" });
            }

            if (row.hasCost)
            {
                element.Add(new Label("$" + row.costUsd.ToString("0.000", CultureInfo.InvariantCulture)) { tooltip = "cost reported by etos" });
            }

            Button cancel = new Button(() => _ = Cancel(row)) { name = "cancel", text = "Cancel", tooltip = "Cancel through etos (a no-op once the task ended)" };
            cancel.SetEnabled(AgentRequestStates.IsOpen(row.State) && row.requestId.Length > 0);
            element.Add(cancel);
            string id = row.changeSetId;
            element.RegisterCallback<PointerDownEvent>(_ => Select(id));
            element.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.Space)
                {
                    Select(id);
                }
            });
            return element;
        }

        private void BuildDetails()
        {
            _details.Clear();
            TaskRow? row = _selected == null ? null : _context.Tasks.Find(_selected);
            if (row == null)
            {
                _details.Add(StudioStyles.Text("Select a request to see its details.", "gcs-muted"));
                return;
            }

            _details.Add(StudioStyles.Header("Request"));
            _details.Add(StudioStyles.Text(row.intent));
            _details.Add(StudioStyles.Text("State: " + row.StateLabel + (row.etosStatus.Length > 0 ? " (etos " + row.etosStatus + ")" : string.Empty)));
            _details.Add(StudioStyles.Text("Change set: " + row.changeSetId));
            if (row.requestId.Length > 0)
            {
                _details.Add(StudioStyles.Text("Request id: " + row.requestId));
            }

            if (row.parent.Length > 0)
            {
                _details.Add(StudioStyles.Text("Parent: " + row.parent));
            }

            _details.Add(StudioStyles.Text("Selection: " + (row.selectionSummary.Length > 0 ? row.selectionSummary : "-")));
            _details.Add(StudioStyles.Text("Mode: " + row.mode + "  Worker: " + (row.worker.Length > 0 ? row.worker : "default")));
            _details.Add(StudioStyles.Text("Created: " + row.CreatedUtc.ToString("u", CultureInfo.InvariantCulture) + "  Updated: " + row.UpdatedUtc.ToString("u", CultureInfo.InvariantCulture)));
            _details.Add(StudioStyles.Text("etos tasks: " + (row.taskIds.Count == 0 ? "-" : string.Join(", ", row.taskIds))));
            if (row.toolCatalogRevision.Length > 0)
            {
                _details.Add(StudioStyles.Text("Tool catalog: " + row.toolCatalogRevision.Substring(0, Math.Min(16, row.toolCatalogRevision.Length)) + "..."));
            }

            IReadOnlyList<Diagnostic> diagnostics = row.Diagnostics();
            if (diagnostics.Count > 0)
            {
                _details.Add(StudioStyles.Header("Diagnostics"));
                foreach (Diagnostic diagnostic in diagnostics)
                {
                    _details.Add(StudioStyles.Text(diagnostic.Code + ": " + diagnostic.Message + (diagnostic.Hint != null ? " (" + diagnostic.Hint + ")" : string.Empty), "gcs-diagnostic"));
                }
            }

            if (row.State == AgentRequestState.NeedsClarification)
            {
                _details.Add(StudioStyles.Header("Clarification"));
                _details.Add(StudioStyles.Text(row.question.Length > 0 ? row.question : "The worker needs a clarification."));
                TextField answer = new TextField { name = "clarification-answer", multiline = true };
                _details.Add(answer);
                _details.Add(new Button(() => _ = Answer(row, answer.value)) { name = "clarification-send", text = "Answer (re-submit with parent)" });
            }

            if (row.State == AgentRequestState.Candidate)
            {
                CandidateEntry? candidate = _context.Candidates.Find(row.changeSetId);
                _details.Add(StudioStyles.Text(candidate != null ? "Candidate: " + candidate.Summary + " (" + candidate.Stage + "); review it in GameCore/Studio/Candidates." : "Candidate ready; fetching...", "gcs-muted"));
            }
        }

        private void Attach()
        {
            if (_attached)
            {
                return;
            }

            _attached = true;
            _context.Tasks.Changed += Rebuild;
            _context.Candidates.Changed += Rebuild;
            schedule.Execute(Tick).Every(1000);
            _ = RefreshFromGateway();
        }

        private void Detach()
        {
            if (!_attached)
            {
                return;
            }

            _attached = false;
            _context.Tasks.Changed -= Rebuild;
            _context.Candidates.Changed -= Rebuild;
        }

        private void Tick()
        {
            if (!_attached)
            {
                return;
            }

            double now = UnityEditor.EditorApplication.timeSinceStartup;
            if (now - _lastTick < 0.9)
            {
                return;
            }

            _lastTick = now;
            DateTime utc = DateTime.UtcNow;
            int index = 0;
            foreach (VisualElement child in _list.Children())
            {
                if (index >= _context.Tasks.Rows.Count)
                {
                    break;
                }

                Label? elapsed = child.Q<Label>("elapsed");
                if (elapsed != null)
                {
                    elapsed.text = _context.Tasks.Rows[index].ElapsedText(utc);
                }

                index++;
            }
        }
    }
}
