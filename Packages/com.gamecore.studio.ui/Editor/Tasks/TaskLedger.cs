// GameCore.Studio.UI - task tray state (docs/studio/04-etos-integration.md s2/s3, SR-4.4, SR-8.3). One row per request:
// state (the companion's vocabulary, see AgentRequestState), elapsed, worker, etos task ids and status, the gateway's
// import state and progress, diagnostics and the clarification question. Rows live in a ScriptableSingleton persisted
// under Library/, so they survive domain reloads and editor restarts; the tray merges the gateway's RequestViews on
// every RequestChanged and on refresh (P2.2's gateway recovers them from the companion's ledger, the authority; the
// local rows only bridge the reload).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>One serialized tray row.</summary>
    [Serializable]
    public sealed class TaskRow
    {
        public string changeSetId = string.Empty;
        public string requestId = string.Empty;
        public string state = "queued";
        public string intent = string.Empty;
        public string worker = string.Empty;
        public string waitingReason = string.Empty;
        public string localState = string.Empty;
        public string progress = string.Empty;
        public long createdTicks;
        public long updatedTicks;
        public List<string> taskIds = new List<string>();
        public List<string> diagnostics = new List<string>();
        public string question = string.Empty;
        public string parent = string.Empty;
        public string toolCatalogRevision = string.Empty;
        public string selectionSummary = string.Empty;
        public string selectionJson = string.Empty;
        public string mode = "Edit";
        public string etosStatus = string.Empty;
        public long sequence;

        /// <summary>The parsed state (Refused when the stored text is unknown, which never happens for rows we wrote).</summary>
        public AgentRequestState State => AgentRequestStates.Parse(state) ?? AgentRequestState.Refused;

        public bool Submitting => string.IsNullOrEmpty(requestId) && State == AgentRequestState.Queued;

        public DateTime CreatedUtc => new DateTime(createdTicks, DateTimeKind.Utc);

        public DateTime UpdatedUtc => new DateTime(updatedTicks, DateTimeKind.Utc);

        /// <summary>The diagnostics as model objects (stored as JSON).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics()
        {
            List<Diagnostic> result = new List<Diagnostic>();
            foreach (string text in diagnostics)
            {
                try
                {
                    result.Add(StudioJson.Deserialize<Diagnostic>(text));
                }
                catch (Exception error) when (error is JsonException || error is ArgumentException)
                {
                    result.Add(new Diagnostic(DiagnosticCodes.Refused, text));
                }
            }

            return result;
        }

        public string StateLabel
        {
            get
            {
                if (Submitting)
                {
                    return "submitting";
                }

                string label = AgentRequestStates.Wire(State);
                return State == AgentRequestState.Waiting && waitingReason.Length > 0 ? label + "(" + waitingReason + ")" : label;
            }
        }

        public string ElapsedText(DateTime nowUtc)
        {
            DateTime end = AgentRequestStates.IsOpen(State) ? nowUtc : UpdatedUtc;
            TimeSpan elapsed = end - CreatedUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return ((int)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Where tray rows are kept.</summary>
    public interface ITaskRowStore
    {
        List<TaskRow> Rows { get; }

        /// <summary>The highest companion ledger sequence seen (cursor for re-fetch).</summary>
        long Cursor { get; set; }

        void Save();
    }

    /// <summary>The project's tray rows (Library/GameCoreStudio/ui-tasks.asset).</summary>
    [FilePath("Library/GameCoreStudio/ui-tasks.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class StudioTaskStore : ScriptableSingleton<StudioTaskStore>, ITaskRowStore
    {
        [SerializeField]
        private List<TaskRow> rows = new List<TaskRow>();

        [SerializeField]
        private long cursor;

        public List<TaskRow> Rows => rows;

        public long Cursor
        {
            get => cursor;
            set => cursor = value;
        }

        public void Save() => Save(true);
    }

    /// <summary>An in-memory store (tests; serialized with EditorJsonUtility to simulate a domain reload).</summary>
    [Serializable]
    public sealed class MemoryTaskRowStore : ITaskRowStore
    {
        [SerializeField]
        private List<TaskRow> rows = new List<TaskRow>();

        [SerializeField]
        private long cursor;

        public List<TaskRow> Rows => rows;

        public long Cursor
        {
            get => cursor;
            set => cursor = value;
        }

        public int Saves { get; private set; }

        public void Save() => Saves++;
    }

    /// <summary>The tray's rows and their updates from submissions, gateway events and re-fetches.</summary>
    public sealed class TaskLedger
    {
        /// <summary>Rows kept at most (oldest closed rows are dropped first).</summary>
        public const int MaxRows = 200;

        private readonly ITaskRowStore _store;

        public TaskLedger(ITaskRowStore store)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public IReadOnlyList<TaskRow> Rows => _store.Rows;

        public ITaskRowStore Store => _store;

        /// <summary>Re-fetches completed (count of rows the gateway reported).</summary>
        public int Refreshes { get; private set; }

        public event Action? Changed;

        public TaskRow? Find(string id)
        {
            foreach (TaskRow row in _store.Rows)
            {
                if (row.changeSetId == id || (row.requestId.Length > 0 && row.requestId == id))
                {
                    return row;
                }
            }

            return null;
        }

        /// <summary>Records a request being submitted (state queued, no request id yet).</summary>
        public TaskRow AddSubmitted(PreparedRequest request, string selectionSummary)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            long now = DateTime.UtcNow.Ticks;
            TaskRow row = Find(request.ChangeSetId) ?? new TaskRow { changeSetId = request.ChangeSetId, createdTicks = now };
            row.state = AgentRequestStates.Wire(AgentRequestState.Queued);
            row.intent = request.Intent.Text;
            row.updatedTicks = now;
            row.parent = request.Parent ?? string.Empty;
            row.toolCatalogRevision = request.ToolCatalogRevision;
            row.selectionSummary = selectionSummary;
            row.selectionJson = StudioJson.Serialize(request.Selection, false);
            row.mode = request.Selection.Mode.ToString();
            if (!_store.Rows.Contains(row))
            {
                _store.Rows.Insert(0, row);
                Trim();
            }

            Save();
            return row;
        }

        /// <summary>The gateway accepted a submission: the row gets its request id (state unchanged until the gateway reports it).</summary>
        public TaskRow? ApplySubmitted(string changeSetId, string requestId)
        {
            TaskRow? row = Find(changeSetId);
            if (row == null)
            {
                return null;
            }

            row.requestId = requestId;
            row.updatedTicks = DateTime.UtcNow.Ticks;
            Save();
            return row;
        }

        /// <summary>The gateway refused a submission: state Refused with the diagnostic (code preserved).</summary>
        public TaskRow? ApplyRefusal(string changeSetId, Diagnostic refusal)
        {
            TaskRow? row = Find(changeSetId);
            if (row == null)
            {
                return null;
            }

            row.state = AgentRequestStates.Wire(AgentRequestState.Refused);
            row.diagnostics.Add(StudioJson.Serialize(refusal, false));
            row.updatedTicks = DateTime.UtcNow.Ticks;
            Save();
            return row;
        }

        /// <summary>Upserts a row from the companion's view of a request (events and re-fetch).</summary>
        public TaskRow Apply(AgentRequestInfo info)
        {
            if (info == null)
            {
                throw new ArgumentNullException(nameof(info));
            }

            TaskRow? row = Find(info.ChangeSetId) ?? Find(info.RequestId);
            if (row == null)
            {
                row = new TaskRow { changeSetId = info.ChangeSetId, createdTicks = info.CreatedUtc.Ticks };
                _store.Rows.Insert(0, row);
            }

            row.requestId = info.RequestId;
            row.state = AgentRequestStates.Wire(info.State);
            if (info.IntentText.Length > 0)
            {
                row.intent = info.IntentText;
            }

            row.worker = info.Worker ?? row.worker;
            row.waitingReason = info.WaitingReason ?? string.Empty;
            row.localState = info.LocalState ?? row.localState;
            row.progress = info.Progress ?? row.progress;
            row.updatedTicks = info.UpdatedUtc.Ticks;
            if (row.createdTicks == 0)
            {
                row.createdTicks = info.CreatedUtc.Ticks;
            }

            foreach (string taskId in info.TaskIds)
            {
                if (!row.taskIds.Contains(taskId))
                {
                    row.taskIds.Add(taskId);
                }
            }

            row.diagnostics.Clear();
            foreach (Diagnostic diagnostic in info.Diagnostics)
            {
                row.diagnostics.Add(StudioJson.Serialize(diagnostic, false));
            }

            row.question = info.Question ?? string.Empty;
            row.etosStatus = info.EtosStatus ?? row.etosStatus;
            row.sequence = Math.Max(row.sequence, info.Sequence);
            if (info.Sequence > _store.Cursor)
            {
                _store.Cursor = info.Sequence;
            }

            Trim();
            Save();
            return row;
        }

        /// <summary>
        /// Merges every request the gateway knows (P2.2 recovers them from the companion after a domain reload). Rows
        /// the gateway does not know keep their last local state. Returns the number of requests merged.
        /// </summary>
        public int Refresh(IAgentGateway gateway)
        {
            if (gateway == null)
            {
                throw new ArgumentNullException(nameof(gateway));
            }

            IReadOnlyList<RequestView> requests = gateway.Requests;
            foreach (RequestView view in requests)
            {
                Apply(AgentRequestInfo.From(view));
            }

            Refreshes++;
            Changed?.Invoke();
            return requests.Count;
        }

        /// <summary>Removes closed rows (keeps open ones).</summary>
        public void ClearClosed()
        {
            _store.Rows.RemoveAll(row => !AgentRequestStates.IsOpen(row.State) && !row.Submitting);
            Save();
        }

        private void Trim()
        {
            while (_store.Rows.Count > MaxRows)
            {
                int victim = _store.Rows.FindLastIndex(row => !AgentRequestStates.IsOpen(row.State));
                _store.Rows.RemoveAt(victim >= 0 ? victim : _store.Rows.Count - 1);
            }
        }

        private void Save()
        {
            _store.Save();
            Changed?.Invoke();
        }
    }
}
