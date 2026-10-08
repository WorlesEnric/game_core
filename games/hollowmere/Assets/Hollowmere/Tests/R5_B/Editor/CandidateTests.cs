#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Edit;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using GameCore.Studio.UI;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace Hollowmere.R5_B
{
    public sealed class CandidateTests
    {
        private static string Repo => Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));

        [TestCase(false)]
        [TestCase(true)]
        public void Request5_RetainedUnpreviewedCandidateBadgeSurvivesReload(bool refreshFails)
        {
            string root = Path.Combine(Path.GetTempPath(), "r5-b-" + Guid.NewGuid().ToString("N"));
            var paths = new StudioPaths(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), root);
            JObject witness = JObject.Parse(File.ReadAllText(Path.Combine(Repo,
                "artifacts/studio/verification/W-MECH-01/p42c-stage-review-20261006T081247.078280Z/panel-verdict.json")));
            Assert.That((bool)witness["verified"]!, Is.True);
            Assert.That((bool)witness["canAdmit"]!, Is.True);
            Assert.That((string)witness["label"]!, Is.EqualTo("not staged"));
            // The original witness records the missing durable badge. Replay the later production
            // stage result, whose signed delta includes the world and predicted combined catalog.
            JObject currentWitness = JObject.Parse(File.ReadAllText(Path.Combine(Repo,
                "artifacts/studio/verification/W-MECH-01/p42e-stage-review-20261006T144510.582506Z/panel-verdict.json")));
            Assert.That((bool)currentWitness["verified"]!, Is.True);
            Assert.That((bool)currentWitness["canAdmit"]!, Is.True);
            var service = new RetainedVerdictService((JObject)currentWitness["verdict"]!);
            string folder = Path.Combine(Repo, "artifacts/studio/workflows/P4.2e/candidate");
            ChangeSet candidate = StudioJson.Deserialize<ChangeSet>(File.ReadAllText(Path.Combine(folder, "change-set.json")));
            try
            {
                using (var runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = paths, LoadIndexCache = false, SearchFolders = Array.Empty<string>() }))
                {
                    Configure(runtime, service);
                    foreach (ArtifactRef artifact in candidate.Artifacts!)
                        runtime.Artifacts.Put(File.ReadAllBytes(Path.Combine(folder, "artifacts", artifact.Name!)), artifact);
                    var coordinator = new CandidateCoordinator(runtime, () => null);
                    CandidateEntry entry = coordinator.Add("retained-app", candidate);
                    Assert.That(runtime.Journal.Exists(entry.Id), Is.False, "no Preview or artificial journal seed");
                    Assert.That(coordinator.RequestStage(entry).GetAwaiter().GetResult(), Is.Null);
                    Assert.That(coordinator.CanAdmit(entry), Is.True);
                    Assert.That(coordinator.StageStateOf(entry).Label, Is.EqualTo("verdict pass"));
                }
                // A fresh runtime has no verified in-memory authority. The durable badge survives;
                // refreshing must fetch/verify again before Admit is enabled.
                using (var runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = paths, LoadIndexCache = false, SearchFolders = Array.Empty<string>() }))
                {
                    Configure(runtime, service);
                    var coordinator = new CandidateCoordinator(runtime, () => null);
                    CandidateEntry entry = coordinator.Add("retained-app", candidate);
                    Assert.That(coordinator.StageStateOf(entry).Label, Is.EqualTo("verdict pass"));
                    Assert.That(coordinator.CanAdmit(entry), Is.False);
                    service.Fail = refreshFails;
                    Assert.That(coordinator.RefreshStage(entry).GetAwaiter().GetResult() == null, Is.EqualTo(!refreshFails));
                    Assert.That(coordinator.StageStateOf(entry).Label, Is.EqualTo(refreshFails ? "verdict fail" : "verdict pass"));
                    Assert.That(coordinator.CanAdmit(entry), Is.EqualTo(!refreshFails));
                    Assert.That(service.Fetches, Is.EqualTo(2));
                }
            }
            finally
            {
                SessionState.EraseString("GameCore.Studio.UI.Stage." + ContentStamp.Sha256Hex(System.Text.Encoding.UTF8.GetBytes(paths.ProjectRoot)) + "." + candidate.Id);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static void Configure(StudioRuntime runtime, RetainedVerdictService service)
        {
            AdmissionOptions options = StageAdmission.Of(runtime).Options;
            options.StageService = service;
            options.ProjectId = (string)service.Document["projectId"]!;
            options.SourceRevision = () => (string)service.Document["sourceRevision"]!;
            options.CatalogRevision = () => (string)service.Document["catalogRevision"]!;
        }

        // Replays the unmodified retained service record through real StageAdmission/VerdictCheck.
        // This tests UI persistence, not companion signature authenticity or live admission.
        private sealed class RetainedVerdictService : IStageService
        {
            public RetainedVerdictService(JObject document) { Document = document; }
            public JObject Document { get; }
            public bool Fail { get; set; }
            public int Fetches { get; private set; }
            public Task<string> RequestStage(StageCandidateRequest request) => Task.FromResult((string)Document["jobId"]!);
            public Task<SignedVerdict> GetVerdict(string jobId)
            {
                Fetches++;
                return Task.FromResult(new SignedVerdict(jobId, "offline-retained-record", Document));
            }
            public Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request) => Task.FromResult(new StageVerification(!Fail, jobId));
        }
    }
}
