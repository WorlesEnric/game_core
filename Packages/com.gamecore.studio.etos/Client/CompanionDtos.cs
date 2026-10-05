// GameCore.Studio.Etos.Client - the companion's API shapes (04 s2; docs/studio/packets/P0.5-companion.md s4), read
// leniently from JSON (each shape keeps its Raw object so nothing the companion added is lost) and written without
// null members. Request states and etos task statuses are strings and are shown verbatim; nothing here invents a state.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>Companion request states (RequestView.state).</summary>
    public static class RequestStates
    {
        public const string Requested = "requested";
        public const string Running = "running";
        public const string Waiting = "waiting";
        public const string Candidate = "candidate";
        public const string CandidateInvalid = "candidate_invalid";
        public const string NeedsClarification = "needs_clarification";
        public const string Failed = "failed";
        public const string Cancelled = "cancelled";
        public const string Unresolved = "unresolved";

        /// <summary>States the companion stops following (it keeps following <c>unresolved</c> a bounded number of times).</summary>
        public static bool IsTerminal(string? state)
        {
            return state == Candidate || state == CandidateInvalid || state == NeedsClarification || state == Failed || state == Cancelled;
        }

        /// <summary>True for terminal states and <c>unresolved</c>: nothing more will arrive without user action.</summary>
        public static bool IsSettled(string? state) => IsTerminal(state) || state == Unresolved;
    }

    /// <summary>Provider capability states in <c>/v1/hello</c>.</summary>
    public static class ProviderStates
    {
        public const string Live = "live";
        public const string NotConfigured = "not_configured";
        public const string Blocked = "blocked";
        public const string Unknown = "unknown";
    }

    /// <summary><c>GET /v1/hello</c>.</summary>
    public sealed class HelloInfo
    {
        public HelloInfo(JObject raw)
        {
            Raw = raw;
            Service = Json.Str(raw, "service") ?? string.Empty;
            Version = Json.Str(raw, "version") ?? string.Empty;
            Protocol = (int)(Json.Long(raw, "protocol") ?? 0);
            App = Json.Str(raw, "app");
            Node = Json.Str(raw, "node");
            Sdk = Json.Str(raw, "sdk");
            Connected = Json.Bool(raw, "connected") ?? false;
            Capabilities = Json.Strings(raw, "capabilities");
            Workers = Json.Strings(raw, "workers");
            ToolCatalogRevisions = Json.Strings(raw, "toolCatalogRevisions");
            ProvidersCheckedAt = Json.Long(raw, "providersCheckedAt");
            IndexRevision = Json.Long(raw, "indexRevision");
            Dictionary<string, string> states = new Dictionary<string, string>(StringComparer.Ordinal);
            if (raw["providers"] is JObject providers)
            {
                foreach (JProperty property in providers.Properties())
                {
                    JToken? value = property.Value;
                    string? state = value != null && value.Type == JTokenType.String ? (string?)value : null;
                    if (state != null)
                    {
                        states[property.Name] = state;
                    }
                }
            }

            Providers = states;
        }

        public JObject Raw { get; }

        public string Service { get; }

        public string Version { get; }

        public int Protocol { get; }

        public string? App { get; }

        public string? Node { get; }

        public string? Sdk { get; }

        /// <summary>Whether the companion's agent channel to the node is connected.</summary>
        public bool Connected { get; }

        public IReadOnlyList<string> Capabilities { get; }

        public IReadOnlyList<string> Workers { get; }

        /// <summary>Tool catalog revisions the companion already holds (a request may then omit the catalog).</summary>
        public IReadOnlyList<string> ToolCatalogRevisions { get; }

        public long? ProvidersCheckedAt { get; }

        public long? IndexRevision { get; }

        /// <summary><c>image</c>, <c>tts</c>, <c>voice</c>, <c>3d</c>, <c>describe</c> → <see cref="ProviderStates"/>.</summary>
        public IReadOnlyDictionary<string, string> Providers { get; }

        public string Provider(string capability) => Providers.TryGetValue(capability, out string? state) ? state : ProviderStates.Unknown;

        /// <summary>True when the companion holds the catalog revision (<c>hex</c> or <c>sha256:hex</c>).</summary>
        public bool HoldsCatalog(string revision)
        {
            string? wanted = Json.NormalizeSha256(revision) ?? revision;
            foreach (string held in ToolCatalogRevisions)
            {
                if (string.Equals(Json.NormalizeSha256(held) ?? held, wanted, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A request as the companion reports it (<c>RequestView</c>).</summary>
    public sealed class RequestInfo
    {
        public RequestInfo(JObject raw)
        {
            Raw = raw;
            RequestId = Json.Str(raw, "requestId") ?? Json.Str(raw, "changeSetId") ?? string.Empty;
            ChangeSetId = Json.Str(raw, "changeSetId") ?? RequestId;
            Worker = Json.Str(raw, "worker");
            State = Json.Str(raw, "state") ?? RequestStates.Requested;
            TaskId = Json.Str(raw, "taskId");
            TaskStatus = Json.Str(raw, "taskStatus");
            Topic = Json.Str(raw, "topic");
            Attempt = (int)(Json.Long(raw, "attempt") ?? 0);
            Tasks = Json.Strings(raw, "tasks");
            Outcome = Json.Obj(raw, "outcome");
            HasCandidate = Json.Bool(raw, "hasCandidate") ?? false;
            Seq = Json.Long(raw, "seq") ?? 0;
            CreatedAt = Json.Long(raw, "createdAt") ?? 0;
            UpdatedAt = Json.Long(raw, "updatedAt") ?? 0;
        }

        public JObject Raw { get; }

        public string RequestId { get; }

        public string ChangeSetId { get; }

        public string? Worker { get; }

        /// <summary>Companion state (<see cref="RequestStates"/>), verbatim.</summary>
        public string State { get; }

        public string? TaskId { get; }

        /// <summary>etos task status, verbatim (<c>queued|starting|running|waiting|done|failed|cancelled|unknown</c>).</summary>
        public string? TaskStatus { get; }

        public string? Topic { get; }

        public int Attempt { get; }

        public IReadOnlyList<string> Tasks { get; }

        /// <summary><c>{code, message?, hint?, text?, diagnostics?, refusal?}</c> once settled.</summary>
        public JObject? Outcome { get; }

        public string? OutcomeCode => Json.Str(Outcome, "code");

        public bool HasCandidate { get; }

        /// <summary>Ledger position of the last change (the <c>after</c> cursor of <c>GET /v1/requests</c>).</summary>
        public long Seq { get; }

        public long CreatedAt { get; }

        public long UpdatedAt { get; }

        public bool IsTerminal => RequestStates.IsTerminal(State);
    }

    /// <summary><c>POST /v1/requests</c> answer.</summary>
    public sealed class SubmitResult
    {
        public SubmitResult(JObject raw)
        {
            Raw = raw;
            JObject view = Json.Obj(raw, "request") ?? raw;
            Request = new RequestInfo(view);
            RequestId = Json.Str(raw, "requestId") ?? Request.RequestId;
            ChangeSetId = Json.Str(raw, "changeSetId") ?? Request.ChangeSetId;
            TaskId = Json.Str(raw, "taskId") ?? Request.TaskId;
            State = Json.Str(raw, "state") ?? Request.State;
            TaskStatus = Json.Str(raw, "taskStatus") ?? Request.TaskStatus;
        }

        public JObject Raw { get; }

        public string RequestId { get; }

        public string ChangeSetId { get; }

        public string? TaskId { get; }

        public string State { get; }

        public string? TaskStatus { get; }

        public RequestInfo Request { get; }
    }

    /// <summary><c>GET /v1/requests?after=</c> answer.</summary>
    public sealed class RequestPage
    {
        public RequestPage(JObject raw)
        {
            List<RequestInfo> requests = new List<RequestInfo>();
            if (raw["requests"] is JArray array)
            {
                foreach (JToken item in array)
                {
                    if (item is JObject obj)
                    {
                        requests.Add(new RequestInfo(obj));
                    }
                }
            }

            Requests = requests;
            Next = Json.Long(raw, "next") ?? 0;
        }

        public IReadOnlyList<RequestInfo> Requests { get; }

        public long Next { get; }
    }

    /// <summary>An artifact as the companion stores and serves it (verified on its write).</summary>
    public sealed class StoredArtifactInfo
    {
        public StoredArtifactInfo(JObject raw)
        {
            Raw = raw;
            Sha256 = Json.NormalizeSha256(Json.Str(raw, "sha256")) ?? Json.Str(raw, "sha256") ?? string.Empty;
            Name = Json.Str(raw, "name") ?? Sha256;
            MediaType = Json.Str(raw, "mediaType") ?? Json.Str(raw, "media_type") ?? "application/octet-stream";
            Bytes = Json.Long(raw, "bytes");
            Producer = raw["producer"] is JObject producer ? producer : null;
            Role = Json.Str(raw, "role");
            Url = Json.Str(raw, "url");
        }

        public JObject Raw { get; }

        public string Sha256 { get; }

        public string Name { get; }

        public string MediaType { get; }

        public long? Bytes { get; }

        public JObject? Producer { get; }

        public string? Role { get; }

        public string? Url { get; }

        internal static List<StoredArtifactInfo> ListOf(JArray? array)
        {
            List<StoredArtifactInfo> list = new List<StoredArtifactInfo>();
            if (array != null)
            {
                foreach (JToken item in array)
                {
                    if (item is JObject obj)
                    {
                        list.Add(new StoredArtifactInfo(obj));
                    }
                }
            }

            return list;
        }
    }

    /// <summary><c>GET /v1/candidates/{id}</c>.</summary>
    public sealed class CandidateInfo
    {
        public CandidateInfo(JObject raw)
        {
            Raw = raw;
            ChangeSetId = Json.Str(raw, "changeSetId") ?? string.Empty;
            TaskId = Json.Str(raw, "taskId");
            Attempt = (int)(Json.Long(raw, "attempt") ?? 0);
            ChangeSet = Json.Obj(raw, "changeSet") ?? throw EtosException.Protocol("The candidate answer has no changeSet object.");
            Artifacts = StoredArtifactInfo.ListOf(Json.Arr(raw, "artifacts"));
            ToolCatalogRevision = Json.Str(raw, "toolCatalogRevision");
            Diagnostics = Json.Arr(raw, "diagnostics");
            ReceivedAt = Json.Long(raw, "receivedAt") ?? 0;
        }

        public JObject Raw { get; }

        public string ChangeSetId { get; }

        public string? TaskId { get; }

        public int Attempt { get; }

        /// <summary>The validated change set, verbatim as the worker wrote it.</summary>
        public JObject ChangeSet { get; }

        public IReadOnlyList<StoredArtifactInfo> Artifacts { get; }

        /// <summary>The catalog revision the candidate was built and checked against.</summary>
        public string? ToolCatalogRevision { get; }

        /// <summary>Non-fatal findings (warnings), verbatim.</summary>
        public JArray? Diagnostics { get; }

        public long ReceivedAt { get; }

        public StoredArtifactInfo? FindArtifact(string sha256)
        {
            string? wanted = Json.NormalizeSha256(sha256);
            foreach (StoredArtifactInfo artifact in Artifacts)
            {
                if (string.Equals(artifact.Sha256, wanted, StringComparison.Ordinal))
                {
                    return artifact;
                }
            }

            return null;
        }
    }

    /// <summary><c>POST /v1/ops/generate</c> answer.</summary>
    public sealed class GenerateResult
    {
        public GenerateResult(JObject raw)
        {
            Raw = raw;
            Op = Json.Str(raw, "op") ?? string.Empty;
            EtosOp = Json.Str(raw, "etosOp");
            Provider = Json.Str(raw, "provider");
            State = raw["state"];
            MaxCostUsd = Json.Double(raw, "max_cost_usd") ?? Json.Double(raw, "maxCostUsd");
            Artifacts = StoredArtifactInfo.ListOf(Json.Arr(raw, "artifacts"));
            Text = Json.Str(raw, "text");
            Key = Json.Str(raw, "key");
        }

        public JObject Raw { get; }

        public string Op { get; }

        public string? EtosOp { get; }

        public string? Provider { get; }

        public JToken? State { get; }

        public double? MaxCostUsd { get; }

        public IReadOnlyList<StoredArtifactInfo> Artifacts { get; }

        /// <summary>The answer of <c>describe</c>.</summary>
        public string? Text { get; }

        /// <summary>The etops idempotency key the companion used.</summary>
        public string? Key { get; }
    }

    /// <summary><c>POST /v1/index/delta</c> answer.</summary>
    public sealed class IndexDeltaAck
    {
        public IndexDeltaAck(JObject raw)
        {
            Revision = Json.Long(raw, "revision") ?? 0;
            Queued = (int)(Json.Long(raw, "queued") ?? 0);
            Skipped = (int)(Json.Long(raw, "skipped") ?? 0);
        }

        public long Revision { get; }

        public int Queued { get; }

        public int Skipped { get; }
    }

    /// <summary>A stage job (<c>POST /v1/stage</c>, <c>GET /v1/stage/{job}</c>).</summary>
    public sealed class StageJobInfo
    {
        public StageJobInfo(JObject raw)
        {
            Raw = raw;
            JobId = Json.Str(raw, "jobId") ?? string.Empty;
            ChangeSetId = Json.Str(raw, "changeSetId") ?? string.Empty;
            PackageRef = Json.Str(raw, "packageRef") ?? string.Empty;
            State = Json.Str(raw, "state") ?? string.Empty;
            Slot = Json.Str(raw, "slot");
            Verdict = raw["verdict"];
        }

        public JObject Raw { get; }

        public string JobId { get; }

        public string ChangeSetId { get; }

        public string PackageRef { get; }

        /// <summary><c>queued | running | done | failed</c>.</summary>
        public string State { get; }

        public string? Slot { get; }

        public JToken? Verdict { get; }
    }

    /// <summary>One frame of <c>WS /v1/events</c>: <c>{cursor, at, type, requestId?, data}</c>.</summary>
    public sealed class EventFrame
    {
        public EventFrame(long cursor, long at, string type, string? requestId, JToken data)
        {
            Cursor = cursor;
            At = at;
            Type = type;
            RequestId = requestId;
            Data = data;
        }

        public long Cursor { get; }

        /// <summary>When the companion recorded it (ms since the epoch).</summary>
        public long At { get; }

        /// <summary><c>request | task_progress | candidate | candidate_invalid | clarification | asset | voice_session | voice_transcript | stage</c>.</summary>
        public string Type { get; }

        public string? RequestId { get; }

        public JToken Data { get; }

        /// <summary>When the client received it (ms since the epoch); set by the event stream.</summary>
        public long ReceivedAt { get; internal set; }

        /// <summary>Parses a frame; null for text that is not an event frame.</summary>
        public static EventFrame? Parse(string text)
        {
            JObject obj;
            try
            {
                obj = JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return null;
            }

            long? cursor = Json.Long(obj, "cursor");
            string? type = Json.Str(obj, "type");
            if (cursor == null || type == null)
            {
                return null;
            }

            return new EventFrame(cursor.Value, Json.Long(obj, "at") ?? 0, type, Json.Str(obj, "requestId"), obj["data"] ?? new JObject());
        }
    }

    /// <summary>Bytes downloaded from <c>/v1/artifacts/{sha256}</c> whose digest (and size, when declared) was verified.</summary>
    public sealed class VerifiedArtifact
    {
        public VerifiedArtifact(string sha256, string mediaType, byte[] bytes)
        {
            Sha256 = sha256;
            MediaType = mediaType;
            Bytes = bytes;
        }

        public string Sha256 { get; }

        public string MediaType { get; }

        public byte[] Bytes { get; }
    }

    /// <summary>A file sent with an edit request (inline base64; at most 16 MiB each, 8 per request, 64 MiB together).</summary>
    public sealed class AttachmentBody
    {
        public AttachmentBody(string name, string mediaType, byte[] data, string? role = null)
        {
            Name = string.IsNullOrEmpty(name) ? throw new ArgumentException("An attachment needs a name.", nameof(name)) : name;
            MediaType = string.IsNullOrEmpty(mediaType) ? "application/octet-stream" : mediaType;
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Role = role;
            Sha256 = Json.Sha256Hex(data);
        }

        public string Name { get; }

        public string MediaType { get; }

        public string? Role { get; }

        public byte[] Data { get; }

        public string Sha256 { get; }
    }

    /// <summary><c>POST /v1/requests</c> body: <c>EditRequest</c> (04 s2). Written without null members.</summary>
    public sealed class EditRequestBody
    {
        public const int MaxAttachments = 8;
        public const long MaxAttachmentBytes = 16L * 1024 * 1024;
        public const long MaxAttachmentsTotal = 64L * 1024 * 1024;

        private static readonly Regex ChangeSetIdPattern = new Regex("^cs_[0-7][0-9A-HJKMNP-TV-Z]{25}$", RegexOptions.CultureInvariant);

        public EditRequestBody(string changeSetId, string intentText, string origin, JObject selection, JObject contextSlice, string toolCatalogRevision)
        {
            ChangeSetId = changeSetId;
            IntentText = intentText;
            Origin = origin;
            Selection = selection;
            ContextSlice = contextSlice;
            ToolCatalogRevision = toolCatalogRevision;
        }

        public string ChangeSetId { get; }

        public string IntentText { get; }

        /// <summary><c>agent | manual | voice | replay</c>.</summary>
        public string Origin { get; }

        public string? VoiceTranscriptId { get; set; }

        public JObject Selection { get; }

        public JObject ContextSlice { get; }

        public string ToolCatalogRevision { get; }

        /// <summary>The full catalog; sent only when the companion does not hold <see cref="ToolCatalogRevision"/>.</summary>
        public JObject? ToolCatalog { get; set; }

        /// <summary><c>gc-designer</c> (the companion's default when null) or <c>gc-mechanic</c>.</summary>
        public string? Worker { get; set; }

        public List<AttachmentBody> Attachments { get; } = new List<AttachmentBody>();

        /// <summary>The same body with the full catalog (for the one retry after <c>stale_context</c>).</summary>
        public EditRequestBody WithCatalog(JObject catalog)
        {
            EditRequestBody copy = new EditRequestBody(ChangeSetId, IntentText, Origin, Selection, ContextSlice, ToolCatalogRevision)
            {
                VoiceTranscriptId = VoiceTranscriptId,
                ToolCatalog = catalog,
                Worker = Worker,
            };
            copy.Attachments.AddRange(Attachments);
            return copy;
        }

        /// <summary>Checks the client-side limits the companion would refuse (bad id, empty intent, attachment sizes).</summary>
        public void Validate()
        {
            if (!ChangeSetIdPattern.IsMatch(ChangeSetId ?? string.Empty))
            {
                throw new EtosException(new EtosError(0, EtosCodes.BadRequest, "changeSetId '" + ChangeSetId + "' is not cs_ plus a 26-character ULID."));
            }

            if (string.IsNullOrWhiteSpace(IntentText))
            {
                throw new EtosException(new EtosError(0, EtosCodes.BadRequest, "intent.text is empty."));
            }

            if (Attachments.Count > MaxAttachments)
            {
                throw new EtosException(new EtosError(0, EtosCodes.BadRequest, "At most " + MaxAttachments.ToString(CultureInfo.InvariantCulture) + " attachments per request."));
            }

            long total = 0;
            foreach (AttachmentBody attachment in Attachments)
            {
                total += attachment.Data.LongLength;
                if (attachment.Data.LongLength > MaxAttachmentBytes || total > MaxAttachmentsTotal)
                {
                    throw new EtosException(new EtosError(413, EtosCodes.TooLarge, "Attachment " + attachment.Name + " does not fit: attachments are at most 16 MiB each and 64 MiB together."));
                }
            }
        }

        public JObject ToJson()
        {
            JObject intent = new JObject { ["text"] = IntentText, ["origin"] = Origin };
            if (VoiceTranscriptId != null)
            {
                intent["voiceTranscriptId"] = VoiceTranscriptId;
            }

            JObject body = new JObject
            {
                ["changeSetId"] = ChangeSetId,
                ["intent"] = intent,
                ["selection"] = Selection.DeepClone(),
                ["contextSlice"] = ContextSlice.DeepClone(),
                ["toolCatalogRevision"] = ToolCatalogRevision,
            };
            if (ToolCatalog != null)
            {
                body["toolCatalog"] = ToolCatalog.DeepClone();
            }

            if (Worker != null)
            {
                body["worker"] = Worker;
            }

            if (Attachments.Count > 0)
            {
                JArray attachments = new JArray();
                foreach (AttachmentBody attachment in Attachments)
                {
                    JObject item = new JObject
                    {
                        ["name"] = attachment.Name,
                        ["mediaType"] = attachment.MediaType,
                        ["data"] = Convert.ToBase64String(attachment.Data),
                        ["sha256"] = attachment.Sha256,
                    };
                    if (attachment.Role != null)
                    {
                        item["role"] = attachment.Role;
                    }

                    attachments.Add(item);
                }

                body["attachments"] = attachments;
            }

            return body;
        }
    }

    /// <summary><c>POST /v1/ops/generate</c> body.</summary>
    public sealed class GenerateBody
    {
        public GenerateBody(string op, JObject? spec = null)
        {
            Op = op;
            Spec = spec ?? new JObject();
        }

        /// <summary><c>image | tts | 3d | describe</c> (the companion's names; it maps them to etos ops).</summary>
        public string Op { get; }

        /// <summary>The op's own input (<c>prompt</c>, <c>size</c>, <c>text</c>, <c>voice</c>; <c>artifact</c> for describe).</summary>
        public JObject Spec { get; }

        /// <summary>Cost ceiling; the client fills the configured default when null, so it is always sent.</summary>
        public double? MaxCostUsd { get; set; }

        public string? ChangeSetId { get; set; }

        /// <summary>Maps an etos op name (<c>generate.image</c>, <c>generate.3d</c>) or a companion name to the companion name.</summary>
        public static string CompanionOp(string op)
        {
            switch (op)
            {
                case "generate.image":
                    return "image";
                case "generate.3d":
                    return "3d";
                default:
                    return op;
            }
        }

        public JObject ToJson(double defaultMaxCostUsd)
        {
            JObject body = new JObject
            {
                ["op"] = CompanionOp(Op),
                ["spec"] = Spec.DeepClone(),
                ["max_cost_usd"] = MaxCostUsd ?? defaultMaxCostUsd,
            };
            if (ChangeSetId != null)
            {
                body["changeSetId"] = ChangeSetId;
            }

            return body;
        }
    }
}
