// GameCore.Composition tests — the P-048 teardown order, its report, and the bounded quarantine registry.
//
// `TeardownSequencer` owns no state of its own: every fact it reports is read from the real resource ledger, the
// tracked-job fence, the quarantine registry and the world binding. The fixtures below therefore build a rig of
// those real collaborators (plus one recording `ILifecycleWorldBinding`, because a faulted step boundary is a
// fact only a caller can script) and assert the observable consequence: what was released, what was retained,
// which step reported it, and in which order.
//
// The rig acquires leases straight into the ledger with the installation's own activation stamp, which is the
// stamp a teardown acts on, so a lease is retired exactly when its acquiring activation's stamp matches (P-005).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Composition;
using NUnit.Framework;

namespace GameCore.Composition.Tests
{
    [TestFixture]
    public sealed class TeardownAndQuarantineTests
    {
        /// <summary>Frozen resource configuration of the rig's acquisitions; immutable, so it is shared safely.</summary>
        private static readonly FrozenPayload ResourceConfig = new FrozenPayload(new byte[] { 1 });

        /// <summary>
        /// Recording world binding: counts the P-048 calls the sequencer makes and scripts the answers a real world
        /// would give — the fenced resources of unfinished work, the step settlement, and the contribution
        /// retraction of step 4, whose attributed and retracted row counts are deliberately separate facts (P-017).
        /// </summary>
        private sealed class RecordingBinding : ILifecycleWorldBinding
        {
            public int CloseCalls { get; private set; }

            public int SettleCalls { get; private set; }

            public int FenceCalls { get; private set; }

            public int RetractCalls { get; private set; }

            /// <summary>Resources the world reports as still reachable by unfinished work (P-047).</summary>
            public IReadOnlyList<Id128> Fenced { get; set; } = Array.Empty<Id128>();

            /// <summary>What the world reports at the step boundary; default is a clean, committed boundary.</summary>
            public LifecycleStepSettlement Settlement { get; set; } = LifecycleStepSettlement.At(LogicalStepId.Zero);

            /// <summary>Retracted rows this binding claims; zero keeps the honest "the caller publishes" answer.</summary>
            public int RetractedRows { get; set; }

            /// <summary>Derived binding rows this binding attributes to the installation (P-017).</summary>
            public int AttributedRows { get; set; }

            public LifecycleIngressClosure CloseIngress(PluginInstanceId instance, ActivationStamp stamp)
            {
                CloseCalls++;
                return new LifecycleIngressClosure(instance, stamp, 0, 0);
            }

            public bool ReopenIngress(PluginInstanceId instance, ActivationStamp stamp)
            {
                _ = instance;
                _ = stamp;
                return true;
            }

            public LifecycleStepSettlement SettleCurrentStep()
            {
                SettleCalls++;
                return Settlement;
            }

            public IReadOnlyList<Id128> FenceUsers(PluginInstanceId instance, ActivationStamp stamp)
            {
                _ = instance;
                _ = stamp;
                FenceCalls++;
                return Fenced;
            }

            public ContributionRetraction RetractContributions(PluginInstanceId instance, ActivationStamp stamp, OperationId operation)
            {
                _ = stamp;
                _ = operation;
                RetractCalls++;
                return new ContributionRetraction(
                    instance,
                    AttributedRows,
                    RetractedRows,
                    RetractedRows != 0,
                    0,
                    0,
                    RetractedRows != 0 ? "this binding published the retraction" : "the caller publishes the assembly");
            }
        }

        private sealed class Rig
        {
            public Rig(ulong domain)
            {
                Ids = new IdFactory(domain);
                World = new WorldId(new Id128(domain, 0x4008UL));
                Factory = new TestResourceFactory(Ids);
                Resources = new ResourceLedger();
                Jobs = new JobFenceRegistry();
                Quarantine = new QuarantineRegistry(maxEntries: 16, maxBytes: 0UL);
                Callbacks = new CallbackGate(World);
                Binding = new RecordingBinding();
                Teardown = new TeardownSequencer(Resources, Jobs, Quarantine, Callbacks, Binding);
                Issuer = new OperationIssuer(World, new Id128(domain, 1UL));
                Instance = Ids.Instance();
                Stamp = new ActivationStamp(InstallationGeneration.First, ActivationEpoch.First);
                Operation = Issuer.Next();
            }

            public IdFactory Ids { get; }

            public WorldId World { get; }

            public TestResourceFactory Factory { get; }

            public ResourceLedger Resources { get; }

            public JobFenceRegistry Jobs { get; }

            public QuarantineRegistry Quarantine { get; }

            public CallbackGate Callbacks { get; }

            public RecordingBinding Binding { get; }

            public TeardownSequencer Teardown { get; }

            public OperationIssuer Issuer { get; }

            public PluginInstanceId Instance { get; }

            public ActivationStamp Stamp { get; }

            /// <summary>The operation whose publication caused the teardown (P-050).</summary>
            public OperationId Operation { get; }

            /// <summary>Registers the activation with the world's callback gate, as a publication would (P-047).</summary>
            public OperationId Register(PluginInstanceId instance)
            {
                Callbacks.RegisterActivation(instance, Stamp.Generation, Stamp.ActivationEpoch);
                return Issuer.Next();
            }

            /// <summary>Acquires one real lease into the ledger under this installation's activation stamp.</summary>
            public ManagedResourceLease Acquire(ResourceKey resource, uint ordinal)
            {
                AsyncWorkToken token = new AsyncWorkToken(Operation, Instance, Stamp.Generation, Stamp.ActivationEpoch, ordinal);
                ManagedResourceLease lease = (ManagedResourceLease)Factory.Prepare(new ManagedResourceRequest(resource, token, ResourceConfig));
                Assert.That(Resources.Acquire(lease, WorldResourceKind.ManagedLease, Ids.Owner(), Instance, ordinal, default(Id128)), Is.True);
                Assert.That(Resources.MarkReady(lease.LeaseId), Is.True);
                return lease;
            }
        }

        private static bool HasId(IReadOnlyList<Id128> ids, Id128 candidate)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i].Equals(candidate))
                {
                    return true;
                }
            }

            return false;
        }

        private static void Admit(QuarantineRegistry registry, Id128 resourceId, ResourceKey key, PluginInstanceId instance, OperationId operation, ulong bytes)
        {
            QuarantineAdmission admission = registry.Admit(
                resourceId,
                key,
                instance,
                operation,
                ResourceRetirementState.Quarantined,
                bytes,
                "unfinished work still reaches this resource (P-047)");
            Assert.That(admission.Admitted, Is.True, admission.Detail);
        }

        [Test]
        public void TeardownDisposesLeasesInReverseAcquisitionOrderAndSettlesCleanly()
        {
            Rig rig = new Rig(0x74656131UL);
            OperationId operation = rig.Register(rig.Instance);
            ResourceKey firstKey = rig.Ids.Resource();
            ResourceKey secondKey = rig.Ids.Resource();
            ResourceKey thirdKey = rig.Ids.Resource();
            ManagedResourceLease first = rig.Acquire(firstKey, 1U);
            ManagedResourceLease second = rig.Acquire(secondKey, 2U);
            ManagedResourceLease third = rig.Acquire(thirdKey, 3U);

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, operation, false);

            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.None), report.Describe());
            Assert.That(report.DisposeSettled, Is.True);
            Assert.That(report.Blocked, Is.False);
            Assert.That(report.Quarantined, Is.Empty);
            Assert.That(report.FencedResources, Is.Empty);
            Assert.That(report.Cleanup.Retired.Count, Is.EqualTo(3));
            Assert.That(report.Cleanup.Failed, Is.Empty);
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(3));
            Assert.That(rig.Factory.DisposedOrder, Is.EqualTo(new[] { thirdKey.Value, secondKey.Value, firstKey.Value }),
                "P-048 retires in reverse acquisition order within one installation.");
            Assert.That(first.IsDisposed, Is.True);
            Assert.That(second.IsDisposed, Is.True);
            Assert.That(third.IsDisposed, Is.True);
            Assert.That(rig.Resources.RetainedCountFor(rig.Instance), Is.EqualTo(0), "a settled teardown retains nothing.");
            Assert.That(rig.Resources.LiveLeaseCount, Is.EqualTo(0));

            // Step 1 retires the gate before any resource is touched, so a late completion is discarded (P-047).
            Assert.That(rig.Callbacks.LiveActivationCount, Is.EqualTo(0));
            Assert.That(rig.Callbacks.TryGetActivation(rig.Instance, out ActivationStamp _), Is.False, "step 1 retires the activation so its late completions are discarded (P-047).");
            Assert.That(rig.Teardown.PassCount, Is.EqualTo(1));
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(0));
        }

        [Test]
        public void ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes()
        {
            Rig rig = new Rig(0x74656132UL);
            OperationId operation = rig.Register(rig.Instance);
            ManagedResourceLease lease = rig.Acquire(rig.Ids.Resource(), 1U);
            Id128 jobId = rig.Ids.NextId();
            rig.Jobs.Track(
                jobId,
                rig.Instance,
                new StageId(rig.Ids.NextId()),
                rig.Ids.Factory(),
                AssemblyEpoch.First,
                LogicalStepId.Zero,
                new[] { lease.LeaseId });

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, operation, true);

            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(0), "unfinished work still reaches the buffer, so it must not be released (P-047).");
            Assert.That(lease.IsDisposed, Is.False);
            Assert.That(report.BlockedByJobFence, Is.True);
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(report.DisposeSettled, Is.False);
            Assert.That(report.OutstandingJobs, Is.EqualTo(1));
            Assert.That(report.FencedResources.Count, Is.EqualTo(1));
            Assert.That(report.FencedResources[0].Equals(lease.LeaseId), Is.True, "the fenced resource is the one the job may reach.");
            Assert.That(report.Quarantined.Count, Is.EqualTo(1));
            Assert.That(report.Quarantined[0].Equals(lease.LeaseId), Is.True);
            Assert.That(rig.Quarantine.Contains(lease.LeaseId), Is.True);
            Assert.That(rig.Quarantine.EntriesFor(rig.Instance).Count, Is.EqualTo(1));
            Assert.That(rig.Jobs.IsOutstanding(jobId), Is.True);
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(1));

            // Completion is the only event that ends the fence; the quarantine is then released explicitly.
            Assert.That(rig.Jobs.Complete(jobId), Is.True);
            CleanupReport released = rig.Teardown.ReleaseQuarantineFor(rig.Instance);

            Assert.That(released.Retired.Count, Is.EqualTo(1));
            Assert.That(released.Retired[0].Equals(lease.LeaseId), Is.True);
            Assert.That(released.Failed, Is.Empty);
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(1), "the buffered release happens exactly once, after the user ended.");
            Assert.That(lease.IsDisposed, Is.True);
            Assert.That(rig.Quarantine.Count, Is.EqualTo(0));
            Assert.That(rig.Resources.RetainedCountFor(rig.Instance), Is.EqualTo(0));
        }

        [Test]
        public void AThrowingDisposerKeepsItsReferenceAndIndependentCleanupContinues()
        {
            Rig rig = new Rig(0x74656133UL);
            OperationId operation = rig.Register(rig.Instance);
            ResourceKey healthyKey = rig.Ids.Resource();
            ResourceKey failingKey = rig.Ids.Resource();
            ResourceKey laterKey = rig.Ids.Resource();
            ManagedResourceLease healthy = rig.Acquire(healthyKey, 1U);
            ManagedResourceLease failing = rig.Acquire(failingKey, 2U);
            ManagedResourceLease later = rig.Acquire(laterKey, 3U);
            rig.Factory.FailingDisposals.Add(failingKey.Value);

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, operation, true);

            Assert.That(report.HasCleanupErrors, Is.True);
            Assert.That(report.DisposeSettled, Is.False);
            Assert.That(report.FailedReleases.Count, Is.EqualTo(1));
            Assert.That(report.FailedReleases[0].Equals(failing.LeaseId), Is.True);
            Assert.That(failing.IsDisposed, Is.False, "a failed release is never reported as a disposal (P-048)");

            // The failed reference stays on the books, so the pass reports the reference it is retaining rather
            // than a settled teardown (TeardownSequencer.cs:310-313: a retained reference wins over the code).
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(HasId(report.Quarantined, failing.LeaseId), Is.True);
            Assert.That(rig.Quarantine.Contains(failing.LeaseId), Is.True);
            Assert.That(rig.Quarantine.EntriesFor(rig.Instance).Count, Is.EqualTo(1));

            Assert.That(report.Cleanup.Retired.Count, Is.EqualTo(2));
            Assert.That(HasId(report.Cleanup.Retired, healthy.LeaseId), Is.True);
            Assert.That(HasId(report.Cleanup.Retired, later.LeaseId), Is.True);
            Assert.That(healthy.IsDisposed, Is.True);
            Assert.That(later.IsDisposed, Is.True);
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(2));
            Assert.That(rig.Resources.RetainedCountFor(rig.Instance), Is.EqualTo(1), "only the failed release is still retained.");
        }

        [Test]
        public void QuarantineExhaustionAndDuplicateAdmissionAreRefusedWithoutDroppingReferences()
        {
            QuarantineRegistry registry = new QuarantineRegistry(maxEntries: 2, maxBytes: 0UL);
            Rig rig = new Rig(0x74656134UL);
            Id128 first = rig.Ids.NextId();
            Id128 second = rig.Ids.NextId();
            Id128 third = rig.Ids.NextId();
            ResourceKey key = rig.Ids.Resource();
            OperationId operation = rig.Issuer.Next();

            Admit(registry, first, key, rig.Instance, operation, 30UL);
            Admit(registry, second, key, rig.Instance, operation, 40UL);

            Assert.That(registry.Count, Is.EqualTo(2));
            Assert.That(registry.Bytes, Is.EqualTo(70UL));
            Assert.That(registry.IsExhausted, Is.True, "two retained references fill a registry bounded at two (06 s6).");

            QuarantineAdmission duplicate = registry.Admit(first, key, rig.Instance, operation, ResourceRetirementState.Quarantined, 30UL, "the same reference again");

            Assert.That(duplicate.Admitted, Is.False);
            Assert.That(duplicate.Code, Is.EqualTo(DiagnosticCode.OwnershipConflict), "a quarantine is a set, not a counter (06 s6).");
            Assert.That(registry.DuplicateCount, Is.EqualTo(1));
            Assert.That(registry.Bytes, Is.EqualTo(70UL), "a refused duplicate adds no bytes.");

            QuarantineAdmission overflow = registry.Admit(third, key, rig.Instance, operation, ResourceRetirementState.Quarantined, 50UL, "one too many");

            Assert.That(overflow.Admitted, Is.False);
            Assert.That(overflow.Code, Is.EqualTo(DiagnosticCode.TeardownBlocked), "exhaustion refuses instead of dropping references (06 s6).");
            Assert.That(registry.ExhaustionCount, Is.EqualTo(1));
            Assert.That(registry.LastRefusalCode, Is.EqualTo(DiagnosticCode.TeardownBlocked));
            Assert.That(registry.Count, Is.EqualTo(2), "the existing references stay on the books.");
            Assert.That(registry.Contains(first), Is.True);
            Assert.That(registry.Contains(second), Is.True);
            Assert.That(registry.Contains(third), Is.False);
            Assert.That(registry.Bytes, Is.EqualTo(70UL));

            IReadOnlyList<QuarantinedResource> entries = registry.Entries();
            Assert.That(entries.Count, Is.EqualTo(2));
            Assert.That(entries[0].ResourceId.Equals(first), Is.True, "the retained set is reported in canonical resource order.");
            Assert.That(entries[1].ResourceId.Equals(second), Is.True);
            Assert.That(entries[0].Bytes, Is.EqualTo(30UL));
            Assert.That(entries[0].Reason, Is.Not.Empty, "a retained reference always says why it is held (P-048).");
        }

        [Test]
        public void QuarantineReleaseRemovesExactlyOneReferenceAndItsBytes()
        {
            QuarantineRegistry registry = new QuarantineRegistry(maxEntries: 4, maxBytes: 0UL);
            Rig rig = new Rig(0x74656135UL);
            Id128 first = rig.Ids.NextId();
            Id128 second = rig.Ids.NextId();
            ResourceKey key = rig.Ids.Resource();
            OperationId operation = rig.Issuer.Next();
            Admit(registry, first, key, rig.Instance, operation, 25UL);
            Admit(registry, second, key, rig.Instance, operation, 60UL);
            Assert.That(registry.Bytes, Is.EqualTo(85UL));

            Assert.That(registry.Release(first), Is.True);

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.Bytes, Is.EqualTo(60UL), "releasing a reference reduces the retained bytes by that entry's bytes.");
            Assert.That(registry.Contains(first), Is.False);
            Assert.That(registry.Contains(second), Is.True, "the other reference is untouched.");
            Assert.That(registry.ReleasedCount, Is.EqualTo(1));
            Assert.That(registry.Release(first), Is.False, "a reference is released at most once (P-048).");
            Assert.That(registry.Count, Is.EqualTo(1));
        }

        [Test]
        public void ReleaseInstanceReleasesOnlyThatInstancesEntries()
        {
            QuarantineRegistry registry = new QuarantineRegistry(maxEntries: 8, maxBytes: 0UL);
            Rig rig = new Rig(0x74656136UL);
            PluginInstanceId other = rig.Ids.Instance();
            Id128 mineOne = rig.Ids.NextId();
            Id128 mineTwo = rig.Ids.NextId();
            Id128 theirs = rig.Ids.NextId();
            ResourceKey key = rig.Ids.Resource();
            OperationId operation = rig.Issuer.Next();
            Admit(registry, mineOne, key, rig.Instance, operation, 10UL);
            Admit(registry, mineTwo, key, rig.Instance, operation, 20UL);
            Admit(registry, theirs, key, other, operation, 30UL);

            Assert.That(registry.ReleaseInstance(rig.Instance), Is.EqualTo(2));

            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.Bytes, Is.EqualTo(30UL));
            Assert.That(registry.Contains(theirs), Is.True, "another installation's reference is never released by this call.");
            Assert.That(registry.EntriesFor(other).Count, Is.EqualTo(1));
            Assert.That(registry.EntriesFor(rig.Instance), Is.Empty);
            Assert.That(registry.ReleaseInstance(rig.Instance), Is.EqualTo(0), "a second release of the same installation releases nothing.");
        }

        [Test]
        public void TheTeardownReportNamesTheSixP048StepsInOrder()
        {
            Rig rig = new Rig(0x74656137UL);
            OperationId operation = rig.Register(rig.Instance);
            ManagedResourceLease lease = rig.Acquire(rig.Ids.Resource(), 1U);

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, operation, true);

            Assert.That(report.CompletedSteps(), Is.EqualTo(new[]
            {
                "close-ingress",
                "settle-step",
                "fence-users",
                "retract-closure",
                "retire-resources",
                "admit-quarantine",
            }));
            Assert.That(report.Steps.Count, Is.EqualTo(6));
            Assert.That(report.Steps[0].Detail, Does.Contain("gate=retired"), "step 1 retires the activation's gate (P-047).");
            Assert.That(report.Steps[3].Name, Is.EqualTo("retract-closure"));
            Assert.That(report.Steps[5].Name, Is.EqualTo("admit-quarantine"));
            Assert.That(lease.IsDisposed, Is.True);
            Assert.That(report.DisposeSettled, Is.True);
        }

        [Test]
        public void SettleStepDecidesWhetherTheWorldIsAskedToReachAStepBoundary()
        {
            Rig settled = new Rig(0x74656138UL);
            OperationId first = settled.Register(settled.Instance);
            settled.Acquire(settled.Ids.Resource(), 1U);
            settled.Binding.AttributedRows = 3;
            settled.Binding.RetractedRows = 2;

            TeardownReport withSettle = settled.Teardown.Unload(settled.Instance, settled.Stamp, first, true);

            Assert.That(settled.Binding.SettleCalls, Is.EqualTo(1), "a teardown that is not the publication boundary asks the world to settle (P-030).");
            Assert.That(settled.Binding.CloseCalls, Is.EqualTo(1));
            Assert.That(settled.Binding.FenceCalls, Is.EqualTo(1));
            Assert.That(settled.Binding.RetractCalls, Is.EqualTo(1));
            Assert.That(withSettle.Retraction.Instance.Equals(settled.Instance), Is.True, "step 4's record names the installation it retracted.");
            Assert.That(withSettle.Retraction.AttributedRows, Is.EqualTo(3), "the record keeps the attributed and the retracted row counts apart (P-017).");
            Assert.That(withSettle.RetractedContributions, Is.EqualTo(2), "the report carries how many rows the binding retracted.");
            Assert.That(withSettle.Retraction.PublishedByBinding, Is.True, "the binding that published the retraction says so (P-030).");
            Assert.That(settled.Teardown.RetractionCount, Is.EqualTo(1));
            Assert.That(withSettle.DisposeSettled, Is.True);

            Rig atBoundary = new Rig(0x74656139UL);
            OperationId second = atBoundary.Register(atBoundary.Instance);
            atBoundary.Acquire(atBoundary.Ids.Resource(), 1U);

            TeardownReport withoutSettle = atBoundary.Teardown.Unload(atBoundary.Instance, atBoundary.Stamp, second, false);

            Assert.That(atBoundary.Binding.SettleCalls, Is.EqualTo(0), "a caller already at the boundary must not ask the world to wait for itself (P-030).");
            Assert.That(atBoundary.Binding.CloseCalls, Is.EqualTo(1), "the other P-048 steps still run.");
            Assert.That(withoutSettle.DisposeSettled, Is.True);
            Assert.That(withoutSettle.CompletedSteps(), Does.Contain("settle-step"));
        }

        [Test]
        public void AFaultedStepSettlementReportsTheTeardownAsUnsettled()
        {
            Rig rig = new Rig(0x74656141UL);
            OperationId operation = rig.Register(rig.Instance);
            rig.Acquire(rig.Ids.Resource(), 1U);
            rig.Binding.Settlement = LifecycleStepSettlement.Fault(new LogicalStepId(7UL), "the step did not reach a boundary");

            TeardownReport report = rig.Teardown.Unload(rig.Instance, rig.Stamp, operation, true);

            Assert.That(report.Code, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.ResourceUnavailable), "nothing is retained, so the unsettled step is what blocked the pass (TeardownSequencer.cs:310-313).");
            Assert.That(report.DisposeSettled, Is.False);
            Assert.That(report.HasCleanupErrors, Is.False, "a faulted step boundary is not a failed release.");
            Assert.That(report.CompletedSteps(), Does.Not.Contain("settle-step"));
            Assert.That(report.Steps[1].Name, Is.EqualTo("settle-step"));
            Assert.That(report.Steps[1].Completed, Is.False);
            Assert.That(report.Steps[1].Detail, Does.Contain("the step did not reach a boundary"));
            Assert.That(report.Steps[1].Detail, Does.Contain("committedStep=7"));
            Assert.That(rig.Teardown.BlockedCount, Is.EqualTo(1));
            Assert.That(rig.Factory.DisposeCount, Is.EqualTo(1), "a faulted step boundary blocks the settlement, not the cleanup (P-048).");
        }
    }
}
