#nullable enable
using System;
using System.Security.Cryptography;
using System.Text;

namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Stable 128-bit registration key. The canonical byte form is the big-endian high word followed by the
    /// big-endian low word (<c>docs/game-core/05-contracts-and-data-model.md</c> section 2); the platform
    /// <see cref="Guid"/> memory order is never used as canonical bytes. The all-zero key is not a valid
    /// catalog identity.
    /// </summary>
    public readonly struct ProbeKey : IEquatable<ProbeKey>, IComparable<ProbeKey>
    {
        /// <summary>High 64 bits of the key.</summary>
        public readonly ulong High;

        /// <summary>Low 64 bits of the key.</summary>
        public readonly ulong Low;

        public ProbeKey(ulong high, ulong low)
        {
            High = high;
            Low = low;
        }

        /// <summary>True for the default all-zero key, which is never a valid generated registration key.</summary>
        public bool IsDefault => High == 0UL && Low == 0UL;

        /// <summary>
        /// Documented key derivation: SHA-256 of the UTF-8 stable name, first 16 digest bytes read as two
        /// big-endian 64-bit words. The generator verifies the literal keys in <see cref="ProbeKeys"/> against
        /// this derivation before it emits the catalog, so the recorded keys stay reproducible.
        /// </summary>
        public static ProbeKey FromStableName(string stableName)
        {
            if (stableName == null)
            {
                throw new ArgumentNullException(nameof(stableName));
            }

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] digest = sha256.ComputeHash(Encoding.UTF8.GetBytes(stableName));
                return new ProbeKey(ReadBigEndianUInt64(digest, 0), ReadBigEndianUInt64(digest, 8));
            }
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

        public bool Equals(ProbeKey other) => High == other.High && Low == other.Low;

        public override bool Equals(object? obj) => obj is ProbeKey other && Equals(other);

        public override int GetHashCode() => unchecked((int)(High ^ (High >> 32) ^ Low ^ (Low >> 32)));

        public int CompareTo(ProbeKey other)
        {
            int high = High.CompareTo(other.High);
            return high != 0 ? high : Low.CompareTo(other.Low);
        }

        /// <summary>
        /// Diagnostic form <c>0xHIGH-0xLOW</c>. Result diagnostics report this so a missing registration names
        /// the stable ID it failed to resolve instead of an anonymous default.
        /// </summary>
        public override string ToString() => "0x" + High.ToString("X16") + "-0x" + Low.ToString("X16");
    }
}
