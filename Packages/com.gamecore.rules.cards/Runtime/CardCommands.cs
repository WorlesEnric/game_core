// GameCore.Rules.Cards — the card slice's command, write and hand vocabulary (GC-011).
//
// Normative source: docs/game-core/07-reference-compositions.md s2 and s10.4. The card table is one owner that
// settles several entities atomically — `cards.commit` "rechecks table version, reserves output capacity, then
// writes all accepted changes before observation is allowed". Every type here is a pure immutable value: no
// Unity type, no clock, no ambient state, no dictionary order, so the same rules run under Unity and under plain
// dotnet and a permutation of the candidate list cannot change an outcome (P-008).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameCore.Rules.Cards
{
    /// <summary>Which card command authored a settlement request; the rules are selected by this value.</summary>
    public enum CardCommandKind
    {
        /// <summary>Play one set of exactly <see cref="CardSetRules.SetCardCount"/> held cards and score it.</summary>
        SubmitSet = 0,

        /// <summary>Move one held card into another seat's hand.</summary>
        Transfer = 1,

        /// <summary>Contest one card; resolved by <see cref="CardSetRules.TryResolveContest"/>, never by one hand's settlement.</summary>
        Contest = 2,
    }

    /// <summary>
    /// Outcome of one settlement or contest attempt. Only <see cref="Committed"/> carries writes; every rejection
    /// carries <see cref="CardWriteSet.Empty"/>, so a rejected command changes no entity (07 s10.4).
    /// </summary>
    public enum CardSettlementStatus
    {
        /// <summary>The request was accepted; the returned write set is the complete effect of the command.</summary>
        Committed = 0,

        /// <summary>The request's seat is not the seat of the hand it was checked against.</summary>
        RejectedUnknownSeat = 1,

        /// <summary>The seat issued a command outside its turn (07 s2.3).</summary>
        RejectedWrongTurn = 2,

        /// <summary>The candidate set was empty, held an invalid card, or was smaller than the command requires.</summary>
        RejectedEmptyCandidateSet = 3,

        /// <summary>The seat does not hold a candidate card, or a contest's holder does not hold the contested card.</summary>
        RejectedCardNotHeld = 4,

        /// <summary>The candidate set names the same card twice, or a single-card command carried several candidates.</summary>
        RejectedDuplicateCard = 5,

        /// <summary>The request was authored against a different table version than the committed one.</summary>
        RejectedStaleTableVersion = 6,

        /// <summary>The contest was decided against the issuing seat; the wider command lane reports this, not the pure resolver.</summary>
        RejectedContestLost = 7,

        /// <summary>The receiving hand is already at <see cref="CardSetRules.MaxHandCards"/>.</summary>
        RejectedSeatFull = 8,

        /// <summary>The request or its effect exceeds a declared bound, so the work is refused rather than truncated.</summary>
        RejectedBoundedOverflow = 9,
    }

    /// <summary>One write of a committed settlement; the order inside a set is canonical, never declaration order.</summary>
    public enum CardWriteKind
    {
        /// <summary>Remove the write's card from the write's seat hand.</summary>
        RemoveCard = 0,

        /// <summary>Add the write's card to the write's seat hand.</summary>
        AddCard = 1,

        /// <summary>Add <see cref="CardWrite.Amount"/> to the write's seat score.</summary>
        AddScore = 2,

        /// <summary>Advance the table version by <see cref="CardWrite.Amount"/> steps, after every other write.</summary>
        AdvanceTable = 3,
    }

    /// <summary>Identity of one card as the table's buffers store it (07 s2.2); zero is never a card.</summary>
    public readonly struct CardId : IEquatable<CardId>, IComparable<CardId>
    {
        /// <summary>The raw card identity; 0 is the invalid "no card" value.</summary>
        public readonly ulong Value;

        /// <summary>Wraps one raw card identity.</summary>
        public CardId(ulong value)
        {
            Value = value;
        }

        /// <summary>True for the invalid "no card" value; such a card is never a legal candidate.</summary>
        public bool IsNone => Value == 0UL;

        /// <summary>Canonical value order, which is the order card removals are written in.</summary>
        public int CompareTo(CardId other) => Value.CompareTo(other.Value);

        /// <summary>Value equality.</summary>
        public bool Equals(CardId other) => Value == other.Value;

        /// <summary>Value equality against any boxed instance.</summary>
        public override bool Equals(object? obj) => obj is CardId other && Equals(other);

        /// <summary>Hash of the raw value.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Value equality.</summary>
        public static bool operator ==(CardId left, CardId right) => left.Equals(right);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(CardId left, CardId right) => !left.Equals(right);

        /// <summary>Invariant 16-digit lowercase hex, used by the write-set one-liner and by diagnostics.</summary>
        public override string ToString() => Value.ToString("x16", CultureInfo.InvariantCulture);
    }

    /// <summary>Ordinal of one seat within a match; 07 s2.3 orders candidates by seat ordinal.</summary>
    public readonly struct SeatOrdinal : IEquatable<SeatOrdinal>, IComparable<SeatOrdinal>
    {
        /// <summary>The seat's ordinal.</summary>
        public readonly uint Value;

        /// <summary>Wraps one seat ordinal.</summary>
        public SeatOrdinal(uint value)
        {
            Value = value;
        }

        /// <summary>Ascending seat order.</summary>
        public int CompareTo(SeatOrdinal other) => Value.CompareTo(other.Value);

        /// <summary>Value equality.</summary>
        public bool Equals(SeatOrdinal other) => Value == other.Value;

        /// <summary>Value equality against any boxed instance.</summary>
        public override bool Equals(object? obj) => obj is SeatOrdinal other && Equals(other);

        /// <summary>Hash of the ordinal.</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>Value equality.</summary>
        public static bool operator ==(SeatOrdinal left, SeatOrdinal right) => left.Equals(right);

        /// <summary>Value inequality.</summary>
        public static bool operator !=(SeatOrdinal left, SeatOrdinal right) => !left.Equals(right);

        /// <summary>Invariant decimal form, used by diagnostics.</summary>
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>One bounded state write of a settled command; the unit of the all-or-nothing commit (07 s10.4).</summary>
    public readonly struct CardWrite
    {
        /// <summary>What the write does.</summary>
        public readonly CardWriteKind Kind;

        /// <summary>The seat the write acts on: the seat losing or gaining a card, the scoring seat, or the acting seat.</summary>
        public readonly SeatOrdinal Seat;

        /// <summary>The card of a card write; <see cref="CardId.IsNone"/> for score and table-version writes.</summary>
        public readonly CardId Card;

        /// <summary>The score delta of a score write, or the table-version steps of an advance; 0 for card writes.</summary>
        public readonly int Amount;

        /// <summary>Builds one write.</summary>
        public CardWrite(CardWriteKind kind, SeatOrdinal seat, CardId card, int amount)
        {
            Kind = kind;
            Seat = seat;
            Card = card;
            Amount = amount;
        }

        /// <summary>One-line diagnostic form: `Kind(seat=N,card=HEX,amount=N)`.</summary>
        public override string ToString() =>
            Kind.ToString()
            + "(seat=" + Seat.Value.ToString(CultureInfo.InvariantCulture)
            + ",card=" + Card.ToString()
            + ",amount=" + Amount.ToString(CultureInfo.InvariantCulture) + ")";
    }

    /// <summary>
    /// One seat's read-only, bounded hand view. A null card list is an empty hand, and the view is a copy of the
    /// list reference only: a settlement can never mutate the seat's state through it.
    /// </summary>
    public readonly struct CardHand
    {
        private readonly IReadOnlyList<CardId>? _cards;

        /// <summary>Captures one seat and its hand; a null list is an empty hand.</summary>
        public CardHand(SeatOrdinal seat, IReadOnlyList<CardId>? cards)
        {
            Seat = seat;
            _cards = cards;
        }

        /// <summary>The seat that owns this hand.</summary>
        public SeatOrdinal Seat { get; }

        /// <summary>How many cards the hand holds; a committed transfer leaves at most <see cref="CardSetRules.MaxHandCards"/>.</summary>
        public int Count => Cards.Count;

        /// <summary>The card at one index; the index must be inside [0, <see cref="Count"/>).</summary>
        public CardId Card(int index)
        {
            IReadOnlyList<CardId> cards = Cards;
            if ((uint)index >= (uint)cards.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return cards[index];
        }

        /// <summary>True when the hand holds the card; a <see cref="CardId.IsNone"/> card is never held.</summary>
        public bool Holds(CardId card) => !card.IsNone && IndexOf(card) >= 0;

        /// <summary>Index of the card in this hand, or -1 when absent; the first occurrence wins.</summary>
        public int IndexOf(CardId card)
        {
            IReadOnlyList<CardId> cards = Cards;
            for (int i = 0; i < cards.Count; i++)
            {
                if (cards[i].Equals(card))
                {
                    return i;
                }
            }

            return -1;
        }

        private IReadOnlyList<CardId> Cards => _cards ?? Array.Empty<CardId>();
    }

    /// <summary>One seat-issued card command, as the admitted command lane hands it to the settlement rules.</summary>
    public readonly struct CardSettlementRequest
    {
        private readonly IReadOnlyList<CardId>? _candidates;

        /// <summary>Captures one command; a null candidate list is an empty candidate set.</summary>
        public CardSettlementRequest(
            CardCommandKind kind,
            ulong sequence,
            SeatOrdinal seat,
            SeatOrdinal counterparty,
            uint expectedTableVersion,
            IReadOnlyList<CardId>? candidates)
        {
            Kind = kind;
            Sequence = sequence;
            Seat = seat;
            Counterparty = counterparty;
            ExpectedTableVersion = expectedTableVersion;
            _candidates = candidates;
        }

        /// <summary>Which command rules apply to this request.</summary>
        public CardCommandKind Kind { get; }

        /// <summary>The issuer's sequence within the admitted order; used to order contests, not to settle one hand.</summary>
        public ulong Sequence { get; }

        /// <summary>The issuing seat; it must be the seat of the hand the settlement is checked against.</summary>
        public SeatOrdinal Seat { get; }

        /// <summary>The other seat of a transfer, or an unused seat for the other commands.</summary>
        public SeatOrdinal Counterparty { get; }

        /// <summary>The table version the issuer authored against; a mismatch rejects the whole command.</summary>
        public uint ExpectedTableVersion { get; }

        /// <summary>How many candidate cards the request carries; never more than the admitted bound.</summary>
        public int CandidateCount => Candidates.Count;

        /// <summary>Reads one candidate; false when the index is outside the candidate set.</summary>
        public bool TryCandidate(int index, out CardId card)
        {
            IReadOnlyList<CardId> candidates = Candidates;
            if ((uint)index >= (uint)candidates.Count)
            {
                card = default(CardId);
                return false;
            }

            card = candidates[index];
            return true;
        }

        private IReadOnlyList<CardId> Candidates => _candidates ?? Array.Empty<CardId>();
    }

    /// <summary>
    /// One contest: a batch of candidate issuers, each paired with the sequence that bid for the contested card.
    /// The paired seats and sequences are read by index, so the winner never depends on array order.
    /// </summary>
    public readonly struct CardContestRequest
    {
        private readonly IReadOnlyList<SeatOrdinal>? _candidateSeats;
        private readonly IReadOnlyList<ulong>? _candidateSequences;

        /// <summary>Captures one contest; a null list is empty, and only paired entries are candidates.</summary>
        public CardContestRequest(
            ulong batchId,
            uint expectedTableVersion,
            CardId contestedCard,
            SeatOrdinal holder,
            IReadOnlyList<SeatOrdinal>? candidateSeats,
            IReadOnlyList<ulong>? candidateSequences)
        {
            BatchId = batchId;
            ExpectedTableVersion = expectedTableVersion;
            ContestedCard = contestedCard;
            Holder = holder;
            _candidateSeats = candidateSeats;
            _candidateSequences = candidateSequences;
        }

        /// <summary>The admitted batch identity this contest belongs to.</summary>
        public ulong BatchId { get; }

        /// <summary>The table version the batch was authored against; a mismatch rejects the whole contest.</summary>
        public uint ExpectedTableVersion { get; }

        /// <summary>The card under contest; the holder's hand must still hold it.</summary>
        public CardId ContestedCard { get; }

        /// <summary>The seat that held the contested card when the batch was admitted.</summary>
        public SeatOrdinal Holder { get; }

        /// <summary>How many candidates can be resolved: the shorter of the seat and sequence lists.</summary>
        public int CandidateCount
        {
            get
            {
                int seats = Seats.Count;
                int sequences = Sequences.Count;
                return seats < sequences ? seats : sequences;
            }
        }

        /// <summary>Reads one candidate pair; false when the index is outside the paired candidate set.</summary>
        public bool TryCandidate(int index, out SeatOrdinal seat, out ulong sequence)
        {
            if ((uint)index >= (uint)CandidateCount)
            {
                seat = default(SeatOrdinal);
                sequence = 0UL;
                return false;
            }

            seat = Seats[index];
            sequence = Sequences[index];
            return true;
        }

        private IReadOnlyList<SeatOrdinal> Seats => _candidateSeats ?? Array.Empty<SeatOrdinal>();

        private IReadOnlyList<ulong> Sequences => _candidateSequences ?? Array.Empty<ulong>();
    }

    /// <summary>The decided contest: the winning candidate and the number of paired candidates considered.</summary>
    public readonly struct CardContestResolution
    {
        /// <summary>Builds one resolution.</summary>
        public CardContestResolution(SeatOrdinal winner, ulong winningSequence, int candidateCount)
        {
            Winner = winner;
            WinningSequence = winningSequence;
            CandidateCount = candidateCount;
        }

        /// <summary>The winning seat: the smallest seat ordinal, then the smallest sequence.</summary>
        public SeatOrdinal Winner { get; }

        /// <summary>The winning candidate's sequence.</summary>
        public ulong WinningSequence { get; }

        /// <summary>How many paired candidates took part.</summary>
        public int CandidateCount { get; }

        /// <summary>One-line diagnostic form: `contest(winner=N,sequence=N,candidates=N)`.</summary>
        public string Describe() =>
            "contest(winner=" + Winner.Value.ToString(CultureInfo.InvariantCulture)
            + ",sequence=" + WinningSequence.ToString(CultureInfo.InvariantCulture)
            + ",candidates=" + CandidateCount.ToString(CultureInfo.InvariantCulture) + ")";
    }
}
