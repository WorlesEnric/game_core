// SADR-012 (studio) executable forward slot migrations: SlotMigrationRegistry, SlotSchemaCatalog and
// SlotMigrationExecutor. Normative sources: P-032 (a version change uses a registered Migrate; a missing policy is an
// error, never implicit zero initialization), P-054 (unique forward paths; ambiguity rejects) and 05 s5 (pure
// migrations).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Execution.Persistence;
using NUnit.Framework;

namespace GameCore.Execution.Tests
{
    [TestFixture]
    public sealed class SlotMigrationTests
    {
        private static readonly SchemaId Health = new SchemaId(StableNameKeyDerivation.Derive("test.slot.health"));
        private static readonly OwnerId Owner = new OwnerId(StableNameKeyDerivation.Derive("test.owner"));
        private static readonly SlotId HealthSlot = new SlotId(StableNameKeyDerivation.Derive("test.owner.health"));
        private static readonly SlotId OtherSlot = new SlotId(StableNameKeyDerivation.Derive("test.owner.other"));
        private static readonly TargetId TargetA = new TargetId(StableNameKeyDerivation.Derive("test.target.a"));
        private static readonly TargetId TargetB = new TargetId(StableNameKeyDerivation.Derive("test.target.b"));

        private static SchemaRef V(uint version) => new SchemaRef(Health, version);

        private static SlotRecordValue Row(TargetId target, SlotId slot, uint version, int value, bool active = true) =>
            new SlotRecordValue(
                target.Value.High, target.Value.Low, Owner.Value.High, Owner.Value.Low, slot.Value.High, slot.Value.Low,
                version, value, active);

        private static SlotSchemaCatalog CatalogAt(uint version, string? name = "test.slot.health") =>
            new SlotSchemaCatalog(new[] { new SlotSchemaBinding(Owner, HealthSlot, V(version), name) });

        [Test]
        public void AV1RowMigratesToV2ThroughItsRegisteredPureTransform()
        {
            var registry = new SlotMigrationRegistry(new[]
            {
                new SlotMigrationStep("test.health.v1-to-v2", V(1U), V(2U), value => value * 10),
            });
            var rows = new[]
            {
                Row(TargetA, HealthSlot, 1U, 7),
                Row(TargetB, HealthSlot, 1U, -3, active: false),
                Row(TargetA, OtherSlot, 4U, 99),
            };

            SlotMigrationReport report = SlotMigrationExecutor.Migrate(rows, CatalogAt(2U), registry);

            Assert.That(report.Succeeded, Is.True, report.Detail);
            Assert.That(report.MigratedRows, Is.EqualTo(2));
            Assert.That(report.UnboundRows, Is.EqualTo(1), "a slot the catalog does not bind is carried unchanged");
            Assert.That(report.AppliedMigrations, Is.EqualTo(new[] { "test.health.v1-to-v2" }));
            Assert.That(report.Slots.Count, Is.EqualTo(3));
            Assert.That(report.Slots[0].SchemaVersion, Is.EqualTo(2U));
            Assert.That(report.Slots[0].Value, Is.EqualTo(70));
            Assert.That(report.Slots[0].Key, Is.EqualTo(rows[0].Key));
            Assert.That(report.Slots[1].Value, Is.EqualTo(-30));
            Assert.That(report.Slots[1].Active, Is.False, "a dormant row stays dormant through a migration (P-032)");
            Assert.That(report.Slots[2].Value, Is.EqualTo(99));
            Assert.That(report.Slots[2].SchemaVersion, Is.EqualTo(4U));
        }

        [Test]
        public void AChainOfStepsRunsInOrderAndARecordTransformSeesTheWholeRow()
        {
            var registry = new SlotMigrationRegistry(new[]
            {
                new SlotMigrationStep("test.health.v2-to-v3", V(2U), V(3U), (SlotRecordValue row, out int value, out string detail) =>
                {
                    value = row.SchemaVersion == 2U ? row.Value + 1 : int.MinValue;
                    detail = string.Empty;
                    return true;
                }),
                new SlotMigrationStep("test.health.v1-to-v2", V(1U), V(2U), value => value * 2),
            });

            SlotMigrationReport report = SlotMigrationExecutor.Migrate(new[] { Row(TargetA, HealthSlot, 1U, 5) }, CatalogAt(3U), registry);

            Assert.That(report.Succeeded, Is.True, report.Detail);
            Assert.That(report.Slots[0].SchemaVersion, Is.EqualTo(3U));
            Assert.That(report.Slots[0].Value, Is.EqualTo(11));
            Assert.That(report.AppliedMigrations, Is.EqualTo(new[] { "test.health.v1-to-v2", "test.health.v2-to-v3" }));
        }

        [Test]
        public void ACurrentRowNeedsNoMigrationAndNoRegistry()
        {
            SlotMigrationReport report = SlotMigrationExecutor.Migrate(
                new[] { Row(TargetA, HealthSlot, 2U, 5) }, CatalogAt(2U), new SlotMigrationRegistry());
            Assert.That(report.Succeeded, Is.True);
            Assert.That(report.MigratedRows, Is.EqualTo(0));
            Assert.That(report.Slots[0].Value, Is.EqualTo(5));
        }

        [Test]
        public void AMissingPathRefusesWithMigrationRequiredAndAHintNamingTheSchemaAndVersions()
        {
            var registry = new SlotMigrationRegistry(new[]
            {
                new SlotMigrationStep("test.health.v2-to-v3", V(2U), V(3U), value => value),
            });

            SlotMigrationReport report = SlotMigrationExecutor.Migrate(new[] { Row(TargetA, HealthSlot, 1U, 5) }, CatalogAt(3U), registry);

            Assert.That(report.Succeeded, Is.False);
            Assert.That(report.Refusal, Is.EqualTo(SlotMigrationRefusal.MigrationPathMissing));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.MigrationRequired));
            Assert.That(report.Hint, Does.Contain("test.slot.health v1 -> v3"));
            Assert.That(report.RefusedFrom, Is.EqualTo(V(1U)));
            Assert.That(report.RefusedTo, Is.EqualTo(V(3U)));
            Assert.That(report.Slots, Is.Empty, "a refused migration returns no half-migrated rows (P-029)");
        }

        [Test]
        public void ANewerRowIsADowngradeAndRefuses()
        {
            SlotMigrationReport report = SlotMigrationExecutor.Migrate(
                new[] { Row(TargetA, HealthSlot, 5U, 1) }, CatalogAt(2U), new SlotMigrationRegistry());
            Assert.That(report.Refusal, Is.EqualTo(SlotMigrationRefusal.Downgrade));
            Assert.That(report.Code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
        }

        [Test]
        public void TwoChainsToTheSameVersionAreAmbiguous()
        {
            var registry = new SlotMigrationRegistry(new[]
            {
                new SlotMigrationStep("test.health.v1-to-v3", V(1U), V(3U), value => value),
                new SlotMigrationStep("test.health.v1-to-v2", V(1U), V(2U), value => value),
                new SlotMigrationStep("test.health.v2-to-v3", V(2U), V(3U), value => value),
            });
            SlotMigrationReport report = SlotMigrationExecutor.Migrate(new[] { Row(TargetA, HealthSlot, 1U, 1) }, CatalogAt(3U), registry);
            Assert.That(report.Refusal, Is.EqualTo(SlotMigrationRefusal.AmbiguousPath));
        }

        [Test]
        public void AThrowingTransformIsARefusalNotACrash()
        {
            var registry = new SlotMigrationRegistry(new[]
            {
                new SlotMigrationStep("test.health.v1-to-v2", V(1U), V(2U), value => checked(value * int.MaxValue)),
            });
            SlotMigrationReport report = SlotMigrationExecutor.Migrate(new[] { Row(TargetA, HealthSlot, 1U, 3) }, CatalogAt(2U), registry);
            Assert.That(report.Refusal, Is.EqualTo(SlotMigrationRefusal.TransformRejected));
            Assert.That(report.Detail, Does.Contain("OverflowException"));
        }

        [Test]
        public void RegistrationRefusesBackwardCrossSchemaAndDuplicateSteps()
        {
            var registry = new SlotMigrationRegistry();
            Assert.That(registry.Register(new SlotMigrationStep("test.ok", V(1U), V(2U), v => v)), Is.True);
            Assert.That(registry.Register(new SlotMigrationStep("test.ok", V(2U), V(3U), v => v)), Is.False, "duplicate id");
            Assert.That(registry.Register(new SlotMigrationStep("test.backward", V(3U), V(2U), v => v)), Is.False);
            var other = new SchemaRef(new SchemaId(StableNameKeyDerivation.Derive("test.other")), 2U);
            Assert.That(registry.Register(new SlotMigrationStep("test.cross", V(1U), other, v => v)), Is.False);
            Assert.That(registry.Count, Is.EqualTo(1));
            Assert.That(registry.Rejections.Count, Is.EqualTo(3));
            Assert.That(registry.IsWellFormed, Is.False);
            Assert.That(registry.TryFind("test.ok", out SlotMigrationStep? found), Is.True);
            Assert.That(found!.Key.RegistrationKey, Is.EqualTo(StableNameKeyDerivation.Derive("test.ok")));
            Assert.Throws<ArgumentException>(() => new SlotMigrationStep("Not A Stable Name", V(1U), V(2U), v => v));

            SlotMigrationReport report = SlotMigrationExecutor.Migrate(new[] { Row(TargetA, HealthSlot, 1U, 1) }, CatalogAt(2U), registry);
            Assert.That(report.Refusal, Is.EqualTo(SlotMigrationRefusal.InvalidDeclarations), "a malformed registry restores nothing");
        }

        [Test]
        public void ACatalogBindsOnePairToOneSchema()
        {
            var catalog = new SlotSchemaCatalog();
            Assert.That(catalog.Add(new SlotSchemaBinding(Owner, HealthSlot, V(2U))), Is.True);
            Assert.That(catalog.Add(new SlotSchemaBinding(Owner, HealthSlot, V(2U))), Is.True, "an identical repeat coalesces");
            Assert.That(catalog.Add(new SlotSchemaBinding(Owner, HealthSlot, V(3U))), Is.False);
            Assert.That(catalog.Count, Is.EqualTo(1));
            Assert.That(catalog.Conflicts.Count, Is.EqualTo(1));
            Assert.That(catalog.TryGet(Owner, HealthSlot, out SlotSchemaBinding binding), Is.True);
            Assert.That(binding.Current, Is.EqualTo(V(2U)));
        }

        [Test]
        public void MigrateDocumentRewritesOnlyTheSlotRecordsAndKeepsAnUnchangedDocumentAsIs()
        {
            CheckpointCodecSet codecs = CheckpointTestCodecs.Complete();
            byte[] original = Document(codecs, new[] { Row(TargetA, HealthSlot, 1U, 4), Row(TargetB, OtherSlot, 1U, 8) });
            Assert.That(CheckpointDocument.TryRead(original, codecs, out CheckpointDocument? document, out DiagnosticCode code, out string detail), Is.True, detail);

            var registry = new SlotMigrationRegistry(new[] { new SlotMigrationStep("test.health.v1-to-v2", V(1U), V(2U), v => v + 100) });
            SlotMigrationReport report = SlotMigrationExecutor.MigrateDocument(document!, CatalogAt(2U), registry, null, out byte[] migrated);
            Assert.That(report.Succeeded, Is.True, report.Detail);
            Assert.That(migrated, Is.Not.EqualTo(original));
            Assert.That(CheckpointDocument.TryRead(migrated, codecs, out CheckpointDocument? reread, out code, out detail), Is.True, detail);
            Assert.That(reread!.TryReadRecords<SlotRecordValue>(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out code, out detail), Is.True, detail);
            Assert.That(slots[0].SchemaVersion, Is.EqualTo(2U));
            Assert.That(slots[0].Value, Is.EqualTo(104));
            Assert.That(slots[1].Value, Is.EqualTo(8));
            Assert.That(reread.Header.LogicalStep, Is.EqualTo(document!.Header.LogicalStep));

            SlotMigrationReport unchanged = SlotMigrationExecutor.MigrateDocument(reread, CatalogAt(2U), registry, null, out byte[] same);
            Assert.That(unchanged.MigratedRows, Is.EqualTo(0));
            Assert.That(same, Is.SameAs(reread.RawBytes), "a document needing no migration is restored as it is");

            SlotMigrationReport missing = SlotMigrationExecutor.MigrateDocument(document, CatalogAt(3U), registry, null, out byte[] none);
            Assert.That(missing.Refusal, Is.EqualTo(SlotMigrationRefusal.MigrationPathMissing));
            Assert.That(none, Is.Empty);
        }

        private static byte[] Document(CheckpointCodecSet codecs, IReadOnlyList<SlotRecordValue> slots)
        {
            var serializer = new CheckpointSerializer(codecs);
            for (int i = 0; i < slots.Count; i++)
            {
                Assert.That(serializer.TryAdd(CheckpointRecordKind.Slot, slots[i], out DiagnosticCode _, out string detail), Is.True, detail);
            }

            var header = new HeaderRecordValue(
                1UL, 2UL, 3UL, 4UL, 1U, 0U, (uint)TemporalModel.CommandDriven, 0UL, 0UL, 1U, false,
                12UL, 0UL, 0.0, 0UL, (uint)PropagationMode.Automatic, 5UL, 6UL, 7UL, 8UL,
                (uint)CheckpointQueuePolicy.IncludeQueued, 0UL, 0U, 0UL,
                0U, 0U, 0U, 0U, (uint)slots.Count, 0U, 0U, 0U, 0U, 0U, 0U,
                1UL, 1UL, 10000000UL, 0U, 0U);
            Assert.That(serializer.TrySerialize(header, out byte[] document, out DiagnosticCode code, out string serializeDetail), Is.True, code + ": " + serializeDetail);
            return document;
        }
    }
}
