#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Threading.Tasks;
using GameCore.Studio.Authoring;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Edit;
using GameCore.Studio.Etos;
using GameCore.Studio.Etos.Client;
using GameCore.Studio.Model;
using Hollowmere.Authoring;
using Hollowmere.Boot;
using Hollowmere.Game;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hollowmere.R5_A
{
    /// <summary>The authenticated historical P4.2c verdict lacks the required signed world/predicted
    /// delta. Even in a ready Play session it must not authorize capture or installation.</summary>
    public sealed class P31AdmissionInPlayMode
    {
        private StudioRuntime? runtime;
        private CompanionClient? client;
        private string root = string.Empty;
        private bool savedEnabled;
        private EnterPlayModeOptions savedOptions;
        private static string Project => Directory.GetParent(Application.dataPath)!.FullName;

        [UnityTest]
        [Timeout(240000)]
        public IEnumerator R5_03_InstalledVerdict_CapturesInPlay_VerifiesCatalogBeforeInstall()
        {
            savedEnabled = EditorSettings.enterPlayModeOptionsEnabled;
            savedOptions = EditorSettings.enterPlayModeOptions;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            EditorSettings.enterPlayModeOptions = EnterPlayModeOptions.DisableDomainReload;
            EditorSceneManager.OpenScene("Assets/Hollowmere/Boot/Boot.unity");
            yield return new EnterPlayMode();
            GameBoot? boot = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(90);
            while (DateTime.UtcNow < deadline)
            {
                boot = UnityEngine.Object.FindAnyObjectByType<GameBoot>();
                if (boot?.Saves != null && boot.AdmissionReady(boot.Saves)) break;
                yield return null;
            }
            Assert.That(boot?.Saves, Is.Not.Null, "the real game must boot");
            Assert.That(boot!.AdmissionReady(boot.Saves!), Is.True);
            root = Path.Combine(Path.GetTempPath(), "r5-installed-" + Guid.NewGuid().ToString("N"));
            runtime = StudioRuntime.Create(new StudioRuntimeOptions { Paths = new StudioPaths(Project, root, "r5-installed"),
                LoadIndexCache = false, Log = new MemoryStudioLog() });
            JObject binding = JObject.Parse(File.ReadAllText(Path.Combine(Project,
                "../../artifacts/studio/verification/W-MECH-01/p42c-stage-submit-20261006T080546.723368Z/stage-request.json")));
            StageCandidateRequest expected = binding["request"]!.ToObject<StageCandidateRequest>()!;
            // Production credential loader and authenticated client; no key/signature is printed or copied.
            EtosCredentials credentials = EtosCredentials.FromKeyFile(EtosCredentials.DefaultKeyFile()!);
            client = new CompanionClient(new EtosClientOptions { NodeUrl = credentials.NodeUrl ?? "http://127.0.0.1:7410",
                ProjectId = expected.ProjectId }, credentials);
            var options = new AdmissionOptions { PackagesRoot = Path.Combine(root, "packages"),
                StageService = new CompanionStageService(client), ProjectId = expected.ProjectId,
                SourceRevision = () => expected.SourceRevision, CatalogRevision = () => expected.CatalogRevision,
                StopPlayMode = () => { } };
            StageAdmission admission = StageAdmission.Configure(runtime, options);
            ChangeSet candidate = admission.RetainCandidate(Path.GetFullPath(Path.Combine(Project, "../../samples/mechanisms/pressure-plate/candidate")));
            string jobId = (string)binding["jobId"]!;
            Task<StageJobInfo> job = client.GetStageAsync(jobId);
            deadline = DateTime.UtcNow.AddSeconds(60);
            while (!job.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(job.IsCompleted, Is.True, "installed companion stage lookup timed out");
            StageJobInfo retained = job.GetAwaiter().GetResult();
            Assert.That(retained.JobId, Is.EqualTo(jobId));
            Assert.That(retained.Verdict, Is.TypeOf<JObject>());
            Assert.That((bool?)retained.Verdict!["pass"], Is.True, "historical pass alone cannot authorize admission");
            Assert.That(retained.Verdict?["catalogDelta"]?["world"], Is.Null);
            Assert.That(retained.Verdict?["catalogDelta"]?["predicted"], Is.Null);
            Task<StageVerdict> fetch = admission.FetchVerdict((string)binding["jobId"]!, expected);
            deadline = DateTime.UtcNow.AddSeconds(60);
            while (!fetch.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(fetch.IsCompleted, Is.True, "installed companion verdict request timed out");
            // The current companion refuses this incomplete signed record before Unity can cache it.
            EtosException refused = Assert.Throws<EtosException>(() => fetch.GetAwaiter().GetResult())!;
            Assert.That(refused.Code, Is.EqualTo(EtosCodes.NotFound));
            Assert.That(refused.Error.Status, Is.EqualTo(404));
            Assert.That(admission.VerdictOf(candidate.Id), Is.Null);
            HollowmereStudioAdmission.Bind(runtime, boot, boot.Saves!);
            string slot = "admit-" + candidate.Id.ToLowerInvariant();
            bool captureExisted = boot.Saves!.Exists(slot);
            AdmissionResult result = admission.Admit(candidate, captureAndStop: true);
            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.Refused));
            Assert.That(admission.ReadPending(candidate.Id), Is.Null);
            Assert.That(boot.Saves.Exists(slot), Is.EqualTo(captureExisted), "rejected verdict must not capture Play");
            Assert.That(Directory.Exists(options.PackagesRoot), Is.False, "rejected verdict must not install code");
            Assert.That(EditorApplication.isPlaying, Is.True);
            yield return new ExitPlayMode();
        }

        [TearDown]
        public void Cleanup()
        {
            EditorSettings.enterPlayModeOptionsEnabled = savedEnabled;
            EditorSettings.enterPlayModeOptions = savedOptions;
            runtime?.Dispose();
            client?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
