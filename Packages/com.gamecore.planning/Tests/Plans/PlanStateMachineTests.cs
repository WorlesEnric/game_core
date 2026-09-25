// GameCore.Planning tests — plan states and transitions (GC-008, 05 s4, P-027 to P-031).
//
// The cases here defend the distinctions the protocol calls load-bearing: `Rejected` made no live writes,
// `PublishedWithCleanupErrors` is already authoritative, `Faulted` means live storage is unsafe, and no terminal
// state admits a second transition. They also defend the state-dependency rules: only one plan per world may be
// applying, and the expected-revision recheck never mutates the plan by itself (P-028).
#nullable enable
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Planning.Tests
{
    [TestFixture]
    public sealed class PlanStateMachineTests
    {
        private static PlanStateMachine Machine()
        {
            WorldId world = PlansFixtureKeys.World(1UL);
            return new PlanStateMachine(
                PlansFixture.Plan(PlansFixture.MountProposal(world, PlansFixture.Revision, PlansFixture.Epoch)).Plan);
        }

        [Test]
        public void AFreshPlanStartsInDraftAndIsNotTerminal()
        {
            PlanStateMachine state = Machine();

            Assert.That(state.Phase, Is.EqualTo(PlanPhase.Draft));
            Assert.That(state.Outcome, Is.EqualTo(Outcome.Pending));
            Assert.That(state.IsTerminal, Is.False);
            Assert.That(state.HasCrossedLiveWriteBoundary, Is.False);
            Assert.That(state.History, Is.Empty, "no transition has been driven yet");
        }

        [Test]
        public void TheLegalSequenceReachesPublishedWithBothOutcomes()
        {
            PlanStateMachine state = Machine();

            Assert.That(state.TryValidate(out _), Is.True);
            Assert.That(state.TryPrepare(out _), Is.True);
            Assert.That(state.TryBeginApplying(out _), Is.True);
            Assert.That(state.HasCrossedLiveWriteBoundary, Is.True, "applying is the postwrite cutoff (P-031)");
            Assert.That(state.TryPublish(Outcome.PublishedWithCleanupErrors, out DiagnosticCode code), Is.True);
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(state.IsTerminal, Is.True);
            Assert.That(state.PublishedWithCleanupErrors, Is.True, "cleanup errors do not unpublish the assembly (05 s4)");
            Assert.That(state.Outcome, Is.EqualTo(Outcome.PublishedWithCleanupErrors));
            Assert.That(state.History.Count, Is.EqualTo(4));
        }

        [Test]
        public void ATerminalPlanRefusesEveryFurtherTransition()
        {
            PlanStateMachine state = Machine();
            state.TryValidate(out _);
            state.TryPrepare(out _);
            state.TryBeginApplying(out _);
            state.TryPublish(Outcome.Published, out _);

            int refusals = state.RefusedTransitionCount;

            Assert.That(state.TryPrepare(out _), Is.False, "a published plan cannot be prepared again");
            Assert.That(state.TryFault(DiagnosticCode.ApplyFault, "late"), Is.False);
            Assert.That(state.TryCancel("late"), Is.False);
            Assert.That(state.TryReject(DiagnosticCode.StalePlan, "late"), Is.False);
            Assert.That(state.RefusedTransitionCount, Is.EqualTo(refusals + 4));
            Assert.That(state.Phase, Is.EqualTo(PlanPhase.Published));
            Assert.That(state.Outcome, Is.EqualTo(Outcome.Published));
        }

        [Test]
        public void RejectionIsOnlyLegalBeforeLiveWritesAreClaimed()
        {
            PlanStateMachine fromDraft = Machine();
            Assert.That(fromDraft.TryReject(DiagnosticCode.StalePlan, "stale base"), Is.True);
            Assert.That(fromDraft.Phase, Is.EqualTo(PlanPhase.Rejected));
            Assert.That(fromDraft.HasCrossedLiveWriteBoundary, Is.False, "a rejected plan made no live writes (05 s4)");

            PlanStateMachine fromApplying = Machine();
            fromApplying.TryValidate(out _);
            fromApplying.TryPrepare(out _);
            fromApplying.TryBeginApplying(out _);
            Assert.That(fromApplying.TryReject(DiagnosticCode.StalePlan, "too late"), Is.False,
                "once applying, the outcome must be Published or Faulted (05 s4)");
            Assert.That(fromApplying.TryFault(DiagnosticCode.ApplyFault, "postwrite"), Is.True);
            Assert.That(fromApplying.Phase, Is.EqualTo(PlanPhase.Faulted));
            Assert.That(fromApplying.Code, Is.EqualTo(DiagnosticCode.ApplyFault));
        }

        [Test]
        public void CancellationIsRefusedAfterTheCutoff()
        {
            PlanStateMachine state = Machine();
            state.TryValidate(out _);
            state.TryPrepare(out _);

            Assert.That(state.TryCancel("caller cancelled before applying"), Is.True);
            Assert.That(state.Phase, Is.EqualTo(PlanPhase.Cancelled));
            Assert.That(state.Code, Is.EqualTo(DiagnosticCode.Cancelled));

            PlanStateMachine applied = Machine();
            applied.TryValidate(out _);
            applied.TryPrepare(out _);
            applied.TryBeginApplying(out _);
            Assert.That(applied.TryCancel("too late"), Is.False, "cancellation after Applying is TooLate (P-051)");
        }

        [Test]
        public void PublishingRequiresAPublishedOutcome()
        {
            PlanStateMachine state = Machine();
            state.TryValidate(out _);
            state.TryPrepare(out _);
            state.TryBeginApplying(out _);

            Assert.That(state.TryPublish(Outcome.Rejected, out DiagnosticCode code), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(state.Phase, Is.EqualTo(PlanPhase.Applying), "the refused call changed nothing");
        }

        [Test]
        public void TheBaseRecheckComparesRevisionAndEpochWithoutMutatingThePlan()
        {
            PlanStateMachine state = Machine();

            Assert.That(state.RecheckBase(PlansFixture.Revision, PlansFixture.Epoch, out DiagnosticCode ok), Is.True);
            Assert.That(ok, Is.EqualTo(DiagnosticCode.None));

            Assert.That(
                state.RecheckBase(new CompositionRevision(PlansFixture.Revision.Value + 1UL), PlansFixture.Epoch, out DiagnosticCode stale),
                Is.False);
            Assert.That(stale, Is.EqualTo(DiagnosticCode.StalePlan));

            Assert.That(
                state.RecheckBase(PlansFixture.Revision, new AssemblyEpoch(PlansFixture.Epoch.Value + 1UL), out DiagnosticCode staleEpoch),
                Is.False);
            Assert.That(staleEpoch, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(state.Phase, Is.EqualTo(PlanPhase.Draft), "a recheck never changes the plan by itself (P-028)");
        }

        [Test]
        public void NoChangeIsAnOutcomeOfATerminalNonErrorState()
        {
            PlanStateMachine state = Machine();
            state.TryValidate(out _);
            state.TryPrepare(out _);

            Assert.That(state.TryNoChange("nothing changed"), Is.True);
            Assert.That(state.Phase, Is.EqualTo(PlanPhase.Published));
            Assert.That(state.Outcome, Is.EqualTo(Outcome.NoChange));
            Assert.That(state.TryPrepare(out _), Is.False, "a no-op plan is terminal too");
        }
    }
}
