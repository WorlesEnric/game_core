// GameCore.Studio.Etos - gameplay IMediaGenerationGateway over IAgentGateway.GenerateAsync. image, tts and describe run through the companion
// (POST /v1/ops/generate, max_cost_usd always sent); the verified bytes are retained in Studio/Artifacts and imported by
// a journaled change set of one asset.import operation, so the import is validated, undoable and re-hashed on disk.
// generate.3d goes to the companion too and its refusal is passed through untouched (blocked/not_configured until a
// predictions credential exists, SADR-020). Sound effects have no companion op and are refused as not_configured.
#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Gameplay.Contracts.Narrative;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace GameCore.Studio.Etos
{
    /// <summary>The outcome of a generate-and-import.</summary>
    public sealed class MediaImport
    {
        public MediaImport(OpResult result, string? assetPath, ArtifactRef? artifact, ApplyReport? report, IReadOnlyList<Diagnostic> diagnostics)
        {
            Result = result;
            AssetPath = assetPath;
            Artifact = artifact;
            Report = report;
            Diagnostics = diagnostics;
        }

        public OpResult Result { get; }

        public string? AssetPath { get; }

        /// <summary>The imported artifact (after a Studio-side resize, the resized bytes).</summary>
        public ArtifactRef? Artifact { get; }

        public ApplyReport? Report { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }

        public bool Ok => Result.Succeeded && Report != null && Report.Ok;

        /// <summary>The refusal or the first problem, or null.</summary>
        public Diagnostic? Problem => Result.Refusal ?? Diagnostics.FirstOrDefault();
    }

    /// <summary>Generates media through the companion and imports it into the project.</summary>
    public sealed class EtosMediaGenerator : IMediaGenerationGateway
    {
        private readonly Authoring.Agent.IAgentGateway? _gateway;
        private readonly StudioRuntime? _runtime;
        private readonly MainThreadQueue? _queue;

        /// <summary>Discovered by gameplay tools; resolves the current session on each request, including after reload.</summary>
        public EtosMediaGenerator() { }

        private Authoring.Agent.IAgentGateway Gateway => _gateway ?? throw NotConfigured();
        private StudioRuntime Runtime => _runtime ?? throw NotConfigured();
        private MainThreadQueue Queue => _queue ?? throw NotConfigured();
        private static EtosException NotConfigured() => new EtosException(new EtosError(0, EtosCodes.NotConfigured,
            "Direct media APIs require a configured gateway; the discovered adapter exposes RequestVoiceLine."));

        public Task<MediaImport>? PendingVoice { get; private set; }

        public MediaGenerationResult RequestVoiceLine(VoiceGenerationRequest request)
        {
            if (_gateway == null)
            {
                EtosAgentGateway? gateway = EtosStudioSession.Gateway;
                if (gateway == null) return new MediaGenerationResult(MediaGenerationStatus.NotConfigured, "", "not_configured: no ETOS session");
                var adapter = new EtosMediaGenerator(gateway, gateway.Runtime, gateway.Queue);
                MediaGenerationResult result = adapter.RequestVoiceLine(request);
                PendingVoice = adapter.PendingVoice;
                return result;
            }
            if (request == null || string.IsNullOrWhiteSpace(request.Text))
                return new MediaGenerationResult(MediaGenerationStatus.Refused, "", "invalid_args: voice text is required");
            string id = IdDerivation.NewChangeSetId();
            string folder = (_gateway as EtosAgentGateway)?.Options.GeneratedFolder ?? "Assets/Generated/Studio";
            string path = folder.TrimEnd('/') + "/voice-" + id + ".wav";
            PendingVoice = GenerateRequestedVoice(request, path, id);
            return new MediaGenerationResult(MediaGenerationStatus.Requested, id, "Voice generation requested; verified media is imported through asset.import at " + path);
        }

        private async Task<MediaImport> GenerateRequestedVoice(VoiceGenerationRequest request, string path, string id)
        {
            MediaImport result;
            try
            {
                result = await GenerateSpeechAsync(request.Text, path, request.Voice,
                    (_gateway as EtosAgentGateway)?.Options.MaxCostUsd).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                var problem = new Diagnostic(EtosCodes.Transport, EtosRedaction.Redact(error.Message));
                result = new MediaImport(OpResult.Refused(problem), path, null, null, new[] { problem });
            }
            await Queue.Run(() =>
            {
                Runtime.Log.Write(result.Ok ? StudioLogLevel.Info : StudioLogLevel.Warning, "etos.media",
                    result.Ok ? "Voice request " + id + " imported " + path : "Voice request " + id + " failed", result.Problem);
                return true;
            }).ConfigureAwait(false);
            return result;
        }

        public EtosMediaGenerator(Authoring.Agent.IAgentGateway gateway, StudioRuntime runtime, MainThreadQueue queue)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        }

        /// <summary>
        /// Image → PNG at <paramref name="assetPath"/>. With <paramref name="squareSize"/> the provider's image is
        /// down-sampled in the Studio (the provider offers fixed sizes) before it is retained and imported.
        /// </summary>
        public async Task<MediaImport> GenerateImageAsync(string prompt, string assetPath, int? squareSize = null, double? maxCostUsd = null, string? changeSetId = null, CancellationToken ct = default)
        {
            OpResult result = await Gateway.GenerateAsync(new OpRequest("generate.image", new JObject { ["prompt"] = prompt }, maxCostUsd) { ChangeSetId = changeSetId }, ct).ConfigureAwait(false);
            return await Queue.Run(() => ImportOnMain(result, assetPath, "texture", "Generate image: " + prompt, squareSize)).ConfigureAwait(false);
        }

        /// <summary>Text to speech → WAV at <paramref name="assetPath"/>.</summary>
        public async Task<MediaImport> GenerateSpeechAsync(string text, string assetPath, string? voice = null, double? maxCostUsd = null, string? changeSetId = null, CancellationToken ct = default)
        {
            JObject inputs = new JObject { ["text"] = text };
            if (!string.IsNullOrEmpty(voice))
            {
                inputs["voice"] = voice;
            }

            OpResult result = await Gateway.GenerateAsync(new OpRequest("tts", inputs, maxCostUsd) { ChangeSetId = changeSetId }, ct).ConfigureAwait(false);
            return await Queue.Run(() => ImportOnMain(result, assetPath, "voiceLine", "Speak: " + text, null)).ConfigureAwait(false);
        }

        /// <summary>Describes a retained (or companion-stored) artifact; the answer is text.</summary>
        public Task<OpResult> DescribeAsync(string sha256, string? prompt = null, double? maxCostUsd = null, CancellationToken ct = default)
        {
            JObject inputs = new JObject { ["artifact"] = Json.NormalizeSha256(sha256) ?? sha256 };
            if (!string.IsNullOrEmpty(prompt))
            {
                inputs["prompt"] = prompt;
            }

            return Gateway.GenerateAsync(new OpRequest("describe", inputs, maxCostUsd), ct);
        }

        /// <summary>3D mesh; the companion's refusal (SADR-020) is returned as it came.</summary>
        public async Task<MediaImport> Generate3dAsync(string prompt, string assetPath, double? maxCostUsd = null, string? changeSetId = null, CancellationToken ct = default)
        {
            OpResult result = await Gateway.GenerateAsync(new OpRequest("generate.3d", new JObject { ["prompt"] = prompt }, maxCostUsd) { ChangeSetId = changeSetId }, ct).ConfigureAwait(false);
            return await Queue.Run(() => ImportOnMain(result, assetPath, "mesh", "Generate mesh: " + prompt, null)).ConfigureAwait(false);
        }

        /// <summary>Sound effects: no companion op exists, so this is an honest not_configured.</summary>
        public Task<MediaImport> GenerateSoundEffectAsync(string prompt, string assetPath)
        {
            Diagnostic refusal = new Diagnostic(EtosCodes.NotConfigured, "The companion offers no sound-effect op (image, tts, 3d and describe only).", "Use tts for spoken lines; sound effects need a provider op on the node.");
            return Task.FromResult(new MediaImport(OpResult.Refused(refusal), assetPath, null, null, new[] { refusal }));
        }

        /// <summary>Retains the verified bytes and applies a journaled asset.import (main thread).</summary>
        public MediaImport ImportOnMain(OpResult result, string assetPath, string role, string intent, int? squareSize)
        {
            if (!result.Succeeded || result.Bytes == null || result.Sha256 == null)
            {
                Diagnostic problem = result.Refusal ?? new Diagnostic(DiagnosticCodes.Refused, "The op returned no artifact.");
                return new MediaImport(result, assetPath, null, null, new[] { problem });
            }

            byte[] bytes = result.Bytes;
            if (Json.Sha256Hex(bytes) != Json.NormalizeSha256(result.Sha256))
            {
                var problem = new Diagnostic(EtosCodes.ArtifactDigestMismatch, "Generated media digest does not match its bytes.");
                return new MediaImport(OpResult.Refused(problem), assetPath, null, null, new[] { problem });
            }
            string mediaType = result.MediaType ?? "application/octet-stream";
            if (squareSize.HasValue && mediaType == "image/png")
            {
                bytes = DownsamplePng(bytes, squareSize.Value);
            }

            string sha = Json.Sha256Hex(bytes);
            ArtifactProducer producer = new ArtifactProducer(op: role == "voiceLine" ? "tts" : role == "mesh" ? "generate.3d" : "generate.image", provider: result.Provider);
            ArtifactRef artifact = new ArtifactRef(sha, mediaType, bytes.LongLength, System.IO.Path.GetFileName(assetPath), producer, role);
            try
            {
                Runtime.Artifacts.Put(bytes, artifact);
            }
            catch (ArtifactStoreException error)
            {
                Diagnostic problem = new Diagnostic(DiagnosticCodes.StageFailed, EtosRedaction.Redact("artifact_retention: " + error.Message));
                return new MediaImport(result, assetPath, artifact, null, new[] { problem });
            }

            Operation import = new Operation("op1", BuiltInToolIds.AssetImport, null, new JObject { ["path"] = assetPath, ["artifact"] = new JObject { ["artifact"] = artifact.Reference } });
            ChangeSet changeSet = new ChangeSet(IdDerivation.NewChangeSetId(), ChangeSet.SchemaId, new Intent(intent, IntentOrigin.Agent), new[] { import }, artifacts: new[] { artifact });
            ApplyReport report = Runtime.Engine.Apply(changeSet);
            return new MediaImport(result, assetPath, artifact, report, report.Diagnostics);
        }

        /// <summary>A PNG box-filtered down to <paramref name="size"/>×<paramref name="size"/> (CPU only; works without graphics).</summary>
        public static byte[] DownsamplePng(byte[] png, int size)
        {
            Texture2D source = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Texture2D target = new Texture2D(size, size, TextureFormat.RGBA32, false);
            try
            {
                if (!source.LoadImage(png, false))
                {
                    return png;
                }

                if (source.width == size && source.height == size)
                {
                    return png;
                }

                Color32[] from = source.GetPixels32();
                Color32[] to = new Color32[size * size];
                int sw = source.width;
                int sh = source.height;
                for (int y = 0; y < size; y++)
                {
                    int y0 = y * sh / size;
                    int y1 = Math.Max(y0 + 1, (y + 1) * sh / size);
                    for (int x = 0; x < size; x++)
                    {
                        int x0 = x * sw / size;
                        int x1 = Math.Max(x0 + 1, (x + 1) * sw / size);
                        long r = 0, g = 0, b = 0, a = 0, n = 0;
                        for (int yy = y0; yy < y1; yy++)
                        {
                            for (int xx = x0; xx < x1; xx++)
                            {
                                Color32 c = from[(yy * sw) + xx];
                                r += c.r;
                                g += c.g;
                                b += c.b;
                                a += c.a;
                                n++;
                            }
                        }

                        to[(y * size) + x] = new Color32((byte)(r / n), (byte)(g / n), (byte)(b / n), (byte)(a / n));
                    }
                }

                target.SetPixels32(to);
                target.Apply(false, false);
                return target.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
