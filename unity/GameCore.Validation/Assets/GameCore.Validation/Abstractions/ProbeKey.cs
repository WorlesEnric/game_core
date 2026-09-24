#nullable enable
using System;
using GameCore.Contracts;

namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Stable 128-bit registration key used by the probe's own registration tables. The canonical byte form is
    /// the big-endian high word followed by the big-endian low word
    /// (<c>docs/game-core/05-contracts-and-data-model.md</c> section 2); the platform <see cref="Guid"/> memory
    /// order is never used as canonical bytes.
    /// </summary>
    /// <remarks>
    /// Since GC-003 this type is a thin projection of the production contract identity
    /// <see cref="GameCore.Contracts.Id128"/>: derivation delegates to
    /// <see cref="StableNameKeyDerivation"/>, so the probe and the content compiler cannot disagree about how a
    /// stable name maps to a key.
    /// </remarks>
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

        /// <summary>Projects a production contract identity into the probe's key shape.</summary>
        public ProbeKey(Id128 value)
        {
            High = value.High;
            Low = value.Low;
        }

        /// <summary>True for the default all-zero key, which is never a valid generated registration key.</summary>
        public bool IsDefault => High == 0UL && Low == 0UL;

        /// <summary>Production contract identity of this key (P-004).</summary>
        public Id128 ToId128() => new Id128(High, Low);

        /// <summary>
        /// Documented key derivation: SHA-256 of the UTF-8 stable name, first 16 digest bytes read as two
        /// big-endian 64-bit words. Delegates to the production contract so one implementation owns the rule;
        /// the generator verifies the literal keys in <see cref="ProbeKeys"/> against it before emitting.
        /// </summary>
        public static ProbeKey FromStableName(string stableName) =>
            new ProbeKey(StableNameKeyDerivation.Derive(stableName));

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
