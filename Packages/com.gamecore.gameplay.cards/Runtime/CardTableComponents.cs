// GameCore.Gameplay.Cards — the card table's ECS storage and its bounded wire payloads (GC-011).
//
// Normative sources: 07 s2.2 (`TableState`, `MarketEntry[]`, `DeckEntry[]`, `SeatHand[]`, `SeatScore`,
// `CardCommand`, `CardDecision`, `SetCommitted`), 05 s6 (declared schema fields, fixed-width scalars, no
// reflection) and 04 s8 (a payload is decoded by a hand-written reader bound to its schema; a missing reader is
// reported).
//
// Every component is blittable and holds no managed reference, so it is safe under Burst and IL2CPP. The
// payload encoding is FIXED-WIDTH BIG-ENDIAN, which is the scalar convention of 05 s6 that
// `IntegrationSlotValues` already writes for derived values, so a command's payload, a binding row's value and a
// committed event's payload all agree byte for byte.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Contracts;
using GameCore.Execution.Messages;
using GameCore.Rules.Cards;
using Unity.Entities;

namespace GameCore.Gameplay.Cards
{
    /// <summary>`TableState { ActiveSeat, TurnNumber, Version }`: the table entity's authoritative state (07 s2.2).</summary>
    public struct CardTableState : IComponentData
    {
        /// <summary>Ordinal of the seat whose turn it is; a command from another seat is rejected.</summary>
        public uint ActiveSeat;

        /// <summary>Turn ordinal, advanced by exactly one per committed settlement.</summary>
        public uint TurnNumber;

        /// <summary>
        /// Bounded commit version. A command authored against another value is rejected before any write, which
        /// is the stale-plan guard of 07 s2.3 ("if the table version is stale ... this command produces a
        /// rejection with no card or score changes").
        /// </summary>
        public uint TableVersion;

        /// <summary>1 when the match is closed; a closed table accepts no settlement.</summary>
        public byte Closed;

        /// <summary>True when the match is closed.</summary>
        public bool IsClosed => Closed != 0;
    }

    /// <summary>`SeatScore { Total }` and the seat's ordinal, on the seat entity (07 s2.2).</summary>
    public struct CardSeatState : IComponentData
    {
        /// <summary>Ordinal of this seat within the match; the settlement request names it.</summary>
        public uint Ordinal;

        /// <summary>Accumulated score; only a committed set changes it.</summary>
        public int Score;

        /// <summary>1 while the seat is seated; a departed seat accepts no command.</summary>
        public byte Seated;
    }

    /// <summary>`SeatHand[]`: one held card per row, on the seat entity (07 s2.2).</summary>
    public struct CardHandRow : IBufferElementData
    {
        /// <summary>The held card identity; <see cref="CardId.IsNone"/> is never stored.</summary>
        public ulong Card;
    }

    /// <summary>`MarketEntry[]`: one market assignment per row, on the table entity (07 s2.2).</summary>
    public struct CardMarketRow : IBufferElementData
    {
        /// <summary>The market card identity.</summary>
        public ulong Card;

        /// <summary>1 while the card is reserved by an admitted draft; a reserved card is not re-claimable.</summary>
        public byte Reserved;
    }

    /// <summary>`DeckEntry[]`: one undealt card per row, on the table entity (07 s2.2).</summary>
    public struct CardDeckRow : IBufferElementData
    {
        /// <summary>The undealt card identity.</summary>
        public ulong Card;
    }

    /// <summary>
    /// The decoded command draft: one row per admitted command of the sealed step, written by `cards.input` and
    /// never read as a decision. It is the owner-local `CardCommand` buffer of 07 s2.2.
    /// </summary>
    public struct CardCommandRow : IBufferElementData
    {
        /// <summary>Canonically ordered carrier of the admitted message; its payload has already been copied.</summary>
        public StepMessage Message;

        /// <summary>Decoded ordinary command payload; meaningful only when <see cref="IsBatch"/> is 0.</summary>
        public CardCommandPayload Command;

        /// <summary>Decoded atomic batch envelope; meaningful only when <see cref="IsBatch"/> is 1.</summary>
        public CardBatchPayload Batch;

        /// <summary>1 when this row carries a batch envelope rather than an ordinary command.</summary>
        public byte IsBatch;

        /// <summary>True when this row carries a batch envelope.</summary>
        public bool IsBatchEnvelope => IsBatch != 0;
    }

    /// <summary>
    /// The validated decision draft: the bounded write set one command would commit, plus the rules' verdict.
    /// `cards.validate` produces it and `cards.commit` consumes it; it is the `CardDecision` of 07 s2.2 and the
    /// declared step buffer's payload.
    /// </summary>
    public struct CardDecisionRow : IBufferElementData
    {
        /// <summary>The admitted message this decision belongs to.</summary>
        public StepMessage Message;

        /// <summary>The command that was validated.</summary>
        public CardCommandKind Kind;

        /// <summary>The rules' verdict; <see cref="CardSettlementStatus.Committed"/> carries writes.</summary>
        public CardSettlementStatus Status;

        /// <summary>How many of the six declared write slots are meaningful; zero for a rejected decision.</summary>
        public byte WriteCount;

        /// <summary>Receiving seat of a transfer, or the contest winner; unused for a set.</summary>
        public uint Counterparty;

        /// <summary>First write of the bounded set.</summary>
        public CardWrite W0;

        /// <summary>Second write of the bounded set.</summary>
        public CardWrite W1;

        /// <summary>Third write of the bounded set.</summary>
        public CardWrite W2;

        /// <summary>Fourth write of the bounded set.</summary>
        public CardWrite W3;

        /// <summary>Fifth write of the bounded set.</summary>
        public CardWrite W4;

        /// <summary>Sixth write of the bounded set.</summary>
        public CardWrite W5;

        /// <summary>Reads one write of the set; false when the index is outside <see cref="WriteCount"/>.</summary>
        public bool TryWrite(int index, out CardWrite write)
        {
            if (index < 0 || index >= WriteCount)
            {
                write = default(CardWrite);
                return false;
            }

            switch (index)
            {
                case 0:
                    write = W0;
                    return true;
                case 1:
                    write = W1;
                    return true;
                case 2:
                    write = W2;
                    return true;
                case 3:
                    write = W3;
                    return true;
                case 4:
                    write = W4;
                    return true;
                default:
                    write = W5;
                    return true;
            }
        }

        /// <summary>Writes one entry of a bounded set; false when the index is outside the six declared slots.</summary>
        public bool TrySetWrite(int index, CardWrite write)
        {
            switch (index)
            {
                case 0:
                    W0 = write;
                    return true;
                case 1:
                    W1 = write;
                    return true;
                case 2:
                    W2 = write;
                    return true;
                case 3:
                    W3 = write;
                    return true;
                case 4:
                    W4 = write;
                    return true;
                case 5:
                    W5 = write;
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// `SetCommitted`: the committed output one settled command prepared, on the table entity (07 s2.2). It
    /// names the consumed cards, the score delta, the request identity, the step and the epoch, so an observer
    /// reads one coherent record rather than predicting the score from its own model.
    /// </summary>
    public struct CardCommittedRow : IBufferElementData
    {
        /// <summary>The settled message: its request, step and epoch are the record's causal identity (P-045).</summary>
        public StepMessage Message;

        /// <summary>The command that was settled.</summary>
        public CardCommandKind Kind;

        /// <summary>Score delta this settlement applied to the addressing seat.</summary>
        public int ScoreDelta;

        /// <summary>Score the addressing seat holds after the settlement.</summary>
        public int ScoreAfter;

        /// <summary>Table version after the settlement.</summary>
        public uint TableVersion;

        /// <summary>How many of <see cref="Card0"/>..<see cref="Card2"/> are meaningful.</summary>
        public byte CardCount;

        /// <summary>First consumed or moved card.</summary>
        public ulong Card0;

        /// <summary>Second consumed or moved card.</summary>
        public ulong Card1;

        /// <summary>Third consumed or moved card.</summary>
        public ulong Card2;
    }

    /// <summary>
    /// The table's committed snapshot: one row per step, so a reader compares the table's own coherent image
    /// with the live storage instead of re-deriving the state (07 s2.3: "no score prediction becomes authority").
    /// </summary>
    public struct CardTableSnapshot : IComponentData
    {
        /// <summary>Logical step of this snapshot.</summary>
        public ulong Step;

        /// <summary>Assembly epoch of this snapshot.</summary>
        public ulong Epoch;

        /// <summary>Table version after the step.</summary>
        public uint TableVersion;

        /// <summary>Turn ordinal after the step.</summary>
        public uint TurnNumber;

        /// <summary>Active seat ordinal after the step.</summary>
        public uint ActiveSeat;

        /// <summary>Sum of every seat's score after the step, so conservation is observable.</summary>
        public int TotalScore;

        /// <summary>Sum of the market, deck and hand card rows after the step, so conservation is observable.</summary>
        public int TotalCards;

        /// <summary>Settlements committed in this step.</summary>
        public int CommittedCount;

        /// <summary>Commands rejected in this step.</summary>
        public int RejectedCount;
    }

    /// <summary>One admitted ordinary card command, as the input stage decodes it (07 s2.3).</summary>
    public readonly struct CardCommandPayload
    {
        /// <summary>The command's kind.</summary>
        public readonly CardCommandKind Kind;

        /// <summary>Ordinal of the issuing seat.</summary>
        public readonly uint Seat;

        /// <summary>Ordinal of the receiving seat of a transfer, or an unused ordinal.</summary>
        public readonly uint Counterparty;

        /// <summary>The table version the issuer authored against; a mismatch rejects before any write.</summary>
        public readonly uint ExpectedTableVersion;

        /// <summary>First candidate card.</summary>
        public readonly CardId Card0;

        /// <summary>Second candidate card, for a set.</summary>
        public readonly CardId Card1;

        /// <summary>Third candidate card, for a set.</summary>
        public readonly CardId Card2;

        /// <summary>Builds one command payload.</summary>
        public CardCommandPayload(
            CardCommandKind kind,
            uint seat,
            uint counterparty,
            uint expectedTableVersion,
            CardId card0,
            CardId card1,
            CardId card2)
        {
            Kind = kind;
            Seat = seat;
            Counterparty = counterparty;
            ExpectedTableVersion = expectedTableVersion;
            Card0 = card0;
            Card1 = card1;
            Card2 = card2;
        }

        /// <summary>How many candidates this payload carries; a set carries three, a transfer one.</summary>
        public int CandidateCount
        {
            get
            {
                if (Kind == CardCommandKind.SubmitSet)
                {
                    return CardSetRules.SetCardCount;
                }

                return Kind == CardCommandKind.Transfer ? 1 : 0;
            }
        }

        /// <summary>Reads one candidate; false when the index is outside the payload's candidate count.</summary>
        public bool TryCandidate(int index, out CardId card)
        {
            if (index < 0 || index >= CandidateCount)
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

        /// <summary>The bounded candidate list this payload declares, in payload order.</summary>
        public CardId[] Candidates()
        {
            int count = CandidateCount;
            var cards = new CardId[count];
            for (int i = 0; i < count; i++)
            {
                TryCandidate(i, out CardId card);
                cards[i] = card;
            }

            return cards;
        }

        /// <summary>One-line diagnostic form; never an identity (P-004).</summary>
        public override string ToString() =>
            Kind.ToString() + "(seat=" + Seat.ToString(CultureInfo.InvariantCulture)
            + ",version=" + ExpectedTableVersion.ToString(CultureInfo.InvariantCulture)
            + ",cards=" + CandidateCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One atomic batch envelope: the bounded candidate set of a simultaneous contest (07 s2.3, P-037). Its
    /// candidates are paired ordinals and bid sequences, read by index, so the winner never depends on array
    /// order.
    /// </summary>
    public readonly struct CardBatchPayload
    {
        /// <summary>Identity of the admitted batch; it names the contest in the committed record.</summary>
        public readonly ulong BatchId;

        /// <summary>The contested card.</summary>
        public readonly CardId ContestedCard;

        /// <summary>Ordinal of the seat currently holding the contested card.</summary>
        public readonly uint Holder;

        /// <summary>The table version the batch was authored against.</summary>
        public readonly uint ExpectedTableVersion;

        /// <summary>How many of the four candidate slots are meaningful.</summary>
        public readonly byte CandidateCount;

        private readonly uint seat0;
        private readonly uint seat1;
        private readonly uint seat2;
        private readonly uint seat3;
        private readonly ulong sequence0;
        private readonly ulong sequence1;
        private readonly ulong sequence2;
        private readonly ulong sequence3;

        /// <summary>Builds one batch envelope from four paired candidate slots.</summary>
        public CardBatchPayload(
            ulong batchId,
            CardId contestedCard,
            uint holder,
            uint expectedTableVersion,
            byte candidateCount,
            uint candidateSeat0,
            ulong candidateSequence0,
            uint candidateSeat1,
            ulong candidateSequence1,
            uint candidateSeat2,
            ulong candidateSequence2,
            uint candidateSeat3,
            ulong candidateSequence3)
        {
            BatchId = batchId;
            ContestedCard = contestedCard;
            Holder = holder;
            ExpectedTableVersion = expectedTableVersion;
            CandidateCount = candidateCount;
            seat0 = candidateSeat0;
            seat1 = candidateSeat1;
            seat2 = candidateSeat2;
            seat3 = candidateSeat3;
            sequence0 = candidateSequence0;
            sequence1 = candidateSequence1;
            sequence2 = candidateSequence2;
            sequence3 = candidateSequence3;
        }

        /// <summary>Reads one paired candidate; false when the index is outside the declared count.</summary>
        public bool TryCandidate(int index, out SeatOrdinal seat, out ulong sequence)
        {
            if (index < 0 || index >= CandidateCount || index > 3)
            {
                seat = default(SeatOrdinal);
                sequence = 0UL;
                return false;
            }

            switch (index)
            {
                case 0:
                    seat = new SeatOrdinal(seat0);
                    sequence = sequence0;
                    return true;
                case 1:
                    seat = new SeatOrdinal(seat1);
                    sequence = sequence1;
                    return true;
                case 2:
                    seat = new SeatOrdinal(seat2);
                    sequence = sequence2;
                    return true;
                default:
                    seat = new SeatOrdinal(seat3);
                    sequence = sequence3;
                    return true;
            }
        }

        /// <summary>The candidate seats in slot order, for the pure contest resolver.</summary>
        public List<SeatOrdinal> CandidateSeats()
        {
            var seats = new List<SeatOrdinal>(CandidateCount);
            for (int i = 0; i < CandidateCount; i++)
            {
                if (TryCandidate(i, out SeatOrdinal seat, out ulong _))
                {
                    seats.Add(seat);
                }
            }

            return seats;
        }

        /// <summary>The candidate bid sequences in slot order, paired with <see cref="CandidateSeats"/>.</summary>
        public List<ulong> CandidateSequences()
        {
            var sequences = new List<ulong>(CandidateCount);
            for (int i = 0; i < CandidateCount; i++)
            {
                if (TryCandidate(i, out SeatOrdinal _, out ulong sequence))
                {
                    sequences.Add(sequence);
                }
            }

            return sequences;
        }

        /// <summary>The pure contest request this envelope describes (07 s2.3, P-037).</summary>
        public CardContestRequest ToContestRequest() =>
            new CardContestRequest(
                BatchId,
                ExpectedTableVersion,
                ContestedCard,
                new SeatOrdinal(Holder),
                CandidateSeats(),
                CandidateSequences());

        /// <summary>One-line diagnostic form; never an identity (P-004).</summary>
        public override string ToString() =>
            "batch(" + BatchId.ToString(CultureInfo.InvariantCulture)
            + ",card=" + ContestedCard.ToString()
            + ",holder=" + Holder.ToString(CultureInfo.InvariantCulture)
            + ",candidates=" + CandidateCount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// Canonical codec of the two card payloads: fixed-width big-endian scalars, the 05 s6 wire convention.
    /// Writing and reading are the same rule, so a payload this fixture writes is decoded by the reader below
    /// without a second interpretation (P-042, 04 s8).
    /// </summary>
    public static class CardPayloadCodec
    {
        /// <summary>Bytes of one ordinary command payload: four scalars and three card identities.</summary>
        public const int CommandBytes = 40;

        /// <summary>
        /// Bytes of one batch envelope: a 24-byte header, the 4-byte candidate count and four 12-byte paired
        /// candidate slots (24 + 4 + 4 * 12 = 76).
        /// </summary>
        public const int BatchBytes = 76;

        /// <summary>Bytes of one candidate slot inside a batch envelope: a 4-byte ordinal and an 8-byte sequence.</summary>
        public const int CandidateSlotBytes = 12;

        /// <summary>First byte offset of the batch envelope's candidate slots.</summary>
        public const int BatchCandidateOffset = 24;


        /// <summary>Encodes one ordinary command payload as a canonical frozen payload.</summary>
        public static FrozenPayload WriteCommand(CardCommandPayload command)
        {
            var bytes = new byte[CommandBytes];
            WriteUInt32(bytes, 0, (uint)command.Kind);
            WriteUInt32(bytes, 4, command.Seat);
            WriteUInt32(bytes, 8, command.Counterparty);
            WriteUInt32(bytes, 12, command.ExpectedTableVersion);
            WriteUInt64(bytes, 16, command.Card0.Value);
            WriteUInt64(bytes, 24, command.Card1.Value);
            WriteUInt64(bytes, 32, command.Card2.Value);
            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one ordinary command payload; false reports a malformed or truncated payload.</summary>
        public static bool TryReadCommand(IReadOnlyList<byte>? payload, out CardCommandPayload command)
        {
            command = default(CardCommandPayload);
            if (payload == null || payload.Count != CommandBytes)
            {
                return false;
            }

            var kind = (CardCommandKind)ReadUInt32(payload, 0);
            if (!IsDeclaredKind(kind))
            {
                return false;
            }

            command = new CardCommandPayload(
                kind,
                ReadUInt32(payload, 4),
                ReadUInt32(payload, 8),
                ReadUInt32(payload, 12),
                new CardId(ReadUInt64(payload, 16)),
                new CardId(ReadUInt64(payload, 24)),
                new CardId(ReadUInt64(payload, 32)));
            return true;
        }

        /// <summary>Encodes one batch envelope as a canonical frozen payload.</summary>
        public static FrozenPayload WriteBatch(CardBatchPayload batch)
        {
            var bytes = new byte[BatchBytes];
            WriteUInt64(bytes, 0, batch.BatchId);
            WriteUInt64(bytes, 8, batch.ContestedCard.Value);
            WriteUInt32(bytes, 16, batch.Holder);
            WriteUInt32(bytes, 20, batch.ExpectedTableVersion);
            WriteUInt32(bytes, 24, batch.CandidateCount);
            for (int i = 0; i < 4; i++)
            {
                batch.TryCandidate(i, out SeatOrdinal seat, out ulong sequence);
                int offset = BatchCandidateOffset + 4 + (i * CandidateSlotBytes);
                WriteUInt32(bytes, offset, seat.Value);
                WriteUInt64(bytes, offset + 4, sequence);
            }

            return new FrozenPayload(bytes);
        }

        /// <summary>Decodes one batch envelope; false reports a malformed, truncated or oversized payload.</summary>
        public static bool TryReadBatch(IReadOnlyList<byte>? payload, out CardBatchPayload batch)
        {
            batch = default(CardBatchPayload);
            if (payload == null || payload.Count != BatchBytes)
            {
                return false;
            }

            uint candidateCount = ReadUInt32(payload, 24);
            if (candidateCount == 0U || candidateCount > 4U)
            {
                // The batch is bounded by four paired candidates; a larger set rejects instead of truncating (P-043).
                return false;
            }

            uint[] seats = new uint[4];
            ulong[] sequences = new ulong[4];
            for (int i = 0; i < 4; i++)
            {
                int offset = BatchCandidateOffset + 4 + (i * CandidateSlotBytes);
                seats[i] = ReadUInt32(payload, offset);
                sequences[i] = ReadUInt64(payload, offset + 4);
            }

            batch = new CardBatchPayload(
                ReadUInt64(payload, 0),
                new CardId(ReadUInt64(payload, 8)),
                ReadUInt32(payload, 16),
                ReadUInt32(payload, 20),
                (byte)candidateCount,
                seats[0],
                sequences[0],
                seats[1],
                sequences[1],
                seats[2],
                sequences[2],
                seats[3],
                sequences[3]);
            return true;
        }

        /// <summary>True when the value is one of the declared command kinds.</summary>
        public static bool IsDeclaredKind(CardCommandKind kind) =>
            kind == CardCommandKind.SubmitSet || kind == CardCommandKind.Transfer || kind == CardCommandKind.Contest;

        private static void WriteUInt32(byte[] bytes, int offset, uint value)
        {
            bytes[offset] = (byte)(value >> 24);
            bytes[offset + 1] = (byte)(value >> 16);
            bytes[offset + 2] = (byte)(value >> 8);
            bytes[offset + 3] = (byte)value;
        }

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

        private static ulong ReadUInt64(IReadOnlyList<byte> bytes, int offset)
            => ((ulong)ReadUInt32(bytes, offset) << 32) | ReadUInt32(bytes, offset + 4);

    }

    /// <summary>
    /// Hand-written reader of the ordinary command payload, bound to its schema. It is the generated-reader
    /// shape of 04 s8: a direct constructor reference, no reflection, and a malformed payload refused rather
    /// than defaulted.
    /// </summary>
    public sealed class CardCommandReader : ICommandPayloadReader<CardCommandPayload>
    {
        /// <inheritdoc />
        public SchemaRef Schema => CardTableKeys.CommandSchema;

        /// <inheritdoc />
        public CardCommandPayload Read(IReadOnlyList<byte> payload)
        {
            if (!CardPayloadCodec.TryReadCommand(payload, out CardCommandPayload command))
            {
                throw new ArgumentException(
                    "the card command payload is exactly " + CardPayloadCodec.CommandBytes.ToString(CultureInfo.InvariantCulture)
                    + " canonical big-endian bytes naming a declared command kind.",
                    nameof(payload));
            }

            return command;
        }
    }

    /// <summary>Hand-written reader of the atomic batch envelope, bound to its schema (04 s8).</summary>
    public sealed class CardBatchReader : ICommandPayloadReader<CardBatchPayload>
    {
        /// <inheritdoc />
        public SchemaRef Schema => CardTableKeys.BatchSchema;

        /// <inheritdoc />
        public CardBatchPayload Read(IReadOnlyList<byte> payload)
        {
            if (!CardPayloadCodec.TryReadBatch(payload, out CardBatchPayload batch))
            {
                throw new ArgumentException(
                    "the card batch payload is exactly " + CardPayloadCodec.BatchBytes.ToString(CultureInfo.InvariantCulture)
                    + " canonical big-endian bytes carrying one to four paired candidates.",
                    nameof(payload));
            }

            return batch;
        }
    }
}
