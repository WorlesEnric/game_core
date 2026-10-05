#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.P2_4.EditMode.Tests;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hollowmere.R2_B.Tests
{
    public sealed class AdmissionTrustTests
    {
        private AdmissionTestBed _bed = null!;
        private ChangeSet _candidate = null!;
        private byte[] _bytes = null!;
        [SetUp]
        public void SetUp()
        {
            _bed = new AdmissionTestBed();
            var files = AdmissionTestBed.PackageFiles();
            _candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string package, out string proposal);
            _bytes = _bed.Verdict(_candidate.Id, package, proposal, files);
        }
        [TearDown] public void TearDown() => _bed.Dispose();
        private byte[] Mutate(Action<JObject> edit)
        {
            JObject json = JObject.Parse(Encoding.UTF8.GetString(_bytes));
            edit(json);
            return Encoding.UTF8.GetBytes(json.ToString(Formatting.None));
        }

        [Test]
        public void R2_09_SelfAuthoredVerdictBytesNeverAuthorize()
        {
            AdmissionResult result = _bed.Admission.Admit(_candidate, _bytes);
            Assert.That(result.Reason, Is.EqualTo(VerdictReasons.Untrusted));
            StageVerdict parsed = StageVerdict.Parse(_bytes, out _)!;
            Assert.That(VerdictCheck.Check(parsed, _candidate.Id, "", "", null, out _), Is.EqualTo(VerdictReasons.Untrusted));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
        }

        [TestCase("")]
        [TestCase("wrong-job")]
        public void R2_09_UnsignedOrMismatchedJobRefuses(string job)
        {
            _bed.Issued = new SignedVerdict(job == "" ? "test-job" : job, job == "" ? "" : "signature", JObject.Parse(Encoding.UTF8.GetString(_bytes)));
            Assert.ThrowsAsync<InvalidOperationException>(async () => await _bed.Admission.FetchVerdict("test-job", _bed.Request(_candidate)));
            Assert.That(_bed.Admission.VerdictOf(_candidate.Id), Is.Null);
        }

        [Test]
        public void R2_09_CompanionRejectsForgedSignature()
        {
            _bed.Verify = false;
            Assert.Throws<InvalidOperationException>(() => _bed.Trust(_candidate, _bytes));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
        }

        [TestCase("partial")]
        [TestCase("missing-step")]
        [TestCase("skipped-step")]
        [TestCase("duplicate-step")]
        [TestCase("forbidden")]
        [TestCase("warm-budget")]
        [TestCase("missing-cold-cache")]
        public void R2_09_IncompleteVerdictRefuses(string failure)
        {
            byte[] bytes = Mutate(json =>
            {
                switch (failure)
                {
                    case "partial": json["partial"] = true; break;
                    case "missing-step": ((JArray)json["steps"]!).RemoveAt(0); break;
                    case "skipped-step": json["steps"]![0]!["status"] = "skipped"; break;
                    case "duplicate-step": ((JArray)json["steps"]!).Add(json["steps"]![0]!.DeepClone()); break;
                    case "forbidden": ((JArray)json["forbiddenHits"]!).Add(new JObject { ["rule"] = "network" }); break;
                    case "warm-budget": json["durationMs"] = 360001; break;
                    case "missing-cold-cache": json.Remove("coldCache"); break;
                }
            });
            Assert.Throws<InvalidOperationException>(() => _bed.Trust(_candidate, bytes));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
        }

        [Test]
        public void R2_09_HostConfinementRequiresOperatorOptIn()
        {
            byte[] bytes = Mutate(json => json["confinement"] = "host");
            Assert.Throws<InvalidOperationException>(() => _bed.Trust(_candidate, bytes));
            _bed.Admission.Options.AllowHostConfinement = true;
            _bed.Trust(_candidate, bytes);
            _bed.Admission.Options.AllowHostConfinement = false;
            Assert.That(_bed.Admission.Admit(_candidate).Reason, Is.EqualTo(VerdictReasons.Untrusted));
        }

        [TestCase("projectId")]
        [TestCase("sourceRevision")]
        [TestCase("catalogRevision")]
        [TestCase("jobId")]
        public void R2_09_ContextBindingRefusesMismatch(string member)
        {
            Assert.Throws<InvalidOperationException>(() => _bed.Trust(_candidate, Mutate(json => json[member] = "other")));
        }

        [Test]
        public void R2_09_CurrentRevisionRecheckedBeforeInstall()
        {
            _bed.Trust(_candidate, _bytes);
            _bed.Admission.Options.SourceRevision = () => "changed";
            Assert.That(_bed.Admission.Admit(_candidate).Reason, Is.EqualTo(VerdictReasons.Mismatch));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
        }

        [Test]
        public void R2_10_MutationToolsAbsentFromCatalogAndDirectApplyRefuses()
        {
            Assert.That(AdmissionTestBed.AdmissionMethods(), Is.Empty);
            _bed.Trust(_candidate, _bytes);
            ChangeSet admission = _bed.Admission.ToAdmission(_candidate, _bed.Admission.VerdictOf(_candidate.Id));
            Assert.That(_bed.Runtime.Engine.Apply(admission).Ok, Is.False);
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
            Assert.That(_bed.Admission.CheckRemovable(_bed.PackageDirectory, AdmissionTestBed.Package), Is.EqualTo("package_not_admitted"));
            Assert.That(_bed.Admission.CheckRemovable(Path.Combine(_bed.PackagesRoot, "com.gamecore.studio.core"), "com.gamecore.studio.core"), Is.EqualTo("reserved_package"));
        }

        [TestCase("Assets/Editor/code.cs")]
        [TestCase("Assets/data.dll")]
        [TestCase("Assets/data.asmdef")]
        [TestCase("Assets/data.rsp")]
        [TestCase("Assets/data.prefab")]
        [TestCase("Assets/data.controller")]
        [TestCase("Assets/data.mat")]
        [TestCase("Assets/World")]
        [TestCase("Assets/../outside.json")]
        [TestCase("/tmp/outside.json")]
        public void R2_12_StageInputDataOnly(string path)
        {
            Assert.Throws<ArgumentException>(() => new StageRequest(_candidate.Id, "p", "games/hollowmere", "s", "c", new string('a', 64), new string('b', 64), new[] { path }));
        }

        [TestCase("../Rules")]
        [TestCase("/Rules")]
        [TestCase("Editor/Rules")]
        public void R2_12_ProposalPathsContained(string path)
        {
            Assert.Throws<ArgumentException>(() => StageDataPaths.ValidateProposal(new JObject { ["rules"] = new JObject { ["directory"] = path } }));
        }

        [Test]
        public void R2_13_TypedRequestPreservesCandidateAndBinding()
        {
            JObject json = JObject.FromObject(_bed.Request(_candidate));
            Assert.That(json.Properties().Select(p => p.Name), Is.EquivalentTo(new[] { "changeSetId", "projectId", "sourceProject", "sourceRevision", "catalogRevision", "packageDigest", "proposalDigest", "stageInputs" }));
            Assert.That((string?)json["changeSetId"], Is.EqualTo(_candidate.Id));
            Assert.That((string?)json["stageInputs"]![0], Is.EqualTo("Assets/Hollowmere/World.json"));
            Assert.That(json["packageRef"], Is.Null);
        }

        [TestCase("../outside.txt")]
        [TestCase("/tmp/outside.txt")]
        [TestCase("folder/outside.txt")]
        [TestCase("folder\\outside.txt")]
        public void R2_08_ArtifactNameCannotReadOutsideCandidate(string name)
        {
            string root = Path.Combine(_bed.Root, "candidate");
            Directory.CreateDirectory(root);
            var artifact = new ArtifactRef(new string('a', 64), "text/plain", 1, name);
            var candidate = new ChangeSet(_candidate.Id, ChangeSet.SchemaId, _candidate.Intent, _candidate.Operations, artifacts: new[] { artifact });
            File.WriteAllText(Path.Combine(root, "change-set.json"), StudioJson.Serialize(candidate));
            Assert.Throws<ArgumentException>(() => _bed.Admission.RetainCandidate(root));
        }

        [Test]
        public void R2_08_SymlinkedCandidateArtifactRefusesBeforeRead()
        {
            string root = Path.Combine(_bed.Root, "candidate");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "change-set.json"), StudioJson.Serialize(_candidate));
            var start = new System.Diagnostics.ProcessStartInfo("ln") { UseShellExecute = false };
            start.ArgumentList.Add("-s"); start.ArgumentList.Add(Path.Combine(_bed.Root, "outside")); start.ArgumentList.Add(Path.Combine(root, "artifacts"));
            Directory.CreateDirectory(Path.Combine(_bed.Root, "outside"));
            using (var process = System.Diagnostics.Process.Start(start)) process!.WaitForExit();
            Assert.Throws<ArgumentException>(() => _bed.Admission.RetainCandidate(root));
        }

        [TestCase(AdmissionFaultPoint.Pending)]
        [TestCase(AdmissionFaultPoint.AfterApply)]
        [TestCase(AdmissionFaultPoint.Compile)]
        [TestCase(AdmissionFaultPoint.Reload)]
        [TestCase(AdmissionFaultPoint.Rebake)]
        [TestCase(AdmissionFaultPoint.Verify)]
        [TestCase(AdmissionFaultPoint.Smoke)]
        [TestCase(AdmissionFaultPoint.Applied)]
        public void R2_14_CrashAtEachTransitionResumes(AdmissionFaultPoint point)
        {
            _bed.Trust(_candidate, _bytes);
            _bed.Admission.Options.FaultHook = (actual, id) => { if (point == actual) throw new AdmissionCrashException(); };
            Assert.Throws<AdmissionCrashException>(() => _bed.Admission.Admit(_candidate));
            Assert.That(_bed.Runtime.Journal.Read(_candidate.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(_bed.Admission.ReadPending(_candidate.Id), Is.Not.Null);
            var options = _bed.Admission.Options;
            options.FaultHook = null;
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, options);
            Assert.That(resumed.VerdictOf(_candidate.Id), Is.Null, "a reload must not trust disk verdict bytes");
            resumed.RefreshPendingVerdicts().GetAwaiter().GetResult();
            Assert.That(resumed.Resume(_candidate.Id).Outcome, Is.EqualTo(AdmissionOutcome.Admitted));
            Assert.That(_bed.Runtime.Journal.Read(_candidate.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Applied));
        }

        [TestCase(AdmissionFaultPoint.UndoPending)]
        [TestCase(AdmissionFaultPoint.UndoCompile)]
        [TestCase(AdmissionFaultPoint.Reload)]
        [TestCase(AdmissionFaultPoint.UndoVerify)]
        public void R2_14_CrashDuringUndoRetainsRecovery(AdmissionFaultPoint point)
        {
            _bed.Trust(_candidate, _bytes);
            Assert.That(_bed.Admission.Admit(_candidate).Outcome, Is.EqualTo(AdmissionOutcome.Admitted));
            _bed.Admission.Options.FaultHook = (actual, id) => { if (actual == point) throw new AdmissionCrashException(); };
            Assert.Throws<AdmissionCrashException>(() => _bed.Admission.Undo(_candidate.Id));
            _bed.Admission.Options.FaultHook = null;
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, _bed.Admission.Options);
            resumed.RefreshPendingVerdicts().GetAwaiter().GetResult();
            Assert.That(resumed.Resume(_candidate.Id).Outcome, Is.EqualTo(AdmissionOutcome.Undone));
        }

        [TestCase(AdmissionFaultPoint.Capture)]
        [TestCase(AdmissionFaultPoint.StopPlay)]
        [TestCase(AdmissionFaultPoint.RestorePlay)]
        [TestCase(AdmissionFaultPoint.RestoreCapture)]
        public void R2_14_CaptureStopAndRestoreResumeAfterCrash(AdmissionFaultPoint point)
        {
            bool playing = true;
            var capture = new FakeCapture();
            var options = _bed.Admission.Options;
            options.PlayModeProbe = () => playing;
            options.StopPlayMode = () => playing = false;
            options.StartPlayMode = () => playing = true;
            options.SessionReady = () => true;
            options.Capture = capture;
            options.SmokeTest = _ => capture.Restored.Count > 0;
            _bed.Trust(_candidate, _bytes);
            options.FaultHook = (actual, id) => { if (actual == point) throw new AdmissionCrashException(); };
            Assert.Throws<AdmissionCrashException>(() =>
            {
                AdmissionResult result = _bed.Admission.Admit(_candidate, captureAndStop: true);
                if (result.Outcome == AdmissionOutcome.Pending) _bed.Admission.Resume(_candidate.Id);
            });
            options.FaultHook = null;
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, options);
            resumed.RefreshPendingVerdicts().GetAwaiter().GetResult();
            AdmissionResult done = resumed.Resume(_candidate.Id);
            if (done.Outcome == AdmissionOutcome.Pending) done = resumed.Resume(_candidate.Id);
            Assert.That(done.Outcome, Is.EqualTo(AdmissionOutcome.Admitted), done.Detail);
            Assert.That(playing, Is.True);
            Assert.That(capture.Restored.Count, Is.EqualTo(1));
        }

        [Test]
        public void R2_14_AsyncStopAutomaticallyResumesFullCandidate()
        {
            bool playing = true;
            var options = _bed.Admission.Options;
            var capture = new FakeCapture();
            options.PlayModeProbe = () => playing;
            options.StopPlayMode = () => { };
            options.StartPlayMode = () => playing = true;
            options.SessionReady = () => true;
            options.Capture = capture;
            _bed.Trust(_candidate, _bytes);
            Assert.That(_bed.Admission.Admit(_candidate, captureAndStop: true).Outcome, Is.EqualTo(AdmissionOutcome.Pending));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
            Assert.That((string?)_bed.Admission.ReadPending(_candidate.Id)?["phase"], Is.EqualTo("stop-play"));
            playing = false;
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, options);
            resumed.RefreshPendingVerdicts().GetAwaiter().GetResult();
            resumed.ResumePending();
            Assert.That(resumed.ResumePending()[0].Outcome, Is.EqualTo(AdmissionOutcome.Admitted));
            Assert.That(capture.Captured.Count, Is.EqualTo(1));
            Assert.That(capture.Restored.Count, Is.EqualTo(1));
        }

        [Test]
        public void R2_14_FailedUndoKeepsInterruptedRecordAndPreimages()
        {
            _bed.Trust(_candidate, _bytes);
            Assert.That(_bed.Admission.Admit(_candidate).Outcome, Is.EqualTo(AdmissionOutcome.Admitted));
            _bed.Admission.Options.FaultHook = (point, id) => { if (point == AdmissionFaultPoint.UndoCompile) throw new IOException("simulated removal fault"); };
            AdmissionResult result = _bed.Admission.Undo(_candidate.Id);
            Assert.That(result.Outcome, Is.EqualTo(AdmissionOutcome.Pending));
            Assert.That(_bed.Runtime.Journal.Read(_candidate.Id)!.EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(_bed.Admission.ReadPending(_candidate.Id), Is.Not.Null);
            Assert.That(File.Exists(Path.Combine(_bed.Admission.StateRoot, "removed", _candidate.Id, "package/package.json")), Is.True);
        }

        [Test]
        public void R2_14_LegacyAfterPlayRecordIsDetectedAndRefused()
        {
            string root = Path.Combine(_bed.Runtime.Paths.LibraryRoot, "stage");
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "admit-after-play-" + _candidate.Id + ".json"), "{\"captureSlot\":\"old\"}");
            var results = _bed.Admission.ResumePending();
            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].Reason, Is.EqualTo("legacy_admission_incomplete"));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
        }

        [Test]
        public void R2_15_HistoryHandlerDispatchesUndoAndRedoWithExactPreimages()
        {
            _bed.Trust(_candidate, _bytes);
            Assert.That(_bed.Admission.Admit(_candidate).Outcome, Is.EqualTo(AdmissionOutcome.Admitted));
            string meta = Path.Combine(_bed.PackageDirectory, "Runtime/Plate.cs.meta");
            File.WriteAllText(meta, "retained importer guid");
            IHistoryEntryHandler handler = new AdmissionHistoryHandler(_bed.Admission);
            ChangeSet entry = _bed.Runtime.Journal.Read(_candidate.Id)!;
            Assert.That(handler.CanHandle(entry), Is.True);
            Assert.That(handler.Undo(entry, false).Ok, Is.True);
            Assert.That(handler.Redo(_bed.Runtime.Journal.Read(_candidate.Id)!).Ok, Is.True);
            Assert.That(File.ReadAllText(meta), Is.EqualTo("retained importer guid"));
            Assert.That(_bed.Compiler.Requests.Count, Is.EqualTo(3));
        }
    }
}
