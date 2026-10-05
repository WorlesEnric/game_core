#nullable enable
using System;
using System.Collections;
using System.IO;
using System.Linq;
using GameCore.Studio.Edit;
using GameCore.Studio.Model;
using Hollowmere.P2_4.EditMode.Tests;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace Hollowmere.R2_B.Tests
{
    public sealed class AdmissionSmokeTests
    {
        private AdmissionTestBed _bed = null!;
        private ChangeSet _candidate = null!;
        private AdmissionOptions Options => _bed.Admission.Options;

        [SetUp]
        public void SetUp()
        {
            _bed = new AdmissionTestBed();
            var files = AdmissionTestBed.PackageFiles();
            _candidate = _bed.Candidate(AdmissionTestBed.TarGz(files), out string package, out string proposal);
            _bed.Trust(_candidate, _bed.Verdict(_candidate.Id, package, proposal, files));
        }

        [TearDown] public void TearDown() => _bed.Dispose();

        private JObject Pending() => _bed.Admission.ReadPending(_candidate.Id)!;
        private ChangeSet Entry() => _bed.Runtime.Journal.Read(_candidate.Id)!;
        private void Begin()
        {
            Assert.That(_bed.Admission.Admit(_candidate).Outcome, Is.EqualTo(AdmissionOutcome.Pending));
            Assert.That((string?)Pending()["phase"], Is.EqualTo("smoke-pending"));
            Assert.That(Entry().EffectiveState, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(Entry().Validation!.Single(v => v.Scenario == StageAdmission.AdmissionScenario).Status,
                Is.EqualTo(ScenarioStatus.Pending));
        }

        private IEnumerator Finish()
        {
            for (int frame = 0; frame < 20 && _bed.Admission.ReadPending(_candidate.Id) != null; frame++)
                yield return null;
            Assert.That(_bed.Admission.ReadPending(_candidate.Id), Is.Null, "Editor updates must finish the admission");
        }

        private void AssertRollback()
        {
            Assert.That(Entry().EffectiveState, Is.EqualTo(ChangeSetState.Failed));
            Assert.That(Directory.Exists(_bed.PackageDirectory), Is.False);
            Assert.That(_bed.Compiler.Requests.Count, Is.EqualTo(2), "install and verified removal both compile");
            Assert.That(File.Exists(Path.Combine(_bed.Admission.StateRoot, "removed", _candidate.Id, "package/package.json")), Is.True);
        }

        [UnityTest]
        public IEnumerator R2_14_PendingSmokePassesAcrossEditorFrames()
        {
            int polls = 0;
            int assertions = 0;
            Options.SmokeTest = _ => { assertions++; return true; };
            Options.PollSmokeTest = verdict =>
            {
                Assert.That(verdict, Is.SameAs(_bed.Admission.VerdictOf(_candidate.Id)));
                Assert.That((int)Pending()["smokeFrames"]!, Is.EqualTo(polls + 1), "charge durably before calling game code");
                return ++polls < 3 ? AdmissionSmokeStatus.Pending : AdmissionSmokeStatus.Passed;
            };
            Begin();
            Assert.That(polls, Is.Zero, "polling starts on the next Editor update");
            Assert.That((int)Pending()["smokeFrameBudget"]!, Is.EqualTo(120));
            Assert.That((long)Pending()["smokeTimeoutMs"]!, Is.EqualTo(60000));
            _bed.Admission.Resume(_candidate.Id);
            _bed.Admission.Resume(_candidate.Id);
            Assert.That(polls, Is.Zero, "manual Resume does not advance smoke within the frame");
            yield return Finish();
            Assert.That(polls, Is.EqualTo(3));
            Assert.That(assertions, Is.EqualTo(1));
            Assert.That(Entry().EffectiveState, Is.EqualTo(ChangeSetState.Applied));
            Assert.That(_bed.Compiler.Requests.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator R2_14_PendingSmokeFailureRollsBack()
        {
            int polls = 0;
            AdmissionResult? finished = null;
            _bed.Admission.Finished += result => finished = result;
            Options.PollSmokeTest = _ => ++polls < 3 ? AdmissionSmokeStatus.Pending : AdmissionSmokeStatus.Failed;
            Begin();
            yield return Finish();
            Assert.That(polls, Is.EqualTo(3));
            Assert.That(finished!.Reason, Is.EqualTo("smoke_failed"));
            AssertRollback();
        }

        [UnityTest]
        public IEnumerator R2_14_PendingSmokeFrameBudgetExhaustion()
        {
            int polls = 0;
            AdmissionResult? finished = null;
            _bed.Admission.Finished += result => finished = result;
            Options.SmokeTestFrameBudget = 3;
            Options.PollSmokeTest = _ => { polls++; return AdmissionSmokeStatus.Pending; };
            Begin();
            Options.SmokeTestFrameBudget = 1000;
            yield return Finish();
            Assert.That(polls, Is.EqualTo(3), "changing options cannot reset a persisted budget");
            Assert.That(finished!.Reason, Is.EqualTo("smoke_budget_exhausted"));
            AssertRollback();
        }

        [UnityTest]
        public IEnumerator R2_14_PendingSmokeWallBudgetSurvivesReloadWithoutTrust()
        {
            int polls = 0;
            Options.PollSmokeTest = _ => { polls++; return AdmissionSmokeStatus.Pending; };
            Begin();
            JObject pending = Pending();
            pending["smokeStartedMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 61000;
            File.WriteAllText(Path.Combine(_bed.Admission.StateRoot, "pending-" + _candidate.Id + ".json"), pending.ToString());
            Options.StageService = null;
            Options.PollSmokeTest = null;
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, Options);
            Assert.That(resumed.VerdictOf(_candidate.Id), Is.Null);
            Assert.That(resumed.Resume(_candidate.Id).Reason, Is.EqualTo("smoke_budget_exhausted"));
            yield return null;
            Assert.That(polls, Is.Zero);
            AssertRollback();
        }

        [UnityTest]
        public IEnumerator R2_14_ReloadDuringPendingSmokeResumesWithRetainedBudget()
        {
            Options.PollSmokeTest = _ => AdmissionSmokeStatus.Pending;
            Begin();
            for (int frame = 0; frame < 20 && (int)Pending()["smokeFrames"]! == 0; frame++) yield return null;
            int frames = (int)Pending()["smokeFrames"]!;
            Assert.That(frames, Is.GreaterThan(0));
            long started = (long)Pending()["smokeStartedMs"]!;
            int polls = 0;
            Options.PollSmokeTest = _ => { polls++; return AdmissionSmokeStatus.Passed; };
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, Options);
            Assert.That(resumed.VerdictOf(_candidate.Id), Is.Null);
            Assert.That(resumed.Resume(_candidate.Id).Outcome, Is.EqualTo(AdmissionOutcome.Pending));
            for (int frame = 0; frame < 20 && (int)Pending()["smokeFrames"]! == frames; frame++) yield return null;
            Assert.That(polls, Is.Zero, "disk verdict bytes cannot authorize polling after reload");
            Assert.That((int)Pending()["smokeFrames"]!, Is.GreaterThan(frames));
            Assert.That((long)Pending()["smokeStartedMs"]!, Is.EqualTo(started));
            resumed.RefreshPendingVerdicts().GetAwaiter().GetResult();
            yield return Finish();
            Assert.That(polls, Is.EqualTo(1), "old instance must stop polling after replacement");
            Assert.That(Entry().EffectiveState, Is.EqualTo(ChangeSetState.Applied));
        }

        [UnityTest]
        public IEnumerator R2_14_ReloadWithoutSmokeAdapterFailsClosed()
        {
            Options.SmokeTestFrameBudget = 3;
            Options.PollSmokeTest = _ => AdmissionSmokeStatus.Pending;
            Begin();
            Options.PollSmokeTest = null;
            StageAdmission resumed = StageAdmission.Configure(_bed.Runtime, Options);
            resumed.RefreshPendingVerdicts().GetAwaiter().GetResult();
            resumed.ResumePending();
            yield return Finish();
            AssertRollback();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void R2_15_UndoRefusesWhileSmokePendingWithoutAdvancing(bool force)
        {
            int polls = 0;
            Options.PollSmokeTest = _ => { polls++; return AdmissionSmokeStatus.Pending; };
            Begin();
            string before = Pending().ToString();
            int finished = 0;
            _bed.Admission.Finished += _ => finished++;
            Assert.That(_bed.Admission.Undo(_candidate.Id).Reason, Is.EqualTo("admission_pending"));
            var handler = new AdmissionHistoryHandler(_bed.Admission);
            HistoryResult result = handler.Handle(Entry(), HistoryAction.Undo, force);
            Assert.That(result.Ok, Is.False);
            Assert.That(result.State, Is.EqualTo(ChangeSetState.Interrupted));
            Assert.That(result.Diagnostics.Select(d => d.Code), Does.Contain(DiagnosticCodes.Refused));
            Assert.That((string?)result.Diagnostics[0].Data?["reason"], Is.EqualTo("admission_pending"));
            // Core currently rejects Interrupted before dispatching to our registered handler.
            // Keep its safe refusal covered; the dispatch-order change is requested from R2-A.
            HistoryResult generic = _bed.Runtime.History.Undo(_candidate.Id, force);
            Assert.That(generic.Ok, Is.False);
            Assert.That(generic.Diagnostics.Select(d => d.Code), Does.Contain(DiagnosticCodes.Refused));
            Assert.That(Pending().ToString(), Is.EqualTo(before));
            Assert.That(polls, Is.Zero);
            Assert.That(finished, Is.Zero);
            Assert.That(Entry().Validation!.Single(v => v.Scenario == StageAdmission.AdmissionScenario).Status,
                Is.EqualTo(ScenarioStatus.Pending));
        }

        [UnityTest]
        public IEnumerator R2_14_PollOnlySmokeNeedsNoSynchronousAdapter()
        {
            Options.SmokeTest = null;
            Options.PollSmokeTest = _ => AdmissionSmokeStatus.Passed;
            Begin();
            yield return Finish();
            Assert.That(Entry().EffectiveState, Is.EqualTo(ChangeSetState.Applied));
        }

        [UnityTest]
        public IEnumerator R2_14_PendingSmokeExceptionFailsClosed()
        {
            Options.PollSmokeTest = _ => throw new InvalidOperationException("smoke assertion failed");
            Begin();
            yield return Finish();
            AssertRollback();
        }

        [Test]
        public void R2_14_SynchronousSmokeFailureStillRollsBackImmediately()
        {
            int polls = 0;
            Options.SmokeTest = _ => false;
            Options.PollSmokeTest = _ => { polls++; return AdmissionSmokeStatus.Passed; };
            Assert.That(_bed.Admission.Admit(_candidate).Outcome, Is.EqualTo(AdmissionOutcome.RolledBack));
            Assert.That(polls, Is.Zero);
            AssertRollback();
        }
    }
}
