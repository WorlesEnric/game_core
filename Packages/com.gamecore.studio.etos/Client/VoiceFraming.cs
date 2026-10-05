// GameCore.Studio.Etos.Client - voice audio framing (04 s2 WS /v1/voice, s5; W-VOICE-01). The companion takes PCM16
// little-endian mono at 24 kHz in JSON text frames {"type":"audio","seq":n,"pcm16":"<base64>"} of at most 24 KiB raw
// (32 KiB base64); a larger frame is refused with too_large and not forwarded, and seq must be gapless (audio_gap).
// Capture produces float samples at the device rate; these helpers convert, resample, measure level and pack frames.
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos.Client
{
    /// <summary>PCM16 conversion and frame packing.</summary>
    public static class VoiceFraming
    {
        /// <summary>Sample rate the companion and the realtime provider expect.</summary>
        public const int SampleRate = 24000;

        /// <summary>Largest raw PCM16 payload per frame: 24 KiB (exactly 32 KiB of base64).</summary>
        public const int MaxChunkBytes = 24 * 1024;

        /// <summary>The frame size capture uses: 100 ms of 24 kHz mono PCM16.</summary>
        public const int CaptureFrameBytes = SampleRate / 10 * 2;

        /// <summary>Float samples in [-1, 1] to PCM16 little-endian bytes (clamped).</summary>
        public static byte[] ToPcm16(float[] samples, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > samples.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            byte[] bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                float s = samples[offset + i];
                if (s > 1f)
                {
                    s = 1f;
                }
                else if (s < -1f)
                {
                    s = -1f;
                }

                short v = (short)Math.Round(s * 32767f);
                bytes[i * 2] = (byte)(v & 0xff);
                bytes[i * 2 + 1] = (byte)((v >> 8) & 0xff);
            }

            return bytes;
        }

        /// <summary>RMS level in [0, 1] of PCM16 little-endian bytes.</summary>
        public static float Level(byte[] pcm16, int offset, int count)
        {
            int samples = count / 2;
            if (samples == 0)
            {
                return 0f;
            }

            double sum = 0;
            for (int i = 0; i < samples; i++)
            {
                short v = (short)(pcm16[offset + i * 2] | (pcm16[offset + i * 2 + 1] << 8));
                double f = v / 32768.0;
                sum += f * f;
            }

            return (float)Math.Min(1.0, Math.Sqrt(sum / samples));
        }

        /// <summary>Linear resampling of mono float samples from <paramref name="fromRate"/> to <paramref name="toRate"/>.</summary>
        public static float[] Resample(float[] input, int count, int fromRate, int toRate)
        {
            if (fromRate <= 0 || toRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fromRate));
            }

            if (fromRate == toRate)
            {
                float[] copy = new float[count];
                Array.Copy(input, copy, count);
                return copy;
            }

            int outCount = (int)((long)count * toRate / fromRate);
            float[] output = new float[outCount];
            double step = (double)fromRate / toRate;
            for (int i = 0; i < outCount; i++)
            {
                double position = i * step;
                int index = (int)position;
                double frac = position - index;
                float a = input[Math.Min(index, count - 1)];
                float b = input[Math.Min(index + 1, count - 1)];
                output[i] = (float)(a + (b - a) * frac);
            }

            return output;
        }

        /// <summary>
        /// Splits <paramref name="pcm16"/> into audio frames of at most <paramref name="maxChunkBytes"/> (even) bytes and
        /// returns their JSON texts, numbering them from <paramref name="seq"/> (advanced past the last frame).
        /// </summary>
        public static List<string> Frames(byte[] pcm16, ref long seq, int maxChunkBytes = MaxChunkBytes)
        {
            if (pcm16 == null)
            {
                throw new ArgumentNullException(nameof(pcm16));
            }

            if (pcm16.Length % 2 != 0)
            {
                throw new ArgumentException("PCM16 audio has an even number of bytes.", nameof(pcm16));
            }

            int chunk = Math.Min(maxChunkBytes, MaxChunkBytes);
            chunk -= chunk % 2;
            if (chunk <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxChunkBytes));
            }

            List<string> frames = new List<string>();
            for (int offset = 0; offset < pcm16.Length; offset += chunk)
            {
                int length = Math.Min(chunk, pcm16.Length - offset);
                JObject frame = new JObject
                {
                    ["type"] = "audio",
                    ["seq"] = seq,
                    ["pcm16"] = Convert.ToBase64String(pcm16, offset, length),
                };
                frames.Add(frame.ToString(Newtonsoft.Json.Formatting.None));
                seq++;
            }

            return frames;
        }

        /// <summary>The stop frame.</summary>
        public static string StopFrame => "{\"type\":\"stop\"}";

        /// <summary>
        /// Reads a RIFF/WAVE file with PCM16 (any channel count, any rate) and returns 24 kHz mono PCM16 (channels are
        /// averaged, then linearly resampled).
        /// </summary>
        public static byte[] WavToPcm16Mono24k(byte[] wav)
        {
            using (MemoryStream stream = new MemoryStream(wav))
            using (BinaryReader reader = new BinaryReader(stream))
            {
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
                {
                    throw new InvalidDataException("Not a RIFF file.");
                }

                reader.ReadInt32();
                if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
                {
                    throw new InvalidDataException("Not a WAVE file.");
                }

                int channels = 0;
                int rate = 0;
                int bits = 0;
                byte[]? data = null;
                while (stream.Position + 8 <= stream.Length)
                {
                    string id = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    int size = reader.ReadInt32();
                    long next = stream.Position + size + (size % 2);
                    if (id == "fmt ")
                    {
                        short format = reader.ReadInt16();
                        channels = reader.ReadInt16();
                        rate = reader.ReadInt32();
                        reader.ReadInt32();
                        reader.ReadInt16();
                        bits = reader.ReadInt16();
                        if (format != 1 && format != -2)
                        {
                            throw new InvalidDataException("Only PCM WAV files are supported (format " + format + ").");
                        }
                    }
                    else if (id == "data")
                    {
                        int available = (int)Math.Min(size, stream.Length - stream.Position);
                        data = reader.ReadBytes(available);
                    }

                    if (next > stream.Length)
                    {
                        break;
                    }

                    stream.Position = next;
                }

                if (data == null || channels <= 0 || rate <= 0 || bits != 16)
                {
                    throw new InvalidDataException("A 16-bit PCM WAV with a fmt and a data chunk is required.");
                }

                int frames = data.Length / (2 * channels);
                float[] mono = new float[frames];
                for (int i = 0; i < frames; i++)
                {
                    float sum = 0f;
                    for (int c = 0; c < channels; c++)
                    {
                        int at = (i * channels + c) * 2;
                        short v = (short)(data[at] | (data[at + 1] << 8));
                        sum += v / 32768f;
                    }

                    mono[i] = sum / channels;
                }

                float[] resampled = Resample(mono, mono.Length, rate, SampleRate);
                return ToPcm16(resampled, 0, resampled.Length);
            }
        }
    }

    /// <summary>
    /// Accumulates PCM16 bytes from capture and hands out fixed-size frames (default 100 ms), so frames are gapless and
    /// never exceed the companion's cap. Not thread-safe: one producer.
    /// </summary>
    public sealed class PcmFrameAccumulator
    {
        private readonly byte[] _buffer;
        private int _count;

        public PcmFrameAccumulator(int frameBytes = VoiceFraming.CaptureFrameBytes)
        {
            if (frameBytes <= 0 || frameBytes % 2 != 0 || frameBytes > VoiceFraming.MaxChunkBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(frameBytes));
            }

            FrameBytes = frameBytes;
            _buffer = new byte[frameBytes];
        }

        public int FrameBytes { get; }

        /// <summary>Bytes waiting for a full frame.</summary>
        public int Pending => _count;

        /// <summary>Adds bytes; returns every frame completed by them.</summary>
        public List<byte[]> Add(byte[] bytes, int offset, int count)
        {
            List<byte[]> frames = new List<byte[]>();
            while (count > 0)
            {
                int take = Math.Min(count, FrameBytes - _count);
                Buffer.BlockCopy(bytes, offset, _buffer, _count, take);
                _count += take;
                offset += take;
                count -= take;
                if (_count == FrameBytes)
                {
                    byte[] frame = new byte[FrameBytes];
                    Buffer.BlockCopy(_buffer, 0, frame, 0, FrameBytes);
                    frames.Add(frame);
                    _count = 0;
                }
            }

            return frames;
        }

        /// <summary>The partial frame left (even length), or null when nothing is pending.</summary>
        public byte[]? Flush()
        {
            int even = _count - (_count % 2);
            if (even == 0)
            {
                _count = 0;
                return null;
            }

            byte[] rest = new byte[even];
            Buffer.BlockCopy(_buffer, 0, rest, 0, even);
            _count = 0;
            return rest;
        }
    }
}
