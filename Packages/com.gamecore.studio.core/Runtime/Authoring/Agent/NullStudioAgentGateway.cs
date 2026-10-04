// GameCore.Studio.Authoring.Agent - the gateway used when no etos client is registered (P2.1). It answers every call
// with NotConfigured and never produces a candidate, transcript or artifact (06 s4: no mock providers in production
// paths). Stateless, so one shared instance is safe.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameCore.Studio.Model;

namespace GameCore.Studio.Authoring.Agent
{
    /// <summary>The not-configured gateway.</summary>
    public sealed class NullStudioAgentGateway : IStudioAgentGateway
    {
        public const string Hint = "Install com.gamecore.studio.etos and pair the editor with the Studio companion (GameCore/Studio/Settings).";

        /// <summary>The shared instance (immutable).</summary>
        public static readonly NullStudioAgentGateway Instance = new NullStudioAgentGateway();

        private static readonly ProviderStatus NotConfiguredStatus = ProviderStatus.NotConfigured("No Studio agent gateway is registered. " + Hint);

        private NullStudioAgentGateway()
        {
        }

        public bool IsConfigured => false;

        public ProviderStatus Status => NotConfiguredStatus;

        public IObservable<AgentEvent> Events => EmptyEvents.Instance;

        public IVoiceSession? Voice => null;

        public static Diagnostic NotConfigured(string what) =>
            new Diagnostic(DiagnosticCodes.NotConfigured, "No Studio agent gateway is configured; " + what + " is unavailable.", Hint);

        public ServiceResult GenerateAsset(AgentAssetRequest request) => ServiceResult.NotConfigured("agent gateway", Hint);

        public ServiceResult ProposeMechanism(AgentMechanismRequest request) => ServiceResult.NotConfigured("agent gateway", Hint);

        public Task<RequestHandle> Submit(AgentRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return Task.FromResult(RequestHandle.Refused(request.ChangeSetId, NotConfigured("submitting a request")));
        }

        public Task<AgentRequestInfo?> Cancel(string requestId) => Task.FromResult<AgentRequestInfo?>(null);

        public Task<IReadOnlyList<AgentRequestInfo>> ListRequests(long after = 0) => Task.FromResult<IReadOnlyList<AgentRequestInfo>>(Array.Empty<AgentRequestInfo>());

        public Task<AgentCandidate> FetchCandidate(string requestId) => Task.FromException<AgentCandidate>(new AgentGatewayException(NotConfigured("fetching a candidate")));

        public Task<byte[]> FetchArtifact(string sha256) => Task.FromException<byte[]>(new AgentGatewayException(NotConfigured("fetching an artifact")));

        public Task RejectCandidate(string changeSetId, string reason) => Task.CompletedTask;

        /// <summary>An event stream that never emits.</summary>
        private sealed class EmptyEvents : IObservable<AgentEvent>, IDisposable
        {
            public static readonly EmptyEvents Instance = new EmptyEvents();

            public IDisposable Subscribe(IObserver<AgentEvent> observer) => this;

            public void Dispose()
            {
            }
        }
    }
}
