// GameCore.Derivation — payload identity for derivation inputs (GC-006).
//
// A rule's configuration payload is immutable and versioned (P-017: "immutable payload revision"). The plan
// hash of P-027 uses semantic inputs, so the derivation input needs one canonical, allocation-free-enough way to
// fold a payload into text and into a 32-byte hash. Two payloads with equal bytes are the same payload; a
// reconfiguration that changes the bytes changes the contribution's effective value while its contribution key
// stays the same (P-017).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using GameCore.Contracts;

namespace GameCore.Derivation
{
    /// <summary>Canonical text and hash of a frozen payload.</summary>
    public static class PayloadCodec
    {
        private const string HexDigits = "0123456789abcdef";

        /// <summary>Lowercase hex of the payload bytes; the canonical text form used by snapshot and plan hashes.</summary>
        public static string ToHex(FrozenPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            IReadOnlyList<byte> bytes = payload.Bytes;
            char[] chars = new char[bytes.Count * 2];
            for (int i = 0; i < bytes.Count; i++)
            {
                byte value = bytes[i];
                chars[i * 2] = HexDigits[value >> 4];
                chars[(i * 2) + 1] = HexDigits[value & 0xF];
            }

            return new string(chars);
        }

        /// <summary>SHA-256 over the payload bytes (empty payload yields the SHA-256 of the empty input).</summary>
        public static ContentHash HashOf(FrozenPayload payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            IReadOnlyList<byte> bytes = payload.Bytes;
            byte[] copy = new byte[bytes.Count];
            for (int i = 0; i < bytes.Count; i++)
            {
                copy[i] = bytes[i];
            }

            using (SHA256 sha = SHA256.Create())
            {
                return new ContentHash(sha.ComputeHash(copy));
            }
        }

        /// <summary>Folds a payload into a running text projection without allocating an intermediate string.</summary>
        public static void AppendCanonical(StringBuilder text, FrozenPayload payload)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            IReadOnlyList<byte> bytes = payload.Bytes;
            for (int i = 0; i < bytes.Count; i++)
            {
                byte value = bytes[i];
                text.Append(HexDigits[value >> 4]).Append(HexDigits[value & 0xF]);
            }
        }

        /// <summary>A canonically ordered decimal projection of one signed 32-bit priority (P-018).</summary>
        public static string PriorityText(int priority) => priority.ToString(CultureInfo.InvariantCulture);
    }
}
