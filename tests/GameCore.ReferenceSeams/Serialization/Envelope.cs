// Test-only reference seam for the shared GameCore.Contracts surface (TestOnlyMarker.cs).
// Generated length-delimited binary envelope from docs/game-core/05-contracts-and-data-model.md s6 and
// P-054: fixed magic/version header, big-endian integer scalars, bounded UTF-8 strings, 128-bit ids as
// high/low words, explicit null markers and (fieldId, wireType, byteLength, payload) fields.
#nullable enable
using System;
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
        List = 8,
    }

    /// <summary>Decode/encode failure classification; checksums detect corruption, not tampering (05 s6).</summary>
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
        DocumentTooLarge = 9,
        InvalidUtf8 = 10,
        NotAField = 11,
    }

    /// <summary>Bounded read/write limits for one envelope document (05 s6).</summary>
    public sealed class SerializationLimits
    {
        public SerializationLimits(int maxDocumentBytes, int maxFieldBytes, int maxStringBytes, int maxListCount)
        {
            MaxDocumentBytes = maxDocumentBytes;
            MaxFieldBytes = maxFieldBytes;
            MaxStringBytes = maxStringBytes;
            MaxListCount = maxListCount;
        }

        public static SerializationLimits Default { get; } = new SerializationLimits(1024 * 1024, 256 * 1024, 64 * 1024, 65536);

        public int MaxDocumentBytes { get; }

        public int MaxFieldBytes { get; }

        public int MaxStringBytes { get; }

        public int MaxListCount { get; }
    }

    /// <summary>Fixed magic/version header. A differing major version is incompatible (P-055).</summary>
    public readonly struct EnvelopeHeader : IEquatable<EnvelopeHeader>
    {
        public const int SizeInBytes = 6;

        private const byte Magic0 = (byte)'G';
        private const byte Magic1 = (byte)'C';
        private const byte Magic2 = (byte)'E';
        private const byte Magic3 = (byte)'N';

        public static readonly EnvelopeHeader Current = new EnvelopeHeader(1, 0);

        public readonly byte Major;
        public readonly byte Minor;

        public EnvelopeHeader(byte major, byte minor)
        {
            Major = major;
            Minor = minor;
        }

        public void Write(byte[] destination, int offset)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (offset < 0 || offset + SizeInBytes > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Destination must hold 6 bytes from offset.");
            }

            destination[offset] = Magic0;
            destination[offset + 1] = Magic1;
            destination[offset + 2] = Magic2;
            destination[offset + 3] = Magic3;
            destination[offset + 4] = Major;
            destination[offset + 5] = Minor;
        }

        public bool Equals(EnvelopeHeader other) => Major == other.Major && Minor == other.Minor;

        public override bool Equals(object? obj) => obj is EnvelopeHeader other && Equals(other);

        public override int GetHashCode() => (Major << 8) | Minor;

        public override string ToString() => Major.ToString(System.Globalization.CultureInfo.InvariantCulture) + "." + Minor.ToString(System.Globalization.CultureInfo.InvariantCulture);
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

        public override string ToString() => FieldId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + Type.ToString() + "(" + ByteLength.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>Append-only canonical envelope writer.</summary>
    public sealed class EnvelopeWriter
    {
        private readonly SerializationLimits limits;
        private readonly EnvelopeHeader header;
        private byte[] buffer;
        private int length;

        public EnvelopeWriter(SerializationLimits? limits = null)
            : this(EnvelopeHeader.Current, limits)
        {
        }

        public EnvelopeWriter(EnvelopeHeader header, SerializationLimits? limits = null)
        {
            this.limits = limits ?? SerializationLimits.Default;
            this.header = header;
            buffer = new byte[64];
            header.Write(buffer, 0);
            length = EnvelopeHeader.SizeInBytes;
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

        /// <summary>Writes a checked list header; the caller then writes the declared number of element fields.</summary>
        public void WriteListHeader(int fieldId, int count)
        {
            if (count < 0 || count > limits.MaxListCount)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "List count exceeds the declared maximum.");
            }

            int start = BeginField(fieldId, WireType.List, 4);
            WriteUInt32BigEndian((uint)count, start);
        }

        public byte[] ToArray()
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(buffer, 0, result, 0, length);
            return result;
        }

        private int BeginField(int fieldId, WireType type, int payloadLength)
        {
            if (fieldId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(fieldId));
            }

            if (payloadLength > limits.MaxFieldBytes)
            {
                throw new ArgumentOutOfRangeException(nameof(payloadLength), "Field payload exceeds the declared maximum.");
            }

            int headerLength = 9;
            EnsureCapacity(length + headerLength + payloadLength);
            int start = length;
            WriteUInt32BigEndian((uint)fieldId, start);
            buffer[start + 4] = (byte)type;
            WriteUInt32BigEndian((uint)payloadLength, start + 5);
            length = start + headerLength;
            int payloadStart = length;
            length += payloadLength;
            return payloadStart;
        }

        private void WriteUInt32BigEndian(uint value, int offset)
        {
            buffer[offset] = (byte)(value >> 24);
            buffer[offset + 1] = (byte)(value >> 16);
            buffer[offset + 2] = (byte)(value >> 8);
            buffer[offset + 3] = (byte)value;
        }

        private void EnsureCapacity(int required)
        {
            if (required > limits.MaxDocumentBytes)
            {
                throw new InvalidOperationException("Envelope document exceeds the declared maximum byte length.");
            }

            if (required <= buffer.Length)
            {
                return;
            }

            int size = buffer.Length;
            while (size < required)
            {
                size *= 2;
            }

            byte[] grown = new byte[size];
            Buffer.BlockCopy(buffer, 0, grown, 0, length);
            buffer = grown;
        }
    }

    /// <summary>
    /// Forward-only canonical envelope reader. Unknown optional fields are skipped by their declared
    /// byte length; every length is checked before it is trusted (05 s6).
    /// </summary>
    public sealed class EnvelopeReader
    {
        private const int FieldHeaderLength = 9;

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

        public bool TryReadHeader(out EnvelopeHeader header)
        {
            header = EnvelopeHeader.Current;
            if (data.Length < EnvelopeHeader.SizeInBytes)
            {
                return Fail(EnvelopeError.Truncated);
            }

            if (data[0] != (byte)'G' || data[1] != (byte)'C' || data[2] != (byte)'E' || data[3] != (byte)'N')
            {
                return Fail(EnvelopeError.BadMagic);
            }

            header = new EnvelopeHeader(data[4], data[5]);
            position = EnvelopeHeader.SizeInBytes;
            if (header.Major != EnvelopeHeader.Current.Major)
            {
                return Fail(EnvelopeError.UnsupportedVersion);
            }

            if (data.Length > limits.MaxDocumentBytes)
            {
                return Fail(EnvelopeError.DocumentTooLarge);
            }

            LastError = EnvelopeError.None;
            return true;
        }

        public bool TryReadField(out EnvelopeField field)
        {
            field = default(EnvelopeField);
            if (position + FieldHeaderLength > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            int fieldId = (int)ReadUInt32BigEndian(position);
            WireType type = (WireType)data[position + 4];
            uint declared = ReadUInt32BigEndian(position + 5);
            position += FieldHeaderLength;

            if (!IsKnownWireType(type))
            {
                return Fail(EnvelopeError.UnknownWireType);
            }

            if (declared > (uint)limits.MaxFieldBytes)
            {
                return Fail(EnvelopeError.FieldTooLong);
            }

            if (position + (int)declared > data.Length)
            {
                return Fail(EnvelopeError.Truncated);
            }

            field = new EnvelopeField(fieldId, type, (int)declared);
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

        public bool TryReadBool(EnvelopeField field, out bool value)
        {
            value = false;
            if (!CheckPayload(field, WireType.Bool, 1))
            {
                return false;
            }

            value = data[position] != 0;
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

        /// <summary>Reads a list header and validates its checked count.</summary>
        public bool TryReadListCount(EnvelopeField field, out int count)
        {
            count = 0;
            if (!CheckPayload(field, WireType.List, 4))
            {
                return false;
            }

            uint declared = ReadUInt32BigEndian(position);
            Advance(field.ByteLength);
            if (declared > (uint)limits.MaxListCount)
            {
                return Fail(EnvelopeError.ListCountExceeded);
            }

            count = (int)declared;
            return true;
        }

        /// <summary>Skips an unknown optional field by its declared length (05 s6).</summary>
        public bool Skip(EnvelopeField field)
        {
            if (position + field.ByteLength > data.Length)
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

        private static bool IsKnownWireType(WireType type)
        {
            switch (type)
            {
                case WireType.UInt32:
                case WireType.UInt64:
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

            if (position + field.ByteLength > data.Length)
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
