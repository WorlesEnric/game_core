// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - checkpoint store (GC-027).
//
// Normative sources: 06 s7 ("Capture at a committed boundary; copy authoritative active/dormant slots and
// composition state under the fence, then serialize off the hot lane. Use a temporary file, checksum, and atomic
// file replacement for a local checkpoint adapter; an object-storage adapter uses its own durable publication
// protocol."), 00 O-20 ("Copy/serialization errors produce no checkpoint; cancel before completed file publication,
// remove temp artifact"), P-053 (a checkpoint is taken at a committed boundary; restore validates everything into a
// new unexposed world before publication), P-054 (canonical byte order, explicit field ids, length bounds, explicit
// null markers; checksums detect corruption, not adversarial tampering) and P-049 ("Pre-mutation failure leaves the
// old published world usable").
//
// WHY THIS EXISTS AT ALL
//
// GC-018's `CheckpointCapture` is deliberately a pure function returning `byte[]`, with publication left to "the
// caller (temp file, checksum, atomic replace)". Nothing in the repository implemented that caller, so an O-22
// recovery had no verified blob to recover from and no place to inject a file-publication fault. This is the
// adapter 06 s7 asks for, and it is a *sample* adapter in the same sense `FileDeliveryJournal` is: one local file,
// one document, one explicit publish/read/remove contract, no directory scan and no discovery.
//
// WHAT IT DELIBERATELY DOES NOT DO
//
//   * It does not capture. The document bytes are produced by `CheckpointCapture` under the committed boundary;
//     this type only knows the bytes, their length, their checksum and their identity (P-053).
//   * It does not decode. A stored document is handed back verbatim; `CheckpointDocument.TryRead` is the one
//     reader that validates it (P-054).
//   * It does not claim exactly-once or tamper resistance. A checksum detects corruption; a document that verifies
//     is the bytes that were published, and nothing more.
//
// THE ENVELOPE
//
// One canonical, self-describing frame around the document bytes (05 s6's rules applied to a file rather than to a
// record stream): a magic word, an explicit format major and minor, a reserved zero field, the document length, a
// checksum of the document, then the document itself, then a checksum over everything before it. All integers are
// big-endian, so the file a capture writes is the file a reader compares, on any host. A file shorter than the
// header, with another magic, with another format major, or whose checksums or length do not verify is refused with
// a code rather than read as a partial document, which is what makes "no checkpoint" observable instead of silent.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using GameCore.Contracts;

namespace GameCore.Execution.Recovery
{
    /// <summary>
    /// The versioned envelope of one stored checkpoint: the format identity, the fixed header layout and the
    /// bounds a reader enforces (P-054).
    /// </summary>
    public static class CheckpointStoreFormat
    {
        /// <summary>Diagnostic format id; never an identity (P-004).</summary>
        public const string FormatName = "gamecore.checkpoint.store/1";

        /// <summary>Major version of the store envelope; another major is a different layout (P-055).</summary>
        public const byte FormatMajor = 1;

        /// <summary>Minor version; a newer minor may add optional fields this reader does not know (P-055).</summary>
        public const byte FormatMinor = 0;

        /// <summary>Magic word of the envelope: 'G','C','C','K', read and written big-endian.</summary>
        public const uint Magic = 0x4743434BU;

        /// <summary>Fixed header bytes before the document: magic, major, minor, reserved, length, checksum.</summary>
        public const int HeaderBytes = 24;

        /// <summary>Trailing bytes after the document: one checksum over header and document.</summary>
        public const int TrailerBytes = 8;

        /// <summary>Bytes an envelope adds to the document it carries.</summary>
        public const int EnvelopeOverheadBytes = HeaderBytes + TrailerBytes;

        /// <summary>
        /// Ceiling on one stored document. It mirrors `CheckpointFormat.MaxDocumentBytes`, which is the bound the
        /// capture and the document reader already enforce; a store that accepted more would hold a blob no restore
        /// can read.
        /// </summary>
        public const int MaxDocumentBytes = 64 * 1024 * 1024;

        /// <summary>Total envelope length for a document of the given length; negative for an impossible length.</summary>
        public static long EnvelopeBytesFor(long documentBytes) =>
            documentBytes < 0 || documentBytes > MaxDocumentBytes ? -1L : documentBytes + EnvelopeOverheadBytes;

        /// <summary>
        /// True when this reader implements the envelope version of a file it opened. A different major is a
        /// different layout; a newer minor is refused because this layout has no optional-field escape hatch, and a
        /// reader that guessed would be reading fields it cannot bound (P-055).
        /// </summary>
        public static bool IsSupported(byte major, byte minor) => major == FormatMajor && minor <= FormatMinor;

        /// <summary>One-line description of the format, for a diagnostic or an evidence file.</summary>
        public static string Describe() =>
            FormatName + " major=" + FormatMajor.ToString(CultureInfo.InvariantCulture)
            + " minor=" + FormatMinor.ToString(CultureInfo.InvariantCulture)
            + " header=" + HeaderBytes.ToString(CultureInfo.InvariantCulture)
            + " maxDocument=" + MaxDocumentBytes.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The identity of one stored checkpoint: where it is, how long it is, which envelope version carries it and
    /// the two checksums and the document hash a caller records as evidence. Every field is a value; the document
    /// bytes are not part of it (P-054).
    /// </summary>
    public readonly struct StoredCheckpoint
    {
        public StoredCheckpoint(
            string location,
            int documentBytes,
            byte formatMajor,
            byte formatMinor,
            ulong documentChecksum,
            ulong envelopeChecksum,
            ContentHash documentHash)
        {
            Location = location ?? string.Empty;
            DocumentBytes = documentBytes;
            FormatMajor = formatMajor;
            FormatMinor = formatMinor;
            DocumentChecksum = documentChecksum;
            EnvelopeChecksum = envelopeChecksum;
            DocumentHash = documentHash;
        }

        /// <summary>Diagnostic location of the stored document; never an identity (P-004).</summary>
        public string Location { get; }

        /// <summary>Length of the document the envelope carries, excluding the envelope itself.</summary>
        public int DocumentBytes { get; }

        public byte FormatMajor { get; }

        public byte FormatMinor { get; }

        /// <summary>Checksum of the document bytes alone, as the header records it.</summary>
        public ulong DocumentChecksum { get; }

        /// <summary>Checksum over header and document, as the trailer records it.</summary>
        public ulong EnvelopeChecksum { get; }

        /// <summary>SHA-256 of the document bytes: the identity a capture publishes and a restore is given.</summary>
        public ContentHash DocumentHash { get; }

        /// <summary>Total bytes on disk or in memory, envelope included.</summary>
        public long EnvelopeBytes => (long)DocumentBytes + CheckpointStoreFormat.EnvelopeOverheadBytes;

        /// <summary>True when this value describes a stored document; a default value describes none.</summary>
        public bool IsStored => !string.IsNullOrEmpty(Location) && DocumentBytes > 0;

        public override string ToString() =>
            "stored(" + DocumentBytes.ToString(CultureInfo.InvariantCulture) + "B,v"
            + FormatMajor.ToString(CultureInfo.InvariantCulture) + "."
            + FormatMinor.ToString(CultureInfo.InvariantCulture) + ",docFnv="
            + DocumentChecksum.ToString("x16", CultureInfo.InvariantCulture) + ",envFnv="
            + EnvelopeChecksum.ToString("x16", CultureInfo.InvariantCulture) + ","
            + DocumentHash.ToHex() + ")";
    }

    /// <summary>
    /// Where one verified checkpoint document is published and read back (06 s7). A store is the caller's seam: the
    /// capture hands it the bytes, a recovery hands it the reserved session's document, and nothing else in the
    /// protocol reads or writes a file (P-053).
    /// </summary>
    public interface ICheckpointStore
    {
        /// <summary>Diagnostic location a caller reports; never an identity (P-004).</summary>
        string Location { get; }

        /// <summary>True when a document is already stored here.</summary>
        bool Exists { get; }

        /// <summary>
        /// Publishes one document so that a reader sees either nothing or the complete verified envelope. A refusal
        /// leaves no partial artifact behind (O-20's "cancel before completed file publication, remove temp
        /// artifact").
        /// </summary>
        bool TryPublish(byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail);

        /// <summary>
        /// Reads the stored document back after verifying the envelope, its version, its length and both checksums.
        /// A file that does not verify is refused with a code, never returned as a partial document (P-054).
        /// </summary>
        bool TryRead(out byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail);

        /// <summary>Deletes the stored document; a test that wants a fresh session rather than a recovery uses it.</summary>
        bool TryRemove(out string detail);
    }

    /// <summary>
    /// The local checkpoint adapter of 06 s7: one temporary file written and flushed, then published by replacement,
    /// with the checksum of the document in the envelope. It is deliberately small and says what it does not
    /// promise: replacement is a two-step operation (write, then replace) whose intermediate state is a *missing*
    /// file rather than a half-written one, because the temporary file is always flushed before it is moved.
    /// </summary>
    public sealed class FileCheckpointStore : ICheckpointStore
    {
        /// <summary>Suffix of the temporary file; it never carries the published document's name (06 s7).</summary>
        public const string TemporarySuffix = ".partial";

        private readonly string path;

        public FileCheckpointStore(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A checkpoint store needs a path.", nameof(path));
            }

            this.path = path;
        }

        public string Location => path;

        /// <summary>Path the next publication writes before replacing the document; reported as evidence.</summary>
        public string TemporaryLocation => path + TemporarySuffix;

        public bool Exists => File.Exists(path);

        /// <summary>Documents this store published; the count a scenario reports.</summary>
        public int PublishCount { get; private set; }

        /// <summary>Documents read back and verified.</summary>
        public int ReadCount { get; private set; }

        /// <summary>Publications and reads this store refused with a code.</summary>
        public int RefusedCount { get; private set; }

        /// <summary>Envelopes refused because a checksum, a length or the magic did not verify (P-054).</summary>
        public int CorruptEnvelopeCount { get; private set; }

        /// <summary>Envelopes refused because their format version is not one this build implements (P-055).</summary>
        public int UnsupportedVersionCount { get; private set; }

        /// <summary>Replacements performed; one per successful publication (06 s7's atomic replacement).</summary>
        public int ReplaceCount { get; private set; }

        /// <summary>
        /// True when the temporary artifact of a refused publication was removed. A false here would mean a refusal
        /// left a partial file behind, which O-20 forbids.
        /// </summary>
        public bool TemporaryArtifactRemoved { get; private set; } = true;

        public bool TryPublish(byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail)
        {
            stored = default(StoredCheckpoint);
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (document == null || document.Length == 0)
            {
                // An empty document is not a checkpoint: `CheckpointCapture` never produces one, and a store that
                // accepted one would publish a file whose restore can only refuse (P-053).
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "an empty document is not a checkpoint; nothing was published (P-053).";
                return false;
            }

            if (document.Length > CheckpointStoreFormat.MaxDocumentBytes)
            {
                RefusedCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the document is " + document.Length.ToString(CultureInfo.InvariantCulture)
                    + " bytes and this store publishes at most "
                    + CheckpointStoreFormat.MaxDocumentBytes.ToString(CultureInfo.InvariantCulture)
                    + " (P-022, P-054); nothing was published.";
                return false;
            }

            byte[] envelope = Frame(document);
            string temporary = TemporaryLocation;
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // The temporary file is written and flushed to stable storage *before* it replaces anything, so a
                // crash during the write leaves the previous document intact rather than a torn new one (06 s7).
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(envelope, 0, envelope.Length);
                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
            }
            catch (Exception exception)
            {
                // A publication that failed here must leave no partial artifact behind (O-20). The temporary file is
                // removed, and the previous document - if any - is whatever the replacement reached.
                TemporaryArtifactRemoved = RemoveTemporary();
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "publishing the checkpoint at " + path + " failed: " + exception.GetType().Name + ": "
                    + exception.Message + "; the temporary artifact was "
                    + (TemporaryArtifactRemoved ? "removed" : "retained and reported") + " (O-20).";
                return false;
            }

            ReplaceCount++;
            PublishCount++;
            TemporaryArtifactRemoved = true;
            stored = Describe(
                path,
                document,
                ReadUInt64(envelope, 16),
                ReadUInt64(envelope, envelope.Length - CheckpointStoreFormat.TrailerBytes));
            return true;
        }

        public bool TryRead(out byte[]? document, out StoredCheckpoint stored, out DiagnosticCode code, out string detail)
        {
            document = null;
            stored = default(StoredCheckpoint);
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (!File.Exists(path))
            {
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "no checkpoint is stored at " + path + "; there is nothing to recover from (P-049).";
                return false;
            }

            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "reading the checkpoint at " + path + " failed: " + exception.GetType().Name + ": "
                    + exception.Message;
                return false;
            }

            if (!TryOpen(raw, out document, out stored, out code, out detail))
            {
                RefusedCount++;
                if (code == DiagnosticCode.UnsupportedVersion)
                {
                    UnsupportedVersionCount++;
                }
                else
                {
                    CorruptEnvelopeCount++;
                }

                document = null;
                return false;
            }

            stored = new StoredCheckpoint(
                path,
                stored.DocumentBytes,
                stored.FormatMajor,
                stored.FormatMinor,
                stored.DocumentChecksum,
                stored.EnvelopeChecksum,
                stored.DocumentHash);
            ReadCount++;
            return true;
        }

        public bool TryRemove(out string detail)
        {
            detail = string.Empty;
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                RemoveTemporary();
                return true;
            }
            catch (Exception exception)
            {
                detail = "deleting the checkpoint at " + path + " failed: " + exception.GetType().Name + ": "
                    + exception.Message;
                return false;
            }
        }

        public override string ToString() =>
            "fileStore(" + path + ",published=" + PublishCount.ToString(CultureInfo.InvariantCulture)
            + ",read=" + ReadCount.ToString(CultureInfo.InvariantCulture) + ")";

        /// <summary>
        /// Verifies one envelope and returns the document it carries. The order is the contract: length first,
        /// because every later read is bounded by it, then magic, then version, then the document checksum, then the
        /// envelope checksum. Each failure names the field that did not verify (P-052).
        /// </summary>
        internal static bool TryOpen(
            byte[]? raw,
            out byte[]? document,
            out StoredCheckpoint stored,
            out DiagnosticCode code,
            out string detail)
        {
            document = null;
            stored = default(StoredCheckpoint);
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (raw == null || raw.Length < CheckpointStoreFormat.EnvelopeOverheadBytes)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the stored checkpoint is shorter than the envelope header ("
                    + (raw == null ? 0 : raw.Length).ToString(CultureInfo.InvariantCulture) + " bytes); it is not a "
                    + "complete envelope and is never read as a partial document (P-054).";
                return false;
            }

            if (ReadUInt32(raw, 0) != CheckpointStoreFormat.Magic)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the stored checkpoint does not begin with the store magic word; it was not written by this "
                    + "adapter (P-054).";
                return false;
            }

            byte major = raw[4];
            byte minor = raw[5];
            if (!CheckpointStoreFormat.IsSupported(major, minor))
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "the stored checkpoint declares store format " + major.ToString(CultureInfo.InvariantCulture)
                    + "." + minor.ToString(CultureInfo.InvariantCulture) + " and this build implements "
                    + CheckpointStoreFormat.FormatMajor.ToString(CultureInfo.InvariantCulture) + "."
                    + CheckpointStoreFormat.FormatMinor.ToString(CultureInfo.InvariantCulture)
                    + "; an unknown required format is refused before anything is read (P-055).";
                return false;
            }

            long declaredLength = (long)ReadUInt64(raw, 8);
            if (declaredLength <= 0 || declaredLength > CheckpointStoreFormat.MaxDocumentBytes)
            {
                code = DiagnosticCode.BudgetExceeded;
                detail = "the stored checkpoint declares a document of "
                    + declaredLength.ToString(CultureInfo.InvariantCulture) + " bytes, which is outside 1.."
                    + CheckpointStoreFormat.MaxDocumentBytes.ToString(CultureInfo.InvariantCulture)
                    + " (P-022, P-054).";
                return false;
            }

            long expected = CheckpointStoreFormat.EnvelopeBytesFor(declaredLength);
            if (expected != raw.Length)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the stored checkpoint declares " + declaredLength.ToString(CultureInfo.InvariantCulture)
                    + " document bytes, so the envelope must be "
                    + expected.ToString(CultureInfo.InvariantCulture) + " bytes, and the file is "
                    + raw.Length.ToString(CultureInfo.InvariantCulture)
                    + "; a truncated or extended envelope is refused (P-054).";
                return false;
            }

            var payload = new byte[declaredLength];
            Array.Copy(raw, CheckpointStoreFormat.HeaderBytes, payload, 0, (int)declaredLength);

            ulong declaredPayloadChecksum = ReadUInt64(raw, 16);
            ulong actualPayloadChecksum = Fnv(payload, 0, payload.Length);
            if (declaredPayloadChecksum != actualPayloadChecksum)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the document checksum does not verify (declared "
                    + declaredPayloadChecksum.ToString("x16", CultureInfo.InvariantCulture) + ", computed "
                    + actualPayloadChecksum.ToString("x16", CultureInfo.InvariantCulture)
                    + "); a corrupted document is never handed to a restore (P-054).";
                return false;
            }

            int trailerAt = raw.Length - CheckpointStoreFormat.TrailerBytes;
            ulong declaredEnvelopeChecksum = ReadUInt64(raw, trailerAt);
            ulong actualEnvelopeChecksum = Fnv(raw, 0, trailerAt);
            if (declaredEnvelopeChecksum != actualEnvelopeChecksum)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the envelope checksum does not verify (declared "
                    + declaredEnvelopeChecksum.ToString("x16", CultureInfo.InvariantCulture) + ", computed "
                    + actualEnvelopeChecksum.ToString("x16", CultureInfo.InvariantCulture)
                    + "); the header was altered after publication (P-054).";
                return false;
            }

            document = payload;
            stored = new StoredCheckpoint(
                string.Empty,
                payload.Length,
                CheckpointStoreFormat.FormatMajor,
                CheckpointStoreFormat.FormatMinor,
                declaredPayloadChecksum,
                declaredEnvelopeChecksum,
                ContentHash.Compute(payload));
            return true;
        }

        /// <summary>Frames one document: header, document, trailer, all big-endian and checksummed (P-054).</summary>
        internal static byte[] Frame(byte[] document)
        {
            int trailerAt = CheckpointStoreFormat.HeaderBytes + document.Length;
            var envelope = new byte[trailerAt + CheckpointStoreFormat.TrailerBytes];
            WriteUInt32(envelope, 0, CheckpointStoreFormat.Magic);
            envelope[4] = CheckpointStoreFormat.FormatMajor;
            envelope[5] = CheckpointStoreFormat.FormatMinor;
            envelope[6] = 0;
            envelope[7] = 0;
            WriteUInt64(envelope, 8, (ulong)document.Length);
            WriteUInt64(envelope, 16, Fnv(document, 0, document.Length));
            Array.Copy(document, 0, envelope, CheckpointStoreFormat.HeaderBytes, document.Length);
            WriteUInt64(envelope, trailerAt, Fnv(envelope, 0, trailerAt));
            return envelope;
        }

        private static StoredCheckpoint Describe(
            string location,
            byte[] document,
            ulong documentChecksum,
            ulong envelopeChecksum) =>
            new StoredCheckpoint(
                location,
                document.Length,
                CheckpointStoreFormat.FormatMajor,
                CheckpointStoreFormat.FormatMinor,
                documentChecksum,
                envelopeChecksum,
                ContentHash.Compute(document));

        private bool RemoveTemporary()
        {
            try
            {
                if (File.Exists(TemporaryLocation))
                {
                    File.Delete(TemporaryLocation);
                }

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>FNV-1a 64 over a range: the same checksum family the envelope and the document use (P-054).</summary>
        internal static ulong Fnv(byte[] data, int offset, int count)
        {
            ulong hash = EnvelopeFormat.FnvOffsetBasis;
            for (int i = 0; i < count; i++)
            {
                hash ^= data[offset + i];
                hash *= EnvelopeFormat.FnvPrime;
            }

            return hash;
        }

        internal static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        internal static void WriteUInt64(byte[] destination, int offset, ulong value)
        {
            WriteUInt32(destination, offset, (uint)(value >> 32));
            WriteUInt32(destination, offset + 4, (uint)value);
        }

        internal static uint ReadUInt32(byte[] source, int offset) =>
            ((uint)source[offset] << 24) | ((uint)source[offset + 1] << 16)
            | ((uint)source[offset + 2] << 8) | source[offset + 3];

        internal static ulong ReadUInt64(byte[] source, int offset) =>
            ((ulong)ReadUInt32(source, offset) << 32) | ReadUInt32(source, offset + 4);
    }

    /// <summary>
    /// An in-memory store with the same envelope and the same refusals, so a pure test can drive the publish/read
    /// contract without a file while the file adapter is checked separately (P-058).
    /// </summary>
    public sealed class MemoryCheckpointStore : ICheckpointStore
    {
        private byte[]? stored;

        public MemoryCheckpointStore(string location)
        {
            Location = string.IsNullOrEmpty(location) ? "memory://checkpoint-store" : location;
        }

        public string Location { get; }

        public bool Exists => stored != null;

        public int PublishCount { get; private set; }

        public int ReadCount { get; private set; }

        public int RefusedCount { get; private set; }

        public bool TryPublish(byte[]? document, out StoredCheckpoint storedValue, out DiagnosticCode code, out string detail)
        {
            storedValue = default(StoredCheckpoint);
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (document == null || document.Length == 0)
            {
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "an empty document is not a checkpoint; nothing was published (P-053).";
                return false;
            }

            if (document.Length > CheckpointStoreFormat.MaxDocumentBytes)
            {
                RefusedCount++;
                code = DiagnosticCode.BudgetExceeded;
                detail = "the document exceeds the store's declared ceiling (P-022, P-054); nothing was published.";
                return false;
            }

            byte[] envelope = FileCheckpointStore.Frame(document);
            if (!FileCheckpointStore.TryOpen(envelope, out byte[]? readBack, out StoredCheckpoint opened, out code, out detail)
                || readBack == null)
            {
                // The in-memory store verifies its own envelope before it keeps it, so a framing defect cannot make a
                // memory-backed test pass while the file adapter fails (P-054).
                RefusedCount++;
                return false;
            }

            stored = envelope;
            storedValue = new StoredCheckpoint(Location, opened.DocumentBytes, opened.FormatMajor, opened.FormatMinor,
                opened.DocumentChecksum, opened.EnvelopeChecksum, opened.DocumentHash);
            PublishCount++;
            return true;
        }

        public bool TryRead(out byte[]? document, out StoredCheckpoint storedValue, out DiagnosticCode code, out string detail)
        {
            document = null;
            storedValue = default(StoredCheckpoint);
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (stored == null)
            {
                RefusedCount++;
                code = DiagnosticCode.ResourceUnavailable;
                detail = "no checkpoint is stored at " + Location + "; there is nothing to recover from (P-049).";
                return false;
            }

            byte[] envelope = stored;
            if (!FileCheckpointStore.TryOpen(envelope, out document, out storedValue, out code, out detail))
            {
                RefusedCount++;
                return false;
            }

            storedValue = new StoredCheckpoint(Location, storedValue.DocumentBytes, storedValue.FormatMajor,
                storedValue.FormatMinor, storedValue.DocumentChecksum, storedValue.EnvelopeChecksum,
                storedValue.DocumentHash);
            ReadCount++;
            return true;
        }

        public bool TryRemove(out string detail)
        {
            detail = string.Empty;
            stored = null;
            return true;
        }

        /// <summary>The raw envelope bytes, so a test can corrupt exactly one field of a published document.</summary>
        public byte[]? EnvelopeBytes() => stored == null ? null : (byte[])stored.Clone();

        /// <summary>Replaces the stored bytes with a corrupted copy, so the reader's refusals are testable.</summary>
        public bool TryOverwriteEnvelope(byte[]? envelope, out string detail)
        {
            detail = string.Empty;
            if (envelope == null || envelope.Length == 0)
            {
                detail = "an empty envelope is not a stored checkpoint.";
                return false;
            }

            stored = envelope;
            return true;
        }

        public override string ToString() =>
            "memoryStore(" + Location + ",published=" + PublishCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One envelope's declared fields, read without verifying anything, so a test can build the corrupt documents
    /// the reader must refuse (a truncated file, a foreign magic, another format version, an altered byte).
    /// </summary>
    public static class CheckpointStoreEnvelope
    {
        /// <summary>Offset of the document inside its envelope.</summary>
        public const int DocumentOffset = CheckpointStoreFormat.HeaderBytes;

        /// <summary>Offset of the format major byte.</summary>
        public const int MajorOffset = 4;

        /// <summary>Offset of the format minor byte.</summary>
        public const int MinorOffset = 5;

        /// <summary>Offset of the declared document length.</summary>
        public const int LengthOffset = 8;

        /// <summary>Offset of the declared document checksum.</summary>
        public const int DocumentChecksumOffset = 16;

        /// <summary>Declared document length of one envelope; zero when the envelope is too short to declare one.</summary>
        public static long DeclaredDocumentBytes(byte[]? envelope) =>
            envelope == null || envelope.Length < CheckpointStoreFormat.HeaderBytes
                ? 0L
                : (long)FileCheckpointStore.ReadUInt64(envelope, LengthOffset);

        /// <summary>Declared document checksum of one envelope, as the header records it.</summary>
        public static ulong DeclaredDocumentChecksum(byte[]? envelope) =>
            envelope == null || envelope.Length < CheckpointStoreFormat.HeaderBytes
                ? 0UL
                : FileCheckpointStore.ReadUInt64(envelope, DocumentChecksumOffset);

        /// <summary>Declared format major of one envelope; zero when the envelope is too short.</summary>
        public static byte DeclaredMajor(byte[]? envelope) =>
            envelope == null || envelope.Length <= MajorOffset ? (byte)0 : envelope[MajorOffset];

        /// <summary>Declared format minor of one envelope; zero when the envelope is too short.</summary>
        public static byte DeclaredMinor(byte[]? envelope) =>
            envelope == null || envelope.Length <= MinorOffset ? (byte)0 : envelope[MinorOffset];

        /// <summary>Every byte offset a corruption case names, so a test says which field it broke.</summary>
        public static IReadOnlyList<int> FieldOffsets { get; } = Array.AsReadOnly(new[]
        {
            0,
            MajorOffset,
            MinorOffset,
            LengthOffset,
            DocumentChecksumOffset,
            DocumentOffset,
        });
    }
}
