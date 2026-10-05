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
        public static IAgentGateway Resolve(StudioRuntime runtime)
        {
            return AgentGatewayLookup.From(runtime.Services.AgentGateway) ?? NullAgentGateway.Instance;
        }
    }

    /// <summary>The gateway used when none is registered: status not_configured, every call refused with that code.</summary>
    public sealed class NullAgentGateway : IAgentGateway
    {
        public const string Detail = "No Studio agent gateway is registered. Install com.gamecore.studio.etos and pair the editor with the Studio companion (Project Settings > GameCore Studio > ETOS).";

        private static readonly ProviderStatus NotConfiguredStatus = new ProviderStatus(
            ProviderState.NotConfigured,
            ProviderState.NotConfigured,
            ProviderState.NotConfigured,
            ProviderState.NotConfigured,
            ProviderState.NotConfigured,
            false,
            false,
            null,
            new Diagnostic("not_configured", Detail));

        private NullAgentGateway()
        {
        }

        public static NullAgentGateway Instance { get; } = new NullAgentGateway();

        public ProviderStatus Status => NotConfiguredStatus;

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

        public static Diagnostic Refusal() => new Diagnostic("not_configured", Detail);

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

        /// <summary>True when the gateway exposes the companion's staging lane (<c>Client.StageAsync</c>).</summary>
        public static bool HasStageLane(IAgentGateway gateway) => StageMethod(gateway, "StageAsync", 3) != null;

        /// <summary>
        /// <c>POST /v1/stage {changeSetId, packageRef}</c> then <c>GET /v1/stage/{job}</c> until done or failed; returns
        /// the final job JSON (<c>{jobId, state, verdict, ...}</c>).
        /// </summary>
        public static async Task<JObject> StageAsync(IAgentGateway gateway, string changeSetId, string packageRef, TimeSpan poll, TimeSpan budget, CancellationToken ct)
        {
            (object client, MethodInfo stage) = StageMethod(gateway, "StageAsync", 3) ?? throw new StudioGatewayException(new Diagnostic("not_configured", "The registered gateway has no staging lane."));
            (_, MethodInfo get) = StageMethod(gateway, "GetStageAsync", 2) ?? throw new StudioGatewayException(new Diagnostic("not_configured", "The registered gateway cannot read stage jobs."));
            JObject job = await RawOf(stage.Invoke(client, new object[] { changeSetId, packageRef, ct })).ConfigureAwait(false);
            DateTime deadline = DateTime.UtcNow + budget;
            string jobId = (string?)job["jobId"] ?? string.Empty;
            while (jobId.Length > 0 && (string?)job["state"] != "done" && (string?)job["state"] != "failed")
            {
                if (DateTime.UtcNow > deadline)
                {
                    throw new StudioGatewayException(new Diagnostic("timeout", "Stage job " + jobId + " did not finish within " + budget.TotalSeconds + " s (last state " + job["state"] + ")."));
                }

                await Task.Delay(poll, ct).ConfigureAwait(false);
                job = await RawOf(get.Invoke(client, new object[] { jobId, ct })).ConfigureAwait(false);
            }

            return job;
        }

        private static (object Client, MethodInfo Method)? StageMethod(IAgentGateway gateway, string name, int parameters)
        {
            object? client = gateway.GetType().GetProperty("Client", Public)?.GetValue(gateway);
            if (client == null)
            {
                return null;
            }

            foreach (MethodInfo method in client.GetType().GetMethods(Public))
            {
                if (method.Name == name && method.GetParameters().Length == parameters && typeof(Task).IsAssignableFrom(method.ReturnType))
                {
                    return (client, method);
                }
            }

            return null;
        }

        private static async Task<JObject> RawOf(object? pending)
        {
            if (!(pending is Task task))
            {
                throw new StudioGatewayException(new Diagnostic("protocol", "The stage call returned no task."));
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception error) when (!(error is OutOfMemoryException) && !(error is StudioGatewayException))
            {
                throw new StudioGatewayException(GatewayErrors.ToDiagnostic(error));
            }

            object? result = task.GetType().GetProperty("Result", Public)?.GetValue(task);
            if (result?.GetType().GetProperty("Raw", Public)?.GetValue(result) is JObject raw)
            {
                return raw;
            }

            throw new StudioGatewayException(new Diagnostic("protocol", "The stage job answer has no JSON body."));
        }
    }
}
