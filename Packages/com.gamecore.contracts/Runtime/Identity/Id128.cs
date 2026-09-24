// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable
using System;
using System.Collections.Generic;

namespace GameCore.Contracts
{
    /// <summary>
    /// 128-bit stable identity word pair. Canonical comparison and canonical bytes are big-endian
    /// high word then low word; platform Guid memory order is never canonical (P-004).
    /// </summary>
    public readonly struct Id128 : IEquatable<Id128>, IComparable<Id128>
    {
        public const int SizeInBytes = 16;

        public static readonly Id128 Zero = new Id128(0UL, 0UL);

        public readonly ulong High;
        public readonly ulong Low;

        public Id128(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        /// <summary>Default zero IDs are invalid catalog identities (05 s2).</summary>
        public bool IsDefault => High == 0UL && Low == 0UL;

        public bool Equals(Id128 other) => High == other.High && Low == other.Low;

        public override bool Equals(object? obj) => obj is Id128 other && Equals(other);

        public override int GetHashCode() => unchecked((int)(High ^ (High >> 32) ^ Low ^ (Low >> 32)));

        /// <summary>Numeric high-then-low order; identical to canonical big-endian byte order (P-008).</summary>
        public int CompareTo(Id128 other)
        {
            int high = High.CompareTo(other.High);
            return high != 0 ? high : Low.CompareTo(other.Low);
        }

        public static bool operator ==(Id128 left, Id128 right) => left.Equals(right);

        public static bool operator !=(Id128 left, Id128 right) => !left.Equals(right);

        public static bool operator <(Id128 left, Id128 right) => left.CompareTo(right) < 0;

        public static bool operator >(Id128 left, Id128 right) => left.CompareTo(right) > 0;

        public static bool operator <=(Id128 left, Id128 right) => left.CompareTo(right) <= 0;

        public static bool operator >=(Id128 left, Id128 right) => left.CompareTo(right) >= 0;

        /// <summary>Canonical 32-character lowercase hex of the big-endian bytes, for diagnostics and fixtures.</summary>
        public override string ToString() => Id128Codec.ToHex(this);
    }

    /// <summary>Canonical big-endian read/write/parse for <see cref="Id128"/> (P-004, P-054).</summary>
    public static class Id128Codec
    {
        /// <summary>Writes high word then low word, each big-endian, into <paramref name="destination"/>.</summary>
        public static void WriteBigEndian(Id128 value, byte[] destination, int offset)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (offset < 0 || offset + Id128.SizeInBytes > destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Destination must hold 16 bytes from offset.");
            }

            WriteUInt64BigEndian(value.High, destination, offset);
            WriteUInt64BigEndian(value.Low, destination, offset + 8);
        }

        public static Id128 ReadBigEndian(byte[] source, int offset)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (offset < 0 || offset + Id128.SizeInBytes > source.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "Source must hold 16 bytes from offset.");
            }

            ulong high = ReadUInt64BigEndian(source, offset);
            ulong low = ReadUInt64BigEndian(source, offset + 8);
            return new Id128(high, low);
        }

        public static byte[] ToBigEndianBytes(Id128 value)
        {
            byte[] bytes = new byte[Id128.SizeInBytes];
            WriteBigEndian(value, bytes, 0);
            return bytes;
        }

        /// <summary>Canonical 32-character lowercase hex; the inverse of <see cref="TryParseHex"/>.</summary>
        public static string ToHex(Id128 value)
        {
            byte[] bytes = ToBigEndianBytes(value);
            return CanonicalHex.ToHex(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Parses the one canonical form: exactly 32 lowercase hex characters, no whitespace, no sign, no
        /// prefixes. Uppercase or padded input is rejected rather than normalized (P-004, P-054).
        /// </summary>
        public static bool TryParseHex(string? text, out Id128 value)
        {
            value = Id128.Zero;
            if (text == null || text.Length != Id128.SizeInBytes * 2)
            {
                return false;
            }

            if (!CanonicalHex.TryParseUInt64(text, 0, out ulong high))
            {
                return false;
            }

            if (!CanonicalHex.TryParseUInt64(text, 16, out ulong low))
            {
                return false;
            }

            value = new Id128(high, low);
            return true;
        }

        public static int CompareBigEndian(Id128 left, Id128 right) => left.CompareTo(right);

        public static IComparer<Id128> CanonicalComparer => CanonicalId128Comparer.Instance;

        internal static void WriteUInt64BigEndian(ulong value, byte[] destination, int offset)
        {
            destination[offset] = (byte)(value >> 56);
            destination[offset + 1] = (byte)(value >> 48);
            destination[offset + 2] = (byte)(value >> 40);
            destination[offset + 3] = (byte)(value >> 32);
            destination[offset + 4] = (byte)(value >> 24);
            destination[offset + 5] = (byte)(value >> 16);
            destination[offset + 6] = (byte)(value >> 8);
            destination[offset + 7] = (byte)value;
        }

        internal static ulong ReadUInt64BigEndian(byte[] source, int offset) =>
            ((ulong)source[offset] << 56) |
            ((ulong)source[offset + 1] << 48) |
            ((ulong)source[offset + 2] << 40) |
            ((ulong)source[offset + 3] << 32) |
            ((ulong)source[offset + 4] << 24) |
            ((ulong)source[offset + 5] << 16) |
            ((ulong)source[offset + 6] << 8) |
            source[offset + 7];

        private sealed class CanonicalId128Comparer : IComparer<Id128>
        {
            internal static readonly CanonicalId128Comparer Instance = new CanonicalId128Comparer();

            public int Compare(Id128 x, Id128 y) => x.CompareTo(y);
        }
    }
}
