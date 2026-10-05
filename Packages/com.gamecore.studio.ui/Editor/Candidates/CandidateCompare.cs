// GameCore.Studio.UI - Compare data of a candidate (03 s5 preview.compare from the UI's side): a side-by-side property
// diff for `set`/`assign` operations (current value read from the resolved target through the authoring metadata, the
// proposed value from the operation), image artifacts decoded for before/after/diff display, and audio artifacts
// decoded from WAV for playback through a hidden AudioSource. Pure reads: nothing is written.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Studio.Authoring;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.UI
{
    /// <summary>One row of the property diff.</summary>
    public sealed class PropertyDiffRow
    {
        public PropertyDiffRow(string opId, string target, string field, string current, string proposed, bool resolved)
        {
            OpId = opId;
            Target = target;
            Field = field;
            Current = current;
            Proposed = proposed;
            Resolved = resolved;
        }

        public string OpId { get; }

        public string Target { get; }

        public string Field { get; }

        /// <summary>The value now ("(unresolved)" when the target does not resolve).</summary>
        public string Current { get; }

        public string Proposed { get; }

        public bool Resolved { get; }

        public bool Changed => !string.Equals(Current, Proposed, StringComparison.Ordinal);
    }

    /// <summary>Compare helpers.</summary>
    public static class CandidateCompare
    {
        /// <summary>The property diff of every <c>set</c>/<c>assign</c> operation.</summary>
        public static IReadOnlyList<PropertyDiffRow> PropertyDiff(StudioRuntime runtime, ChangeSet changeSet)
        {
            List<PropertyDiffRow> rows = new List<PropertyDiffRow>();
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.Tool != BuiltInToolIdsExt.Set && operation.Tool != BuiltInToolIdsExt.Assign)
                {
                    continue;
                }

                UnityEngine.Object? target = operation.Target == null ? null : runtime.Resolver.Find(operation.Target);
                string label = target != null ? target.name : operation.Target?.Path ?? operation.Target?.AuthoringId ?? "(no target)";
                JObject args = operation.Args ?? new JObject();
                List<KeyValuePair<string, JToken?>> fields = new List<KeyValuePair<string, JToken?>>();
                if (args["fields"] is JObject many)
                {
                    foreach (JProperty property in many.Properties())
                    {
                        fields.Add(new KeyValuePair<string, JToken?>(property.Name, property.Value));
                    }
                }
                else if (args["field"] != null && args["field"]!.Type == JTokenType.String)
                {
                    fields.Add(new KeyValuePair<string, JToken?>(args["field"]!.Value<string>()!, args["value"]));
                }

                foreach (KeyValuePair<string, JToken?> field in fields)
                {
                    string current = "(unresolved)";
                    bool resolved = false;
                    if (target != null)
                    {
                        AuthorMemberInfo? member = runtime.Identity.Describe(target)?.FindMember(field.Key);
                        if (member != null)
                        {
                            current = Text(runtime.Resolver.Codec.FromClr(member.GetValue(target)));
                            resolved = true;
                        }
                        else
                        {
                            current = "(no authorable field '" + field.Key + "')";
                        }
                    }

                    rows.Add(new PropertyDiffRow(operation.OpId, label, field.Key, current, Text(field.Value), resolved));
                }
            }

            return rows;
        }

        /// <summary>Compact text of a JSON value.</summary>
        public static string Text(JToken? value)
        {
            if (value == null || value.Type == JTokenType.Null)
            {
                return "null";
            }

            if (value.Type == JTokenType.String)
            {
                return value.Value<string>() ?? string.Empty;
            }

            return value.ToString(Formatting.None);
        }

        /// <summary>The artifacts of a change set whose media type is an image.</summary>
        public static IReadOnlyList<ArtifactRef> Images(ChangeSet changeSet) => ArtifactsOf(changeSet, "image/");

        /// <summary>The artifacts of a change set whose media type is audio.</summary>
        public static IReadOnlyList<ArtifactRef> Audio(ChangeSet changeSet) => ArtifactsOf(changeSet, "audio/");

        /// <summary>
        /// The texture an operation would replace with <paramref name="artifact"/> (an <c>assign</c>/<c>set</c> whose value
        /// names the artifact, on a target field currently holding a texture), or null.
        /// </summary>
        public static Texture? CurrentTextureFor(StudioRuntime runtime, ChangeSet changeSet, ArtifactRef artifact)
        {
            foreach (Operation operation in changeSet.Operations)
            {
                if (operation.Target == null || operation.Args == null)
                {
                    continue;
                }

                string? field = operation.Args["field"]?.Type == JTokenType.String ? operation.Args["field"]!.Value<string>() : null;
                string value = operation.Args["value"]?.ToString(Formatting.None) ?? string.Empty;
                if (field == null || value.IndexOf(artifact.Sha256, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                UnityEngine.Object? target = runtime.Resolver.Find(operation.Target);
                if (target == null)
                {
                    continue;
                }

                AuthorMemberInfo? member = runtime.Identity.Describe(target)?.FindMember(field);
                if (member?.GetValue(target) is Texture texture)
                {
                    return texture;
                }

                if (target is Renderer renderer && renderer.sharedMaterial != null && renderer.sharedMaterial.mainTexture != null)
                {
                    return renderer.sharedMaterial.mainTexture;
                }
            }

            return null;
        }

        /// <summary>Decodes a retained image artifact (the caller destroys the texture).</summary>
        public static Texture2D? LoadImage(StudioRuntime runtime, string sha256)
        {
            if (!runtime.Artifacts.Has(sha256))
            {
                return null;
            }

            byte[] bytes = runtime.Artifacts.Read(sha256);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "GameCoreStudio.Compare." + sha256.Substring(0, 8) };
            if (!ImageConversion.LoadImage(texture, bytes, false))
            {
                UnityEngine.Object.DestroyImmediate(texture);
                return null;
            }

            return texture;
        }

        /// <summary>
        /// An absolute-difference image of two readable textures of the same size, or null with a reason (the caller
        /// destroys the texture).
        /// </summary>
        public static Texture2D? Difference(Texture before, Texture2D after, out string? reason)
        {
            reason = null;
            if (!(before is Texture2D source) || !source.isReadable)
            {
                reason = "The current texture is not readable; the diff image needs Read/Write enabled.";
                return null;
            }

            if (source.width != after.width || source.height != after.height)
            {
                reason = "Sizes differ (" + source.width + "x" + source.height + " vs " + after.width + "x" + after.height + "); no pixel diff.";
                return null;
            }

            Color32[] left = source.GetPixels32();
            Color32[] right = after.GetPixels32();
            Color32[] diff = new Color32[left.Length];
            for (int i = 0; i < left.Length; i++)
            {
                diff[i] = new Color32(
                    (byte)Math.Abs(left[i].r - right[i].r),
                    (byte)Math.Abs(left[i].g - right[i].g),
                    (byte)Math.Abs(left[i].b - right[i].b),
                    255);
            }

            Texture2D result = new Texture2D(after.width, after.height, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, name = "GameCoreStudio.Compare.Diff" };
            result.SetPixels32(diff);
            result.Apply(false, false);
            return result;
        }

        /// <summary>Decodes a retained WAV artifact (PCM 8/16/24/32-bit integer or 32-bit float) into an AudioClip, or null with a reason.</summary>
        public static AudioClip? LoadAudio(StudioRuntime runtime, ArtifactRef artifact, out string? reason)
        {
            reason = null;
            if (!runtime.Artifacts.Has(artifact.Sha256))
            {
                reason = "The artifact is not retained.";
                return null;
            }

            return DecodeWav(runtime.Artifacts.Read(artifact.Sha256), artifact.Name ?? artifact.Sha256.Substring(0, 8), out reason);
        }

        /// <summary>A minimal RIFF/WAVE decoder (fmt + data chunks).</summary>
        public static AudioClip? DecodeWav(byte[] bytes, string name, out string? reason)
        {
            reason = null;
            if (bytes.Length < 44 || bytes[0] != 'R' || bytes[1] != 'I' || bytes[2] != 'F' || bytes[3] != 'F' || bytes[8] != 'W' || bytes[9] != 'A' || bytes[10] != 'V' || bytes[11] != 'E')
            {
                reason = "Not a RIFF/WAVE file.";
                return null;
            }

            int format = 0;
            int channels = 0;
            int rate = 0;
            int bits = 0;
            int dataOffset = -1;
            int dataLength = 0;
            int position = 12;
            while (position + 8 <= bytes.Length)
            {
                string chunk = System.Text.Encoding.ASCII.GetString(bytes, position, 4);
                int length = BitConverter.ToInt32(bytes, position + 4);
                int body = position + 8;
                if (length < 0 || body + Math.Min(length, bytes.Length - body) > bytes.Length)
                {
                    break;
                }

                if (chunk == "fmt " && length >= 16)
                {
                    format = BitConverter.ToInt16(bytes, body);
                    channels = BitConverter.ToInt16(bytes, body + 2);
                    rate = BitConverter.ToInt32(bytes, body + 4);
                    bits = BitConverter.ToInt16(bytes, body + 14);
                }
                else if (chunk == "data")
                {
                    dataOffset = body;
                    dataLength = Math.Min(length, bytes.Length - body);
                    break;
                }

                position = body + length + (length & 1);
            }

            bool integer = format == 1 || format == -2;
            bool floating = format == 3;
            if (dataOffset < 0 || channels <= 0 || rate <= 0 || (!integer && !floating) || (floating && bits != 32) || (integer && bits != 8 && bits != 16 && bits != 24 && bits != 32))
            {
                reason = "Unsupported WAVE encoding (format " + format + ", " + bits + " bits, " + channels + " channels).";
                return null;
            }

            int bytesPerSample = bits / 8;
            int samples = dataLength / bytesPerSample;
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                int at = dataOffset + (i * bytesPerSample);
                if (floating)
                {
                    data[i] = BitConverter.ToSingle(bytes, at);
                }
                else if (bits == 8)
                {
                    data[i] = (bytes[at] - 128) / 128f;
                }
                else if (bits == 16)
                {
                    data[i] = BitConverter.ToInt16(bytes, at) / 32768f;
                }
                else if (bits == 24)
                {
                    int value = bytes[at] | (bytes[at + 1] << 8) | ((sbyte)bytes[at + 2] << 16);
                    data[i] = value / 8388608f;
                }
                else
                {
                    data[i] = BitConverter.ToInt32(bytes, at) / 2147483648f;
                }
            }

            int frames = samples / channels;
            if (frames <= 0)
            {
                reason = "The WAVE data chunk is empty.";
                return null;
            }

            AudioClip clip = AudioClip.Create("GameCoreStudio.Compare." + name, frames, channels, rate, false);
            clip.hideFlags = HideFlags.HideAndDontSave;
            clip.SetData(data, 0);
            return clip;
        }

        private static IReadOnlyList<ArtifactRef> ArtifactsOf(ChangeSet changeSet, string mediaPrefix)
        {
            List<ArtifactRef> result = new List<ArtifactRef>();
            if (changeSet.Artifacts == null)
            {
                return result;
            }

            foreach (ArtifactRef artifact in changeSet.Artifacts)
            {
                if (artifact.MediaType.StartsWith(mediaPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(artifact);
                }
            }

            return result;
        }
    }

    /// <summary>Plays clips through one hidden AudioSource (no AudioUtil reflection).</summary>
    public sealed class EditorAudioPlayer : IDisposable
    {
        private GameObject? _host;
        private AudioSource? _source;

        public bool IsPlaying => _source != null && _source.isPlaying;

        public void Play(AudioClip clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            if (_source == null)
            {
                _host = new GameObject("GameCoreStudio.AudioPreview") { hideFlags = HideFlags.HideAndDontSave };
                _source = _host.AddComponent<AudioSource>();
                _source.playOnAwake = false;
                _source.spatialBlend = 0f;
            }

            _source.Stop();
            _source.clip = clip;
            _source.Play();
        }

        public void Stop()
        {
            if (_source != null)
            {
                _source.Stop();
            }
        }

        public void Dispose()
        {
            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
            }

            _host = null;
            _source = null;
        }
    }
}
