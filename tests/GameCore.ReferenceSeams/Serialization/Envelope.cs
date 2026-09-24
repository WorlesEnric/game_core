// Test-only reference seam for the shared GameCore.Contracts surface (TestOnlyMarker.cs).
// Generated length-delimited binary envelope from docs/game-core/05-contracts-and-data-model.md s6 and P-054:
// fixed magic/version header carrying the schema id and the document's required feature ids (so an unknown
// required feature rejects before any allocation), big-endian integer scalars, signed integers as two's
// complement, IEEE-754 floats with one canonical NaN bit pattern, bounded UTF-8 strings, 128-bit ids as
// high/low words, explicit null markers, (fieldId, wireType, byteLength) field records, checked list counts
// with a maximum byte length, canonical map-key ordering, and a trailing checksum. Checksums detect
// corruption, not adversarial tampering.
#nullable enable
using System;
using System.Collections.Generic;
using System.Text;

namespace GameCore.Contracts
{
    /// <summary>Wire type of one envelope field (05 s6).</summary>
    public enum WireType : byte
    {
        None = 0,
        UInt32 = 1,
        UInt64 = 2,
        Bool = 3,
        Id128 = 4,
        Bytes = 5,
        Utf8 = 6,
        Null = 7,

        /// <summary>Payload is a checked list header: element count then declared payload byte length.</summary>
        List = 8,

        /// <summary>Two's complement, big-endian.</summary>
        Int32 = 9,

        /// <summary>Two's complement, big-endian.</summary>
        Int64 = 10,

        /// <summary>IEEE-754 bits, big-endian, with NaN canonicalized on write (05 s6).</summary>
        Float32 = 11,

        /// <summary>IEEE-754 bits, big-endian, with NaN canonicalized on write (05 s6).</summary>
        Float64 = 12,
    }

    /// <summary>Decode/encode failure classification (05 s6).</summary>
    public enum EnvelopeError
    {
        None = 0,
        Truncated = 1,
        BadMagic = 2,
        UnsupportedVersion = 3,
        UnknownWireType = 4,
        FieldLengthMismatch = 5,
        StringTooLong = 6,
        FieldTooLong = 7,
        ListCountExceeded = 8,
        ListTooLarge = 9,
        DocumentTooLarge = 10,
        InvalidUtf8 = 11,
        ChecksumMismatch = 12,
        InvalidBooleanValue = 13,
        FieldIdOutOfRange = 14,
        UnknownRequiredFeature = 15,
    }

    /// <summary>Reserved envelope field ids and constants of the wire format (05 s6).</summary>
    public static class EnvelopeFormat
    {
        public const byte Magic0 = (byte)'G';
        public const byte Magic1 = (byte)'C';
        public const byte Magic2 = (byte)'E';
        public const byte Magic3 = (byte)'N';

        /// <summary>
        /// Fixed header size before the required feature id list: magic 4, major 1, minor 1, schema id 16,
        /// schema version 4, required feature count 4.
        /// </summary>
        public const int FixedHeaderSize = 30;

        /// <summary>Offset of the schema version within the fixed header.</summary>
        public const int SchemaVersionOffset = 22;

        /// <summary>Offset of the required feature count within the fixed header.</summary>
        public const int FeatureCountOffset = 26;

        /// <summary>Size in bytes of one required feature id entry.</summary>
        public const int FeatureIdSize = 16;

        /// <summary>Size of one field record header: fieldId, wireType, payloadLength.</summary>
        public const int FieldHeaderSize = 9;

        /// <summary>Field id reserved for the trailing checksum record; callers must not reuse it.</summary>
        public const int ChecksumFieldId = 0;

        /// <summary>Field ids must stay inside the positive 31-bit range so a reader can index safely.</summary>
        public const int MaxFieldId = int.MaxValue;

        /// <summary>FNV-1a 64-bit offset basis and prime; the seam's corruption checksum.</summary>
        public const ulong FnvOffsetBasis = 14695981039346656037UL;

        public const ulong FnvPrime = 1099511628211UL;
    }

    /// <summary>Bounded read/write limits for one envelope document (05 s6).</summary>
    public sealed class SerializationLimits
    {
        public SerializationLimits(int maxDocumentBytes, int maxFieldBytes, int maxStringBytes, int maxListCount, int maxListBytes)
        {
            MaxDocumentBytes = maxDocumentBytes;
            MaxFieldBytes = maxFieldBytes;
            MaxStringBytes = maxStringBytes;
            MaxListCount = maxListCount;
            MaxListBytes = maxListBytes;
        }

        public static SerializationLimits Default { get; } =
            new SerializationLimits(1024 * 1024, 256 * 1024, 64 * 1024, 65536, 512 * 1024);

        public int MaxDocumentBytes { get; }

        public int MaxFieldBytes { get; }

        public int MaxStringBytes { get; }

        public int MaxListCount { get; }

        /// <summary>Maximum declared payload bytes for one list (05 s6: lists have a maximum byte length).</summary>
        public int MaxListBytes { get; }
    }

    /// <summary>Canonical NaN handling and bit-exact float helpers (05 s6).</summary>
    public static class EnvelopeFloats
    {
        /// <summary>One canonical quiet NaN for 32-bit fields: 0x7FC00000.</summary>
        public const uint CanonicalNaN32 = 0x7FC00000U;

        /// <summary>One canonical quiet NaN for 64-bit fields: 0x7FF8000000000000.</summary>
        public const ulong CanonicalNaN64 = 0x7FF8000000000000UL;

        public static uint Bits(float value) => unchecked((uint)BitConverter.SingleToInt32Bits(value));

        public static ulong Bits(double value) => unchecked((ulong)BitConverter.DoubleToInt64Bits(value));

        public static float FromBits(uint bits) => BitConverter.Int32BitsToSingle(unchecked((int)bits));

        public static double FromBits(ulong bits) => BitConverter.Int64BitsToDouble(unchecked((long)bits));

        /// <summary>
        /// Canonicalizes every NaN payload to the single canonical quiet NaN; other values are untouched. The
        /// schema-defined treatment of NaN therefore never depends on the producing platform's payload bits.
        /// </summary>
        public static float CanonicalizeNaN(float value) => float.IsNaN(value) ? FromBits(CanonicalNaN32) : value;

        public static double CanonicalizeNaN(double value) => double.IsNaN(value) ? FromBits(CanonicalNaN64) : value;

        public static bool IsCanonicalNaN(uint bits) => bits == CanonicalNaN32;

        public static bool IsCanonicalNaN(ulong bits) => bits == CanonicalNaN64;

        /// <summary>
        /// Bit equality, so two NaNs compare equal and the unordered-comparison problem never reaches a
        /// canonical fixture (05 s6, TEST-022).
        /// </summary>
        public static bool BitsEqual(float left, float right) => Bits(left) == Bits(right);

        public static bool BitsEqual(double left, double right) => Bits(left) == Bits(right);
    }

    /// <summary>Fixed magic/version header plus the document schema and its required feature ids (05 s6, P-055).</summary>
    public sealed class EnvelopeHeader
    {
        public EnvelopeHeader(byte major, byte minor, SchemaRef schema, IReadOnlyList<Id128>? requiredFeatureIds)
        {
            Major = major;
            Minor = minor;
            Schema = schema;
            RequiredFeatureIds = ContractCollections.Freeze(requiredFeatureIds);
        }

        /// <summary>V1 protocol header: major 1, minor 0.</summary>
        public static EnvelopeHeader Current(SchemaRef schema) => new EnvelopeHeader(1, 0, schema, null);

        public byte Major { get; }

        public byte Minor { get; }

        /// <summary>Schema identity of the document; part of the header so a mismatched reader rejects early.</summary>
        public SchemaRef Schema { get; }

        /// <summary>Feature ids the reader must know; an unknown one rejects before any allocation (P-055).</summary>
        public IReadOnlyList<Id128> RequiredFeatureIds { get; }

        public int SizeInBytes => EnvelopeFormat.FixedHeaderSize + (RequiredFeatureIds.Count * EnvelopeFormat.FeatureIdSize);

        public bool IsKnownFeatures(IReadOnlyList<Id128> knownFeatureIds)
        {
            for (int i = 0; i < RequiredFeatureIds.Count; i++)
            {
                bool known = false;
                for (int j = 0; j < knownFeatureIds.Count; j++)
                {
                    if (RequiredFeatureIds[i].Equals(knownFeatureIds[j]))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    return false;
                }
            }

            return true;
        }

        public override string ToString() =>
            Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." +
            Minor.ToString(System.Globalization.CultureInfo.InvariantCulture) + "@" + Schema.ToString();
    }

    /// <summary>One decoded field header: (fieldId, wireType, byteLength) (05 s6).</summary>
    public readonly struct EnvelopeField
    {
        public readonly int FieldId;
        public readonly WireType Type;
        public readonly int ByteLength;

        public EnvelopeField(int fieldId, WireType type, int byteLength)
        {
            FieldId = fieldId;
            Type = type;
            ByteLength = byteLength;
        }

        public bool IsChecksum => FieldId == EnvelopeFormat.ChecksumFieldId && Type == WireType.UInt64;

        public override string ToString() =>
            FieldId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Type.ToString() + "(" +
            ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Append-only canonical envelope writer.</summary>
    public sealed class EnvelopeWriter
    {
        private readonly SerializationLimits limits;
        private readonly EnvelopeHeader header;
        private byte[] buffer;
        private int length;
        private bool checksumWritten;

        public EnvelopeWriter(SchemaRef schema, SerializationLimits? limits = null)
            : this(EnvelopeHeader.Current(schema), limits)
        {
        }

        public EnvelopeWriter(EnvelopeHeader header, SerializationLimits? limits = null)
        {
            this.limits = limits ?? SerializationLimits.Default;
            this.header = header ?? throw new ArgumentNullException(nameof(header));
            if (header.RequiredFeatureIds.Count > this.limits.MaxListCount)
            {
                throw new ArgumentOutOfRangeException(nameof(header), "The header declares more feature ids than the limits allow.");
            }

            buffer = new byte[Math.Max(128, header.SizeInBytes)];
            WriteHeader();
        }

        public int Length => length;

        public EnvelopeHeader Header => header;

        public void WriteUInt32Field(int fieldId, uint value)
        {
            int start = BeginField(fieldId, WireType.UInt32, 4);
            WriteUInt32BigEndian(value, start);
        }

        public void WriteUInt64Field(int fieldId, ulong value)
        {
            int start = BeginField(fieldId, WireType.UInt64, 8);
            Id128Codec.WriteUInt64BigEndian(value, buffer, start);
        }

        public void WriteInt32Field(int fieldId, int value)
        {
            int start = BeginField(fieldId, WireType.Int32, 4);
            WriteUInt32BigEndian(unchecked((uint)value), start);
        }

        public void WriteInt64Field(int fieldId, long value)
        {
            int start = BeginField(fieldId, WireType.Int64, 8);
            Id128Codec.WriteUInt64BigEndian(unchecked((ulong)value), buffer, start);
        }

        public void WriteBoolField(int fieldId, bool value)
        {
            int start = BeginField(fieldId, WireType.Bool, 1);
            buffer[start] = value ? (byte)1 : (byte)0;
        }

        /// <summary>Writes 32-bit IEEE-754 bits; a NaN is canonicalized before it reaches the wire (05 s6).</summary>
        public void WriteFloat32Field(int fieldId, float value)
        {
            int start = BeginField(fieldId, WireType.Float32, 4);
            WriteUInt32BigEndian(EnvelopeFloats.Bits(EnvelopeFloats.CanonicalizeNaN(value)), start);
        }

        /// <summary>Writes 64-bit IEEE-754 bits; a NaN is canonicalized before it reaches the wire (05 s6).</summary>
        public void WriteFloat64Field(int fieldId, double value)
        {
            int start = BeginField(fieldId, WireType.Float64, 8);
            Id128Codec.WriteUInt64BigEndian(EnvelopeFloats.Bits(EnvelopeFloats.CanonicalizeNaN(value)), buffer, start);
        }

        public void WriteId128Field(int fieldId, Id128 value)
        {
            int start = BeginField(fieldId, WireType.Id128, Id128.SizeInBytes);
            Id128Codec.WriteBigEndian(value, buffer, start);
        }

        /// <summary>Writes a bounded UTF-8 string, or an explicit null marker when <paramref name="value"/> is null.</summary>
        public void WriteUtf8Field(int fieldId, string? value)
        {
            if (value == null)
            {
                WriteNullField(fieldId);
                return;
            }

            byte[] encoded = Encoding.UTF8.GetBytes(value);
            if (encoded.Length > limits.MaxStringBytes)
            {
                throw new ArgumentException("String exceeds the declared maximum byte length.", nameof(value));
            }

            int start = BeginField(fieldId, WireType.Utf8, encoded.Length);
            Buffer.BlockCopy(encoded, 0, buffer, start, encoded.Length);
        }

        public void WriteBytesField(int fieldId, byte[]? value)
        {
            if (value == null)
            {
                WriteNullField(fieldId);
                return;
            }

            int start = BeginField(fieldId, WireType.Bytes, value.Length);
            Buffer.BlockCopy(value, 0, buffer, start, value.Length);
        }

        public void WriteNullField(int fieldId) => BeginField(fieldId, WireType.Null, 0);

        /// <summary>
        /// Writes a checked list header: element count and the declared payload byte length of the element
        /// fields that follow it. Both values are validated on write and re-validated on read against the
        /// declared limits, so a reader never allocates from an unchecked header (05 s6).
        /// </summary>
        public void WriteListHeader(int fieldId, int count, int payloadByteLength)
        {
            if (count < 0 || count > limits.MaxListCount)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "List count exceeds the declared maximum.");
            }

            if (payloadByteLength < 0 || payloadByteLength > limits.MaxListBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadByteLength), "List payload exceeds the declared maximum byte length.");
            }

            int start = BeginField(fieldId, WireType.List, 8);
            WriteUInt32BigEndian((uint)count, start);
            WriteUInt32BigEndian((uint)payloadByteLength, start + 4);
        }

        /// <summary>
        /// Appends the trailing checksum record over every preceding byte and returns the checksum. Called once;
        /// a second call is an error rather than a second, silently different trailer.
        /// </summary>
        public ulong WriteChecksum()
        {
            if (checksumWritten)
            {
                throw new InvalidOperationException("The envelope checksum has already been written.");
            }

            ulong checksum = ComputeChecksum(0, length);
            long required = (long)length + EnvelopeFormat.FieldHeaderSize + 8L;
            if (required > limits.MaxDocumentBytes)
            {
                throw new InvalidOperationException("Envelope document would exceed the declared maximum byte count.");
            }

            if (required > buffer.Length)
            {
                byte[] grown = new byte[(int)Math.Max(required, (long)buffer.Length * 2L)];
                Buffer.BlockCopy(buffer, 0, grown, 0, length);
                buffer = grown;
            }

            int start = length;
            WriteUInt32BigEndian(unchecked((uint)EnvelopeFormat.ChecksumFieldId), start);
            buffer[start + 4] = (byte)WireType.UInt64;
            WriteUInt32BigEndian(8U, start + 5);
            Id128Codec.WriteUInt64BigEndian(checksum, buffer, start + EnvelopeFormat.FieldHeaderSize);
            length = start + EnvelopeFormat.FieldHeaderSize + 8;
            checksumWritten = true;
            return checksum;
        }

        /// <summary>FNV-1a 64-bit over <paramref name="count"/> bytes starting at <paramref name="offset"/>.</summary>
        public ulong ComputeChecksum(int offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The requested range is outside the written document.");
            }

            ulong hash = EnvelopeFormat.FnvOffsetBasis;
            for (int i = 0; i < count; i++)
            {
                hash ^= buffer[offset + i];
                hash *= EnvelopeFormat.FnvPrime;
            }

            return hash;
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }

        private void WriteHeader()
        {
            buffer[0] = EnvelopeFormat.Magic0;
            buffer[1] = EnvelopeFormat.Magic1;
            buffer[2] = EnvelopeFormat.Magic2;
            buffer[3] = EnvelopeFormat.Magic3;
            buffer[4] = header.Major;
            buffer[5] = header.Minor;
            Id128Codec.WriteBigEndian(header.Schema.Id.Value, buffer, 6);
            WriteUInt32BigEndian(header.Schema.Version, buffer, EnvelopeFormat.SchemaVersionOffset);
            WriteUInt32BigEndian((uint)header.RequiredFeatureIds.Count, buffer, EnvelopeFormat.FeatureCountOffset);
            int offset = EnvelopeFormat.FixedHeaderSize;
            for (int i = 0; i < header.RequiredFeatureIds.Count; i++)
            {
                Id128Codec.WriteBigEndian(header.RequiredFeatureIds[i], buffer, offset);
                offset += EnvelopeFormat.FeatureIdSize;
            }

            length = offset;
        }

        private int BeginField(int fieldId, WireType type, int payloadLength)
        {
            if (fieldId <= EnvelopeFormat.ChecksumFieldId)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldId), "Field id 0 is reserved for the envelope checksum; ids start at 1.");
            }

            if (type == WireType.None)
            {
                throw new ArgumentOutOfRangeException(nameof(type), "The None wire type is not a writable field type.");
            }

            if (payloadLength < 0 || payloadLength > limits.MaxFieldBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), "Field payload exceeds the declared maximum.");
            }

            long required = (long)length + EnvelopeFormat.FieldHeaderSize + payloadLength;
            EnsureCapacity(required, payloadLength);
            int start = length;
            WriteUInt32BigEndian(unchecked((uint)fieldId), start);
            buffer[start + 4] = (byte)type;
            WriteUInt32BigEndian(unchecked((uint)payloadLength), start + 5);
            int payloadStart = start + EnvelopeFormat.FieldHeaderSize;
            length = payloadStart + payloadLength;
            return payloadStart;
        }

        private void WriteUInt32BigEndian(uint value, int offset)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private void EnsureCapacity(long required, int payloadLength)
        {
            if (required > limits.MaxDocumentBytes)
            {
                throw new InvalidOperationException(
                    "Envelope document would reach " + required + " bytes, above the declared maximum of " + limits.MaxDocumentBytes + ".");
            }

            if (payloadLength > limits.MaxFieldBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), "Field payload exceeds the declared maximum.");
            }

            if (required <= buffer.Length)
            {
                return;
            }

            long size = buffer.Length;
            while (size < required)
            {
                size *= 2L;
                if (size > limits.MaxDocumentBytes)
                {
                    size = limits.MaxDocumentBytes;
                    break;
                }
            }

            byte[] grown = new byte[(int)size];
            Buffer.BlockCopy(buffer, 0, grown, 0, length);
            buffer = grown;
        }
    }

    /// <summary>
    /// Forward-only canonical envelope reader. Every declared length is validated with wide arithmetic before
    /// it is trusted, unknown optional fields are skipped by their length, and unknown required features reject
    /// before any world allocation happens (05 s6, P-055).
    /// </summary>
    public sealed class EnvelopeReader
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private readonly byte[] data;
        private readonly SerializationLimits limits;
        private int position;

        public EnvelopeReader(byte[] data, SerializationLimits? limits = null)
        {
            this.data = data ?? throw new ArgumentNullException(nameof(data));
            this.limits = limits ?? SerializationLimits.Default;
            position = 0;
        }

        public EnvelopeError LastError { get; private set; }

        public int Position => position;

        /// <summary>Parses the fixed header without judging the declared required features.</summary>
        public bool TryReadHeader(out EnvelopeHeader header)
        {
            header = EnvelopeHeader.Current(default(SchemaRef));
            if (data.Length > limits.MaxDocumentBytes)
            {
                return Fail(EnvelopeError.DocumentTooLarge);
            }

            if (data.Length < EnvelopeFormat.FixedHeaderSize)
            {
                return Fail(EnvelopeError.Truncated);
            }

            if (data[0] != EnvelopeFormat.Magic0 || data[1] != EnvelopeFormat.Magic1 ||
                data[2] != EnvelopeFormat.Magic2 || data[3] != EnvelopeFormat.Magic3)
            {
                return Fail(EnvelopeError.BadMagic);
            }

            byte major = data[4];
            byte minor = data[5];
            Id128 schemaId = Id128Codec.ReadBigEndian(data, 6);
            uint schemaVersion = ReadUInt32BigEndian(EnvelopeFormat.SchemaVersionOffset);
            uint featureCount = ReadUInt32BigEndian(EnvelopeFormat.FeatureCountOffset);

            if (featureCount > (uint)limits.MaxListCount)
            {
                return Fail(EnvelopeError.ListCountExceeded);
            }

            long featureEnd = EnvelopeFormat.FixedHeaderSize + ((long)featureCount * EnvelopeFormat.FeatureIdSize);
            if (featureEnd > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            Id128[] features = new Id128[featureCount];
            for (int i = 0; i < features.Length; i++)
            {
                features[i] = Id128Codec.ReadBigEndian(data, EnvelopeFormat.FixedHeaderSize + (i * EnvelopeFormat.FeatureIdSize));
            }

            header = new EnvelopeHeader(major, minor, new SchemaRef(new SchemaId(schemaId), schemaVersion), features);
            position = (int)featureEnd;

            if (major != 1)
            {
                return Fail(EnvelopeError.UnsupportedVersion);
            }

            LastError = EnvelopeError.None;
            return true;
        }

        /// <summary>
        /// Parses the header and additionally requires every declared feature id to be known, so an unsupported
        /// document is refused before any storage is allocated (P-055).
        /// </summary>
        public bool TryReadHeader(IReadOnlyList<Id128> knownRequiredFeatureIds, out EnvelopeHeader header)
        {
            if (!TryReadHeader(out header))
            {
                return false;
            }

            if (knownRequiredFeatureIds == null)
            {
                throw new ArgumentNullException(nameof(knownRequiredFeatureIds));
            }

            return header.IsKnownFeatures(knownRequiredFeatureIds) || Fail(EnvelopeError.UnknownRequiredFeature);
        }

        public bool TryReadField(out EnvelopeField field)
        {
            field = default(EnvelopeField);
            if (position < 0 || (long)position + EnvelopeFormat.FieldHeaderSize > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            uint rawFieldId = ReadUInt32BigEndian(position);
            if (rawFieldId > EnvelopeFormat.MaxFieldId)
            {
                return Fail(EnvelopeError.FieldIdOutOfRange);
            }

            WireType type = (WireType)data[position + 4];
            uint declared = ReadUInt32BigEndian(position + 5);
            int headerEnd = position + EnvelopeFormat.FieldHeaderSize;

            if (!IsKnownWireType(type))
            {
                return Fail(EnvelopeError.UnknownWireType);
            }

            if (declared > (uint)limits.MaxFieldBytes)
            {
                return Fail(EnvelopeError.FieldTooLong);
            }

            if ((long)headerEnd + declared > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            field = new EnvelopeField((int)rawFieldId, type, (int)declared);
            position = headerEnd;
            LastError = EnvelopeError.None;
            return true;
        }

        public bool TryReadUInt32(EnvelopeField field, out uint value)
        {
            value = 0U;
            if (!CheckPayload(field, WireType.UInt32, 4))
            {
                return false;
            }

            value = ReadUInt32BigEndian(position);
            Advance(field.ByteLength);
            return true;
        }

        public bool TryReadUInt64(EnvelopeField field, out ulong value)
        {
            value = 0UL;
            if (!CheckPayload(field, WireType.UInt64, 8))
            {
                return false;
            }

            value = Id128Codec.ReadUInt64BigEndian(data, position);
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>Reads a two's complement big-endian signed 32-bit value.</summary>
        public bool TryReadInt32(EnvelopeField field, out int value)
        {
            value = 0;
            if (!CheckPayload(field, WireType.Int32, 4))
            {
                return false;
            }

            value = unchecked((int)ReadUInt32BigEndian(position));
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>Reads a two's complement big-endian signed 64-bit value.</summary>
        public bool TryReadInt64(EnvelopeField field, out long value)
        {
            value = 0L;
            if (!CheckPayload(field, WireType.Int64, 8))
            {
                return false;
            }

            value = unchecked((long)Id128Codec.ReadUInt64BigEndian(data, position));
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>Reads a bool, rejecting any byte other than 0 or 1 (05 s6).</summary>
        public bool TryReadBool(EnvelopeField field, out bool value)
        {
            value = false;
            if (!CheckPayload(field, WireType.Bool, 1))
            {
                return false;
            }

            byte raw = data[position];
            if (raw > 1)
            {
                return Fail(EnvelopeError.InvalidBooleanValue);
            }

            value = raw == 1;
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>Reads 32-bit IEEE-754 bits, which callers compare bit-exactly (05 s6).</summary>
        public bool TryReadFloat32(EnvelopeField field, out float value, out uint bits)
        {
            value = 0f;
            bits = 0U;
            if (!CheckPayload(field, WireType.Float32, 4))
            {
                return false;
            }

            bits = ReadUInt32BigEndian(position);
            value = EnvelopeFloats.FromBits(bits);
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>Reads 64-bit IEEE-754 bits, which callers compare bit-exactly (05 s6).</summary>
        public bool TryReadFloat64(EnvelopeField field, out double value, out ulong bits)
        {
            value = 0d;
            bits = 0UL;
            if (!CheckPayload(field, WireType.Float64, 8))
            {
                return false;
            }

            bits = Id128Codec.ReadUInt64BigEndian(data, position);
            value = EnvelopeFloats.FromBits(bits);
            Advance(field.ByteLength);
            return true;
        }

        public bool TryReadId128(EnvelopeField field, out Id128 value)
        {
            value = Id128.Zero;
            if (!CheckPayload(field, WireType.Id128, Id128.SizeInBytes))
            {
                return false;
            }

            value = Id128Codec.ReadBigEndian(data, position);
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>Reads a bounded UTF-8 string; a null field yields null (explicit null marker).</summary>
        public bool TryReadUtf8(EnvelopeField field, out string? value)
        {
            value = null;
            if (field.Type == WireType.Null)
            {
                if (field.ByteLength != 0)
                {
                    return Fail(EnvelopeError.FieldLengthMismatch);
                }

                LastError = EnvelopeError.None;
                return true;
            }

            if (!CheckPayload(field, WireType.Utf8, -1))
            {
                return false;
            }

            if (field.ByteLength > limits.MaxStringBytes)
            {
                return Fail(EnvelopeError.StringTooLong);
            }

            try
            {
                value = StrictUtf8.GetString(data, position, field.ByteLength);
            }
            catch (DecoderFallbackException)
            {
                return Fail(EnvelopeError.InvalidUtf8);
            }

            Advance(field.ByteLength);
            return true;
        }

        public bool TryReadBytes(EnvelopeField field, out byte[]? value)
        {
            value = null;
            if (field.Type == WireType.Null)
            {
                if (field.ByteLength != 0)
                {
                    return Fail(EnvelopeError.FieldLengthMismatch);
                }

                LastError = EnvelopeError.None;
                return true;
            }

            if (!CheckPayload(field, WireType.Bytes, -1))
            {
                return false;
            }

            value = new byte[field.ByteLength];
            Buffer.BlockCopy(data, position, value, 0, field.ByteLength);
            Advance(field.ByteLength);
            return true;
        }

        /// <summary>
        /// Reads a list header and validates both its checked count and the declared byte length of the element
        /// fields that follow, before any of them is materialized (05 s6).
        /// </summary>
        public bool TryReadListCount(EnvelopeField field, out int count, out int payloadByteLength)
        {
            count = 0;
            payloadByteLength = 0;
            if (!CheckPayload(field, WireType.List, 8))
            {
                return false;
            }

            uint declaredCount = ReadUInt32BigEndian(position);
            uint declaredBytes = ReadUInt32BigEndian(position + 4);
            Advance(field.ByteLength);

            if (declaredCount > (uint)limits.MaxListCount)
            {
                return Fail(EnvelopeError.ListCountExceeded);
            }

            if (declaredBytes > (uint)limits.MaxListBytes)
            {
                return Fail(EnvelopeError.ListTooLarge);
            }

            count = (int)declaredCount;
            payloadByteLength = (int)declaredBytes;
            return true;
        }

        /// <summary>Skips an unknown optional field by its declared length (05 s6).</summary>
        public bool Skip(EnvelopeField field)
        {
            if (position < 0 || (long)position + field.ByteLength > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            Advance(field.ByteLength);
            return true;
        }

        public bool TryReadNull(EnvelopeField field)
        {
            if (field.Type != WireType.Null || field.ByteLength != 0)
            {
                return Fail(EnvelopeError.FieldLengthMismatch);
            }

            LastError = EnvelopeError.None;
            return true;
        }

        /// <summary>
        /// Verifies the trailing checksum record against the bytes before it. A mismatch means corruption, not
        /// tampering detection (05 s6).
        /// </summary>
        public bool TryVerifyChecksum(EnvelopeField checksumField, out ulong declaredChecksum)
        {
            declaredChecksum = 0UL;
            if (!checksumField.IsChecksum)
            {
                return Fail(EnvelopeError.FieldLengthMismatch);
            }

            if (position < 0 || (long)position + 8 > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            int checksumStart = position - EnvelopeFormat.FieldHeaderSize;
            declaredChecksum = Id128Codec.ReadUInt64BigEndian(data, position);
            ulong computed = ComputeChecksum(0, checksumStart);
            Advance(8);
            if (declaredChecksum != computed)
            {
                return Fail(EnvelopeError.ChecksumMismatch);
            }

            LastError = EnvelopeError.None;
            return true;
        }

        public ulong ComputeChecksum(int offset, int count)
        {
            if (offset < 0 || count < 0 || (long)offset + count > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The requested range is outside the document.");
            }

            ulong hash = EnvelopeFormat.FnvOffsetBasis;
            for (int i = 0; i < count; i++)
            {
                hash ^= data[offset + i];
                hash *= EnvelopeFormat.FnvPrime;
            }

            return hash;
        }

        private static bool IsKnownWireType(WireType type)
        {
            switch (type)
            {
                case WireType.UInt32:
                case WireType.UInt64:
                case WireType.Int32:
                case WireType.Int64:
                case WireType.Bool:
                case WireType.Id128:
                case WireType.Bytes:
                case WireType.Utf8:
                case WireType.Null:
                case WireType.List:
                    return true;
                default:
                    return false;
            }
        }

        private bool CheckPayload(EnvelopeField field, WireType expected, int expectedLength)
        {
            if (field.Type != expected)
            {
                return Fail(EnvelopeError.FieldLengthMismatch);
            }

            if (expectedLength >= 0 && field.ByteLength != expectedLength)
            {
                return Fail(EnvelopeError.FieldLengthMismatch);
            }

            if (position < 0 || (long)position + field.ByteLength > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            LastError = EnvelopeError.None;
            return true;
        }

        private void Advance(int count) => position += count;

        private uint ReadUInt32BigEndian(int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];

        private bool Fail(EnvelopeError error)
        {
            LastError = error;
            return false;
        }
    }
}
