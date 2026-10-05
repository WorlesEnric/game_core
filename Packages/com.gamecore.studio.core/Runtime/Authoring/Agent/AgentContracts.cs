// GameCore.Studio.Authoring.Agent - the Studio's view of the etos agent (boundary D, docs/studio/02-architecture.md s2;
// 04-etos-integration.md s2/s5), implemented by com.gamecore.studio.etos (P2.2, EtosAgentGateway) and consumed by the
// Studio UI (P2.1: prompt bar, task tray, candidate strip, voice button) without a reference to the etos package.
// The tool-facing IAgentGateway of GameCore.Studio.Authoring (asset.generate, mechanism.propose) stays as P1.6 shipped
// it; the etos gateway implements both, and the registered instance is found with AgentGatewayLookup.From(...).
// Names are shared with P2.1 (06 s2); the integrator reconciles if both packets add this file.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Authoring.Agent
{
    /// <summary>A provider capability as <c>/v1/hello</c> reports it.</summary>
    public enum ProviderState
    {
        /// <summary>Not determined yet (voice is unknown until the first session).</summary>
        Unknown,
        /// <summary>A provider is configured on the node.</summary>
        Live,
        /// <summary>No provider is configured.</summary>
        NotConfigured,
        /// <summary>The agent may not use it (grant, budget, credential).</summary>
        Blocked,
    }

    /// <summary>Reachability and provider status of the node and companion.</summary>
    public sealed class ProviderStatus
    {
        public ProviderStatus(
            ProviderState image,
            ProviderState tts,
            ProviderState voice,
            ProviderState threeD,
            ProviderState describe,
            bool nodeReachable,
            bool agentReady,
            string? companionVersion,
            Diagnostic? problem = null,
            DateTime? checkedAtUtc = null)
        {
            Image = image;
            Tts = tts;
            Voice = voice;
            ThreeD = threeD;
            Describe = describe;
            NodeReachable = nodeReachable;
            AgentReady = agentReady;
            CompanionVersion = companionVersion;
            Problem = problem;
            CheckedAtUtc = checkedAtUtc;
        }

        public ProviderState Image { get; }

        public ProviderState Tts { get; }

        public ProviderState Voice { get; }

        /// <summary>3D mesh generation (blocked/not_configured without a provider credential, SADR-020).</summary>
        public ProviderState ThreeD { get; }

        public ProviderState Describe { get; }

        /// <summary>The node answered (the app key was accepted).</summary>
        public bool NodeReachable { get; }

        /// <summary>The companion answered and its agent channel is connected.</summary>
        public bool AgentReady { get; }

        public string? CompanionVersion { get; }

        /// <summary>Why the status is degraded (etos code preserved, e.g. <c>agent_starting</c>, <c>not_configured</c>).</summary>
        public Diagnostic? Problem { get; }

        public DateTime? CheckedAtUtc { get; }

        /// <summary>Nothing known yet (before the first hello).</summary>
        public static ProviderStatus Unknown { get; } = new ProviderStatus(ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, false, false, null);

        /// <summary>The state of a capability by its hello name (<c>image</c>, <c>tts</c>, <c>voice</c>, <c>3d</c>, <c>describe</c>).</summary>
        public ProviderState For(string capability)
        {
            switch (capability)
            {
                case "image":
                    return Image;
                case "tts":
                    return Tts;
                case "voice":
                    return Voice;
                case "3d":
                    return ThreeD;
                case "describe":
                    return Describe;
                default:
                    return ProviderState.Unknown;
            }
        }

        public override string ToString()
        {
            return "node " + (NodeReachable ? "reachable" : "unreachable") + ", agent " + (AgentReady ? "ready" : "not ready")
                + "; image " + Image + ", tts " + Tts + ", voice " + Voice + ", 3d " + ThreeD + ", describe " + Describe
                + (Problem == null ? string.Empty : " (" + Problem.Code + ": " + Problem.Message + ")");
        }
    }

    /// <summary>A file sent with a request (at most 16 MiB; its sha256 is computed by the gateway).</summary>
    public sealed class Attachment
    {
        public Attachment(string name, string mediaType, byte[] data, string? role = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Role = role;
        }

        public string Name { get; }

        public string MediaType { get; }

        /// <summary><c>frame</c>, <c>reference</c>, ...</summary>
        public string? Role { get; }

        public byte[] Data { get; }
    }

    /// <summary>An AI edit request (02 s4 request path).</summary>
    public sealed class AgentRequest
    {
        public AgentRequest(string intent, SelectionSnapshot selection, SemanticIndex contextSlice, string toolCatalogRevision)
        {
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            ContextSlice = contextSlice ?? throw new ArgumentNullException(nameof(contextSlice));
            ToolCatalogRevision = toolCatalogRevision ?? throw new ArgumentNullException(nameof(toolCatalogRevision));
        }

        public string Intent { get; }

        public SelectionSnapshot Selection { get; }

        /// <summary>The bounded index slice (selection closure, depth 2, 64 KB; 03 s3).</summary>
        public SemanticIndex ContextSlice { get; }

        /// <summary><c>ToolRegistry.Catalog.Revision</c> at request time.</summary>
        public string ToolCatalogRevision { get; }

        /// <summary>Which worker: <c>design</c> (gc-designer, default) or <c>mechanism</c> (gc-mechanic).</summary>
        public string Mode { get; set; } = "design";

        public List<Attachment> Attachments { get; } = new List<Attachment>();

        /// <summary>The change set this one follows up (a retry or a re-ask after a rejection).</summary>
        public string? Parent { get; set; }

        /// <summary>The voice transcript the intent came from (origin becomes <c>voice</c>).</summary>
        public string? VoiceTranscriptId { get; set; }

        /// <summary>The change-set id to use; minted by the gateway when null. Resubmitting the same id is idempotent.</summary>
        public string? ChangeSetId { get; set; }
    }

    /// <summary>A request as the companion reports it, plus the Studio's local import state.</summary>
    public sealed class RequestView
    {
        public RequestView(
            string requestId,
            string changeSetId,
            string state,
            string? worker,
            string? taskId,
            string? taskStatus,
            int attempt,
            IReadOnlyList<string> tasks,
            JObject? outcome,
            bool hasCandidate,
            long seq,
            long createdAt,
            long updatedAt,
            string? intent = null,
            string? progress = null,
            string? localState = null,
            IReadOnlyList<Diagnostic>? diagnostics = null)
        {
            RequestId = requestId;
            ChangeSetId = changeSetId;
            State = state;
            Worker = worker;
            TaskId = taskId;
            TaskStatus = taskStatus;
            Attempt = attempt;
            Tasks = tasks;
            Outcome = outcome;
            HasCandidate = hasCandidate;
            Seq = seq;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            Intent = intent;
            Progress = progress;
            LocalState = localState;
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
        }

        public string RequestId { get; }

        public string ChangeSetId { get; }

        /// <summary>Companion state, verbatim: requested, running, waiting, candidate, candidate_invalid, needs_clarification, failed, cancelled, unresolved.</summary>
        public string State { get; }

        public string? Worker { get; }

        public string? TaskId { get; }

        /// <summary>etos task status, verbatim: queued, starting, running, waiting, done, failed, cancelled, unknown.</summary>
        public string? TaskStatus { get; }

        public int Attempt { get; }

        public IReadOnlyList<string> Tasks { get; }

        /// <summary><c>{code, message?, hint?, text?, diagnostics?, refusal?}</c>; code is candidate, task_failed, waiting, needs_clarification, cancelled, unresolved, candidate_invalid.</summary>
        public JObject? Outcome { get; }

        public bool HasCandidate { get; }

        public long Seq { get; }

        public long CreatedAt { get; }

        public long UpdatedAt { get; }

        /// <summary>The prompt text (known locally for requests submitted in this project).</summary>
        public string? Intent { get; }

        /// <summary>The last progress text of the task (redacted by the companion).</summary>
        public string? Progress { get; }

        /// <summary>Studio-side import: importing, staged, stage_refused, import_failed, applied, rejected (null before a candidate).</summary>
        public string? LocalState { get; }

        /// <summary>Findings of the import or staging (codes preserved).</summary>
        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>The UI label of the outcome: etos <c>unknown</c>/<c>outcome_unknown</c> show as unresolved, never success.</summary>
        public string OutcomeLabel => State == "unresolved" ? "unresolved" : (Outcome?["code"]?.ToString() ?? State);
    }

    /// <summary>A validated candidate is available for a request.</summary>
    public sealed class CandidateNotice
    {
        public CandidateNotice(string requestId, string changeSetId)
        {
            RequestId = requestId;
            ChangeSetId = changeSetId;
        }

        public string RequestId { get; }

        public string ChangeSetId { get; }
    }

    /// <summary>A direct media operation (04 s5): <c>generate.image</c>, <c>tts</c>, <c>describe</c>, <c>generate.3d</c>.</summary>
    public sealed class OpRequest
    {
        public OpRequest(string op, JObject? inputs = null, double? maxCostUsd = null)
        {
            Op = op ?? throw new ArgumentNullException(nameof(op));
            Inputs = inputs ?? new JObject();
            MaxCostUsd = maxCostUsd;
        }

        public string Op { get; }

        /// <summary>The op's input: <c>prompt</c>/<c>size</c> (image), <c>text</c>/<c>voice</c> (tts), <c>artifact</c> (describe: a sha256).</summary>
        public JObject Inputs { get; }

        /// <summary>Cost ceiling in USD; the configured default (0.50) when null. Always sent.</summary>
        public double? MaxCostUsd { get; }

        /// <summary>The change set the asset is for (also scopes the companion's idempotency key).</summary>
        public string? ChangeSetId { get; set; }
    }

    /// <summary>The result of a media operation: a verified artifact, a text (describe), or a refusal passed through untouched.</summary>
    public sealed class OpResult
    {
        public OpResult(string? sha256, string? mediaType, byte[]? bytes, Diagnostic? refusal, string? text = null, string? name = null, string? provider = null)
        {
            Sha256 = sha256;
            MediaType = mediaType;
            Bytes = bytes;
            Refusal = refusal;
            Text = text;
            Name = name;
            Provider = provider;
        }

        public string? Sha256 { get; }

        public string? MediaType { get; }

        /// <summary>The verified bytes (their sha256 equals <see cref="Sha256"/>).</summary>
        public byte[]? Bytes { get; }

        /// <summary>The etos refusal (<c>not_configured</c>, <c>budget_exhausted</c>, <c>blocked</c>...), code preserved.</summary>
        public Diagnostic? Refusal { get; }

        /// <summary>The answer of <c>describe</c>.</summary>
        public string? Text { get; }

        public string? Name { get; }

        public string? Provider { get; }

        /// <summary>The etops job state the companion reported (<c>{state, usage, error?}</c>), when it did.</summary>
        public JToken? State { get; set; }

        public bool Succeeded => Refusal == null;

        public static OpResult Refused(Diagnostic refusal) => new OpResult(null, null, null, refusal);
    }

    /// <summary>One transcript revision of the user's speech.</summary>
    public sealed class TranscriptUpdate
    {
        public TranscriptUpdate(string itemId, long revision, string text, bool final, string role = "user")
        {
            ItemId = itemId;
            Revision = revision;
            Text = text;
            Final = final;
            Role = role;
        }

        public string ItemId { get; }

        public long Revision { get; }

        /// <summary>The full text of this revision (not a delta).</summary>
        public string Text { get; }

        /// <summary>Only a final revision may be placed in the prompt box; nothing is ever sent implicitly.</summary>
        public bool Final { get; }

        public string Role { get; }
    }

    /// <summary>The Studio's agent gateway (etos through the companion).</summary>
    public interface IAgentGateway
    {
        ProviderStatus Status { get; }

        /// <summary>Raised on the main thread.</summary>
        event Action<ProviderStatus>? StatusChanged;

        /// <summary>Submits a request; returns the request id (= change-set id).</summary>
        Task<string> SubmitAsync(AgentRequest req, CancellationToken ct);

        /// <summary>Requests known to this project (recovered from the companion after a reload).</summary>
        IReadOnlyList<RequestView> Requests { get; }

        /// <summary>Raised on the main thread for every state or progress change.</summary>
        event Action<RequestView>? RequestChanged;

        /// <summary>Raised on the main thread when a validated candidate is available.</summary>
        event Action<CandidateNotice>? CandidateReady;

        Task CancelAsync(string requestId, CancellationToken ct);

        /// <summary>The candidate change set (read with the strict reader; refused when it carries null).</summary>
        Task<ChangeSet> FetchCandidateAsync(string requestId, CancellationToken ct);

        /// <summary>Artifact bytes, verified against <paramref name="sha256"/> before they are returned.</summary>
        Task<byte[]> FetchArtifactAsync(string sha256, CancellationToken ct);

        Task<OpResult> GenerateAsync(OpRequest req, CancellationToken ct);

        IVoiceSession CreateVoiceSession();
    }

    /// <summary>A microphone-to-transcript session (realtime voice through the companion).</summary>
    public interface IVoiceSession
    {
        Task StartAsync();

        Task StopAsync();

        /// <summary>Raised on the main thread for every revision.</summary>
        event Action<TranscriptUpdate>? Transcript;

        /// <summary>Input level in [0, 1], on the main thread.</summary>
        event Action<float>? Level;

        /// <summary>Refusals and failures (codes preserved), on the main thread.</summary>
        event Action<Diagnostic>? Error;
    }

    /// <summary>Finds the Studio agent gateway behind the tool-facing registration.</summary>
    public static class AgentGatewayLookup
    {
        /// <summary><c>StudioServices.Runtime.Services.AgentGateway</c> as the Studio gateway, or null.</summary>
        public static IAgentGateway? From(GameCore.Studio.Authoring.IAgentGateway? registered)
        {
            return registered as IAgentGateway;
        }
    }
}
