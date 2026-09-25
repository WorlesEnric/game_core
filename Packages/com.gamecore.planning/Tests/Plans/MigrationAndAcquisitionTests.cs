// GameCore.Planning tests — bounded migration scratch and inert acquisition accounting (GC-008, P-022, P-029, P-032, P-048).
//
// The two accounts are deliberately separate: scratch holds temporary bytes for copied state, and the acquisition
// set holds staged leases that must stay inert until publication. The cases below defend the bounded budget, the
// "missing compatible policy is a validation error" rule, reverse-order release, and the honest aggregation of
// cleanup failures instead of a false `Disposed`.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Planning.Tests
{
    [TestFixture]
    public sealed class MigrationAndAcquisitionTests
    {
        private static StateSlotKey Slot(ulong ordinal = 1UL) =>
            new StateSlotKey(
                PlansFixtureKeys.Target(ordinal),
                PlansFixtureKeys.SlotOwner,
                PlansFixtureKeys.QuestSlot);

        [Test]
        public void ScratchReservesWithinItsBudgetAndRefusesBeyondIt()
        {
            var scratch = new MigrationScratch(128UL, 64UL);

            Assert.That(scratch.TryReserve(Slot(1UL), out DiagnosticCode first), Is.True);
            Assert.That(first, Is.EqualTo(DiagnosticCode.None));
            Assert.That(scratch.TryReserve(Slot(2UL), out _), Is.True);
            Assert.That(scratch.ReservedSlots, Is.EqualTo(2));
            Assert.That(scratch.ReservedBytes, Is.EqualTo(128UL));
            Assert.That(scratch.HighWaterBytes, Is.EqualTo(128UL));

            Assert.That(scratch.TryReserve(Slot(3UL), out DiagnosticCode third), Is.False);
            Assert.That(third, Is.EqualTo(DiagnosticCode.BudgetExceeded), "temporary bytes are a hard limit (P-022)");
            Assert.That(scratch.BudgetExceededCount, Is.EqualTo(1));
            Assert.That(scratch.ReservedSlots, Is.EqualTo(2), "a refused reservation leaves no residue");

            // Reserving a slot that already holds scratch is free, so a repeated plan does not double-charge.
            Assert.That(scratch.TryReserve(Slot(1UL), out _), Is.True);
            Assert.That(scratch.ReservedBytes, Is.EqualTo(128UL));
        }

        [Test]
        public void AMigrationRunsOnTheCopyAndReportsItsValue()
        {
            var scratch = new MigrationScratch(256UL, 64UL);
            var registry = PlansFixture.Migrations();

            Assert.That(
                scratch.TryMigrate(Slot(), PlansFixtureKeys.QuestMigrationV1ToV2, 1U, 7, registry, out MigrationOutcome outcome),
                Is.True,
                outcome.ToString());
            Assert.That(outcome.Applied, Is.True);
            Assert.That(outcome.Value, Is.EqualTo(17), "the registered pure transform ran on the copied value");
            Assert.That(outcome.FromVersion, Is.EqualTo(1U));
            Assert.That(outcome.ToVersion, Is.EqualTo(PlansFixture.QuestSchemaVersion));
            Assert.That(scratch.TryRead(Slot(), out int staged), Is.True);
            Assert.That(staged, Is.EqualTo(17));
        }

        [Test]
        public void AMissingOrMismatchedHandlerIsRefusedWithItsOwnCode()
        {
            var scratch = new MigrationScratch(256UL, 64UL);
            var empty = new MigrationRegistry(null);

            Assert.That(
                scratch.TryMigrate(Slot(), PlansFixtureKeys.QuestMigrationV1ToV2, 1U, 7, empty, out MigrationOutcome missing),
                Is.False);
            Assert.That(missing.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(missing.Applied, Is.False);
            Assert.That(scratch.TryRead(Slot(), out _), Is.False, "a refused migration stages nothing");

            // A handler registered for the key but another source version does not migrate this state (P-032).
            var wrongVersion = new MigrationRegistry(new List<ISlotMigration>
            {
                new DeltaMigration(PlansFixtureKeys.QuestMigrationV1ToV2, 5U, PlansFixture.QuestSchemaVersion, 1),
            });

            Assert.That(
                scratch.TryMigrate(Slot(), PlansFixtureKeys.QuestMigrationV1ToV2, 1U, 7, wrongVersion, out MigrationOutcome mismatched),
                Is.False);
            Assert.That(mismatched.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(scratch.RefusedMigrationCount, Is.EqualTo(2));
        }

        [Test]
        public void AFailingMigrationRejectsItsInputWithoutWritingScratch()
        {
            var scratch = new MigrationScratch(256UL, 64UL);
            var registry = PlansFixture.Migrations();

            Assert.That(
                scratch.TryMigrate(Slot(), PlansFixtureKeys.QuestMigrationV1ToV2, 1U, -5, registry, out MigrationOutcome outcome),
                Is.False,
                "the migration itself refused the input");
            Assert.That(outcome.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(scratch.TryRead(Slot(), out _), Is.False);
        }

        [Test]
        public void ScratchReleasesInReverseReservationOrderAndReportsTheCount()
        {
            var scratch = new MigrationScratch(256UL, 64UL);
            scratch.TryReserve(Slot(1UL), out _);
            scratch.TryReserve(Slot(2UL), out _);
            scratch.TryReserve(Slot(3UL), out _);

            IReadOnlyList<StateSlotKey> order = scratch.ReservedSlotsInOrder();
            Assert.That(order.Count, Is.EqualTo(3));
            Assert.That(order[0].Target, Is.EqualTo(PlansFixtureKeys.Target(1UL)));
            Assert.That(order[2].Target, Is.EqualTo(PlansFixtureKeys.Target(3UL)));

            Assert.That(scratch.ReleaseAll(), Is.EqualTo(3));
            Assert.That(scratch.IsEmpty, Is.True);
            Assert.That(scratch.ReleaseAll(), Is.EqualTo(0), "a second release finds nothing to free");
        }

        [Test]
        public void StagedLeasesStayInertUntilPublication()
        {
            var gate = new RecordingResourceGate();
            var set = new InertAcquisitionSet(gate, PlansFixtureKeys.Operation(PlansFixtureKeys.World(1UL), 1UL));

            Assert.That(
                set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8001UL)), 128UL, null, out DiagnosticCode first),
                Is.True);
            Assert.That(set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8002UL)), 64UL, null, out _), Is.True);

            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.StagedBytes, Is.EqualTo(192UL));
            Assert.That(set.CanEmitGameplay, Is.False, "a staged subscription cannot submit gameplay (P-029)");
            Assert.That(set.Leases[0].Readiness, Is.EqualTo(ResourceReadiness.Pending));
            Assert.That(set.Leases[0].AcquisitionOrdinal, Is.EqualTo(0U));

            Assert.That(set.OpenGates(), Is.EqualTo(2), "publication is the only moment gates open (P-030)");
            Assert.That(set.CanEmitGameplay, Is.True);
            Assert.That(set.Leases[0].Readiness, Is.EqualTo(ResourceReadiness.Ready));

            ResourceStaging staging = set.ToStaging(4096UL, null);
            Assert.That(staging.Handles.Count, Is.EqualTo(2));
            Assert.That(staging.Handles[1].AcquisitionOrdinal, Is.EqualTo(1U));
            Assert.That(staging.ScratchCapacityBytes, Is.EqualTo(4096UL));
        }

        [Test]
        public void AFailedAcquisitionIsCountedAndLateAcquisitionIsTooLate()
        {
            var gate = new RecordingResourceGate { AcquireFailureCount = 1 };
            var set = new InertAcquisitionSet(gate, PlansFixtureKeys.Operation(PlansFixtureKeys.World(1UL), 2UL));

            Assert.That(
                set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8010UL)), 16UL, null, out DiagnosticCode code),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(set.FailedCount, Is.EqualTo(1));
            Assert.That(set.Count, Is.EqualTo(0), "a failed acquisition stages nothing");

            set.OpenGates();
            Assert.That(
                set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8011UL)), 16UL, null, out DiagnosticCode late),
                Is.False);
            Assert.That(late, Is.EqualTo(DiagnosticCode.TooLate), "a late acquisition is not staged work (P-051)");
        }

        [Test]
        public void ReleasingStagedLeasesRunsInReverseOrderAndAggregatesFailures()
        {
            var gate = new RecordingResourceGate();
            var set = new InertAcquisitionSet(gate, PlansFixtureKeys.Operation(PlansFixtureKeys.World(1UL), 3UL));
            set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8020UL)), 16UL, null, out _);
            set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8021UL)), 16UL, null, out _);
            set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8022UL)), 16UL, null, out _);

            // The middle lease cannot be released, which is the "one disposer throws" case of P-048.
            Id128 stubborn = set.Leases[1].LeaseId;
            gate.FailReleaseOf(stubborn);

            AcquisitionCleanup cleanup = set.ReleaseAll();

            Assert.That(cleanup.Attempted, Is.EqualTo(3), "every independent cleanup is attempted");
            Assert.That(cleanup.Released.Count, Is.EqualTo(2));
            Assert.That(cleanup.Failed.Count, Is.EqualTo(1));
            Assert.That(cleanup.Failed[0], Is.EqualTo(stubborn));
            Assert.That(cleanup.HasCleanupErrors, Is.True, "a failed release is recorded, not reported as disposed");
            Assert.That(set.RetainedLeaseIds().Count, Is.EqualTo(1));
            Assert.That(gate.ReleaseAttemptCount, Is.EqualTo(3), "each lease is attempted exactly once");

            // A repeated release never retries a lease that was already released (P-048): only the still-retained
            // one is attempted again.
            AcquisitionCleanup again = set.ReleaseAll();
            Assert.That(again.Attempted, Is.EqualTo(1), "only the still-retained lease is retried");
            Assert.That(gate.ReleaseAttemptCount, Is.EqualTo(4));
        }

        [Test]
        public void QuarantinedLeasesAreReportedAsRetainedRatherThanReleased()
        {
            var gate = new RecordingResourceGate();
            var set = new InertAcquisitionSet(gate, PlansFixtureKeys.Operation(PlansFixtureKeys.World(1UL), 4UL));
            set.TryAcquire(new ResourceKey(new Id128(PlansFixtureKeys.Namespace, 0x8030UL)), 16UL, null, out _);

            Id128 reachable = set.Leases[0].LeaseId;
            gate.QuarantineReleaseOf(reachable);

            // The publisher names the leases it must retain because unfinished work may still reach them (P-048).
            AcquisitionCleanup cleanup = set.ReleaseAll(new List<Id128> { reachable });

            Assert.That(cleanup.Quarantined.Count, Is.EqualTo(1), "a retained lease is reported as retained, not disposed");
            Assert.That(cleanup.Quarantined[0], Is.EqualTo(reachable));
            Assert.That(cleanup.Failed.Count, Is.EqualTo(1), "the refused release is reported too");
            Assert.That(cleanup.HasCleanupErrors, Is.True);
            Assert.That(set.RetainedLeaseIds(), Does.Contain(reachable));
        }
    }
}
