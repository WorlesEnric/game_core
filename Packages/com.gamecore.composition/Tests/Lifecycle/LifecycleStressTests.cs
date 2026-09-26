// GameCore.Composition tests — GC-022's unload stress, callback and lifetime acceptance (TEST-015, TEST-016,
// TEST-023).
//
// The counts these tests assert are the ones GC-022 names: "Lease/system/callback/view counts return to baseline
// after bounded retention; no stale result writes authority. Stalled jobs retain reachable buffers; independent
// cleanup continues after a disposer error. Loop nodes/subscriptions do not accumulate."
//
// Two deliberate reading decisions, both recorded in artifacts/gc-022/HANDOFF.md:
//
//   1. "Returns to baseline" is asserted on the *live* counters (`LiveLeaseCount`, `OutstandingCount`,
//      `LiveActivationCount`, `Quarantine.Count`, `AuthorityCount`) and on the ledger state of every acquisition
//      (each record must leave `Acquired`/`Ready`/`Retiring`/`Quarantined` for `Retired`). The record *tables*
//      themselves are a separate fact: `ResourceLedger` keeps one row per acquisition ever made and
//      `JobFenceRegistry` keeps one row per job ever tracked until `Release` is called. Tests 10 and 11 measure
//      that growth exactly instead of hiding it behind a passing assertion, because P-048's baseline is about
//      resources still reachable and P-023's counters are about work still pending — neither is satisfied by
//      pretending the history table is empty.
//   2. A lease whose disposer threw is *not* retried. `ManagedResourceLease.Dispose` records the attempt before
//      running the disposer and refuses a repeat (P-048: "dispose each lease at most once"), so a failed release
//      stays retained and quarantined, and the bounded quarantine registry is what keeps that from being
//      unbounded. Test 7 asserts that behaviour rather than assuming a retry that the protocol does not promise.
//
// Every test is deterministic: identities come from an explicit domain tag and ordinal, no clock is read, and no
// test depends on another's ordering.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class LifecycleStressTests
    {
        /// <summary>The cycle count GC-022 requires for the unload stress (TEST-015).</summary>
        private const int RequiredCycles = 1000;

        /// <summary>Delayed completions GC-022 requires after unload (O-24, P-047).</summary>
        private const int RequiredDelayedCompletions = 100;

        /// <summary>Provider churn repeats: enough to prove the wait/resume edge is not a one-shot.</summary>
        private const int RequiredChurnCycles = 10;

        [Test]
        public void AThousandMountUnmountCyclesReturnEveryLiveCounterToBaseline()
        {
            var rig = new LifecycleStressRig(0x7374726573733031UL, quarantineCapacity: 4096);

            for (int cycle = 0; cycle < RequiredCycles; cycle++)
            {
                LifecycleStressCycle result = rig.MountAndUnload(cycle);
                Assert.That(
                    result.Settled,
                    Is.True,
                    "GC-022/TEST-015: every mount/unmount cycle must publish, settle and retain nothing.\n" + result.Describe());
            }

            Assert.That(rig.Coordinator.UnloadCount, Is.EqualTo(RequiredCycles), "one O-07 unload per cycle");
            Assert.That(rig.Teardown.PassCount, Is.EqualTo(RequiredCycles), "one P-048 pass per cycle");
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(0), "no cycle was blocked: nothing was still reachable");

            // Live counts, which is what "returns to baseline" means (P-048).
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0), "no lease is still retained after 1000 cycles");
            Assert.That(rig.Resources.RetainedResourceIds().Count, Is.EqualTo(0));
            Assert.That(rig.Resources.QuarantinedCount, Is.EqualTo(0));
            Assert.That(rig.Resources.FailedReleaseCount, Is.EqualTo(0));
            Assert.That(rig.Quarantine.Count, Is.EqualTo(0));
            Assert.That(rig.Quarantine.ExhaustionCount, Is.EqualTo(0));
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0), "the callback count returns to baseline (TEST-015)");
            Assert.That(rig.Jobs.OutstandingCount, Is.EqualTo(0), "the job-fence count returns to baseline (TEST-015)");
            Assert.That(rig.Jobs.QuarantinedJobCount, Is.EqualTo(0));
            Assert.That(rig.Activations.AuthorityCount, Is.EqualTo(0), "no installation still holds execution authority");
            Assert.That(rig.Activations.InstallationCount, Is.EqualTo(RequiredCycles), "one activation record per installation identity");

            // Monotone counters must grow by exactly one per operation, never less and never more.
            Assert.That(rig.Activations.DisposedCount, Is.EqualTo(RequiredCycles), "every installation reaches Disposed");
            Assert.That(rig.Activations.RefusedTransitionCount, Is.EqualTo(0), "no legal cycle requested an illegal edge");
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(RequiredCycles * LifecycleStressRig.RolesInAcquisitionOrder.Length));
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(RequiredCycles * LifecycleStressRig.RolesInAcquisitionOrder.Length));
            Assert.That(rig.Factory.FailedDisposalCount, Is.EqualTo(0));
            Assert.That(rig.Factory.PrepareCount, Is.EqualTo(RequiredCycles * LifecycleStressRig.RolesInAcquisitionOrder.Length));

            // Every acquisition of every cycle is traced out of the retained states.
            IReadOnlyList<WorldResourceRecord> records = rig.Resources.Records();
            Assert.That(records.Count, Is.EqualTo(RequiredCycles * LifecycleStressRig.RolesInAcquisitionOrder.Length));
            int retained = 0;
            int byKind = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].IsRetained)
                {
                    retained++;
                }

                if (records[i].Kind == WorldResourceKind.Subscription)
                {
                    byKind++;
                }
            }

            Assert.That(retained, Is.EqualTo(0), "every ledger record left the retained states for Retired");
            Assert.That(
                byKind,
                Is.EqualTo(RequiredCycles),
                "the subscription kind is the one that must not accumulate across cycles (TEST-015, TEST-023)");
        }

        [Test]
        public void EveryAcquisitionOfACycleIsTracedToRetirementOrQuarantineByKind()
        {
            var rig = new LifecycleStressRig(0x7374726573733032UL, quarantineCapacity: 16);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = rig.AcquireAll(instance, activation.Stamp, mount, 1);

            Assert.That(leases.Count, Is.EqualTo(5), "TEST-015: a service lease, system registration, asset lease, callback and subscription");
            for (int i = 0; i < leases.Count; i++)
            {
                Assert.That(FindLease(rig, leases[i].LeaseId).Readiness, Is.EqualTo(ResourceReadiness.Ready),
                    "a published lease is Ready, not Pending (P-029)");
                Assert.That(FindLease(rig, leases[i].LeaseId).Gate.IsOpen, Is.True, "publication opens the gate (P-030)");
            }

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None), report.Describe());
            Assert.That(report.DisposeSettled, Is.True);
            Assert.That(report.CompletedSteps().Count, Is.EqualTo(6), "the whole P-048 order ran");

            for (int i = 0; i < leases.Count; i++)
            {
                StressLease lease = leases[i];
                Assert.That(
                    rig.Resources.TryGetRecord(lease.LeaseId, out WorldResourceRecord record),
                    Is.True,
                    "acquisition " + lease.ToString() + " must be on the ledger (P-048: trace every acquisition)");
                Assert.That(record.State, Is.EqualTo(ResourceRetirementState.Retired), "lease " + lease.ToString());
                Assert.That(record.Kind, Is.EqualTo(lease.Kind), "the role maps to a declared ledger kind");
                Assert.That(record.AcquisitionOrdinal, Is.EqualTo(lease.Ordinal));
                Assert.That(record.Instance.Equals(instance), Is.True);
                Assert.That(record.IsRetained, Is.False);
            }

            // The five roles must be five distinct ledger kinds-or-roles, not five copies of one record.
            Assert.That(KindOf(leases, LifecycleStressRole.ServiceLease), Is.EqualTo(WorldResourceKind.ManagedLease));
            Assert.That(KindOf(leases, LifecycleStressRole.Subscription), Is.EqualTo(WorldResourceKind.Subscription));
            Assert.That(KindOf(leases, LifecycleStressRole.SystemRegistration), Is.EqualTo(WorldResourceKind.SystemRegistration));
            Assert.That(KindOf(leases, LifecycleStressRole.AssetLease), Is.EqualTo(WorldResourceKind.NativeContainer));
            Assert.That(KindOf(leases, LifecycleStressRole.Callback), Is.EqualTo(WorldResourceKind.ManagedLease));

            // Reverse acquisition order within one installation (P-048).
            var expected = new List<Id128>(leases.Count);
            for (int i = leases.Count - 1; i >= 0; i--)
            {
                expected.Add(leases[i].Key.Value);
            }

            Assert.That(rig.Factory.DisposedOrder, Is.EqualTo(expected), "leases dispose in reverse acquisition order (P-048)");
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(leases.Count));
        }

        [Test]
        public void AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement()
        {
            var rig = new LifecycleStressRig(0x7374726573733033UL, quarantineCapacity: 16);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = rig.AcquireAll(instance, activation.Stamp, mount, 1);
            AsyncWorkToken token = rig.TokenFor(instance, activation.Stamp, mount, 90U);

            Assert.That(
                rig.Coordinator.EvaluateCompletion(token),
                Is.EqualTo(CallbackGateDecision.Dispatch),
                "while the activation is live, its own stamped completion is dispatched (P-007)");

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None), report.Describe());
            Assert.That(report.DisposeSettled, Is.True);
            int retiredAfterUnload = rig.Resources.RetiredCount;

            for (int i = 0; i < RequiredDelayedCompletions / 2; i++)
            {
                Assert.That(
                    rig.Coordinator.EvaluateCompletion(token),
                    Is.EqualTo(CallbackGateDecision.DiscardRetiredRoute),
                    "O-24: a completion for a retired route is discarded, never delivered (P-047)");
            }

            // The same stable id is mounted again with a new generation and epoch: every old token is stale.
            StressActivation remount = rig.Activate(instance, 2UL, 2UL, rig.Issuer.Next());
            Assert.That(remount.Transition.Allowed, Is.True, "a Disposed identity is mountable again with a new generation (P-005)");
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(1));

            for (int i = 0; i < RequiredDelayedCompletions / 2; i++)
            {
                Assert.That(
                    rig.Coordinator.EvaluateCompletion(token),
                    Is.EqualTo(CallbackGateDecision.DiscardStaleActivation),
                    "P-005/P-047: the old generation/epoch is not the live one, even though the stable id exists again");
            }

            Assert.That(rig.Callbacks.EvaluatedCount, Is.EqualTo(RequiredDelayedCompletions + 1));
            Assert.That(rig.Callbacks.DiscardedCount, Is.EqualTo(RequiredDelayedCompletions), "all 100 delayed completions were discarded");
            Assert.That(rig.Coordinator.HoldsAuthority(instance), Is.True, "the remount holds authority; the stale tokens did not take it");
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(retiredAfterUnload), "no completion released or re-acquired a resource");
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0));
            Assert.That(rig.Resources.QuarantinedCount, Is.EqualTo(0));

            // The retired lease's own gate refuses dispatch before the callback gate is even asked (P-047).
            ManagedResourceLease retiredLease = FindLease(rig, leases[0].LeaseId);
            Assert.That(retiredLease.IsDisposed, Is.True);
            Assert.That(
                GatedCallbackPath.Evaluate(retiredLease, rig.Callbacks, token),
                Is.EqualTo(CallbackGateDecision.DiscardPostPublicationFence),
                "a closed resource gate drops the work, so a retired activation can never run gameplay code (P-029, P-047)");
            var gate = retiredLease.Gate as ManagedResourceGate;
            Assert.That(gate, Is.Not.Null);
            Assert.That(gate!.DroppedDispatches, Is.EqualTo(1));
            Assert.That(gate.CloseCount, Is.EqualTo(1), "the gate closed exactly once, at retirement");
        }

        [Test]
        public void ACompletionStampedByAnotherWorldIncarnationIsDiscarded()
        {
            var rig = new LifecycleStressRig(0x7374726573733034UL, quarantineCapacity: 8);
            var otherWorld = new WorldId(new Id128(0x6F74686572776F72UL, 0x4C535452455353UL));
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);

            var foreignToken = new AsyncWorkToken(
                new OperationId(otherWorld, rig.Issuer.Id, 1UL),
                instance,
                activation.Stamp.Generation,
                activation.Stamp.ActivationEpoch,
                1U);

            Assert.That(
                rig.Coordinator.EvaluateCompletion(foreignToken),
                Is.EqualTo(CallbackGateDecision.DiscardForeignWorld),
                "P-004: another world incarnation can never write authority here");

            // A closed publication fence drops everything, including a token that would otherwise dispatch.
            AsyncWorkToken own = rig.TokenFor(instance, activation.Stamp, mount, 1U);
            rig.Callbacks.CloseFence();
            Assert.That(rig.Coordinator.EvaluateCompletion(own), Is.EqualTo(CallbackGateDecision.DiscardPostPublicationFence));
            rig.Callbacks.OpenFence();
            Assert.That(rig.Coordinator.EvaluateCompletion(own), Is.EqualTo(CallbackGateDecision.Dispatch), "the fence is a window, not a permanent close");
            Assert.That(rig.Callbacks.DiscardedCount, Is.EqualTo(2));
        }

        [Test]
        public void AStalledJobRetainsItsReachableBuffersUntilItCompletes()
        {
            var rig = new LifecycleStressRig(0x7374726573733035UL, quarantineCapacity: 16);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = rig.AcquireAll(instance, activation.Stamp, mount, 1);
            StressLease buffer = leases[3];

            Id128 jobId = rig.Ids.NextId();
            rig.Jobs.Track(
                jobId,
                instance,
                new StageId(rig.Ids.NextId()),
                rig.Ids.Factory(),
                AssemblyEpoch.First,
                LogicalStepId.Zero,
                new[] { buffer.LeaseId });

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());

            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked), report.Describe());
            Assert.That(report.DisposeSettled, Is.False, "P-048: a false value is never a false success");
            Assert.That(report.BlockedByJobFence, Is.True, "TEST-015: an unfinished job keeps its buffer alive");
            Assert.That(report.OutstandingJobs, Is.EqualTo(1));
            Assert.That(report.FencedResources.Count, Is.EqualTo(1));
            Assert.That(report.FencedResources[0].Equals(buffer.LeaseId), Is.True, "the fenced resource is the one the job may reach");
            Assert.That(report.Quarantined.Count, Is.EqualTo(1));
            Assert.That(report.HasCleanupErrors, Is.False, "nothing threw; the buffer is retained because it is reachable");
            Assert.That(
                report.Cleanup.Retired.Count,
                Is.EqualTo(4),
                "P-048: independent cleanup continues for the resources no user reaches");
            Assert.That(FindLease(rig, buffer.LeaseId).IsDisposed, Is.False, "the buffered release must not happen");
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(4));
            Assert.That(rig.Quarantine.Contains(buffer.LeaseId), Is.True);
            Assert.That(rig.Quarantine.EntriesFor(instance).Count, Is.EqualTo(1));
            Assert.That(rig.Jobs.IsOutstanding(jobId), Is.True);
            Assert.That(rig.Jobs.IsResourceFenced(buffer.LeaseId), Is.True);
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(1));
            Assert.That(rig.Resources.RetainedCountFor(instance), Is.EqualTo(1));
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(1));
            Assert.That(rig.Activations.TryGetCurrent(instance, out ActivationAttempt? current), Is.True);
            Assert.That(current!.State, Is.EqualTo(InstallationState.Retiring), "a retained resource keeps the installation Retiring (P-048)");
            Assert.That(
                rig.Activations.RefusedTransitionCount,
                Is.EqualTo(1),
                "the unload's own settle was refused: a retained resource is never a Disposed report (P-048)");
            Assert.That(rig.Activations.DisposedCount, Is.EqualTo(0), "no installation is reported Disposed while a job owns its buffer");

            // Completion is the only event that ends the fence, and the release is explicit, never time-based.
            Assert.That(rig.Jobs.Complete(jobId), Is.True, "the job's own completion is the fence's end");
            Assert.That(rig.Coordinator.HoldsAuthority(instance), Is.False);
            CleanupReport released = rig.Coordinator.ReleaseQuarantineFor(instance);

            Assert.That(released.Failed.Count, Is.EqualTo(0));
            Assert.That(released.Retired.Count, Is.EqualTo(1));
            Assert.That(released.Retired[0].Equals(buffer.LeaseId), Is.True);
            Assert.That(FindLease(rig, buffer.LeaseId).IsDisposed, Is.True);
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(5), "the buffered release happened exactly once, after the user ended");
            Assert.That(rig.Quarantine.Count, Is.EqualTo(0));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0));
            Assert.That(rig.Resources.RetainedCountFor(instance), Is.EqualTo(0));
            Assert.That(rig.Jobs.OutstandingCount, Is.EqualTo(0));
            Assert.That(
                rig.Activations.Settle(instance, true).Allowed,
                Is.True,
                "with every resource settled the explicit settle is the lawful Retiring -> Disposed edge (P-048)");
            Assert.That(rig.Activations.TryGetCurrent(instance, out ActivationAttempt? settled), Is.True);
            Assert.That(settled!.State, Is.EqualTo(InstallationState.Disposed), "only a settled installation becomes Disposed (P-048)");
        }

        [Test]
        public void AThrowingDisposerIsQuarantinedWhileIndependentCleanupContinues()
        {
            var rig = new LifecycleStressRig(0x7374726573733036UL, quarantineCapacity: 16);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = rig.AcquireAll(instance, activation.Stamp, mount, 1);
            rig.Factory.FailingDisposals.Add(leases[1].Key.Value);

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());

            Assert.That(report.HasCleanupErrors, Is.True, "the failing release is reported (P-048)");
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked), report.Describe());
            Assert.That(report.DisposeSettled, Is.False);
            Assert.That(report.FailedReleases.Count, Is.EqualTo(1));
            Assert.That(report.FailedReleases[0].Equals(leases[1].LeaseId), Is.True);
            Assert.That(report.Quarantined.Count, Is.EqualTo(1));
            Assert.That(report.Cleanup.Retired.Count, Is.EqualTo(4));
            Assert.That(FindLease(rig, leases[1].LeaseId).IsDisposed, Is.False, "a failed release is never reported as a disposal (P-048)");
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(4));
            Assert.That(rig.Factory.FailedDisposalCount, Is.EqualTo(1));

            // The failing acquisition was second, so the *first* acquisition disposes after it and still runs:
            // independent cleanup continues after a disposer error (TEST-016 row 8).
            var expectedDisposed = new List<Id128> { leases[4].Key.Value, leases[3].Key.Value, leases[2].Key.Value, leases[0].Key.Value };
            Assert.That(rig.Factory.DisposedOrder, Is.EqualTo(expectedDisposed));
            Assert.That(rig.Factory.FailedDisposalOrder, Is.EqualTo(new List<Id128> { leases[1].Key.Value }));

            Assert.That(rig.Resources.TryGetRecord(leases[0].LeaseId, out WorldResourceRecord healthy), Is.True);
            Assert.That(healthy.State, Is.EqualTo(ResourceRetirementState.Retired));
            Assert.That(rig.Resources.TryGetRecord(leases[1].LeaseId, out WorldResourceRecord failed), Is.True);
            Assert.That(failed.State, Is.EqualTo(ResourceRetirementState.Quarantined), "a failed release stays retained (P-048)");
            Assert.That(rig.Resources.QuarantinedCount, Is.EqualTo(1));
            Assert.That(rig.Resources.FailedReleaseCount, Is.EqualTo(1));
            Assert.That(rig.Resources.RetainedCountFor(instance), Is.EqualTo(1));
            Assert.That(rig.Quarantine.Contains(leases[1].LeaseId), Is.True);

            // The pin is per-lease: a later failure-free cycle still settles completely.
            LifecycleStressCycle next = rig.MountAndUnload(2);
            Assert.That(next.Settled, Is.True, "one quarantined reference does not stop the next installation settling.\n" + next.Describe());
        }

        [Test]
        public void AFailedReleaseIsNeverRetriedAndItsQuarantineStaysBounded()
        {
            const int capacity = 8;
            var rig = new LifecycleStressRig(0x7374726573733037UL, quarantineCapacity: capacity);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = rig.AcquireAll(instance, activation.Stamp, mount, 1);
            rig.Factory.FailingDisposals.Add(leases[2].Key.Value);

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());
            Assert.That(report.FailedReleases.Count, Is.EqualTo(1), report.Describe());

            // Clearing the script does not make the lease releasable: Dispose records the attempt before running
            // the disposer, and P-048 permits exactly one (ManagedResources.cs:141-164).
            rig.Factory.FailingDisposals.Clear();
            CleanupReport released = rig.Coordinator.ReleaseQuarantineFor(instance);

            Assert.That(released.Retired.Count, Is.EqualTo(0), "a lease whose first release failed is not retried (P-048)");
            Assert.That(released.Failed.Count, Is.EqualTo(1));
            Assert.That(released.Quarantined.Count, Is.EqualTo(1), "the still-retained reference is reported, not dropped");
            Assert.That(rig.Quarantine.Contains(leases[2].LeaseId), Is.True, "the reference is retained, never dropped (06 s6)");
            Assert.That(rig.Resources.RetainedCountFor(instance), Is.EqualTo(1));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(1));
            Assert.That(rig.Resources.FailedReleaseCount, Is.EqualTo(2), "the retry attempt is recorded as a second failure");
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(4), "the other four acquisitions were released normally");

            // What keeps this from being unbounded is the declared bound, not elapsed time.
            Assert.That(rig.Quarantine.MaxEntries, Is.EqualTo(capacity));
            Assert.That(rig.Quarantine.Count, Is.LessThanOrEqualTo(rig.Quarantine.MaxEntries));
            Assert.That(rig.Quarantine.IsExhausted, Is.False);
            Assert.That(
                rig.Quarantine.Bytes,
                Is.EqualTo(0UL),
                "the composition ledger's Acquire records no byte size, so the composition-side quarantine bound is the entry count");
        }

        [Test]
        public void RequiredProviderChurnWaitsAndResumesTheConsumerOnEveryCycle()
        {
            var rig = new LifecycleStressRig(0x7374726573733038UL, quarantineCapacity: 64);
            PluginInstanceId consumer = rig.Ids.Instance();
            var consumerGeneration = InstallationGeneration.First;
            ulong consumerEpoch = 1UL;

            // Cycle 0 is a mount whose required provider is absent: a published waiting instance with no
            // contribution and no lease (P-046, P-012).
            LifecycleTransition waiting = rig.Activations.WaitOnRegistration(
                consumer, consumerGeneration, new ActivationEpoch(consumerEpoch), rig.Issuer.Next());
            Assert.That(waiting.Allowed, Is.True);
            Assert.That(rig.Activations.WaitingRegistrationCount, Is.EqualTo(1));

            int resumeCount = 0;
            for (int cycle = 0; cycle < RequiredChurnCycles; cycle++)
            {
                Assert.That(
                    rig.Activations.InstallationsIn(InstallationState.WaitingForDependencies).Count,
                    Is.EqualTo(1),
                    "cycle " + cycle.ToString(CultureInfo.InvariantCulture) + ": the consumer waits while the provider is absent");

                // The provider returns on a fresh identity, acquires its own service lease, and the consumer
                // resumes in the same publication with its contribution back (P-012).
                PluginInstanceId provider = rig.Ids.Instance();
                StressActivation providerActivation = rig.Activate(provider, (ulong)cycle + 1UL, 1UL, rig.Issuer.Next());
                Assert.That(providerActivation.Transition.Allowed, Is.True, "provider mount, cycle " + cycle.ToString(CultureInfo.InvariantCulture));
                StressLease providerLease = rig.Acquire(provider, providerActivation.Stamp, rig.Issuer.Next(), LifecycleStressRole.ServiceLease, 1U);

                consumerEpoch++;
                OperationId resumeOperation = rig.Issuer.Next();
                LifecycleTransition resume = rig.Activations.ResumeFromWaiting(
                    consumer, consumerGeneration, new ActivationEpoch(consumerEpoch), resumeOperation);
                Assert.That(resume.Allowed, Is.True, "O-04: a provider's return resumes the waiting consumer, cycle "
                    + cycle.ToString(CultureInfo.InvariantCulture));
                resumeCount++;
                var consumerStamp = new ActivationStamp(consumerGeneration, new ActivationEpoch(consumerEpoch));
                rig.Callbacks.RegisterActivation(consumer, consumerStamp.Generation, consumerStamp.ActivationEpoch);
                StressLease consumerLease = rig.Acquire(consumer, consumerStamp, resumeOperation, LifecycleStressRole.Subscription, 1U);
                Assert.That(rig.Activations.AuthorityCount, Is.EqualTo(2), "the provider and the resumed consumer both hold authority (P-046)");

                // The provider is lost again: the consumer waits and retracts in the same publication, and the
                // consumer's contribution is released before the provider's (P-048 reverse dependency order).
                LifecycleTransition loss = rig.Activations.WaitForDependencies(consumer);
                Assert.That(loss.Allowed, Is.True, "O-06/P-012: provider loss makes the consumer wait, cycle "
                    + cycle.ToString(CultureInfo.InvariantCulture));
                TeardownReport consumerTeardown = rig.Teardown.Unload(consumer, consumerStamp, rig.Issuer.Next(), true);
                Assert.That(consumerTeardown.DisposeSettled, Is.True, consumerTeardown.Describe());
                Assert.That(consumerTeardown.Cleanup.Retired.Count, Is.EqualTo(1), "the consumer's own contribution retracted");
                Assert.That(
                    rig.Teardown.RetractionCount,
                    Is.EqualTo(0),
                    "the composition-only binding retracts nothing itself: the caller owes the assembly publication (P-048)");
                Assert.That(rig.Callbacks.TryGetActivation(consumer, out ActivationStamp _), Is.False,
                    "a waiting consumer holds no live activation, so its callbacks are discarded (P-047)");
                Assert.That(FindLease(rig, consumerLease.LeaseId).IsDisposed, Is.True);

                TeardownReport providerTeardown = rig.Coordinator.Unload(provider, rig.Issuer.Next());
                Assert.That(providerTeardown.Code, Is.EqualTo(DiagnosticCode.None), providerTeardown.Describe());
                Assert.That(providerTeardown.DisposeSettled, Is.True);
                Assert.That(FindLease(rig, providerLease.LeaseId).IsDisposed, Is.True);

                Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0), "both sides return to baseline inside the cycle");
                Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0));
                Assert.That(rig.Jobs.OutstandingCount, Is.EqualTo(0));
            }

            Assert.That(resumeCount, Is.EqualTo(RequiredChurnCycles), "the wait/resume edge is not a one-shot");
            Assert.That(rig.Activations.ResumedCount, Is.EqualTo(RequiredChurnCycles));
            Assert.That(rig.Activations.RetractedCount, Is.EqualTo(RequiredChurnCycles));
            Assert.That(rig.Activations.DisposedCount, Is.EqualTo(RequiredChurnCycles), "one provider per cycle reached Disposed");
            Assert.That(
                rig.Activations.InstallationsIn(InstallationState.WaitingForDependencies).Count,
                Is.EqualTo(1),
                "P-012: losing a provider makes the consumer wait; it does not remove the consumer");
            Assert.That(rig.Activations.AuthorityCount, Is.EqualTo(0), "nothing holds authority once the provider is gone");
            Assert.That(rig.Teardown.PassCount, Is.EqualTo(RequiredChurnCycles * 2), "one pass for the consumer's retraction and one for the provider's removal, per cycle");
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0));
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(RequiredChurnCycles * 2));
            Assert.That(rig.Resources.QuarantinedCount, Is.EqualTo(0));
            Assert.That(rig.Quarantine.Count, Is.EqualTo(0));
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0));
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(0));
        }

        [Test]
        public void QuarantineExhaustionRefusesAdmissionInsteadOfDroppingReferences()
        {
            var rig = new LifecycleStressRig(0x7374726573733039UL, quarantineCapacity: 1);
            PluginInstanceId instance = rig.Ids.Instance();
            OperationId mount = rig.Issuer.Next();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, mount);
            IReadOnlyList<StressLease> leases = rig.AcquireAll(instance, activation.Stamp, mount, 1);
            rig.Factory.FailingDisposals.Add(leases[0].Key.Value);
            rig.Factory.FailingDisposals.Add(leases[4].Key.Value);

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());

            Assert.That(report.Cleanup.Retired.Count, Is.EqualTo(3));
            Assert.That(report.FailedReleases.Count, Is.EqualTo(2));
            Assert.That(rig.Quarantine.Count, Is.EqualTo(1), "the bound is honoured");
            Assert.That(rig.Quarantine.ExhaustionCount, Is.EqualTo(1), "the second admission is refused, not silently accepted");
            Assert.That(rig.Quarantine.LastRefusalCode, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked), report.Describe());
            Assert.That(
                rig.Resources.RetainedCountFor(instance),
                Is.EqualTo(2),
                "06 s6: an exhausted registry refuses admission and the references stay on the books, never dropped");
            Assert.That(rig.Resources.RetainedResourceIds().Count, Is.EqualTo(2));
            Assert.That(report.Quarantined.Count, Is.EqualTo(2), "both retained references are reported to the caller");
        }

        [Test]
        public void CompletedJobRecordsAccumulateUntilExplicitlyReleased()
        {
            const int cycles = 50;
            var rig = new LifecycleStressRig(0x7374726573733130UL, quarantineCapacity: 16);
            var jobIds = new List<Id128>(cycles);

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                PluginInstanceId instance = rig.Ids.Instance();
                OperationId mount = rig.Issuer.Next();
                StressActivation activation = rig.Activate(instance, (ulong)cycle + 1UL, 1UL, mount);
                StressLease lease = rig.Acquire(instance, activation.Stamp, mount, LifecycleStressRole.AssetLease, 1U);
                Id128 jobId = rig.Ids.NextId();
                rig.Jobs.Track(
                    jobId,
                    instance,
                    new StageId(rig.Ids.NextId()),
                    rig.Ids.Factory(),
                    AssemblyEpoch.First,
                    LogicalStepId.Zero,
                    new[] { lease.LeaseId });
                Assert.That(rig.Jobs.Complete(jobId), Is.True, "the job finishes before its installation is unloaded");
                jobIds.Add(jobId);

                TeardownReport teardown = rig.Coordinator.Unload(instance, rig.Issuer.Next());
                Assert.That(teardown.DisposeSettled, Is.True, "a completed job no longer fences anything (P-047)");
            }

            // The live count is at baseline, which is what P-047 requires.
            Assert.That(rig.Jobs.OutstandingCount, Is.EqualTo(0));
            Assert.That(rig.Jobs.QuarantinedJobCount, Is.EqualTo(0));
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0));

            // The record table is a different fact and it grows: nothing in the repository calls
            // JobFenceRegistry.Release, so a completed job's record — and the resource ids it holds — is retained
            // for the life of the ledger (JobFenceRegistry.cs:298-315 is the only removal path).
            Assert.That(
                rig.Jobs.TrackedCount,
                Is.EqualTo(cycles),
                "GC-022 finding: completed job records accumulate until a caller releases them; the release API exists and nothing calls it");
            Assert.That(rig.Jobs.CompletedCount, Is.EqualTo(cycles));
            Assert.That(rig.Jobs.RegisteredCount, Is.EqualTo(cycles));
            Assert.That(rig.Jobs.ReleasedCount, Is.EqualTo(0));

            // The removal path does what a fix needs: it hands back exactly the resources the job held.
            for (int i = 0; i < jobIds.Count; i++)
            {
                Assert.That(rig.Jobs.Release(jobIds[i], out IReadOnlyList<Id128>? resources), Is.True);
                Assert.That(resources, Is.Not.Null);
                Assert.That(resources!.Count, Is.EqualTo(1));
            }

            Assert.That(rig.Jobs.TrackedCount, Is.EqualTo(0), "an explicitly released record leaves the table");
            Assert.That(rig.Jobs.ReleasedCount, Is.EqualTo(cycles));
            Assert.That(rig.Jobs.OutstandingCount, Is.EqualTo(0));
        }

        [Test]
        public void RetiredLedgerRecordsAreRetainedAndTheRetainedBoundIsReported()
        {
            const int cycles = 100;
            var rig = new LifecycleStressRig(0x7374726573733131UL, quarantineCapacity: 32);

            for (int cycle = 0; cycle < cycles; cycle++)
            {
                LifecycleStressCycle result = rig.MountAndUnload(cycle);
                Assert.That(result.Settled, Is.True, result.Describe());
            }

            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0), "nothing is still reachable");
            Assert.That(rig.Resources.RetainedResourceIds().Count, Is.EqualTo(0));
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(cycles * LifecycleStressRig.RolesInAcquisitionOrder.Length));

            // The ledger's history table has no eviction path: one row per acquisition ever made. This is a
            // measured, linear growth and it is reported rather than hidden — see artifacts/gc-022/HANDOFF.md
            // "retained history" for the bound and the proposed fix.
            Assert.That(
                rig.Resources.Records().Count,
                Is.EqualTo(cycles * LifecycleStressRig.RolesInAcquisitionOrder.Length),
                "GC-022 finding: ResourceLedger keeps one record per acquisition, retired or not");
            Assert.That(rig.Resources.QuarantinedCount, Is.EqualTo(0));
            Assert.That(rig.Resources.FailedReleaseCount, Is.EqualTo(0));
            Assert.That(rig.Quarantine.Count, Is.EqualTo(0), "no reference ends in quarantine on a clean run");
        }

        [Test]
        public void AnUnloadOfACycleThatNeverRanRetainsNothing()
        {
            // The zero-case: a cycle with no acquisition at all must still be a legal, settled pass with an
            // explicit empty report, so "nothing to release" is never confused with "the release was not observed".
            var rig = new LifecycleStressRig(0x7374726573733132UL, quarantineCapacity: 4);
            PluginInstanceId instance = rig.Ids.Instance();
            StressActivation activation = rig.Activate(instance, 1UL, 1UL, rig.Issuer.Next());
            Assert.That(activation.Transition.Allowed, Is.True);

            TeardownReport report = rig.Coordinator.Unload(instance, rig.Issuer.Next());
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None), report.Describe());
            Assert.That(report.DisposeSettled, Is.True);
            Assert.That(report.Cleanup.Retired.Count, Is.EqualTo(0));
            Assert.That(report.FailedReleases.Count, Is.EqualTo(0));
            Assert.That(report.FencedResources.Count, Is.EqualTo(0));
            Assert.That(report.OutstandingJobs, Is.EqualTo(0));
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0));
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(0));
            Assert.That(rig.Activations.TryGetCurrent(instance, out ActivationAttempt? current), Is.True);
            Assert.That(current!.State, Is.EqualTo(InstallationState.Disposed));

            // Repeating the unload is idempotent for the ledger and settled for the activation (P-050, P-048).
            TeardownReport again = rig.Coordinator.Unload(instance, rig.Issuer.Next());
            Assert.That(again.DisposeSettled, Is.True, again.Describe());
            Assert.That(again.Cleanup.Retired.Count, Is.EqualTo(0));
            Assert.That(rig.Resources.RetiredCount, Is.EqualTo(0), "a repeated pass releases nothing a second time");
        }

        private static ManagedResourceLease FindLease(LifecycleStressRig rig, Id128 leaseId)
        {
            IReadOnlyList<ManagedResourceLease> leases = rig.Factory.Leases;
            for (int i = 0; i < leases.Count; i++)
            {
                if (leases[i].LeaseId.Equals(leaseId))
                {
                    return leases[i];
                }
            }

            throw new InvalidOperationException("No lease " + leaseId.ToString() + " was prepared by this rig.");
        }

        private static WorldResourceKind KindOf(IReadOnlyList<StressLease> leases, LifecycleStressRole role)
        {
            for (int i = 0; i < leases.Count; i++)
            {
                if (leases[i].Role == role)
                {
                    return leases[i].Kind;
                }
            }

            throw new InvalidOperationException("The cycle acquired no " + role + ".");
        }
    }
}
