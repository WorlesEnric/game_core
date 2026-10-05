// GameCore.Gameplay.Audio.Editor - procedural placeholder audio (P1.5, catalog row 11).
//
// Deterministic synthesis of placeholder clips from a few recipes, written as 16-bit mono PCM .wav files, with a
// .manifest.json that records every generator parameter and the SHA-256 of every output. Same specs give the same
// bytes, so a regenerated folder diffs clean. No downloaded assets and no provider calls.
//
//   Ambience  looping filtered noise with a slow swell, drones and periodic pulses (frogs, distant knocks)
//   Pad       looping chord with tremolo, optional pulse gating (music beds)
//   Bell      inharmonic partials with exponential decay (stingers)
//   Click     short decaying sine with a noise transient (UI clicks)
//   Footstep  low-passed noise thump with a low sine body
// Loops are seamless: every periodic component completes an integer number of cycles per loop and the noise tail is
// crossfaded into its head.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GameCore.Gameplay.Audio.Editor
{
    /// <summary>A synthesis recipe.</summary>
    public enum ProceduralRecipe
    {
        Ambience = 0,
        Pad = 1,
        Bell = 2,
        Click = 3,
        Footstep = 4,
    }

    /// <summary>The parameters of one generated clip (serialized verbatim into the manifest).</summary>
    [Serializable]
    public sealed class ProceduralClipSpec
    {
        public string id = string.Empty;
        public string file = string.Empty;
        public ProceduralRecipe recipe;
        public float seconds = 1f;
        public int seed = 1;
        public float peak = 0.5f;
        public bool loop;
        public float frequency = 440f;
        public float decay = 8f;
        public float noise;
        public float noiseCutoffHz = 800f;
        public float swellCycles = 1f;
        public float swellDepth = 0.3f;
        public float[] tones = Array.Empty<float>();
        public float toneGain = 0.3f;
        public float pulseHz;
        public int pulsesPerLoop;
        public float pulseGain;
        public float pulseDecay = 12f;

        public ProceduralClipSpec()
        {
        }

        public ProceduralClipSpec(string id, string file, ProceduralRecipe recipe, float seconds, int seed)
        {
            this.id = id;
            this.file = file;
            this.recipe = recipe;
            this.seconds = seconds;
            this.seed = seed;
            loop = recipe == ProceduralRecipe.Ambience || recipe == ProceduralRecipe.Pad;
        }
    }

    /// <summary>One written clip.</summary>
    [Serializable]
    public sealed class ProceduralOutput
    {
        public string id = string.Empty;
        public string file = string.Empty;
        public int samples;
        public int bytes;
        public string sha256 = string.Empty;
    }

    /// <summary>The manifest written next to the clips.</summary>
    [Serializable]
    public sealed class ProceduralAudioManifest
    {
        public string generator = "GameCore.Gameplay.Audio.Editor.ProceduralAudioGenerator";
        public int version = 1;
        public int sampleRate = ProceduralAudioGenerator.DefaultSampleRate;
        public int bitsPerSample = 16;
        public int channels = 1;
        public ProceduralClipSpec[] clips = Array.Empty<ProceduralClipSpec>();
        public ProceduralOutput[] outputs = Array.Empty<ProceduralOutput>();
    }

    /// <summary>Renders specs to PCM and writes .wav files plus the manifest.</summary>
    public static class ProceduralAudioGenerator
    {
        public const int DefaultSampleRate = 22050;

        /// <summary>Renders, writes every clip under <paramref name="folder"/> and writes <paramref name="manifestName"/> there.</summary>
        public static ProceduralAudioManifest Generate(string folder, string manifestName, IReadOnlyList<ProceduralClipSpec> specs, int sampleRate = DefaultSampleRate)
        {
            if (string.IsNullOrEmpty(folder))
            {
                throw new ArgumentException("an output folder is required", nameof(folder));
            }

            if (specs == null)
            {
                throw new ArgumentNullException(nameof(specs));
            }

            Directory.CreateDirectory(folder);
            var outputs = new List<ProceduralOutput>(specs.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < specs.Count; i++)
            {
                ProceduralClipSpec spec = specs[i];
                if (spec == null || spec.id.Length == 0 || spec.file.Length == 0 || !spec.file.EndsWith(".wav", StringComparison.Ordinal))
                {
                    throw new ArgumentException("spec " + i + " needs an id and a .wav file name");
                }

                if (!ids.Add(spec.id))
                {
                    throw new ArgumentException("duplicate clip id '" + spec.id + "'");
                }

                short[] pcm = Render(spec, sampleRate);
                byte[] wav = Wav(pcm, sampleRate);
                File.WriteAllBytes(Path.Combine(folder, spec.file), wav);
                outputs.Add(new ProceduralOutput { id = spec.id, file = spec.file, samples = pcm.Length, bytes = wav.Length, sha256 = Sha256(wav) });
            }

            var manifest = new ProceduralAudioManifest
            {
                sampleRate = sampleRate,
                clips = new List<ProceduralClipSpec>(specs).ToArray(),
                outputs = outputs.ToArray(),
            };
            File.WriteAllText(Path.Combine(folder, manifestName), JsonUtility.ToJson(manifest, true) + "\n", new UTF8Encoding(false));
            return manifest;
        }

        /// <summary>Renders one spec to 16-bit samples, normalized to the spec's peak.</summary>
        public static short[] Render(ProceduralClipSpec spec, int sampleRate = DefaultSampleRate)
        {
            if (spec == null)
            {
                throw new ArgumentNullException(nameof(spec));
            }

            int count = Math.Max(1, (int)Math.Round(spec.seconds * sampleRate));
            double[] signal;
            switch (spec.recipe)
            {
                case ProceduralRecipe.Ambience:
                    signal = Ambience(spec, count, sampleRate);
                    break;
                case ProceduralRecipe.Pad:
                    signal = Pad(spec, count, sampleRate);
                    break;
                case ProceduralRecipe.Bell:
                    signal = Bell(spec, count, sampleRate);
                    break;
                case ProceduralRecipe.Click:
                    signal = Click(spec, count, sampleRate);
                    break;
                default:
                    signal = Footstep(spec, count, sampleRate);
                    break;
            }

            return Normalize(signal, spec.peak);
        }

        /// <summary>A 16-bit mono PCM RIFF/WAVE file.</summary>
        public static byte[] Wav(short[] samples, int sampleRate)
        {
            using (var stream = new MemoryStream(44 + samples.Length * 2))
            using (var writer = new BinaryWriter(stream))
            {
                int data = samples.Length * 2;
                writer.Write(Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + data);
                writer.Write(Encoding.ASCII.GetBytes("WAVE"));
                writer.Write(Encoding.ASCII.GetBytes("fmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(sampleRate);
                writer.Write(sampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(Encoding.ASCII.GetBytes("data"));
                writer.Write(data);
                for (int i = 0; i < samples.Length; i++)
                {
                    writer.Write(samples[i]);
                }

                writer.Flush();
                return stream.ToArray();
            }
        }

        public static string Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(bytes);
                var text = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                {
                    text.Append(hash[i].ToString("x2", CultureInfo.InvariantCulture));
                }

                return text.ToString();
            }
        }

        // ------------------------------------------------------------------ recipes

        private static double[] Ambience(ProceduralClipSpec spec, int count, int rate)
        {
            double seconds = (double)count / rate;
            double[] noise = LoopNoise(spec.seed, count, rate, spec.noiseCutoffHz);
            double[] output = new double[count];
            double swell = Math.Max(0.0, Math.Round(spec.swellCycles));
            double[] tones = LoopFrequencies(spec.tones, seconds);
            double pulseHz = LoopFrequency(spec.pulseHz, seconds);
            double period = spec.pulsesPerLoop > 0 ? seconds / spec.pulsesPerLoop : 0.0;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double envelope = 1.0 - spec.swellDepth * 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * swell * t / seconds));
                double value = spec.noise * noise[i] * envelope;
                for (int k = 0; k < tones.Length; k++)
                {
                    value += spec.toneGain / tones.Length * Math.Sin(2.0 * Math.PI * tones[k] * t);
                }

                if (period > 0.0 && spec.pulseGain > 0f)
                {
                    double tau = t % period;
                    value += spec.pulseGain * Math.Exp(-tau * spec.pulseDecay) * Math.Sin(2.0 * Math.PI * pulseHz * t);
                }

                output[i] = value;
            }

            return output;
        }

        private static double[] Pad(ProceduralClipSpec spec, int count, int rate)
        {
            double seconds = (double)count / rate;
            double[] tones = LoopFrequencies(spec.tones.Length > 0 ? spec.tones : new[] { spec.frequency }, seconds);
            double[] noise = spec.noise > 0f ? LoopNoise(spec.seed, count, rate, spec.noiseCutoffHz) : new double[count];
            double swell = Math.Max(0.0, Math.Round(spec.swellCycles));
            double period = spec.pulsesPerLoop > 0 ? seconds / spec.pulsesPerLoop : 0.0;
            double[] output = new double[count];
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double value = 0.0;
                for (int k = 0; k < tones.Length; k++)
                {
                    double phase = 2.0 * Math.PI * tones[k] * t;
                    double tremolo = 1.0 - spec.swellDepth * 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * (swell + k) * t / seconds));
                    value += tremolo * (Math.Sin(phase) + 0.25 * Math.Sin(2.0 * phase) + 0.1 * Math.Sin(3.0 * phase)) / tones.Length;
                }

                if (period > 0.0 && spec.pulseGain > 0f)
                {
                    double tau = t % period;
                    value *= 1.0 - spec.pulseGain + spec.pulseGain * Math.Exp(-tau * spec.pulseDecay);
                }

                output[i] = value + spec.noise * noise[i];
            }

            return output;
        }

        private static double[] Bell(ProceduralClipSpec spec, int count, int rate)
        {
            double[] ratios = { 1.0, 2.0, 2.4, 3.0, 4.2, 5.4, 6.8 };
            double[] amplitudes = { 1.0, 0.6, 0.45, 0.3, 0.22, 0.15, 0.08 };
            double[] output = new double[count];
            double attack = 0.004 * rate;
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double value = 0.0;
                for (int k = 0; k < ratios.Length; k++)
                {
                    double rateK = spec.decay * (1.0 + 0.6 * ratios[k]);
                    value += amplitudes[k] * Math.Exp(-t * rateK) * Math.Sin(2.0 * Math.PI * spec.frequency * ratios[k] * t);
                }

                output[i] = value * Math.Min(1.0, i / attack);
            }

            return output;
        }

        private static double[] Click(ProceduralClipSpec spec, int count, int rate)
        {
            uint state = Seed(spec.seed);
            double[] output = new double[count];
            int transient = Math.Max(1, (int)(0.003 * rate));
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                double value = Math.Exp(-t * spec.decay) * Math.Sin(2.0 * Math.PI * spec.frequency * t);
                if (i < transient)
                {
                    value += spec.noise * NextNoise(ref state) * (1.0 - (double)i / transient);
                }

                output[i] = value * FadeOut(i, count, rate);
            }

            return output;
        }

        private static double[] Footstep(ProceduralClipSpec spec, int count, int rate)
        {
            uint state = Seed(spec.seed);
            double alpha = LowPassAlpha(spec.noiseCutoffHz, rate);
            double filtered = 0.0;
            double[] output = new double[count];
            for (int i = 0; i < count; i++)
            {
                double t = (double)i / rate;
                filtered += alpha * (NextNoise(ref state) - filtered);
                double body = 0.6 * Math.Exp(-t * spec.decay * 1.5) * Math.Sin(2.0 * Math.PI * spec.frequency * t);
                output[i] = (spec.noise * filtered * Math.Exp(-t * spec.decay) + body) * Math.Min(1.0, i / (0.002 * rate)) * FadeOut(i, count, rate);
            }

            return output;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Low-passed noise of <paramref name="count"/> samples whose end crossfades into its start.</summary>
        private static double[] LoopNoise(int seed, int count, int rate, float cutoffHz)
        {
            int fade = Math.Max(1, Math.Min(count / 4, rate / 2));
            uint state = Seed(seed);
            double alpha = LowPassAlpha(cutoffHz, rate);
            double filtered = 0.0;
            var raw = new double[count + fade];
            for (int i = 0; i < 2048; i++)
            {
                filtered += alpha * (NextNoise(ref state) - filtered);
            }

            for (int i = 0; i < raw.Length; i++)
            {
                filtered += alpha * (NextNoise(ref state) - filtered);
                raw[i] = filtered;
            }

            var loop = new double[count];
            for (int i = 0; i < count; i++)
            {
                if (i < fade)
                {
                    double w = (double)i / fade;
                    loop[i] = raw[i] * Math.Sqrt(w) + raw[count + i] * Math.Sqrt(1.0 - w);
                }
                else
                {
                    loop[i] = raw[i];
                }
            }

            return loop;
        }

        private static double[] LoopFrequencies(float[] frequencies, double seconds)
        {
            var result = new double[frequencies != null ? frequencies.Length : 0];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = LoopFrequency(frequencies![i], seconds);
            }

            return result;
        }

        /// <summary>The closest frequency that completes an integer number of cycles in <paramref name="seconds"/>.</summary>
        private static double LoopFrequency(float frequency, double seconds) =>
            frequency <= 0f ? 0.0 : Math.Max(1.0, Math.Round(frequency * seconds)) / seconds;

        private static double LowPassAlpha(float cutoffHz, int rate) =>
            1.0 - Math.Exp(-2.0 * Math.PI * Math.Max(10.0, cutoffHz) / rate);

        private static double FadeOut(int i, int count, int rate)
        {
            int tail = Math.Max(1, (int)(0.004 * rate));
            int remaining = count - 1 - i;
            return remaining >= tail ? 1.0 : (double)remaining / tail;
        }

        private static uint Seed(int seed)
        {
            uint state = (uint)seed * 2654435761U;
            return state == 0U ? 0x9E3779B9U : state;
        }

        /// <summary>xorshift32 white noise in [-1, 1].</summary>
        private static double NextNoise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state / 2147483647.5 - 1.0;
        }

        private static short[] Normalize(double[] signal, float peak)
        {
            double max = 0.0;
            for (int i = 0; i < signal.Length; i++)
            {
                max = Math.Max(max, Math.Abs(signal[i]));
            }

            double scale = max > 0.0 ? Math.Max(0.0, Math.Min(1.0, peak)) * 32767.0 / max : 0.0;
            var pcm = new short[signal.Length];
            for (int i = 0; i < signal.Length; i++)
            {
                pcm[i] = (short)Math.Round(signal[i] * scale);
            }

            return pcm;
        }
    }
}
