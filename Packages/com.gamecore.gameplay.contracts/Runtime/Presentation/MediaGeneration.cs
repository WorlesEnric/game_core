// GameCore.Gameplay.Contracts - the media generation seam of the audio tools (P1.5; Studio 04 s5).
//
// audio.generateVoice and audio.generateSfx never call a provider. They hand a request to an IMediaGenerationGateway.
// Until the Studio ETOS client (P2.2) supplies one, the NullMediaGenerationGateway answers NotConfigured (GP-AUD-020)
// and nothing changes. The real gateway runs the etos op `tts` (voice) or the sfx op and writes a .wav under the given
// project path. It may complete at once or answer Pending with a job id. Engine-free.
#nullable enable
using System;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>What a generation request produces.</summary>
    public enum MediaKind
    {
        Voice = 0,
        Sfx = 1,
    }

    /// <summary>The outcome of a generation request.</summary>
    public enum MediaGenerationStatus
    {
        /// <summary>The file was written to the request's output path.</summary>
        Completed = 0,

        /// <summary>Accepted; the file lands later (JobId names the job).</summary>
        Pending = 1,

        /// <summary>No gateway is configured (GP-AUD-020).</summary>
        NotConfigured = 2,

        /// <summary>The gateway refused the request (GP-AUD-021 plus the gateway's detail).</summary>
        Refused = 3,
    }

    /// <summary>One generation request.</summary>
    public sealed class MediaGenerationRequest
    {
        public MediaGenerationRequest(MediaKind kind, string text, string outputPath)
        {
            Kind = kind;
            Text = text ?? string.Empty;
            OutputPath = outputPath ?? string.Empty;
        }

        public MediaKind Kind { get; }

        /// <summary>The line to speak (voice) or the sound to describe (sfx).</summary>
        public string Text { get; }

        /// <summary>Project-relative .wav path the gateway writes.</summary>
        public string OutputPath { get; }

        /// <summary>The voice preset (voice only; empty = the gateway's default).</summary>
        public string Voice { get; set; } = string.Empty;

        /// <summary>The speaker the line belongs to (provenance).</summary>
        public string SpeakerId { get; set; } = string.Empty;

        /// <summary>The bank clip id the result is assigned to.</summary>
        public string ClipId { get; set; } = string.Empty;

        /// <summary>Requested length in milliseconds (sfx only; 0 = the gateway decides).</summary>
        public int DurationMs { get; set; }
    }

    /// <summary>The answer to a generation request.</summary>
    public sealed class MediaGenerationResult
    {
        public MediaGenerationResult(MediaGenerationStatus status, string code, string detail, string outputPath, string jobId)
        {
            Status = status;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
            OutputPath = outputPath ?? string.Empty;
            JobId = jobId ?? string.Empty;
        }

        public MediaGenerationStatus Status { get; }

        /// <summary>A stable code when not completed (GP-AUD-020, GP-AUD-021); empty otherwise.</summary>
        public string Code { get; }

        public string Detail { get; }

        public string OutputPath { get; }

        public string JobId { get; }

        public static MediaGenerationResult NotConfigured(string detail) =>
            new MediaGenerationResult(MediaGenerationStatus.NotConfigured, PresentationDiagnosticCodes.MediaNotConfigured, detail, string.Empty, string.Empty);

        public static MediaGenerationResult Refused(string detail) =>
            new MediaGenerationResult(MediaGenerationStatus.Refused, PresentationDiagnosticCodes.MediaRefused, detail, string.Empty, string.Empty);

        public override string ToString() => Status + (Code.Length > 0 ? " " + Code : string.Empty) + (Detail.Length > 0 ? ": " + Detail : string.Empty);
    }

    /// <summary>Generates media for the audio tools.</summary>
    public interface IMediaGenerationGateway
    {
        /// <summary>A short name for logs (e.g. "null", "etos").</summary>
        string Name { get; }

        /// <summary>When several gateways are present the highest priority wins; the null gateway is the lowest.</summary>
        int Priority { get; }

        MediaGenerationResult Generate(MediaGenerationRequest request);
    }

    /// <summary>The gateway used until one is configured: every request answers NotConfigured.</summary>
    public sealed class NullMediaGenerationGateway : IMediaGenerationGateway
    {
        public string Name => "null";

        public int Priority => int.MinValue;

        public MediaGenerationResult Generate(MediaGenerationRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            return MediaGenerationResult.NotConfigured(
                "media generation is not configured: the Studio ETOS client (P2.2) supplies the gateway (etos op tts); no provider is called");
        }
    }
}
