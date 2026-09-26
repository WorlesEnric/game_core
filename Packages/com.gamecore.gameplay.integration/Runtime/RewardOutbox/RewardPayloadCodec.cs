// GameCore.Gameplay.Integration - the reward payload (GC-021).
//
// The reward a narrative choice grants is content, so its payload is explicit, fixed-width and big-endian: the same
// canonical framing every other payload in this repository uses (docs/game-core/05-contracts-and-data-model.md s6,
// P-054). It carries the whole reward definition rather than a lookup key, so an obligation reinstated from a
// checkpoint in a *new* session still knows what it owes the destination without a second side table (P-049, P-053).
//
// The payload is the obligation's recorded bytes. It is what the journal writes, what a checkpoint carries and what a
// restored world reads back, so it must be self-describing and versioned: byte 0 is the payload version, and a reader
// that does not know the version refuses the payload rather than reading the fields at the wrong offsets.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Integration.RewardOutbox
{
    /// <summary>The canonical reward payload: version, node, card, recipient, holder, revision (P-054).</summary>
    public static class RewardPayloadCodec
    {
        /// <summary>Payload layout this build writes; a payload from another version is refused (P-054).</summary>
        public const uint CurrentPayloadVersion = 1U;

        /// <summary>Exact payload size: version, node, recipient, holder, revision, card (P-054).</summary>
        public const int PayloadBytes = 4 + 4 + 4 + 4 + 4 + 8;

        /// <summary>Encodes one reward definition.</summary>
        public static byte[] Write(RewardDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var bytes = new byte[PayloadBytes];
            WriteUInt32(bytes, 0, CurrentPayloadVersion);
            WriteInt32(bytes, 4, definition.NodeOrdinal);
            WriteUInt32(bytes, 8, definition.RecipientSeat);
            WriteUInt32(bytes, 12, definition.HoldingSeat);
            WriteUInt32(bytes, 16, definition.Revision);
            WriteUInt64(bytes, 20, definition.Card.Value);
            return bytes;
        }

        /// <summary>
        /// Decodes one reward payload. False reports the exact reason — a wrong length, an unknown version — rather
        /// than substituting a default reward, because a defaulted reward would grant the wrong card (P-054).
        /// </summary>
        public static bool TryRead(
            IReadOnlyList<byte>? payload,
            out RewardDefinition? definition,
            out string detail)
        {
            definition = null;
            detail = string.Empty;
            if (payload == null || payload.Count != PayloadBytes)
            {
                detail = "a reward payload is exactly " + PayloadBytes.ToString(CultureInfo.InvariantCulture)
                    + " bytes and this one is " + (payload == null ? 0 : payload.Count).ToString(
                        CultureInfo.InvariantCulture) + " (P-054).";
                return false;
            }

            uint version = ReadUInt32(payload, 0);
            if (version != CurrentPayloadVersion)
            {
                detail = "a reward payload declares version " + version.ToString(CultureInfo.InvariantCulture)
                    + " and this build writes " + CurrentPayloadVersion.ToString(CultureInfo.InvariantCulture)
                    + "; an unknown payload version is refused (P-054).";
                return false;
            }

            var card = new CardId(ReadUInt64(payload, 20));
            if (card.IsNone)
            {
                detail = "a reward payload grants the all-zero card identity, which is not a card (P-004).";
                return false;
            }

            definition = new RewardDefinition(
                ReadInt32(payload, 4),
                card,
                ReadUInt32(payload, 8),
                ReadUInt32(payload, 12),
                ReadUInt32(payload, 16));
            return true;
        }

        private static void WriteInt32(byte[] destination, int offset, int value) =>
            WriteUInt32(destination, offset, unchecked((uint)value));

        private static void WriteUInt32(byte[] destination, int offset, uint value)
        {
            destination[offset] = (byte)(value >> 24);
            destination[offset + 1] = (byte)(value >> 16);
            destination[offset + 2] = (byte)(value >> 8);
            destination[offset + 3] = (byte)value;
        }

        private static void WriteUInt64(byte[] destination, int offset, ulong value)
        {
            WriteUInt32(destination, offset, (uint)(value >> 32));
            WriteUInt32(destination, offset + 4, (uint)value);
        }

        private static int ReadInt32(IReadOnlyList<byte> source, int offset) =>
            unchecked((int)ReadUInt32(source, offset));

        private static uint ReadUInt32(IReadOnlyList<byte> source, int offset) =>
            ((uint)source[offset] << 24) | ((uint)source[offset + 1] << 16)
            | ((uint)source[offset + 2] << 8) | source[offset + 3];

        private static ulong ReadUInt64(IReadOnlyList<byte> source, int offset) =>
            ((ulong)ReadUInt32(source, offset) << 32) | ReadUInt32(source, offset + 4);
    }
}
