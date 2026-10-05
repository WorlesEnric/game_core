// GameCore.Studio.Etos - push-to-talk voice through the companion (04 s4): audio from an IPcmSource is cut into
// 100 ms PCM16 frames (<= 24 KiB, gapless seq) and streamed on /v1/voice (ticketed WebSocket); transcript revisions
// come back and are raised on the main thread. Only final user transcripts may fill the prompt box; nothing is ever
// sent as a request implicitly. Refusals (not_configured, budget_exhausted, rate_limited...) keep their etos code.
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using UnityEditor;

namespace GameCore.Studio.Etos
{
    /// <summary>One voice session.</summary>
    public sealed class EtosVoiceSession : IVoiceSession, IDisposable
    {
        private readonly CompanionClient _client;
        private readonly MainThreadQueue _queue;
        private readonly IPcmSource _source;
        private readonly Func<ProviderStatus> _status;
        private readonly IStudioLog _log;
        private readonly PcmFrameAccumulator _frames = new PcmFrameAccumulator();
        private readonly List<TranscriptUpdate> _finals = new List<TranscriptUpdate>();
        private VoiceChannel? _channel;
        private Task _sending = Task.CompletedTask;
        private bool _ticking;
        private bool _capturing;
        private bool _disposed;
        private long _lastFramesSent;
        private VoiceReady? _lastReady;
        private readonly SemaphoreSlim _lifecycle = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource _life = new CancellationTokenSource();

        public EtosVoiceSession(CompanionClient client, MainThreadQueue queue, IPcmSource source, Func<ProviderStatus> status, IStudioLog log)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _status = status ?? throw new ArgumentNullException(nameof(status));
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public event Action<TranscriptUpdate>? Transcript;

        public event Action<float>? Level;

        public event Action<Diagnostic>? Error;

        /// <summary>The source in use.</summary>
        public IPcmSource Source => _source;

        /// <summary>The companion's ready frame (null before it arrived).</summary>
        public VoiceReady? Ready => _channel?.ReadyInfo ?? _lastReady;

        public bool IsCapturing => _capturing;

        /// <summary>The last refusal or failure.</summary>
        public Diagnostic? LastError { get; private set; }

        /// <summary>The close reason (<c>stopped</c>, <c>refused</c>, <c>provider_closed</c>...), once closed.</summary>
        public string? CloseReason { get; private set; }

        public long FramesSent => _channel?.FramesSent ?? _lastFramesSent;

        /// <summary>Final transcripts received (main thread).</summary>
        public IReadOnlyList<TranscriptUpdate> Finals => _finals;

        /// <summary>The newest final text (the prompt-box candidate), or null.</summary>
        public string? FinalText => _finals.Count == 0 ? null : _finals[_finals.Count - 1].Text;

        /// <summary>Opens the session and starts capture; a refusal is raised as <see cref="Error"/> and rethrown.</summary>
        public async Task StartAsync()
        {
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_disposed) throw new ObjectDisposedException(nameof(EtosVoiceSession));
                if (_channel != null && !_channel.Completion.IsCompleted) return;
                _channel?.Dispose();
                _channel = null;
                ProviderState voice = _status().Voice;
                if (voice == ProviderState.NotConfigured || voice == ProviderState.Blocked)
                {
                    Diagnostic refused = new Diagnostic(voice == ProviderState.Blocked ? DiagnosticCodes.Blocked : EtosCodes.NotConfigured, "Voice is " + (voice == ProviderState.Blocked ? "blocked" : "not configured") + " on the node.");
                    _queue.Post(() => Raise(refused));
                    throw new EtosException(new EtosError(0, refused.Code, refused.Message));
                }

                VoiceChannel channel = new VoiceChannel(_client);
                channel.Transcript += t => _queue.Post(() => { if (ReferenceEquals(_channel, channel)) OnTranscript(t); });
                channel.Error += e => _queue.Post(() => { if (ReferenceEquals(_channel, channel)) Raise(EtosAgentGateway.DiagnosticOf(e)); });
                channel.Closed += reason => _queue.Post(() => { if (ReferenceEquals(_channel, channel)) OnClosed(reason); });
                _channel = channel;
                try
                {
                    await _queue.Run(() =>
                    {
                        _capturing = false;
                        Unhook();
                        _source.Stop();
                        LastError = null;
                        CloseReason = null;
                        _lastFramesSent = 0;
                        _lastReady = null;
                        _finals.Clear();
                        _frames.Flush();
                        _sending = Task.CompletedTask;
                        return true;
                    }).ConfigureAwait(false);
                    _lastReady = await channel.ConnectAsync(_life.Token).ConfigureAwait(false);
                    await _queue.Run(() =>
                    {
                        if (_disposed) throw new ObjectDisposedException(nameof(EtosVoiceSession));
                        _source.Start();
                        _capturing = true;
                        if (!_ticking)
                        {
                            EditorApplication.update += Tick;
                            _ticking = true;
                        }
                        _log.Write(StudioLogLevel.Info, "etos", "voice session " + channel.ReadyInfo?.SessionId + " capturing from " + _source.Name);
                        return true;
                    }).ConfigureAwait(false);
                }
                catch (Exception error)
                {
                    await _queue.Run(() =>
                    {
                        _capturing = false;
                        Unhook();
                        _source.Stop();
                        _channel = null;
                        if (error is EtosException etos) Raise(EtosAgentGateway.DiagnosticOf(etos.Error));
                        return true;
                    }).ConfigureAwait(false);
                    channel.Dispose();
                    throw;
                }
            }
            finally { _lifecycle.Release(); }
        }

        /// <summary>Main-thread tick: reads the source, raises the level, streams complete frames.</summary>
        public void Tick()
        {
            if (!_capturing || _channel == null)
            {
                return;
            }

            byte[] pcm = _source.Read();
            if (pcm.Length == 0)
            {
                return;
            }

            Level?.Invoke(VoiceFraming.Level(pcm, 0, pcm.Length));
            foreach (byte[] frame in _frames.Add(pcm, 0, pcm.Length))
            {
                Send(frame);
            }
        }

        /// <summary>Stops capture, sends the remaining audio and stop, and waits for the final transcript and close.</summary>
        public async Task StopAsync()
        {
            // Serialize release with an outstanding connect and any next take.
            await _lifecycle.WaitAsync().ConfigureAwait(false);
            VoiceChannel? channel = _channel;
            try
            {
                if (channel == null || _disposed) return;
                await _queue.Run(() =>
                {
                    Tick();
                    _capturing = false;
                    _source.Stop();
                    byte[]? rest = _frames.Flush();
                    if (rest != null) Send(rest);
                    Unhook();
                    return true;
                }).ConfigureAwait(false);
                await _sending.ConfigureAwait(false);
                string reason = await channel.StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                // This barrier follows all transcript callbacks, so StopAsync completes only after main-thread transcript delivery.
                await _queue.Run(() =>
                {
                    OnClosed(reason);
                    if (_finals.Count == 0 && LastError == null)
                        Raise(new Diagnostic("voice_no_transcript", "The voice session closed without a final user transcript (" + reason + ").", "Try another take; no request was sent."));
                    return true;
                }).ConfigureAwait(false);
            }
            finally
            {
                if (channel != null)
                {
                    _lastFramesSent = channel.FramesSent;
                    if (ReferenceEquals(_channel, channel)) _channel = null;
                    channel.Dispose();
                }
                _lifecycle.Release();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _life.Cancel();
            Unhook();
            _capturing = false;
            _source.Dispose();
            _channel?.Dispose();
            _channel = null;
        }

        private void Send(byte[] frame)
        {
            VoiceChannel channel = _channel!;
            _sending = SendAfterAsync(_sending, channel, frame);
        }

        private static async Task SendAfterAsync(Task previous, VoiceChannel channel, byte[] frame)
        {
            await previous.ConfigureAwait(false);
            await channel.SendPcmAsync(frame).ConfigureAwait(false);
            return;
        }

        private void Unhook()
        {
            if (_ticking)
            {
                EditorApplication.update -= Tick;
                _ticking = false;
            }
        }

        private void OnTranscript(VoiceTranscript transcript)
        {
            TranscriptUpdate update = new TranscriptUpdate(transcript.ItemId, transcript.Revision, transcript.Text, transcript.Final, transcript.Role);
            if (update.Final)
            {
                _finals.Add(update);
            }

            Transcript?.Invoke(update);
        }

        private void OnClosed(string reason)
        {
            if (CloseReason == null)
            {
                CloseReason = reason;
                _log.Write(StudioLogLevel.Info, "etos", "voice session closed: " + reason);
            }

            _capturing = false;
            _source.Stop();
            Unhook();
        }

        private void Raise(Diagnostic diagnostic)
        {
            LastError = diagnostic;
            _log.Write(StudioLogLevel.Warning, "etos", "voice: " + diagnostic.Code + ": " + diagnostic.Message, diagnostic);
            Error?.Invoke(diagnostic);
        }
    }
}
