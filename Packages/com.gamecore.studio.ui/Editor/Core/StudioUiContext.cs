// GameCore.Studio.UI - one Studio UI context: the edit runtime, the selection, the task ledger, the candidate
// coordinator and the gateway event pump. The project's context lives in StudioUiSession (a ScriptableSingleton, rebuilt
// after every domain reload); tests build their own over a test runtime, a test gateway and an in-memory task store.
//
// Gateway events may arrive on any thread; the context marshals them onto the main thread (MainThreadQueue) and then:
// RequestUpdated -> task ledger; CandidateReady -> candidate coordinator (fetch + retain artifacts); VoiceTranscript ->
// VoiceTranscriptReceived; ProviderStatusChanged -> StatusChanged. The gateway is resolved on every tick, so a gateway
// registered after the UI opened (P2.2 on load) is picked up and subscribed.
#nullable enable
using System;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using UnityEditor;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>The services every Studio panel uses.</summary>
    public sealed class StudioUiContext : IDisposable
    {
        private readonly Func<IStudioAgentGateway> _gatewayProvider;
        private readonly bool _hooked;
        private IStudioAgentGateway? _subscribedGateway;
        private IDisposable? _subscription;

        public StudioUiContext(StudioRuntime runtime, Func<IStudioAgentGateway>? gateway, SelectionModel selection, TaskLedger tasks, bool hookEditorUpdate)
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
        public IStudioAgentGateway Gateway => _gatewayProvider();

        /// <summary>Events seen (all kinds), for status displays and tests.</summary>
        public int EventsSeen { get; private set; }

        /// <summary>A panel asked the prompt bar to show text (Agent-tier tools); the viewport handles it.</summary>
        public event Action<string>? PromptPrefillRequested;

        public event Action<VoiceTranscript>? VoiceTranscriptReceived;

        public event Action<ProviderStatus>? StatusChanged;

        /// <summary>Raised when a candidate arrived (after its artifacts were retained) or failed to arrive.</summary>
        public event Action<CandidateEntry>? CandidateArrived;

        public void RequestPrompt(string text) => PromptPrefillRequested?.Invoke(text);

        /// <summary>Drains marshalled gateway callbacks and keeps the event subscription on the current gateway.</summary>
        public void Tick()
        {
            EnsureSubscribed();
            Dispatcher.Drain();
        }

        /// <summary>Submits a built request: records the row, awaits the gateway, records the answer.</summary>
        public async Task<RequestHandle> Submit(AgentRequest request)
        {
            Tasks.AddSubmitted(request, AgentRequestBuilder.Summarize(request.Selection));
            RequestHandle handle;
            try
            {
                handle = await Gateway.Submit(request);
            }
            catch (AgentGatewayException error)
            {
                handle = RequestHandle.Refused(request.ChangeSetId, error.Diagnostic);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                handle = RequestHandle.Refused(request.ChangeSetId, new Diagnostic(DiagnosticCodes.Refused, "transport: " + error.Message));
            }

            Tasks.ApplyHandle(handle);
            return handle;
        }

        /// <summary>Handles one gateway event on the main thread (the observer posts here; tests call it directly).</summary>
        public void Handle(AgentEvent agentEvent)
        {
            EventsSeen++;
            switch (agentEvent.Kind)
            {
                case AgentEventKind.RequestUpdated:
                    if (agentEvent.Request != null)
                    {
                        Tasks.Apply(agentEvent.Request);
                        if (agentEvent.Request.State == AgentRequestState.Candidate)
                        {
                            _ = ReceiveCandidate(agentEvent.Request.RequestId, agentEvent.Request.ChangeSetId);
                        }
                    }

                    break;
                case AgentEventKind.CandidateReady:
                    if (agentEvent.RequestId != null)
                    {
                        _ = ReceiveCandidate(agentEvent.RequestId, agentEvent.ChangeSetId);
                    }

                    break;
                case AgentEventKind.VoiceTranscript:
                    if (agentEvent.Transcript != null)
                    {
                        VoiceTranscriptReceived?.Invoke(agentEvent.Transcript);
                    }

                    break;
                case AgentEventKind.ProviderStatusChanged:
                    if (agentEvent.Status != null)
                    {
                        StatusChanged?.Invoke(agentEvent.Status);
                    }

                    break;
            }
        }

        /// <summary>Fetches a candidate (once) and announces it.</summary>
        public async Task<CandidateEntry> ReceiveCandidate(string requestId, string? changeSetId)
        {
            CandidateEntry entry = await Candidates.Receive(requestId, changeSetId);
            CandidateArrived?.Invoke(entry);
            return entry;
        }

        /// <summary>
        /// Recovery after a domain reload: re-fetches the tray rows and the candidates of rows in state candidate that
        /// are not under review yet.
        /// </summary>
        public async Task RecoverAsync()
        {
            try
            {
                await Tasks.Refresh(Gateway);
            }
            catch (Exception error) when (!(error is OutOfMemoryException))
            {
                Debug.LogWarning("GameCore Studio: the task tray could not be refreshed from the gateway: " + error.Message);
                return;
            }

            foreach (TaskRow row in Tasks.Rows)
            {
                if (row.State == AgentRequestState.Candidate && row.requestId.Length > 0 && Candidates.Find(row.changeSetId) == null)
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

            _subscription?.Dispose();
            _subscription = null;
            _subscribedGateway = null;
            Dispatcher.Dispose();
            Selection.Dispose();
        }

        private void EnsureSubscribed()
        {
            IStudioAgentGateway gateway = Gateway;
            if (ReferenceEquals(gateway, _subscribedGateway))
            {
                return;
            }

            _subscription?.Dispose();
            _subscribedGateway = gateway;
            _subscription = gateway.Events.Subscribe(new Observer(this));
        }

        private sealed class Observer : IObserver<AgentEvent>
        {
            private readonly StudioUiContext _owner;

            public Observer(StudioUiContext owner)
            {
                _owner = owner;
            }

            public void OnCompleted()
            {
            }

            public void OnError(Exception error)
            {
                _owner.Dispatcher.Post(() => Debug.LogWarning("GameCore Studio: the agent event stream failed: " + error.Message));
            }

            public void OnNext(AgentEvent value)
            {
                _owner.Dispatcher.Post(() => _owner.Handle(value));
            }
        }
    }
}
