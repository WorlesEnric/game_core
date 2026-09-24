// GameCore.Contracts - production shared contract type (GC-003). Unity-free: BCL subset only, no
// UnityEngine/Unity.* reference, no runtime reflection and no second ECS facade (01 s1, P-058).
// Normative sources: docs/game-core/00-core-protocols.md and docs/game-core/05-contracts-and-data-model.md.
// The public surface of this assembly is API-compatible with the frozen W0 reference seam
// (tests/GameCore.ReferenceSeams); additions are reviewed in artifacts/gc-003/HANDOFF.md.
#nullable enable
using System;

namespace GameCore.Contracts
{
    /// <summary>Canonical lowercase hexadecimal parsing and formatting.</summary>
    public static class CanonicalHex
    {
        private const string Digits = "0123456789abcdef";

        /// <summary>True for the canonical digits only: '0'-'9' and 'a'-'f'. Uppercase is not canonical.</summary>
        public static bool IsCanonicalDigit(char value) =>
            (value >= '0' && value <= '9') || (value >= 'a' && value <= 'f');

        /// <summary>Parses exactly 16 canonical hex characters at <paramref name="offset"/>.</summary>
        public static bool TryParseUInt64(string? text, int offset, out ulong value)
        {
            value = 0UL;
            if (text == null || offset < 0 || offset + 16 > text.Length)
            {
                return false;
            }

            ulong accumulator = 0UL;
            for (int i = 0; i < 16; i++)
            {
                char character = text[offset + i];
                if (!IsCanonicalDigit(character))
                {
                    return false;
                }

                accumulator = (accumulator << 4) | (uint)Nibble(character);
            }

            value = accumulator;
            return true;
        }

        /// <summary>Parses exactly <c>destination.Length * 2</c> canonical hex characters into bytes.</summary>
        public static bool TryParseBytes(string? text, byte[] destination, int offset)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (text == null || offset < 0 || offset + (destination.Length * 2) > text.Length)
            {
                return false;
            }

            for (int i = 0; i < destination.Length; i++)
            {
                char high = text[offset + (i * 2)];
                char low = text[(offset + (i * 2)) + 1];
                if (!IsCanonicalDigit(high) || !IsCanonicalDigit(low))
                {
                    return false;
                }

                destination[i] = (byte)((Nibble(high) << 4) | Nibble(low));
            }

            return true;
        }

        /// <summary>Formats bytes as lowercase hex; the inverse of <see cref="TryParseBytes"/>.</summary>
        public static string ToHex(byte[] bytes, int offset, int count)
        {
            if (bytes == null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }

            if (offset < 0 || count < 0 || offset + count > bytes.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(offset), "The requested byte range is outside the array.");
            }

            char[] text = new char[count * 2];
            for (int i = 0; i < count; i++)
            {
                byte value = bytes[offset + i];
                text[i * 2] = Digits[value >> 4];
                text[(i * 2) + 1] = Digits[value & 0x0F];
            }

            return new string(text);
        }

        private static int Nibble(char value) => value <= '9' ? value - '0' : (value - 'a') + 10;
    }
}
