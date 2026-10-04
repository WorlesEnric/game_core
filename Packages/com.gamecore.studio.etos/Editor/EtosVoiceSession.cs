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
        public VoiceReady? Ready => _channel?.ReadyInfo;

        public bool IsCapturing => _capturing;

        /// <summary>The last refusal or failure.</summary>
        public Diagnostic? LastError { get; private set; }

        /// <summary>The close reason (<c>stopped</c>, <c>refused</c>, <c>provider_closed</c>...), once closed.</summary>
        public string? CloseReason { get; private set; }

        public long FramesSent => _channel?.FramesSent ?? 0;

        /// <summary>Final transcripts received (main thread).</summary>
        public IReadOnlyList<TranscriptUpdate> Finals => _finals;

        /// <summary>The newest final text (the prompt-box candidate), or null.</summary>
        public string? FinalText => _finals.Count == 0 ? null : _finals[_finals.Count - 1].Text;

        /// <summary>Opens the session and starts capture; a refusal is raised as <see cref="Error"/> and rethrown.</summary>
        public async Task StartAsync()
        {
            if (_channel != null)
            {
                return;
            }

            ProviderState voice = _status().Voice;
            if (voice == ProviderState.NotConfigured || voice == ProviderState.Blocked)
            {
                Diagnostic refused = new Diagnostic(voice == ProviderState.Blocked ? DiagnosticCodes.Blocked : EtosCodes.NotConfigured, "Voice is " + (voice == ProviderState.Blocked ? "blocked" : "not configured") + " on the node.");
                Raise(refused);
                throw new EtosException(new EtosError(0, refused.Code, refused.Message));
            }

            VoiceChannel channel = new VoiceChannel(_client);
            channel.Transcript += t => _queue.Post(() => OnTranscript(t));
            channel.Error += e => _queue.Post(() => Raise(EtosAgentGateway.DiagnosticOf(e)));
            channel.Closed += reason => _queue.Post(() => OnClosed(reason));
            _channel = channel;
            try
            {
                await channel.ConnectAsync().ConfigureAwait(false);
            }
            catch (EtosException error)
            {
                _channel = null;
                channel.Dispose();
                Diagnostic diagnostic = EtosAgentGateway.DiagnosticOf(error.Error);
                _queue.Post(() => Raise(diagnostic));
                throw;
            }

            await _queue.Run(() =>
            {
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
            VoiceChannel? channel = _channel;
            if (channel == null)
            {
                return;
            }

            await _queue.Run(() =>
            {
                Tick();
                _capturing = false;
                _source.Stop();
                byte[]? rest = _frames.Flush();
                if (rest != null)
                {
                    Send(rest);
                }

                Unhook();
                return true;
            }).ConfigureAwait(false);
            try
            {
                await _sending.ConfigureAwait(false);
            }
            catch (EtosException error)
            {
                Diagnostic diagnostic = EtosAgentGateway.DiagnosticOf(error.Error);
                _queue.Post(() => Raise(diagnostic));
            }

            string reason = await channel.StopAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            _queue.Post(() => OnClosed(reason));
        }

        public void Dispose()
        {
            Unhook();
            _capturing = false;
            _source.Dispose();
            _channel?.Dispose();
        }

        private void Send(byte[] frame)
        {
            VoiceChannel channel = _channel!;
            _sending = _sending.ContinueWith(_ => channel.SendPcmAsync(frame), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
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
