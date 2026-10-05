// GameCore.Studio.Etos - audio sources for voice sessions (04 s4): the Editor microphone (UnityEngine.Microphone, any
// rate the device offers, down-mixed and resampled to 24 kHz mono PCM16) and a WAV played in real time (tests and the
// live check without a microphone). Both are read on the main thread by the session's update tick.
#nullable enable
using System;
using System.Diagnostics;
using GameCore.Studio.Etos.Client;
using UnityEngine;

namespace GameCore.Studio.Etos
{
    /// <summary>24 kHz mono PCM16 audio, read incrementally on the main thread.</summary>
    public interface IPcmSource : IDisposable
    {
        string Name { get; }

        /// <summary>True once a finite source delivered everything (a microphone never finishes).</summary>
        bool Finished { get; }

        void Start();

        void Stop();

        /// <summary>The PCM16 captured since the previous call (possibly empty).</summary>
        byte[] Read();
    }

    /// <summary>The Editor microphone.</summary>
    public sealed class MicrophoneCapture : IPcmSource
    {
        private readonly string? _device;
        private AudioClip? _clip;
        private int _last;
        private int _rate;

        /// <param name="device">A name from <c>Microphone.devices</c>; null for the default device.</param>
        public MicrophoneCapture(string? device = null)
        {
            _device = string.IsNullOrEmpty(device) ? null : device;
        }

        public string Name => "microphone " + (_device ?? "(default)") + (_rate > 0 ? " @ " + _rate + " Hz" : string.Empty);

        public bool Finished => false;

        /// <summary>The device rate in use (resampled to 24 kHz).</summary>
        public int DeviceRate => _rate;

        public bool IsRecording => _clip != null && Microphone.IsRecording(_device);

        public void Start()
        {
            if (Microphone.devices.Length == 0)
            {
                throw new EtosException(new EtosError(0, EtosCodes.NotConfigured, "No microphone is available to the Editor.", "Connect a capture device (on Linux a PipeWire/PulseAudio source)."));
            }

            Microphone.GetDeviceCaps(_device, out int min, out int max);
            _rate = min == 0 && max == 0 ? VoiceFraming.SampleRate : Mathf.Clamp(VoiceFraming.SampleRate, min, max);
            _clip = Microphone.Start(_device, true, 4, _rate);
            if (_clip == null)
            {
                throw new EtosException(new EtosError(0, EtosCodes.Transport, "The microphone did not start (" + (_device ?? "default device") + ")."));
            }

            _last = 0;
        }

        public void Stop()
        {
            if (_clip != null)
            {
                Microphone.End(_device);
                UnityEngine.Object.DestroyImmediate(_clip);
                _clip = null;
            }
        }

        public byte[] Read()
        {
            AudioClip? clip = _clip;
            if (clip == null)
            {
                return Array.Empty<byte>();
            }

            int position = Microphone.GetPosition(_device);
            if (position < 0 || position == _last)
            {
                return Array.Empty<byte>();
            }

            int total = clip.samples;
            int count = position - _last;
            if (count < 0)
            {
                count += total;
            }

            int channels = Math.Max(1, clip.channels);
            float[] interleaved = new float[count * channels];
            int first = Math.Min(count, total - _last);
            float[] head = new float[first * channels];
            clip.GetData(head, _last);
            Array.Copy(head, interleaved, head.Length);
            if (first < count)
            {
                float[] tail = new float[(count - first) * channels];
                clip.GetData(tail, 0);
                Array.Copy(tail, 0, interleaved, head.Length, tail.Length);
            }

            _last = position;
            float[] mono = channels == 1 ? interleaved : DownMix(interleaved, channels);
            float[] resampled = _rate == VoiceFraming.SampleRate ? mono : VoiceFraming.Resample(mono, mono.Length, _rate, VoiceFraming.SampleRate);
            return VoiceFraming.ToPcm16(resampled, 0, resampled.Length);
        }

        public void Dispose() => Stop();

        private static float[] DownMix(float[] interleaved, int channels)
        {
            float[] mono = new float[interleaved.Length / channels];
            for (int i = 0; i < mono.Length; i++)
            {
                float sum = 0;
                for (int c = 0; c < channels; c++)
                {
                    sum += interleaved[(i * channels) + c];
                }

                mono[i] = sum / channels;
            }

            return mono;
        }
    }

    /// <summary>A WAV (any rate, mono or stereo PCM16) delivered at real-time pace, then optional trailing silence.</summary>
    public sealed class WavPcmSource : IPcmSource
    {
        private readonly byte[] _pcm;
        private readonly Stopwatch _clock = new Stopwatch();
        private int _delivered;

        public WavPcmSource(byte[] wav, double trailingSilenceSeconds = 1.5, double speed = 1.0)
        {
            byte[] speech = VoiceFraming.WavToPcm16Mono24k(wav ?? throw new ArgumentNullException(nameof(wav)));
            int silence = (int)(trailingSilenceSeconds * VoiceFraming.SampleRate) * 2;
            _pcm = new byte[speech.Length + silence];
            Buffer.BlockCopy(speech, 0, _pcm, 0, speech.Length);
            Speed = speed <= 0 ? 1.0 : speed;
            SpeechBytes = speech.Length;
        }

        public string Name => "wav (" + (SpeechBytes / 2.0 / VoiceFraming.SampleRate).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s)";

        public double Speed { get; }

        public int SpeechBytes { get; }

        public int TotalBytes => _pcm.Length;

        public bool Finished => _delivered >= _pcm.Length;

        public void Start() => _clock.Restart();

        public void Stop() => _clock.Stop();

        public byte[] Read()
        {
            if (!_clock.IsRunning)
            {
                return Array.Empty<byte>();
            }

            long due = (long)(_clock.Elapsed.TotalSeconds * Speed * VoiceFraming.SampleRate) * 2;
            int until = (int)Math.Min(_pcm.Length, due);
            if (until <= _delivered)
            {
                return Array.Empty<byte>();
            }

            byte[] chunk = new byte[until - _delivered];
            Buffer.BlockCopy(_pcm, _delivered, chunk, 0, chunk.Length);
            _delivered = until;
            return chunk;
        }

        public void Dispose() => _clock.Stop();
    }
}
