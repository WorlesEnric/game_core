#nullable enable
using System;

namespace GameCore.Validation.Probe
{
    /// <summary>
    /// Small record used for the canonical byte round trip. Shape follows
    /// <c>docs/game-core/05-contracts-and-data-model.md</c> section 6: a 128-bit identifier as two 64-bit words
    /// in big-endian order, explicit integer versions and values, and a flag byte.
    /// </summary>
    public readonly struct ProbeRecord : IEquatable<ProbeRecord>
    {
        public ProbeRecord(ulong high, ulong low, uint version, int value, byte flags)
        {
            High = high;
            Low = low;
            Version = version;
            Value = value;
            Flags = flags;
        }

        public readonly ulong High;

        public readonly ulong Low;

        public readonly uint Version;

        public readonly int Value;

        public readonly byte Flags;

        public bool Equals(ProbeRecord other)
            => High == other.High
            && Low == other.Low
            && Version == other.Version
            && Value == other.Value
            && Flags == other.Flags;

        public override bool Equals(object? obj) => obj is ProbeRecord other && Equals(other);

        public override int GetHashCode()
            => unchecked((int)(High ^ (High >> 32) ^ Low) ^ (int)Version ^ Value ^ Flags);

        public override string ToString()
            => "record(high=0x" + High.ToString("X16")
            + ", low=0x" + Low.ToString("X16")
            + ", version=" + Version
            + ", value=" + Value
            + ", flags=0x" + Flags.ToString("X2") + ")";
    }

    /// <summary>
    /// Hand-written canonical codec for the probe record. It is deliberately dependency-free so the probe
    /// proves big-endian ordering and length bounds directly instead of trusting a serializer package:
    /// fixed 25-byte layout, big-endian scalars, exact-length requirement.
    /// </summary>
    public static class ProbeCanonicalCodec
    {
        /// <summary>Encoded size: high word (8) + low word (8) + version (4) + value (4) + flags (1).</summary>
        public const int EncodedLength = 25;

        /// <summary>Writes <paramref name="record"/> in canonical big-endian order into <paramref name="destination"/>.</summary>
        public static void Encode(ProbeRecord record, byte[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (destination.Length < EncodedLength)
            {
                throw new ArgumentException(
                    "destination must hold at least " + EncodedLength + " bytes but has " + destination.Length,
                    nameof(destination));
            }

            WriteBigEndianUInt64(destination, 0, record.High);
            WriteBigEndianUInt64(destination, 8, record.Low);
            WriteBigEndianUInt32(destination, 16, record.Version);
            WriteBigEndianUInt32(destination, 20, unchecked((uint)record.Value));
            destination[24] = record.Flags;
        }

        /// <summary>
        /// Decodes exactly <paramref name="length"/> bytes. Any length other than <see cref="EncodedLength"/> is
        /// rejected, which is the length-bound rejection the probe exercises with a truncated buffer.
        /// </summary>
        public static bool TryDecode(byte[] source, int length, out ProbeRecord record)
        {
            record = default;
            if (source == null || length != EncodedLength || source.Length < length)
            {
                return false;
            }

            ulong high = ReadBigEndianUInt64(source, 0);
            ulong low = ReadBigEndianUInt64(source, 8);
            uint version = ReadBigEndianUInt32(source, 16);
            int value = unchecked((int)ReadBigEndianUInt32(source, 20));
            record = new ProbeRecord(high, low, version, value, source[24]);
            return true;
        }

        private static void WriteBigEndianUInt64(byte[] buffer, int offset, ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                buffer[offset + i] = (byte)(value >> (56 - (i * 8)));
            }
        }

        private static void WriteBigEndianUInt32(byte[] buffer, int offset, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                buffer[offset + i] = (byte)(value >> (24 - (i * 8)));
            }
        }

        private static ulong ReadBigEndianUInt64(byte[] buffer, int offset)
        {
            ulong value = 0UL;
            for (int i = 0; i < 8; i++)
            {
                value = (value << 8) | buffer[offset + i];
            }

            return value;
        }

        private static uint ReadBigEndianUInt32(byte[] buffer, int offset)
        {
            uint value = 0U;
            for (int i = 0; i < 4; i++)
            {
                value = (value << 8) | buffer[offset + i];
            }

            return value;
        }
    }
}
