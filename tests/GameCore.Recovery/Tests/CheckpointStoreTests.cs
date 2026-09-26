// GC-027's checkpoint-store suite (06 s7, O-20, P-053, P-054).
//
// The subject is the production adapter `GameCore.Execution.Recovery.FileCheckpointStore` (one temporary file, one
// checksummed envelope, one atomic replacement) and its in-memory twin `MemoryCheckpointStore`, which carries the
// same envelope and the same refusals so the contract is drivable without a file. Every case here is a refusal the
// envelope documents: an empty document, a document over the ceiling, a truncated or extended envelope, a foreign
// magic, a declared length that disagrees with the file, a document checksum or an envelope checksum that does not
// verify, and a format version this build does not implement. The bytes the reader is handed are always produced by
// the store itself and then damaged in exactly one field, so each case names the field that made it fail (P-052) and
// the reader is never allowed to return a partial document (P-054).
//
// This suite does not capture a checkpoint and does not decode one: `CheckpointCapture` and
// `CheckpointDocument.TryRead` own those halves, and the GC-018 suites cover them.
#nullable enable
using System;
using System.Globalization;
using System.IO;
using GameCore.Contracts;
using GameCore.Execution.Recovery;
using NUnit.Framework;

namespace GameCore.Recovery.Fixtures.Tests
{
    /// <summary>The publish/read contract of both checkpoint stores, and every refusal the envelope documents.</summary>
    [TestFixture]
    public sealed class CheckpointStoreTests
    {
        private string directory = string.Empty;
        private string path = string.Empty;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "gc027-" + Guid.NewGuid().ToString("N"));
            path = Path.Combine(directory, "recovery.checkpoint");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        [Test]
        public void AFilePublishThenReadReturnsTheSameDocument()
        {
            byte[] document = Document("round-trip");
            var store = new FileCheckpointStore(path);

            Assert.That(store.TryPublish(document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(stored.IsStored, Is.True);
            Assert.That(stored.Location, Is.EqualTo(path));
            Assert.That(stored.DocumentBytes, Is.EqualTo(document.Length));
            Assert.That(stored.DocumentHash, Is.EqualTo(ContentHash.Compute(document)));
            Assert.That(stored.FormatMajor, Is.EqualTo(CheckpointStoreFormat.FormatMajor));
            Assert.That(stored.FormatMinor, Is.EqualTo(CheckpointStoreFormat.FormatMinor));
            Assert.That(store.PublishCount, Is.EqualTo(1));
            Assert.That(store.ReplaceCount, Is.EqualTo(1));
            Assert.That(store.Exists, Is.True);

            Assert.That(store.TryRead(out byte[]? readBack, out StoredCheckpoint read, out DiagnosticCode readCode, out string readDetail),
                Is.True, readDetail);
            Assert.That(readCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(readBack, Is.Not.Null);
            Assert.That(readBack, Is.EqualTo(document), "a published document is read back byte for byte.");
            Assert.That(read.DocumentHash, Is.EqualTo(stored.DocumentHash));
            Assert.That(read.DocumentBytes, Is.EqualTo(stored.DocumentBytes));
            Assert.That(read.DocumentChecksum, Is.EqualTo(stored.DocumentChecksum));
            Assert.That(read.EnvelopeChecksum, Is.EqualTo(stored.EnvelopeChecksum));
            Assert.That(store.ReadCount, Is.EqualTo(1));
            Assert.That(store.RefusedCount, Is.EqualTo(0));
        }

        [Test]
        public void AMemoryPublishThenReadReturnsTheSameDocument()
        {
            byte[] document = Document("round-trip");
            var store = new MemoryCheckpointStore("memory://gc027");

            Assert.That(store.TryPublish(document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(stored.Location, Is.EqualTo("memory://gc027"));
            Assert.That(stored.DocumentHash, Is.EqualTo(ContentHash.Compute(document)));
            Assert.That(store.Exists, Is.True);

            Assert.That(store.TryRead(out byte[]? readBack, out StoredCheckpoint read, out DiagnosticCode readCode, out string readDetail),
                Is.True, readDetail);
            Assert.That(readCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(readBack, Is.Not.Null);
            Assert.That(readBack, Is.EqualTo(document));
            Assert.That(read.DocumentHash, Is.EqualTo(stored.DocumentHash));
            Assert.That(read.Location, Is.EqualTo(store.Location));
        }

        [Test]
        public void PublishingTwiceLeavesTheNewestDocumentStored()
        {
            byte[] older = Document("older");
            byte[] newer = Document("newer-document");
            Assert.That(older, Is.Not.EqualTo(newer), "the two documents must differ for the case to mean anything.");

            foreach (ICheckpointStore store in BothStores())
            {
                Assert.That(store.TryPublish(older, out StoredCheckpoint first, out DiagnosticCode firstCode, out string firstDetail),
                    Is.True, firstDetail);
                Assert.That(firstCode, Is.EqualTo(DiagnosticCode.None));
                Assert.That(store.TryPublish(newer, out StoredCheckpoint second, out DiagnosticCode secondCode, out string secondDetail),
                    Is.True, secondDetail);
                Assert.That(secondCode, Is.EqualTo(DiagnosticCode.None));

                Assert.That(store.TryRead(out byte[]? readBack, out StoredCheckpoint read, out DiagnosticCode readCode, out string readDetail),
                    Is.True, readDetail);
                Assert.That(readCode, Is.EqualTo(DiagnosticCode.None));
                Assert.That(readBack, Is.EqualTo(newer), store.Location + ": the newest publication is the stored one.");
                Assert.That(read.DocumentHash, Is.EqualTo(ContentHash.Compute(newer)));
                Assert.That(second.DocumentHash, Is.EqualTo(ContentHash.Compute(newer)));
                Assert.That(first.DocumentHash, Is.Not.EqualTo(second.DocumentHash));
            }
        }

        [Test]
        public void AnEmptyDocumentIsRefusedWithResourceUnavailable()
        {
            foreach (ICheckpointStore store in BothStores())
            {
                Assert.That(store.TryPublish(Array.Empty<byte>(), out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                    Is.False, store.Location + ": an empty document is not a checkpoint.");
                Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
                Assert.That(detail, Does.Contain("empty document"));
                Assert.That(stored.IsStored, Is.False, "a refused publication describes no stored document.");
                Assert.That(store.Exists, Is.False, "a refused publication stores nothing.");
            }
        }

        [Test]
        public void ADocumentOneByteOverTheCeilingIsRefusedWithBudgetExceeded()
        {
            byte[] tooLarge = new byte[CheckpointStoreFormat.MaxDocumentBytes + 1];
            var memory = new MemoryCheckpointStore("memory://gc027");
            Assert.That(memory.TryPublish(tooLarge, out _, out DiagnosticCode memoryCode, out string memoryDetail), Is.False);
            Assert.That(memoryCode, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(memoryDetail, Does.Contain("ceiling"));
            Assert.That(memory.Exists, Is.False);

            var file = new FileCheckpointStore(path);
            Assert.That(file.TryPublish(tooLarge, out StoredCheckpoint fileStored, out DiagnosticCode fileCode, out string fileDetail),
                Is.False, fileDetail);
            Assert.That(fileCode, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(fileDetail,
                Does.Contain(CheckpointStoreFormat.MaxDocumentBytes.ToString(CultureInfo.InvariantCulture)),
                "the file adapter's refusal names the ceiling it enforced.");
            Assert.That(fileStored.IsStored, Is.False);
            Assert.That(file.Exists, Is.False);
            Assert.That(File.Exists(file.TemporaryLocation), Is.False,
                "a refused publication leaves no temporary artifact behind (O-20).");
            Assert.That(file.RefusedCount, Is.EqualTo(1));
        }

        [Test]
        public void ReadingALocationWithNothingStoredIsRefusedWithResourceUnavailable()
        {
            foreach (ICheckpointStore store in BothStores())
            {
                Assert.That(store.TryRead(out byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                    Is.False, store.Location + ": there is nothing to recover from.");
                Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
                Assert.That(detail, Does.Contain("no checkpoint is stored"));
                Assert.That(document, Is.Null);
                Assert.That(stored.IsStored, Is.False);
            }
        }

        [Test]
        public void EveryCorruptEnvelopeIsRefusedWithResourceUnavailableAndNamesTheField()
        {
            byte[] envelope = PublishedEnvelope();
            long declared = CheckpointStoreEnvelope.DeclaredDocumentBytes(envelope);
            Assert.That(declared, Is.GreaterThan(0L), "the published envelope must declare its document length.");

            byte[] shorterThanHeader = new byte[CheckpointStoreFormat.EnvelopeOverheadBytes - 1];
            Array.Copy(envelope, shorterThanHeader, shorterThanHeader.Length);
            AssertRefused(shorterThanHeader, "shorter than the envelope header");

            byte[] missingTrailerByte = new byte[envelope.Length - 1];
            Array.Copy(envelope, missingTrailerByte, missingTrailerByte.Length);
            AssertRefused(missingTrailerByte, "truncated or extended envelope");

            byte[] foreignMagic = Clone(envelope);
            foreignMagic[0] = (byte)(foreignMagic[0] ^ 0xFF);
            AssertRefused(foreignMagic, "store magic word");

            byte[] flippedDocumentByte = Clone(envelope);
            flippedDocumentByte[CheckpointStoreEnvelope.DocumentOffset] =
                (byte)(flippedDocumentByte[CheckpointStoreEnvelope.DocumentOffset] ^ 0xFF);
            AssertRefused(flippedDocumentByte, "document checksum does not verify");

            byte[] flippedHeaderLength = Clone(envelope);
            WriteUInt64BigEndian(flippedHeaderLength, CheckpointStoreEnvelope.LengthOffset, (ulong)(declared + 1L));
            AssertRefused(flippedHeaderLength, "truncated or extended envelope");

            byte[] flippedDocumentChecksum = Clone(envelope);
            WriteUInt64BigEndian(
                flippedDocumentChecksum,
                CheckpointStoreEnvelope.DocumentChecksumOffset,
                CheckpointStoreEnvelope.DeclaredDocumentChecksum(envelope) ^ 0xFFFFUL);
            AssertRefused(flippedDocumentChecksum, "document checksum does not verify");

            byte[] flippedEnvelopeChecksum = Clone(envelope);
            int last = flippedEnvelopeChecksum.Length - 1;
            flippedEnvelopeChecksum[last] = (byte)(flippedEnvelopeChecksum[last] ^ 0xFF);
            AssertRefused(flippedEnvelopeChecksum, "envelope checksum does not verify");
        }

        [Test]
        public void ADeclaredVersionThisBuildDoesNotImplementIsRefusedWithUnsupportedVersion()
        {
            byte[] envelope = PublishedEnvelope();
            Assert.That(CheckpointStoreEnvelope.DeclaredMajor(envelope), Is.EqualTo(CheckpointStoreFormat.FormatMajor));
            Assert.That(CheckpointStoreEnvelope.DeclaredMinor(envelope), Is.EqualTo(CheckpointStoreFormat.FormatMinor));
            Assert.That(CheckpointStoreFormat.IsSupported(CheckpointStoreFormat.FormatMajor, CheckpointStoreFormat.FormatMinor),
                Is.True);

            AssertVersionRefused(envelope, 1, 1);
            AssertVersionRefused(envelope, 2, 0);
            AssertVersionRefused(envelope, 2, 1);
            AssertVersionRefused(envelope, 0, 0);
            AssertVersionRefused(envelope, 1, 255);
        }

        [Test]
        public void TheEnvelopeLengthIsTheDocumentPlusTheDeclaredOverhead()
        {
            byte[] document = Document("length");
            var store = new FileCheckpointStore(path);
            Assert.That(store.TryPublish(document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));

            long expected = CheckpointStoreFormat.EnvelopeBytesFor(document.Length);
            Assert.That(expected, Is.EqualTo((long)document.Length + CheckpointStoreFormat.EnvelopeOverheadBytes));
            Assert.That(stored.EnvelopeBytes, Is.EqualTo(expected));
            Assert.That(new FileInfo(path).Length, Is.EqualTo(expected), "the file is exactly the envelope.");

            Assert.That(store.TryRead(out byte[]? readBack, out StoredCheckpoint read, out DiagnosticCode readCode, out string readDetail),
                Is.True, readDetail);
            Assert.That(readCode, Is.EqualTo(DiagnosticCode.None));
            Assert.That(readBack, Is.Not.Null);
            Assert.That(read.EnvelopeBytes, Is.EqualTo(expected));

            Assert.That(CheckpointStoreFormat.EnvelopeBytesFor(0L), Is.EqualTo((long)CheckpointStoreFormat.EnvelopeOverheadBytes));
            Assert.That(CheckpointStoreFormat.EnvelopeBytesFor(-1L), Is.EqualTo(-1L), "a negative document length has no envelope.");
            Assert.That(
                CheckpointStoreFormat.EnvelopeBytesFor(CheckpointStoreFormat.MaxDocumentBytes + 1L),
                Is.EqualTo(-1L),
                "a document over the ceiling has no envelope (P-022, P-054).");
        }

        [Test]
        public void ASuccessfulPublishLeavesNoTemporaryArtifactBehind()
        {
            var store = new FileCheckpointStore(path);
            Assert.That(store.TemporaryLocation, Is.EqualTo(path + FileCheckpointStore.TemporarySuffix));
            Assert.That(File.Exists(store.TemporaryLocation), Is.False);
            Assert.That(store.Exists, Is.False);

            Assert.That(store.TryPublish(Document("temporary"), out _, out DiagnosticCode code, out string detail),
                Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));
            Assert.That(File.Exists(store.TemporaryLocation), Is.False, "the temporary artifact was replaced, not kept.");
            Assert.That(store.TemporaryArtifactRemoved, Is.True);
            Assert.That(store.Exists, Is.True);
        }

        [Test]
        public void ARemovedCheckpointIsGoneAndReportedAsAbsent()
        {
            var store = new FileCheckpointStore(path);
            Assert.That(store.TryPublish(Document("removed"), out _, out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));

            Assert.That(store.TryRemove(out string removeDetail), Is.True, removeDetail);
            Assert.That(store.Exists, Is.False);
            Assert.That(store.TryRead(out byte[]? document, out _, out DiagnosticCode readCode, out string readDetail), Is.False);
            Assert.That(document, Is.Null);
            Assert.That(readCode, Is.EqualTo(DiagnosticCode.ResourceUnavailable));
            Assert.That(readDetail, Does.Contain("no checkpoint is stored"));
        }

        private ICheckpointStore[] BothStores() =>
            new ICheckpointStore[]
            {
                new FileCheckpointStore(path),
                new MemoryCheckpointStore("memory://gc027"),
            };

        /// <summary>Deterministic document bytes; the seed reaches every byte so two seeds never collide.</summary>
        private static byte[] Document(string seed)
        {
            var document = new byte[32];
            for (int i = 0; i < document.Length; i++)
            {
                document[i] = (byte)((seed.Length + (i * 7)) & 0xFF);
            }

            return document;
        }

        /// <summary>One envelope the production store framed itself, so a case damages exactly one field of it.</summary>
        private static byte[] PublishedEnvelope()
        {
            var store = new MemoryCheckpointStore("memory://gc027-source");
            byte[] document = Document("envelope");
            Assert.That(store.TryPublish(document, out _, out DiagnosticCode code, out string detail), Is.True, detail);
            Assert.That(code, Is.EqualTo(DiagnosticCode.None));

            byte[]? envelope = store.EnvelopeBytes();
            Assert.That(envelope, Is.Not.Null, "a published store exposes the bytes a corruption case starts from.");
            return envelope!;
        }

        private static byte[] Clone(byte[] source) => (byte[])source.Clone();

        private static void AssertRefused(byte[] envelope, string expectedDetail)
        {
            var store = new MemoryCheckpointStore("memory://gc027-corrupt");
            Assert.That(store.TryOverwriteEnvelope(envelope, out string overwriteDetail), Is.True, overwriteDetail);

            Assert.That(store.TryRead(out byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                Is.False,
                "a damaged envelope must be refused, never read as a partial document (P-054).");
            Assert.That(document, Is.Null, "no reader path may hand back a partial document.");
            Assert.That(stored.IsStored, Is.False, "a refused read describes no stored document.");
            Assert.That(code, Is.EqualTo(DiagnosticCode.ResourceUnavailable), detail);
            Assert.That(detail, Does.Contain(expectedDetail), "the refusal names the field that did not verify (P-052).");
        }

        private static void AssertVersionRefused(byte[] envelope, byte major, byte minor)
        {
            Assert.That(CheckpointStoreFormat.IsSupported(major, minor), Is.False,
                "this case exists for a version the build does not implement.");

            byte[] patched = Clone(envelope);
            patched[CheckpointStoreEnvelope.MajorOffset] = major;
            patched[CheckpointStoreEnvelope.MinorOffset] = minor;

            var store = new MemoryCheckpointStore("memory://gc027-version");
            Assert.That(store.TryOverwriteEnvelope(patched, out string overwriteDetail), Is.True, overwriteDetail);

            string version = major.ToString(CultureInfo.InvariantCulture) + "." + minor.ToString(CultureInfo.InvariantCulture);
            Assert.That(store.TryRead(out byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail),
                Is.False, "store format " + version + " is not one this build implements (P-055).");
            Assert.That(document, Is.Null);
            Assert.That(stored.IsStored, Is.False);
            Assert.That(code, Is.EqualTo(DiagnosticCode.UnsupportedVersion), detail);
            Assert.That(detail, Does.Contain("store format " + version));
            Assert.That(detail, Does.Contain("refused before anything is read"));
        }

        private static void WriteUInt64BigEndian(byte[] destination, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                destination[offset + 7 - i] = (byte)(value >> (8 * i));
            }
        }
    }
}
