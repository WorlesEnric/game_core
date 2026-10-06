#nullable enable
using System;
using System.Linq;
using System.Collections.Generic;
using GameCore.Studio.Model;
using System.Threading;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos.Client;
using Newtonsoft.Json.Linq;

namespace GameCore.Studio.Etos
{
    /// <summary>Authenticated HTTP adapter. The wrapper retains the exact signed wire record for verification.</summary>
    public sealed class CompanionStageService : IStageService
    {
        private readonly CompanionClient _client;
        private readonly Func<string, ChangeSet?>? _candidate;
        private readonly Func<ToolCatalog>? _catalog;
        private readonly Func<string, byte[]>? _artifact;

        public CompanionStageService(CompanionClient client, Func<string, ChangeSet?>? candidate = null,
            Func<ToolCatalog>? catalog = null, Func<string, byte[]>? artifact = null)
        {
            _client = client;
            _candidate = candidate;
            _catalog = catalog;
            _artifact = artifact;
        }

        public async Task<string> RequestStage(StageCandidateRequest request)
        {
            StageJobInfo job = await RequestStageJobAsync(request).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(job.JobId)) throw EtosException.Protocol("Stage response has no jobId.");
            return job.JobId;
        }

        public async Task<StageJobInfo> RequestStageJobAsync(StageCandidateRequest request, CancellationToken ct = default)
        {
            // Snapshot Unity-owned data before the first await. Neither intent.origin nor a candidate-supplied
            // filesystem path establishes provenance; the authenticated ledger determines worker ownership.
            ChangeSet? candidate = _candidate?.Invoke(request.ChangeSetId);
            JObject? candidateJson = candidate == null ? null : JObject.Parse(StudioJson.Serialize(candidate));
            ToolCatalog? catalog = candidate == null ? null : _catalog?.Invoke();
            JObject? catalogJson = catalog == null ? null : JObject.Parse(StudioJson.Serialize(catalog));
            var files = new List<byte[]>();
            if (candidate != null)
            {
                if (catalog == null || _artifact == null || (catalog.Revision ?? catalog.ComputeRevision()) != request.CatalogRevision)
                    throw new EtosException(new EtosError(0, EtosCodes.StaleContext, "stage_catalog_changed"));
                foreach (ArtifactRef artifact in candidate.Artifacts ?? Array.Empty<ArtifactRef>())
                {
                    byte[] bytes = _artifact(artifact.Sha256);
                    if (Json.Sha256Hex(bytes) != artifact.Sha256 || bytes.LongLength != artifact.Bytes)
                        throw EtosException.Protocol("stage_artifact_mismatch");
                    files.Add(bytes);
                }
                bool appOrigin;
                try
                {
                    RequestInfo retained = await _client.GetRequestAsync(request.ChangeSetId, ct).ConfigureAwait(false);
                    appOrigin = (string?)retained.Raw["worker"] == "app";
                }
                catch (EtosException error) when (error.Error.Status == 404)
                {
                    // A previously linked worker candidate must never be reclassified when its ledger is missing.
                    appOrigin = candidate.Links?.EtosTasks == null || candidate.Links.EtosTasks.Count == 0;
                }
                if (appOrigin)
                    return await _client.StageAppCandidateAsync(request.ChangeSetId, request.ProjectId, request.SourceRevision,
                        request.CatalogRevision, candidateJson!, catalogJson!, files, ct).ConfigureAwait(false);
            }
            return await _client.StageAsync(request.ChangeSetId, request.ProjectId,
                request.SourceRevision, request.CatalogRevision, ct).ConfigureAwait(false);
        }

        public async Task<SignedVerdict> GetVerdict(string jobId)
        {
            // Cold-cache runs may legitimately exceed the warm budget; the companion owns that budget.
            // Poll only state. A terminal job still needs the authenticated issued-verdict route below.
            StageJobInfo job = await _client.GetStageAsync(jobId).ConfigureAwait(false);
            while (job.State == "queued" || job.State == "running")
            {
                await Task.Delay(1000).ConfigureAwait(false);
                job = await _client.GetStageAsync(jobId).ConfigureAwait(false);
            }
            if (job.State != "done")
                throw new EtosException(new EtosError(0, EtosCodes.StageFailed, "stage_job_" + job.State));
            JObject record = await _client.FetchTrustedVerdictAsync(jobId).ConfigureAwait(false);
            if ((string?)record["jobId"] != jobId || string.IsNullOrWhiteSpace((string?)record["signature"]))
                throw EtosException.Protocol("stage_verdict_untrusted");
            return new SignedVerdict(jobId, (string)record["signature"]!, record);
        }

        public async Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request)
        {
            JObject record = request.SignedVerdict.Verdict;
            bool verified = await _client.VerifyVerdictAsync(jobId, record).ConfigureAwait(false);
            StageCandidateRequest expected = request.Expected;
            verified = verified && request.SignedVerdict.JobId == jobId
                && request.SignedVerdict.Signature == (string?)record["signature"]
                && expected.ProjectId == _client.Options.ProjectId
                && (string?)record["jobId"] == jobId && (string?)record["projectId"] == expected.ProjectId
                && (string?)record["changeSetId"] == expected.ChangeSetId
                && (string?)record["sourceRevision"] == expected.SourceRevision
                && (string?)record["catalogRevision"] == expected.CatalogRevision
                && DigestMatches(record, "package", expected.PackageDigest)
                && DigestMatches(record, "proposal", expected.ProposalDigest)
                && InputsMatch(record, expected);
            return new StageVerification(verified, jobId);
        }

        private static bool DigestMatches(JObject record, string role, string digest)
        {
            if (string.IsNullOrEmpty(digest) || (string?)record[role + "Digest"] != digest) return false;
            var matches = (record["artifacts"] as JArray ?? new JArray()).Where(a => (string?)a["role"] == role).ToArray();
            return matches.Length == 1 && (string?)matches[0]["sha256"] == digest;
        }

        private static bool InputsMatch(JObject record, StageCandidateRequest expected)
        {
            // Older companion records omit the list. They cannot authorize nonempty input bindings.
            if (record["stageInputs"] == null) return expected.StageInputs.Count == 0;
            return record["stageInputs"] is JArray inputs && inputs.All(x => x.Type == JTokenType.String)
                && inputs.Values<string>().SequenceEqual(expected.StageInputs);
        }
    }
}
