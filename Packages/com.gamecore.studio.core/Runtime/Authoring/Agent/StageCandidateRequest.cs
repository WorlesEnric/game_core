#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Authoring.Agent
{
    /// <summary>Creator-selected candidate stage request. Source roots come from local project configuration,
    /// never candidate JSON. The companion resolves the retained candidate by changeSetId in the authenticated project.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class StageCandidateRequest
    {
        [JsonProperty("changeSetId")] public string ChangeSetId { get; }
        [JsonProperty("projectId")] public string ProjectId { get; }
        [JsonProperty("sourceProject")] public string SourceProject { get; }
        [JsonProperty("sourceRevision")] public string SourceRevision { get; }
        [JsonProperty("catalogRevision")] public string CatalogRevision { get; }
        [JsonProperty("packageDigest")] public string PackageDigest { get; }
        [JsonProperty("proposalDigest")] public string ProposalDigest { get; }
        [JsonProperty("stageInputs")] public IReadOnlyList<string> StageInputs { get; }

        [JsonConstructor]
        public StageCandidateRequest(string changeSetId, string projectId, string sourceProject, string sourceRevision,
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
                ValidateStageInput(input);
                copy.Add(input);
            }
            StageInputs = copy.AsReadOnly();
        }

        // Compatibility context for existing local callers; transport adapters must use the
        // companion's operator-configured project mapping, never transmit these host paths.
        public StageCandidateRequest(string changeSetId, string projectId, string projectPath, string repoRoot, string sourceRevision, string catalogRevision)
        {
            if (!IdDerivation.IsChangeSetId(changeSetId)) throw new ArgumentException("Invalid change set id.", nameof(changeSetId));
            ChangeSetId = changeSetId;
            ProjectId = projectId;
            SourceProject = projectPath;
            SourceRevision = sourceRevision;
            CatalogRevision = catalogRevision;
            // The admission builder must supply retained artifact bindings before staging.
            PackageDigest = string.Empty;
            ProposalDigest = string.Empty;
            StageInputs = Array.Empty<string>();
            ProjectPath = projectPath;
            RepoRoot = repoRoot;
        }
        [JsonIgnore] public string ProjectPath { get; } = string.Empty;
        [JsonIgnore] public string RepoRoot { get; } = string.Empty;

        public static void ValidateStageInput(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) || path.Contains("\\") || path.Contains(":"))
                throw new ArgumentException("stage_input_forbidden");
            foreach (string part in path.Split('/'))
                if (part.Length == 0 || part == "." || part == ".." || part.Equals("Editor", StringComparison.OrdinalIgnoreCase)
                    || part.Equals("Plugins", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("stage_input_forbidden");
            if (!path.StartsWith("Assets/", StringComparison.Ordinal)) throw new ArgumentException("stage_input_forbidden");
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".png": case ".jpg": case ".jpeg": case ".webp": case ".wav": case ".ogg": case ".mp3":
                case ".json": case ".txt": case ".csv": case ".fbx": case ".glb": case ".gltf": return;
                default: throw new ArgumentException("stage_input_forbidden");
            }
        }
    }

    /// <summary>Implemented by the authenticated companion gateway. Verdict bytes from artifacts are not trusted.</summary>
    public interface ICandidateStageGateway
    {
        Task<JObject> StageCandidateAsync(StageCandidateRequest request, CancellationToken cancellationToken);
        Task<JObject> FetchTrustedVerdictAsync(string jobId, CancellationToken cancellationToken);
    }
}
