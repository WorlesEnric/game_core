// GameCore.Gameplay.Contracts - the gameplay command/event payload codec.
//
// Gameplay payloads are flat little-endian records of int32 values and 128-bit ids (high word first, each word
// little-endian). A reader refuses a payload of the wrong length instead of guessing, so a command written against
// another schema version is a decode failure, never a silently misread value (P-054).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;

namespace GameCore.Gameplay.Contracts
{
    /// <summary>Writes one gameplay payload.</summary>
    public sealed class GameplayPayloadWriter
    {
        private readonly List<byte> bytes = new List<byte>(32);

        public int Length => bytes.Count;

        public GameplayPayloadWriter Int32(int value)
        {
            unchecked
            {
                bytes.Add((byte)value);
                bytes.Add((byte)(value >> 8));
                bytes.Add((byte)(value >> 16));
                bytes.Add((byte)(value >> 24));
            }

            return this;
        }

        public GameplayPayloadWriter UInt64(ulong value)
        {
            for (int i = 0; i < 8; i++)
            {
                bytes.Add((byte)(value >> (8 * i)));
            }

            return this;
        }

        public GameplayPayloadWriter Id(Id128 value) => UInt64(value.High).UInt64(value.Low);

        public byte[] ToArray() => bytes.ToArray();

        public FrozenPayload Freeze() => new FrozenPayload(bytes.ToArray());
    }

    /// <summary>Reads one gameplay payload of an exact expected length.</summary>
    public sealed class GameplayPayloadReader
    {
        private readonly IReadOnlyList<byte> bytes;
        private int offset;

        public GameplayPayloadReader(IReadOnlyList<byte> bytes)
        {
            this.bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
        }

        public int Remaining => bytes.Count - offset;

        /// <summary>True when the payload is exactly <paramref name="expected"/> bytes long.</summary>
        public bool HasLength(int expected) => bytes.Count == expected;

        public int Int32()
        {
            Require(4);
            int value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16) | (bytes[offset + 3] << 24);
            offset += 4;
            return value;
        }

        public ulong UInt64()
        {
            Require(8);
            ulong value = 0UL;
            for (int i = 7; i >= 0; i--)
            {
                value = (value << 8) | bytes[offset + i];
            }

            offset += 8;
            return value;
        }

        public Id128 Id()
        {
            ulong high = UInt64();
            ulong low = UInt64();
            return new Id128(high, low);
        }

        private void Require(int count)
        {
            if (offset + count > bytes.Count)
            {
                throw new FormatException(
                    "A gameplay payload of " + bytes.Count + " bytes has no " + count + " more bytes at offset " + offset + ".");
            }
        }
    }

    /// <summary>
    /// The catalog binding object of every generated gameplay registration: the generated catalog's tables bind each
    /// key to one of these, so the catalog is pure data and no gameplay type is constructed through reflection (P-009).
    /// </summary>
    public interface IGameplayCatalogEntry
    {
        FactoryKey Key { get; }
    }

    /// <summary>The one implementation of <see cref="IGameplayCatalogEntry"/>.</summary>
    public sealed class GameplayCatalogEntry : IGameplayCatalogEntry
    {
        public GameplayCatalogEntry(FactoryKey key)
        {
            Key = key;
        }

        public FactoryKey Key { get; }
    }
}
