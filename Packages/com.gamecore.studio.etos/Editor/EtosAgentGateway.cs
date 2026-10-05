// GameCore.Studio.Etos - the Studio's agent gateway over the gamecore-studio companion (boundary D; 02 s4, s5; 04 s2,
// s3, s5). It implements the tool-facing GameCore.Studio.Authoring.IAgentGateway (asset.generate, mechanism.propose)
// registered in StudioServiceRegistry.AgentGateway, and the UI-facing GameCore.Studio.Authoring.Agent.IAgentGateway
// (submit, requests, candidates, media ops, voice). Network work runs on thread-pool threads; every Studio state change
// and every event is raised on the main thread through the MainThreadQueue.
//
// Candidate import (02 s4 step 5, 03 s6/s9): fetch the candidate -> refuse a document with null anywhere and read it
// with the strict model reader -> download every declared artifact and verify its sha256 and size BEFORE anything is
// retained -> ArtifactStore.Put (which re-verifies) -> ChangeSetEngine.Stage(Candidate mode, the catalog revision the
// candidate was built against), which journals it as Candidate when valid. Assets are written only by the engine's
// tools when the user applies. Nothing is ever retried or fabricated: refusals keep their etos code.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using AgentGateway = GameCore.Studio.Authoring.Agent.IAgentGateway;
using ToolGateway = GameCore.Studio.Authoring.IAgentGateway;

namespace GameCore.Studio.Etos
{
    /// <summary>Gateway behaviour.</summary>
    public sealed class EtosGatewayOptions
    {
        /// <summary>Cost ceiling of media ops that do not name one (USD).</summary>
        public double MaxCostUsd { get; set; } = EtosSettings.DefaultMaxCostUsd;

        public string DesignWorker { get; set; } = "gc-designer";

        public string MechanismWorker { get; set; } = "gc-mechanic";

        /// <summary>Import and stage a candidate as soon as it is announced.</summary>
        public bool AutoImport { get; set; } = true;

        /// <summary>Provider status poll interval (04 s2: polled on open; here also every 30 s and on focus).</summary>
        public TimeSpan StatusInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>Where asset.generate imports media when no path is given.</summary>
        public string GeneratedFolder { get; set; } = "Assets/Generated/Studio";

        /// <summary>Reconnect schedule of the event stream.</summary>
        public BackoffPolicy? Backoff { get; set; }

        /// <summary>
        /// Request ids this project submitted (persisted by the session across reloads). Only these are imported
        /// automatically: the companion's ledger is shared by every client of the app, and a recovered or replayed
        /// candidate of someone else's request is listed, never staged on its own.
        /// </summary>
        public ISet<string>? OwnRequests { get; set; }

        /// <summary>
        /// How long a long media op (image, tts, 3d) is re-issued after a companion <c>transport</c> timeout. The body
        /// is identical, so the companion derives the same etos effect key and etops looks the job up instead of
        /// submitting again (crates/etops/src/service.rs: "Starting again with the same key and arguments never
        /// repeats the request"). Zero disables the re-issue.
        /// </summary>
        public TimeSpan OpReplayWindow { get; set; } = TimeSpan.FromMinutes(6);

        /// <summary>Pause between re-issues of a long op.</summary>
        public TimeSpan OpReplayDelay { get; set; } = TimeSpan.FromSeconds(5);
    }

    /// <summary>The result of a candidate import.</summary>
    public sealed class CandidateImport
    {
        public CandidateImport(string requestId, StagedChangeSet? staged, IReadOnlyList<Diagnostic> diagnostics, double milliseconds)
        {
            RequestId = requestId;
            Staged = staged;
            Diagnostics = diagnostics;
            Milliseconds = milliseconds;
        }

        public string RequestId { get; }

        /// <summary>The staged change set (null when the import failed before staging).</summary>
        public StagedChangeSet? Staged { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public double Milliseconds { get; }

        public bool Ok => Staged != null && Staged.Ok;
    }

    /// <summary>The etos gateway.</summary>
    public sealed class EtosAgentGateway : ToolGateway, AgentGateway, ICandidateStageGateway, IDisposable
    {
        private readonly MainThreadQueue _queue;
        private readonly EtosGatewayOptions _options;
        private readonly IStudioLog _log;
        private readonly Dictionary<string, Tracked> _requests = new Dictionary<string, Tracked>(StringComparer.Ordinal);
        private readonly List<string> _order = new List<string>();
        private readonly Dictionary<string, StagedChangeSet> _staged = new Dictionary<string, StagedChangeSet>(StringComparer.Ordinal);
        private readonly Stopwatch _sinceStatus = new Stopwatch();
        private readonly Stopwatch _sinceRecovery = new Stopwatch();
        private int _recoveryInFlight;
        private readonly ISet<string> _own;
        private ProviderStatus _status = ProviderStatus.Unknown;
        private HelloInfo? _hello;
        private int _statusInFlight;
        private bool _disposed;

        public EtosAgentGateway(CompanionClient client, StudioRuntime runtime, MainThreadQueue queue, ICursorStore cursors, EtosGatewayOptions? options = null, IStudioLog? log = null)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
            Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _options = options ?? new EtosGatewayOptions();
            _log = new RedactingStudioLog(log ?? runtime.Log);
            _own = _options.OwnRequests ?? new HashSet<string>(StringComparer.Ordinal);
            Events = new EventStream(client, cursors ?? throw new ArgumentNullException(nameof(cursors)), _options.Backoff);
            Events.MaxPendingEvents = 256;
            Events.HandleAsync = (frame, token) => _queue.Run(() => OnEvent(frame), token);
            Events.StateChanged += (state, error) => _queue.Post(() => OnStreamState(state, error));
        }

        public async Task<JObject> StageCandidateAsync(StageCandidateRequest request, CancellationToken cancellationToken)
        {
            var options = StageAdmission.Of(Runtime).Options;
            if (request.ProjectId != options.ProjectId || request.SourceRevision != options.SourceRevision?.Invoke()
                || request.CatalogRevision != options.CatalogRevision?.Invoke())
                throw new EtosException(new EtosError(0, EtosCodes.StaleContext, "stage_context_changed"));
            StageJobInfo job = await Client.StageAsync(request.ChangeSetId, request.ProjectId,
                request.SourceRevision, request.CatalogRevision, cancellationToken).ConfigureAwait(false);
            return job.Raw;
        }

        public async Task<JObject> FetchTrustedVerdictAsync(string jobId, CancellationToken cancellationToken)
        {
            JObject record = await Client.FetchTrustedVerdictAsync(jobId, cancellationToken).ConfigureAwait(false);
            if (!await Client.VerifyVerdictAsync(jobId, record, cancellationToken).ConfigureAwait(false)
                || (string?)record["jobId"] != jobId || (string?)record["projectId"] != Client.Options.ProjectId)
                throw EtosException.Protocol("stage_verdict_untrusted");
            return record;
        }

        public CompanionClient Client { get; }

        public StudioRuntime Runtime { get; }

        public EventStream Events { get; }

        public EtosGatewayOptions Options => _options;

        /// <summary>The main-thread queue events and imports are marshalled through.</summary>
        public MainThreadQueue Queue => _queue;

        /// <summary>The redacting log of the gateway.</summary>
        public IStudioLog Log => _log;

        /// <summary>The last hello (null before the first answer).</summary>
        public HelloInfo? Hello => _hello;

        /// <summary>Exact worker ids advertised by the authenticated companion hello.</summary>
        public IReadOnlyList<string> Workers => _hello?.Workers ?? Array.Empty<string>();

        // ------------------------------------------------------------------------------- tool-facing gateway

        bool ToolGateway.IsConfigured => !_disposed;

        /// <summary>
        /// <c>asset.generate</c>: refused at once when hello reports the capability not_configured/blocked; otherwise the
        /// op runs in the background and its verified artifact arrives as a candidate change set (asset.import) staged for
        /// review, parented to the requesting change set.
        /// </summary>
        public ServiceResult GenerateAsset(AgentAssetRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            string? op = OpForKind(request.Kind);
            if (op == null)
            {
                return new ServiceResult(ServiceRequestStatus.Refused, diagnostic: new Diagnostic(DiagnosticCodes.InvalidArgs, "Cannot generate a '" + request.Kind + "': the companion offers image, voice (tts) and 3d."));
            }

            string capability = CapabilityOf(op);
            ProviderState state = _status.For(capability);
            if (state == ProviderState.NotConfigured)
            {
                return new ServiceResult(ServiceRequestStatus.NotConfigured, diagnostic: new Diagnostic(EtosCodes.NotConfigured, "The node has no " + capability + " provider (hello reports not_configured).", capability == "3d" ? "3D generation needs a predictions provider credential (SADR-020)." : "Configure it in the node's ops.toml."));
            }

            if (state == ProviderState.Blocked)
            {
                return new ServiceResult(ServiceRequestStatus.Blocked, diagnostic: new Diagnostic(DiagnosticCodes.Blocked, "The " + capability + " provider is blocked for the companion (grant or budget)."));
            }

            JObject inputs = InputsFor(op, request.Prompt, request.Options);
            OpRequest opRequest = new OpRequest(op, inputs, Json.Double(request.Options, "max_cost_usd")) { ChangeSetId = request.ChangeSetId };
            string extension = op == "tts" ? ".wav" : op == "generate.3d" ? ".glb" : ".png";
            string path = _options.GeneratedFolder.TrimEnd('/') + "/" + Slug(request.Prompt) + "_" + request.OpId + extension;
            Task.Run(async () =>
            {
                OpResult result = await GenerateAsync(opRequest, CancellationToken.None).ConfigureAwait(false);
                _queue.Post(() => OnGenerated(request, result, path));
            });
            return new ServiceResult(ServiceRequestStatus.Accepted, new JObject { ["op"] = op, ["kind"] = request.Kind, ["path"] = path, ["followUp"] = "a candidate change set importing the generated asset" });
        }

        /// <summary><c>mechanism.propose</c>: opens a gc-mechanic request; the package arrives through a candidate and the staging lane.</summary>
        public ServiceResult ProposeMechanism(AgentMechanismRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            SelectionSnapshot selection = new SelectionSnapshot(IdDerivation.NewSelectionId(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CryptoIdEntropy.Instance), SelectionMode.Edit, request.Context.ToList(), Runtime.Index.Revision);
            AgentRequest agent = AgentRequestBuilder.Build(Runtime, selection, request.Description, null, "mechanism");
            agent.Parent = request.ChangeSetId;
            string id = IdDerivation.NewChangeSetId();
            agent.ChangeSetId = id;
            Task<string> submit = SubmitAsync(agent, CancellationToken.None);
            submit.ContinueWith(t => _queue.Post(() => _log.Write(StudioLogLevel.Warning, "etos", "mechanism request " + id + " failed: " + t.Exception?.GetBaseException().Message)), TaskContinuationOptions.OnlyOnFaulted);
            return new ServiceResult(ServiceRequestStatus.Accepted, new JObject { ["requestId"] = id, ["worker"] = _options.MechanismWorker });
        }

        // ------------------------------------------------------------------------------- UI-facing gateway

        public ProviderStatus Status => _status;

        public event Action<ProviderStatus>? StatusChanged;

        public event Action<RequestView>? RequestChanged;

        public event Action<CandidateNotice>? CandidateReady;

        /// <summary>Raised on the main thread after a candidate was imported and staged (or refused at staging).</summary>
        public event Action<CandidateImport>? CandidateStaged;

        public IReadOnlyList<RequestView> Requests => _order.Select(id => _requests[id].View()).ToList();

        /// <summary>True for a request this project submitted.</summary>
        public bool IsOwn(string requestId) => _own.Contains(requestId);

        /// <summary>Staged candidates by request id (the UI applies or rejects them).</summary>
        public IReadOnlyDictionary<string, StagedChangeSet> Staged => _staged;

        /// <summary>Starts the event stream, the first status check and the recovery of this app's requests.</summary>
        public void Start()
        {
            Events.Start();
            _ = RefreshStatusAsync();
            _ = RecoverAsync();
        }

        /// <summary>Main-thread tick: polls the provider status every <see cref="EtosGatewayOptions.StatusInterval"/>.</summary>
        public void Tick()
        {
            if (Events.State != EventStreamState.Connected && (!_sinceRecovery.IsRunning || _sinceRecovery.Elapsed >= TimeSpan.FromSeconds(1)))
                _ = RecoverAsync();
            if (!_sinceStatus.IsRunning || _sinceStatus.Elapsed >= _options.StatusInterval)
            {
                _ = RefreshStatusAsync();
            }
        }

        /// <summary><c>GET /v1/hello</c> → <see cref="Status"/> (raised on the main thread).</summary>
        public async Task<ProviderStatus> RefreshStatusAsync()
        {
            if (Interlocked.Exchange(ref _statusInFlight, 1) == 1)
            {
                return _status;
            }

            _sinceStatus.Restart();
            ProviderStatus status;
            HelloInfo? hello = null;
            try
            {
                hello = await Client.HelloAsync().ConfigureAwait(false);
                status = new ProviderStatus(
                    StateOf(hello.Provider("image")),
                    StateOf(hello.Provider("tts")),
                    StateOf(hello.Provider("voice")),
                    StateOf(hello.Provider("3d")),
                    StateOf(hello.Provider("describe")),
                    true,
                    hello.Connected,
                    hello.Version,
                    hello.Connected ? null : new Diagnostic(EtosCodes.AgentStarting, "The companion is not connected to the node."),
                    DateTime.UtcNow);
            }
            catch (EtosException error)
            {
                status = new ProviderStatus(ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, ProviderState.Unknown, error.Error.Status != 0, false, null, DiagnosticOf(error.Error), DateTime.UtcNow);
            }
            finally
            {
                Interlocked.Exchange(ref _statusInFlight, 0);
            }

            _queue.Post(() =>
            {
                if (hello != null)
                {
                    _hello = hello;
                }

                bool changed = _status.ToString() != status.ToString();
                _status = status;
                if (changed)
                {
                    _log.Write(status.Problem == null ? StudioLogLevel.Info : StudioLogLevel.Warning, "etos", "provider status: " + status, status.Problem);
                }

                StatusChanged?.Invoke(status);
            });
            return status;
        }

        /// <summary>Recovers this app's requests from the companion's ledger (after a domain reload or restart).</summary>
        public async Task RecoverAsync()
        {
            if (Interlocked.Exchange(ref _recoveryInFlight, 1) == 1) return;
            _sinceRecovery.Restart();
            try
            {
                long after = 0;
                while (true)
                {
                    RequestPage page = await Client.ListRequestsAsync(after, 200).ConfigureAwait(false);
                    if (page.Requests.Count == 0)
                    {
                        break;
                    }

                    foreach (RequestInfo info in page.Requests)
                    {
                        _queue.Post(() => Upsert(info, recovered: true));
                    }

                    if (page.Next <= after)
                    {
                        break;
                    }

                    after = page.Next;
                }
            }
            catch (EtosException error)
            {
                _queue.Post(() => _log.Write(StudioLogLevel.Warning, "etos", "request recovery failed", DiagnosticOf(error.Error)));
            }

            finally { Interlocked.Exchange(ref _recoveryInFlight, 0); }

            return;
        }

        public async Task<string> SubmitAsync(AgentRequest req, CancellationToken ct)
        {
            if (req == null)
            {
                throw new ArgumentNullException(nameof(req));
            }

            string id = req.ChangeSetId ?? IdDerivation.NewChangeSetId();
            ToolCatalog catalog = Runtime.Registry.Catalog;
            string current = catalog.Revision ?? catalog.ComputeRevision();
            if (!string.Equals(Json.NormalizeSha256(current), Json.NormalizeSha256(req.ToolCatalogRevision), StringComparison.Ordinal))
            {
                throw new EtosException(new EtosError(0, EtosCodes.StaleContext, "The tool catalog changed since the request was built (" + req.ToolCatalogRevision + " -> " + current + "); rebuild the request."));
            }

            bool held = _hello != null && _hello.HoldsCatalog(current);
            EditRequestBody body = AgentRequestBuilder.ToBody(req, id, _options.DesignWorker, _options.MechanismWorker, held ? null : catalog);
            JObject catalogJson = (JObject)StudioJson.ToToken(catalog);
            _queue.Post(() => _own.Add(id));
            Track(id, req.Intent, req.Parent, current);
            SubmitResult result;
            try
            {
                result = await Client.SubmitAsync(body, ct).ConfigureAwait(false);
            }
            catch (EtosException error) when (error.Code == EtosCodes.StaleContext && body.ToolCatalog == null)
            {
                result = await Client.SubmitAsync(body.WithCatalog(catalogJson), ct).ConfigureAwait(false);
            }
            catch (EtosException error)
            {
                _queue.Post(() => Fail(id, DiagnosticOf(error.Error), "submit_failed"));
                throw;
            }

            _queue.Post(() => Upsert(result.Request, recovered: false));
            return result.RequestId;
        }

        public async Task CancelAsync(string requestId, CancellationToken ct)
        {
            RequestInfo info = await Client.CancelAsync(requestId, ct).ConfigureAwait(false);
            _queue.Post(() => Upsert(info, recovered: false));

            return;
        }

        public async Task<ChangeSet> FetchCandidateAsync(string requestId, CancellationToken ct)
        {
            CandidateInfo candidate = await Client.GetCandidateAsync(requestId, ct).ConfigureAwait(false);
            return ParseCandidate(candidate, requestId);
        }

        public async Task<byte[]> FetchArtifactAsync(string sha256, CancellationToken ct)
        {
            VerifiedArtifact artifact = await Client.DownloadArtifactAsync(sha256, null, ct).ConfigureAwait(false);
            return artifact.Bytes;
        }

        public async Task<OpResult> GenerateAsync(OpRequest req, CancellationToken ct)
        {
            if (req == null)
            {
                throw new ArgumentNullException(nameof(req));
            }

            GenerateBody body = new GenerateBody(GenerateBody.CompanionOp(req.Op), req.Inputs) { MaxCostUsd = req.MaxCostUsd ?? _options.MaxCostUsd, ChangeSetId = req.ChangeSetId };
            try
            {
                GenerateResult result = await GenerateWithReplayAsync(body, ct).ConfigureAwait(false);
                if (result.Artifacts.Count == 0)
                {
                    Diagnostic? failed = body.Op == "describe" ? null : FailedState(result);
                    return new OpResult(null, null, null, failed, result.Text, null, result.Provider) { State = result.State };
                }

                StoredArtifactInfo stored = result.Artifacts[0];
                VerifiedArtifact verified = await Client.DownloadArtifactAsync(stored.Sha256, stored.Bytes, ct).ConfigureAwait(false);
                return new OpResult(verified.Sha256, stored.MediaType, verified.Bytes, null, result.Text, stored.Name, result.Provider) { State = result.State };
            }
            catch (EtosException error)
            {
                return OpResult.Refused(DiagnosticOf(error.Error));
            }
        }

        /// <summary>
        /// <c>POST /v1/ops/generate</c>; a long op whose companion call timed out (<c>transport</c>) is re-issued with
        /// the identical body within <see cref="EtosGatewayOptions.OpReplayWindow"/> (same effect key: a lookup, not a
        /// second submission). Any other refusal is returned at once.
        /// </summary>
        public async Task<GenerateResult> GenerateWithReplayAsync(GenerateBody body, CancellationToken ct)
        {
            Stopwatch watch = Stopwatch.StartNew();
            int replays = 0;
            while (true)
            {
                try
                {
                    GenerateResult result = await Client.GenerateAsync(body, ct).ConfigureAwait(false);
                    if (result.Artifacts.Count == 0 && body.Op != "describe" && !IsFinal(StateName(result.State)) && watch.Elapsed + _options.OpReplayDelay < _options.OpReplayWindow)
                    {
                        replays++;
                        int pending = replays;
                        string state = StateName(result.State) ?? "unknown";
                        _queue.Post(() => _log.Write(StudioLogLevel.Info, "etos", "op " + body.Op + " is " + state + "; looking the job up again (" + pending + ")"));
                        await Task.Delay(_options.OpReplayDelay, ct).ConfigureAwait(false);
                        continue;
                    }

                    if (replays > 0)
                    {
                        int count = replays;
                        _queue.Post(() => _log.Write(StudioLogLevel.Info, "etos", "op " + body.Op + " answered after " + count + " re-issue(s) of the same effect key, " + watch.ElapsedMilliseconds + " ms"));
                    }

                    return result;
                }
                catch (EtosException error) when (error.Code == EtosCodes.Transport && body.Op != "describe" && watch.Elapsed + _options.OpReplayDelay < _options.OpReplayWindow)
                {
                    replays++;
                    int count = replays;
                    string message = error.Error.Message;
                    _queue.Post(() => _log.Write(StudioLogLevel.Info, "etos", "op " + body.Op + " re-issue " + count + " after a companion transport timeout (" + message + ")"));
                    await Task.Delay(_options.OpReplayDelay, ct).ConfigureAwait(false);
                }
            }
        }

        public IVoiceSession CreateVoiceSession()
        {
            return new EtosVoiceSession(Client, _queue, new MicrophoneCapture(), () => _status, _log);
        }

        /// <summary>A voice session over any PCM source (a WAV in tests and live checks, a virtual microphone...).</summary>
        public EtosVoiceSession CreateVoiceSession(IPcmSource source)
        {
            return new EtosVoiceSession(Client, _queue, source, () => _status, _log);
        }

        // ------------------------------------------------------------------------------- candidates

        /// <summary>Imports and stages the candidate of <paramref name="requestId"/> (also used to re-stage after a domain reload).</summary>
        public async Task<CandidateImport> ImportCandidateAsync(string requestId, CancellationToken ct = default)
        {
            Stopwatch watch = Stopwatch.StartNew();
            _queue.Post(() => SetLocal(requestId, "importing", null));
            CandidateInfo candidate;
            ChangeSet changeSet;
            List<KeyValuePair<ArtifactRef, VerifiedArtifact>> verified = new List<KeyValuePair<ArtifactRef, VerifiedArtifact>>();
            try
            {
                candidate = await Client.GetCandidateAsync(requestId, ct).ConfigureAwait(false);
                changeSet = ParseCandidate(candidate, requestId);
                foreach (ArtifactRef artifact in changeSet.Artifacts ?? Array.Empty<ArtifactRef>())
                {
                    VerifiedArtifact bytes = await Client.DownloadArtifactAsync(artifact.Sha256, artifact.Bytes, ct).ConfigureAwait(false);
                    verified.Add(new KeyValuePair<ArtifactRef, VerifiedArtifact>(artifact, bytes));
                }
            }
            catch (EtosException error)
            {
                Diagnostic diagnostic = DiagnosticOf(error.Error);
                return await _queue.Run(() => Failed(requestId, diagnostic, watch)).ConfigureAwait(false);
            }

            return await _queue.Run(() => StageOnMain(requestId, candidate, changeSet, verified, watch)).ConfigureAwait(false);
        }

        /// <summary>Applies a staged candidate through the engine (main thread).</summary>
        public ApplyReport Apply(string requestId)
        {
            if (!_staged.TryGetValue(requestId, out StagedChangeSet? staged))
            {
                throw new InvalidOperationException("No staged candidate for " + requestId + ".");
            }

            ApplyReport report = Runtime.Engine.Apply(staged);
            _staged.Remove(requestId);
            SetLocal(requestId, "applied", report.Diagnostics);
            return report;
        }

        /// <summary>
        /// Rejects a staged candidate: previews dropped, journal state Rejected, artifacts retained for the journal only.
        /// The reason is logged and kept with the request; the companion has no route for it (04 s2), so it is not sent.
        /// </summary>
        public void Reject(string requestId, string reason)
        {
            if (_staged.TryGetValue(requestId, out StagedChangeSet? staged))
            {
                Runtime.Engine.Discard(staged, true);
                _staged.Remove(requestId);
            }

            Diagnostic why = new Diagnostic(DiagnosticCodes.Refused, EtosRedaction.Redact("Rejected by the user: " + reason));
            SetLocal(requestId, "rejected", new[] { why });
            _log.Write(StudioLogLevel.Info, "etos", "candidate " + requestId + " rejected: " + reason);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Events.Dispose();
            foreach (StagedChangeSet staged in _staged.Values.ToList())
            {
                if (!staged.Consumed)
                {
                    Runtime.Engine.Discard(staged);
                }
            }

            _staged.Clear();
        }

        // ------------------------------------------------------------------------------- internals (main thread)

        private void OnEvent(EventFrame frame)
        {
            string? id = frame.RequestId;
            JObject data = frame.Data as JObject ?? new JObject();
            switch (frame.Type)
            {
                case "request":
                    Upsert(new RequestInfo(data), recovered: false);
                    break;
                case "task_progress":
                    if (id != null)
                    {
                        SetProgress(id, (Json.Str(data, "status") ?? "working") + (Json.Str(data, "text") is string text && text.Length > 0 ? ": " + text : string.Empty));
                    }

                    break;
                case "candidate":
                    if (id != null)
                    {
                        CandidateReady?.Invoke(new CandidateNotice(id, Json.Str(data, "changeSetId") ?? id));
                        MaybeImport(id);
                    }

                    break;
                case "candidate_invalid":
                    if (id != null)
                    {
                        SetLocal(id, null, DiagnosticsOf(data["diagnostics"] as JArray));
                    }

                    break;
                case "clarification":
                    if (id != null)
                    {
                        SetProgress(id, "question: " + (Json.Str(data, "question") ?? string.Empty));
                    }

                    break;
            }
        }

        private void OnStreamState(EventStreamState state, EtosError? error)
        {
            if (state == EventStreamState.Reconnecting && error != null)
            {
                _log.Write(StudioLogLevel.Debug, "etos", "event stream reconnecting", DiagnosticOf(error));
            }
        }

        private void Upsert(RequestInfo info, bool recovered)
        {
            if (string.IsNullOrEmpty(info.RequestId))
            {
                return;
            }

            Tracked tracked = TrackedFor(info.RequestId);
            if (tracked.Info != null && tracked.Info.Seq > info.Seq)
            {
                return;
            }

            tracked.Info = new RequestInfo((JObject)EtosRedaction.RedactJson(info.Raw));
            RequestChanged?.Invoke(tracked.View());
            if (info.State == RequestStates.Candidate && info.HasCandidate)
            {
                if (!recovered || !Runtime.Journal.Exists(info.ChangeSetId))
                {
                    MaybeImport(info.RequestId);
                }
            }
        }

        private void MaybeImport(string requestId)
        {
            Tracked tracked = TrackedFor(requestId);
            if (!_options.AutoImport || tracked.ImportStarted || !_own.Contains(requestId))
            {
                return;
            }

            tracked.ImportStarted = true;
            _ = ImportCandidateAsync(requestId);
        }

        private CandidateImport StageOnMain(string requestId, CandidateInfo candidate, ChangeSet changeSet, List<KeyValuePair<ArtifactRef, VerifiedArtifact>> verified, Stopwatch watch)
        {
            try
            {
                foreach (KeyValuePair<ArtifactRef, VerifiedArtifact> pair in verified)
                {
                    Runtime.Artifacts.Put(pair.Value.Bytes, pair.Key);
                }
            }
            catch (ArtifactStoreException error)
            {
                return Failed(requestId, new Diagnostic(DiagnosticCodes.CandidateInvalid, EtosRedaction.Redact("artifact_retention: " + error.Message)), watch);
            }

            Tracked tracked = TrackedFor(requestId);
            List<string> tasks = new List<string>(changeSet.Links?.EtosTasks ?? Array.Empty<string>());
            foreach (string task in tracked.Info?.Tasks ?? (IReadOnlyList<string>)Array.Empty<string>())
            {
                if (!tasks.Contains(task))
                {
                    tasks.Add(task);
                }
            }

            if (candidate.TaskId != null && !tasks.Contains(candidate.TaskId))
            {
                tasks.Add(candidate.TaskId);
            }

            ChangeSet linked = changeSet.WithLinks(new Links(tasks.Count == 0 ? null : tasks, changeSet.Links?.Parent ?? tracked.Parent, changeSet.Links?.GameCoreOps));
            string revision = candidate.ToolCatalogRevision ?? tracked.CatalogRevision ?? string.Empty;
            StagedChangeSet staged = Runtime.Engine.Stage(linked, new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = revision });
            if (_staged.TryGetValue(requestId, out StagedChangeSet? previous) && !ReferenceEquals(previous, staged))
            {
                Runtime.Engine.Discard(previous);
            }

            _staged[requestId] = staged;
            IReadOnlyList<Diagnostic> diagnostics = staged.AllDiagnostics;
            SetLocal(requestId, staged.Ok ? "staged" : "stage_refused", diagnostics);
            CandidateImport import = new CandidateImport(requestId, staged, diagnostics, watch.Elapsed.TotalMilliseconds);
            _log.Write(staged.Ok ? StudioLogLevel.Info : StudioLogLevel.Warning, "etos", "candidate " + requestId + (staged.Ok ? " staged" : " refused at staging") + " in " + import.Milliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms (" + verified.Count + " artifact(s) verified)", staged.Ok ? null : diagnostics.FirstOrDefault());
            CandidateStaged?.Invoke(import);
            return import;
        }

        private CandidateImport Failed(string requestId, Diagnostic diagnostic, Stopwatch watch)
        {
            Fail(requestId, diagnostic, "import_failed");
            CandidateImport import = new CandidateImport(requestId, null, new[] { diagnostic }, watch.Elapsed.TotalMilliseconds);
            CandidateStaged?.Invoke(import);
            return import;
        }

        private void Fail(string requestId, Diagnostic diagnostic, string localState)
        {
            SetLocal(requestId, localState, new[] { diagnostic });
            _log.Write(StudioLogLevel.Warning, "etos", "request " + requestId + ": " + localState, diagnostic);
        }

        private void OnGenerated(AgentAssetRequest request, OpResult result, string path)
        {
            if (!result.Succeeded || result.Sha256 == null || result.Bytes == null)
            {
                _log.Write(StudioLogLevel.Warning, "etos", "asset.generate for " + request.ChangeSetId + "/" + request.OpId + " produced nothing", result.Refusal ?? new Diagnostic(DiagnosticCodes.Refused, "The op returned no artifact."));
                return;
            }

            ArtifactRef artifact = new ArtifactRef(result.Sha256, result.MediaType ?? "application/octet-stream", result.Bytes.LongLength, System.IO.Path.GetFileName(path), new ArtifactProducer(op: OpForKind(request.Kind), provider: result.Provider), RoleOf(request.Kind));
            try
            {
                Runtime.Artifacts.Put(result.Bytes, artifact);
            }
            catch (ArtifactStoreException error)
            {
                _log.Write(StudioLogLevel.Warning, "etos", "asset.generate artifact not retained: " + error.Message);
                return;
            }

            string id = IdDerivation.NewChangeSetId();
            Operation import = new Operation("op1", BuiltInToolIds.AssetImport, null, new JObject { ["path"] = path, ["artifact"] = new JObject { ["artifact"] = artifact.Reference } });
            ChangeSet changeSet = new ChangeSet(id, ChangeSet.SchemaId, new Intent("Import the generated " + request.Kind + " (" + request.Prompt + ")", IntentOrigin.Agent), new[] { import }, artifacts: new[] { artifact }, requirements: new Requirements(RuntimeApply.Live, false, false, false), links: new Links(null, request.ChangeSetId, null));
            ToolCatalog catalog = Runtime.Registry.Catalog;
            StagedChangeSet staged = Runtime.Engine.Stage(changeSet, new StageOptions { Mode = ValidationMode.Candidate, ToolCatalogRevision = catalog.Revision ?? catalog.ComputeRevision() });
            _staged[id] = staged;
            Tracked tracked = Track(id, changeSet.Intent.Text, request.ChangeSetId, catalog.Revision);
            tracked.LocalState = staged.Ok ? "staged" : "stage_refused";
            tracked.Diagnostics = staged.AllDiagnostics.ToList();
            CandidateImport importResult = new CandidateImport(id, staged, staged.AllDiagnostics, 0);
            CandidateStaged?.Invoke(importResult);
        }

        private Tracked Track(string id, string? intent, string? parent, string? catalogRevision)
        {
            Tracked tracked = TrackedFor(id);
            tracked.Intent = intent == null ? tracked.Intent : EtosRedaction.Redact(intent);
            tracked.Parent = parent ?? tracked.Parent;
            tracked.CatalogRevision = catalogRevision ?? tracked.CatalogRevision;
            return tracked;
        }

        private Tracked TrackedFor(string id)
        {
            if (!_requests.TryGetValue(id, out Tracked? tracked))
            {
                tracked = new Tracked(id);
                _requests[id] = tracked;
                _order.Add(id);
            }

            return tracked;
        }

        private void SetProgress(string id, string progress)
        {
            Tracked tracked = TrackedFor(id);
            tracked.Progress = EtosRedaction.Redact(progress);
            RequestChanged?.Invoke(tracked.View());
        }

        private void SetLocal(string id, string? localState, IReadOnlyList<Diagnostic>? diagnostics)
        {
            Tracked tracked = TrackedFor(id);
            if (localState != null)
            {
                tracked.LocalState = localState;
            }

            if (diagnostics != null)
            {
                tracked.Diagnostics = diagnostics.Select(RedactingStudioLog.Redact).ToList();
            }

            RequestChanged?.Invoke(tracked.View());
        }

        // ------------------------------------------------------------------------------- pure helpers

        /// <summary>The candidate change set: null anywhere is refused (03 s9), then the strict model reader.</summary>
        public static ChangeSet ParseCandidate(CandidateInfo candidate, string requestId)
        {
            string? nullAt = Json.FirstNull(candidate.ChangeSet);
            if (nullAt != null)
            {
                throw new EtosException(new EtosError(0, EtosCodes.CandidateInvalid, "The candidate change set carries null at " + nullAt + " (03 s9: optional members are omitted, never null)."));
            }

            ChangeSet changeSet;
            try
            {
                changeSet = StudioJson.Deserialize<ChangeSet>(candidate.ChangeSet.ToString(Formatting.None));
            }
            catch (JsonException error)
            {
                throw new EtosException(new EtosError(0, EtosCodes.CandidateInvalid, "The candidate change set does not fit the contract: " + error.Message));
            }

            if (!string.Equals(changeSet.Id, requestId, StringComparison.Ordinal))
            {
                throw new EtosException(new EtosError(0, EtosCodes.CandidateInvalid, "The candidate is change set " + changeSet.Id + ", not " + requestId + "."));
            }

            return changeSet;
        }

        /// <summary>An etos error as a 03 s9 diagnostic with the etos code preserved.</summary>
        public static Diagnostic DiagnosticOf(EtosError error)
        {
            JObject? data = error.Data == null ? null : (JObject)error.Data.DeepClone();
            if (error.Status != 0) { data ??= new JObject(); data["status"] = error.Status; }
            if (error.Diagnostics != null && error.Diagnostics.Count > 0)
            {
                data ??= new JObject();
                data["diagnostics"] = error.Diagnostics.DeepClone();
            }

            return new Diagnostic(error.Code, error.Message, error.Hint, null, data);
        }

        public static ProviderState StateOf(string? state)
        {
            switch (state)
            {
                case ProviderStates.Live:
                    return ProviderState.Live;
                case ProviderStates.NotConfigured:
                    return ProviderState.NotConfigured;
                case ProviderStates.Blocked:
                    return ProviderState.Blocked;
                default:
                    return ProviderState.Unknown;
            }
        }

        /// <summary>The job state of an op answer (<c>state</c> as a string or <c>state.state</c>), or null.</summary>
        public static string? StateName(JToken? state)
        {
            if (state == null || state.Type == JTokenType.Null)
            {
                return null;
            }

            if (state.Type == JTokenType.String)
            {
                return (string?)state;
            }

            return state is JObject obj ? Json.Str(obj, "state") : null;
        }

        /// <summary>True for a final etops job state.</summary>
        public static bool IsFinal(string? state)
        {
            return state == "succeeded" || state == "failed" || state == "cancelled" || state == "canceled" || state == "unknown" || state == "expired";
        }

        /// <summary>The refusal of an op answer without artifacts: its job failed, or it is still pending after the window.</summary>
        private static Diagnostic FailedState(GenerateResult result)
        {
            string state = StateName(result.State) ?? "unknown";
            JObject? obj = result.State as JObject;
            JObject? error = obj?["error"] as JObject;
            string code = (error == null ? null : Json.Str(error, "code")) ?? (IsFinal(state) ? "op_" + state : "op_pending");
            string message = (error == null ? null : Json.Str(error, "message")) ?? ("The " + result.Op + " job is " + state + " and returned no artifact.");
            JObject data = new JObject { ["state"] = result.State?.DeepClone() };
            return RedactingStudioLog.Redact(new Diagnostic(code, message, state == "unknown" ? "etos reports the outcome unknown: it is unresolved, not a success." : null, null, data));
        }

        /// <summary>The etos op for an asset.generate kind, or null.</summary>
        public static string? OpForKind(string kind)
        {
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "image":
                case "texture":
                case "portrait":
                case "sprite":
                case "icon":
                    return "generate.image";
                case "voice":
                case "tts":
                case "line":
                case "voiceline":
                    return "tts";
                case "mesh":
                case "3d":
                case "model":
                    return "generate.3d";
                default:
                    return null;
            }
        }

        private static string CapabilityOf(string op) => op == "generate.image" ? "image" : op == "generate.3d" ? "3d" : op;

        private static string RoleOf(string kind)
        {
            string? op = OpForKind(kind);
            return op == "tts" ? "voiceLine" : op == "generate.3d" ? "mesh" : "texture";
        }

        private static JObject InputsFor(string op, string prompt, JObject? options)
        {
            JObject inputs = new JObject();
            if (op == "tts")
            {
                inputs["text"] = prompt;
            }
            else
            {
                inputs["prompt"] = prompt;
            }

            if (options != null)
            {
                foreach (JProperty property in options.Properties())
                {
                    if (property.Name != "max_cost_usd" && property.Value != null && property.Value.Type != JTokenType.Null)
                    {
                        inputs[property.Name] = property.Value.DeepClone();
                    }
                }
            }

            return inputs;
        }

        private static List<Diagnostic> DiagnosticsOf(JArray? array)
        {
            List<Diagnostic> list = new List<Diagnostic>();
            if (array == null)
            {
                return list;
            }

            foreach (JToken item in array)
            {
                if (item is JObject obj)
                {
                    list.Add(DiagnosticOf(new EtosError(0, Json.Str(obj, "code") ?? DiagnosticCodes.CandidateInvalid, Json.Str(obj, "message") ?? string.Empty, Json.Str(obj, "hint"), data: obj["data"] as JObject)));
                }
            }

            return list;
        }

        /// <summary>A file-name slug of a prompt.</summary>
        public static string Slug(string text)
        {
            System.Text.StringBuilder slug = new System.Text.StringBuilder();
            foreach (char c in (text ?? string.Empty).ToLowerInvariant())
            {
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    slug.Append(c);
                }
                else if (slug.Length > 0 && slug[slug.Length - 1] != '_')
                {
                    slug.Append('_');
                }

                if (slug.Length >= 40)
                {
                    break;
                }
            }

            string result = slug.ToString().Trim('_');
            return result.Length == 0 ? "asset" : result;
        }

        private sealed class Tracked
        {
            public Tracked(string id)
            {
                Id = id;
            }

            public string Id { get; }

            public RequestInfo? Info { get; set; }

            public string? Intent { get; set; }

            public string? Parent { get; set; }

            public string? CatalogRevision { get; set; }

            public string? Progress { get; set; }

            public string? LocalState { get; set; }

            public List<Diagnostic> Diagnostics { get; set; } = new List<Diagnostic>();

            public bool ImportStarted { get; set; }

            public RequestView View()
            {
                RequestInfo? info = Info;
                return new RequestView(
                    Id,
                    info?.ChangeSetId ?? Id,
                    info?.State ?? (LocalState == "staged" || LocalState == "stage_refused" ? "candidate" : "requested"),
                    info?.Worker,
                    info?.TaskId,
                    info?.TaskStatus,
                    info?.Attempt ?? 0,
                    info?.Tasks ?? (IReadOnlyList<string>)Array.Empty<string>(),
                    info?.Outcome,
                    info?.HasCandidate ?? false,
                    info?.Seq ?? 0,
                    info?.CreatedAt ?? 0,
                    info?.UpdatedAt ?? 0,
                    Intent,
                    Progress,
                    LocalState,
                    Diagnostics.ToList());
            }
        }
    }
}
