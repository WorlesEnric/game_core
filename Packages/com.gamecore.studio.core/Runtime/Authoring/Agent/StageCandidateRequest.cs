#nullable enable
using System;
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
        public StageCandidateRequest(string changeSetId, string projectId, string projectPath, string repoRoot, string sourceRevision, string catalogRevision)
        {
            if (!IdDerivation.IsChangeSetId(changeSetId)) throw new ArgumentException("Invalid change set id.", nameof(changeSetId));
            ChangeSetId = changeSetId;
            ProjectId = projectId;
            ProjectPath = projectPath;
            RepoRoot = repoRoot;
            SourceRevision = sourceRevision;
            CatalogRevision = catalogRevision;
        }
        [JsonProperty("changeSetId")] public string ChangeSetId { get; }
        [JsonProperty("projectId")] public string ProjectId { get; }
        [JsonProperty("projectPath")] public string ProjectPath { get; }
        [JsonProperty("repoRoot")] public string RepoRoot { get; }
        [JsonProperty("sourceRevision")] public string SourceRevision { get; }
        [JsonProperty("catalogRevision")] public string CatalogRevision { get; }
    }

    /// <summary>Implemented by the authenticated companion gateway. Verdict bytes from artifacts are not trusted.</summary>
    public interface ICandidateStageGateway
    {
        Task<JObject> StageCandidateAsync(StageCandidateRequest request, CancellationToken cancellationToken);
        Task<JObject> FetchTrustedVerdictAsync(string jobId, CancellationToken cancellationToken);
    }
}
