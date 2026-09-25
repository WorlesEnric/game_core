// GameCore.Contracts - checkpoint document framing (GC-018). Normative sources:
// docs/game-core/00-core-protocols.md P-020/P-022 (bounded temporary storage; a bound is a hard limit, never a
// truncation), P-053 (a capture is taken at a committed boundary and contains what the header declares), P-054
// (explicit field ids, canonical byte order, checked lengths, reference tables) and 05 s6 (the envelope every
// record document is written in).
//
// The container is one canonical envelope document (05 s6). Its payload is one framed record document per field:
// the field id is CheckpointFormat.FirstRecordFieldId + the record kind's ordinal, and the field payload is that
// record's own envelope document, bytes and all. The header record is field id 1 and MUST be first; every other
// field id must be strictly ascending, which makes the record order canonical and a reordered or repeated field
// detectable without trusting the writer (P-008).
//
// Serialization is a pure managed operation over immutable copies: nothing here reads live ECS storage, acquires a
// lease or publishes anything, so a capture can serialize off the control lane (P-053, 06 s7).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Contracts
{
    /// <summary>The record counts of one document, in the header's own declaration order (P-053).</summary>
    public readonly struct CheckpointCounts
    {
        public readonly int Scopes;
        public readonly int Installs;
        public readonly int Selections;
        public readonly int Targets;
        public readonly int Slots;
        public readonly int Grants;
        public readonly int Clocks;
        public readonly int Commands;
        public readonly int Messages;
        public readonly int RngStreams;
        public readonly int Cursors;

        public CheckpointCounts(
            int scopes,
            int installs,
            int selections,
            int targets,
            int slots,
            int grants,
            int clocks,
            int commands,
            int messages,
            int rngStreams,
            int cursors)
        {
            Scopes = scopes;
            Installs = installs;
            Selections = selections;
            Targets = targets;
            Slots = slots;
            Grants = grants;
            Clocks = clocks;
            Commands = commands;
            Messages = messages;
            RngStreams = rngStreams;
            Cursors = cursors;
        }

        /// <summary>Every record this document frames, the single header record included.</summary>
        public int Total =>
            1 + Scopes + Installs + Selections + Targets + Slots + Grants + Clocks + Commands + Messages
            + RngStreams + Cursors;

        /// <summary>Count of one kind, so a caller can compare a header with the records it received.</summary>
        public int Of(CheckpointRecordKind kind)
        {
            switch (kind)
            {
                case CheckpointRecordKind.Header: return 1;
                case CheckpointRecordKind.Scope: return Scopes;
                case CheckpointRecordKind.Install: return Installs;
                case CheckpointRecordKind.Selection: return Selections;
                case CheckpointRecordKind.Target: return Targets;
                case CheckpointRecordKind.Slot: return Slots;
                case CheckpointRecordKind.Grant: return Grants;
                case CheckpointRecordKind.Clock: return Clocks;
                case CheckpointRecordKind.Command: return Commands;
                case CheckpointRecordKind.Message: return Messages;
                case CheckpointRecordKind.Rng: return RngStreams;
                case CheckpointRecordKind.Cursor: return Cursors;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown checkpoint record kind.");
            }
        }

        public override string ToString() =>
            "counts(scope=" + Scopes.ToString(CultureInfo.InvariantCulture)
            + ",install=" + Installs.ToString(CultureInfo.InvariantCulture)
            + ",target=" + Targets.ToString(CultureInfo.InvariantCulture)
            + ",slot=" + Slots.ToString(CultureInfo.InvariantCulture)
            + ",grant=" + Grants.ToString(CultureInfo.InvariantCulture)
            + ",command=" + Commands.ToString(CultureInfo.InvariantCulture)
            + ",message=" + Messages.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Collects typed records and serializes one checkpoint document. Records are grouped by kind and ordered
    /// lexicographically by their encoded bytes within each kind, independent of discovery order (P-008, TEST-022).
    /// </summary>
    public sealed class CheckpointSerializer
    {
        private readonly CheckpointCodecSet codecs;
        private readonly SerializationLimits limits;
        private readonly List<byte[]>[] records = new List<byte[]>[CheckpointFormat.RecordKindCount];
        private int total;

        public CheckpointSerializer(CheckpointCodecSet codecs, SerializationLimits? limits = null)
        {
            this.codecs = codecs ?? throw new ArgumentNullException(nameof(codecs));
            this.limits = limits ?? CheckpointFormat.Limits;
            for (int i = 0; i < records.Length; i++)
            {
                records[i] = new List<byte[]>();
            }
        }

        public CheckpointCodecSet Codecs => codecs;

        /// <summary>Records collected so far, the header excluded.</summary>
        public int RecordCount => total;

        /// <summary>The counts a header must declare to match the records collected so far (P-053).</summary>
        public CheckpointCounts Counts => new CheckpointCounts(
            records[(int)CheckpointRecordKind.Scope].Count,
            records[(int)CheckpointRecordKind.Install].Count,
            records[(int)CheckpointRecordKind.Selection].Count,
            records[(int)CheckpointRecordKind.Target].Count,
            records[(int)CheckpointRecordKind.Slot].Count,
            records[(int)CheckpointRecordKind.Grant].Count,
            records[(int)CheckpointRecordKind.Clock].Count,
            records[(int)CheckpointRecordKind.Command].Count,
            records[(int)CheckpointRecordKind.Message].Count,
            records[(int)CheckpointRecordKind.Rng].Count,
            records[(int)CheckpointRecordKind.Cursor].Count);

        /// <summary>
        /// Appends one record. A missing codec, a refused encode or a bound overrun refuses the whole capture
        /// before any bytes exist, so a failed capture never publishes a partial file (P-053, 06 s7).
        /// </summary>
        public bool TryAdd<TValue>(
            CheckpointRecordKind kind,
            TValue value,
            out DiagnosticCode code,
            out string detail)
            where TValue : struct
        {
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (kind == CheckpointRecordKind.Header)
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "the header record is supplied by the serializer, not added as a body record (P-053).";
                return false;
            }

            if (!codecs.TryGet(kind, out ICheckpointRecordCodec<TValue>? codec) || codec == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "no codec is registered for record kind " + kind + " (P-054).";
                return false;
            }

            byte[] document;
            try
            {
                document = codec.Encode(value);
            }
            catch (Exception exception)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "encoding a " + kind + " record failed: " + exception.GetType().Name + ": "
                    + exception.Message;
                return false;
            }

            if (document.Length > limits.MaxFieldBytes)
            {
                code = DiagnosticCode.BudgetExceeded;
                detail = "one " + kind + " record is " + document.Length.ToString(CultureInfo.InvariantCulture)
                    + " bytes, above the configured " + limits.MaxFieldBytes.ToString(CultureInfo.InvariantCulture)
                    + "-byte per-record bound, per P-022 and P-054.";
                return false;
            }

            if (total >= CheckpointFormat.MaxRecordCount)
            {
                code = DiagnosticCode.BudgetExceeded;
                detail = "the capture exceeds the " + CheckpointFormat.MaxRecordCount.ToString(CultureInfo.InvariantCulture)
                    + "-per-record bound, per P-022.";
                return false;
            }

            records[(int)kind].Add(document);
            total++;
            return true;
        }

        /// <summary>
        /// Serializes one complete document: the header record first, then every body record in the format's kind
        /// order, then the envelope's trailing checksum. Refuses when the header's declared counts disagree with the
        /// records actually collected, so a document can never claim a target it does not carry (P-053).
        /// </summary>
        public bool TrySerialize(
            HeaderRecordValue header,
            out byte[] document,
            out DiagnosticCode code,
            out string detail)
        {
            document = Array.Empty<byte>();
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (!codecs.TryGet(CheckpointRecordKind.Header, out ICheckpointRecordCodec<HeaderRecordValue>? headerCodec)
                || headerCodec == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the codec set has no header codec, so no checkpoint document can be written (P-054).";
                return false;
            }

            CheckpointCounts counts = Counts;
            if (!header.CountsMatch(
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
                    counts.Cursors))
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "the header declares " + Describe(header) + " but the capture holds " + counts
                    + "; a document never declares a record it does not carry (P-053).";
                return false;
            }

            if (!header.IsSupportedProtocol)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "the header declares protocol " + header.ProtocolMajor.ToString(CultureInfo.InvariantCulture)
                    + "." + header.ProtocolMinor.ToString(CultureInfo.InvariantCulture)
                    + " but this build writes " + CheckpointFormat.ProtocolMajor.ToString(CultureInfo.InvariantCulture)
                    + "." + CheckpointFormat.ProtocolMinor.ToString(CultureInfo.InvariantCulture) + " (P-055).";
                return false;
            }

            try
            {
                var writer = new EnvelopeWriter(
                    new EnvelopeHeader(
                        CheckpointFormat.ProtocolMajor,
                        CheckpointFormat.ProtocolMinor,
                        CheckpointFormat.DocumentSchema,
                        CheckpointFormat.KnownFeatureIds),
                    limits);

                writer.WriteBytesField(CheckpointFormat.HeaderFieldId, headerCodec.Encode(header));

                for (int k = 1; k < CheckpointFormat.RecordKindCount; k++)
                {
                    List<byte[]> bucket = records[k];
                    bucket.Sort(CompareRecordBytes);
                    int fieldId = CheckpointFormat.FirstRecordFieldId + k;
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        writer.WriteBytesField(fieldId, bucket[i]);
                    }
                }

                writer.WriteChecksum();
                document = writer.ToArray();
                return document.Length <= limits.MaxDocumentBytes;
            }
            catch (Exception exception)
            {
                document = Array.Empty<byte>();
                code = DiagnosticCode.BudgetExceeded;
                detail = "serializing the checkpoint document failed: " + exception.GetType().Name + ": "
                    + exception.Message;
                return false;
            }
        }

        private static int CompareRecordBytes(byte[] left, byte[] right)
        {
            int common = Math.Min(left.Length, right.Length);
            for (int i = 0; i < common; i++)
            {
                int difference = left[i].CompareTo(right[i]);
                if (difference != 0)
                {
                    return difference;
                }
            }

            return left.Length.CompareTo(right.Length);
        }

        private static string Describe(HeaderRecordValue header) =>
            "counts(scope=" + header.ScopeCount.ToString(CultureInfo.InvariantCulture)
            + ",install=" + header.InstallCount.ToString(CultureInfo.InvariantCulture)
            + ",target=" + header.TargetCount.ToString(CultureInfo.InvariantCulture)
            + ",slot=" + header.SlotCount.ToString(CultureInfo.InvariantCulture)
            + ",grant=" + header.GrantCount.ToString(CultureInfo.InvariantCulture)
            + ",command=" + header.CommandCount.ToString(CultureInfo.InvariantCulture)
            + ",message=" + header.MessageCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One verified checkpoint document. A document is only constructed when its envelope, its record framing, its
    /// header record, its per-kind schema versions and its declared counts all agree, so a caller that holds one has
    /// already passed every structural check (P-053, P-054). No record value is decoded during construction beyond
    /// the header: typed access decodes on demand, so a restore pays only for what it reads.
    /// </summary>
    public sealed class CheckpointDocument
    {
        private readonly List<byte[]>[] records = new List<byte[]>[CheckpointFormat.RecordKindCount];
        private readonly CheckpointCodecSet codecs;

        private CheckpointDocument(CheckpointCodecSet codecs, HeaderRecordValue header, ulong checksum, byte[] rawBytes)
        {
            this.codecs = codecs;
            Header = header;
            Checksum = checksum;
            RawBytes = rawBytes;
            for (int i = 0; i < records.Length; i++)
            {
                records[i] = new List<byte[]>();
            }
        }

        public HeaderRecordValue Header { get; private set; }
        /// <summary>
        /// The envelope's own trailing FNV-1a checksum, recomputed and matched by <see cref="TryRead"/> (05 s6).
        /// It detects corruption, not tampering, and it is never a stable identity.
        /// </summary>
        public ulong Checksum { get; private set; }

        /// <summary>
        /// The exact bytes this document was read from. A capture writes them unchanged and a restore hashes them, so
        /// a caller always names one blob rather than a re-encoding of it (06 s7's checksum-beside-the-file rule).
        /// </summary>
        public byte[] RawBytes { get; }

        /// <summary>SHA-256 of <see cref="RawBytes"/>; the blob identity a restore records and a caller can prove.</summary>
        public ContentHash DocumentHash => ContentHash.Compute(RawBytes);

        /// <summary>Records of one kind, header excluded.</summary>
        public int CountOf(CheckpointRecordKind kind) =>
            kind == CheckpointRecordKind.Header ? 1 : records[(int)kind].Count;

        /// <summary>The counts this document actually carries (P-053).</summary>
        public CheckpointCounts Counts => new CheckpointCounts(
            records[(int)CheckpointRecordKind.Scope].Count,
            records[(int)CheckpointRecordKind.Install].Count,
            records[(int)CheckpointRecordKind.Selection].Count,
            records[(int)CheckpointRecordKind.Target].Count,
            records[(int)CheckpointRecordKind.Slot].Count,
            records[(int)CheckpointRecordKind.Grant].Count,
            records[(int)CheckpointRecordKind.Clock].Count,
            records[(int)CheckpointRecordKind.Command].Count,
            records[(int)CheckpointRecordKind.Message].Count,
            records[(int)CheckpointRecordKind.Rng].Count,
            records[(int)CheckpointRecordKind.Cursor].Count);

        /// <summary>
        /// Reads and verifies one document. Every failure is a coded value, never an exception and never a partially
        /// decoded document (P-052, P-053).
        /// </summary>
        public static bool TryRead(
            byte[]? document,
            CheckpointCodecSet codecs,
            out CheckpointDocument? read,
            out DiagnosticCode code,
            out string detail)
        {
            read = null;
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (codecs == null)
            {
                throw new ArgumentNullException(nameof(codecs));
            }

            if (document == null || document.Length == 0)
            {
                code = DiagnosticCode.ResourceUnavailable;
                detail = "the checkpoint document is empty (05 s6).";
                return false;
            }

            var reader = new EnvelopeReader(document, CheckpointFormat.Limits);
            if (!reader.TryReadHeader(CheckpointFormat.KnownFeatureIds, out EnvelopeHeader header))
            {
                code = CheckpointErrors.CodeFor(reader.LastError);
                detail = "the checkpoint document header was refused: " + reader.LastError + " (P-055).";
                return false;
            }

            if (!header.Schema.Equals(CheckpointFormat.DocumentSchema))
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "the document names schema " + header.Schema.ToString() + " rather than "
                    + CheckpointFormat.DocumentSchema.ToString() + " (P-054).";
                return false;
            }

            var pending = new CheckpointDocument(codecs, default(HeaderRecordValue), 0UL, document);
            bool sawHeader = false;
            bool sawChecksum = false;
            int lastFieldId = CheckpointFormat.HeaderFieldId - 1;
            ulong declaredChecksum = 0UL;

            while (true)
            {
                if (!reader.TryReadField(out EnvelopeField field))
                {
                    if (reader.LastError == EnvelopeError.Truncated && !sawChecksum)
                    {
                        // The envelope ends without its trailing checksum, so the document is not one (05 s6).
                        code = DiagnosticCode.UnsupportedVersion;
                        detail = "the checkpoint document carries no trailing checksum (05 s6).";
                        return false;
                    }

                    code = CheckpointErrors.CodeFor(reader.LastError);
                    detail = "reading a checkpoint field failed: " + reader.LastError + " (05 s6).";
                    return false;
                }

                if (field.IsChecksum)
                {
                    if (!reader.TryVerifyChecksum(field, out declaredChecksum))
                    {
                        code = CheckpointErrors.CodeFor(reader.LastError);
                        detail = "the checkpoint document checksum did not match its content: "
                            + reader.LastError + " (05 s6).";
                        return false;
                    }

                    sawChecksum = true;
                    if (reader.Position != document.Length)
                    {
                        // The checksum is the last record of a canonical document; a byte after it means the file
                        // carries content the writer did not checksum, which is a different document (05 s6).
                        code = DiagnosticCode.ResourceUnavailable;
                        detail = "the checkpoint document carries "
                            + (document.Length - reader.Position).ToString(CultureInfo.InvariantCulture)
                            + " byte(s) after its checksum (05 s6).";
                        return false;
                    }

                    break;
                }

                if (field.Type != WireType.Bytes)
                {
                    code = DiagnosticCode.UnsupportedVersion;
                    detail = "checkpoint field " + field.FieldId.ToString(CultureInfo.InvariantCulture)
                        + " is wire type " + field.Type + " rather than Bytes (05 s6).";
                    return false;
                }

                if (field.FieldId < lastFieldId)
                {
                    // Each kind may contain many records; only the transition between kinds must ascend.
                    code = DiagnosticCode.OwnershipConflict;
                    detail = "checkpoint field " + field.FieldId.ToString(CultureInfo.InvariantCulture)
                        + " follows field " + lastFieldId.ToString(CultureInfo.InvariantCulture)
                        + "; record kinds must ascend (P-008).";
                    return false;
                }

                if (!reader.TryReadBytes(field, out byte[]? payload) || payload == null)
                {
                    code = CheckpointErrors.CodeFor(reader.LastError);
                    detail = "checkpoint field " + field.FieldId.ToString(CultureInfo.InvariantCulture)
                        + " carried no payload: " + reader.LastError + " (05 s6).";
                    return false;
                }

                if (field.FieldId == CheckpointFormat.HeaderFieldId)
                {
                    if (sawHeader)
                    {
                        code = DiagnosticCode.OwnershipConflict;
                        detail = "the document carries more than one header record (P-053).";
                        return false;
                    }

                    if (!codecs.TryGet(CheckpointRecordKind.Header, out ICheckpointRecordCodec<HeaderRecordValue>? headerCodec)
                        || headerCodec == null)
                    {
                        code = DiagnosticCode.MissingDependency;
                        detail = "the codec set has no header codec, so no checkpoint document can be read (P-054).";
                        return false;
                    }

                    if (!headerCodec.TryDecode(payload, out HeaderRecordValue headerValue, out EnvelopeError headerError))
                    {
                        code = CheckpointErrors.CodeFor(headerError);
                        detail = "the header record was refused: " + headerError + " (P-053).";
                        return false;
                    }

                    pending.Header = headerValue;
                    sawHeader = true;
                    lastFieldId = field.FieldId;
                    continue;
                }

                if (!CheckpointFormat.TryKindOfField(field.FieldId, out CheckpointRecordKind kind))
                {
                    code = DiagnosticCode.UnsupportedVersion;
                    detail = "checkpoint field " + field.FieldId.ToString(CultureInfo.InvariantCulture)
                        + " names no declared record-kind (P-054).";
                    return false;
                }

                if (!sawHeader)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "a " + kind + " record appears before the header record (P-053).";
                    return false;
                }

                if (!codecs.TryGet(kind, out ICheckpointRecordCodec? recordCodec) || recordCodec == null)
                {
                    code = DiagnosticCode.MissingDependency;
                    detail = "no codec is registered for record kind " + kind
                        + ", so this document cannot be restored by this build (P-054).";
                    return false;
                }

                if (!recordCodec.TryValidate(payload, out EnvelopeError recordError))
                {
                    code = CheckpointErrors.CodeFor(recordError);
                    detail = "the " + kind + " record at field "
                        + field.FieldId.ToString(CultureInfo.InvariantCulture) + " was refused: " + recordError
                        + " (P-054).";
                    return false;
                }

                pending.records[(int)kind].Add(payload);
                lastFieldId = field.FieldId;
            }

            if (!sawHeader)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the checkpoint document carries no header record (P-053).";
                return false;
            }

            if (!sawChecksum)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "the checkpoint document carries no trailing checksum (05 s6).";
                return false;
            }

            if (!pending.Header.IsSupportedProtocol)
            {
                code = DiagnosticCode.UnsupportedVersion;
                detail = "the captured protocol " + pending.Header.ProtocolMajor.ToString(CultureInfo.InvariantCulture)
                    + "." + pending.Header.ProtocolMinor.ToString(CultureInfo.InvariantCulture)
                    + " is not one this build supports (P-055).";
                return false;
            }

            CheckpointCounts counts = pending.Counts;
            if (!pending.Header.CountsMatch(
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
                    counts.Cursors))
            {
                code = DiagnosticCode.MissingDependency;
                detail = "the header declares " + pending.Header.ToString() + " but the document carries " + counts
                    + "; a truncated or padded document is refused instead of partially restored (P-053).";
                return false;
            }

            pending.Checksum = declaredChecksum;
            read = pending;
            return true;
        }

        /// <summary>
        /// Decodes every record of one kind, in document order. A record that passed framing still has to decode,
        /// and a failure here reports rather than substituting a default (P-054).
        /// </summary>
        public bool TryReadRecords<TValue>(
            CheckpointRecordKind kind,
            out IReadOnlyList<TValue> values,
            out DiagnosticCode code,
            out string detail)
            where TValue : struct
        {
            values = Array.Empty<TValue>();
            code = DiagnosticCode.None;
            detail = string.Empty;

            if (kind == CheckpointRecordKind.Header)
            {
                code = DiagnosticCode.OwnershipConflict;
                detail = "the header record is read through Header, not as a body record (P-053).";
                return false;
            }

            if (!codecs.TryGet<TValue>(kind, out ICheckpointRecordCodec<TValue>? codec) || codec == null)
            {
                code = DiagnosticCode.MissingDependency;
                detail = "no " + typeof(TValue).Name + " codec is registered for record kind " + kind
                    + " (P-054).";
                return false;
            }

            List<byte[]> bucket = records[(int)kind];
            var decoded = new List<TValue>(bucket.Count);
            for (int i = 0; i < bucket.Count; i++)
            {
                if (!codec.TryDecode(bucket[i], out TValue value, out EnvelopeError error))
                {
                    code = CheckpointErrors.CodeFor(error);
                    detail = "the " + kind + " record at index " + i.ToString(CultureInfo.InvariantCulture)
                        + " did not decode: " + error + " (P-054).";
                    values = Array.Empty<TValue>();
                    return false;
                }

                decoded.Add(value);
            }

            values = decoded;
            return true;
        }
    }
}
