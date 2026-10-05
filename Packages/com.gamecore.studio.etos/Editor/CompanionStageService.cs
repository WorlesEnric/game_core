#nullable enable
using System;
using System.Linq;
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
        public CompanionStageService(CompanionClient client) { _client = client; }

        public async Task<string> RequestStage(StageCandidateRequest request)
        {
            StageJobInfo job = await _client.StageAsync(request.ChangeSetId, request.ProjectId,
                request.SourceRevision, request.CatalogRevision).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(job.JobId)) throw EtosException.Protocol("Stage response has no jobId.");
            return job.JobId;
        }

        public async Task<SignedVerdict> GetVerdict(string jobId)
        {
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
