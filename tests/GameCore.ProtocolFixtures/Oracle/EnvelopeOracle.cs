// Independent oracle probe for the seam's serialization envelope (GC-002). Unlike the identity, counter and
// version oracles, these probes deliberately drive the seam's own envelope codec: the envelope is a codec whose
// declared read/write rules are what the fixtures must pin down (05 s6). Byte-level documents that the writer
// would refuse are assembled here by hand so the reader's limit checks are exercisable.
#nullable enable
using System;
using System.Text;
using GameCore.Contracts;

namespace GameCore.ProtocolFixtures.Oracle
{
    /// <summary>Outcome of one envelope probe: the observed rejection code, or accepted.</summary>
    public readonly struct EnvelopeProbe
    {
        public EnvelopeProbe(bool accepted, string code, string detail)
        {
            Accepted = accepted;
            Code = code ?? string.Empty;
            Detail = detail ?? string.Empty;
        }

        public static EnvelopeProbe Accept(string detail) => new EnvelopeProbe(true, string.Empty, detail);

        public static EnvelopeProbe Reject(string code, string detail) => new EnvelopeProbe(false, code, detail);
    }

    /// <summary>Hand-assembled byte-level envelope documents, used to exercise reader-side limit checks.</summary>
    public static class RawEnvelopeBuilder
    {
        /// <summary>A canonical document header with no required features.</summary>
        public static byte[] Header(SchemaRef schema)
        {
            byte[] buffer = new byte[EnvelopeFormat.FixedHeaderSize];
            buffer[0] = EnvelopeFormat.Magic0;
            buffer[1] = EnvelopeFormat.Magic1;
            buffer[2] = EnvelopeFormat.Magic2;
            buffer[3] = EnvelopeFormat.Magic3;
            buffer[4] = 1;
            buffer[5] = 0;
            Id128Codec.WriteBigEndian(schema.Id.Value, buffer, 6);
            WriteUInt32(schema.Version, buffer, EnvelopeFormat.SchemaVersionOffset);
            WriteUInt32(0U, buffer, EnvelopeFormat.FeatureCountOffset);
            return buffer;
        }

        /// <summary>One field record with an arbitrary declared field id, wire type and raw payload.</summary>
        public static byte[] Field(uint fieldId, WireType type, byte[] payload, int? declaredLengthOverride = null)
        {
            byte[] payloadBytes = payload ?? Array.Empty<byte>();
            int declared = declaredLengthOverride ?? payloadBytes.Length;
            byte[] record = new byte[EnvelopeFormat.FieldHeaderSize + payloadBytes.Length];
            WriteUInt32(fieldId, record, 0);
            record[4] = (byte)type;
            WriteUInt32(unchecked((uint)declared), record, 5);
            Buffer.BlockCopy(payloadBytes, 0, record, EnvelopeFormat.FieldHeaderSize, payloadBytes.Length);
            return record;
        }

        public static byte[] Concat(params byte[][] parts)
        {
            int total = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                total += parts[i].Length;
            }

            byte[] result = new byte[total];
            int offset = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                Buffer.BlockCopy(parts[i], 0, result, offset, parts[i].Length);
                offset += parts[i].Length;
            }

            return result;
        }

        public static byte[] UInt32Payload(uint value)
        {
            byte[] payload = new byte[4];
            WriteUInt32(value, payload, 0);
            return payload;
        }

        public static byte[] ListPayload(uint count, uint payloadByteLength)
        {
            byte[] payload = new byte[8];
            WriteUInt32(count, payload, 0);
            WriteUInt32(payloadByteLength, payload, 4);
            return payload;
        }

        public static byte[] BytesPayload(int length, byte fill)
        {
            byte[] payload = new byte[length];
            for (int i = 0; i < length; i++)
            {
                payload[i] = fill;
            }

            return payload;
        }

        public static byte[] Utf8Payload(string text) => Encoding.UTF8.GetBytes(text);

        public static void WriteUInt32(uint value, byte[] destination, int offset)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }
    }

    /// <summary>Reader-side probes over a raw document: the first field record is decoded and judged.</summary>
    public static class EnvelopeOracle
    {
        /// <summary>Reads the first field of one raw document and reports what the reader decided.</summary>
        public static EnvelopeProbe ReadFirstField(byte[] document, SchemaRef schema)
        {
            EnvelopeReader reader = new EnvelopeReader(document);
            if (!reader.TryReadHeader(out EnvelopeHeader header))
            {
                return EnvelopeProbe.Reject(reader.LastError.ToString(), "header rejected");
            }

            if (!header.Schema.Id.Equals(schema.Id) || header.Schema.Version != schema.Version)
            {
                return EnvelopeProbe.Reject("HeaderSchemaMismatch", "header schema " + header.Schema + " differs from the expected " + schema);
            }

            if (!reader.TryReadField(out EnvelopeField field))
            {
                return EnvelopeProbe.Reject(reader.LastError.ToString(), "field header rejected");
            }

            string code;
            switch (field.Type)
            {
                case WireType.Bool:
                    reader.TryReadBool(field, out bool _);
                    code = reader.LastError.ToString();
                    break;
                case WireType.List:
                    reader.TryReadListCount(field, out int _, out int _);
                    code = reader.LastError.ToString();
                    break;
                case WireType.Utf8:
                    reader.TryReadUtf8(field, out string? _);
                    code = reader.LastError.ToString();
                    break;
                case WireType.Bytes:
                    reader.TryReadBytes(field, out byte[]? _);
                    code = reader.LastError.ToString();
                    break;
                case WireType.UInt32:
                    reader.TryReadUInt32(field, out uint _);
                    code = reader.LastError.ToString();
                    break;
                case WireType.Null:
                    reader.TryReadNull(field);
                    code = reader.LastError.ToString();
                    break;
                default:
                    reader.Skip(field);
                    code = reader.LastError.ToString();
                    break;
            }

            return code == nameof(EnvelopeError.None)
                ? EnvelopeProbe.Accept("field " + field + " accepted")
                : EnvelopeProbe.Reject(code, "field " + field + " rejected");
        }

        /// <summary>
        /// Writes a document with the seam writer, verifies its checksum, then optionally corrupts one covered
        /// byte before verification. This exercises the checksum contract end to end (05 s6).
        /// </summary>
        public static EnvelopeProbe ChecksumRoundTrip(SchemaRef schema, bool tamper)
        {
            EnvelopeWriter writer = new EnvelopeWriter(schema);
            writer.WriteUInt32Field(1, 0x01020304U);
            writer.WriteUtf8Field(2, "checksum");
            ulong written = writer.WriteChecksum();
            byte[] document = writer.ToArray();

            if (tamper)
            {
                // Flip a byte inside the first field payload, which the checksum covers.
                int payloadOffset = EnvelopeFormat.FixedHeaderSize + EnvelopeFormat.FieldHeaderSize;
                document[payloadOffset] ^= 0xFF;
            }

            EnvelopeReader reader = new EnvelopeReader(document);
            if (!reader.TryReadHeader(out EnvelopeHeader _))
            {
                return EnvelopeProbe.Reject(reader.LastError.ToString(), "header rejected");
            }

            EnvelopeField first = default(EnvelopeField);
            bool sawChecksum = false;
            ulong declaredChecksum = 0UL;
            while (reader.TryReadField(out EnvelopeField field))
            {
                if (field.IsChecksum)
                {
                    sawChecksum = true;
                    if (!reader.TryVerifyChecksum(field, out declaredChecksum))
                    {
                        return EnvelopeProbe.Reject(reader.LastError.ToString(), "checksum rejected");
                    }

                    break;
                }

                if (first.Type == WireType.None)
                {
                    first = field;
                }

                reader.Skip(field);
            }

            if (!sawChecksum)
            {
                return EnvelopeProbe.Reject("ChecksumMissing", "no trailing checksum record was found");
            }

            if (tamper)
            {
                return EnvelopeProbe.Reject("ChecksumAcceptedTamperedDocument", "a corrupted document verified");
            }

            if (declaredChecksum != written)
            {
                return EnvelopeProbe.Reject("ChecksumMismatch", "declared checksum differs from the value the writer returned");
            }

            return EnvelopeProbe.Accept("checksum " + declaredChecksum.ToString("x16", System.Globalization.CultureInfo.InvariantCulture) + " verified");
        }

        /// <summary>Round-trips every writable field type and reports bit-exact mismatches (05 s6).</summary>
        public static EnvelopeProbe FieldTypeRoundTrip(SchemaRef schema)
        {
            EnvelopeWriter writer = new EnvelopeWriter(schema);
            writer.WriteUInt32Field(1, uint.MaxValue);
            writer.WriteUInt64Field(2, ulong.MaxValue);
            writer.WriteInt32Field(3, int.MinValue);
            writer.WriteInt64Field(4, long.MaxValue);
            writer.WriteBoolField(5, true);
            writer.WriteFloat32Field(6, -0.5f);
            writer.WriteFloat64Field(7, 1.0d / 3.0d);
            writer.WriteId128Field(8, new Id128(ulong.MaxValue, 1UL));
            writer.WriteUtf8Field(9, "game core");
            writer.WriteBytesField(10, new byte[] { 1, 2, 3 });
            writer.WriteNullField(11);

            // A consistent list: two 16-byte element fields follow the header and match its declared byte length.
            writer.WriteListHeader(12, 2, 32);
            writer.WriteId128Field(13, new Id128(7UL, 8UL));
            writer.WriteId128Field(14, new Id128(9UL, 10UL));
            writer.WriteChecksum();
            byte[] document = writer.ToArray();

            EnvelopeReader reader = new EnvelopeReader(document);
            if (!reader.TryReadHeader(out EnvelopeHeader _))
            {
                return EnvelopeProbe.Reject(reader.LastError.ToString(), "header rejected");
            }

            int seen = 0;
            while (reader.TryReadField(out EnvelopeField field))
            {
                if (field.IsChecksum)
                {
                    if (!reader.TryVerifyChecksum(field, out ulong _))
                    {
                        return EnvelopeProbe.Reject(reader.LastError.ToString(), "checksum rejected");
                    }

                    break;
                }

                seen++;
                switch (field.FieldId)
                {
                    case 1:
                        if (!reader.TryReadUInt32(field, out uint u32) || u32 != uint.MaxValue)
                        {
                            return Mismatch(reader, field, "uint32");
                        }

                        break;
                    case 2:
                        if (!reader.TryReadUInt64(field, out ulong u64) || u64 != ulong.MaxValue)
                        {
                            return Mismatch(reader, field, "uint64");
                        }

                        break;
                    case 3:
                        if (!reader.TryReadInt32(field, out int i32) || i32 != int.MinValue)
                        {
                            return Mismatch(reader, field, "int32");
                        }

                        break;
                    case 4:
                        if (!reader.TryReadInt64(field, out long i64) || i64 != long.MaxValue)
                        {
                            return Mismatch(reader, field, "int64");
                        }

                        break;
                    case 5:
                        if (!reader.TryReadBool(field, out bool flag) || !flag)
                        {
                            return Mismatch(reader, field, "bool");
                        }

                        break;
                    case 6:
                        if (!reader.TryReadFloat32(field, out float f32, out uint bits32) ||
                            !EnvelopeFloats.BitsEqual(f32, -0.5f) || bits32 != EnvelopeFloats.Bits(-0.5f))
                        {
                            return Mismatch(reader, field, "float32");
                        }

                        break;
                    case 7:
                        if (!reader.TryReadFloat64(field, out double f64, out ulong bits64) ||
                            bits64 != EnvelopeFloats.Bits(1.0d / 3.0d))
                        {
                            return Mismatch(reader, field, "float64");
                        }

                        break;
                    case 8:
                        if (!reader.TryReadId128(field, out Id128 id) || !id.Equals(new Id128(ulong.MaxValue, 1UL)))
                        {
                            return Mismatch(reader, field, "id128");
                        }

                        break;
                    case 9:
                        if (!reader.TryReadUtf8(field, out string? text) || text != "game core")
                        {
                            return Mismatch(reader, field, "utf8");
                        }

                        break;
                    case 10:
                        if (!reader.TryReadBytes(field, out byte[]? bytes) || bytes == null || bytes.Length != 3 || bytes[2] != 3)
                        {
                            return Mismatch(reader, field, "bytes");
                        }

                        break;
                    case 11:
                        if (!reader.TryReadNull(field))
                        {
                            return Mismatch(reader, field, "null");
                        }

                        break;
                    case 12:
                        if (!reader.TryReadListCount(field, out int count, out int payloadBytes) || count != 2 || payloadBytes != 32)
                        {
                            return Mismatch(reader, field, "list header");
                        }

                        break;
                    case 13:
                    case 14:
                        if (!reader.TryReadId128(field, out Id128 element) || element.Equals(Id128.Zero))
                        {
                            return Mismatch(reader, field, "list element");
                        }

                        break;
                    default:
                        reader.Skip(field);
                        break;
                }
            }

            return seen == 14
                ? EnvelopeProbe.Accept("12 field types plus 2 list elements round-tripped bit-exactly")
                : EnvelopeProbe.Reject("FieldCountMismatch", "decoded " + seen + " of 14 fields");
        }

        /// <summary>Writes two different NaN payloads and reports whether both became the canonical NaN.</summary>
        public static EnvelopeProbe CanonicalNaN(SchemaRef schema)
        {
            const uint SignalingNaN32 = 0x7F800001U;
            const ulong PitchedNaN64 = 0x7FF8000000000123UL;

            EnvelopeWriter writer = new EnvelopeWriter(schema);
            writer.WriteFloat32Field(1, EnvelopeFloats.FromBits(SignalingNaN32));
            writer.WriteFloat64Field(2, EnvelopeFloats.FromBits(PitchedNaN64));
            writer.WriteChecksum();
            byte[] document = writer.ToArray();

            EnvelopeReader reader = new EnvelopeReader(document);
            if (!reader.TryReadHeader(out EnvelopeHeader _))
            {
                return EnvelopeProbe.Reject(reader.LastError.ToString(), "header rejected");
            }

            uint float32Bits = 0U;
            ulong float64Bits = 0UL;
            while (reader.TryReadField(out EnvelopeField field))
            {
                if (field.IsChecksum)
                {
                    if (!reader.TryVerifyChecksum(field, out ulong _))
                    {
                        return EnvelopeProbe.Reject(reader.LastError.ToString(), "checksum rejected");
                    }

                    break;
                }

                if (field.FieldId == 1)
                {
                    reader.TryReadFloat32(field, out float _, out float32Bits);
                }
                else if (field.FieldId == 2)
                {
                    reader.TryReadFloat64(field, out double _, out float64Bits);
                }
                else
                {
                    reader.Skip(field);
                }
            }

            if (!EnvelopeFloats.IsCanonicalNaN(float32Bits))
            {
                return EnvelopeProbe.Reject("NaNNotCanonical", "float32 NaN bits 0x" + float32Bits.ToString("x8", System.Globalization.CultureInfo.InvariantCulture));
            }

            if (!EnvelopeFloats.IsCanonicalNaN(float64Bits))
            {
                return EnvelopeProbe.Reject("NaNNotCanonical", "float64 NaN bits 0x" + float64Bits.ToString("x16", System.Globalization.CultureInfo.InvariantCulture));
            }

            return EnvelopeProbe.Accept("both NaN payloads became the canonical quiet NaN");
        }

        /// <summary>Requires every declared feature id to be known before the body is used (P-055).</summary>
        public static EnvelopeProbe UnknownRequiredFeature(SchemaRef schema, bool readerKnowsFeature)
        {
            Id128 feature = new Id128(0x6665617475726573UL, 1UL);
            EnvelopeHeader header = new EnvelopeHeader(1, 0, schema, new[] { feature });
            EnvelopeWriter writer = new EnvelopeWriter(header);
            writer.WriteUInt32Field(1, 7U);
            writer.WriteChecksum();

            EnvelopeReader reader = new EnvelopeReader(writer.ToArray());
            Id128[] known = readerKnowsFeature ? new[] { feature } : Array.Empty<Id128>();
            if (!reader.TryReadHeader(known, out EnvelopeHeader parsed))
            {
                return EnvelopeProbe.Reject(reader.LastError.ToString(), "declared features were refused");
            }

            return parsed.RequiredFeatureIds.Count == 1
                ? EnvelopeProbe.Accept("one required feature accepted")
                : EnvelopeProbe.Reject("FeatureCountMismatch", "expected one required feature id");
        }

        /// <summary>Reports the writer's refusal of a reserved or out-of-range field id.</summary>
        public static EnvelopeProbe WriterFieldId(SchemaRef schema, int fieldId)
        {
            try
            {
                EnvelopeWriter writer = new EnvelopeWriter(schema);
                writer.WriteUInt32Field(fieldId, 1U);
                return EnvelopeProbe.Accept("field id " + fieldId + " accepted on write");
            }
            catch (ArgumentOutOfRangeException)
            {
                return EnvelopeProbe.Reject("FieldIdOutOfRange", "field id " + fieldId + " refused on write");
            }
        }

        /// <summary>Reports the writer's refusal of over-limit list headers.</summary>
        public static EnvelopeProbe WriterListHeader(SchemaRef schema, int count, int payloadByteLength)
        {
            try
            {
                EnvelopeWriter writer = new EnvelopeWriter(schema);
                writer.WriteListHeader(1, count, payloadByteLength);
                return EnvelopeProbe.Accept("list header (" + count + ", " + payloadByteLength + ") accepted on write");
            }
            catch (ArgumentOutOfRangeException)
            {
                return EnvelopeProbe.Reject("ListBoundsRefused", "list header refused on write");
            }
        }

        private static EnvelopeProbe Mismatch(EnvelopeReader reader, EnvelopeField field, string what) =>
            reader.LastError == EnvelopeError.None
                ? EnvelopeProbe.Reject("ValueMismatch", what + " did not round-trip for field " + field)
                : EnvelopeProbe.Reject(reader.LastError.ToString(), what + " rejected for field " + field);
    }
}
