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
    /// <summary>Replays the installed P4.2c job's authenticated context and exact candidate through the
    /// real Play capture and real catalog. Stops at the durable pre-install checkpoint: the retained
    /// job does not authorize installing code as a newly staged revision of this clone.</summary>
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
            Task<StageVerdict> fetch = admission.FetchVerdict((string)binding["jobId"]!, expected);
            deadline = DateTime.UtcNow.AddSeconds(60);
            while (!fetch.IsCompleted && DateTime.UtcNow < deadline) yield return null;
            Assert.That(fetch.IsCompleted, Is.True, "installed companion verdict request timed out");
            StageVerdict verdict = fetch.GetAwaiter().GetResult();
            Assert.That(verdict.Pass, Is.True);
            HollowmereStudioAdmission.Bind(runtime, boot, boot.Saves!);
            string destination = Path.Combine(options.PackagesRoot!, verdict.Package);
            bool captureExisted = boot.Saves!.Exists("admit-" + candidate.Id.ToLowerInvariant());
            AdmissionResult result = admission.Admit(candidate, captureAndStop: true);
            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.Pending), result.Reason + ": " + result.Detail);
            JObject pending = admission.ReadPending(candidate.Id)!;
            Assert.That((string?)pending["phase"], Is.EqualTo("stop-play"));
            Assert.That(pending["before"]?.Type, Is.Not.EqualTo(JTokenType.String), "catalog verification must wait for Edit mode");
            Assert.That(Directory.Exists(destination), Is.False);
            string slot = (string)pending["captureSlot"]!;
            Assert.That(boot.Saves!.Exists(slot), Is.True, "real SaveService capture precedes stopping Play");
            if (!captureExisted) boot.Saves.Delete(slot);
            yield return new ExitPlayMode();
            options.FaultHook = (point, id) =>
            {
                JObject p = admission.ReadPending(id)!;
                if (point != AdmissionFaultPoint.Pending || p["before"]?.Type != JTokenType.String) return;
                Assert.That(EditorApplication.isPlaying, Is.False);
                Assert.That(Directory.Exists(destination), Is.False, "catalog baseline must be durable before code side effects");
                throw new AdmissionCrashException();
            };
            Assert.Throws<AdmissionCrashException>(() => admission.Resume(candidate.Id));
            pending = admission.ReadPending(candidate.Id)!;
            string? actual = new ReflectionAdmissionCatalog().WorldFingerprint(out string? problem);
            Assert.That(actual, Is.Not.Null.And.Length.EqualTo(64), problem);
            Assert.That((string?)pending["before"], Is.EqualTo(CatalogSet.Combine(actual!, Array.Empty<string>())));
            Assert.That(runtime.Journal.Read(candidate.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(Directory.Exists(destination), Is.False);
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
