#nullable enable
using System;
using GameCore.Contracts;
using GameCore.Execution;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    /// <summary>
    /// World/job resource ledger tests (GC-005, TEST-015/TEST-018). Every resource is disposed at most once, a
    /// dependent is retired before the resource it depends on, a failing disposer never blocks the other cleanups,
    /// and unfinished jobs stay tracked behind quarantine instead of being freed on a timeout (P-047, P-048).
    /// </summary>
    [TestFixture]
    public sealed class WorldResourceLedgerTests
    {
        private static readonly WorldId World = new WorldId(new Id128(0x574F524C44534553UL, 1UL));
        private static readonly OwnerId Owner = new OwnerId(new Id128(0x4F574E4552544553UL, 2UL));
        private static readonly PluginInstanceId Instance = new PluginInstanceId(new Id128(0x494E5354414E4345UL, 3UL));
        private static readonly StageId Stage = new StageId(new Id128(0x5354414745494458UL, 4UL));
        private static readonly FactoryKey SystemKey = new FactoryKey(new Id128(0x53595354454D4B59UL, 5UL), 1U);

        private static ResourceKey Resource(ulong ordinal) => new ResourceKey(new Id128(0x5245534F55524345UL, ordinal));

        private static WorldResourceLedger NewLedger() => new WorldResourceLedger(World);

        [Test]
        public void ResourcesAreDisposedAtMostOnce()
        {
            WorldResourceLedger ledger = NewLedger();
            Id128 lease = ledger.Acquire(WorldResourceKind.ManagedLease, Resource(1UL), Owner, Instance, default(Id128), 128UL);

            Assert.That(ledger.MarkReady(lease), Is.True);
            Assert.That(ledger.Retire(lease), Is.True);
            Assert.That(ledger.Retire(lease), Is.False, "Disposing the same resource twice is neither attempted nor counted.");
            Assert.That(ledger.RetireCount, Is.EqualTo(1));
            Assert.That(ledger.DisposalAttemptCount, Is.EqualTo(1));
            Assert.That(ledger.RetainedResourceCount, Is.EqualTo(0));
        }

        [Test]
        public void QuarantineRetainsUntilItsUsersEndAndThenAllowsRetirement()
        {
            WorldResourceLedger ledger = NewLedger();
            Id128 container = ledger.Acquire(WorldResourceKind.NativeContainer, Resource(2UL), Owner, Instance, default(Id128), 4096UL);

            Assert.That(ledger.Quarantine(container), Is.True);
            Assert.That(ledger.Retire(container), Is.False, "A quarantined resource is never reported as successfully disposed.");
            Assert.That(ledger.RetainedResourceCount, Is.EqualTo(1));
            Assert.That(ledger.QuarantinedBytes, Is.EqualTo(4096UL));

            Assert.That(ledger.ReleaseQuarantine(container), Is.True);
            Assert.That(ledger.QuarantineCount, Is.EqualTo(1), "Releasing quarantine is not a second quarantine event.");
            Assert.That(ledger.Retire(container), Is.True);
            Assert.That(ledger.QuarantinedBytes, Is.EqualTo(0UL));
        }

        [Test]
        public void RetirementRunsFromDependentsTowardDependencies()
        {
            WorldResourceLedger ledger = NewLedger();
            Id128 storage = ledger.Acquire(WorldResourceKind.WorldStorage, Resource(3UL), Owner, Instance, default(Id128), 0UL);
            Id128 index = ledger.Acquire(WorldResourceKind.IdentityIndex, Resource(4UL), Owner, Instance, storage, 0UL);
            Id128 system = ledger.Acquire(WorldResourceKind.SystemRegistration, Resource(5UL), Owner, Instance, index, 0UL);

            RetirementOutcome outcome = ledger.RetireAll(default(Id128));

            Assert.That(outcome.AllRetired, Is.True, outcome.ToString());
            Assert.That(outcome.RetiredCount, Is.EqualTo(3));
            Assert.That(ledger.TryGetResource(system, out WorldResourceRecord systemRecord), Is.True);
            Assert.That(systemRecord.State, Is.EqualTo(ResourceRetirementState.Retired));
            Assert.That(systemRecord.HasDependency, Is.True, "The retirement edge is recorded, not inferred from acquisition order.");
        }

        [Test]
        public void AFailingDisposerIsQuarantinedWhileOtherCleanupStillRuns()
        {
            WorldResourceLedger ledger = NewLedger();
            Id128 storage = ledger.Acquire(WorldResourceKind.WorldStorage, Resource(6UL), Owner, Instance, default(Id128), 0UL);
            Id128 lease = ledger.Acquire(WorldResourceKind.ManagedLease, Resource(7UL), Owner, Instance, storage, 64UL);

            RetirementOutcome outcome = ledger.RetireAll(lease);

            Assert.That(outcome.AllRetired, Is.False, "A failed release must not be reported as a successful disposal (P-048).");
            Assert.That(outcome.FailedCount, Is.EqualTo(1));
            Assert.That(outcome.QuarantinedCount, Is.EqualTo(1));
            Assert.That(ledger.TryGetResource(lease, out WorldResourceRecord leaseRecord), Is.True);
            Assert.That(leaseRecord.State, Is.EqualTo(ResourceRetirementState.Quarantined));
            Assert.That(ledger.TryGetResource(storage, out WorldResourceRecord storageRecord), Is.True);
            Assert.That(storageRecord.State, Is.EqualTo(ResourceRetirementState.Retired), "Independent cleanup continues past a failure.");
            Assert.That(ledger.DisposalAttemptCount, Is.EqualTo(2), "Every independent cleanup is attempted exactly once.");
        }

        [Test]
        public void UnfinishedJobsStayTrackedAndQuarantined()
        {
            WorldResourceLedger ledger = NewLedger();
            Id128 first = ledger.RecordJob(Stage, SystemKey, AssemblyEpoch.First, LogicalStepId.Zero);
            Id128 second = ledger.RecordJob(Stage, SystemKey, AssemblyEpoch.First, LogicalStepId.Zero);

            Assert.That(ledger.OutstandingJobCount, Is.EqualTo(2));
            Assert.That(ledger.CompleteJob(first), Is.True);
            Assert.That(ledger.OutstandingJobCount, Is.EqualTo(1));

            Assert.That(ledger.RetainJobByQuarantine(second), Is.True);
            Assert.That(ledger.QuarantinedJobCount, Is.EqualTo(1));
            Assert.That(ledger.RetainJobByQuarantine(second), Is.True, "Retaining an already retained job is harmless.");
            Assert.That(ledger.QuarantinedJobCount, Is.EqualTo(1));
            Assert.That(ledger.IsJobCompleted(first), Is.True);
            Assert.That(ledger.IsJobCompleted(second), Is.False, "An unfinished job is never reported complete.");

            Assert.That(ledger.CompleteJob(second), Is.True);
            Assert.That(ledger.OutstandingJobCount, Is.EqualTo(0));
            Assert.That(ledger.QuarantinedJobCount, Is.EqualTo(1), "Completion does not silently clear the quarantine flag.");
        }

        [Test]
        public void SnapshotIsOrderedByResourceIdAndReportsQuarantineBytes()
        {
            WorldResourceLedger ledger = NewLedger();
            Id128 third = ledger.Acquire(WorldResourceKind.ScratchAllocation, Resource(9UL), Owner, Instance, default(Id128), 512UL);
            Id128 first = ledger.Acquire(WorldResourceKind.ManagedLease, Resource(8UL), Owner, Instance, default(Id128), 32UL);
            ledger.Quarantine(third);
            Id128 second = ledger.RecordJob(Stage, SystemKey, AssemblyEpoch.First, LogicalStepId.First);

            WorldResourceLedgerSnapshot snapshot = ledger.Snapshot(AssemblyEpoch.First);

            Assert.That(snapshot.World, Is.EqualTo(World));
            Assert.That(snapshot.Epoch, Is.EqualTo(AssemblyEpoch.First));
            Assert.That(snapshot.Resources.Count, Is.EqualTo(2));
            Assert.That(snapshot.Resources[0].ResourceId, Is.EqualTo(first), "Resources are reported in canonical id order (P-008).");
            Assert.That(snapshot.Resources[1].ResourceId, Is.EqualTo(third));
            Assert.That(snapshot.QuarantinedCount, Is.EqualTo(1));
            Assert.That(snapshot.QuarantinedBytes, Is.EqualTo(512UL));
            Assert.That(snapshot.Jobs.Count, Is.EqualTo(1));
            Assert.That(snapshot.Jobs[0].JobId, Is.EqualTo(second));
            Assert.That(snapshot.Jobs[0].Stage, Is.EqualTo(Stage));
        }

        [Test]
        public void UnknownResourcesAndJobsAreRefusedInsteadOfInvented()
        {
            WorldResourceLedger ledger = NewLedger();
            var missing = new Id128(1UL, 1UL);

            Assert.That(ledger.Retire(missing), Is.False);
            Assert.That(ledger.Quarantine(missing), Is.False);
            Assert.That(ledger.ReleaseQuarantine(missing), Is.False);
            Assert.That(ledger.MarkReady(missing), Is.False);
            Assert.That(ledger.CompleteJob(missing), Is.False);
            Assert.That(ledger.RetainJobByQuarantine(missing), Is.False);
            Assert.That(ledger.TryGetResource(missing, out _), Is.False);
            Assert.That(ledger.ResourceCount, Is.EqualTo(0));
        }
    }
}
