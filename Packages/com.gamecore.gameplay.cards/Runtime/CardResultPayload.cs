// GameCore.Gameplay.Cards — the committed result record of one settled card command (GC-011).
//
// Normative sources: 07 s2.2 (`SetCommitted`: "Table/seat IDs, consumed card IDs, score delta, request ID, step,
// epoch"), P-044 (a successful logical step prepares committed events and a consistent snapshot) and 05 s6
// (declared fields, fixed-width scalars).
//
// The record carries the values the step actually committed, never a prediction. An observer that compares this
// payload with the live ECS storage is comparing the authority's own statement with the authority's own state,
// which is what makes "no score prediction becomes authority" (07 s2.3) checkable.
#nullable enable
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Rules.Cards;

namespace GameCore.Gameplay.Cards
{
    /// <summary>`SetCommitted`: the committed outcome of one settled command, as the event payload carries it.</summary>
    public readonly struct CardResultPayload
    {
        /// <summary>The command that was settled: `SubmitSet`, `Transfer` or `Contest`.</summary>
        public readonly CardCommandKind Kind;

        /// <summary>Ordinal of the seat the settlement addressed.</summary>
        public readonly uint Seat;

        /// <summary>Ordinal of the receiving seat of a transfer, or of the contest winner; unused otherwise.</summary>
        public readonly uint Counterparty;

        /// <summary>The rules' verdict. Only <see cref="CardSettlementStatus.Committed"/> is ever published.</summary>
        public readonly CardSettlementStatus Status;

        /// <summary>Score delta applied to the addressing seat; zero for a transfer or a contest.</summary>
        public readonly int ScoreDelta;

        /// <summary>Score the addressing seat holds after the settlement.</summary>
        public readonly int ScoreAfter;

        /// <summary>Table version after the settlement; the version the next command must be authored against.</summary>
        public readonly uint TableVersion;

        /// <summary>How many card identities this record names.</summary>
        public readonly byte CardCount;

        /// <summary>First moved or consumed card.</summary>
        public readonly CardId Card0;

        /// <summary>Second moved or consumed card.</summary>
        public readonly CardId Card1;

        /// <summary>Third moved or consumed card.</summary>
        public readonly CardId Card2;

        /// <summary>Builds one committed result record.</summary>
        public CardResultPayload(
            CardCommandKind kind,
            uint seat,
            uint counterparty,
            CardSettlementStatus status,
            int scoreDelta,
            int scoreAfter,
            uint tableVersion,
            byte cardCount,
            CardId card0,
            CardId card1,
            CardId card2)
        {
            Kind = kind;
            Seat = seat;
            Counterparty = counterparty;
            Status = status;
            ScoreDelta = scoreDelta;
            ScoreAfter = scoreAfter;
            TableVersion = tableVersion;
            CardCount = cardCount;
            Card0 = card0;
            Card1 = card1;
            Card2 = card2;
        }

        /// <summary>Reads one named card; false when the index is outside <see cref="CardCount"/>.</summary>
        public bool TryCard(int index, out CardId card)
        {
            if (index < 0 || index >= CardCount)
            {
                card = default(CardId);
                return false;
            }

            switch (index)
            {
                case 0:
                    card = Card0;
                    return true;
                case 1:
                    card = Card1;
                    return true;
                default:
                    card = Card2;
                    return true;
            }
        }

        /// <summary>One-line diagnostic form; never an identity (P-004).</summary>
        public override string ToString() =>
            "committed(" + Kind.ToString()
            + ",seat=" + Seat.ToString(CultureInfo.InvariantCulture)
            + ",counterparty=" + Counterparty.ToString(CultureInfo.InvariantCulture)
            + ",delta=" + ScoreDelta.ToString(CultureInfo.InvariantCulture)
            + ",after=" + ScoreAfter.ToString(CultureInfo.InvariantCulture)
            + ",version=" + TableVersion.ToString(CultureInfo.InvariantCulture)
            + ",cards=" + CardCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Canonical codec of the committed result record: fixed-width big-endian scalars, the 05 s6 convention, so an
    /// event payload is decoded by the same rule that wrote it.
    /// </summary>
    public static class CardResultCodec
    {
        /// <summary>Bytes of one committed result record.</summary>
        public const int ResultBytes = 60;

        /// <summary>Encodes one committed result record as a canonical frozen payload (P-045).</summary>
        public static FrozenPayload Write(CardResultPayload result)
        {
            var bytes = new byte[ResultBytes];
            WriteUInt32(bytes, 0, (uint)result.Kind);
            WriteUInt32(bytes, 4, result.Seat);
            WriteUInt32(bytes, 8, result.Counterparty);
            WriteUInt32(bytes, 12, (uint)result.Status);
            WriteInt32(bytes, 16, result.ScoreDelta);
            WriteInt32(bytes, 20, result.ScoreAfter);
            WriteUInt32(bytes, 24, result.TableVersion);
            WriteUInt32(bytes, 28, result.CardCount);
            WriteUInt64(bytes, 32, result.Card0.Value);
            WriteUInt64(bytes, 40, result.Card1.Value);
            WriteUInt64(bytes, 48, result.Card2.Value);
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one committed result record; false reports a malformed or truncated payload.</summary>
        public static bool TryRead(IReadOnlyList<byte>? payload, out CardResultPayload result)
        {
            result = default(CardResultPayload);
            if (payload == null || payload.Count != ResultBytes)
            {
                return false;
            }

            var kind = (CardCommandKind)ReadUInt32(payload, 0);
            var status = (CardSettlementStatus)ReadUInt32(payload, 12);
            uint cardCount = ReadUInt32(payload, 28);
            if (!CardPayloadCodec.IsDeclaredKind(kind)
                || status != CardSettlementStatus.Committed
                || cardCount > 3U)
            {
                return false;
            }

            result = new CardResultPayload(
                kind,
                ReadUInt32(payload, 4),
                ReadUInt32(payload, 8),
                status,
                ReadInt32(payload, 16),
                ReadInt32(payload, 20),
                ReadUInt32(payload, 24),
                (byte)cardCount,
                new CardId(ReadUInt64(payload, 32)),
                new CardId(ReadUInt64(payload, 40)),
                new CardId(ReadUInt64(payload, 48)));
            return true;
        }

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

        private static void WriteInt32(byte[] bytes, int offset, int value) =>
            WriteUInt32(bytes, offset, unchecked((uint)value));

        private static void WriteUInt64(byte[] bytes, int offset, ulong value)
        {
            WriteUInt32(bytes, offset, (uint)(value >> 32));
            WriteUInt32(bytes, offset + 4, (uint)value);
        }

        private static uint ReadUInt32(IReadOnlyList<byte> bytes, int offset)
            => ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];

        private static int ReadInt32(IReadOnlyList<byte> bytes, int offset) =>
            unchecked((int)ReadUInt32(bytes, offset));

        private static ulong ReadUInt64(IReadOnlyList<byte> bytes, int offset)
            => ((ulong)ReadUInt32(bytes, offset) << 32) | ReadUInt32(bytes, offset + 4);
    }
}
