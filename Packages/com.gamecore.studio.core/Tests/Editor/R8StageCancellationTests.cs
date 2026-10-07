#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using GameCore.Studio.Authoring.Agent;
using GameCore.Studio.Model;
using NUnit.Framework;

namespace GameCore.Studio.Edit.Tests
{
    public sealed class R8StageCancellationTests
    {
        [Test]
        public async Task W_REC_03_CancellationDoesNotCompleteBeforeServiceTeardownAcknowledgment()
        {
            using (var bed = new StudioTestBed())
            {
                ChangeSet candidate = StudioTestBed.NewChangeSet("Stage cancellation", null);
                bed.Runtime.Journal.Write(candidate);
                var service = new ControlledService();
                StageAdmission admission = StageAdmission.Of(bed.Runtime);
                admission.Options.StageService = service;
                admission.MarkStagePending(candidate.Id, "slot");
                Task cancelling = admission.CancelStage("stg_owned", candidate.Id);
                Assert.That(cancelling.IsCompleted, Is.False);
                Assert.That(bed.Runtime.Journal.Read(candidate.Id)!.Validation!
                    .Single(s => s.Scenario == StageAdmission.VerdictScenario).Status, Is.EqualTo(ScenarioStatus.Pending));
                service.Completion.SetResult("cancelled");
                await cancelling;
                Assert.That(bed.Runtime.Journal.Read(candidate.Id)!.Validation!
                    .Single(s => s.Scenario == StageAdmission.VerdictScenario).Status, Is.EqualTo(ScenarioStatus.Fail));
                Assert.That(admission.VerdictOf(candidate.Id), Is.Null);
            }
            return;
        }

        [Test]
        public void W_REC_03_AlreadyFinishedJobIsNotReportedAsCancelled()
        {
            using (var bed = new StudioTestBed())
            {
                ChangeSet candidate = StudioTestBed.NewChangeSet("Completed stage", null);
                bed.Runtime.Journal.Write(candidate);
                var service = new ControlledService();
                service.Completion.SetResult("done");
                StageAdmission admission = StageAdmission.Of(bed.Runtime);
                admission.Options.StageService = service;
                admission.MarkStagePending(candidate.Id, "slot");
                Assert.ThrowsAsync<InvalidOperationException>(() => admission.CancelStage("stg_done", candidate.Id));
                Assert.That(bed.Runtime.Journal.Read(candidate.Id)!.Validation!
                    .Single(s => s.Scenario == StageAdmission.VerdictScenario).Status, Is.EqualTo(ScenarioStatus.Pending));
            }
        }

        private sealed class ControlledService : IStageService, IStageJobControl
        {
            public TaskCompletionSource<string> Completion { get; } = new TaskCompletionSource<string>();
            public Task<string> CancelStage(string jobId) => Completion.Task;
            public Task<string> RequestStage(StageCandidateRequest request) => throw new NotSupportedException();
            public Task<SignedVerdict> GetVerdict(string jobId) => throw new NotSupportedException();
            public Task<StageVerification> VerifyVerdict(string jobId, StageVerificationRequest request) => throw new NotSupportedException();
        }
    }
}
