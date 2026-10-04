// GameCore.Studio.Authoring.Agent - the editor-facing agent gateway (docs/studio/04-etos-integration.md s2, 03 s2/s6,
// 06 P2.1/P2.2). Added by P2.1 (studio-ui) next to P1.6's tool-call gateway (IAgentGateway): the Studio UI renders
// requests, candidates, voice transcripts and provider status through IStudioAgentGateway; P2.2
// (com.gamecore.studio.etos) implements it over the companion's HTTP + WebSocket API. IStudioAgentGateway extends
// IAgentGateway so a single object registered as StudioServiceRegistry.AgentGateway serves both the built-in tools
// (asset.generate, mechanism.propose) and the UI.
//
// Vocabulary rules (04 s2/s3): request states are the companion's RequestView states, never invented; "Refused" is the
// only local state and means the submission itself was refused (the diagnostic carries the etos/companion code, e.g.
// not_configured, ledger_conflict). Provider availability is live | not_configured | blocked | unknown. Nothing here
// fabricates an outcome: a gateway that cannot answer reports NotConfigured.
//
// Threading: a gateway may complete tasks and raise events on any thread. Consumers marshal to the editor main thread.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Authoring.Agent
{
    /// <summary>Availability of one provider family as reported by the companion's <c>GET /v1/hello</c>.</summary>
    public enum ProviderAvailability
    {
        /// <summary>Not reported (no hello yet, or the companion does not know).</summary>
        Unknown,
        Live,
        NotConfigured,
        Blocked,
    }

    /// <summary>The provider families the Studio shows as status chips (04 s2 <c>/v1/hello</c>).</summary>
    public static class ProviderNames
    {
        public const string Image = "image";
        public const string Tts = "tts";
        public const string Voice = "voice";
        public const string ThreeD = "3d";
        public const string Describe = "describe";

        public static readonly IReadOnlyList<string> All = new[] { Image, Tts, Voice, ThreeD, Describe };

        /// <summary>The wire spelling of an availability (<c>live</c>, <c>not_configured</c>, <c>blocked</c>, <c>unknown</c>).</summary>
        public static string Wire(ProviderAvailability availability)
        {
            switch (availability)
            {
                case ProviderAvailability.Live:
                    return "live";
                case ProviderAvailability.NotConfigured:
                    return "not_configured";
                case ProviderAvailability.Blocked:
                    return "blocked";
                default:
                    return "unknown";
            }
        }

        /// <summary>Parses a wire spelling; anything unrecognised is <see cref="ProviderAvailability.Unknown"/>.</summary>
        public static ProviderAvailability Parse(string? wire)
        {
            switch (wire)
            {
                case "live":
                    return ProviderAvailability.Live;
                case "not_configured":
                    return ProviderAvailability.NotConfigured;
                case "blocked":
                    return ProviderAvailability.Blocked;
                default:
                    return ProviderAvailability.Unknown;
            }
        }
    }

    /// <summary>How far the editor got in reaching the companion.</summary>
    public enum GatewayConnection
    {
        /// <summary>No gateway, no key file or no base URL: nothing can be sent.</summary>
        NotConfigured,
        Connecting,
        Connected,
        /// <summary>Configured, but the node or the companion does not answer (transport error).</summary>
        Disconnected,
        /// <summary>The node answered with a refusal (e.g. <c>forbidden</c>, <c>wrong_app</c>, <c>agent_starting</c>).</summary>
        Refused,
    }

    /// <summary>Connection and provider status (immutable).</summary>
    public sealed class ProviderStatus
    {
        public ProviderStatus(
            GatewayConnection connection,
            IReadOnlyDictionary<string, ProviderAvailability>? providers = null,
            string? detail = null,
            string? version = null,
            DateTime? checkedUtc = null,
            string? code = null)
        {
            Connection = connection;
            Dictionary<string, ProviderAvailability> filled = new Dictionary<string, ProviderAvailability>(StringComparer.Ordinal);
            foreach (string name in ProviderNames.All)
            {
                filled[name] = providers != null && providers.TryGetValue(name, out ProviderAvailability value) ? value : ProviderAvailability.Unknown;
            }

            if (providers != null)
            {
                foreach (KeyValuePair<string, ProviderAvailability> pair in providers)
                {
                    filled[pair.Key] = pair.Value;
                }
            }

            Providers = filled;
            Detail = detail;
            Version = version;
            CheckedUtc = checkedUtc;
            Code = code;
        }

        public GatewayConnection Connection { get; }

        /// <summary>Every name of <see cref="ProviderNames.All"/> (Unknown when not reported) plus any extra family reported.</summary>
        public IReadOnlyDictionary<string, ProviderAvailability> Providers { get; }

        /// <summary>Human-readable reason (why not configured, the transport error, the refusal message).</summary>
        public string? Detail { get; }

        /// <summary>The refusal or transport code when <see cref="Connection"/> is Refused or Disconnected.</summary>
        public string? Code { get; }

        /// <summary>Companion version from <c>/v1/hello</c>.</summary>
        public string? Version { get; }

        public DateTime? CheckedUtc { get; }

        public bool IsConnected => Connection == GatewayConnection.Connected;

        public ProviderAvailability Of(string name) => Providers.TryGetValue(name, out ProviderAvailability value) ? value : ProviderAvailability.Unknown;

        public static ProviderStatus NotConfigured(string detail) => new ProviderStatus(GatewayConnection.NotConfigured, null, detail, null, null, "not_configured");
    }

    /// <summary>The viewport mode a request was sent in (03 s2 <c>mode</c>).</summary>
    public enum AgentRequestMode
    {
        Edit,
        Play,
    }

    /// <summary>
    /// Request states shown in the task tray: the companion's RequestView states (P0.5 s4) plus <see cref="Refused"/>,
    /// the local state of a submission the gateway refused before a request existed.
    /// </summary>
    public enum AgentRequestState
    {
        /// <summary>Companion <c>requested</c>: accepted, task not yet running (etos queued/starting).</summary>
        Queued,
        Running,
        /// <summary>etos <c>waiting</c>; <see cref="AgentRequestInfo.WaitingReason"/> says why (budget, input, ...).</summary>
        Waiting,
        Candidate,
        /// <summary>The worker's change set failed validation; the companion may re-ask once (a new task with parent).</summary>
        CandidateInvalid,
        NeedsClarification,
        /// <summary>Companion <c>failed</c>.</summary>
        TaskFailed,
        Cancelled,
        /// <summary>etos <c>unknown</c> / <c>outcome_unknown</c>; never a success.</summary>
        Unresolved,
        /// <summary>Local: the submission was refused (not configured, ledger conflict, transport); no request exists.</summary>
        Refused,
    }

    /// <summary>Wire spellings and lifecycle predicates of <see cref="AgentRequestState"/>.</summary>
    public static class AgentRequestStates
    {
        public static string Wire(AgentRequestState state)
        {
            switch (state)
            {
                case AgentRequestState.Queued:
                    return "queued";
                case AgentRequestState.Running:
                    return "running";
                case AgentRequestState.Waiting:
                    return "waiting";
                case AgentRequestState.Candidate:
                    return "candidate";
                case AgentRequestState.CandidateInvalid:
                    return "candidate_invalid";
                case AgentRequestState.NeedsClarification:
                    return "needs_clarification";
                case AgentRequestState.TaskFailed:
                    return "task_failed";
                case AgentRequestState.Cancelled:
                    return "cancelled";
                case AgentRequestState.Unresolved:
                    return "unresolved";
                default:
                    return "refused";
            }
        }

        /// <summary>
        /// Parses the tray spelling or the companion RequestView spelling (<c>requested</c> = Queued, <c>failed</c> =
        /// TaskFailed); null for anything else (callers must not guess a state).
        /// </summary>
        public static AgentRequestState? Parse(string? wire)
        {
            switch (wire)
            {
                case "queued":
                case "requested":
                    return AgentRequestState.Queued;
                case "running":
                    return AgentRequestState.Running;
                case "waiting":
                    return AgentRequestState.Waiting;
                case "candidate":
                    return AgentRequestState.Candidate;
                case "candidate_invalid":
                    return AgentRequestState.CandidateInvalid;
                case "needs_clarification":
                    return AgentRequestState.NeedsClarification;
                case "task_failed":
                case "failed":
                    return AgentRequestState.TaskFailed;
                case "cancelled":
                    return AgentRequestState.Cancelled;
                case "unresolved":
                    return AgentRequestState.Unresolved;
                case "refused":
                    return AgentRequestState.Refused;
                default:
                    return null;
            }
        }

        /// <summary>True when the request can still change (cancel is meaningful).</summary>
        public static bool IsOpen(AgentRequestState state) =>
            state == AgentRequestState.Queued || state == AgentRequestState.Running || state == AgentRequestState.Waiting;
    }

    /// <summary>A file dragged onto the prompt bar (P2.2 reads and uploads it).</summary>
    public sealed class AgentAttachment
    {
        public AgentAttachment(string path, string name, string mediaType, long bytes, string? role = null)
        {
            Path = path ?? throw new ArgumentNullException(nameof(path));
            Name = name ?? throw new ArgumentNullException(nameof(name));
            MediaType = mediaType ?? throw new ArgumentNullException(nameof(mediaType));
            Bytes = bytes;
            Role = role;
        }

        /// <summary>Absolute path of the file on disk.</summary>
        public string Path { get; }

        public string Name { get; }

        public string MediaType { get; }

        public long Bytes { get; }

        /// <summary>Optional role hint (<c>reference</c>, <c>texture</c>, ...).</summary>
        public string? Role { get; }
    }

    /// <summary>
    /// An edit request (04 s2 <c>EditRequest</c>): intent, selection snapshot, bounded context slice, tool catalog
    /// revision, mode and attachments. Built by the Studio UI; serialized and sent by P2.2.
    /// </summary>
    public sealed class AgentRequest
    {
        public AgentRequest(
            string changeSetId,
            Intent intent,
            SelectionSnapshot selection,
            JObject contextSlice,
            bool contextTruncated,
            int contextBytes,
            int contextOmittedNodes,
            string toolCatalogRevision,
            AgentRequestMode mode,
            IReadOnlyList<AgentAttachment>? attachments = null,
            string? parent = null,
            string? worker = null)
        {
            ChangeSetId = changeSetId ?? throw new ArgumentNullException(nameof(changeSetId));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            Selection = selection ?? throw new ArgumentNullException(nameof(selection));
            ContextSlice = contextSlice ?? throw new ArgumentNullException(nameof(contextSlice));
            ContextTruncated = contextTruncated;
            ContextBytes = contextBytes;
            ContextOmittedNodes = contextOmittedNodes;
            ToolCatalogRevision = toolCatalogRevision ?? throw new ArgumentNullException(nameof(toolCatalogRevision));
            Mode = mode;
            Attachments = attachments ?? Array.Empty<AgentAttachment>();
            Parent = parent;
            Worker = worker;
        }

        /// <summary>Minted by Unity (<c>cs_</c> + ULID); the companion is idempotent on it.</summary>
        public string ChangeSetId { get; }

        public Intent Intent { get; }

        public SelectionSnapshot Selection { get; }

        /// <summary>The serialized index slice (03 s3) of the selection closure.</summary>
        public JObject ContextSlice { get; }

        /// <summary>True when the slice hit its byte cap (reported explicitly, 03 s3).</summary>
        public bool ContextTruncated { get; }

        public int ContextBytes { get; }

        public int ContextOmittedNodes { get; }

        /// <summary>sha256 hex of the canonical tool catalog (03 s9); a candidate built against another revision is StaleContext.</summary>
        public string ToolCatalogRevision { get; }

        public AgentRequestMode Mode { get; }

        public IReadOnlyList<AgentAttachment> Attachments { get; }

        /// <summary>The change set this request follows up (clarification answers, re-asks); omitted when absent.</summary>
        public string? Parent { get; }

        /// <summary>The worker to use (companion default <c>gc-designer</c> when absent).</summary>
        public string? Worker { get; }

        /// <summary>
        /// The JSON body of 04 s2 <c>POST /v1/requests</c> without attachment bytes (P2.2 adds <c>data</c>/<c>sha256</c>):
        /// optional members are omitted, never null (03 s9).
        /// </summary>
        public JObject ToJson()
        {
            JObject body = new JObject
            {
                ["changeSetId"] = ChangeSetId,
                ["intent"] = StudioJson.ToToken(Intent),
                ["selection"] = StudioJson.ToToken(Selection),
                ["contextSlice"] = ContextSlice.DeepClone(),
                ["toolCatalogRevision"] = ToolCatalogRevision,
                ["mode"] = Mode == AgentRequestMode.Play ? "Play" : "Edit",
            };
            if (ContextTruncated)
            {
                body["contextTruncated"] = true;
            }

            if (Worker != null)
            {
                body["worker"] = Worker;
            }

            if (Parent != null)
            {
                body["parent"] = Parent;
            }

            if (Attachments.Count > 0)
            {
                JArray attachments = new JArray();
                foreach (AgentAttachment attachment in Attachments)
                {
                    JObject row = new JObject { ["name"] = attachment.Name, ["mediaType"] = attachment.MediaType, ["bytes"] = attachment.Bytes };
                    if (attachment.Role != null)
                    {
                        row["role"] = attachment.Role;
                    }

                    attachments.Add(row);
                }

                body["attachments"] = attachments;
            }

            return body;
        }
    }

    /// <summary>The answer to a submission.</summary>
    public sealed class RequestHandle
    {
        public RequestHandle(string changeSetId, AgentRequestState state, string? requestId = null, string? taskId = null, Diagnostic? refusal = null)
        {
            ChangeSetId = changeSetId ?? throw new ArgumentNullException(nameof(changeSetId));
            State = state;
            RequestId = requestId;
            TaskId = taskId;
            Refusal = refusal;
        }

        public string ChangeSetId { get; }

        /// <summary>The companion's request id; null when the submission was refused.</summary>
        public string? RequestId { get; }

        public string? TaskId { get; }

        public AgentRequestState State { get; }

        /// <summary>Why the submission was refused (State Refused); the code passes through unchanged.</summary>
        public Diagnostic? Refusal { get; }

        public bool Accepted => State != AgentRequestState.Refused && RequestId != null;

        public static RequestHandle Refused(string changeSetId, Diagnostic refusal) => new RequestHandle(changeSetId, AgentRequestState.Refused, null, null, refusal);
    }

    /// <summary>One request as the companion reports it (a task tray row).</summary>
    public sealed class AgentRequestInfo
    {
        public AgentRequestInfo(
            string requestId,
            string changeSetId,
            AgentRequestState state,
            string intentText,
            DateTime createdUtc,
            DateTime updatedUtc,
            string? worker = null,
            string? waitingReason = null,
            double? costUsd = null,
            IReadOnlyList<string>? taskIds = null,
            IReadOnlyList<Diagnostic>? diagnostics = null,
            string? question = null,
            string? parent = null,
            string? toolCatalogRevision = null,
            long sequence = 0,
            string? etosStatus = null)
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            ChangeSetId = changeSetId ?? throw new ArgumentNullException(nameof(changeSetId));
            State = state;
            IntentText = intentText ?? string.Empty;
            CreatedUtc = createdUtc;
            UpdatedUtc = updatedUtc;
            Worker = worker;
            WaitingReason = waitingReason;
            CostUsd = costUsd;
            TaskIds = taskIds ?? Array.Empty<string>();
            Diagnostics = diagnostics ?? Array.Empty<Diagnostic>();
            Question = question;
            Parent = parent;
            ToolCatalogRevision = toolCatalogRevision;
            Sequence = sequence;
            EtosStatus = etosStatus;
        }

        public string RequestId { get; }

        public string ChangeSetId { get; }

        public AgentRequestState State { get; }

        public string IntentText { get; }

        public DateTime CreatedUtc { get; }

        public DateTime UpdatedUtc { get; }

        public string? Worker { get; }

        /// <summary>Why a Waiting request waits (<c>budget</c>, <c>input</c>, ...), from the record text; null otherwise.</summary>
        public string? WaitingReason { get; }

        /// <summary>Cost reported by etos usage, when provided.</summary>
        public double? CostUsd { get; }

        /// <summary>etos task ids, oldest first (re-asks are new tasks with <c>parent</c>).</summary>
        public IReadOnlyList<string> TaskIds { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        /// <summary>The worker's clarification question (NeedsClarification).</summary>
        public string? Question { get; }

        public string? Parent { get; }

        public string? ToolCatalogRevision { get; }

        /// <summary>The companion ledger sequence (cursor for <c>GET /v1/requests?after=</c>).</summary>
        public long Sequence { get; }

        /// <summary>The etos task status verbatim (<c>queued</c>, <c>starting</c>, ...), when known.</summary>
        public string? EtosStatus { get; }

        public string ElapsedText(DateTime nowUtc)
        {
            DateTime end = AgentRequestStates.IsOpen(State) ? nowUtc : UpdatedUtc;
            TimeSpan elapsed = end - CreatedUtc;
            if (elapsed < TimeSpan.Zero)
            {
                elapsed = TimeSpan.Zero;
            }

            return elapsed.TotalHours >= 1
                ? ((int)elapsed.TotalHours).ToString(CultureInfo.InvariantCulture) + "h" + elapsed.Minutes.ToString("00", CultureInfo.InvariantCulture) + "m"
                : ((int)elapsed.TotalMinutes).ToString(CultureInfo.InvariantCulture) + ":" + elapsed.Seconds.ToString("00", CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A validated candidate (04 s2 <c>GET /v1/candidates/{id}</c>): the change set plus its envelope.</summary>
    public sealed class AgentCandidate
    {
        public AgentCandidate(string requestId, ChangeSet changeSet, string? toolCatalogRevision, IReadOnlyList<ArtifactRef>? artifacts = null, IReadOnlyList<Diagnostic>? warnings = null, string? taskId = null)
        {
            RequestId = requestId ?? throw new ArgumentNullException(nameof(requestId));
            ChangeSet = changeSet ?? throw new ArgumentNullException(nameof(changeSet));
            ToolCatalogRevision = toolCatalogRevision;
            Artifacts = artifacts ?? changeSet.Artifacts ?? Array.Empty<ArtifactRef>();
            Warnings = warnings ?? Array.Empty<Diagnostic>();
            TaskId = taskId;
        }

        public string RequestId { get; }

        public ChangeSet ChangeSet { get; }

        /// <summary>The catalog revision the worker planned against (StaleContext when it differs from the registry's).</summary>
        public string? ToolCatalogRevision { get; }

        public IReadOnlyList<ArtifactRef> Artifacts { get; }

        public IReadOnlyList<Diagnostic> Warnings { get; }

        public string? TaskId { get; }
    }

    /// <summary>One transcript revision of a voice utterance (04 s5): only <see cref="IsFinal"/> text may become a prompt.</summary>
    public sealed class VoiceTranscript
    {
        public VoiceTranscript(string utteranceId, string text, int revision, bool isFinal)
        {
            UtteranceId = utteranceId ?? throw new ArgumentNullException(nameof(utteranceId));
            Text = text ?? string.Empty;
            Revision = revision;
            IsFinal = isFinal;
        }

        public string UtteranceId { get; }

        public string Text { get; }

        public int Revision { get; }

        public bool IsFinal { get; }
    }

    /// <summary>What an <see cref="AgentEvent"/> reports.</summary>
    public enum AgentEventKind
    {
        /// <summary>A request changed state (<see cref="AgentEvent.Request"/>).</summary>
        RequestUpdated,
        /// <summary>A validated candidate is ready (<see cref="AgentEvent.RequestId"/>, <see cref="AgentEvent.ChangeSetId"/>).</summary>
        CandidateReady,
        /// <summary>A transcript revision (<see cref="AgentEvent.Transcript"/>).</summary>
        VoiceTranscript,
        /// <summary>Connection or provider status changed (<see cref="AgentEvent.Status"/>).</summary>
        ProviderStatusChanged,
    }

    /// <summary>One ordered event of the gateway's stream (04 s2 <c>WS /v1/events</c>, cursor included).</summary>
    public sealed class AgentEvent
    {
        private AgentEvent(AgentEventKind kind, long cursor, AgentRequestInfo? request, string? requestId, string? changeSetId, VoiceTranscript? transcript, ProviderStatus? status)
        {
            Kind = kind;
            Cursor = cursor;
            Request = request;
            RequestId = requestId ?? request?.RequestId;
            ChangeSetId = changeSetId ?? request?.ChangeSetId;
            Transcript = transcript;
            Status = status;
        }

        public AgentEventKind Kind { get; }

        /// <summary>The companion event cursor (0 for local events such as status changes).</summary>
        public long Cursor { get; }

        public AgentRequestInfo? Request { get; }

        public string? RequestId { get; }

        public string? ChangeSetId { get; }

        public VoiceTranscript? Transcript { get; }

        public ProviderStatus? Status { get; }

        public static AgentEvent RequestUpdated(AgentRequestInfo request, long cursor = 0) =>
            new AgentEvent(AgentEventKind.RequestUpdated, cursor, request ?? throw new ArgumentNullException(nameof(request)), null, null, null, null);

        public static AgentEvent CandidateReady(string requestId, string changeSetId, long cursor = 0) =>
            new AgentEvent(AgentEventKind.CandidateReady, cursor, null, requestId, changeSetId, null, null);

        public static AgentEvent Voice(VoiceTranscript transcript, long cursor = 0) =>
            new AgentEvent(AgentEventKind.VoiceTranscript, cursor, null, null, null, transcript ?? throw new ArgumentNullException(nameof(transcript)), null);

        public static AgentEvent StatusChanged(ProviderStatus status) =>
            new AgentEvent(AgentEventKind.ProviderStatusChanged, 0, null, null, null, null, status ?? throw new ArgumentNullException(nameof(status)));
    }

    /// <summary>
    /// A realtime transcription session (04 s5, W-VOICE-01). Microphone capture and the WebSocket belong to P2.2. A
    /// transcript never triggers anything by itself: the UI only places final text in the prompt box.
    /// </summary>
    public interface IVoiceSession
    {
        bool IsActive { get; }

        /// <summary>Starts capturing and streaming (no-op when active).</summary>
        void Start();

        /// <summary>Stops capturing; the final transcript of the current utterance may still arrive.</summary>
        void Stop();

        /// <summary>Transcript revisions; <see cref="VoiceTranscript.IsFinal"/> only on the utterance's done revision.</summary>
        event Action<VoiceTranscript>? Transcript;

        /// <summary>Input level 0..1 (for a meter).</summary>
        event Action<float>? Level;

        /// <summary>The session failed or was refused (code passes through, e.g. <c>rate_limited</c>, <c>not_configured</c>).</summary>
        event Action<Diagnostic>? Failed;
    }

    /// <summary>Raised (as a faulted task) by gateway calls that cannot be answered; never replaced by a made-up result.</summary>
    public sealed class AgentGatewayException : Exception
    {
        public AgentGatewayException(Diagnostic diagnostic)
            : base(diagnostic?.Code + ": " + diagnostic?.Message)
        {
            Diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        }

        public Diagnostic Diagnostic { get; }
    }

    /// <summary>
    /// The editor side of boundary D for the Studio UI (implemented by com.gamecore.studio.etos, P2.2). Register the
    /// implementation as <c>StudioServiceRegistry.AgentGateway</c>; the UI uses it when it implements this interface
    /// and falls back to <see cref="NullStudioAgentGateway"/> otherwise.
    /// </summary>
    public interface IStudioAgentGateway : IAgentGateway
    {
        /// <summary>Current connection and provider status (cheap; refreshed by the gateway).</summary>
        ProviderStatus Status { get; }

        /// <summary>Ordered request, candidate, voice and status events. Observers may be called on any thread.</summary>
        IObservable<AgentEvent> Events { get; }

        /// <summary>The voice session, or null when voice is unavailable (the status says why).</summary>
        IVoiceSession? Voice { get; }

        /// <summary>Submits a request (idempotent on <see cref="AgentRequest.ChangeSetId"/>); a refusal is a Refused handle, not an exception.</summary>
        Task<RequestHandle> Submit(AgentRequest request);

        /// <summary>Cancels a request (04 s2 <c>POST /v1/requests/{id}/cancel</c>); returns the request as it now stands.</summary>
        Task<AgentRequestInfo?> Cancel(string requestId);

        /// <summary>This app's requests after a ledger sequence (recovery after a domain reload).</summary>
        Task<IReadOnlyList<AgentRequestInfo>> ListRequests(long after = 0);

        /// <summary>The validated candidate of a request (04 s2 <c>GET /v1/candidates/{id}</c>).</summary>
        Task<AgentCandidate> FetchCandidate(string requestId);

        /// <summary>Artifact bytes by sha256 (hex); the caller verifies the digest again before retaining them.</summary>
        Task<byte[]> FetchArtifact(string sha256);

        /// <summary>Tells the companion a candidate was rejected and why (recorded in its ledger and the RG).</summary>
        Task RejectCandidate(string changeSetId, string reason);
    }
}
