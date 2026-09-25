// GC-007 pure tests — generated partitions and field-to-component ownership (TEST-013, P-033, P-034).
//
// A generated partition id is derived from stable identities only, so it is stable across runs and two writers can
// never claim one partition by accident (P-008). Field-to-component ownership rejects two physical owners of one
// component, two slots claiming one field and a field the layout does not declare (P-033).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Planning.Ownership;
using NUnit.Framework;

namespace GameCore.Planning.Tests.Ownership
{
    /// <summary>Partition derivation and component ownership map.</summary>
    [TestFixture]
    public sealed class PartitionAndComponentOwnershipTests
    {
        private static readonly SchemaRef Runners = OwnershipFixtureIds.SchemaRefOf(1UL);
        private static readonly SchemaRef Other = OwnershipFixtureIds.SchemaRefOf(2UL);

        private static readonly OwnerId MotionOwner = OwnershipFixtureIds.Owner(1UL);
        private static readonly OwnerId OtherOwner = OwnershipFixtureIds.Owner(2UL);

        private static readonly FactoryKey JobA = OwnershipFixtureIds.Key(1UL);
        private static readonly FactoryKey JobB = OwnershipFixtureIds.Key(2UL);

        [Test]
        public void AGeneratedPartitionIdIsStableAndWriterSpecific()
        {
            var generator = new PartitionIdGenerator();

            Id128 first = generator.Next(MotionOwner, Runners, JobA);
            Id128 again = generator.Next(MotionOwner, Runners, JobA);
            Id128 other = generator.Next(MotionOwner, Runners, JobB);
            Id128 otherDomain = generator.Next(MotionOwner, Other, JobA);
            Id128 otherOwner = generator.Next(OtherOwner, Runners, JobA);

            Assert.That(first.IsDefault, Is.False);
            Assert.That(again, Is.EqualTo(first), "The same declaration derives the same partition id (P-008).");
            Assert.That(other, Is.Not.EqualTo(first), "Two writers never share a generated partition id.");
            Assert.That(otherDomain, Is.Not.EqualTo(first));
            Assert.That(otherOwner, Is.Not.EqualTo(first));
        }

        [Test]
        public void TwoWholeSchemaWritersAreNotProvablyDisjoint()
        {
            var left = new PartitionAssignment(Runners, MotionOwner, JobA, Id128.Zero, false);
            var right = new PartitionAssignment(Runners, MotionOwner, JobB, Id128.Zero, false);

            Assert.That(PartitionIdGenerator.ProvablyDisjoint(left, right), Is.False);
            Assert.That(PartitionIdGenerator.Overlaps(left, right), Is.False);
        }

        [Test]
        public void TwoDifferentDeclaredPartitionsAreProvablyDisjoint()
        {
            var left = new PartitionAssignment(Runners, MotionOwner, JobA, OwnershipFixtureIds.Partition(1UL), false);
            var right = new PartitionAssignment(Runners, MotionOwner, JobB, OwnershipFixtureIds.Partition(2UL), false);

            Assert.That(PartitionIdGenerator.ProvablyDisjoint(left, right), Is.True);
        }

        [Test]
        public void TheSameDeclaredPartitionIsAnOverlap()
        {
            var left = new PartitionAssignment(Runners, MotionOwner, JobA, OwnershipFixtureIds.Partition(1UL), false);
            var right = new PartitionAssignment(Runners, MotionOwner, JobB, OwnershipFixtureIds.Partition(1UL), false);

            Assert.That(PartitionIdGenerator.ProvablyDisjoint(left, right), Is.False);
            Assert.That(PartitionIdGenerator.Overlaps(left, right), Is.True);
        }

        [Test]
        public void GeneratedPartitionsAreAssignedToPerPartitionWritersOnly()
        {
            var writers = new List<WriterDeclaration>
            {
                new WriterDeclaration(
                    OwnershipFixtureIds.Stage(1UL),
                    JobA,
                    MotionOwner,
                    Access.Whole(Runners),
                    SystemMultiplicity.PerPartition,
                    null,
                    null),
                new WriterDeclaration(
                    OwnershipFixtureIds.Stage(1UL),
                    JobB,
                    MotionOwner,
                    Access.Whole(Runners),
                    SystemMultiplicity.World,
                    null,
                    null),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(writers, null, null, null));

            Assert.That(report.IsValid, Is.False, "A whole-schema writer cannot be proven disjoint from a partition.");
            Assert.That(report.Map.PartitionCount, Is.EqualTo(1), "Only the per-partition writer receives a generated id.");
            Assert.That(report.Map.TryGetPartition(Runners, JobA, out PartitionAssignment generated), Is.True);
            Assert.That(generated.Generated, Is.True);
            Assert.That(report.Map.TryGetPartition(Runners, JobB, out _), Is.False);
        }

        [Test]
        public void TwoPhysicalOwnersOfOneComponentReject()
        {
            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    MotionOwner,
                    new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)) }),
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    OtherOwner,
                    new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)) }),
            };

            Assert.That(
                ComponentOwnershipMap.TryBuild(components, null, out ComponentOwnershipMap? map, out IReadOnlyList<Diagnostic> diagnostics),
                Is.False);
            Assert.That(Contains(diagnostics, DiagnosticCode.OwnershipConflict), Is.True);
            Assert.That(map, Is.Not.Null, "A map is still produced for inspection, marked by the diagnostics.");
        }

        [Test]
        public void OneLayoutDeclaringOneFieldTwiceRejects()
        {
            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    MotionOwner,
                    new[]
                    {
                        new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)),
                        new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)),
                    }),
            };

            Assert.That(
                ComponentOwnershipMap.TryBuild(components, null, out ComponentOwnershipMap? _, out IReadOnlyList<Diagnostic> diagnostics),
                Is.False);
            Assert.That(Contains(diagnostics, DiagnosticCode.OwnershipConflict), Is.True);
        }

        [Test]
        public void TwoSlotsClaimingOneFieldReject()
        {
            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    MotionOwner,
                    new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)) }),
            };

            var slots = new List<SlotAuthorityDeclaration>
            {
                Slot(1UL, MotionOwner, Runners, new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)) }),
                Slot(2UL, MotionOwner, Runners, new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)) }),
            };

            Assert.That(
                ComponentOwnershipMap.TryBuild(components, slots, out ComponentOwnershipMap? _, out IReadOnlyList<Diagnostic> diagnostics),
                Is.False);
            Assert.That(Contains(diagnostics, DiagnosticCode.OwnershipConflict), Is.True);
        }

        [Test]
        public void ASlotClaimingAFieldTheLayoutDoesNotDeclareRejects()
        {
            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    MotionOwner,
                    new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C01UL)) }),
            };

            var slots = new List<SlotAuthorityDeclaration>
            {
                Slot(1UL, MotionOwner, Runners, new[] { new FieldOwnership(Runners, OwnershipFixtureIds.Id(0x0C02UL)) }),
            };

            Assert.That(
                ComponentOwnershipMap.TryBuild(components, slots, out ComponentOwnershipMap? _, out IReadOnlyList<Diagnostic> diagnostics),
                Is.False);
            Assert.That(Contains(diagnostics, DiagnosticCode.MissingDependency), Is.True);
        }

        [Test]
        public void AValidLayoutResolvesThePhysicalOwnerAndTheFieldSlot()
        {
            Id128 field = OwnershipFixtureIds.Id(0x0C01UL);
            var oneSlot = OwnershipFixtureIds.Slot(1UL);

            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    MotionOwner,
                    new[] { new FieldOwnership(Runners, field) }),
            };

            var slots = new List<SlotAuthorityDeclaration>
            {
                Slot(1UL, MotionOwner, Runners, new[] { new FieldOwnership(Runners, field) }),
            };

            Assert.That(
                ComponentOwnershipMap.TryBuild(components, slots, out ComponentOwnershipMap? map, out IReadOnlyList<Diagnostic> diagnostics),
                Is.True);
            Assert.That(diagnostics.Count, Is.EqualTo(0));
            Assert.That(map, Is.Not.Null);
            Assert.That(map!.TryGetOwner(Runners, out OwnerId owner), Is.True);
            Assert.That(owner, Is.EqualTo(MotionOwner));
            Assert.That(map.TryResolveField(Runners, field, out SlotId slot), Is.True);
            Assert.That(slot, Is.EqualTo(oneSlot));
            Assert.That(map.TryGetEntry(Runners, out ComponentOwnershipEntry? entry), Is.True);
            Assert.That(entry, Is.Not.Null);
            Assert.That(entry!.DeclaresField(field), Is.True);
            Assert.That(entry.Slots.Count, Is.EqualTo(1));
        }

        [Test]
        public void ASlotOwnerDisagreeingWithThePhysicalOwnerRejects()
        {
            Id128 field = OwnershipFixtureIds.Id(0x0C01UL);

            var components = new List<ComponentLayoutDeclaration>
            {
                new ComponentLayoutDeclaration(
                    Runners,
                    OwnershipFixtureIds.Key(10UL),
                    OtherOwner,
                    new[] { new FieldOwnership(Runners, field) }),
            };

            var slots = new List<SlotAuthorityDeclaration>
            {
                Slot(1UL, MotionOwner, Runners, new[] { new FieldOwnership(Runners, field) }),
            };

            OwnershipReport report = OwnerAuthorityValidator.Validate(
                new OwnerAuthorityDeclaration(null, slots, components, null));

            Assert.That(report.IsValid, Is.False, "One physical owner is required for one component (P-033).");
            Assert.That(Contains(report.Diagnostics, DiagnosticCode.OwnershipConflict), Is.True, report.Describe());
        }

        private static SlotAuthorityDeclaration Slot(
            ulong ordinal,
            OwnerId owner,
            SchemaRef schema,
            IReadOnlyList<FieldOwnership> fields)
            => new SlotAuthorityDeclaration(
                OwnershipFixtureIds.Slot(ordinal),
                owner,
                schema,
                OwnershipFixtureIds.Key(10UL),
                fields,
                LastSupportPolicy.PreserveDormant,
                default(FactoryKey),
                null,
                SlotAuthorityOptions.Dormant());

        private static bool Contains(IReadOnlyList<Diagnostic> diagnostics, DiagnosticCode code)
        {
            for (int i = 0; i < diagnostics.Count; i++)
            {
                if (diagnostics[i].Code == code)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
