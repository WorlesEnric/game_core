// GC-007 pure tests — state authority: one owner per domain, order-or-partition proof, direct owner writes.
//
// Normative sources: 00-core-protocols.md P-028, P-034, P-040 (TEST-013) and 09 GC-007. These are the pure,
// engine-free half of TEST-013: the Unity-world half (request → drain → owner commit) lives in
// Packages/com.gamecore.unity.runtime/Tests/Messages.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using NUnit.Framework;

namespace GameCore.Planning.Tests.Ownership
{
    /// <summary>
    /// Authority validation (P-034, P-040). Two undeclared writers of one domain reject; two validated mutually
    /// exclusive partitions are legal; one owner with ordered systems is legal; and a single owner updating an
    /// existing component needs no queue, no buffer and no partition.
    /// </summary>
    [TestFixture]
    public sealed class OwnerAuthorityValidatorTests
    {
        private static readonly SchemaRef TableState = OwnershipFixtureIds.SchemaRefOf(1UL);
        private static readonly SchemaRef SeatScore = OwnershipFixtureIds.SchemaRefOf(2UL);

        private static readonly OwnerId TableOwner = OwnershipFixtureIds.Owner(1UL);
        private static readonly OwnerId SeatOwner = OwnershipFixtureIds.Owner(2UL);

        private static readonly StageId AcceptStage = OwnershipFixtureIds.Stage(1UL);
        private static readonly StageId SettleStage = OwnershipFixtureIds.Stage(2UL);

        private static readonly FactoryKey AcceptSystem = OwnershipFixtureIds.Key(1UL);
        private static readonly FactoryKey SettleSystem = OwnershipFixtureIds.Key(2UL);
        private static readonly FactoryKey CommitSystem = OwnershipFixtureIds.Key(3UL);
        private static readonly FactoryKey ProjectSystem = OwnershipFixtureIds.Key(4UL);

        [Test]
        public void TwoUndeclaredWritersOfOneDomainReject()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
                new WriterDeclaration(SettleStage, SettleSystem, SeatOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage, SettleStage }));

            Assert.That(report.IsValid, Is.False, "Two owners claiming one authoritative domain must reject (P-034).");
            Assert.That(Contains(report, DiagnosticCode.OwnershipConflict), Is.True, report.Describe());
            Assert.That(report.Map.TryGetDomainOwner(TableState, out OwnerId resolved), Is.False);
            Assert.That(resolved.Value.IsDefault, Is.True, "A contested domain resolves to no single owner.");
        }

        [Test]
        public void TwoOwnersWithDifferentDomainsAreLegal()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
                new WriterDeclaration(SettleStage, SettleSystem, SeatOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage, SettleStage }));

            Assert.That(report.IsValid, Is.True, report.Describe());
            Assert.That(report.Map.DomainCount, Is.EqualTo(2));
            Assert.That(report.Map.TryGetDomainOwner(TableState, out OwnerId table), Is.True);
            Assert.That(table, Is.EqualTo(TableOwner));
            Assert.That(report.Map.TryGetDomainOwner(SeatScore, out OwnerId seat), Is.True);
            Assert.That(seat, Is.EqualTo(SeatOwner));
        }

        [Test]
        public void TwoValidatedDisjointPartitionsOfOneOwnerAreLegal()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(
                    AcceptStage,
                    AcceptSystem,
                    TableOwner,
                    Access.Partitioned(SeatScore, OwnershipFixtureIds.Partition(1UL)),
                    SystemMultiplicity.PerPartition,
                    null,
                    null),
                new WriterDeclaration(
                    AcceptStage,
                    SettleSystem,
                    TableOwner,
                    Access.Partitioned(SeatScore, OwnershipFixtureIds.Partition(2UL)),
                    SystemMultiplicity.PerPartition,
                    null,
                    null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage }));

            Assert.That(report.IsValid, Is.True, report.Describe());
            Assert.That(report.Map.PartitionCount, Is.EqualTo(2), "Both declared partitions are recorded (P-034).");
            Assert.That(report.Map.TryGetDomainOwner(SeatScore, out OwnerId owner), Is.True);
            Assert.That(owner, Is.EqualTo(TableOwner), "Disjoint writers of one owner keep one domain owner.");
            Assert.That(
                report.Map.TryGetPartition(SeatScore, AcceptSystem, out PartitionAssignment first),
                Is.True);
            Assert.That(
                report.Map.TryGetPartition(SeatScore, SettleSystem, out PartitionAssignment second),
                Is.True);
            Assert.That(first.PartitionId, Is.Not.EqualTo(second.PartitionId));
            Assert.That(PartitionIdGenerator.ProvablyDisjoint(first, second), Is.True);
        }

        [Test]
        public void OverlappingWritersWithoutOrderOrPartitionRejectAsAmbiguous()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, null),
                new WriterDeclaration(AcceptStage, SettleSystem, TableOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, null),
            };

            // No stage order is supplied, so the two same-stage writers cannot be ordered by the compiled plan.
            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, null));

            Assert.That(report.IsValid, Is.False);
            Assert.That(Contains(report, DiagnosticCode.AmbiguousOrder), Is.True, report.Describe());
        }

        [Test]
        public void ADeclaredRequiredEdgeOrdersTwoWritersOfOneDomain()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(
                    AcceptStage,
                    AcceptSystem,
                    TableOwner,
                    Access.Whole(SeatScore),
                    SystemMultiplicity.World,
                    null,
                    new[] { SettleSystem }),
                new WriterDeclaration(
                    AcceptStage,
                    SettleSystem,
                    TableOwner,
                    Access.Whole(SeatScore),
                    SystemMultiplicity.World,
                    null,
                    null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, null));

            Assert.That(report.IsValid, Is.True, report.Describe());
            Assert.That(report.Map.PartitionCount, Is.EqualTo(0), "An ordered writer needs no partition claim.");
        }

        [Test]
        public void PartitionedAgainstUnpartitionedWriterStillConflicts()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(
                    AcceptStage,
                    AcceptSystem,
                    TableOwner,
                    Access.Partitioned(SeatScore, OwnershipFixtureIds.Partition(1UL)),
                    SystemMultiplicity.PerPartition,
                    null,
                    null),
                new WriterDeclaration(AcceptStage, SettleSystem, TableOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage }));

            Assert.That(report.IsValid, Is.False, "A partitioned writer against a whole-schema writer is not disjoint (P-040).");
            Assert.That(Contains(report, DiagnosticCode.AmbiguousOrder), Is.True, report.Describe());
        }

        [Test]
        public void ADirectOwnerUpdateOfAnExistingComponentNeedsNoQueueOrPartition()
        {
            // One owner, one writer, one whole-schema component write: the legal "compute owned state by direct
            // component write" path from 03 s4. Nothing here declares a buffer, a queue or a partition, so a passing
            // validation is the proof that a direct owner update needs no global queue (09 GC-007).
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
            };

            var slots = new List<SlotAuthorityDeclaration>
            {
                Slot(1UL, TableOwner, TableState, SlotAuthorityOptions.Durable()),
            };

            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    TableState,
                    OwnershipFixtureIds.Key(10UL),
                    TableOwner,
                    new[] { new FieldOwnership(TableState, OwnershipFixtureIds.Id(0x0C01UL)) }),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, slots, components, new[] { AcceptStage }));

            Assert.That(report.IsValid, Is.True, report.Describe());
            Assert.That(report.Map.PartitionCount, Is.EqualTo(0), "A direct owner update claims no partition.");
            Assert.That(report.Map.WritersOf(TableState).Count, Is.EqualTo(1));
            Assert.That(report.Map.TryGetDomainOwner(TableState, out OwnerId owner), Is.True);
            Assert.That(owner, Is.EqualTo(TableOwner));
        }

        [Test]
        public void AReaderOfAnotherOwnerIsNotAWriter()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
                new WriterDeclaration(SettleStage, SettleSystem, SeatOwner, Access.ReadOnly(TableState), SystemMultiplicity.World, null, null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage, SettleStage }));

            Assert.That(report.IsValid, Is.True, report.Describe());
            Assert.That(report.Map.WritersOf(TableState).Count, Is.EqualTo(1), "A read-only declaration claims no authority.");
        }

        [Test]
        public void ADeclaredWriterWithoutAnOwnerRejects()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, default(OwnerId), Access.Whole(TableState), SystemMultiplicity.World, null, null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage }));

            Assert.That(report.IsValid, Is.False);
            Assert.That(Contains(report, DiagnosticCode.MissingDependency), Is.True, report.Describe());
        }

        [Test]
        public void TheSameWriterDeclaredTwiceRejects()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
                new WriterDeclaration(SettleStage, AcceptSystem, TableOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, new[] { AcceptStage, SettleStage }));

            Assert.That(report.IsValid, Is.False, "One system key is one scheduling instance per world (P-039).");
            Assert.That(Contains(report, DiagnosticCode.OwnershipConflict), Is.True, report.Describe());
        }

        [Test]
        public void ADeclaredRequiredCycleRejects()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, new[] { SettleSystem }),
                new WriterDeclaration(AcceptStage, SettleSystem, TableOwner, Access.Whole(SeatScore), SystemMultiplicity.World, null, new[] { AcceptSystem }),
            };

            // Both writers declare a write on one domain AND a mutual required edge: the edge must make them ordered
            // rather than conflict, and the cycle is the plan compiler's rejection (P-040). This test pins the
            // ownership half: a mutual edge is still a directed path, so authority does not report AmbiguousOrder.
            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, null));

            Assert.That(Contains(report, DiagnosticCode.AmbiguousOrder), Is.False, report.Describe());
        }

        [Test]
        public void TheReportIsCanonicalAndStableAcrossInputPermutations()
        {
            var forward = new List<WriterDeclaration>
            {
                new WriterDeclaration(AcceptStage, AcceptSystem, TableOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
                new WriterDeclaration(SettleStage, SettleSystem, SeatOwner, Access.Whole(TableState), SystemMultiplicity.World, null, null),
            };

            var reversed = new List<WriterDeclaration> { forward[1], forward[0] };

            OwnershipReport first = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(forward, null, null, new[] { AcceptStage, SettleStage }));
            OwnershipReport second = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(reversed, null, null, new[] { SettleStage, AcceptStage }));

            Assert.That(second.Describe(), Is.EqualTo(first.Describe()), "Declaration order must not change the report (P-008).");
        }

        private static SlotAuthorityDeclaration Slot(ulong ordinal, OwnerId owner, SchemaRef schema, SlotAuthorityOptions options)
            => new SlotAuthorityDeclaration(
                OwnershipFixtureIds.Slot(ordinal),
                owner,
                schema,
                OwnershipFixtureIds.Key(0x20UL + ordinal),
                null,
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null,
                options);

        private static bool Contains(OwnershipReport report, DiagnosticCode code)
        {
            for (int i = 0; i < report.Diagnostics.Count; i++)
            {
                if (report.Diagnostics[i].Code == code)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
