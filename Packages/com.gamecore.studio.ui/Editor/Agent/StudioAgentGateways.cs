// GameCore.Studio.UI - resolves the agent gateway the UI talks to (P2.2's GameCore.Studio.Authoring.Agent.IAgentGateway,
// found with AgentGatewayLookup.From(StudioServiceRegistry.AgentGateway)), else the not-configured gateway, which
// refuses everything with not_configured and never produces a candidate.
//
// GatewayExtras reads the members P2.2's EtosAgentGateway has beyond the interface, by name, because this package does
// not reference com.gamecore.studio.etos: Staged (the candidates the gateway imported and staged itself),
// Options.AutoImport + IsOwn (whether it will), Reject(id, reason) and Client.StageAsync/GetStageAsync (the staging
// lane, POST /v1/stage). A gateway without them is driven through the interface only.
#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.UI
{
    /// <summary>Gateway resolution.</summary>
    public static class StudioAgentGateways
    {
        // Optional package seam: UI must not add an ETOS assembly/package dependency.
        private static Type? SessionType => Type.GetType("GameCore.Studio.Etos.EtosStudioSession, GameCore.Studio.Etos", false);

        public static void EnsureSessionStarted() => StartSession(StudioServices.Runtime, SessionType);

        private static bool StartSession(StudioRuntime runtime, Type? session)
        {
            if (AgentGatewayLookup.From(runtime.Services.AgentGateway) != null) return true;
            MethodInfo? start = session?.GetMethod("EnsureStarted", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null) ?? session?.GetMethod("Start", BindingFlags.Public | BindingFlags.Static,
                null, Type.EmptyTypes, null);
            return start?.Invoke(null, null) is bool started && started;
        }

        public static Diagnostic? SessionProblem => SessionType?.GetProperty("Problem", BindingFlags.Public | BindingFlags.Static)
            ?.GetValue(null) is Diagnostic problem ? GatewayErrors.Redact(problem) : null;

        public static IAgentGateway Resolve(StudioRuntime runtime)
        {
            return AgentGatewayLookup.From(runtime.Services.AgentGateway) ?? NullAgentGateway.Instance;
        }
    }

    /// <summary>The gateway used when none is registered: status not_configured, every call refused with that code.</summary>
    public sealed class NullAgentGateway : IAgentGateway
    {
        public const string Detail = "No Studio agent gateway is registered. Install com.gamecore.studio.etos and pair the editor with the Studio companion (Project Settings > GameCore Studio > ETOS).";

        private NullAgentGateway()
        {
        }

        public static NullAgentGateway Instance { get; } = new NullAgentGateway();

        public ProviderStatus Status => new ProviderStatus(ProviderState.NotConfigured, ProviderState.NotConfigured,
            ProviderState.NotConfigured, ProviderState.NotConfigured, ProviderState.NotConfigured, false, false,
            null, Refusal());

        public IReadOnlyList<RequestView> Requests => Array.Empty<RequestView>();

        public event Action<ProviderStatus>? StatusChanged
        {
            add { }
            remove { }
        }

        public event Action<RequestView>? RequestChanged
        {
            add { }
            remove { }
        }

        public event Action<CandidateNotice>? CandidateReady
        {
            add { }
            remove { }
        }

        public static Diagnostic Refusal() => StudioAgentGateways.SessionProblem ?? new Diagnostic("not_configured", Detail);

        public Task<string> SubmitAsync(AgentRequest req, CancellationToken ct) => Task.FromException<string>(new StudioGatewayException(Refusal()));

        public Task CancelAsync(string requestId, CancellationToken ct) => Task.FromException(new StudioGatewayException(Refusal()));

        public Task<ChangeSet> FetchCandidateAsync(string requestId, CancellationToken ct) => Task.FromException<ChangeSet>(new StudioGatewayException(Refusal()));

        public Task<byte[]> FetchArtifactAsync(string sha256, CancellationToken ct) => Task.FromException<byte[]>(new StudioGatewayException(Refusal()));

        public Task<OpResult> GenerateAsync(OpRequest req, CancellationToken ct) => Task.FromResult(OpResult.Refused(Refusal()));

        public IVoiceSession CreateVoiceSession() => new RefusedVoiceSession();

        /// <summary>A voice session that reports not_configured on start.</summary>
        private sealed class RefusedVoiceSession : IVoiceSession
        {
            public event Action<TranscriptUpdate>? Transcript
            {
                add { }
                remove { }
            }

            public event Action<float>? Level
            {
                add { }
                remove { }
            }

            public event Action<Diagnostic>? Error;

            public Task StartAsync()
            {
                Error?.Invoke(Refusal());
                return Task.CompletedTask;
            }

            public Task StopAsync() => Task.CompletedTask;
        }
    }

    /// <summary>The etos gateway's members beyond the interface, read by name (see the file header).</summary>
    public static class GatewayExtras
    {
        private const BindingFlags Public = BindingFlags.Public | BindingFlags.Instance;

        /// <summary>Optional worker discovery seam, populated from the companion hello.</summary>
        public static IReadOnlyList<string> Workers(IAgentGateway gateway)
        {
            return gateway.GetType().GetProperty("Workers", Public)?.GetValue(gateway) as IReadOnlyList<string> ?? Array.Empty<string>();
        }

        /// <summary>The change set the gateway staged itself for <paramref name="requestId"/> (unconsumed), or null.</summary>
        public static StagedChangeSet? StagedBy(IAgentGateway gateway, string requestId)
        {
            if (gateway.GetType().GetProperty("Staged", Public)?.GetValue(gateway) is IReadOnlyDictionary<string, StagedChangeSet> staged
                && staged.TryGetValue(requestId, out StagedChangeSet? found)
                && !found.Consumed)
            {
                return found;
            }

            return null;
        }

        /// <summary>
        /// Whether this project submitted the request, as the gateway knows it (<c>IsOwn</c>); null when the gateway
        /// cannot tell. The companion's ledger is shared by every client of the app, so the etos gateway lists other
        /// clients' requests too; the UI shows and reviews only its own.
        /// </summary>
        public static bool? IsOwn(IAgentGateway gateway, string requestId)
        {
            MethodInfo? isOwn = gateway.GetType().GetMethod("IsOwn", Public, null, new[] { typeof(string) }, null);
            return isOwn == null ? (bool?)null : isOwn.Invoke(gateway, new object[] { requestId }) is bool own && own;
        }

        /// <summary>True when the gateway imports and stages this request's candidate by itself (etos AutoImport of an own request).</summary>
        public static bool ImportsItself(IAgentGateway gateway, string requestId)
        {
            Type type = gateway.GetType();
            if (type.GetProperty("Staged", Public) == null)
            {
                return false;
            }

            object? options = type.GetProperty("Options", Public)?.GetValue(gateway);
            bool autoImport = options?.GetType().GetProperty("AutoImport", Public)?.GetValue(options) is bool flag && flag;
            MethodInfo? isOwn = type.GetMethod("IsOwn", Public, null, new[] { typeof(string) }, null);
            return autoImport && isOwn != null && isOwn.Invoke(gateway, new object[] { requestId }) is bool own && own;
        }

        /// <summary>Calls the gateway's <c>Reject(requestId, reason)</c> when it has one; false otherwise.</summary>
        public static bool TryReject(IAgentGateway gateway, string requestId, string reason)
        {
            MethodInfo? reject = gateway.GetType().GetMethod("Reject", Public, null, new[] { typeof(string), typeof(string) }, null);
            if (reject == null)
            {
                return false;
            }

            reject.Invoke(gateway, new object[] { requestId, reason });
            return true;
        }

    }
}
