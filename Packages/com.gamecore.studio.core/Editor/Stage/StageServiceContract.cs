#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Edit
{
    // Implemented by the authenticated companion client, never by a candidate artifact reader.
    public interface IStageService
    {
        Task<string> RequestStage(StageRequest request);
        Task<SignedVerdict> GetVerdict(string jobId);
        Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request);
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StageRequest
    {
        [JsonProperty("changeSetId")] public string ChangeSetId { get; }
        [JsonProperty("projectId")] public string ProjectId { get; }
        [JsonProperty("sourceProject")] public string SourceProject { get; }
        [JsonProperty("sourceRevision")] public string SourceRevision { get; }
        [JsonProperty("catalogRevision")] public string CatalogRevision { get; }
        [JsonProperty("packageDigest")] public string PackageDigest { get; }
        [JsonProperty("proposalDigest")] public string ProposalDigest { get; }
        [JsonProperty("stageInputs")] public IReadOnlyList<string> StageInputs { get; }

        public StageRequest(string changeSetId, string projectId, string sourceProject, string sourceRevision,
            string catalogRevision, string packageDigest, string proposalDigest, IReadOnlyList<string> stageInputs)
        {
            if (!IdDerivation.IsChangeSetId(changeSetId) || string.IsNullOrWhiteSpace(projectId)
                || string.IsNullOrWhiteSpace(sourceProject) || string.IsNullOrWhiteSpace(sourceRevision)
                || string.IsNullOrWhiteSpace(catalogRevision) || !ContentStamp.IsValidHex(packageDigest)
                || !ContentStamp.IsValidHex(proposalDigest)) throw new ArgumentException("invalid_stage_request");
            ChangeSetId = changeSetId;
            ProjectId = projectId;
            SourceProject = sourceProject;
            SourceRevision = sourceRevision;
            CatalogRevision = catalogRevision;
            PackageDigest = packageDigest;
            ProposalDigest = proposalDigest;
            var copy = new List<string>();
            foreach (string input in stageInputs)
            {
                StageDataPaths.ValidateInput(input);
                copy.Add(input);
            }
            StageInputs = copy.AsReadOnly();
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class SignedVerdict
    {
        [JsonProperty("jobId")] public string JobId { get; }
        [JsonProperty("signature")] public string Signature { get; }
        [JsonProperty("verdict")] public JObject Verdict => (JObject)_verdict.DeepClone();
        private readonly JObject _verdict;
        [JsonConstructor]
        public SignedVerdict(string jobId, string signature, JObject verdict)
        {
            JobId = jobId;
            Signature = signature;
            _verdict = (JObject)verdict.DeepClone();
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StageVerificationRequest
    {
        [JsonProperty("signedVerdict")] public SignedVerdict SignedVerdict { get; }
        [JsonProperty("expected")] public StageRequest Expected { get; }
        public StageVerificationRequest(SignedVerdict signedVerdict, StageRequest expected)
        {
            SignedVerdict = signedVerdict;
            Expected = expected;
        }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StageVerification
    {
        [JsonProperty("verified")] public bool Verified { get; }
        [JsonProperty("jobId")] public string JobId { get; }
        [JsonConstructor]
        public StageVerification(bool verified, string jobId) { Verified = verified; JobId = jobId; }
    }

    public static class StageDataPaths
    {
        public static void ValidateRelative(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("\\") || path.Contains(":"))
                throw new ArgumentException("stage_input_forbidden");
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.Equals("Editor", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Plugins", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("stage_input_forbidden");
        }

        public static void ValidateInput(string path)
        {
            ValidateRelative(path);
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("stage_input_forbidden");
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".webp": case ".wav": case ".ogg": case ".mp3":
                case ".json": case ".txt": case ".csv": case ".fbx": case ".glb": case ".gltf": return;
                default: throw new ArgumentException("stage_input_forbidden");
            }
        }

        public static void ValidateProposal(JObject proposal)
        {
            foreach (JToken input in proposal["stageInputs"] as JArray ?? new JArray()) ValidateInput(input.Value<string>() ?? "");
            foreach (string member in new[] { "directory", "tests" })
            {
                JToken? path = proposal["rules"]?[member];
                if (path != null) ValidateRelative(path.Value<string>() ?? "");
            }
            if (proposal["allowUnsafe"] is JArray unsafeReasons && unsafeReasons.Count != 0)
                throw new ArgumentException("stage_input_forbidden");
        }

        // Reject links in every ancestor before opening any candidate-controlled path.
        public static string ContainedFile(string root, string relative)
        {
            ValidateRelative(relative);
            string fullRoot = Path.GetFullPath(root);
            string full = Path.GetFullPath(Path.Combine(fullRoot, relative));
            if (!full.StartsWith(fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("artifact_path_forbidden");
            for (string? path = full; path != null; path = Path.GetDirectoryName(path))
                if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("artifact_path_forbidden");
            return full;
        }
    }
}
