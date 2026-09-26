// GameCore.Unity.Runtime (engine-free part, compiled as GameCore.Execution) - the durable delivery adapter (GC-021).
//
// Normative sources: docs/game-core/00-core-protocols.md P-042 (a destination command is an ordinary typed request:
// its result distinguishes `Accepted`, `Rejected`, `Cancelled` and `Committed`, and admission acceptance is not
// gameplay success), P-044 (a post-publication subscriber failure never rolls committed work back, and a
// postpublication delivery error does not undo a committed step), P-045 ("irreversible output adapters consume only
// committed events, use explicit external idempotency keys, and persist an outbox when delivery must survive
// crashes"), P-049 (a post-mutation failure halts the world; "no hidden automatic replay of external side effects is
// allowed" — so redelivery is explicit, never implicit) and P-054 (canonical byte order, explicit field ids, length
// bounds, explicit null markers).
//
// WHAT THIS FILE PROVIDES
//
//   1. `IDeliveryJournal` — where an obligation is persisted before it is treated as durable, with a file-backed
//      implementation (`FileDeliveryJournal`) and an in-memory one for tests that do not need a file.
//   2. `IDeliveryStepHook` and `DeliveryBoundaries` — the boundary seam. Every persistence boundary names itself
//      here before and after it is crossed, so the *test* assemblies can place a deterministic crash at an exact
//      point (`Packages/com.gamecore.unity.runtime/Tests/Delivery/DeliveryCrashFixture.cs` declares the scripting
//      marker and its exception). The seam is deliberately powerless: this assembly can only *report* a boundary,
//      never crash, so a shipping build contains no crash-on-demand API and the release surface is unchanged
//      (04 s6's injected faults are compiled out of release; this seam is not one, it is a callback).
//   3. `IOutboxRowCodec` / `CanonicalOutboxRowCodec` — the canonical frame one outbox row is written as, so the
//      journal is a versioned, self-describing, checksummed record stream rather than a serialization of a C# object.
//   4. `DurableDeliveryAdapter` — the ordering that ties the three together: persist, then apply; pass over, then
//      record; acknowledge, then record. Each ordering is chosen so a crash at either side of a boundary has one
//      defined meaning, and the adapter reports which side it was.
//
// THE ONE THING THIS DELIBERATELY DOES NOT DO
//
// It does not promise exactly-once. P-045's boundary is at-least-once delivery with destination deduplication, and
// that is what the adapter implements: a redelivery reuses the obligation's `IdempotencyKey`, and a destination that
// recognises the key reports `AlreadyApplied` instead of mutating again. A destination that does not recognise it is
// a destination that cannot honour the contract, which the adapter reports rather than hides.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Execution.Delivery
{
    /// <summary>
    /// The named persistence boundaries of a delivery attempt (GC-021). A boundary is named by a string constant and
    /// not by an enum member, because the enum, the marker and its exception are test-only: this assembly can report
    /// where it is, and only a test can decide that a boundary is where the process dies. That split is what keeps a
    /// shipping build free of any crash-on-demand API (04 s6).
    /// </summary>
    public static class DeliveryBoundaries
    {
        /// <summary>Not a boundary; the value a caller passes when neither side of a persist should be reported.</summary>
        public const string None = "none";

        /// <summary>Before an obligation's frame is appended: nothing was persisted (P-045).</summary>
        public const string BeforeAppend = "before-append";

        /// <summary>After the append and before the in-memory outbox applies it: the obligation survived.</summary>
        public const string AfterAppend = "after-append";

        /// <summary>Before an attempt is handed to the destination: the destination was not touched (P-042).</summary>
        public const string BeforeDelivery = "before-delivery";

        /// <summary>After the destination was asked and before the attempt is recorded: the acknowledgement window.</summary>
        public const string AfterDelivery = "after-delivery";

        /// <summary>Before an acknowledgement is persisted: the destination's mutation stands unrecorded locally.</summary>
        public const string BeforeAcknowledge = "before-acknowledge";

        /// <summary>After the acknowledgement was persisted: a redelivery must find it and do nothing.</summary>
        public const string AfterAcknowledge = "after-acknowledge";

        /// <summary>Every declared boundary, so a test can prove it covered the whole set.</summary>
        public static IReadOnlyList<string> All { get; } = Array.AsReadOnly(new[]
        {
            BeforeAppend,
            AfterAppend,
            BeforeDelivery,
            AfterDelivery,
            BeforeAcknowledge,
            AfterAcknowledge,
        });

        public static string Describe()
        {
            var text = new System.Text.StringBuilder();
            for (int i = 0; i < All.Count; i++)
            {
                if (i != 0)
                {
                    text.Append(", ");
                }

                text.Append(All[i]);
            }

            return "delivery boundaries: " + text.ToString();
        }
    }

    /// <summary>
    /// The observation point a delivery adapter reports its persistence boundaries through (GC-021). It is a
    /// *reporting* seam with no power to change the outcome: an implementation may record, count or (in a test
    /// assembly) throw, and the production code path is identical either way. No implementation ships in a release
    /// build, because the type is only ever installed by a caller, and the only callers that install one are tests.
    /// </summary>
    public interface IDeliveryStepHook
    {
        /// <summary>Called at one named boundary, before the adapter proceeds past it.</summary>
        void Reach(string boundary, string detail);
    }

    /// <summary>
    /// Where a durable adapter persists an obligation before it treats the commit as durable. A journal is
    /// append-only because appending is the one file operation whose outcome is unambiguous after a crash: a record
    /// is either complete in the file or absent, and a frame that does not verify is discarded and counted rather
    /// than decoded (P-045, P-054).
    /// </summary>
    public interface IDeliveryJournal
    {
        /// <summary>Diagnostic location of the journal; never an identity (P-004).</summary>
        string Location { get; }

        /// <summary>True when the journal already holds frames from an earlier session (a recovery is required).</summary>
        bool Exists { get; }

        /// <summary>Complete frames currently readable, as reported by the last read or append (P-052).</summary>
        int FrameCount { get; }

        /// <summary>Frames dropped because they did not verify; reported, never silently ignored (P-052).</summary>
        int DiscardedFrameCount { get; }

        /// <summary>Appends one complete frame and returns only once it is durable.</summary>
        bool TryAppend(byte[] frame, out DiagnosticCode code, out string detail);

        /// <summary>Every verified frame in file order; a truncated tail is discarded and counted.</summary>
        bool TryReadAll(out IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail);

        /// <summary>Replaces the journal with one frame set, for a compaction the caller chooses to perform.</summary>
        bool TryReplace(IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail);

        /// <summary>Deletes the journal; used by a test that wants a fresh session rather than a recovery.</summary>
        bool TryRemove(out string detail);
    }

    /// <summary>
    /// A file-backed journal: a length-prefixed, checksummed, append-only frame stream (GC-021's "file-backed
    /// durable test adapter"). It is deliberately a *test* adapter and says so: append is durable, replacement is a
    /// two-step copy that a caller must be prepared to repeat, and a discarded tail is counted rather than repaired.
    /// </summary>
    public sealed class FileDeliveryJournal : IDeliveryJournal
    {
        private const uint FrameMagic = 0x47434F42U;
        private const int FrameHeaderBytes = 20;

        private readonly string path;
        private int frameCount;
        private int discarded;

        public FileDeliveryJournal(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("A journal needs a path.", nameof(path));
            }

            this.path = path;
        }

        public string Location => path;

        public bool Exists => File.Exists(path);

        public int FrameCount => frameCount;

        public int DiscardedFrameCount => discarded;

        public bool TryAppend(byte[] frame, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (frame == null || frame.Length == 0)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "an empty frame is not a journal record (P-054).";
                return false;
            }

            byte[] framed = Frame(frame);
            try
            {
                string? directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                {
                    stream.Write(framed, 0, framed.Length);
                    stream.Flush(true);
                }

                frameCount++;
                return true;
            }
            catch (Exception exception)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "appending to the delivery journal at " + path + " failed: "
                    + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public bool TryReadAll(out IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            frames = Array.Empty<byte[]>();
            discarded = 0;
            frameCount = 0;

            if (!File.Exists(path))
            {
                return true;
            }

            byte[] raw;
            try
            {
                raw = File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "reading the delivery journal at " + path + " failed: "
                    + exception.GetType().Name + ": " + exception.Message;
                return false;
            }

            var read = new List<byte[]>();
            int offset = 0;
            while (offset + FrameHeaderBytes <= raw.Length)
            {
                uint magic = ReadUInt32(raw, offset);
                int length = (int)ReadUInt32(raw, offset + 4);
                ulong declared = ReadUInt64(raw, offset + 8);
                if (magic != FrameMagic || length <= 0)
                {
                    // A frame that does not even declare itself is the end of the usable stream: everything from
                    // here on was not written by this codec, so it is discarded and counted (P-054).
                    discarded++;
                    break;
                }

                int payloadStart = offset + FrameHeaderBytes;
                int payloadEnd = payloadStart + length;
                if (payloadEnd + 8 > raw.Length)
                {
                    discarded++;
                    break;
                }

                var payload = new byte[length];
                Array.Copy(raw, payloadStart, payload, 0, length);
                ulong actual = Fnv(payload);
                if (actual != declared)
                {
                    discarded++;
                    break;
                }

                read.Add(payload);
                offset = payloadEnd + 8;
            }

            if (offset < raw.Length && offset + FrameHeaderBytes > raw.Length)
            {
                // A partial frame header at the tail is a torn write; it is discarded, not decoded (P-054).
                discarded++;
            }

            frames = read;
            frameCount = read.Count;
            return true;
        }

        public bool TryReplace(IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (frames == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "no frames were supplied for the replacement (P-054).";
                return false;
            }

            try
            {
                string temporary = path + ".new";
                using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    for (int i = 0; i < frames.Count; i++)
                    {
                        byte[] framed = Frame(frames[i]);
                        stream.Write(framed, 0, framed.Length);
                    }

                    stream.Flush(true);
                }

                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
                frameCount = frames.Count;
                return true;
            }
            catch (Exception exception)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "replacing the delivery journal at " + path + " failed: "
                    + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
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

                frameCount = 0;
                return true;
            }
            catch (Exception exception)
            {
                detail = "deleting the delivery journal at " + path + " failed: "
                    + exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        public override string ToString() => "fileJournal(" + path + ",frames="
            + frameCount.ToString(CultureInfo.InvariantCulture) + ")";

        /// <summary>One framed record: magic, payload length, FNV-1a of the payload, the payload, the same FNV.</summary>
        private static byte[] Frame(byte[] payload)
        {
            var framed = new byte[FrameHeaderBytes + payload.Length + 8];
            WriteUInt32(framed, 0, FrameMagic);
            WriteUInt32(framed, 4, (uint)payload.Length);
            WriteUInt64(framed, 8, Fnv(payload));
            Array.Copy(payload, 0, framed, FrameHeaderBytes, payload.Length);
            WriteUInt64(framed, FrameHeaderBytes + payload.Length, Fnv(payload));
            return framed;
        }

        private static ulong Fnv(byte[] data)
        {
            ulong hash = EnvelopeFormat.FnvOffsetBasis;
            for (int i = 0; i < data.Length; i++)
            {
                hash ^= data[i];
                hash *= EnvelopeFormat.FnvPrime;
            }

            return hash;
        }

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        private static void WriteUInt64(byte[] destination, int offset, ulong value)
        {
            WriteUInt32(destination, offset, (uint)(value >> 32));
            WriteUInt32(destination, offset + 4, (uint)value);
        }

        private static uint ReadUInt32(byte[] source, int offset) =>
            ((uint)source[offset] << 24) | ((uint)source[offset + 1] << 16)
            | ((uint)source[offset + 2] << 8) | source[offset + 3];

        private static ulong ReadUInt64(byte[] source, int offset) =>
            ((ulong)ReadUInt32(source, offset) << 32) | ReadUInt32(source, offset + 4);
    }

    /// <summary>An in-memory journal, so a test can exercise the adapter's ordering without a file (P-058).</summary>
    public sealed class MemoryDeliveryJournal : IDeliveryJournal
    {
        private readonly List<byte[]> frames = new List<byte[]>();

        public MemoryDeliveryJournal(string location)
        {
            Location = string.IsNullOrEmpty(location) ? "memory://delivery-journal" : location;
        }

        public string Location { get; }

        public bool Exists => frames.Count != 0;

        public int FrameCount => frames.Count;

        public int DiscardedFrameCount => 0;

        public bool TryAppend(byte[] frame, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (frame == null || frame.Length == 0)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "an empty frame is not a journal record (P-054).";
                return false;
            }

            var copy = new byte[frame.Length];
            Array.Copy(frame, copy, frame.Length);
            frames.Add(copy);
            return true;
        }

        public bool TryReadAll(out IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            frames = this.frames.ToArray();
            return true;
        }

        public bool TryReplace(IReadOnlyList<byte[]> frames, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            this.frames.Clear();
            for (int i = 0; i < frames.Count; i++)
            {
                var copy = new byte[frames[i].Length];
                Array.Copy(frames[i], copy, frames[i].Length);
                this.frames.Add(copy);
            }

            return true;
        }

        public bool TryRemove(out string detail)
        {
            detail = string.Empty;
            frames.Clear();
            return true;
        }

        public override string ToString() => "memoryJournal(frames="
            + frames.Count.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>One outbox row as bytes: the journal's own frame codec (P-054).</summary>
    public interface IOutboxRowCodec
    {
        /// <summary>Canonical frame of one outbox row.</summary>
        byte[] Encode(OutboxRecordValue row);

        /// <summary>Reads one frame back; false reports the exact reason rather than substituting a default.</summary>
        bool TryDecode(byte[] frame, out OutboxRecordValue row, out DiagnosticCode code, out string detail);
    }

    /// <summary>
    /// The canonical outbox row frame: a fixed format header, then one framed field per record field in ascending
    /// field-id order, then a trailing FNV-1a over everything before it. Scalars are big-endian, lengths are explicit
    /// bounds, and a null payload is an explicit marker rather than an absent field (05 s6, P-054).
    ///
    /// The *checkpoint* document writes the same record value through the generated serializer bound by the
    /// checkpoint catalog; this codec is the journal's own frame, and the row's content — identities, schema, state,
    /// ordering — is identical either way.
    /// </summary>
    public sealed class CanonicalOutboxRowCodec : IOutboxRowCodec
    {
        public const string FormatName = "gamecore.delivery.row/1";

        public const byte FormatMajor = 1;

        public const byte FormatMinor = 0;

        /// <summary>Field count of the row layout this codec writes; a frame with another count is refused.</summary>
        public const int FieldCount = 27;

        private const int FormatHeaderBytes = 12;

        public byte[] Encode(OutboxRecordValue row)
        {
            var writer = new RowWriter();
            writer.U32(1, row.RowKind);
            writer.U32(2, row.RecordVersion);
            writer.U64(3, row.OutboxHigh);
            writer.U64(4, row.OutboxLow);
            writer.U64(5, row.DestinationHigh);
            writer.U64(6, row.DestinationLow);
            writer.U64(7, row.IdempotencyHigh);
            writer.U64(8, row.IdempotencyLow);
            writer.U64(9, row.SourceEventSequence);
            writer.U64(10, row.SourceStep);
            writer.U64(11, row.SourceEpoch);
            writer.U64(12, row.CausalIssuerHigh);
            writer.U64(13, row.CausalIssuerLow);
            writer.U64(14, row.CausalIssuerSequence);
            writer.U64(15, row.PayloadSchemaHigh);
            writer.U64(16, row.PayloadSchemaLow);
            writer.U32(17, row.PayloadSchemaVersion);
            writer.U32(18, row.DeliveryState);
            writer.U32(19, row.ReasonCode);
            writer.U32(20, row.AttemptCount);
            writer.U32(21, row.Durability);
            writer.U32(22, row.OrderOrdinal);
            writer.U64(23, row.CursorHigh);
            writer.U64(24, row.CursorLow);
            writer.U32(25, row.CursorCount);
            writer.U32(26, row.PrunedCount);
            writer.Payload(27, row.Payload);

            byte[] body = writer.ToArray();
            var framed = new byte[FormatHeaderBytes + body.Length + 8];
            framed[0] = (byte)'G';
            framed[1] = (byte)'C';
            framed[2] = (byte)'O';
            framed[3] = (byte)'B';
            framed[4] = FormatMajor;
            framed[5] = FormatMinor;
            WriteUInt32(framed, 6, OutboxRecordValue.CurrentRecordVersion);
            WriteUInt16(framed, 10, (ushort)FieldCount);
            Array.Copy(body, 0, framed, FormatHeaderBytes, body.Length);
            WriteUInt64(framed, FormatHeaderBytes + body.Length, Fnv(framed, 0, FormatHeaderBytes + body.Length));
            return framed;
        }

        public bool TryDecode(byte[] frame, out OutboxRecordValue row, out DiagnosticCode code, out string detail)
        {
            row = default(OutboxRecordValue);
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (frame == null || frame.Length < FormatHeaderBytes + 8)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "a delivery row frame shorter than its header is not a frame (P-054).";
                return false;
            }

            if (frame[0] != (byte)'G' || frame[1] != (byte)'C' || frame[2] != (byte)'O' || frame[3] != (byte)'B')
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "a delivery row frame without this codec's magic is refused (P-054).";
                return false;
            }

            if (frame[4] != FormatMajor || frame[5] != FormatMinor)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "a delivery row frame declares format " + frame[4].ToString(CultureInfo.InvariantCulture)
                    + "." + frame[5].ToString(CultureInfo.InvariantCulture) + " and this build writes "
                    + FormatMajor.ToString(CultureInfo.InvariantCulture) + "."
                    + FormatMinor.ToString(CultureInfo.InvariantCulture) + " (P-054).";
                return false;
            }

            uint recordVersion = ReadUInt32(frame, 6);
            if (recordVersion != OutboxRecordValue.CurrentRecordVersion)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "a delivery row frame declares record version "
                    + recordVersion.ToString(CultureInfo.InvariantCulture) + " and this build writes "
                    + OutboxRecordValue.CurrentRecordVersion.ToString(CultureInfo.InvariantCulture) + " (P-054).";
                return false;
            }

            ushort declaredFields = ReadUInt16(frame, 10);
            if (declaredFields != FieldCount)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "a delivery row frame declares " + declaredFields.ToString(CultureInfo.InvariantCulture)
                    + " field(s) and this build writes " + FieldCount.ToString(CultureInfo.InvariantCulture)
                    + "; an unknown field set is refused rather than skipped (P-054).";
                return false;
            }

            int bodyStart = FormatHeaderBytes;
            int bodyEnd = frame.Length - 8;
            ulong declared = ReadUInt64(frame, bodyEnd);
            if (Fnv(frame, 0, bodyEnd) != declared)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "a delivery row frame did not match its own checksum (05 s6).";
                return false;
            }

            var fields = new byte[FieldCount + 1][];
            int offset = bodyStart;
            while (offset + 8 <= bodyEnd)
            {
                int fieldId = (int)ReadUInt32(frame, offset);
                int length = (int)ReadUInt32(frame, offset + 4);
                offset += 8;
                if (fieldId < 1 || fieldId > FieldCount || length < 0 || offset + length > bodyEnd)
                {
                    code = DiagnosticCode.ResourceUnavailable;
                    detail = "a delivery row frame carries a field that does not fit it (P-054).";
                    return false;
                }

                var value = new byte[length];
                Array.Copy(frame, offset, value, 0, length);
                fields[fieldId] = value;
                offset += length;
            }

            for (int fieldId = 1; fieldId <= FieldCount; fieldId++)
            {
                if (fields[fieldId] == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "a delivery row frame omits required field "
                        + fieldId.ToString(CultureInfo.InvariantCulture) + " (P-054).";
                    return false;
                }
            }

            if (!TryValidateWidths(fields, out code, out detail))
            {
                return false;
            }

            bool hasPayload = fields[27].Length != 0 && fields[27][0] != 0;
            byte[]? payload = null;
            if (hasPayload)
            {
                payload = new byte[fields[27].Length - 1];
                Array.Copy(fields[27], 1, payload, 0, payload.Length);
            }

            row = new OutboxRecordValue(
                ReadUInt32(fields[1], 0),
                recordVersion,
                ReadUInt64(fields[3], 0),
                ReadUInt64(fields[4], 0),
                ReadUInt64(fields[5], 0),
                ReadUInt64(fields[6], 0),
                ReadUInt64(fields[7], 0),
                ReadUInt64(fields[8], 0),
                ReadUInt64(fields[9], 0),
                ReadUInt64(fields[10], 0),
                ReadUInt64(fields[11], 0),
                ReadUInt64(fields[12], 0),
                ReadUInt64(fields[13], 0),
                ReadUInt64(fields[14], 0),
                ReadUInt64(fields[15], 0),
                ReadUInt64(fields[16], 0),
                ReadUInt32(fields[17], 0),
                ReadUInt32(fields[18], 0),
                ReadUInt32(fields[19], 0),
                ReadUInt32(fields[20], 0),
                ReadUInt32(fields[21], 0),
                ReadUInt32(fields[22], 0),
                ReadUInt64(fields[23], 0),
                ReadUInt64(fields[24], 0),
                ReadUInt32(fields[25], 0),
                ReadUInt32(fields[26], 0),
                payload);
            return true;
        }

        /// <summary>
        /// Every field's exact width, checked before a value is read. A frame whose field widths are not this
        /// layout's is refused rather than read at the wrong offsets, which is the difference between a decode error
        /// and a silently transposed record (P-054).
        /// </summary>
        private static bool TryValidateWidths(byte[][] fields, out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            for (int fieldId = 1; fieldId <= FieldCount; fieldId++)
            {
                int expected = fieldId == 27 ? -1 : (IsUInt64Field(fieldId) ? 8 : 4);
                if (expected < 0)
                {
                    if (fields[fieldId].Length == 0)
                    {
                        code = DiagnosticCode.ResourceUnavailable;
                        detail = "field 27 carries no explicit null or payload marker (P-054).";
                        return false;
                    }

                    continue;
                }

                if (fields[fieldId].Length != expected)
                {
                    code = DiagnosticCode.ResourceUnavailable;
                    detail = "field " + fieldId.ToString(CultureInfo.InvariantCulture) + " is "
                        + fields[fieldId].Length.ToString(CultureInfo.InvariantCulture) + " byte(s) rather than "
                        + expected.ToString(CultureInfo.InvariantCulture) + " (P-054).";
                    return false;
                }
            }

            return true;
        }

        private static bool IsUInt64Field(int fieldId)
        {
            switch (fieldId)
            {
                case 3:
                case 4:
                case 5:
                case 6:
                case 7:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 13:
                case 14:
                case 15:
                case 16:
                case 23:
                case 24:
                    return true;
                default:
                    return false;
            }
        }

        public override string ToString() => FormatName;

        private static ulong Fnv(byte[] data, int offset, int count)
        {
            ulong hash = EnvelopeFormat.FnvOffsetBasis;
            for (int i = offset; i < offset + count; i++)
            {
                hash ^= data[i];
                hash *= EnvelopeFormat.FnvPrime;
            }

            return hash;
        }

        private static void WriteUInt16(byte[] destination, int offset, ushort value)
        {
            destination[offset] = (byte)(value >> 8);
            destination[offset + 1] = (byte)value;
        }

        private static ushort ReadUInt16(byte[] source, int offset) =>
            (ushort)(((uint)source[offset] << 8) | source[offset + 1]);

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        private static void WriteUInt64(byte[] destination, int offset, ulong value)
        {
            WriteUInt32(destination, offset, (uint)(value >> 32));
            WriteUInt32(destination, offset + 4, (uint)value);
        }

        private static uint ReadUInt32(byte[] source, int offset) =>
            ((uint)source[offset] << 24) | ((uint)source[offset + 1] << 16)
            | ((uint)source[offset + 2] << 8) | source[offset + 3];

        private static ulong ReadUInt64(byte[] source, int offset) =>
            ((ulong)ReadUInt32(source, offset) << 32) | ReadUInt32(source, offset + 4);

        /// <summary>One framed field list, written in ascending field-id order (P-008, P-054).</summary>
        private sealed class RowWriter
        {
            private readonly List<byte> body = new List<byte>(256);
            private byte[] scratch = new byte[8];

            internal void U32(int fieldId, uint value)
            {
                Begin(fieldId, 4);
                WriteUInt32(scratch, 0, value);
                Append(scratch, 4);
            }

            internal void U64(int fieldId, ulong value)
            {
                Begin(fieldId, 8);
                WriteUInt64(scratch, 0, value);
                Append(scratch, 8);
            }

            internal void Payload(int fieldId, byte[]? payload)
            {
                int length = payload == null ? 1 : payload.Length + 1;
                Begin(fieldId, length);
                scratch[0] = payload == null ? (byte)0 : (byte)1;
                if (payload != null)
                {
                    Array.Copy(payload, 0, scratch, 1, payload.Length);
                }

                Append(scratch, length);
            }

            internal byte[] ToArray() => body.ToArray();

            private void Begin(int fieldId, int length)
            {
                var header = new byte[8];
                WriteUInt32(header, 0, (uint)fieldId);
                WriteUInt32(header, 4, (uint)length);
                body.AddRange(header);
                if (scratch.Length < length)
                {
                    scratch = new byte[length];
                }
            }

            private void Append(byte[] source, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    body.Add(source[i]);
                }
            }
        }
    }

    /// <summary>What a destination port did with one attempt (P-042's result vocabulary, at the delivery boundary).</summary>
    public enum DestinationOutcome
    {
        /// <summary>The destination applied the mutation in this attempt.</summary>
        Applied = 0,

        /// <summary>
        /// The destination recognised the attempt's idempotency key and applied nothing: the mutation it names is
        /// already there. This is the outcome that makes redelivery safe (P-045).
        /// </summary>
        AlreadyApplied = 1,

        /// <summary>
        /// The destination cannot accept the attempt right now (its provider is unmounted, its target is gone, its
        /// queue is full). The obligation stays open and a later attempt may succeed (P-042's backpressure rule).
        /// </summary>
        Unavailable = 2,

        /// <summary>The destination refused the obligation terminally; the adapter records a terminal refusal.</summary>
        Refused = 3,

        /// <summary>
        /// The destination state cannot express the obligation at all, and no compensation is declared for it. The
        /// adapter records a terminal refusal with this reason rather than inventing a compensation (GC-021's
        /// explicit "compensation or rejection for unsupported destination state", P-003).
        /// </summary>
        UnsupportedState = 4,

        /// <summary>
        /// The destination applied the mutation and could not complete its own bookkeeping, so the caller declares an
        /// explicit compensation instead. The adapter records a terminal compensation with this reason.
        /// </summary>
        Compensated = 5,
    }

    /// <summary>One attempt as a destination port receives it: the command bytes and the key it must deduplicate by.</summary>
    public readonly struct DeliveryAttempt
    {
        public DeliveryAttempt(DeliveryObligation obligation, int attemptOrdinal)
        {
            Obligation = obligation;
            AttemptOrdinal = attemptOrdinal;
        }

        public readonly DeliveryObligation Obligation;

        /// <summary>One-based attempt number, so a port can report which attempt it saw (P-045).</summary>
        public readonly int AttemptOrdinal;

        public Id128 OutboxId => Obligation.Key.OutboxId;

        public Id128 DestinationId => Obligation.Key.DestinationId;

        /// <summary>The explicit external idempotency key; a destination that deduplicates uses this (P-045).</summary>
        public Id128 IdempotencyKey => Obligation.Key.IdempotencyKey;

        public SchemaRef PayloadSchema => Obligation.PayloadSchema;

        public OperationId Causal => Obligation.Causal;

        /// <summary>A copy of the destination command bytes (P-054).</summary>
        public byte[] PayloadBytes() => Obligation.PayloadBytes();

        public override string ToString() => "attempt(#" + AttemptOrdinal.ToString(CultureInfo.InvariantCulture)
            + "," + Obligation.Key.OutboxId.ToString() + ")";
    }

    /// <summary>
    /// The destination half of delivery: what the receiving package exposes to an outbox. It is deliberately tiny
    /// and deliberately *not* a generic gameplay effect: the recipient decides what its mutation is, and it reports
    /// one of six outcomes (P-003, P-042).
    /// </summary>
    public interface IDestinationPort
    {
        /// <summary>Stable identity of the destination this port owns; an obligation for another one is refused.</summary>
        Id128 DestinationId { get; }

        /// <summary>The command schema this port accepts, so a schema mismatch is caught before the port is called.</summary>
        SchemaRef CommandSchema { get; }

        /// <summary>
        /// Applies one attempt, or reports why it did not. `AlreadyApplied` is the honest answer of a destination
        /// that recognises the attempt's idempotency key; `UnsupportedState` is the honest answer for a destination
        /// state the obligation cannot be expressed in. Neither is a success, and neither is a silent drop (P-052).
        /// </summary>
        DestinationOutcome TryApply(in DeliveryAttempt attempt, out DiagnosticCode code, out string detail);
    }

    /// <summary>
    /// Ties an outbox to a journal and a destination port with one explicit ordering, and reports which side of each
    /// boundary a crash occurred on (GC-021).
    ///
    /// The orderings are the content of this type:
    ///
    ///   commit       persist the obligation, then apply it in memory. An obligation the caller was told is durable
    ///                is in the journal before the caller is told anything (P-045).
    ///   pass over    record the attempt, ask the destination, then persist the attempt. A crash between the
    ///                destination's mutation and the persisted attempt is exactly the ack-loss window the protocol
    ///                requires to be safe, and it is safe because a redelivery reuses the idempotency key.
    ///   acknowledge  persist the acknowledgement, then advance the in-memory cursor. A crash before the append
    ///                leaves the obligation delivered-but-unacknowledged, which is redeliverable by construction.
    ///
    /// Volatile and durable are distinguishable in the type, not in a comment: `Durability` is `Durable` only when a
    /// journal is present, and `TryCommit(..., requiresDurability: true)` on a volatile adapter is refused
    /// (`OutboxAdmission.DurabilityUnavailable`) rather than accepted and then lost.
    /// </summary>
    public sealed class DurableDeliveryAdapter
    {
        private readonly IDeliveryJournal? journal;
        private readonly IDeliveryStepHook? hook;
        private readonly IOutboxRowCodec codec;

        public DurableDeliveryAdapter(
            DurableOutbox outbox,
            IDeliveryJournal? journal = null,
            IDeliveryStepHook? hook = null,
            IOutboxRowCodec? codec = null)
        {
            Outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
            this.journal = journal;
            this.hook = hook;
            this.codec = codec ?? new CanonicalOutboxRowCodec();

            // A durable adapter's outbox must say so in its own rows, so a capture records the durability the world
            // actually had rather than the durability a reader hoped for (P-045).
            if (journal != null && outbox.Durability == OutboxDurability.Unspecified)
            {
                outbox.Durability = OutboxDurability.Durable;
            }
        }

        public DurableOutbox Outbox { get; }

        /// <summary>True when this adapter can persist an obligation; the only value that may promise survival.</summary>
        public bool IsDurable => journal != null;

        /// <summary>
        /// The durability this adapter actually provides. `Volatile` is a complete answer, not a degradation: a
        /// caller that needs durability checks `IsDurable` and declines the obligation (P-045).
        /// </summary>
        public OutboxDurability Durability => journal == null ? OutboxDurability.Volatile : OutboxDurability.Durable;

        /// <summary>The journal in use, or null for a volatile adapter.</summary>
        public IDeliveryJournal? Journal => journal;

        /// <summary>Frames appended, one per persisted state change.</summary>
        public int AppendCount { get; private set; }

        /// <summary>Frames the journal refused; a refusal never advances the outbox past what is persisted.</summary>
        public int AppendRefusalCount { get; private set; }

        /// <summary>Frames read back by <see cref="TryRecover"/>.</summary>
        public int RecoveredFrameCount { get; private set; }

        /// <summary>Frames dropped by the journal because they did not verify (P-052).</summary>
        public int DiscardedFrameCount { get; private set; }

        /// <summary>Recoveries performed by this adapter.</summary>
        public int RecoveryCount { get; private set; }

        /// <summary>Attempts a destination reported as already applied, so no second mutation happened (P-045).</summary>
        public int AlreadyAppliedCount { get; private set; }

        /// <summary>Attempts left open because the destination was unavailable.</summary>
        public int UnavailableCount { get; private set; }

        /// <summary>
        /// Commits one obligation: persisted first, applied second. A refusal at either step changes nothing, and a
        /// crash at `DeliveryBoundaries.BeforeAppend` leaves no record while a crash at
        /// `DeliveryBoundaries.AfterAppend` leaves one — which is precisely the pair that proves the
        /// obligation outlives the process that committed it.
        /// </summary>
        public OutboxAdmission TryCommit(
            DeliveryKey key,
            SchemaRef payloadSchema,
            byte[]? payload,
            EventSequence sourceEvent,
            LogicalStepId step,
            AssemblyEpoch epoch,
            OperationId causal,
            bool requiresDurability,
            out DeliveryObligation? obligation,
            out DiagnosticCode code,
            out string detail)
        {
            obligation = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            // Ask the outbox first, so a frame the in-memory outbox would refuse is never persisted: the journal and
            // the outbox cannot disagree about what was committed (P-045).
            if (!Outbox.CanAccept(key, payloadSchema, requiresDurability, out _, out code, out detail))
            {
                return Outbox.TryCommit(
                    key,
                    payloadSchema,
                    payload,
                    sourceEvent,
                    step,
                    epoch,
                    causal,
                    requiresDurability,
                    out obligation,
                    out code,
                    out detail);
            }

            if (journal == null)
            {
                return Outbox.TryCommit(
                    key,
                    payloadSchema,
                    payload,
                    sourceEvent,
                    step,
                    epoch,
                    causal,
                    requiresDurability,
                    out obligation,
                    out code,
                    out detail);
            }

            var pending = new OutboxRecordValue(
                (uint)OutboxRowKind.Obligation,
                OutboxRecordValue.CurrentRecordVersion,
                key.OutboxId.High,
                key.OutboxId.Low,
                key.DestinationId.High,
                key.DestinationId.Low,
                key.IdempotencyKey.High,
                key.IdempotencyKey.Low,
                sourceEvent.Value,
                step.Value,
                epoch.Value,
                causal.IssuerId.High,
                causal.IssuerId.Low,
                causal.IssuerSequence,
                payloadSchema.Id.Value.High,
                payloadSchema.Id.Value.Low,
                payloadSchema.Version,
                (uint)OutboxDeliveryState.Pending,
                (uint)DiagnosticCode.None,
                0U,
                (uint)OutboxDurability.Durable,
                Outbox.NextOrder,
                0UL,
                0UL,
                0U,
                0U,
                payload);

            if (!TryPersist(pending, DeliveryBoundaries.BeforeAppend, DeliveryBoundaries.AfterAppend, out code, out detail))
            {
                // The obligation was never made durable, so the outbox is deliberately not advanced: a caller that
                // was refused is not left holding an obligation the journal does not have (P-045).
                return OutboxAdmission.JournalRefused;
            }

            OutboxAdmission admission = Outbox.TryCommit(
                key,
                payloadSchema,
                payload,
                sourceEvent,
                step,
                epoch,
                causal,
                requiresDurability,
                out obligation,
                out code,
                out detail);

            if (admission != OutboxAdmission.Accepted)
            {
                // The outbox refused what the journal already holds. That is a real inconsistency, and the honest
                // response is to say so rather than to leave a frame that the in-memory state contradicts (P-052).
                code = DiagnosticCode.IdempotencyConflict;
                detail = "the obligation was persisted but the outbox refused it: " + detail;
                return admission;
            }

            if (obligation != null && obligation.Order != pending.OrderOrdinal)
            {
                // The frame was written with the order the outbox *would* assign; a mismatch means the projection and
                // the outbox disagree about canonical order, which P-008 forbids, so the authoritative row is
                // appended. The outbox's order is the one that wins, because it is the one a checkpoint projects.
                TryPersist(RowOf(obligation), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail);
            }

            return OutboxAdmission.Accepted;
        }

        /// <summary>
        /// Hands one obligation to its destination port and records what happened. The destination is asked between
        /// the two delivery boundaries, so a crash at `DeliveryBoundaries.AfterDelivery` reproduces the
        /// ack-loss window exactly: the destination has mutated and this process cannot record that it did.
        /// </summary>
        public DeliveryOutcome TryDeliver(
            Id128 outboxId,
            IDestinationPort port,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (port == null)
            {
                throw new ArgumentNullException(nameof(port));
            }

            if (!Outbox.TryGet(outboxId, out DeliveryObligation? obligation) || obligation == null)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                detail = "obligation " + outboxId.ToString() + " already reached "
                    + obligation.State.ToString()
                    + "; a redelivery is answered from the record rather than handed over again (P-045).";
                return DeliveryOutcome.AlreadyTerminal;
            }

            if (!obligation.Key.DestinationId.Equals(port.DestinationId))
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "obligation " + outboxId.ToString() + " belongs to destination "
                    + obligation.Key.DestinationId.ToString() + " and the port owns "
                    + port.DestinationId.ToString() + "; a delivery is never re-targeted (P-034).";
                return DeliveryOutcome.UnsupportedAttempt;
            }

            if (!obligation.PayloadSchema.Equals(port.CommandSchema))
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "obligation " + outboxId.ToString() + " carries schema "
                    + obligation.PayloadSchema.ToString() + " and the destination accepts "
                    + port.CommandSchema.ToString() + "; a payload is never reinterpreted (P-054).";
                return DeliveryOutcome.UnsupportedAttempt;
            }

            Reach(DeliveryBoundaries.BeforeDelivery, "before handing obligation " + outboxId.ToString() + " over");

            DeliveryOutcome begun = Outbox.TryBeginDelivery(outboxId, out code, out detail);
            if (begun != DeliveryOutcome.Delivered)
            {
                return begun;
            }

            var attempt = new DeliveryAttempt(obligation, (int)obligation.Attempts);
            DestinationOutcome outcome = port.TryApply(attempt, out DiagnosticCode portCode, out string portDetail);
            Reach(DeliveryBoundaries.AfterDelivery, "after asking the destination for obligation "
                + outboxId.ToString());

            if (!TryPersist(RowOf(obligation), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail))
            {
                return DeliveryOutcome.NotRecorded;
            }

            switch (outcome)
            {
                case DestinationOutcome.Applied:
                    return DeliveryOutcome.Delivered;

                case DestinationOutcome.AlreadyApplied:
                    // The destination recognises the key: this attempt applied nothing, so it is safe to settle the
                    // obligation right here. This is the redelivery-after-acknowledgement-loss path (P-045).
                    AlreadyAppliedCount++;
                    return Acknowledge(outboxId, out code, out detail, portCode, portDetail);

                case DestinationOutcome.Unavailable:
                    UnavailableCount++;
                    code = portCode;
                    detail = portDetail;
                    return DeliveryOutcome.Delivered;

                case DestinationOutcome.Compensated:
                    DeliveryOutcome compensated = Outbox.TryCompensate(outboxId, portCode, out code, out detail);
                    if (compensated == DeliveryOutcome.Compensated)
                    {
                        code = portCode;
                        detail = portDetail;
                    }
                    return compensated;

                default:
                    DeliveryOutcome rejected = Outbox.TryReject(
                        outboxId,
                        portCode == DiagnosticCode.None ? DiagnosticCode.ResourceUnavailable : portCode,
                        out code,
                        out detail);
                    if (rejected == DeliveryOutcome.Rejected)
                    {
                        code = portCode == DiagnosticCode.None ? DiagnosticCode.ResourceUnavailable : portCode;
                        detail = portDetail;
                    }
                    return rejected;
        }
        }

        /// <summary>
        /// Persists an acknowledgement and then advances the cursor. A crash at
        /// `DeliveryBoundaries.BeforeAcknowledge` leaves the obligation redeliverable; a crash at
        /// `DeliveryBoundaries.AfterAcknowledge` leaves it settled, and a recovery must find that and do
        /// nothing. Those two observations are the whole point of putting a crash point on each side.
        /// </summary>
        public DeliveryOutcome TryAcknowledge(
            Id128 outboxId,
            out DiagnosticCode code,
            out string detail) =>
            Acknowledge(outboxId, out code, out detail, DiagnosticCode.None, string.Empty);

        /// <summary>Records a terminal refusal, persisted before it is applied in memory (P-052).</summary>
        public DeliveryOutcome TryReject(
            Id128 outboxId,
            DiagnosticCode reason,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!Outbox.TryGet(outboxId, out DeliveryObligation? obligation) || obligation == null)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                return DeliveryOutcome.AlreadyTerminal;
            }

            var settled = new OutboxObligationPreview(obligation, OutboxDeliveryState.Rejected, reason);
            if (!TryPersist(settled.Row(Durability), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail))
            {
                return DeliveryOutcome.NotRecorded;
            }

            return Outbox.TryReject(outboxId, reason, out code, out detail);
        }

        /// <summary>Records a terminal compensation for a destination state the obligation cannot be expressed in.</summary>
        public DeliveryOutcome TryCompensate(
            Id128 outboxId,
            DiagnosticCode reason,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (!Outbox.TryGet(outboxId, out DeliveryObligation? obligation) || obligation == null)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                return DeliveryOutcome.AlreadyTerminal;
            }

            var settled = new OutboxObligationPreview(obligation, OutboxDeliveryState.Compensated, reason);
            if (!TryPersist(settled.Row(Durability), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail))
            {
                return DeliveryOutcome.NotRecorded;
            }

            return Outbox.TryCompensate(outboxId, reason, out code, out detail);
        }

        /// <summary>
        /// Reads the journal and rebuilds the obligation set from it, so a process that died between any two
        /// boundaries resumes with the obligations it had persisted — never with fewer (P-045, P-049, P-053).
        ///
        /// The rebuilt outbox is this adapter's own: the caller supplies no state, because the journal is the state.
        /// </summary>
        public bool TryRecover(out DiagnosticCode code, out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            if (journal == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "a volatile adapter has no journal to recover from, and an in-memory obligation is not "
                    + "recoverable by construction (P-045).";
                return false;
            }

            if (!journal.TryReadAll(out IReadOnlyList<byte[]> frames, out code, out detail))
            {
                return false;
            }

            DiscardedFrameCount = journal.DiscardedFrameCount;
            RecoveredFrameCount = frames.Count;

            IReadOnlyList<OutboxRecordValue> rows = Fold(frames, out code, out detail);
            if (code != DiagnosticCode.None)
            {
                return false;
            }

            if (!DurableOutbox.TryRestore(
                    rows,
                    Outbox.OwnerId,
                    Outbox.Capacity,
                    Outbox.TerminalRetention,
                    OutboxDurability.Durable,
                    out DurableOutbox? restored,
                    out detail)
                || restored == null)
            {
                code = DiagnosticCode.ResourceUnavailable;
                return false;
            }

            Outbox.AdoptFrom(restored);
            RecoveryCount++;
            return true;
        }

        public override string ToString() =>
            "deliveryAdapter(" + Durability.ToString() + ",appends="
            + AppendCount.ToString(CultureInfo.InvariantCulture) + ")";

        /// <summary>
        /// Folds an append-only frame stream into the current row set: the newest frame per (row kind, identity)
        /// wins, and frames that do not decode are refused. Folding is why the journal can stay append-only, which is
        /// the one file operation whose crash outcome is unambiguous (P-045, P-054).
        /// </summary>
        private IReadOnlyList<OutboxRecordValue> Fold(
            IReadOnlyList<byte[]> frames,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            var order = new List<string>();
            var byKey = new Dictionary<string, OutboxRecordValue>();

            for (int i = 0; i < frames.Count; i++)
            {
                if (!codec.TryDecode(frames[i], out OutboxRecordValue row, out code, out detail))
                {
                    detail = "frame " + i.ToString(CultureInfo.InvariantCulture) + ": " + detail;
                    return Array.Empty<OutboxRecordValue>();
                }

                string key = KeyOf(row);
                if (!byKey.ContainsKey(key))
                {
                    order.Add(key);
                }

                byKey[key] = row;
            }

            var rows = new List<OutboxRecordValue>(order.Count);
            for (int i = 0; i < order.Count; i++)
            {
                rows.Add(byKey[order[i]]);
            }

            return rows;
        }

        private static string KeyOf(OutboxRecordValue row) =>
            row.Row == OutboxRowKind.Cursor
                ? "c:" + row.DestinationId.ToString()
                : ((int)row.Row).ToString(CultureInfo.InvariantCulture) + ":" + row.OutboxId.ToString();

        private DeliveryOutcome Acknowledge(
            Id128 outboxId,
            out DiagnosticCode code,
            out string detail,
            DiagnosticCode portCode,
            string portDetail)
        {
            code = portCode;
            detail = portDetail;
            if (!Outbox.TryGet(outboxId, out DeliveryObligation? obligation) || obligation == null)
            {
                code = DiagnosticCode.StaleHandle;
                detail = "obligation " + outboxId.ToString() + " is not tracked by this outbox (P-052).";
                return DeliveryOutcome.NoObligation;
            }

            if (obligation.IsTerminal)
            {
                return DeliveryOutcome.AlreadyTerminal;
            }

            Reach(DeliveryBoundaries.BeforeAcknowledge, "before persisting the acknowledgement of "
                + outboxId.ToString());

            var settled = new OutboxObligationPreview(obligation, OutboxDeliveryState.Acknowledged, DiagnosticCode.None);
            if (!TryPersist(settled.Row(Durability), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail))
            {
                return DeliveryOutcome.NotRecorded;
            }

            DeliveryOutcome outcome = Outbox.TryAcknowledge(outboxId, out code, out detail);
            if (outcome == DeliveryOutcome.Acknowledged)
            {
                if (!TryPersist(RowOf(obligation), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail))
                {
                    return DeliveryOutcome.NotRecorded;
                }

                if (!TryPersist(CursorRowOf(obligation), DeliveryBoundaries.None, DeliveryBoundaries.None, out code, out detail))
                {
                    return DeliveryOutcome.NotRecorded;
                }
            }

            code = portCode;
            detail = portDetail;
            Reach(DeliveryBoundaries.AfterAcknowledge, "after persisting the acknowledgement of "
                + outboxId.ToString());
            return outcome;
        }

        /// <summary>
        /// Appends one row frame, reporting the boundary on either side to the installed hook. A journal refusal
        /// stops the operation before the in-memory outbox moves, so the adapter never claims more than the journal
        /// holds (P-045).
        /// </summary>
        private bool TryPersist(
            OutboxRecordValue row,
            string before,
            string after,
            out DiagnosticCode code,
            out string detail)
        {
            code = DiagnosticCode.None;
            detail = string.Empty;
            Reach(before, "before appending " + row.Row.ToString() + " " + row.OutboxId.ToString());

            if (journal == null)
            {
                Reach(after, "after (volatile) " + row.Row.ToString() + " " + row.OutboxId.ToString());
                return true;
            }

            if (!journal.TryAppend(codec.Encode(row), out code, out detail))
            {
                AppendRefusalCount++;
                return false;
            }

            AppendCount++;
            Reach(after, "after appending " + row.Row.ToString() + " " + row.OutboxId.ToString());
            return true;
        }

        /// <summary>
        /// Reports one persistence boundary to the hook, when one is installed. The boundary is named by a
        /// `DeliveryBoundaries` constant rather than an enum, so a test-only marker type never has to exist in this
        /// assembly at all.
        /// </summary>
        private void Reach(string boundary, string detail)
        {
            if (hook != null)
            {
                hook.Reach(boundary, detail);
            }
        }

        private OutboxRecordValue RowOf(DeliveryObligation obligation) =>
            new OutboxObligationPreview(obligation, obligation.State, obligation.Reason).Row(Durability);

        private OutboxRecordValue CursorRowOf(DeliveryObligation obligation)
        {
            if (!Outbox.TryGetCursor(obligation.Key.DestinationId, out DeliveryCursor cursor))
            {
                cursor = new DeliveryCursor(obligation.Key.DestinationId, obligation.Key.OutboxId, 0U, 0U);
            }

            Id128 destination = cursor.DestinationId;
            Id128 newest = cursor.NewestAcknowledged;
            return new OutboxRecordValue(
                (uint)OutboxRowKind.Cursor,
                OutboxRecordValue.CurrentRecordVersion,
                destination.High,
                destination.Low,
                destination.High,
                destination.Low,
                destination.High,
                destination.Low,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0UL,
                0U,
                (uint)OutboxDeliveryState.Acknowledged,
                (uint)DiagnosticCode.None,
                0U,
                (uint)Durability,
                0U,
                newest.High,
                newest.Low,
                cursor.RetainedTerminals,
                cursor.PrunedTerminals,
                null);
        }

        /// <summary>
        /// A row projected at a state an obligation is *about* to have, so the journal can record the transition
        /// before the in-memory state moves (P-045's persist-then-apply ordering).
        /// </summary>
        private readonly struct OutboxObligationPreview
        {
            private readonly DeliveryObligation obligation;
            private readonly OutboxDeliveryState state;
            private readonly DiagnosticCode reason;

            internal OutboxObligationPreview(
                DeliveryObligation obligation,
                OutboxDeliveryState state,
                DiagnosticCode reason)
            {
                this.obligation = obligation;
                this.state = state;
                this.reason = reason;
            }

            internal OutboxRecordValue Row(OutboxDurability durability)
            {
                DeliveryKey key = obligation.Key;
                SchemaRef schema = obligation.PayloadSchema;
                OperationId causal = obligation.Causal;
                return new OutboxRecordValue(
                    (uint)OutboxRowKind.Obligation,
                    OutboxRecordValue.CurrentRecordVersion,
                    key.OutboxId.High,
                    key.OutboxId.Low,
                    key.DestinationId.High,
                    key.DestinationId.Low,
                    key.IdempotencyKey.High,
                    key.IdempotencyKey.Low,
                    obligation.SourceEvent.Value,
                    obligation.Step.Value,
                    obligation.Epoch.Value,
                    causal.IssuerId.High,
                    causal.IssuerId.Low,
                    causal.IssuerSequence,
                    schema.Id.Value.High,
                    schema.Id.Value.Low,
                    schema.Version,
                    (uint)state,
                    (uint)reason,
                    obligation.Attempts,
                    (uint)durability,
                    obligation.Order,
                    0UL,
                    0UL,
                    0U,
                    0U,
                    obligation.PayloadBytes());
            }
        }
    }
}
