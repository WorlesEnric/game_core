// Checkpoint container framing tests (GC-018): CheckpointSerializer, CheckpointDocument.TryRead and
// CheckpointCodecSet. Normative sources: P-008 (canonical order), P-020/P-022 (a bound is a hard limit), P-052
// (every failure carries a stable code), P-053 (a capture contains what its header declares), P-054 (explicit
// field ids, no reflection) and 05 s6 (the envelope every document is written in).
//
// The container's first field is always field id 1 (the header record), and a body record is framed at
// CheckpointFormat.FirstRecordFieldId + its kind ordinal, so the stream is self-describing and its kind order is
// checkable without trusting the writer.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Contracts.Tests
{
    /// <summary>Container framing: the header record, the record order, the bounds and the read-side checks.</summary>
    [TestFixture]
    public sealed class CheckpointContainerTests
    {
        private static readonly SchemaRef ForeignContainerSchema =
            new SchemaRef(new SchemaId(new Id128(0x6C00000000000001UL, 0x6D00000000000001UL)), 1U);

        private static readonly Id128 UnknownFeatureId = new Id128(0x6E00000000000001UL, 0x6F00000000000001UL);

        private static void Add<TValue>(
            CheckpointSerializer serializer,
            CheckpointRecordKind kind,
            TValue value)
            where TValue : struct
        {
            Assert.That(
                serializer.TryAdd(kind, value, out DiagnosticCode code, out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
        }

        private static byte[] HeaderBytes(HeaderRecordValue header) =>
            CheckpointTestRecords.HeaderCodec().Encode(header);

        private static byte[] TargetBytes(TargetRecordValue target) =>
            CheckpointTestRecords.TargetCodec().Encode(target);

        private static byte[] SlotBytes(SlotRecordValue slot) =>
            CheckpointTestRecords.SlotCodec().Encode(slot);

        /// <summary>Builds one container document by hand, so a malformed framing is reachable.</summary>
        private static byte[] Container(int[] fieldIds, byte[][] payloads)
        {
            Assert.That(payloads.Length, Is.EqualTo(fieldIds.Length), "one payload per field id");
            var writer = new EnvelopeWriter(
                new EnvelopeHeader(
                    1,
                    0,
                    CheckpointFormat.DocumentSchema,
                    CheckpointFormat.KnownFeatureIds),
                CheckpointFormat.Limits);
            for (int i = 0; i < fieldIds.Length; i++)
            {
                writer.WriteBytesField(fieldIds[i], payloads[i]);
            }

            writer.WriteChecksum();
            return writer.ToArray();
        }

        /// <summary>Walks a container document and returns the field ids it frames, checksum excluded.</summary>
        private static List<int> FieldIdsOf(byte[] document)
        {
            var reader = new EnvelopeReader(document, CheckpointFormat.Limits);
            var ids = new List<int>();
            Assert.That(
                reader.TryReadHeader(CheckpointFormat.KnownFeatureIds, out EnvelopeHeader _),
                Is.True);
            while (reader.TryReadField(out EnvelopeField field))
            {
                if (field.IsChecksum)
                {
                    Assert.That(reader.TryVerifyChecksum(field, out ulong _), Is.True);
                    break;
                }

                ids.Add(field.FieldId);
                Assert.That(reader.Skip(field), Is.True);
            }

            return ids;
        }

        private static void SerializeOrFail(
            CheckpointSerializer serializer,
            HeaderRecordValue header,
            out byte[] document)
        {
            Assert.That(
                serializer.TrySerialize(header, out document, out DiagnosticCode code, out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void MinimalDocumentRoundTripsItsHeaderCountsAndChecksum()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            var serializer = new CheckpointSerializer(codecs);
            HeaderRecordValue header = CheckpointTestRecords.EmptyHeader();

            SerializeOrFail(serializer, header, out byte[] document);
            Assert.That(serializer.RecordCount, Is.EqualTo(0));
            Assert.That(serializer.Codecs, Is.SameAs(codecs));

            Assert.That(
                CheckpointDocument.TryRead(document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(read, Is.Not.Null);
            CheckpointTestRecords.AssertHeaderEquals(header, read!.Header);
            Assert.That(read.Counts.Total, Is.EqualTo(1));
            Assert.That(read.Counts.Scopes, Is.EqualTo(0));
            Assert.That(read.Counts.Installs, Is.EqualTo(0));
            Assert.That(read.Counts.Selections, Is.EqualTo(0));
            Assert.That(read.Counts.Targets, Is.EqualTo(0));
            Assert.That(read.Counts.Slots, Is.EqualTo(0));
            Assert.That(read.Counts.Grants, Is.EqualTo(0));
            Assert.That(read.Counts.Clocks, Is.EqualTo(0));
            Assert.That(read.Counts.Commands, Is.EqualTo(0));
            Assert.That(read.Counts.Messages, Is.EqualTo(0));
            Assert.That(read.Counts.RngStreams, Is.EqualTo(0));
            Assert.That(read.Counts.Cursors, Is.EqualTo(0));
            Assert.That(read.CountOf(CheckpointRecordKind.Header), Is.EqualTo(1));
            Assert.That(read.Checksum, Is.Not.EqualTo(0UL));
            Assert.That(read.RawBytes, Is.EqualTo(document));
            Assert.That(read.DocumentHash, Is.EqualTo(ContentHash.Compute(document)));
            Assert.That(read.DocumentHash.IsEmpty, Is.False);
            Assert.That(
                read.Header.CountsMatch(
                    read.Counts.Scopes,
                    read.Counts.Installs,
                    read.Counts.Selections,
                    read.Counts.Targets,
                    read.Counts.Slots,
                    read.Counts.Grants,
                    read.Counts.Clocks,
                    read.Counts.Commands,
                    read.Counts.Messages,
                    read.Counts.RngStreams,
                    read.Counts.Cursors),
                Is.True);
        }

        [Test]
        public void RecordsAreGroupedInKindOrderSoCaptureOrderDoesNotChangeTheBytes()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(
                CheckpointRecordKind.Target,
                CheckpointRecordKind.Slot);
            HeaderRecordValue header = CheckpointTestRecords.Header(0, 0, 0, 2, 1, 0, 0, 0, 0, 0, 0);
            TargetRecordValue first = CheckpointTestRecords.SampleTarget(1, 1);
            TargetRecordValue second = CheckpointTestRecords.SampleTarget(2, 1);
            SlotRecordValue slot = CheckpointTestRecords.SampleSlot(2, 1, 1);

            var forward = new CheckpointSerializer(codecs);
            Add(forward, CheckpointRecordKind.Target, first);
            Add(forward, CheckpointRecordKind.Slot, slot);
            Add(forward, CheckpointRecordKind.Target, second);
            SerializeOrFail(forward, header, out byte[] forwardDocument);
            Assert.That(forward.RecordCount, Is.EqualTo(3));
            Assert.That(forward.Counts.Targets, Is.EqualTo(2));
            Assert.That(forward.Counts.Slots, Is.EqualTo(1));

            var reverse = new CheckpointSerializer(codecs);
            Add(reverse, CheckpointRecordKind.Slot, slot);
            Add(reverse, CheckpointRecordKind.Target, second);
            Add(reverse, CheckpointRecordKind.Target, first);
            SerializeOrFail(reverse, header, out byte[] reverseDocument);

            Assert.That(reverseDocument, Is.EqualTo(forwardDocument));
            Assert.That(
                FieldIdsOf(forwardDocument),
                Is.EqualTo(new[]
                {
                    CheckpointFormat.HeaderFieldId,
                    CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target),
                    CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target),
                    CheckpointFormat.FieldIdOf(CheckpointRecordKind.Slot),
                }));
        }

        [Test]
        public void TheHeaderRecordIsTheFirstFieldOfTheDocument()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(
                CheckpointRecordKind.Target,
                CheckpointRecordKind.Slot);
            var serializer = new CheckpointSerializer(codecs);
            Add(serializer, CheckpointRecordKind.Target, CheckpointTestRecords.SampleTarget(1, 1));
            Add(serializer, CheckpointRecordKind.Slot, CheckpointTestRecords.SampleSlot(1, 1, 1));
            SerializeOrFail(
                serializer,
                CheckpointTestRecords.Header(0, 0, 0, 1, 1, 0, 0, 0, 0, 0, 0),
                out byte[] document);

            List<int> fieldIds = FieldIdsOf(document);
            Assert.That(fieldIds[0], Is.EqualTo(CheckpointFormat.HeaderFieldId));
            Assert.That(fieldIds.Count, Is.EqualTo(3));
            Assert.That(fieldIds[1], Is.EqualTo(CheckpointFormat.FirstRecordFieldId + 4));
            Assert.That(fieldIds[2], Is.EqualTo(CheckpointFormat.FirstRecordFieldId + 5));
        }

        [Test]
        public void DeclaredCountsThatDisagreeWithTheRecordsCollectedRefuseTheCapture()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(CheckpointRecordKind.Target);
            var serializer = new CheckpointSerializer(codecs);
            Add(serializer, CheckpointRecordKind.Target, CheckpointTestRecords.SampleTarget(1, 1));

            // The capture holds one target but the header declares two: a document never declares a record it
            // does not carry (P-053).
            Assert.That(
                serializer.TrySerialize(
                    CheckpointTestRecords.Header(0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0),
                    out byte[] overDeclared,
                    out DiagnosticCode overCode,
                    out string overDetail),
                Is.False);
            Assert.That(overCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(overCode, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(overDetail, Is.Not.Empty);
            Assert.That(overDeclared.Length, Is.EqualTo(0));

            Assert.That(
                serializer.TrySerialize(
                    CheckpointTestRecords.Header(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
                    out byte[] underDeclared,
                    out DiagnosticCode underCode,
                    out string underDetail),
                Is.False);
            Assert.That(underCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(underDetail, Is.Not.Empty);
            Assert.That(underDeclared.Length, Is.EqualTo(0));

            // The matching declaration is accepted, so the refusal above is the count check and not the capture.
            SerializeOrFail(
                serializer,
                CheckpointTestRecords.Header(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0),
                out byte[] accepted);
            Assert.That(accepted.Length, Is.GreaterThan(0));
        }

        [Test]
        public void CountsMatchIsTrueExactlyForTheCountsTheCaptureHolds()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(
                CheckpointRecordKind.Target,
                CheckpointRecordKind.Slot);
            var serializer = new CheckpointSerializer(codecs);
            Add(serializer, CheckpointRecordKind.Target, CheckpointTestRecords.SampleTarget(1, 1));
            Add(serializer, CheckpointRecordKind.Target, CheckpointTestRecords.SampleTarget(2, 1));
            Add(serializer, CheckpointRecordKind.Slot, CheckpointTestRecords.SampleSlot(1, 1, 1));

            CheckpointCounts counts = serializer.Counts;
            Assert.That(counts.Targets, Is.EqualTo(2));
            Assert.That(counts.Slots, Is.EqualTo(1));
            Assert.That(counts.Scopes, Is.EqualTo(0));
            Assert.That(counts.Total, Is.EqualTo(4));

            HeaderRecordValue header = CheckpointTestRecords.Header(0, 0, 0, 2, 1, 0, 0, 0, 0, 0, 0);
            Assert.That(Match(header, counts), Is.True);
            Assert.That(Match(header, new CheckpointCounts(0, 0, 0, 3, 1, 0, 0, 0, 0, 0, 0)), Is.False);
            Assert.That(Match(header, new CheckpointCounts(0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0)), Is.False);

            // A declared count is an unsigned count: no negative argument can match it (P-053).
            Assert.That(header.CountsMatch(-1, 0, 0, 2, 1, 0, 0, 0, 0, 0, 0), Is.False);
            Assert.That(header.CountsMatch(0, 0, 0, 2, 1, 0, 0, 0, 0, 0, -1), Is.False);
            Assert.That(header.CountsMatch(0, 0, -1, 2, 1, 0, 0, 0, 0, 0, 0), Is.False);
        }

        [Test]
        public void CorruptedBytesAreDetectedWithoutProducingADocument()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            var serializer = new CheckpointSerializer(codecs);
            SerializeOrFail(serializer, CheckpointTestRecords.EmptyHeader(), out byte[] document);

            byte[] corrupted = (byte[])document.Clone();
            corrupted[corrupted.Length / 2] ^= 0x01;
            Assert.That(
                CheckpointDocument.TryRead(
                    corrupted, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(read, Is.Null);
            Assert.That(detail, Is.Not.Empty);

            // A flipped byte inside the trailing checksum is the same failure with a known code: the envelope's
            // FNV-1a checksum no longer matches the bytes before it (05 s6).
            byte[] checksumCorrupted = (byte[])document.Clone();
            checksumCorrupted[checksumCorrupted.Length - 1] ^= 0x01;
            Assert.That(
                CheckpointDocument.TryRead(
                    checksumCorrupted,
                    codecs,
                    out CheckpointDocument? checksumRead,
                    out DiagnosticCode checksumCode,
                    out string checksumDetail),
                Is.False);
            Assert.That(checksumCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(checksumRead, Is.Null);
            Assert.That(checksumDetail, Does.Contain("ChecksumMismatch"));
        }

        [Test]
        public void TruncatedDocumentsAreRefusedAndNeverReadAsValid()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(CheckpointRecordKind.Target);
            var serializer = new CheckpointSerializer(codecs);
            Add(serializer, CheckpointRecordKind.Target, CheckpointTestRecords.SampleTarget(1, 1));
            SerializeOrFail(
                serializer,
                CheckpointTestRecords.Header(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0),
                out byte[] document);

            int[] lengths = { 0, 1, 2, 29, 30, 31, 40, 64, EnvelopeFormat.FixedHeaderSize + 1, document.Length / 2, document.Length - 1 };
            for (int i = 0; i < lengths.Length; i++)
            {
                int length = lengths[i];
                var prefix = new byte[length];
                Buffer.BlockCopy(document, 0, prefix, 0, length);

                bool accepted = CheckpointDocument.TryRead(
                    prefix, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail);
                Assert.That(accepted, Is.False, "a " + length + "-byte prefix is not a document");
                Assert.That(read, Is.Null, "a refused read never yields a document");
                Assert.That(code, Is.Not.EqualTo(DiagnosticCode.None), "a refusal carries a real code");
                Assert.That(detail, Is.Not.Empty);
            }
        }

        [Test]
        public void TrailingBytesAfterTheChecksumAreRefused()
        {
            // The checksum is the last record of a canonical document (05 s6), so a byte after it means the blob
            // carries content the writer did not checksum. TryRead compares the reader's position with the document
            // length at the checksum field and refuses, reporting ResourceUnavailable.
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            var serializer = new CheckpointSerializer(codecs);
            SerializeOrFail(serializer, CheckpointTestRecords.EmptyHeader(), out byte[] document);

            var extended = new byte[document.Length + 3];
            Buffer.BlockCopy(document, 0, extended, 0, document.Length);
            extended[document.Length] = 0xDE;
            extended[document.Length + 1] = 0xAD;
            extended[document.Length + 2] = 0x01;

            bool accepted = CheckpointDocument.TryRead(
                extended, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail);
            Assert.That(accepted, Is.False, "bytes after the checksum are not part of the document");
            Assert.That(read, Is.Null, "a refused read never yields a document");
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(detail, Does.Contain("after its checksum"));

            // The same document without the appended bytes is still accepted, so the refusal above is the trailing
            // bytes and not a reader that rejects its own writer's output.
            Assert.That(
                CheckpointDocument.TryRead(
                    document, codecs, out CheckpointDocument? intact, out DiagnosticCode _, out string intactDetail),
                Is.True,
                intactDetail);
            Assert.That(intact!.RawBytes.Length, Is.EqualTo(document.Length));

            // A truncation of the same document is refused too: the trailer is mandatory, not optional.
            var shortened = new byte[document.Length - 1];
            Buffer.BlockCopy(document, 0, shortened, 0, shortened.Length);
            Assert.That(
                CheckpointDocument.TryRead(shortened, codecs, out CheckpointDocument? _, out DiagnosticCode _, out string _),
                Is.False);
        }

        [Test]
        public void ADocumentForAnotherSchemaIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            byte[] header = HeaderBytes(CheckpointTestRecords.EmptyHeader());
            var writer = new EnvelopeWriter(
                new EnvelopeHeader(1, 0, ForeignContainerSchema, CheckpointFormat.KnownFeatureIds),
                CheckpointFormat.Limits);
            writer.WriteBytesField(CheckpointFormat.HeaderFieldId, header);
            writer.WriteChecksum();

            Assert.That(
                CheckpointDocument.TryRead(
                    writer.ToArray(),
                    codecs,
                    out CheckpointDocument? read,
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(read, Is.Null);
            Assert.That(detail, Does.Contain("schema"));
        }

        [Test]
        public void ADocumentDeclaringAnUnknownRequiredFeatureIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            byte[] header = HeaderBytes(CheckpointTestRecords.EmptyHeader());
            var writer = new EnvelopeWriter(
                new EnvelopeHeader(
                    1,
                    0,
                    CheckpointFormat.DocumentSchema,
                    new[] { UnknownFeatureId }),
                CheckpointFormat.Limits);
            writer.WriteBytesField(CheckpointFormat.HeaderFieldId, header);
            writer.WriteChecksum();

            Assert.That(
                CheckpointDocument.TryRead(
                    writer.ToArray(),
                    codecs,
                    out CheckpointDocument? read,
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(read, Is.Null);
            Assert.That(detail, Does.Contain("UnknownRequiredFeature"));
        }

        [Test]
        public void ARepeatedHeaderOrDescendingKindIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(
                CheckpointRecordKind.Target,
                CheckpointRecordKind.Slot);
            byte[] header = HeaderBytes(CheckpointTestRecords.Header(0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0));
            byte[] target = TargetBytes(CheckpointTestRecords.SampleTarget(1, 1));
            byte[] slot = SlotBytes(CheckpointTestRecords.SampleSlot(1, 1, 1));
            int targetField = CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target);
            int slotField = CheckpointFormat.FieldIdOf(CheckpointRecordKind.Slot);

            byte[] repeated = Container(
                new[] { CheckpointFormat.HeaderFieldId, CheckpointFormat.HeaderFieldId },
                new[] { header, header });
            Assert.That(
                CheckpointDocument.TryRead(
                    repeated, codecs, out CheckpointDocument? repeatedRead, out DiagnosticCode repeatedCode, out string repeatedDetail),
                Is.False);
            Assert.That(repeatedCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(repeatedRead, Is.Null);
            Assert.That(repeatedDetail, Does.Contain("header"));

            byte[] descending = Container(
                new[] { CheckpointFormat.HeaderFieldId, slotField, targetField },
                new[] { header, slot, target });
            Assert.That(
                CheckpointDocument.TryRead(
                    descending, codecs, out CheckpointDocument? descendingRead, out DiagnosticCode descendingCode, out string descendingDetail),
                Is.False);
            Assert.That(descendingCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(descendingRead, Is.Null);
            Assert.That(descendingDetail, Does.Contain("ascend"));
        }

        [Test]
        public void ABodyRecordDocumentOfTheWrongSchemaIsRefusedByItsOwnCodec()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(CheckpointRecordKind.Target);
            byte[] header = HeaderBytes(CheckpointTestRecords.Header(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0));

            var foreign = new EnvelopeWriter(
                new EnvelopeHeader(1, 0, ForeignContainerSchema, CheckpointFormat.KnownFeatureIds),
                CheckpointFormat.Limits);
            foreign.WriteUInt64Field(1, 1UL);
            foreign.WriteChecksum();

            byte[] document = Container(
                new[] { CheckpointFormat.HeaderFieldId, CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target) },
                new[] { header, foreign.ToArray() });

            Assert.That(
                CheckpointDocument.TryRead(
                    document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion));
            Assert.That(read, Is.Null);
            Assert.That(detail, Does.Contain("Target"));
        }

        [Test]
        public void DeclaredCountsThatDisagreeWithTheRecordsPresentRefuseTheRead()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(CheckpointRecordKind.Target);
            byte[] header = HeaderBytes(CheckpointTestRecords.Header(0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0));
            byte[] document = Container(
                new[] { CheckpointFormat.HeaderFieldId, CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target) },
                new[] { header, TargetBytes(CheckpointTestRecords.SampleTarget(1, 1)) });

            Assert.That(
                CheckpointDocument.TryRead(
                    document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(read, Is.Null);
            Assert.That(detail, Does.Contain("declares"));
        }

        [Test]
        public void ADocumentWithoutAHeaderRecordIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(CheckpointRecordKind.Target);
            byte[] document = Container(
                new[] { CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target) },
                new[] { TargetBytes(CheckpointTestRecords.SampleTarget(1, 1)) });

            Assert.That(
                CheckpointDocument.TryRead(
                    document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(read, Is.Null);
            Assert.That(detail, Does.Contain("header"));
        }

        [Test]
        public void ADocumentWithNoCodecForAFieldItCarriesIsRefused()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            byte[] header = HeaderBytes(CheckpointTestRecords.Header(0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0));
            byte[] document = Container(
                new[] { CheckpointFormat.HeaderFieldId, CheckpointFormat.FieldIdOf(CheckpointRecordKind.Target) },
                new[] { header, TargetBytes(CheckpointTestRecords.SampleTarget(1, 1)) });

            Assert.That(
                CheckpointDocument.TryRead(
                    document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(read, Is.Null);
            Assert.That(detail, Does.Contain("no codec"));
        }

        [Test]
        public void EveryRecordKindFramedInOneDocumentRoundTripsThroughTypedReads()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.CompleteSet();
            var serializer = new CheckpointSerializer(codecs);
            TargetRecordValue target = CheckpointTestRecords.SampleTarget(1, 1);

            Add(serializer, CheckpointRecordKind.Scope, CheckpointTestRecords.SampleRootScope(1));
            Add(serializer, CheckpointRecordKind.Install, CheckpointTestRecords.SampleInstall(1, 1));
            Add(serializer, CheckpointRecordKind.Selection, CheckpointTestRecords.SampleSelection(1, 1));
            Add(serializer, CheckpointRecordKind.Target, target);
            Add(serializer, CheckpointRecordKind.Slot, CheckpointTestRecords.SampleSlot(1, 1, 1));
            Add(
                serializer,
                CheckpointRecordKind.Grant,
                CheckpointTestRecords.SampleGrant((uint)GrantKind.ScopeImport, 1, 1, 1));
            Add(serializer, CheckpointRecordKind.Clock, CheckpointTestRecords.SampleClockDeclaration(1));
            Add(serializer, CheckpointRecordKind.Command, CheckpointTestRecords.SampleCommand(5, 1));
            Add(serializer, CheckpointRecordKind.Message, CheckpointTestRecords.SampleMessage(5, 1, 1));
            Add(serializer, CheckpointRecordKind.Rng, CheckpointTestRecords.SampleRng(5));
            Add(
                serializer,
                CheckpointRecordKind.Cursor,
                CheckpointTestRecords.SampleCursor((uint)CursorRowKind.EventCursor, 5));

            SerializeOrFail(
                serializer,
                CheckpointTestRecords.Header(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1),
                out byte[] document);
            Assert.That(serializer.RecordCount, Is.EqualTo(11));

            Assert.That(
                CheckpointDocument.TryRead(document, codecs, out CheckpointDocument? read, out DiagnosticCode code, out string detail),
                Is.True,
                detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(read!.Counts.Total, Is.EqualTo(12));
            for (int ordinal = 1; ordinal < CheckpointFormat.RecordKindCount; ordinal++)
            {
                Assert.That(
                    read.CountOf((CheckpointRecordKind)ordinal),
                    Is.EqualTo(1),
                    "one record of each declared body kind");
            }

            Assert.That(
                read.TryReadRecords(CheckpointRecordKind.Target, out IReadOnlyList<TargetRecordValue> targets, out DiagnosticCode targetCode, out string targetDetail),
                Is.True,
                targetDetail);
            Assert.That(targetCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(targets.Count, Is.EqualTo(1));
            Assert.That(targets[0].Target, Is.EqualTo(target.Target));
            Assert.That(targets[0].Recipe, Is.EqualTo(target.Recipe));

            Assert.That(
                read.TryReadRecords(CheckpointRecordKind.Slot, out IReadOnlyList<SlotRecordValue> slots, out DiagnosticCode slotCode, out string slotDetail),
                Is.True,
                slotDetail);
            Assert.That(slotCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(slots.Count, Is.EqualTo(1));
            Assert.That(slots[0].Key, Is.EqualTo(CheckpointTestRecords.SampleSlot(1, 1, 1).Key));

            // The header is read through Header, never as a body record (P-053).
            Assert.That(
                read.TryReadRecords(CheckpointRecordKind.Header, out IReadOnlyList<HeaderRecordValue> _, out DiagnosticCode headerCode, out string headerDetail),
                Is.False);
            Assert.That(headerCode, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(headerDetail, Is.Not.Empty);

            // A typed read of a kind whose codec serves another value type reports rather than defaulting.
            Assert.That(
                read.TryReadRecords(CheckpointRecordKind.Target, out IReadOnlyList<SlotRecordValue> _, out DiagnosticCode mismatchCode, out string mismatchDetail),
                Is.False);
            Assert.That(mismatchCode, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(mismatchDetail, Does.Contain("SlotRecordValue"));
        }

        private static bool Match(HeaderRecordValue header, CheckpointCounts counts) =>
            header.CountsMatch(
                counts.Scopes,
                counts.Installs,
                counts.Selections,
                counts.Targets,
                counts.Slots,
                counts.Grants,
                counts.Clocks,
                counts.Commands,
                counts.Messages,
                counts.RngStreams,
                counts.Cursors);
    }

    /// <summary>
    /// The codec seam: which kinds a set serves, that a kind has at most one codec, and that a missing or
    /// mismatched codec is a value rather than a fallback (P-054).
    /// </summary>
    [TestFixture]
    public sealed class CheckpointCodecSetTests
    {
        [Test]
        public void AnEmptyCodecSetMissesEveryDeclaredKind()
        {
            var codecs = new CheckpointCodecSet(null);

            Assert.That(codecs.IsComplete, Is.False);
            Assert.That(codecs.CompleteKindCount, Is.EqualTo(0));
            Assert.That(codecs.Schemas.Count, Is.EqualTo(0));
            Assert.That(codecs.KnownFeatureIds, Is.EqualTo(CheckpointFormat.KnownFeatureIds));

            var expected = new List<CheckpointRecordKind>();
            for (int ordinal = 0; ordinal < CheckpointFormat.RecordKindCount; ordinal++)
            {
                expected.Add((CheckpointRecordKind)ordinal);
                Assert.That(
                    codecs.TryGet((CheckpointRecordKind)ordinal, out ICheckpointRecordCodec? codec),
                    Is.False);
                Assert.That(codec, Is.Null);
            }

            Assert.That(codecs.MissingKinds(), Is.EquivalentTo(expected));
            Assert.That(codecs.MissingKinds().Count, Is.EqualTo(CheckpointFormat.RecordKindCount));
        }

        [Test]
        public void ACompleteCodecSetCoversEveryDeclaredKindExactlyOnce()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.CompleteSet();

            Assert.That(codecs.IsComplete, Is.True);
            Assert.That(codecs.CompleteKindCount, Is.EqualTo(CheckpointFormat.RecordKindCount));
            Assert.That(codecs.MissingKinds(), Is.Empty);
            Assert.That(codecs.Schemas.Count, Is.EqualTo(CheckpointFormat.RecordKindCount));
            Assert.That(codecs.Schemas.Count, Is.EqualTo(codecs.CompleteKindCount));

            var schemas = new HashSet<SchemaId>();
            for (int ordinal = 0; ordinal < CheckpointFormat.RecordKindCount; ordinal++)
            {
                var kind = (CheckpointRecordKind)ordinal;
                Assert.That(codecs.TryGet(kind, out ICheckpointRecordCodec? codec), Is.True);
                Assert.That(codec, Is.Not.Null);
                Assert.That(codec!.Kind, Is.EqualTo(kind));
                Assert.That(
                    codec.Schema,
                    Is.EqualTo(CheckpointTestRecords.SchemaOf(kind)),
                    "a codec declares the schema of its own kind");
                Assert.That(schemas.Add(codec.Schema.Id), Is.True, "each kind owns a distinct schema id");
                Assert.That(codecs.Schemas[ordinal], Is.EqualTo(codec.Schema));
            }
        }

        [Test]
        public void ADuplicateNullOrUndeclaredCodecIsRefused()
        {
            ICheckpointRecordCodec header = CheckpointTestRecords.HeaderCodec();
            ICheckpointRecordCodec scope = CheckpointTestRecords.ScopeCodec();

            Assert.Throws<ArgumentException>(
                () => new CheckpointCodecSet(new List<ICheckpointRecordCodec> { header, header }));
            Assert.Throws<ArgumentException>(
                () => new CheckpointCodecSet(new List<ICheckpointRecordCodec> { header, null! }));
            Assert.Throws<ArgumentException>(
                () => new CheckpointCodecSet(new List<ICheckpointRecordCodec>
                {
                    header,
                    CheckpointTestRecords.HeaderCodecForUndeclaredKind(
                        (CheckpointRecordKind)CheckpointFormat.RecordKindCount),
                }));
            Assert.Throws<ArgumentException>(
                () => new CheckpointCodecSet(new List<ICheckpointRecordCodec>
                {
                    header,
                    CheckpointTestRecords.HeaderCodecForUndeclaredKind((CheckpointRecordKind)(-1)),
                }));

            // A second, distinct codec for one kind is refused even when the first one is a different type.
            ICheckpointRecordCodec<HeaderRecordValue> typed = CheckpointTestRecords.HeaderCodec();
            Assert.Throws<ArgumentException>(
                () => new CheckpointCodecSet(new List<ICheckpointRecordCodec> { typed, scope, scope }));
        }

        [Test]
        public void TypedLookupIsFalseForAKindOrValueTypeTheSetDoesNotServe()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf(
                CheckpointRecordKind.Target,
                CheckpointRecordKind.Slot);

            Assert.That(
                codecs.TryGet(CheckpointRecordKind.Target, out ICheckpointRecordCodec<TargetRecordValue>? target),
                Is.True);
            Assert.That(target, Is.Not.Null);

            // The kind is present, but its codec serves another value type: false, never a throw.
            Assert.That(
                codecs.TryGet(CheckpointRecordKind.Target, out ICheckpointRecordCodec<ScopeRecordValue>? wrongType),
                Is.False);
            Assert.That(wrongType, Is.Null);

            Assert.That(
                codecs.TryGet(CheckpointRecordKind.Message, out ICheckpointRecordCodec<MessageRecordValue>? absent),
                Is.False);
            Assert.That(absent, Is.Null);
            Assert.That(
                codecs.TryGet(
                    (CheckpointRecordKind)CheckpointFormat.RecordKindCount,
                    out ICheckpointRecordCodec<HeaderRecordValue>? undeclared),
                Is.False);
            Assert.That(undeclared, Is.Null);
            Assert.That(codecs.IsComplete, Is.False);
            Assert.That(codecs.MissingKinds().Count, Is.EqualTo(CheckpointFormat.RecordKindCount - 3));
        }

        [Test]
        public void AddingARecordWhoseKindHasNoCodecIsRefused()
        {
            var serializer = new CheckpointSerializer(CheckpointTestRecords.SetOf(CheckpointRecordKind.Target));

            Assert.That(
                serializer.TryAdd(
                    CheckpointRecordKind.Message,
                    CheckpointTestRecords.SampleMessage(1, 1, 1),
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(detail, Does.Contain("no codec"));
            Assert.That(serializer.RecordCount, Is.EqualTo(0));

            // A value whose type does not match the registered codec is the same refusal.
            Assert.That(
                serializer.TryAdd(
                    CheckpointRecordKind.Target,
                    CheckpointTestRecords.SampleSlot(1, 1, 1),
                    out DiagnosticCode mismatchCode,
                    out string mismatchDetail),
                Is.False);
            Assert.That(mismatchCode, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(mismatchDetail, Is.Not.Empty);
            Assert.That(serializer.RecordCount, Is.EqualTo(0));
        }

        [Test]
        public void TheHeaderRecordIsSuppliedByTheSerializerAndNeverAdded()
        {
            CheckpointCodecSet codecs = CheckpointTestRecords.SetOf();
            var serializer = new CheckpointSerializer(codecs);

            Assert.That(
                serializer.TryAdd(
                    CheckpointRecordKind.Header,
                    CheckpointTestRecords.EmptyHeader(),
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.OwnershipConflict));
            Assert.That(detail, Does.Contain("supplied by the serializer"));
            Assert.That(serializer.RecordCount, Is.EqualTo(0));
        }

        [Test]
        public void ASerializerWithoutAHeaderCodecWritesNothing()
        {
            var serializer = new CheckpointSerializer(new CheckpointCodecSet(null));

            Assert.That(
                serializer.TrySerialize(
                    CheckpointTestRecords.EmptyHeader(),
                    out byte[] document,
                    out DiagnosticCode code,
                    out string detail),
                Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.MissingDependency));
            Assert.That(detail, Does.Contain("header codec"));
            Assert.That(document.Length, Is.EqualTo(0));

            Assert.That(
                CheckpointDocument.TryRead(
                    new byte[] { 1 },
                    new CheckpointCodecSet(null),
                    out CheckpointDocument? read,
                    out DiagnosticCode readCode,
                    out string readDetail),
                Is.False);
            Assert.That(readCode, Is.Not.EqualTo(DiagnosticCode.None));
            Assert.That(read, Is.Null);
            Assert.That(readDetail, Is.Not.Empty);
        }
    }
}
