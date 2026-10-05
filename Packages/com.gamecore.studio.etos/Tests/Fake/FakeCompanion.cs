// GameCore.Studio.Etos.Testing - an in-process stand-in for "etos node proxy + gamecore-studio companion" (04 s2,
// P0.5 s4), for the client's dotnet tests and the gateway's EditMode tests. It is a deterministic double for isolated
// tests only (07 s1): it never stands in for the live node in evidence. It enforces what the client must get right:
// the app key on every HTTP call, single-use path-bound tickets on WebSocket upgrades (no Authorization there), the
// companion routes and shapes, idempotent submits by changeSetId, stale_context for an unknown catalog revision,
// max_cost_usd on every op, the 24 KiB voice frame cap and gapless seq, ordered events with ?after= replay.
// Answers can be seeded from transcripts recorded on the real node (Hello, OpRefusals, CandidateTemplate).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Testing
{
    /// <summary>An artifact the fake serves.</summary>
    public sealed class FakeArtifact
    {
        public FakeArtifact(string name, string mediaType, byte[] bytes, string? role = null)
        {
            Name = name;
            MediaType = mediaType;
            Bytes = bytes;
            Role = role;
            Sha256 = Client.Json.Sha256Hex(bytes);
        }

        public string Name { get; }

        public string MediaType { get; }

        public byte[] Bytes { get; }

        public string? Role { get; }

        public string Sha256 { get; }

        public JObject ToStored(JObject? producer = null)
        {
            JObject stored = new JObject
            {
                ["sha256"] = Sha256,
                ["name"] = Name,
                ["mediaType"] = MediaType,
                ["bytes"] = Bytes.LongLength,
                ["producer"] = producer ?? new JObject { ["etosTask"] = "t_fake" },
                ["url"] = "/v1/artifacts/" + Sha256,
            };
            if (Role != null)
            {
                stored["role"] = Role;
            }

            return stored;
        }
    }

    /// <summary>What the fake worker produces for a request.</summary>
    public sealed class FakeCandidate
    {
        public FakeCandidate(JObject changeSet, IEnumerable<FakeArtifact>? artifacts = null)
        {
            ChangeSet = changeSet;
            Artifacts = artifacts == null ? new List<FakeArtifact>() : new List<FakeArtifact>(artifacts);
        }

        public JObject ChangeSet { get; }

        public List<FakeArtifact> Artifacts { get; }
    }

    /// <summary>One HTTP call the fake received (for assertions; Authorization is recorded as present/absent only).</summary>
    public sealed class FakeCall
    {
        public FakeCall(string method, string path, string query, bool authorized, string? app, string body, string? projectId = null)
        {
            Method = method;
            Path = path;
            Query = query;
            Authorized = authorized;
            App = app;
            Body = body;
            ProjectId = projectId;
        }

        public string Method { get; }

        public string Path { get; }

        public string Query { get; }

        public bool Authorized { get; }

        public string? App { get; }

        public string Body { get; }

        public string? ProjectId { get; }
    }

    /// <summary>The fake node + companion.</summary>
    public sealed class FakeCompanion : IDisposable
    {
        /// <summary>The fixture app key (shaped like a real one so redaction is exercised).</summary>
        public const string AppKey = "etk_fakefixturekey_0123456789abcdef0123456789abcdef0123";

        public const string Agent = "gamecore-studio";

        private readonly object _gate = new object();
        private readonly MiniHttpServer _server;
        private readonly Dictionary<string, string> _tickets = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> _requests = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _requestDigests = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> _bodies = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> _candidates = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly Dictionary<string, FakeArtifact> _artifacts = new Dictionary<string, FakeArtifact>(StringComparer.Ordinal);
        private readonly Dictionary<string, JObject> _stageJobs = new Dictionary<string, JObject>(StringComparer.Ordinal);
        private readonly List<JObject> _events = new List<JObject>();
        private readonly List<FakeCall> _calls = new List<FakeCall>();
        private readonly List<MiniWebSocket> _eventSockets = new List<MiniWebSocket>();
        private readonly Queue<Tuple<string, int, JObject>> _failures = new Queue<Tuple<string, int, JObject>>();
        private TaskCompletionSource<bool> _changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private long _cursor;
        private int _taskCounter;
        private int _ticketCounter;

        public bool StallWebSocketUpgrade { get; set; }
        private int _pendingUpgrades;
        private int _openedEventConnections;
        public int OpenedEventConnections => Volatile.Read(ref _openedEventConnections);
        public int PendingUpgrades => Volatile.Read(ref _pendingUpgrades);
        public JObject? SignedStageVerdict { get; set; }
        public bool VerifyStageVerdict { get; set; } = true;

        public FakeCompanion()
        {
            _server = new MiniHttpServer(Handle);
            HeldCatalogs = new HashSet<string>(StringComparer.Ordinal);
            Hello = new JObject
            {
                ["service"] = Agent,
                ["version"] = "0.1.0",
                ["protocol"] = 1,
                ["app"] = "gamecore-unity",
                ["node"] = "fake",
                ["sdk"] = "1.0.0",
                ["connected"] = true,
                ["capabilities"] = new JArray("requests", "candidates", "artifacts", "index", "ops", "events", "voice", "stage"),
                ["providers"] = new JObject { ["image"] = "live", ["tts"] = "live", ["describe"] = "live", ["3d"] = "not_configured", ["voice"] = "unknown" },
                ["workers"] = new JArray("gc-designer", "gc-mechanic"),
            };
            OpRefusals["3d"] = Tuple.Create(503, new JObject { ["code"] = "not_configured", ["message"] = "3D model generation is not available on this node: no provider is configured", ["hint"] = "add a `3d` provider to ops.toml" });
        }

        /// <summary>The node URL to configure the client with.</summary>
        public string NodeUrl => "http://127.0.0.1:" + _server.Port.ToString(CultureInfo.InvariantCulture);

        /// <summary>The hello body (its toolCatalogRevisions are filled from <see cref="HeldCatalogs"/>).</summary>
        public JObject Hello { get; set; }

        /// <summary>Catalog revisions the companion holds.</summary>
        public HashSet<string> HeldCatalogs { get; }

        /// <summary>The worker: builds the candidate for a request body (null: the task keeps running until cancelled).</summary>
        public Func<JObject, FakeCandidate?>? Worker { get; set; }

        /// <summary>Delay before the worker runs.</summary>
        public TimeSpan WorkerDelay { get; set; } = TimeSpan.FromMilliseconds(50);

        /// <summary>Serve every artifact with one byte flipped (the client must refuse it).</summary>
        public bool TamperArtifacts { get; set; }

        /// <summary>Op refusals by companion op name (<c>3d</c> is refused not_configured by default).</summary>
        public Dictionary<string, Tuple<int, JObject>> OpRefusals { get; } = new Dictionary<string, Tuple<int, JObject>>(StringComparer.Ordinal);

        /// <summary>Bytes the image op returns.</summary>
        public FakeArtifact ImageOutput { get; set; } = new FakeArtifact("image.png", "image/png", FakeMedia.TinyPng());

        /// <summary>Bytes the tts op returns.</summary>
        public FakeArtifact SpeechOutput { get; set; } = new FakeArtifact("speech.wav", "audio/wav", FakeMedia.SilentWav(2400));

        /// <summary>When set, the voice WebSocket refuses with this error then closes.</summary>
        public JObject? VoiceRefusal { get; set; }

        /// <summary>Partial transcript texts sent while audio arrives (one per this many frames), then the final on stop.</summary>
        public List<string> VoiceTranscript { get; } = new List<string> { "move this", "move this NPC", "move this NPC two metres north" };

        public int VoiceFramesPerRevision { get; set; } = 2;

        /// <summary>Voice frames received (decoded byte counts, by seq order).</summary>
        public List<int> VoiceFrameBytes { get; } = new List<int>();

        public List<long> VoiceSeqs { get; } = new List<long>();

        public int VoiceStops { get; private set; }

        public TimeSpan VoiceFinalDelay { get; set; }

        public bool VoiceNeverCloses { get; set; }

        /// <summary>Every HTTP call (newest last).</summary>
        public IReadOnlyList<FakeCall> Calls
        {
            get
            {
                lock (_gate)
                {
                    return _calls.ToList();
                }
            }
        }

        public IReadOnlyList<JObject> Events
        {
            get
            {
                lock (_gate)
                {
                    return _events.Select(e => (JObject)e.DeepClone()).ToList();
                }
            }
        }

        public int TicketsIssued => Volatile.Read(ref _ticketCounter);

        /// <summary>Open event WebSockets.</summary>
        public int EventConnections
        {
            get
            {
                lock (_gate)
                {
                    return _eventSockets.Count(s => !s.IsClosed);
                }
            }
        }

        public void Start() => _server.Start();

        public void Dispose()
        {
            DropEventConnections();
            _server.Dispose();
        }

        /// <summary>Makes the next call whose path starts with <paramref name="pathPrefix"/> fail with this answer.</summary>
        public void FailNext(string pathPrefix, int status, JObject body)
        {
            lock (_gate)
            {
                _failures.Enqueue(Tuple.Create(pathPrefix, status, body));
            }
        }

        /// <summary>Aborts every open event WebSocket (no close frame), as a network break would.</summary>
        public void DropEventConnections()
        {
            List<MiniWebSocket> sockets;
            lock (_gate)
            {
                sockets = _eventSockets.ToList();
                _eventSockets.Clear();
            }

            foreach (MiniWebSocket socket in sockets)
            {
                socket.Abort();
            }
        }

        /// <summary>Appends an event of any type (tests of ordering and resume).</summary>
        public long Emit(string type, string? requestId, JToken data)
        {
            lock (_gate)
            {
                return EmitLocked(type, requestId, data);
            }
        }

        /// <summary>Settles a running request with a candidate (what the worker would do).</summary>
        public void Complete(string requestId, FakeCandidate candidate)
        {
            lock (_gate)
            {
                CompleteLocked(requestId, candidate);
            }
        }

        public JObject? RequestView(string requestId)
        {
            lock (_gate)
            {
                return _requests.TryGetValue(requestId, out JObject? view) ? (JObject)view.DeepClone() : null;
            }
        }

        public int SubmitCount(string requestId)
        {
            lock (_gate)
            {
                return _calls.Count(c => c.Method == "POST" && c.Path.EndsWith("/v1/requests", StringComparison.Ordinal) && c.Body.Contains(requestId));
            }
        }

        // ------------------------------------------------------------------------------------------- dispatch

        private async Task Handle(MiniRequest request, MiniConnection connection)
        {
            bool authorized = request.Header("authorization") == "Bearer " + AppKey;
            lock (_gate)
            {
                _calls.Add(new FakeCall(request.Method, request.Path, string.Join("&", request.Query.Where(q => q.Key != "etos_ticket").Select(q => q.Key + "=" + q.Value)), authorized, request.Header("x-etos-app"), request.BodyText, request.Header("x-gamecore-project")));
            }

            Tuple<int, JObject>? failure = TakeFailure(request.Path);
            if (failure != null)
            {
                await connection.RespondJsonAsync(failure.Item1, failure.Item2.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (request.Path == "/api/v1/tickets" && request.Method == "POST")
            {
                await Ticket(request, connection, authorized).ConfigureAwait(false);
                return;
            }

            string agentPrefix = "/api/v1/agents/";
            if (!request.Path.StartsWith(agentPrefix, StringComparison.Ordinal))
            {
                await Refuse(connection, 404, "not_found", "no such route").ConfigureAwait(false);
                return;
            }

            string rest = request.Path.Substring(agentPrefix.Length);
            int slash = rest.IndexOf('/');
            string agent = slash < 0 ? rest : rest.Substring(0, slash);
            string sub = slash < 0 ? string.Empty : rest.Substring(slash);
            if (request.WantsWebSocket)
            {
                string? ticket = request.Query.TryGetValue("etos_ticket", out string? t) ? t : null;
                if (ticket == null || !RedeemTicket(ticket, request.Path))
                {
                    await Refuse(connection, 401, "unauthorized", "a WebSocket upgrade needs a valid ticket").ConfigureAwait(false);
                    return;
                }
            }
            else if (!authorized)
            {
                await Refuse(connection, 401, "unauthorized", "missing or unknown app key").ConfigureAwait(false);
                return;
            }

            if (agent != Agent)
            {
                await Refuse(connection, 403, "forbidden", "the app `gamecore-unity` does not use the agent `" + agent + "`", "add `uses = [\"" + agent + "\"]` to the app's app.toml and install it again").ConfigureAwait(false);
                return;
            }

            if (!sub.StartsWith("/http/", StringComparison.Ordinal))
            {
                await Refuse(connection, 404, "not_found", "no such route").ConfigureAwait(false);
                return;
            }

            string route = sub.Substring("/http".Length);
            await Companion(route, request, connection).ConfigureAwait(false);
        }

        private async Task Companion(string route, MiniRequest request, MiniConnection connection)
        {
            string method = request.Method;
            if (route == "/v1/hello" && method == "GET")
            {
                JObject hello;
                lock (_gate)
                {
                    hello = (JObject)Hello.DeepClone();
                    hello["toolCatalogRevisions"] = new JArray(HeldCatalogs.OrderBy(x => x, StringComparer.Ordinal));
                }

                await connection.RespondJsonAsync(200, hello.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/requests" && method == "POST")
            {
                await Submit(request, connection).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/requests" && method == "GET")
            {
                long after = request.Query.TryGetValue("after", out string? a) ? long.Parse(a, CultureInfo.InvariantCulture) : 0;
                JArray list = new JArray();
                long next = after;
                lock (_gate)
                {
                    foreach (JObject view in _requests.Values.OrderBy(v => (long)v["seq"]!))
                    {
                        if ((long)view["seq"]! > after)
                        {
                            list.Add(view.DeepClone());
                            next = Math.Max(next, (long)view["seq"]!);
                        }
                    }
                }

                await connection.RespondJsonAsync(200, new JObject { ["requests"] = list, ["next"] = next }.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (route.StartsWith("/v1/requests/", StringComparison.Ordinal))
            {
                string tail = route.Substring("/v1/requests/".Length);
                bool cancel = tail.EndsWith("/cancel", StringComparison.Ordinal) && method == "POST";
                string id = cancel ? tail.Substring(0, tail.Length - "/cancel".Length) : tail;
                JObject? view;
                lock (_gate)
                {
                    if (!_requests.TryGetValue(id, out view))
                    {
                        view = null;
                    }
                    else if (cancel && !RequestStates.IsTerminal((string?)view["state"]))
                    {
                        UpdateLocked(id, v =>
                        {
                            v["state"] = "cancelled";
                            v["taskStatus"] = "cancelled";
                            v["outcome"] = new JObject { ["code"] = "cancelled" };
                        });
                        view = _requests[id];
                    }
                }

                if (view == null)
                {
                    await Refuse(connection, 404, "not_found", "no request " + id).ConfigureAwait(false);
                    return;
                }

                await connection.RespondJsonAsync(200, view.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (route.StartsWith("/v1/candidates/", StringComparison.Ordinal) && method == "GET")
            {
                string id = route.Substring("/v1/candidates/".Length);
                JObject? candidate;
                lock (_gate)
                {
                    candidate = _candidates.TryGetValue(id, out JObject? c) ? c : null;
                }

                if (candidate == null)
                {
                    await Refuse(connection, 404, "not_found", "no candidate for " + id).ConfigureAwait(false);
                    return;
                }

                await connection.RespondJsonAsync(200, candidate.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (route.StartsWith("/v1/artifacts/", StringComparison.Ordinal) && method == "GET")
            {
                string sha = route.Substring("/v1/artifacts/".Length);
                FakeArtifact? artifact;
                lock (_gate)
                {
                    artifact = _artifacts.TryGetValue(sha, out FakeArtifact? found) ? found : null;
                }

                if (artifact == null)
                {
                    await Refuse(connection, 404, "not_found", "no artifact " + sha).ConfigureAwait(false);
                    return;
                }

                byte[] bytes = (byte[])artifact.Bytes.Clone();
                if (TamperArtifacts && bytes.Length > 0)
                {
                    bytes[bytes.Length / 2] ^= 0x5a;
                }

                await connection.RespondAsync(200, artifact.MediaType, bytes, new Dictionary<string, string> { ["x-content-sha256"] = artifact.Sha256 }).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/index/delta" && method == "POST")
            {
                JObject delta = JObject.Parse(request.BodyText);
                JObject ack = new JObject { ["revision"] = delta["revision"] ?? 0, ["queued"] = (delta["nodes"] as JArray)?.Count ?? 0, ["skipped"] = 0 };
                await connection.RespondJsonAsync(200, ack.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/ops/generate" && method == "POST")
            {
                await Generate(request, connection).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/stage" && method == "POST")
            {
                JObject body = JObject.Parse(request.BodyText);
                if (body["packageRef"] != null || body["sourceProject"] != null
                    || (string?)body["projectId"] != request.Header("x-gamecore-project")
                    || body.Properties().Any(p => !new[] { "changeSetId", "projectId", "sourceRevision", "catalogRevision", "action" }.Contains(p.Name)))
                {
                    await Refuse(connection, 400, "bad_request", "stage_context_invalid").ConfigureAwait(false);
                    return;
                }
                if ((string?)body["action"] == "discard")
                {
                    await connection.RespondJsonAsync(200, "{\"discarded\":true}").ConfigureAwait(false);
                    return;
                }
                JObject job = new JObject
                {
                    ["jobId"] = "sj_" + Interlocked.Increment(ref _taskCounter).ToString(CultureInfo.InvariantCulture),
                    ["changeSetId"] = body["changeSetId"],
                    ["projectId"] = body["projectId"],
                    ["app"] = request.Header("x-etos-app"),
                    ["state"] = "queued",
                    ["createdAt"] = Now(),
                    ["updatedAt"] = Now(),
                };
                lock (_gate)
                {
                    _stageJobs[(string)job["jobId"]!] = job;
                }

                await connection.RespondJsonAsync(202, job.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (route.StartsWith("/v1/stage/", StringComparison.Ordinal))
            {
                string[] parts = route.Substring("/v1/stage/".Length).Split('/');
                string id = parts[0];
                JObject? job;
                lock (_gate) job = _stageJobs.TryGetValue(id, out JObject? j) ? j : null;
                if (job == null || (string?)job["projectId"] != request.Header("x-gamecore-project") || (string?)job["app"] != request.Header("x-etos-app"))
                {
                    await Refuse(connection, 404, "not_found", "no stage job").ConfigureAwait(false);
                    return;
                }
                if (parts.Length == 2 && parts[1] == "verdict" && method == "GET")
                {
                    if (SignedStageVerdict == null) await Refuse(connection, 409, "stage_failed", "no issued verdict").ConfigureAwait(false);
                    else await connection.RespondJsonAsync(200, SignedStageVerdict.ToString(Formatting.None)).ConfigureAwait(false);
                }
                else if (parts.Length == 2 && parts[1] == "verify" && method == "POST")
                    await connection.RespondJsonAsync(200, new JObject { ["verified"] = VerifyStageVerdict && JToken.DeepEquals(JObject.Parse(request.BodyText), SignedStageVerdict) }.ToString()).ConfigureAwait(false);
                else await connection.RespondJsonAsync(200, job.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            if (request.WantsWebSocket && StallWebSocketUpgrade)
            {
                Interlocked.Increment(ref _pendingUpgrades);
                try { await connection.WaitForDisconnectAsync().ConfigureAwait(false); }
                finally { Interlocked.Decrement(ref _pendingUpgrades); }
                return;
            }

            if (route == "/v1/events" && request.WantsWebSocket)
            {
                long after = request.Query.TryGetValue("after", out string? a) && long.TryParse(a, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
                MiniWebSocket socket = await connection.UpgradeAsync(request).ConfigureAwait(false);
                await ServeEvents(socket, after).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/voice" && request.WantsWebSocket)
            {
                MiniWebSocket socket = await connection.UpgradeAsync(request).ConfigureAwait(false);
                await ServeVoice(socket).ConfigureAwait(false);
                return;
            }

            if (route == "/v1/events" || route == "/v1/voice")
            {
                await Refuse(connection, 426, "bad_request", "open this route as a WebSocket").ConfigureAwait(false);
                return;
            }

            await Refuse(connection, 404, "not_found", "no such companion route").ConfigureAwait(false);
        }

        // ------------------------------------------------------------------------------------------- routes

        private async Task Ticket(MiniRequest request, MiniConnection connection, bool authorized)
        {
            if (!authorized)
            {
                await Refuse(connection, 401, "unauthorized", "missing or unknown app key").ConfigureAwait(false);
                return;
            }

            JObject body = JObject.Parse(request.BodyText);
            string path = (string?)body["path"] ?? string.Empty;
            if (!path.StartsWith("/api/v1/agents/" + Agent + "/http/", StringComparison.Ordinal) || path.Contains("?"))
            {
                await Refuse(connection, 403, "not_allowed", "no ticket for " + path + ": it is not a proxied path").ConfigureAwait(false);
                return;
            }

            int n = Interlocked.Increment(ref _ticketCounter);
            string ticket = "ett_" + n.ToString("D4", CultureInfo.InvariantCulture) + "fakeTicket" + Guid.NewGuid().ToString("N");
            lock (_gate)
            {
                _tickets[ticket] = path;
            }

            await connection.RespondJsonAsync(200, new JObject { ["ticket"] = ticket, ["expires_in"] = 30 }.ToString(Formatting.None)).ConfigureAwait(false);
        }

        private bool RedeemTicket(string ticket, string path)
        {
            lock (_gate)
            {
                if (_tickets.TryGetValue(ticket, out string? bound) && bound == path)
                {
                    _tickets.Remove(ticket);
                    return true;
                }

                return false;
            }
        }

        private async Task Submit(MiniRequest request, MiniConnection connection)
        {
            JObject body;
            try
            {
                body = JObject.Parse(request.BodyText);
            }
            catch (JsonException)
            {
                await Refuse(connection, 400, "bad_request", "the body is not JSON").ConfigureAwait(false);
                return;
            }

            string? nullAt = Client.Json.FirstNull(body);
            if (nullAt != null)
            {
                await Refuse(connection, 400, "bad_request", "null at " + nullAt, "optional members are omitted when absent, never sent as null").ConfigureAwait(false);
                return;
            }

            string id = (string?)body["changeSetId"] ?? string.Empty;
            if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^cs_[0-7][0-9A-HJKMNP-TV-Z]{25}$"))
            {
                await Refuse(connection, 400, "bad_request", "changeSetId is not `cs_` plus a 26-character ULID").ConfigureAwait(false);
                return;
            }

            string revision = Client.Json.NormalizeSha256((string?)body["toolCatalogRevision"]) ?? (string?)body["toolCatalogRevision"] ?? string.Empty;
            JObject? view = null;
            bool stale = false;
            bool conflict = false;
            lock (_gate)
            {
                if (body["toolCatalog"] is JObject)
                {
                    HeldCatalogs.Add(revision);
                }

                if (!HeldCatalogs.Contains(revision))
                {
                    stale = true;
                }
                else
                {
                    JObject digestBasis = (JObject)body.DeepClone();
                    digestBasis.Remove("toolCatalog");
                    string digest = Client.Json.Sha256Hex(Encoding.UTF8.GetBytes(digestBasis.ToString(Formatting.None)));
                    if (_requests.TryGetValue(id, out JObject? existing))
                    {
                        conflict = _requestDigests[id] != digest;
                        view = existing;
                    }
                    else
                    {
                        view = CreateLocked(id, body, digest);
                    }
                }
            }

            if (stale)
            {
                await Refuse(connection, 409, "stale_context", "the companion holds no tool catalog revision " + revision, "send the request again with `toolCatalog` for this revision").ConfigureAwait(false);
                return;
            }

            if (conflict || view == null)
            {
                await Refuse(connection, 409, "ledger_conflict", "a different request is recorded under " + id).ConfigureAwait(false);
                return;
            }

            JObject answer;
            lock (_gate)
            {
                answer = new JObject
                {
                    ["requestId"] = view["requestId"]!.DeepClone(),
                    ["changeSetId"] = view["changeSetId"]!.DeepClone(),
                    ["taskId"] = view["taskId"]?.DeepClone(),
                    ["state"] = view["state"]!.DeepClone(),
                    ["taskStatus"] = view["taskStatus"]?.DeepClone(),
                    ["request"] = view.DeepClone(),
                };
            }

            await connection.RespondJsonAsync(200, answer.ToString(Formatting.None)).ConfigureAwait(false);
        }

        private JObject CreateLocked(string id, JObject body, string digest)
        {
            string task = "t" + Interlocked.Increment(ref _taskCounter).ToString("x8", CultureInfo.InvariantCulture) + "fake";
            JObject view = new JObject
            {
                ["requestId"] = id,
                ["changeSetId"] = id,
                ["worker"] = (string?)body["worker"] ?? "gc-designer",
                ["state"] = "requested",
                ["attempt"] = 0,
                ["tasks"] = new JArray(),
                ["hasCandidate"] = false,
                ["seq"] = 0,
                ["createdAt"] = Now(),
                ["updatedAt"] = Now(),
            };
            _requests[id] = view;
            _requestDigests[id] = digest;
            _bodies[id] = (JObject)body.DeepClone();
            UpdateLocked(id, v => { });
            UpdateLocked(id, v =>
            {
                v["state"] = "running";
                v["taskId"] = task;
                v["taskStatus"] = "queued";
                v["topic"] = "#agent/gamecore-studio/cs-" + id.Substring(3).ToLowerInvariant();
                v["tasks"] = new JArray(task);
            });
            EmitLocked("task_progress", id, new JObject { ["taskId"] = task, ["attempt"] = 0, ["status"] = "working", ["text"] = "starting" });
            ScheduleWorker(id, (JObject)body.DeepClone());
            return _requests[id];
        }

        private void ScheduleWorker(string id, JObject body)
        {
            Func<JObject, FakeCandidate?>? worker = Worker;
            if (worker == null)
            {
                return;
            }

            TimeSpan delay = WorkerDelay;
            Task.Run(async () =>
            {
                await Task.Delay(delay).ConfigureAwait(false);
                FakeCandidate? candidate = worker(body);
                if (candidate == null)
                {
                    return;
                }

                lock (_gate)
                {
                    CompleteLocked(id, candidate);
                }
            });
        }

        private void CompleteLocked(string id, FakeCandidate candidate)
        {
            if (!_requests.TryGetValue(id, out JObject? view) || RequestStates.IsTerminal((string?)view["state"]))
            {
                return;
            }

            string task = (string?)view["taskId"] ?? "t_fake";
            JArray artifacts = new JArray();
            foreach (FakeArtifact artifact in candidate.Artifacts)
            {
                _artifacts[artifact.Sha256] = artifact;
                artifacts.Add(artifact.ToStored(new JObject { ["etosTask"] = task }));
            }

            JObject body = _bodies.TryGetValue(id, out JObject? submitted) ? submitted : new JObject();
            _candidates[id] = new JObject
            {
                ["changeSetId"] = id,
                ["taskId"] = task,
                ["attempt"] = 0,
                ["changeSet"] = candidate.ChangeSet.DeepClone(),
                ["artifacts"] = artifacts,
                ["toolCatalogRevision"] = Client.Json.NormalizeSha256((string?)body["toolCatalogRevision"]) ?? string.Empty,
                ["receivedAt"] = Now(),
            };
            EmitLocked("task_progress", id, new JObject { ["taskId"] = task, ["attempt"] = 0, ["status"] = "done", ["text"] = "wrote /outputs/changeset.json" });
            UpdateLocked(id, v =>
            {
                v["state"] = "candidate";
                v["taskStatus"] = "done";
                v["hasCandidate"] = true;
                v["outcome"] = new JObject { ["code"] = "candidate" };
            });
            EmitLocked("candidate", id, new JObject
            {
                ["changeSetId"] = id,
                ["taskId"] = task,
                ["attempt"] = 0,
                ["operations"] = (candidate.ChangeSet["operations"] as JArray)?.Count ?? 0,
                ["artifacts"] = artifacts.DeepClone(),
                ["url"] = "/v1/candidates/" + id,
            });
        }

        private async Task Generate(MiniRequest request, MiniConnection connection)
        {
            JObject body = JObject.Parse(request.BodyText);
            string op = (string?)body["op"] ?? string.Empty;
            if (body["max_cost_usd"] == null || body["max_cost_usd"]!.Type == JTokenType.Null)
            {
                await Refuse(connection, 400, "bad_request", "max_cost_usd is required", "send a cost ceiling in USD, or configure ops_max_cost_usd").ConfigureAwait(false);
                return;
            }

            if (OpRefusals.TryGetValue(op, out Tuple<int, JObject>? refusal))
            {
                await connection.RespondJsonAsync(refusal.Item1, refusal.Item2.ToString(Formatting.None)).ConfigureAwait(false);
                return;
            }

            double max = (double)body["max_cost_usd"]!;
            JObject answer = new JObject { ["op"] = op, ["max_cost_usd"] = max, ["key"] = "gc-fake" + op };
            switch (op)
            {
                case "image":
                case "tts":
                    FakeArtifact output = op == "image" ? ImageOutput : SpeechOutput;
                    lock (_gate)
                    {
                        _artifacts[output.Sha256] = output;
                    }

                    answer["etosOp"] = op == "image" ? "generate.image" : "tts";
                    answer["provider"] = op == "image" ? "echo-images" : "bailian-tts";
                    answer["state"] = "succeeded";
                    answer["artifacts"] = new JArray(output.ToStored(new JObject { ["op"] = answer["etosOp"], ["provider"] = answer["provider"] }));
                    break;
                case "describe":
                    if (body["spec"]?["artifact"] == null && body["spec"]?["input"] == null)
                    {
                        await Refuse(connection, 400, "bad_request", "describe needs `artifact` (a stored digest) or `input` (an etos reference)").ConfigureAwait(false);
                        return;
                    }

                    answer["etosOp"] = "describe";
                    answer["provider"] = "echo-describe";
                    answer["state"] = "succeeded";
                    answer["text"] = "A small wooden well with a stone rim.";
                    answer["artifacts"] = new JArray();
                    break;
                default:
                    await Refuse(connection, 400, "bad_request", "op \"" + op + "\" is not one of image, tts, 3d, describe").ConfigureAwait(false);
                    return;
            }

            lock (_gate)
            {
                EmitLocked("asset", (string?)body["changeSetId"], answer.DeepClone());
            }

            await connection.RespondJsonAsync(200, answer.ToString(Formatting.None)).ConfigureAwait(false);
        }

        private async Task ServeEvents(MiniWebSocket socket, long after)
        {
            lock (_gate)
            {
                _eventSockets.Add(socket);
                Interlocked.Increment(ref _openedEventConnections);
            }

            Task<string?> receive = socket.ReceiveTextAsync();
            long cursor = after;
            try
            {
                while (!socket.IsClosed)
                {
                    List<JObject> pending;
                    Task changed;
                    lock (_gate)
                    {
                        pending = _events.Where(e => (long)e["cursor"]! > cursor).Select(e => (JObject)e.DeepClone()).ToList();
                        changed = _changed.Task;
                    }

                    foreach (JObject frame in pending)
                    {
                        await socket.SendTextAsync(frame.ToString(Formatting.None)).ConfigureAwait(false);
                        cursor = (long)frame["cursor"]!;
                    }

                    Task done = await Task.WhenAny(receive, changed, Task.Delay(500)).ConfigureAwait(false);
                    if (done == receive)
                    {
                        string? text = await receive.ConfigureAwait(false);
                        if (text == null)
                        {
                            break;
                        }

                        receive = socket.ReceiveTextAsync();
                    }
                }
            }
            catch (System.IO.IOException)
            {
            }
            finally
            {
                lock (_gate)
                {
                    _eventSockets.Remove(socket);
                }

                socket.Abort();
            }

            return;
        }

        private async Task ServeVoice(MiniWebSocket socket)
        {
            if (VoiceRefusal != null)
            {
                await socket.SendTextAsync(new JObject { ["type"] = "error", ["code"] = VoiceRefusal["code"], ["message"] = VoiceRefusal["message"] }.ToString(Formatting.None)).ConfigureAwait(false);
                await socket.SendTextAsync("{\"type\":\"closed\",\"reason\":\"refused\"}").ConfigureAwait(false);
                await socket.CloseAsync().ConfigureAwait(false);
                return;
            }

            string session = "vs_fake" + Guid.NewGuid().ToString("N").Substring(0, 8);
            await socket.SendTextAsync(new JObject { ["type"] = "ready", ["sessionId"] = session, ["audioFormat"] = "pcm16", ["sampleRateHz"] = 24000, ["maxChunkBytes"] = 24576 }.ToString(Formatting.None)).ConfigureAwait(false);
            int frames = 0;
            int revision = 0;
            long? last = null;
            string item = "item_fake_1";
            try
            {
                while (true)
                {
                    string? text = await socket.ReceiveTextAsync().ConfigureAwait(false);
                    if (text == null)
                    {
                        return;
                    }

                    JObject message = JObject.Parse(text);
                    string type = (string?)message["type"] ?? string.Empty;
                    if (type == "audio")
                    {
                        byte[] bytes = Convert.FromBase64String((string?)message["pcm16"] ?? string.Empty);
                        long? seq = message["seq"]?.Type == JTokenType.Integer ? (long)message["seq"]! : (long?)null;
                        if (bytes.Length > 24576)
                        {
                            await socket.SendTextAsync("{\"type\":\"error\",\"code\":\"too_large\",\"message\":\"an audio frame is at most 24576 bytes of PCM16\"}").ConfigureAwait(false);
                            continue;
                        }

                        if (last != null && seq != null && seq != last + 1)
                        {
                            await socket.SendTextAsync("{\"type\":\"error\",\"code\":\"audio_gap\",\"message\":\"client chunk " + seq + " follows " + last + "\"}").ConfigureAwait(false);
                        }

                        if (seq != null)
                        {
                            last = seq;
                        }

                        lock (_gate)
                        {
                            VoiceFrameBytes.Add(bytes.Length);
                            if (seq != null)
                            {
                                VoiceSeqs.Add(seq.Value);
                            }
                        }

                        frames++;
                        if (frames == 1)
                        {
                            await socket.SendTextAsync("{\"type\":\"speech_started\",\"itemId\":\"" + item + "\"}").ConfigureAwait(false);
                        }

                        if (frames % VoiceFramesPerRevision == 0 && revision < VoiceTranscript.Count - 1)
                        {
                            await socket.SendTextAsync(new JObject { ["type"] = "transcript", ["role"] = "user", ["itemId"] = item, ["revision"] = revision, ["text"] = VoiceTranscript[revision] }.ToString(Formatting.None)).ConfigureAwait(false);
                            revision++;
                        }
                    }
                    else if (type == "stop")
                    {
                        VoiceStops++;
                        if (VoiceNeverCloses) continue;
                        await Task.Delay(VoiceFinalDelay).ConfigureAwait(false);
                        await socket.SendTextAsync("{\"type\":\"speech_ended\",\"itemId\":\"" + item + "\"}").ConfigureAwait(false);
                        await socket.SendTextAsync(new JObject { ["type"] = "transcript", ["role"] = "assistant", ["itemId"] = "item_assistant", ["revision"] = 0, ["text"] = "dropped by the client", ["final"] = true }.ToString(Formatting.None)).ConfigureAwait(false);
                        await socket.SendTextAsync(new JObject { ["type"] = "transcript", ["role"] = "user", ["itemId"] = item, ["revision"] = revision, ["text"] = VoiceTranscript[VoiceTranscript.Count - 1], ["final"] = true }.ToString(Formatting.None)).ConfigureAwait(false);
                        await socket.SendTextAsync("{\"type\":\"closed\",\"reason\":\"stopped\"}").ConfigureAwait(false);
                        await socket.CloseAsync().ConfigureAwait(false);
                        return;
                    }
                    else
                    {
                        await socket.SendTextAsync("{\"type\":\"error\",\"code\":\"bad_message\",\"message\":\"expected audio or stop\"}").ConfigureAwait(false);
                    }
                }
            }
            catch (System.IO.IOException)
            {
            }
        }

        // ------------------------------------------------------------------------------------------- helpers

        private Tuple<int, JObject>? TakeFailure(string path)
        {
            lock (_gate)
            {
                if (_failures.Count > 0 && path.Contains(_failures.Peek().Item1))
                {
                    Tuple<string, int, JObject> failure = _failures.Dequeue();
                    return Tuple.Create(failure.Item2, failure.Item3);
                }

                return null;
            }
        }

        private void UpdateLocked(string id, Action<JObject> change)
        {
            JObject view = _requests[id];
            change(view);
            long cursor = ++_cursor;
            view["seq"] = cursor;
            view["updatedAt"] = Now();
            AppendLocked(cursor, "request", id, view.DeepClone());
        }

        private long EmitLocked(string type, string? requestId, JToken data)
        {
            long cursor = ++_cursor;
            AppendLocked(cursor, type, requestId, data);
            return cursor;
        }

        private void AppendLocked(long cursor, string type, string? requestId, JToken data)
        {
            JObject frame = new JObject { ["cursor"] = cursor, ["at"] = Now(), ["type"] = type };
            if (requestId != null)
            {
                frame["requestId"] = requestId;
            }

            frame["data"] = data;
            _events.Add(frame);
            TaskCompletionSource<bool> changed = _changed;
            _changed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            changed.TrySetResult(true);
        }

        private static Task Refuse(MiniConnection connection, int status, string code, string message, string? hint = null)
        {
            JObject body = new JObject { ["code"] = code, ["message"] = message };
            if (hint != null)
            {
                body["hint"] = hint;
            }

            return connection.RespondJsonAsync(status, body.ToString(Formatting.None));
        }

        private static long Now() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    /// <summary>Small valid media files for the fake's ops.</summary>
    public static class FakeMedia
    {
        /// <summary>A 1x1 RGBA PNG.</summary>
        public static byte[] TinyPng()
        {
            return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        }

        /// <summary>A PCM16 mono 24 kHz WAV of <paramref name="samples"/> samples of silence.</summary>
        public static byte[] SilentWav(int samples)
        {
            return Wav(new byte[samples * 2], 24000, 1);
        }

        /// <summary>A PCM16 WAV around <paramref name="pcm"/>.</summary>
        public static byte[] Wav(byte[] pcm, int rate, int channels)
        {
            using (System.IO.MemoryStream stream = new System.IO.MemoryStream())
            using (System.IO.BinaryWriter writer = new System.IO.BinaryWriter(stream))
            {
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + pcm.Length);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)channels);
                writer.Write(rate);
                writer.Write(rate * channels * 2);
                writer.Write((short)(channels * 2));
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(pcm.Length);
                writer.Write(pcm);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>A sine tone as PCM16 mono at <paramref name="rate"/>.</summary>
        public static byte[] Tone(double seconds, int rate, double hz)
        {
            int count = (int)(seconds * rate);
            float[] samples = new float[count];
            for (int i = 0; i < count; i++)
            {
                samples[i] = (float)(0.3 * Math.Sin(2 * Math.PI * hz * i / rate));
            }

            return VoiceFraming.ToPcm16(samples, 0, count);
        }
    }
}
