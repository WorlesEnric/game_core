// GameCore.Contracts - generated registration key derivation (GC-003). Normative sources:
// docs/game-core/00-core-protocols.md P-004 (stable 128-bit ids with a catalog name for diagnostics) and
// docs/game-core/05-contracts-and-data-model.md s3/s6 (generated registration keys; no CLR name is identity).
//
// The rule is fixed and documented so a generated literal, a build-time generator and a runtime catalog all
// derive the same key: SHA-256 over the UTF-8 stable name, first 16 digest bytes read as two big-endian 64-bit
// words. The stable name is the diagnostic name, never the runtime identity.
#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace GameCore.Contracts
{
    /// <summary>Deterministic 128-bit key derivation from a stable content name (P-004).</summary>
    public static class StableNameKeyDerivation
    {
        /// <summary>Exact derivation, embedded in generated catalogs beside the derived literals.</summary>
        public const string Scope =
            "SHA-256 over the UTF-8 bytes of the stable name; the first 16 digest bytes are read as two "
            + "big-endian unsigned 64-bit words (first 8 -> High, last 8 -> Low). The all-zero result is not a "
            + "valid catalog identity, and the stable name is a diagnostic label only (P-004).";

        /// <summary>Characters accepted in a stable name; anything else rejects rather than being normalized.</summary>
        public const string AllowedCharacters = "a-z 0-9 . _ -";

        /// <summary>Derives the key of one stable name. The name is not normalized: it must already be canonical.</summary>
        public static Id128 Derive(string stableName)
        {
            if (stableName == null)
            {
                throw new ArgumentNullException(nameof(stableName));
            }

            if (stableName.Length == 0)
            {
                throw new ArgumentException("A stable name cannot be empty.", nameof(stableName));
            }

            if (!IsCanonicalStableName(stableName))
            {
                throw new ArgumentException(
                    "A stable name uses only " + AllowedCharacters + " and cannot start or end with '.'; received '" +
                    stableName + "'.",
                    nameof(stableName));
            }

            byte[] digest;
            using (SHA256 sha256 = SHA256.Create())
            {
                digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(stableName));
            }

            return new Id128(ReadBigEndianUInt64(digest, 0), ReadBigEndianUInt64(digest, 8));
        }

        /// <summary>True when the name is canonical lowercase reverse-domain-ish text; no case folding is applied.</summary>
        public static bool IsCanonicalStableName(string? stableName)
        {
            if (string.IsNullOrEmpty(stableName) || stableName.Length > 200)
            {
                return false;
            }

            if (stableName[0] == '.' || stableName[stableName.Length - 1] == '.')
            {
                return false;
            }

            bool previousWasDot = false;
            for (int i = 0; i < stableName.Length; i++)
            {
                char c = stableName[i];
                bool allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (c == '.')
                {
                    if (previousWasDot)
                    {
                        return false;
                    }

                    previousWasDot = true;
                    continue;
                }

                if (!allowed)
                {
                    return false;
                }

                previousWasDot = false;
            }

            return true;
        }

        private static ulong ReadBigEndianUInt64(byte[] bytes, int offset)
        {
            ulong value = 0UL;
            for (int i = 0; i < 8; i++)
            {
                value = (value << 8) | bytes[offset + i];
            }

            return value;
        }
    }
}
