// GameCore.Studio.Etos.Client - the typed HTTP client of the companion API (04 s2), reached ONLY through the etos node's
// proxied agent route: {node}/api/v1/agents/gamecore-studio/http/<path> with "Authorization: Bearer <app key>" (etos
// checks the key and the app's `uses`, strips Authorization, and adds X-Etos-App and X-Etos-Proxy-Token). WebSockets use
// a single-use ticket from POST {node}/api/v1/tickets {path} (30 s, bound to the path without its query; the upgrade
// carries ?etos_ticket=..., no Authorization header). No provider is ever called directly and no endpoint beyond 04 s2
// is used. Errors keep the etos code (EtosException.Code); every logged or surfaced text is redacted.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>One timed HTTP exchange (for evidence transcripts and latency reports).</summary>
    public sealed class ExchangeRecord
    {
        public ExchangeRecord(string method, string path, int status, double milliseconds, string? code)
        {
            Method = method;
            Path = path;
            Status = status;
            Milliseconds = milliseconds;
            Code = code;
        }

        public string Method { get; }

        /// <summary>Path relative to the companion base (never carries a ticket or key).</summary>
        public string Path { get; }

        public int Status { get; }

        public double Milliseconds { get; }

        /// <summary>The etos error code when the call was refused.</summary>
        public string? Code { get; }
    }

    /// <summary>HTTP + WebSocket client of the gamecore-studio companion through the etos proxy.</summary>
    public sealed class CompanionClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly EtosCredentials _credentials;
        private readonly bool _ownsHttp;

        public CompanionClient(EtosClientOptions options, EtosCredentials credentials, HttpMessageHandler? handler = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            _credentials = credentials ?? throw new ArgumentNullException(nameof(credentials));
            if (handler == null)
            {
                HttpClientHandler own = new HttpClientHandler { UseProxy = options.UseSystemProxy, AllowAutoRedirect = false };
                _http = new HttpClient(own, true);
            }
            else
            {
                _http = new HttpClient(handler, false);
            }

            _ownsHttp = true;
            _http.Timeout = Timeout.InfiniteTimeSpan;
            NodeUrl = (options.NodeUrl ?? string.Empty).TrimEnd('/');
            if (!Uri.TryCreate(NodeUrl, UriKind.Absolute, out Uri? parsed) || (parsed.Scheme != "http" && parsed.Scheme != "https"))
            {
                throw new EtosException(new EtosError(0, EtosCodes.NotConfigured, "The node URL '" + NodeUrl + "' is not an http(s) URL."));
            }
        }

        public EtosClientOptions Options { get; }

        /// <summary>The node API root (no trailing slash).</summary>
        public string NodeUrl { get; }

        /// <summary>The proxied companion base path on the node: <c>/api/v1/agents/{agent}/http</c>.</summary>
        public string BasePath => "/api/v1/agents/" + Options.AgentName + "/http";

        /// <summary>The credentials' display form (never the key).</summary>
        public string KeyDisplay => _credentials.Display;

        /// <summary>Called after every HTTP exchange (method, path, status, duration, code).</summary>
        public event Action<ExchangeRecord>? Exchanged;

        // ------------------------------------------------------------------------------------------------ routes

        /// <summary><c>GET /v1/hello</c>: version, capabilities, provider status.</summary>
        public async Task<HelloInfo> HelloAsync(CancellationToken ct = default)
        {
            return new HelloInfo(await GetObjectAsync("/v1/hello", ct).ConfigureAwait(false));
        }

        /// <summary><c>POST /v1/requests</c>: idempotent on <c>changeSetId</c>.</summary>
        public async Task<SubmitResult> SubmitAsync(EditRequestBody request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            request.Validate();
            return new SubmitResult(await SendObjectAsync(HttpMethod.Post, "/v1/requests", request.ToJson(), Options.RequestTimeout, ct).ConfigureAwait(false));
        }

        /// <summary><c>GET /v1/requests/{id}</c>.</summary>
        public async Task<RequestInfo> GetRequestAsync(string requestId, CancellationToken ct = default)
        {
            return new RequestInfo(await GetObjectAsync("/v1/requests/" + Escape(requestId), ct).ConfigureAwait(false));
        }

        /// <summary><c>GET /v1/requests?after=&amp;limit=</c>: this app's requests changed after <paramref name="after"/>.</summary>
        public async Task<RequestPage> ListRequestsAsync(long after = 0, int? limit = null, CancellationToken ct = default)
        {
            string path = "/v1/requests?after=" + after.ToString(CultureInfo.InvariantCulture) + (limit == null ? string.Empty : "&limit=" + limit.Value.ToString(CultureInfo.InvariantCulture));
            return new RequestPage(await GetObjectAsync(path, ct).ConfigureAwait(false));
        }

        /// <summary><c>POST /v1/requests/{id}/cancel</c>: cancelled through etos; the answer is the request view.</summary>
        public async Task<RequestInfo> CancelAsync(string requestId, CancellationToken ct = default)
        {
            return new RequestInfo(await SendObjectAsync(HttpMethod.Post, "/v1/requests/" + Escape(requestId) + "/cancel", new JObject(), Options.RequestTimeout, ct).ConfigureAwait(false));
        }

        /// <summary><c>GET /v1/candidates/{id}</c>: the validated change set and its artifact manifest.</summary>
        public async Task<CandidateInfo> GetCandidateAsync(string requestId, CancellationToken ct = default)
        {
            return new CandidateInfo(await GetObjectAsync("/v1/candidates/" + Escape(requestId), ct).ConfigureAwait(false));
        }

        /// <summary><c>POST /v1/index/delta</c>.</summary>
        public async Task<IndexDeltaAck> PostIndexDeltaAsync(JObject delta, CancellationToken ct = default)
        {
            return new IndexDeltaAck(await SendObjectAsync(HttpMethod.Post, "/v1/index/delta", delta, Options.RequestTimeout, ct).ConfigureAwait(false));
        }

        /// <summary><c>POST /v1/ops/generate</c>; <c>max_cost_usd</c> is always sent (the configured default when unset).</summary>
        public async Task<GenerateResult> GenerateAsync(GenerateBody request, CancellationToken ct = default)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return new GenerateResult(await SendObjectAsync(HttpMethod.Post, "/v1/ops/generate", request.ToJson(Options.DefaultMaxCostUsd), Options.GenerateTimeout, ct).ConfigureAwait(false));
        }

        /// <summary><c>POST /v1/stage</c> (202 with the job).</summary>
        public async Task<StageJobInfo> StageAsync(string changeSetId, string projectId, string sourceRevision, string catalogRevision, CancellationToken ct = default)
        {
            if (projectId != Options.ProjectId || Json.NormalizeSha256(projectId) == null
                || string.IsNullOrWhiteSpace(sourceRevision) || Json.NormalizeSha256(catalogRevision) == null)
                throw new EtosException(new EtosError(0, EtosCodes.BadRequest, "stage_context_invalid"));
            JObject body = new JObject { ["changeSetId"] = changeSetId, ["projectId"] = projectId,
                ["sourceRevision"] = sourceRevision, ["catalogRevision"] = catalogRevision };
            return new StageJobInfo(await SendObjectAsync(HttpMethod.Post, "/v1/stage", body, Options.RequestTimeout, ct).ConfigureAwait(false));
        }

        public Task<JObject> FetchTrustedVerdictAsync(string jobId, CancellationToken ct = default) =>
            GetObjectAsync("/v1/stage/" + Escape(jobId) + "/verdict", ct);

        public async Task<bool> VerifyVerdictAsync(string jobId, JObject signedRecord, CancellationToken ct = default)
        {
            JObject result = await SendObjectAsync(HttpMethod.Post, "/v1/stage/" + Escape(jobId) + "/verify",
                signedRecord, Options.RequestTimeout, ct).ConfigureAwait(false);
            return result["verified"]?.Type == JTokenType.Boolean && result["verified"]!.Value<bool>();
        }

        public Task<JObject> DiscardStageAsync(string changeSetId, CancellationToken ct = default) =>
            SendObjectAsync(HttpMethod.Post, "/v1/stage", new JObject { ["changeSetId"] = changeSetId,
                ["projectId"] = Options.ProjectId, ["action"] = "discard" }, Options.RequestTimeout, ct);

        /// <summary><c>GET /v1/stage/{job}</c>.</summary>
        public async Task<StageJobInfo> GetStageAsync(string jobId, CancellationToken ct = default)
        {
            return new StageJobInfo(await GetObjectAsync("/v1/stage/" + Escape(jobId), ct).ConfigureAwait(false));
        }

        /// <summary>
        /// <c>GET /v1/artifacts/{sha256}</c>: streams the bytes to a temp file, then verifies the digest (and
        /// <paramref name="expectedBytes"/> when given) on what is on disk before returning them. A mismatch is
        /// <c>artifact_digest_mismatch</c> / <c>artifact_size_mismatch</c> and the bytes are discarded.
        /// </summary>
        public async Task<VerifiedArtifact> DownloadArtifactAsync(string sha256, long? expectedBytes = null, CancellationToken ct = default)
        {
            string? digest = Json.NormalizeSha256(sha256);
            if (digest == null)
            {
                throw new EtosException(new EtosError(0, EtosCodes.BadRequest, "'" + sha256 + "' is not a sha256 digest."));
            }

            string directory = Options.TempDirectory ?? Path.GetTempPath();
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, "gcstudio-artifact-" + digest.Substring(0, 12) + "-" + Guid.NewGuid().ToString("N") + ".part");
            string path = "/v1/artifacts/" + digest;
            Stopwatch watch = Stopwatch.StartNew();
            string mediaType = "application/octet-stream";
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(Options.DownloadTimeout);
                try
                {
                    using (HttpRequestMessage message = NewMessage(HttpMethod.Get, BasePath + path))
                    using (HttpResponseMessage response = await SendRaw(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token, ct, path).ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            string text = await ReadText(response).ConfigureAwait(false);
                            EtosError error = EtosError.FromBody((int)response.StatusCode, text);
                            Record("GET", path, (int)response.StatusCode, watch, error.Code);
                            throw new EtosException(error);
                        }

                        mediaType = response.Content.Headers.ContentType?.MediaType ?? mediaType;
                        using (Stream body = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (FileStream file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        {
                            await body.CopyToAsync(file, 81920, deadline.Token).ConfigureAwait(false);
                        }

                        Record("GET", path, (int)response.StatusCode, watch, null);
                    }

                    Options.DownloadTamperHook?.Invoke(temp);
                    byte[] bytes = File.ReadAllBytes(temp);
                    string actual = Json.Sha256Hex(bytes);
                    if (!string.Equals(actual, digest, StringComparison.Ordinal))
                    {
                        Log("artifact " + digest + " refused: the downloaded bytes hash to " + actual);
                        throw new EtosException(new EtosError(0, EtosCodes.ArtifactDigestMismatch, "Artifact sha256:" + digest + " was refused: the downloaded bytes hash to sha256:" + actual + ".", "Nothing was imported; the artifact may be corrupted in transit or in the companion's store."));
                    }

                    if (expectedBytes != null && bytes.LongLength != expectedBytes.Value)
                    {
                        throw new EtosException(new EtosError(0, EtosCodes.ArtifactSizeMismatch, "Artifact sha256:" + digest + " has " + bytes.LongLength.ToString(CultureInfo.InvariantCulture) + " bytes, the change set declares " + expectedBytes.Value.ToString(CultureInfo.InvariantCulture) + "."));
                    }

                    return new VerifiedArtifact(digest, mediaType, bytes);
                }
                catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
                {
                    throw new EtosException(new EtosError(0, EtosCodes.Timeout, "Downloading artifact " + digest + " took longer than " + Options.DownloadTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s."), error);
                }
                catch (IOException error)
                {
                    throw EtosException.Transport("Downloading artifact " + digest + " failed", error);
                }
                finally
                {
                    TryDelete(temp);
                }
            }
        }

        /// <summary>
        /// Authority diagnostic (W-ETOS-02, settings "Test connection"): sends a body-less GET with the app key to a node
        /// path and returns the refusal, or null when the node answered 2xx. It does not read or return any answer body
        /// beyond the error, and Studio uses it only to show what the key may not do.
        /// </summary>
        public async Task<EtosError?> ProbeAsync(string absoluteNodePath, CancellationToken ct = default)
        {
            try
            {
                await SendObjectAsync(HttpMethod.Get, absoluteNodePath, null, Options.RequestTimeout, ct, absolute: true).ConfigureAwait(false);
                return null;
            }
            catch (EtosException error)
            {
                return error.Error;
            }
        }

        // -------------------------------------------------------------------------------------------- websockets

        /// <summary><c>POST /api/v1/tickets {path}</c>: a single-use 30 s ticket for a proxied WebSocket path (no query).</summary>
        public async Task<string> IssueTicketAsync(string companionPath, CancellationToken ct = default)
        {
            string full = BasePath + companionPath;
            JObject body = new JObject { ["path"] = full };
            JObject answer = await SendObjectAsync(HttpMethod.Post, "/api/v1/tickets", body, Options.RequestTimeout, ct, absolute: true).ConfigureAwait(false);
            string? ticket = Json.Str(answer, "ticket");
            if (string.IsNullOrEmpty(ticket))
            {
                throw EtosException.Protocol("The ticket answer has no ticket.");
            }

            return ticket!;
        }

        /// <summary>The ws(s):// URI of a proxied companion path with its query and ticket.</summary>
        public Uri WebSocketUri(string companionPath, string? query, string ticket)
        {
            string root = NodeUrl.StartsWith("https://", StringComparison.Ordinal) ? "wss://" + NodeUrl.Substring(8) : "ws://" + NodeUrl.Substring(NodeUrl.IndexOf("://", StringComparison.Ordinal) + 3);
            string q = string.IsNullOrEmpty(query) ? string.Empty : query + "&";
            return new Uri(root + BasePath + companionPath + "?" + q + "etos_ticket=" + Uri.EscapeDataString(ticket));
        }

        /// <summary>Obtains a ticket and opens a WebSocket on a proxied companion path (<c>/v1/events</c>, <c>/v1/voice</c>).</summary>
        public async Task<ClientWebSocket> ConnectWebSocketAsync(string companionPath, string? query, CancellationToken ct = default)
        {
            string ticket = await IssueTicketAsync(companionPath, ct).ConfigureAwait(false);
            ClientWebSocket socket = new ClientWebSocket();
            bool transferred = false;
            try
            {
                if (!Options.UseSystemProxy) socket.Options.Proxy = null;
                socket.Options.SetRequestHeader("X-GameCore-Project", Options.ProjectId);
                socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
                Uri uri = WebSocketUri(companionPath, query, ticket);
                Stopwatch watch = Stopwatch.StartNew();
                using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    deadline.CancelAfter(Options.RequestTimeout);
                    try
                    {
                        await socket.ConnectAsync(uri, deadline.Token).ConfigureAwait(false);
                        Record("WS", companionPath, 101, watch, null);
                        transferred = true;
                        return socket;
                    }
                    catch (Exception error) when (error is WebSocketException || error is HttpRequestException || (error is OperationCanceledException && !ct.IsCancellationRequested))
                    {
                        Record("WS", companionPath, 0, watch, EtosCodes.Transport);
                        throw EtosException.Transport("The WebSocket " + companionPath + " could not be opened through the node", error);
                    }
                }
            }
            finally
            {
                if (!transferred) socket.Dispose();
            }
        }

        public void Dispose()
        {
            if (_ownsHttp)
            {
                _http.Dispose();
            }
        }

        // -------------------------------------------------------------------------------------------- plumbing

        internal void Log(string line)
        {
            Options.Log?.Invoke(EtosRedaction.Redact("[etos] " + line));
        }

        private Task<JObject> GetObjectAsync(string path, CancellationToken ct)
        {
            return SendObjectAsync(HttpMethod.Get, path, null, Options.RequestTimeout, ct);
        }

        private async Task<JObject> SendObjectAsync(HttpMethod method, string path, JObject? body, TimeSpan timeout, CancellationToken ct, bool absolute = false)
        {
            string full = absolute ? path : BasePath + path;
            Stopwatch watch = Stopwatch.StartNew();
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            using (HttpRequestMessage message = NewMessage(method, full))
            {
                deadline.CancelAfter(timeout);
                if (body != null)
                {
                    message.Content = new StringContent(Json.Write(body), Encoding.UTF8, "application/json");
                }

                string text;
                int status;
                try
                {
                    using (HttpResponseMessage response = await SendRaw(message, HttpCompletionOption.ResponseContentRead, deadline.Token, ct, path).ConfigureAwait(false))
                    {
                        status = (int)response.StatusCode;
                        text = await ReadText(response).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException error) when (!ct.IsCancellationRequested)
                {
                    Record(method.Method, path, 0, watch, EtosCodes.Timeout);
                    throw new EtosException(new EtosError(0, EtosCodes.Timeout, method.Method + " " + path + " got no answer within " + timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture) + " s.", "The node or the companion may be overloaded; the request may still have been received (resubmitting the same change-set id is safe)."), error);
                }

                if (status < 200 || status > 299)
                {
                    EtosError error = EtosError.FromBody(status, text);
                    Record(method.Method, path, status, watch, error.Code);
                    Log(method.Method + " " + path + " -> " + error);
                    throw new EtosException(error);
                }

                Record(method.Method, path, status, watch, null);
                return Json.ParseObject(text.Length == 0 ? "{}" : text, method.Method + " " + path + " answer");
            }
        }

        private HttpRequestMessage NewMessage(HttpMethod method, string fullPath)
        {
            if (Json.NormalizeSha256(Options.ProjectId) == null)
                throw new EtosException(new EtosError(0, EtosCodes.NotConfigured, "A trusted project identity is required."));
            HttpRequestMessage message = new HttpRequestMessage(method, NodeUrl + fullPath);
            message.Headers.TryAddWithoutValidation("Authorization", _credentials.AuthorizationValue);
            message.Headers.TryAddWithoutValidation("X-Etos-App", Options.AppName);
            message.Headers.TryAddWithoutValidation("X-GameCore-Project", Options.ProjectId);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return message;
        }

        private async Task<HttpResponseMessage> SendRaw(HttpRequestMessage message, HttpCompletionOption completion, CancellationToken deadline, CancellationToken caller, string path)
        {
            try
            {
                return await _http.SendAsync(message, completion, deadline).ConfigureAwait(false);
            }
            catch (HttpRequestException error)
            {
                throw EtosException.Transport(message.Method.Method + " " + path + " did not reach the node", error);
            }
            catch (WebException error)
            {
                throw EtosException.Transport(message.Method.Method + " " + path + " did not reach the node", error);
            }
            catch (IOException error) when (!caller.IsCancellationRequested)
            {
                throw EtosException.Transport(message.Method.Method + " " + path + " broke off", error);
            }
        }

        private static async Task<string> ReadText(HttpResponseMessage response)
        {
            byte[] bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            return Encoding.UTF8.GetString(bytes);
        }

        private void Record(string method, string path, int status, Stopwatch watch, string? code)
        {
            Action<ExchangeRecord>? handler = Exchanged;
            handler?.Invoke(new ExchangeRecord(method, EtosRedaction.Redact(path), status, watch.Elapsed.TotalMilliseconds, code));
        }

        private static string Escape(string segment)
        {
            if (string.IsNullOrEmpty(segment))
            {
                throw new ArgumentException("An id is required.", nameof(segment));
            }

            return Uri.EscapeDataString(segment);
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
