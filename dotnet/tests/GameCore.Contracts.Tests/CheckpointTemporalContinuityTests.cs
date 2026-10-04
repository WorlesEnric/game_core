// SADR-012 (studio) temporal-continuity tests: the additive container feature, document feature capture, the
// byte-preserving rewrite and the restored temporal origin (continued vs legacy step 0). Normative sources: P-036,
// P-038, P-050, P-053, P-055.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    [TestFixture]
    public sealed class CheckpointTemporalContinuityTests
    {
        private static readonly Id128 UnknownFeatureId = new Id128(0x6E00000000000001UL, 0x6F00000000000001UL);

        private static HeaderRecordValue Header(TemporalModel model, ulong step, ulong debt, double domain, int targets, int slots, int cursors)
        {
            return new HeaderRecordValue(
                0x1111111111111111UL,
                0x2222222222222222UL,
                0x3333333333333333UL,
                0x4444444444444444UL,
                1U,
                0U,
                (uint)model,
                model == TemporalModel.FixedStep ? 166666UL : 0UL,
                model == TemporalModel.FixedStep ? 10000000UL : 0UL,
                7U,
                true,
                step,
                debt,
                domain,
                0UL,
                (uint)PropagationMode.Automatic,
                0x7777777777777777UL,
                0x8888888888888888UL,
                0x9999999999999999UL,
                0xAAAAAAAAAAAAAAAAUL,
                (uint)CheckpointQueuePolicy.IncludeQueued,
                5UL,
                0U,
                4UL,
                0U,
                0U,
                0U,
                (uint)targets,
                (uint)slots,
                0U,
                0U,
                0U,
                0U,
                0U,
                (uint)cursors,
                6UL,
                8UL,
                60UL,
                3U,
                0U);
        }

        private static byte[] Document(IReadOnlyList<Id128>? features, TemporalModel model, ulong step, ulong debt, double domain)
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.CompleteSet();
            var serializer = new CheckpointSerializer(codecs, null, features);
            Assert.That(serializer.TryAdd(CheckpointRecordKind.Target, CheckpointTestRecords.SampleTarget(10UL, 1UL), out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(serializer.TryAdd(CheckpointRecordKind.Slot, CheckpointTestRecords.SampleSlot(10UL, 20UL, 30UL), out code, out detail), Is.True, detail);
            Assert.That(serializer.TryAdd(CheckpointRecordKind.Cursor, CheckpointTestRecords.SampleCursor((uint)CursorRowKind.EventCursor, 1UL), out code, out detail), Is.True, detail);
            Assert.That(serializer.TryAdd(CheckpointRecordKind.Cursor, CheckpointTestRecords.SampleCursor((uint)CursorRowKind.IssuerHighWater, 2UL), out code, out detail), Is.True, detail);
            Assert.That(serializer.TrySerialize(Header(model, step, debt, domain, 1, 1, 2), out byte[] document, out code, out detail), Is.True, detail);
            return document;
        }

        private static CheckpointDocument Read(byte[] document)
        {
            Assert.That(
                CheckpointDocument.TryRead(document, CheckpointTestRecords.CompleteSet(), out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.True,
                code + ": " + detail);
            return read!;
        }

        [Test]
        public void TemporalContinuityFeatureIdIsTheDerivedStableName()
        {
            Assert.That(
                CheckpointFormat.TemporalContinuityFeatureId,
                Is.EqualTo(StableNameKeyDerivation.Derive(CheckpointFormat.TemporalContinuityFeatureStableName)));
            Assert.That(CheckpointFormat.TemporalContinuityFeatureId, Is.Not.EqualTo(CheckpointFormat.RequiredFeatureId));
            Assert.That(CheckpointFormat.KnownFeatureIds, Is.EqualTo(new[] { CheckpointFormat.RequiredFeatureId }), "the V1 record feature set is unchanged");
            Assert.That(CheckpointFormat.ReadableFeatureIds, Is.EqualTo(new[] { CheckpointFormat.RequiredFeatureId, CheckpointFormat.TemporalContinuityFeatureId }));
        }

        [Test]
        public void WritableFeatureSetsNameTheV1FeatureAndOnlyReadableFeaturesOnce()
        {
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(CheckpointFormat.KnownFeatureIds), Is.True);
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(CheckpointFormat.TemporalContinuityFeatureIds), Is.True);
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(null), Is.False);
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(Array.Empty<Id128>()), Is.False);
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(new[] { CheckpointFormat.TemporalContinuityFeatureId }), Is.False, "the V1 feature is required");
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(new[] { CheckpointFormat.RequiredFeatureId, UnknownFeatureId }), Is.False);
            Assert.That(CheckpointFormat.IsWritableContainerFeatureSet(new[] { CheckpointFormat.RequiredFeatureId, CheckpointFormat.RequiredFeatureId }), Is.False);
            Assert.Throws<ArgumentException>(() => new CheckpointSerializer(CheckpointTestRecords.CompleteSet(), null, new[] { UnknownFeatureId }));
        }

        [Test]
        public void AContinuousDocumentDeclaresTheFeatureAndAV1DocumentDoesNot()
        {
            CheckpointDocument continuous = Read(Document(CheckpointFormat.TemporalContinuityFeatureIds, TemporalModel.FixedStep, 42UL, 3UL, 1.5));
            Assert.That(continuous.DeclaresTemporalContinuity, Is.True);
            Assert.That(continuous.DeclaredFeatureIds, Is.EqualTo(CheckpointFormat.TemporalContinuityFeatureIds));

            CheckpointDocument legacy = Read(Document(null, TemporalModel.FixedStep, 42UL, 3UL, 1.5));
            Assert.That(legacy.DeclaresTemporalContinuity, Is.False);
            Assert.That(legacy.DeclaredFeatureIds, Is.EqualTo(CheckpointFormat.KnownFeatureIds));
        }

        [Test]
        public void ADocumentDeclaringAnUnknownFeatureIsStillRefused()
        {
            var writer = new EnvelopeWriter(
                new EnvelopeHeader(1, 0, CheckpointFormat.DocumentSchema, new[] { CheckpointFormat.RequiredFeatureId, UnknownFeatureId }),
                CheckpointFormat.Limits);
            writer.WriteBytesField(1, CheckpointTestRecords.HeaderCodec().Encode(CheckpointTestRecords.EmptyHeader()));
            writer.WriteChecksum();
            Assert.That(
                CheckpointDocument.TryRead(writer.ToArray(), CheckpointTestRecords.CompleteSet(), out CheckpointDocument? read, out DiagnosticCode code, out string _),
                Is.False);
            Assert.That(read, Is.Null);
            Assert.That(code, Is.Not.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void RewriteDeclaresContinuityAndCarriesEveryRecordByteForByte()
        {
            byte[] original = Document(null, TemporalModel.CommandDriven, 9UL, 0UL, 4.0);
            CheckpointDocument legacy = Read(original);
            Assert.That(legacy.TryRewrite(null, CheckpointFormat.TemporalContinuityFeatureIds, out byte[] rewritten, out DiagnosticCode code, out string detail), Is.True, detail);
            CheckpointDocument continuous = Read(rewritten);
            Assert.That(continuous.DeclaresTemporalContinuity, Is.True);
            CheckpointTestRecords.AssertHeaderEquals(legacy.Header, continuous.Header);
            foreach (CheckpointRecordKind kind in new[] { CheckpointRecordKind.Target, CheckpointRecordKind.Slot, CheckpointRecordKind.Cursor })
            {
                Assert.That(continuous.CountOf(kind), Is.EqualTo(legacy.CountOf(kind)), kind.ToString());
            }

            Assert.That(continuous.TryReadRecords<SlotRecordValue>(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out code, out detail), Is.True, detail);
            Assert.That(slots[0].Value, Is.EqualTo(-9));

            // Rewriting back to the V1 set reproduces the original bytes exactly.
            Assert.That(continuous.TryRewrite(null, CheckpointFormat.KnownFeatureIds, out byte[] back, out code, out detail), Is.True, detail);
            Assert.That(back, Is.EqualTo(original));
        }

        [Test]
        public void RewriteReplacesSlotValuesButRefusesToChangeTheSlotCount()
        {
            CheckpointDocument document = Read(Document(null, TemporalModel.CommandDriven, 9UL, 0UL, 0.0));
            SlotRecordValue slot = CheckpointTestRecords.SampleSlot(10UL, 20UL, 30UL);
            var migrated = new SlotRecordValue(slot.TargetHigh, slot.TargetLow, slot.OwnerHigh, slot.OwnerLow, slot.SlotHigh, slot.SlotLow, 3U, 100, slot.Active);
            Assert.That(document.TryRewrite(new[] { migrated }, null, out byte[] rewritten, out DiagnosticCode code, out string detail), Is.True, detail);
            CheckpointDocument read = Read(rewritten);
            Assert.That(read.TryReadRecords<SlotRecordValue>(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out code, out detail), Is.True, detail);
            Assert.That(slots[0].SchemaVersion, Is.EqualTo(3U));
            Assert.That(slots[0].Value, Is.EqualTo(100));
            Assert.That(slots[0].Active, Is.EqualTo(slot.Active));

            Assert.That(document.TryRewrite(new[] { migrated, migrated }, null, out byte[] _, out code, out detail), Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
        }

        [Test]
        public void AContinuousFixedStepOriginContinuesStepDebtDomainAndIssuers()
        {
            CheckpointDocument document = Read(Document(CheckpointFormat.TemporalContinuityFeatureIds, TemporalModel.FixedStep, 42UL, 3UL, 1.5));
            Assert.That(RestoredTemporalOrigin.TryFromDocument(document, out RestoredTemporalOrigin origin, out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(origin.Source, Is.EqualTo(TemporalOriginSource.Continued));
            Assert.That(origin.IsContinued, Is.True);
            Assert.That(origin.LogicalStep.Value, Is.EqualTo(42UL));
            Assert.That(origin.TimeDebtTicks, Is.EqualTo(3UL));
            Assert.That(origin.DomainSeconds, Is.EqualTo(1.5));
            CursorRecordValue issuerRow = CheckpointTestRecords.SampleCursor((uint)CursorRowKind.IssuerHighWater, 2UL);
            Assert.That(origin.IssuerHighWaterMarks.Count, Is.EqualTo(1), "the event cursor is not an issuer mark");
            Assert.That(origin.TryGetIssuerHighWater(issuerRow.IssuerId, out ulong mark), Is.True);
            Assert.That(mark, Is.EqualTo(issuerRow.Sequence));
        }

        [Test]
        public void ACommandDrivenOriginCarriesNoDebt()
        {
            CheckpointDocument document = Read(Document(CheckpointFormat.TemporalContinuityFeatureIds, TemporalModel.CommandDriven, 7UL, 99UL, 2.0));
            Assert.That(RestoredTemporalOrigin.TryFromDocument(document, out RestoredTemporalOrigin origin, out DiagnosticCode _, out string _), Is.True);
            Assert.That(origin.LogicalStep.Value, Is.EqualTo(7UL));
            Assert.That(origin.TimeDebtTicks, Is.EqualTo(0UL));
            Assert.That(origin.DomainSeconds, Is.EqualTo(2.0));
        }

        [Test]
        public void ALegacyDocumentRestoresAtStepZeroAndSaysSo()
        {
            CheckpointDocument document = Read(Document(null, TemporalModel.FixedStep, 42UL, 3UL, 1.5));
            Assert.That(RestoredTemporalOrigin.TryFromDocument(document, out RestoredTemporalOrigin origin, out DiagnosticCode _, out string _), Is.True);
            Assert.That(origin.Source, Is.EqualTo(TemporalOriginSource.LegacyStepZero));
            Assert.That(origin.IsLegacyStepZero, Is.True);
            Assert.That(origin.LogicalStep, Is.EqualTo(LogicalStepId.Zero));
            Assert.That(origin.TimeDebtTicks, Is.EqualTo(0UL));
            Assert.That(origin.DomainSeconds, Is.EqualTo(0.0));
            Assert.That(origin.Detail, Does.Contain(CheckpointFormat.TemporalContinuityFeatureStableName));
            Assert.That(origin.Detail, Does.Contain("42"));
            Assert.That(origin.IssuerHighWaterMarks.Count, Is.EqualTo(1), "issuer marks are V1 content and survive either way");
        }

        [Test]
        public void IssuerMarksAreCanonicalAndNeverMoveBackwards()
        {
            var issuer = new Id128(1UL, 2UL);
            var other = new Id128(0UL, 9UL);
            var origin = new RestoredTemporalOrigin(
                new LogicalStepId(5UL),
                0UL,
                0.0,
                new[] { new IssuerHighWaterMark(issuer, 4UL), new IssuerHighWaterMark(other, 1UL), new IssuerHighWaterMark(issuer, 9UL), new IssuerHighWaterMark(issuer, 2UL), new IssuerHighWaterMark(default(Id128), 50UL) },
                TemporalOriginSource.Continued,
                null);
            Assert.That(origin.IssuerHighWaterMarks.Count, Is.EqualTo(2));
            Assert.That(origin.IssuerHighWaterMarks[0].Issuer, Is.EqualTo(other));
            Assert.That(origin.TryGetIssuerHighWater(issuer, out ulong mark), Is.True);
            Assert.That(mark, Is.EqualTo(9UL));
            Assert.That(origin.TryGetIssuerHighWater(new Id128(7UL, 7UL), out ulong _), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new RestoredTemporalOrigin(LogicalStepId.Zero, 0UL, double.NaN, null, TemporalOriginSource.Fresh, null));
            Assert.That(RestoredTemporalOrigin.Fresh.Source, Is.EqualTo(TemporalOriginSource.Fresh));
        }
    }
}
