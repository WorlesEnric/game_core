// GameCore.Composition tests — the P-046 activation ledger: one live activation plus one staged candidate.
//
// `ActivationLedger` is pure: it owns no resource, no gate and no live world state, and it reports every illegal
// edge as a value (`LifecycleTransition`) instead of throwing. The fixtures below therefore assert the ledger's
// own observable facts — the attempt identity a transition produced, the authority that attempt holds, and the
// counter a refusal increments — because those are what a caller reads to decide gate effects.
//
// Every fixture uses its own domain and builds its own ledger, so no state is shared between tests (P-008).
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class ActivationLedgerTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x776F726C64UL, 0x4006UL));

        private sealed class Rig
        {
            public Rig(ulong domain)
            {
                Ids = new IdFactory(domain);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
            }

            public IdFactory Ids { get; }

            public OperationIssuer Issuer { get; }

            /// <summary>One ledger per fixture: no activation state is shared between tests.</summary>
            public ActivationLedger Ledger { get; } = new ActivationLedger();

            public InstallationGeneration Generation { get; } = InstallationGeneration.First;

            public ActivationEpoch Epoch { get; } = ActivationEpoch.First;

            /// <summary>Activates one freshly minted installation and proves the mount edge was permitted.</summary>
            public PluginInstanceId ActivateNew()
            {
                PluginInstanceId instance = Ids.Instance();
                LifecycleTransition transition = Ledger.Activate(instance, Generation, Epoch, Issuer.Next());
                Assert.That(transition.Allowed, Is.True, transition.ToString());
                return instance;
            }
        }

        /// <summary>The live activation of one installation; a missing one is a test failure, not a silent null.</summary>
        private static ActivationAttempt Current(ActivationLedger ledger, PluginInstanceId instance)
        {
            Assert.That(ledger.TryGetCurrent(instance, out ActivationAttempt? current), Is.True, "the installation must have a live activation");
            Assert.That(current, Is.Not.Null);
            return current!;
        }

        /// <summary>The staged candidate of one installation; the ledger must have recorded one.</summary>
        private static ActivationAttempt Candidate(ActivationLedger ledger, PluginInstanceId instance)
        {
            Assert.That(ledger.TryGetCandidate(instance, out ActivationAttempt? candidate), Is.True, "the installation must have a staged candidate");
            Assert.That(candidate, Is.Not.Null);
            return candidate!;
        }

        [Test]
        public void FirstActivationIsPermittedAndALiveDuplicateIdentityIsRefused()
        {
            Rig rig = new Rig(0x61637431UL);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();

            LifecycleTransition activation = rig.Ledger.Activate(instance, rig.Generation, rig.Epoch, mount);

            Assert.That(activation.Allowed, Is.True, activation.ToString());
            Assert.That(activation.From, Is.EqualTo(InstallationState.Preparing));
            Assert.That(activation.To, Is.EqualTo(InstallationState.Active));
            Assert.That(activation.Code, Is.EqualTo(DiagnosticCode.None));

            // "Preparing -> Active" is one publication: the attempt the ledger stores is already Active (P-046).
            ActivationAttempt live = Current(rig.Ledger, instance);
            Assert.That(live.State, Is.EqualTo(InstallationState.Active));
            Assert.That(live.HoldsAuthority, Is.True, "an active activation holds execution authority (P-046).");
            Assert.That(live.IsCandidate, Is.False);
            Assert.That(live.Operation.Equals(mount), Is.True, "the attempt names the operation that activated it (P-050).");
            Assert.That(rig.Ledger.RegisteredActivationCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));

            // Reusing a stable identity that is still live is a duplicate live id, not a second activation (P-004).
            LifecycleTransition duplicate = rig.Ledger.Activate(instance, rig.Generation, rig.Epoch, rig.Issuer.Next());

            Assert.That(duplicate.Allowed, Is.False);
            Assert.That(duplicate.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(1));

            ActivationAttempt unchanged = Current(rig.Ledger, instance);
            Assert.That(unchanged.AttemptId.Equals(live.AttemptId), Is.True, "a refused activation never replaces the live attempt.");
            Assert.That(unchanged.HoldsAuthority, Is.True);
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.RegisteredActivationCount, Is.EqualTo(1));
        }

        [Test]
        public void StagingACandidateKeepsTheRunningActivationInCharge()
        {
            Rig rig = new Rig(0x61637432UL);
            PluginInstanceId instance = rig.ActivateNew();
            ActivationAttempt before = Current(rig.Ledger, instance);

            LifecycleTransition staged = rig.Ledger.StageCandidate(instance, rig.Generation, new ActivationEpoch(2UL), rig.Issuer.Next());

            Assert.That(staged.Allowed, Is.True, staged.ToString());
            Assert.That(rig.Ledger.StagedCandidateCount, Is.EqualTo(1));

            ActivationAttempt candidate = Candidate(rig.Ledger, instance);
            Assert.That(candidate.IsCandidate, Is.True);
            Assert.That(candidate.State, Is.EqualTo(InstallationState.Preparing));
            Assert.That(candidate.HoldsAuthority, Is.False, "a staged candidate is inert until publication (P-046).");
            Assert.That(candidate.Generation.Equals(rig.Generation), Is.True, "an in-place replacement keeps the generation (P-005).");

            // The old activation keeps running: same attempt, still Active, still the authority.
            ActivationAttempt live = Current(rig.Ledger, instance);
            Assert.That(live.AttemptId.Equals(before.AttemptId), Is.True, "the current activation is untouched by staging.");
            Assert.That(live.State, Is.EqualTo(InstallationState.Active));
            Assert.That(live.HoldsAuthority, Is.True);
            Assert.That(candidate.AttemptId.Equals(live.AttemptId), Is.False, "the candidate is a separate attempt, not a state change.");
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));
        }

        [Test]
        public void StagingASecondCandidateIsRefusedAndLeavesTheFirstOneStaged()
        {
            Rig rig = new Rig(0x61637433UL);
            PluginInstanceId instance = rig.ActivateNew();
            Assert.That(rig.Ledger.StageCandidate(instance, rig.Generation, rig.Epoch, rig.Issuer.Next()).Allowed, Is.True);
            ActivationAttempt first = Candidate(rig.Ledger, instance);

            LifecycleTransition second = rig.Ledger.StageCandidate(instance, rig.Generation, new ActivationEpoch(2UL), rig.Issuer.Next());

            Assert.That(second.Allowed, Is.False);
            Assert.That(second.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(second.To, Is.EqualTo(InstallationState.Preparing));
            Assert.That(rig.Ledger.StagedCandidateCount, Is.EqualTo(1), "one installation stages at most one candidate (P-046).");
            Assert.That(Candidate(rig.Ledger, instance).AttemptId.Equals(first.AttemptId), Is.True, "the refused staging never overwrote the staged candidate.");
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(1));
        }

        [Test]
        public void StagingIsRefusedForASuspendedActivation()
        {
            Rig rig = new Rig(0x61637434UL);
            PluginInstanceId instance = rig.ActivateNew();
            Assert.That(rig.Ledger.Suspend(instance).Allowed, Is.True);

            LifecycleTransition refused = rig.Ledger.StageCandidate(instance, rig.Generation, new ActivationEpoch(2UL), rig.Issuer.Next());

            Assert.That(refused.Allowed, Is.False);
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(rig.Ledger.StagedCandidateCount, Is.EqualTo(0), "only an Active activation can be replaced in place (P-046).");
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(1));
            Assert.That(Current(rig.Ledger, instance).State, Is.EqualTo(InstallationState.Suspended));
        }

        [Test]
        public void CommitMovesTheCandidateInAndDisplacesTheRunningActivationToRetiring()
        {
            Rig rig = new Rig(0x61637435UL);
            PluginInstanceId instance = rig.ActivateNew();
            ActivationAttempt predecessor = Current(rig.Ledger, instance);
            ActivationEpoch replacement = new ActivationEpoch(2UL);
            Assert.That(rig.Ledger.StageCandidate(instance, rig.Generation, replacement, rig.Issuer.Next()).Allowed, Is.True);
            ActivationAttempt candidate = Candidate(rig.Ledger, instance);

            LifecycleTransition commit = rig.Ledger.CommitCandidate(instance, out ActivationAttempt? displaced);

            Assert.That(commit.Allowed, Is.True, commit.ToString());
            Assert.That(commit.To, Is.EqualTo(InstallationState.Active));
            Assert.That(displaced, Is.Not.Null);
            Assert.That(displaced!.State, Is.EqualTo(InstallationState.Retiring), "the displaced activation walks Active -> Quiescing -> Retiring at one publication (P-046).");
            Assert.That(displaced.IsCandidate, Is.False, "the displaced activation was the authority, not a candidate.");
            Assert.That(displaced.AttemptId.Equals(predecessor.AttemptId), Is.True);
            Assert.That(displaced.HoldsAuthority, Is.False, "a retiring activation has already lost authority (P-048).");
            Assert.That(rig.Ledger.CommittedReplacementCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.RetiredCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.StagedCandidateCount, Is.EqualTo(0), "the committed candidate is no longer staged.");

            ActivationAttempt live = Current(rig.Ledger, instance);
            Assert.That(live.AttemptId.Equals(candidate.AttemptId), Is.True, "the staged candidate takes over the installation.");
            Assert.That(live.State, Is.EqualTo(InstallationState.Active));
            Assert.That(live.ActivationEpoch.Equals(replacement), Is.True);
            Assert.That(live.HoldsAuthority, Is.True);
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));

            Assert.That(rig.Ledger.TryGetCandidate(instance, out ActivationAttempt? leftover), Is.False);
            Assert.That(leftover, Is.Null);
        }

        [Test]
        public void AbortingACandidateFailsItAndLeavesTheRunningActivationInCharge()
        {
            Rig rig = new Rig(0x61637436UL);
            PluginInstanceId instance = rig.ActivateNew();
            ActivationAttempt live = Current(rig.Ledger, instance);
            Assert.That(rig.Ledger.StageCandidate(instance, rig.Generation, new ActivationEpoch(2UL), rig.Issuer.Next()).Allowed, Is.True);

            LifecycleTransition abort = rig.Ledger.AbortCandidate(instance, out ActivationAttempt? aborted);

            Assert.That(abort.Allowed, Is.True, abort.ToString());
            Assert.That(abort.To, Is.EqualTo(InstallationState.Failed));
            Assert.That(aborted, Is.Not.Null);
            Assert.That(aborted!.State, Is.EqualTo(InstallationState.Failed));
            Assert.That(aborted.IsCandidate, Is.True);
            Assert.That(aborted.HoldsAuthority, Is.False);
            Assert.That(rig.Ledger.AbortedCandidateCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.StagedCandidateCount, Is.EqualTo(0));

            // A failed candidate never costs the running activation (P-046).
            ActivationAttempt stillLive = Current(rig.Ledger, instance);
            Assert.That(stillLive.AttemptId.Equals(live.AttemptId), Is.True);
            Assert.That(stillLive.State, Is.EqualTo(InstallationState.Active));
            Assert.That(stillLive.HoldsAuthority, Is.True);
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));

            Assert.That(rig.Ledger.TryGetCandidate(instance, out ActivationAttempt? none), Is.False);
            Assert.That(none, Is.Null);
        }

        [Test]
        public void SuspendWalksThroughQuiescingAndResumeRecordsANewAttempt()
        {
            Rig rig = new Rig(0x61637437UL);
            PluginInstanceId instance = rig.ActivateNew();
            ActivationAttempt first = Current(rig.Ledger, instance);

            LifecycleTransition suspended = rig.Ledger.Suspend(instance);

            Assert.That(suspended.Allowed, Is.True, suspended.ToString());
            Assert.That(suspended.From, Is.EqualTo(InstallationState.Quiescing), "suspension walks Active -> Quiescing -> Suspended (P-046).");
            Assert.That(suspended.To, Is.EqualTo(InstallationState.Suspended));
            Assert.That(rig.Ledger.SuspendedCount, Is.EqualTo(1));

            ActivationAttempt retracted = Current(rig.Ledger, instance);
            Assert.That(retracted.AttemptId.Equals(first.AttemptId), Is.True, "a suspend keeps the attempt; it only loses authority.");
            Assert.That(retracted.State, Is.EqualTo(InstallationState.Suspended));
            Assert.That(retracted.HoldsAuthority, Is.False, "a suspended activation holds no execution authority (P-046).");
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(0));

            ActivationEpoch resumedEpoch = new ActivationEpoch(2UL);
            LifecycleTransition resume = rig.Ledger.Resume(instance, rig.Generation, resumedEpoch, rig.Issuer.Next());

            Assert.That(resume.Allowed, Is.True, resume.ToString());
            Assert.That(resume.To, Is.EqualTo(InstallationState.Active));
            Assert.That(rig.Ledger.ResumedCount, Is.EqualTo(1));

            ActivationAttempt live = Current(rig.Ledger, instance);
            Assert.That(live.AttemptId.Equals(first.AttemptId), Is.False, "a resume records a new attempt, not the old one revived.");
            Assert.That(live.State, Is.EqualTo(InstallationState.Active));
            Assert.That(live.ActivationEpoch.Equals(resumedEpoch), Is.True);
            Assert.That(live.IsCandidate, Is.False);
            Assert.That(live.HoldsAuthority, Is.True);
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));
        }

        [Test]
        public void WaitingForDependenciesRetractsOnceAndResumeFromWaitingRestoresAuthority()
        {
            Rig rig = new Rig(0x61637438UL);
            PluginInstanceId instance = rig.ActivateNew();
            ActivationAttempt first = Current(rig.Ledger, instance);

            LifecycleTransition waiting = rig.Ledger.WaitForDependencies(instance);

            Assert.That(waiting.Allowed, Is.True, waiting.ToString());
            Assert.That(waiting.From, Is.EqualTo(InstallationState.Quiescing), "the only path from Active to Waiting runs through Quiescing (P-046).");
            Assert.That(waiting.To, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(rig.Ledger.RetractedCount, Is.EqualTo(1));

            ActivationAttempt retracted = Current(rig.Ledger, instance);
            Assert.That(retracted.State, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(retracted.HoldsAuthority, Is.False);
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(0));

            // A repeated provider-loss publication is a permitted no-op, not a second retraction (P-012).
            LifecycleTransition repeated = rig.Ledger.WaitForDependencies(instance);

            Assert.That(repeated.Allowed, Is.True);
            Assert.That(repeated.From, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(repeated.To, Is.EqualTo(InstallationState.WaitingForDependencies));
            Assert.That(rig.Ledger.RetractedCount, Is.EqualTo(1), "a second provider-loss publication must not retract twice.");
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(0), "the repeated wait is permitted, not refused.");

            LifecycleTransition resumed = rig.Ledger.ResumeFromWaiting(instance, rig.Generation, new ActivationEpoch(2UL), rig.Issuer.Next());

            Assert.That(resumed.Allowed, Is.True, resumed.ToString());
            Assert.That(resumed.To, Is.EqualTo(InstallationState.Active));
            Assert.That(rig.Ledger.ResumedCount, Is.EqualTo(1));
            ActivationAttempt live = Current(rig.Ledger, instance);
            Assert.That(live.AttemptId.Equals(first.AttemptId), Is.False, "a dependency return records a new attempt (P-012).");
            Assert.That(live.State, Is.EqualTo(InstallationState.Active));
            Assert.That(live.HoldsAuthority, Is.True);
            Assert.That(rig.Ledger.RetractedCount, Is.EqualTo(1));

            // The dependency path is not a general resume: an Active activation cannot take it.
            LifecycleTransition refused = rig.Ledger.ResumeFromWaiting(instance, rig.Generation, new ActivationEpoch(3UL), rig.Issuer.Next());

            Assert.That(refused.Allowed, Is.False);
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.ResumedCount, Is.EqualTo(1), "a refused resume records no second attempt.");
            Assert.That(Current(rig.Ledger, instance).AttemptId.Equals(live.AttemptId), Is.True);
        }

        [Test]
        public void RetireWalksTheTeardownPathAndSettlingRequiresEveryResourceSettled()
        {
            Rig rig = new Rig(0x61637439UL);
            PluginInstanceId instance = rig.ActivateNew();

            // The documented path Retire walks from Active: Active -> Quiescing -> Retiring (06 s1).
            Assert.That(InstallationStateMachine.TryTeardownPath(InstallationState.Active, out IReadOnlyList<InstallationState>? path), Is.True);
            Assert.That(path, Is.EqualTo(new[] { InstallationState.Quiescing, InstallationState.Retiring }));

            LifecycleTransition retire = rig.Ledger.Retire(instance);

            Assert.That(retire.Allowed, Is.True, retire.ToString());
            Assert.That(retire.From, Is.EqualTo(InstallationState.Active));
            Assert.That(retire.To, Is.EqualTo(InstallationState.Retiring));
            Assert.That(rig.Ledger.RetiredCount, Is.EqualTo(1));

            ActivationAttempt retiring = Current(rig.Ledger, instance);
            Assert.That(retiring.State, Is.EqualTo(InstallationState.Retiring));
            Assert.That(retiring.HoldsAuthority, Is.False);
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(0));

            // A still-retained resource keeps the installation in Retiring and refuses the edge (P-048).
            LifecycleTransition retained = rig.Ledger.Settle(instance, false);

            Assert.That(retained.Allowed, Is.False);
            Assert.That(retained.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(retained.From, Is.EqualTo(InstallationState.Retiring));
            Assert.That(retained.To, Is.EqualTo(InstallationState.Disposed));
            Assert.That(rig.Ledger.DisposedCount, Is.EqualTo(0));
            Assert.That(Current(rig.Ledger, instance).State, Is.EqualTo(InstallationState.Retiring));

            LifecycleTransition settled = rig.Ledger.Settle(instance, true);

            Assert.That(settled.Allowed, Is.True, settled.ToString());
            Assert.That(settled.From, Is.EqualTo(InstallationState.Retiring));
            Assert.That(settled.To, Is.EqualTo(InstallationState.Disposed));
            Assert.That(rig.Ledger.DisposedCount, Is.EqualTo(1));
            Assert.That(Current(rig.Ledger, instance).State, Is.EqualTo(InstallationState.Disposed));

            // Disposed is terminal: a repeated settle permits the no-op and counts nothing a second time.
            Assert.That(rig.Ledger.Settle(instance, true).Allowed, Is.True);
            Assert.That(rig.Ledger.DisposedCount, Is.EqualTo(1));
        }

        [Test]
        public void InstallationsInReturnsExactlyTheInstancesInThatStateInCanonicalOrder()
        {
            Rig rig = new Rig(0x61637441UL);
            PluginInstanceId first = rig.ActivateNew();
            PluginInstanceId second = rig.ActivateNew();
            PluginInstanceId third = rig.ActivateNew();
            Assert.That(rig.Ledger.Suspend(second).Allowed, Is.True);

            IReadOnlyList<PluginInstanceId> active = rig.Ledger.InstallationsIn(InstallationState.Active);
            IReadOnlyList<PluginInstanceId> suspended = rig.Ledger.InstallationsIn(InstallationState.Suspended);
            IReadOnlyList<PluginInstanceId> retiring = rig.Ledger.InstallationsIn(InstallationState.Retiring);

            Assert.That(active.Count, Is.EqualTo(2), "only the two still-active installations are reported.");
            Assert.That(active[0].Equals(first), Is.True, "results are in canonical identity order.");
            Assert.That(active[1].Equals(third), Is.True);
            Assert.That(suspended.Count, Is.EqualTo(1));
            Assert.That(suspended[0].Equals(second), Is.True, "the suspended installation is exactly the one that was suspended.");
            Assert.That(retiring, Is.Empty, "a state nothing occupies yields nothing.");
            Assert.That(rig.Ledger.InstallationCount, Is.EqualTo(3));
        }

        [Test]
        public void ARefusedEdgeCountsTheRefusalAndLeavesTheStoredStateUntouched()
        {
            Rig rig = new Rig(0x61637442UL);
            PluginInstanceId instance = rig.ActivateNew();
            ActivationAttempt before = Current(rig.Ledger, instance);

            // Resume is the suspended/failed path: Active has no Preparing successor in the diagram (P-046).
            LifecycleTransition refused = rig.Ledger.Resume(instance, rig.Generation, new ActivationEpoch(2UL), rig.Issuer.Next());

            Assert.That(refused.Allowed, Is.False);
            Assert.That(refused.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(refused.From, Is.EqualTo(InstallationState.Active));
            Assert.That(refused.To, Is.EqualTo(InstallationState.Preparing));
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(1));
            Assert.That(rig.Ledger.LastRefusalCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(rig.Ledger.LastRefusalDetail, Is.Not.Empty);

            ActivationAttempt after = Current(rig.Ledger, instance);
            Assert.That(after.AttemptId.Equals(before.AttemptId), Is.True, "a refused edge changes no stored fact.");
            Assert.That(after.State, Is.EqualTo(InstallationState.Active));
            Assert.That(rig.Ledger.ResumedCount, Is.EqualTo(0));
            Assert.That(rig.Ledger.AuthorityCount, Is.EqualTo(1));

            // The same rule holds on a later state: Retiring has no Quiescing successor either.
            Assert.That(rig.Ledger.Retire(instance).Allowed, Is.True);
            LifecycleTransition fromRetiring = rig.Ledger.Quiesce(instance);

            Assert.That(fromRetiring.Allowed, Is.False);
            Assert.That(rig.Ledger.RefusedTransitionCount, Is.EqualTo(2));
            Assert.That(Current(rig.Ledger, instance).State, Is.EqualTo(InstallationState.Retiring));
        }
    }
}
