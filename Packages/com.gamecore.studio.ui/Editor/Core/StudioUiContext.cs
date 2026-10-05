// GameCore.Studio.UI - one Studio UI context: the edit runtime, the selection, the task ledger, the candidate
// coordinator and the gateway event pump. The project's context lives in StudioUiSession (a ScriptableSingleton, rebuilt
// after every domain reload); tests build their own over a test runtime, a test gateway and an in-memory task store.
//
// The gateway is P2.2's GameCore.Studio.Authoring.Agent.IAgentGateway. Its events are posted onto the context's
// MainThreadQueue and handled on the next tick: RequestChanged -> task ledger, and the candidate coordinator when the
// gateway reports an imported candidate (LocalState staged / stage_refused / import_failed: the etos gateway imports and
// stages its own requests' candidates, the UI adopts that staged change set instead of staging it twice);
// CandidateReady -> the coordinator fetches the candidate itself when the gateway does not import it;
// StatusChanged -> StatusChanged. The gateway is resolved on every tick, so a gateway registered after the UI opened
// (P2.2 on load) is picked up and subscribed.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>The outcome of a prompt submission.</summary>
    public sealed class PromptSubmission
    {
        public PromptSubmission(string changeSetId, string? requestId, Diagnostic? refusal)
        {
            ChangeSetId = changeSetId;
            RequestId = requestId;
            Refusal = refusal;
        }

        public string ChangeSetId { get; }

        /// <summary>The gateway's request id (P2.2: the change-set id), null when refused.</summary>
        public string? RequestId { get; }

        /// <summary>Why the gateway refused (code preserved: not_configured, stale_context, transport, ...).</summary>
        public Diagnostic? Refusal { get; }

        public bool Accepted => Refusal == null && RequestId != null;
    }

    /// <summary>The services every Studio panel uses.</summary>
    public sealed class StudioUiContext : IDisposable
    {
        private readonly Func<IAgentGateway> _gatewayProvider;
        private readonly bool _hooked;
        private IAgentGateway? _subscribedGateway;

        public StudioUiContext(StudioRuntime runtime, Func<IAgentGateway>? gateway, SelectionModel selection, TaskLedger tasks, bool hookEditorUpdate)
        {
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _gatewayProvider = gateway ?? (() => StudioAgentGateways.Resolve(runtime));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            Tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
            Dispatcher = new MainThreadQueue(false);
            Requests = new AgentRequestBuilder(runtime);
            Candidates = new CandidateCoordinator(runtime, () => Gateway, tasks);
            _hooked = hookEditorUpdate;
            if (hookEditorUpdate)
            {
                EditorApplication.update += Tick;
            }

            EnsureSubscribed();
        }

        public StudioRuntime Runtime { get; }

        public SelectionModel Selection { get; }

        public TaskLedger Tasks { get; }

        public CandidateCoordinator Candidates { get; }

        public AgentRequestBuilder Requests { get; }

        public MainThreadQueue Dispatcher { get; }

        /// <summary>The gateway in use (resolved on each access).</summary>
        public IAgentGateway Gateway => _gatewayProvider();

        /// <summary>Gateway events handled (all kinds), for status displays and tests.</summary>
        public int EventsSeen { get; private set; }

        /// <summary>A panel asked the prompt bar to show text (Agent-tier tools); the viewport handles it.</summary>
        public event Action<string>? PromptPrefillRequested;

        public event Action<ProviderStatus>? StatusChanged;

        /// <summary>Raised when a candidate arrived (fetched, or adopted from the gateway's import) or failed to arrive.</summary>
        public event Action<CandidateEntry>? CandidateArrived;

        public void RequestPrompt(string text) => PromptPrefillRequested?.Invoke(text);

        /// <summary>Drains marshalled gateway callbacks and keeps the event subscription on the current gateway.</summary>
        public void Tick()
        {
            EnsureSubscribed();
            Dispatcher.Drain();
        }

        /// <summary>Submits a built request: records the row, awaits the gateway, records the answer.</summary>
        public async Task<PromptSubmission> Submit(PreparedRequest request)
        {
            Tasks.AddSubmitted(request, AgentRequestBuilder.Summarize(request.Selection));
            PromptSubmission submission;
            try
            {
                string requestId = await Gateway.SubmitAsync(request.Request, CancellationToken.None);
                submission = new PromptSubmission(request.ChangeSetId, requestId, null);
                Tasks.ApplySubmitted(request.ChangeSetId, requestId);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                submission = new PromptSubmission(request.ChangeSetId, null, GatewayErrors.ToDiagnostic(error));
                Tasks.ApplyRefusal(request.ChangeSetId, submission.Refusal!);
            }

            return submission;
        }

        /// <summary>Handles a request view on the main thread (the subscription posts here; tests call it directly).</summary>
        public void HandleRequest(RequestView view)
        {
            EventsSeen++;
            AgentRequestInfo info = AgentRequestInfo.From(view);
            Tasks.Apply(info);
            IAgentGateway gateway = Gateway;
            switch (view.LocalState)
            {
                case "staged":
                case "stage_refused":
                {
                    StagedChangeSet? staged = GatewayExtras.StagedBy(gateway, view.RequestId);
                    if (staged != null)
                    {
                        CandidateArrived?.Invoke(Candidates.Adopt(view.RequestId, staged));
                    }
                    else if (Candidates.Find(view.ChangeSetId) == null)
                    {
                        Observe(ReceiveCandidate(view.RequestId, view.ChangeSetId));
                    }

                    return;
                }

                case "import_failed":
                    CandidateArrived?.Invoke(Candidates.AddInvalid(view.RequestId, view.ChangeSetId, info.Diagnostics));
                    return;
            }

            if (info.State == AgentRequestState.Candidate && view.HasCandidate && !GatewayExtras.ImportsItself(gateway, view.RequestId) && Candidates.Find(view.ChangeSetId) == null)
            {
                Observe(ReceiveCandidate(view.RequestId, view.ChangeSetId));
            }
        }

        /// <summary>Handles a candidate notice on the main thread: fetched here unless the gateway imports it itself.</summary>
        public void HandleCandidate(CandidateNotice notice)
        {
            EventsSeen++;
            if (!GatewayExtras.ImportsItself(Gateway, notice.RequestId))
            {
                Observe(ReceiveCandidate(notice.RequestId, notice.ChangeSetId));
            }
        }

        /// <summary>Fetches a candidate (once) and announces it.</summary>
        public async Task<CandidateEntry> ReceiveCandidate(string requestId, string? changeSetId, string? toolCatalogRevision = null)
        {
            CandidateEntry entry = await Candidates.Receive(requestId, changeSetId, toolCatalogRevision);
            CandidateArrived?.Invoke(entry);
            return entry;
        }

        /// <summary>
        /// Recovery after a domain reload: merges the gateway's requests into the tray, then brings rows in state
        /// candidate under review (adopting what the gateway staged, fetching the rest).
        /// </summary>
        public async Task RecoverAsync()
        {
            IAgentGateway gateway = Gateway;
            try
            {
                Tasks.Refresh(gateway);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Debug.LogWarning("GameCore Studio: the task tray could not be refreshed from the gateway: " + error.Message);
                return;
            }

            foreach (TaskRow row in new List<TaskRow>(Tasks.Rows))
            {
                if (row.State != AgentRequestState.Candidate || row.requestId.Length == 0 || Candidates.Find(row.changeSetId) != null)
                {
                    continue;
                }

                StagedChangeSet? staged = GatewayExtras.StagedBy(gateway, row.requestId);
                if (staged != null)
                {
                    CandidateArrived?.Invoke(Candidates.Adopt(row.requestId, staged));
                }
                else
                {
                    await ReceiveCandidate(row.requestId, row.changeSetId);
                }
            }
        }

        public void Dispose()
        {
            if (_hooked)
            {
                EditorApplication.update -= Tick;
            }

            Unsubscribe();
            Dispatcher.Dispose();
            Selection.Dispose();
        }

        private void EnsureSubscribed()
        {
            IAgentGateway gateway = Gateway;
            if (ReferenceEquals(gateway, _subscribedGateway))
            {
                return;
            }

            Unsubscribe();
            _subscribedGateway = gateway;
            gateway.RequestChanged += OnRequestChanged;
            gateway.CandidateReady += OnCandidateReady;
            gateway.StatusChanged += OnStatusChanged;
        }

        private void Unsubscribe()
        {
            if (_subscribedGateway != null)
            {
                _subscribedGateway.RequestChanged -= OnRequestChanged;
                _subscribedGateway.CandidateReady -= OnCandidateReady;
                _subscribedGateway.StatusChanged -= OnStatusChanged;
                _subscribedGateway = null;
            }
        }

        /// <summary>A fire-and-forget receive never fails silently: a fault is logged.</summary>
        private static void Observe(Task task)
        {
            task.ContinueWith(done => Debug.LogException(done.Exception!.GetBaseException()), TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private void OnRequestChanged(RequestView view) => Dispatcher.Post(() => HandleRequest(view));

        private void OnCandidateReady(CandidateNotice notice) => Dispatcher.Post(() => HandleCandidate(notice));

        private void OnStatusChanged(ProviderStatus status) => Dispatcher.Post(() =>
        {
            EventsSeen++;
            StatusChanged?.Invoke(status);
        });
    }
}
