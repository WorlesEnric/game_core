// GameCore.Studio.Etos.Client - WS /v1/voice (04 s2, s5; SR-4.8). One duplex session: the companion answers
// ready{sessionId, audioFormat, sampleRateHz, maxChunkBytes}, takes gapless audio frames and {type:"stop"}, and sends
// transcript revisions (role always "user"; "final" only on the item's done revision), speech boundaries, usage, errors
// and closed{reason}. A refusal (not_configured, forbidden, budget_exhausted, rate_limited...) arrives as error then
// closed; ConnectAsync turns it into an EtosException with the code preserved. Nothing here sends a prompt: only the
// caller decides what a final transcript becomes.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>The companion's ready frame.</summary>
    public sealed class VoiceReady
    {
        public VoiceReady(string sessionId, int sampleRateHz, int maxChunkBytes)
        {
            SessionId = sessionId;
            SampleRateHz = sampleRateHz;
            MaxChunkBytes = maxChunkBytes;
        }

        public string SessionId { get; }

        public int SampleRateHz { get; }

        public int MaxChunkBytes { get; }
    }

    /// <summary>One transcript revision: a full text, not a delta.</summary>
    public sealed class VoiceTranscript
    {
        public VoiceTranscript(string itemId, long revision, string text, bool final, string role, long receivedAt)
        {
            ItemId = itemId;
            Revision = revision;
            Text = EtosRedaction.Redact(text);
            Final = final;
            Role = role;
            ReceivedAt = receivedAt;
        }

        public string ItemId { get; }

        public long Revision { get; }

        public string Text { get; }

        /// <summary>True only on the item's last revision; only that text may go into the prompt box.</summary>
        public bool Final { get; }

        public string Role { get; }

        /// <summary>Client receipt time (ms since the epoch).</summary>
        public long ReceivedAt { get; }
    }

    /// <summary>A voice session over the proxied WebSocket.</summary>
    public sealed class VoiceChannel : IDisposable
    {
        private readonly CompanionClient _client;
        private readonly SemaphoreSlim _send = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _life = new CancellationTokenSource();
        private readonly TaskCompletionSource<VoiceReady> _ready = new TaskCompletionSource<VoiceReady>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<string> _closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        private WebSocket? _socket;
        private Task? _receive;
        private long _seq;
        private EtosError? _firstError;
        private bool _disposed;

        public VoiceChannel(CompanionClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public event Action<VoiceTranscript>? Transcript;

        public event Action<string>? SpeechStarted;

        public event Action<string>? SpeechEnded;

        public event Action<JToken>? Usage;

        /// <summary>An error frame (the session may continue, e.g. audio_gap; a refusal is followed by Closed).</summary>
        public event Action<EtosError>? Error;

        /// <summary>The session ended (reason as the companion gave it, or the client's).</summary>
        public event Action<string>? Closed;

        public VoiceReady? ReadyInfo { get; private set; }

        /// <summary>Audio frames sent (the next seq).</summary>
        public long FramesSent => Interlocked.Read(ref _seq);

        public long BytesSent { get; private set; }

        public bool IsOpen => _socket != null && _socket.State == WebSocketState.Open && !_closed.Task.IsCompleted;

        /// <summary>The reason once closed.</summary>
        public Task<string> Completion => _closed.Task;

        /// <summary>Opens the session and waits for ready; a refusal throws with the companion's code.</summary>
        public async Task<VoiceReady> ConnectAsync(CancellationToken ct = default)
        {
            if (_socket != null)
            {
                throw new InvalidOperationException("A voice channel connects once.");
            }

            _socket = await _client.ConnectWebSocketAsync("/v1/voice", null, ct).ConfigureAwait(false);
            _receive = Task.Run(() => ReceiveLoop(_socket, _life.Token));
            using (CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                Task finished = await Task.WhenAny(_ready.Task, _closed.Task, Task.Delay(Timeout.Infinite, deadline.Token)).ConfigureAwait(false);
                if (finished == _ready.Task)
                {
                    ReadyInfo = await _ready.Task.ConfigureAwait(false);
                    return ReadyInfo;
                }

                string reason = _closed.Task.IsCompleted ? await _closed.Task.ConfigureAwait(false) : "deadline";
                EtosError error = _firstError ?? new EtosError(0, reason == "deadline" ? EtosCodes.Timeout : EtosCodes.Transport, "The voice session did not become ready (" + reason + ").");
                await AbortAsync().ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                throw new EtosException(error);
            }
        }

        /// <summary>Sends PCM16 mono 24 kHz audio as gapless frames of at most 24 KiB.</summary>
        public async Task SendPcmAsync(byte[] pcm16, CancellationToken ct = default)
        {
            WebSocket socket = _socket ?? throw new InvalidOperationException("Connect first.");
            await _send.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                long seq = Interlocked.Read(ref _seq);
                List<string> frames = VoiceFraming.Frames(pcm16, ref seq, ReadyInfo?.MaxChunkBytes ?? VoiceFraming.MaxChunkBytes);
                foreach (string frame in frames)
                {
                    await WebSocketText.SendAsync(socket, frame, ct).ConfigureAwait(false);
                    Interlocked.Increment(ref _seq);
                }

                BytesSent += pcm16.Length;
            }
            catch (Exception error) when (error is WebSocketException || error is IOException || error is ObjectDisposedException)
            {
                throw EtosException.Transport("Sending voice audio failed", error);
            }
            finally
            {
                _send.Release();
            }
        }

        /// <summary>Sends stop, waits up to <paramref name="wait"/> for the companion's closed (pending transcripts arrive first), then closes.</summary>
        public async Task<string> StopAsync(TimeSpan wait)
        {
            WebSocket? socket = _socket;
            if (socket == null)
            {
                return "not connected";
            }

            if (socket.State == WebSocketState.Open && !_closed.Task.IsCompleted)
            {
                await _send.WaitAsync().ConfigureAwait(false);
                try
                {
                    await WebSocketText.SendAsync(socket, VoiceFraming.StopFrame, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception error) when (error is WebSocketException || error is IOException || error is ObjectDisposedException)
                {
                }
                finally
                {
                    _send.Release();
                }
            }

            Task finished = await Task.WhenAny(_closed.Task, Task.Delay(wait)).ConfigureAwait(false);
            string reason = finished == _closed.Task ? await _closed.Task.ConfigureAwait(false) : "stop timed out";
            // Preserve the diagnostic cause before cancellation can report a generic client close.
            Close(reason);
            await AbortAsync().ConfigureAwait(false);
            return reason;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _life.Cancel();
            _socket?.Abort();
            _socket?.Dispose();
            // In-flight send/receive continuations still own these synchronization objects.
        }

        private async Task AbortAsync()
        {
            WebSocket? socket = _socket;
            if (socket != null)
            {
                await WebSocketText.CloseAsync(socket, TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }

            _life.Cancel();
            if (_receive != null)
            {
                try
                {
                    await _receive.ConfigureAwait(false);
                }
                catch (Exception)
                {
                }
            }

            Close("client closed");

            return;
        }

        private async Task ReceiveLoop(WebSocket socket, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? text = await WebSocketText.ReceiveAsync(socket, ct).ConfigureAwait(false);
                    if (text == null)
                    {
                        Close("socket closed");
                        return;
                    }

                    Handle(text);
                    if (_closed.Task.IsCompleted)
                    {
                        return;
                    }
                }
            }
            catch (Exception error) when (error is WebSocketException || error is IOException || error is ObjectDisposedException || error is OperationCanceledException || error is EtosException)
            {
                Close(ct.IsCancellationRequested ? "client closed" : "connection broke: " + error.Message);
            }
        }

        private void Handle(string text)
        {
            JObject frame;
            try
            {
                frame = JObject.Parse(text);
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return;
            }

            switch (Json.Str(frame, "type"))
            {
                case "ready":
                    _ready.TrySetResult(new VoiceReady(Json.Str(frame, "sessionId") ?? string.Empty, (int)(Json.Long(frame, "sampleRateHz") ?? VoiceFraming.SampleRate), (int)(Json.Long(frame, "maxChunkBytes") ?? VoiceFraming.MaxChunkBytes)));
                    break;
                case "transcript":
                    string role = Json.Str(frame, "role") ?? "user";
                    if (role != "user")
                    {
                        return;
                    }

                    VoiceTranscript transcript = new VoiceTranscript(Json.Str(frame, "itemId") ?? string.Empty, Json.Long(frame, "revision") ?? 0, Json.Str(frame, "text") ?? string.Empty, Json.Bool(frame, "final") ?? false, role, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    Transcript?.Invoke(transcript);
                    break;
                case "speech_started":
                    SpeechStarted?.Invoke(Json.Str(frame, "itemId") ?? string.Empty);
                    break;
                case "speech_ended":
                    SpeechEnded?.Invoke(Json.Str(frame, "itemId") ?? string.Empty);
                    break;
                case "usage":
                    Usage?.Invoke(frame["usage"] ?? new JObject());
                    break;
                case "error":
                    EtosError error = new EtosError(0, Json.Str(frame, "code") ?? EtosCodes.Protocol, Json.Str(frame, "message") ?? string.Empty);
                    if (_firstError == null)
                    {
                        _firstError = error;
                    }

                    Error?.Invoke(error);
                    break;
                case "closed":
                    Close(Json.Str(frame, "reason") ?? "closed");
                    break;
            }
        }

        private void Close(string reason)
        {
            reason = EtosRedaction.Redact(reason);
            if (_closed.TrySetResult(reason))
            {
                Closed?.Invoke(reason);
            }
        }
    }
}
