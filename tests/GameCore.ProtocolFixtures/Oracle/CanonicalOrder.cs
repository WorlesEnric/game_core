// Independent pure oracle (GC-002). Canonical stable-ID byte order and ordering.
// This file deliberately re-implements the byte layout and comparison instead of calling the seam's
// Id128Codec: an oracle that delegates to the code under test cannot disagree with it (P-004, P-008).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;

namespace GameCore.ProtocolFixtures.Oracle
{
    /// <summary>Canonical big-endian byte order, hex form and order-independent sorting for stable ids.</summary>
    public static class CanonicalOrder
    {
        public const int IdSizeInBytes = 16;

        /// <summary>Canonical bytes: high word then low word, each big-endian (never Guid memory order).</summary>
        public static byte[] Bytes(Id128 value)
        {
            byte[] bytes = new byte[IdSizeInBytes];
            WriteWord(value.High, bytes, 0);
            WriteWord(value.Low, bytes, 8);
            return bytes;
        }

        /// <summary>Canonical 32-character lowercase hex of <see cref="Bytes"/>.</summary>
        public static string Hex(Id128 value)
        {
            byte[] bytes = Bytes(value);
            char[] text = new char[IdSizeInBytes * 2];
            const string Digits = "0123456789abcdef";
            for (int i = 0; i < bytes.Length; i++)
            {
                text[i * 2] = Digits[bytes[i] >> 4];
                text[(i * 2) + 1] = Digits[bytes[i] & 0x0F];
            }

            return new string(text);
        }

        public static bool TryParseHex(string? text, out Id128 value)
        {
            value = Id128.Zero;
            if (text == null || text.Length != IdSizeInBytes * 2)
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char digit = text[i];
                if (!((digit >= '0' && digit <= '9') || (digit >= 'a' && digit <= 'f')))
                {
                    return false;
                }
            }

            if (!ulong.TryParse(text.AsSpan(0, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong high))
            {
                return false;
            }

            if (!ulong.TryParse(text.AsSpan(16, 16), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out ulong low))
            {
                return false;
            }

            value = new Id128(high, low);
            return true;
        }

        /// <summary>Numeric high-then-low comparison; the protocol's canonical order.</summary>
        public static int Compare(Id128 left, Id128 right)
        {
            int high = left.High.CompareTo(right.High);
            return high != 0 ? high : left.Low.CompareTo(right.Low);
        }

        /// <summary>Same order computed by lexicographic comparison of the canonical bytes.</summary>
        public static int CompareBytes(Id128 left, Id128 right)
        {
            byte[] leftBytes = Bytes(left);
            byte[] rightBytes = Bytes(right);
            for (int i = 0; i < IdSizeInBytes; i++)
            {
                int difference = leftBytes[i].CompareTo(rightBytes[i]);
                if (difference != 0)
                {
                    return difference;
                }
            }

            return 0;
        }

        public static Id128[] SortedCanonical(IEnumerable<Id128> ids)
        {
            Id128[] copy = ToArray(ids);
            Array.Sort(copy, Compare);
            return copy;
        }

        public static Id128[] SortedByBytes(IEnumerable<Id128> ids)
        {
            Id128[] copy = ToArray(ids);
            Array.Sort(copy, CompareBytes);
            return copy;
        }

        /// <summary>True when the two independent comparison paths agree on every pair.</summary>
        public static bool ComparisonPathsAgree(IReadOnlyList<Id128> ids)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                for (int j = 0; j < ids.Count; j++)
                {
                    int numeric = Math.Sign(Compare(ids[i], ids[j]));
                    int lexical = Math.Sign(CompareBytes(ids[i], ids[j]));
                    if (numeric != lexical)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>True when the sequence is already in canonical order with no duplicates reordered.</summary>
        public static bool IsCanonical(IReadOnlyList<Id128> ids)
        {
            for (int i = 1; i < ids.Count; i++)
            {
                if (Compare(ids[i - 1], ids[i]) > 0)
                {
                    return false;
                }
            }

            return true;
        }

        public static string Describe(IReadOnlyList<Id128> ids)
        {
            string[] parts = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                parts[i] = Hex(ids[i]);
            }

            return string.Join(",", parts);
        }

        private static Id128[] ToArray(IEnumerable<Id128> ids)
        {
            if (ids == null)
            {
                throw new ArgumentNullException(nameof(ids));
            }

            List<Id128> list = new List<Id128>();
            foreach (Id128 id in ids)
            {
                list.Add(id);
            }

            return list.ToArray();
        }

        private static void WriteWord(ulong value, byte[] destination, int offset)
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
    }
}
