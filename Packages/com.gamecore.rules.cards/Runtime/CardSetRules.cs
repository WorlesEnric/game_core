// GameCore.Rules.Cards — the pure settlement rules of the card table (GC-011).
//
// The card table is one owner that settles several entities atomically (07 s2.2: "The table entity and seat
// entities span several scopes. One CardTableRuntime instance therefore owns multiple ECS entities"). A
// settlement therefore never writes as it goes: it validates, builds one bounded write set, and either commits
// that whole set or returns CardWriteSet.Empty.
//
// The rules here are pure functions of their arguments. They never mutate an input, never read a clock, never
// consult a dictionary order and never allocate an unbounded structure. Every check order below is deliberate
// and documented, because the status a caller sees must be reproducible.
#nullable enable
using System.Collections.Generic;

namespace GameCore.Rules.Cards
{
    /// <summary>The bounded card-table rules: set scoring, bonus reduction, transfer and contest (07 s2).</summary>
    public sealed class CardSetRules
    {
        /// <summary>Cards one accepted set plays; a SubmitSet with a different count is rejected.</summary>
        public const int SetCardCount = 3;

        /// <summary>Score of one played set before the effective bonus (07 s2.3: "10 points per valid set").</summary>
        public const int BaseSetScore = 10;

        /// <summary>Hand capacity; a transfer into a hand already this large is rejected.</summary>
        public const int MaxHandCards = 8;

        /// <summary>Candidate bound of one request: a longer candidate list is refused, not truncated.</summary>
        public const int MaxCandidates = 4;

        /// <summary>Write bound of one settlement: a larger effect is refused, not truncated (07 s10.4).</summary>
        public const int MaxWriteEntries = 12;

        private CardSetRules()
        {
        }

        /// <summary>
        /// Score delta of one committed set: <see cref="BaseSetScore"/> plus the effective bonus. Overflow
        /// throws, so a score is never silently wrapped (P-005: reject, never wrap).
        /// </summary>
        public static int SetScoreDelta(int effectiveBonus)
        {
            checked
            {
                return BaseSetScore + effectiveBonus;
            }
        }

        /// <summary>
        /// Folds the contribution list in ascending index order into one effective bonus (the registered Int32
        /// reducer of `cards.set-bonus`, 07 s2.1). A null or empty list reduces to 0. False reports an overflow;
        /// <paramref name="effectiveBonus"/> is then 0 and no partial sum is exposed.
        /// </summary>
        public static bool TryReduceBonus(IReadOnlyList<int>? contributions, out int effectiveBonus)
        {
            int total = 0;
            if (contributions != null)
            {
                for (int i = 0; i < contributions.Count; i++)
                {
                    int contribution = contributions[i];
                    if (contribution > 0)
                    {
                        if (total > int.MaxValue - contribution)
                        {
                            effectiveBonus = 0;
                            return false;
                        }
                    }
                    else if (contribution < 0 && total < int.MinValue - contribution)
                    {
                        effectiveBonus = 0;
                        return false;
                    }

                    total += contribution;
                }
            }

            effectiveBonus = total;
            return true;
        }

        /// <summary>
        /// Resolves a contest to the candidate with the smallest (<see cref="SeatOrdinal"/>, sequence) ascending
        /// total order, so the winner is independent of candidate array order; a tie on seat is broken by
        /// sequence. Checks, in order: committed table version, at least one paired candidate, the candidate
        /// bound, and the holder's hand still holding the contested card. A rejection returns a default
        /// resolution and changes nothing.
        /// </summary>
        public static bool TryResolveContest(
            CardContestRequest request,
            uint tableVersion,
            CardHand holderHand,
            out CardContestResolution resolution,
            out CardSettlementStatus status)
        {
            resolution = default(CardContestResolution);
            if (request.ExpectedTableVersion != tableVersion)
            {
                status = CardSettlementStatus.RejectedStaleTableVersion;
                return false;
            }

            if (request.CandidateCount < 1)
            {
                status = CardSettlementStatus.RejectedEmptyCandidateSet;
                return false;
            }

            if (request.CandidateCount > MaxCandidates)
            {
                status = CardSettlementStatus.RejectedBoundedOverflow;
                return false;
            }

            if (!holderHand.Holds(request.ContestedCard))
            {
                status = CardSettlementStatus.RejectedCardNotHeld;
                return false;
            }

            SeatOrdinal winner = default(SeatOrdinal);
            ulong winningSequence = 0UL;
            for (int i = 0; i < request.CandidateCount; i++)
            {
                if (request.TryCandidate(i, out SeatOrdinal seat, out ulong sequence)
                    && (i == 0 || IsEarlier(seat, sequence, winner, winningSequence)))
                {
                    winner = seat;
                    winningSequence = sequence;
                }
            }

            resolution = new CardContestResolution(winner, winningSequence, request.CandidateCount);
            status = CardSettlementStatus.Committed;
            return true;
        }

        /// <summary>
        /// Builds the write set of one command against the committed table version. Checks, in order: the
        /// expected table version, the hand's seat matching the request's seat, the candidate bound, and then the
        /// command's own rules. A rejection returns <see cref="CardWriteSet.Empty"/> and a status; a commit
        /// returns the whole effect as one set, so the score and the cards move together or not at all.
        /// <see cref="CardCommandKind.Contest"/> is never settled here — it is resolved by
        /// <see cref="TryResolveContest"/>, which is why it reports an empty candidate set.
        /// Throws <see cref="System.OverflowException"/> through <see cref="SetScoreDelta"/> when the effective
        /// bonus overflows <see cref="int"/>.
        /// </summary>
        public static bool TryBuildSettlement(
            CardSettlementRequest request,
            uint activeSeatOrdinal,
            uint tableVersion,
            CardHand hand,
            CardHand counterpartyHand,
            int effectiveBonus,
            out CardWriteSet writes,
            out CardSettlementStatus status)
        {
            writes = CardWriteSet.Empty;
            if (request.ExpectedTableVersion != tableVersion)
            {
                status = CardSettlementStatus.RejectedStaleTableVersion;
                return false;
            }

            if (!hand.Seat.Equals(request.Seat))
            {
                status = CardSettlementStatus.RejectedUnknownSeat;
                return false;
            }

            if (request.CandidateCount > MaxCandidates)
            {
                status = CardSettlementStatus.RejectedBoundedOverflow;
                return false;
            }

            switch (request.Kind)
            {
                case CardCommandKind.SubmitSet:
                    return TryBuildSubmitSet(
                        request, activeSeatOrdinal, hand, effectiveBonus, out writes, out status);

                case CardCommandKind.Transfer:
                    return TryBuildTransfer(request, hand, counterpartyHand, out writes, out status);

                default:
                    status = CardSettlementStatus.RejectedEmptyCandidateSet;
                    return false;
            }
        }

        private static bool IsEarlier(SeatOrdinal seat, ulong sequence, SeatOrdinal otherSeat, ulong otherSequence)
        {
            int bySeat = seat.CompareTo(otherSeat);
            if (bySeat != 0)
            {
                return bySeat < 0;
            }

            return sequence < otherSequence;
        }

        /// <summary>
        /// A SubmitSet plays exactly <see cref="SetCardCount"/> distinct, held cards: a count mismatch, an invalid
        /// card and a duplicate each have their own status, then the seat's turn is checked last because a
        /// malformed set is reported before a turn violation. The committed set is the removals in ascending card
        /// order, then the score, then the table advance — all in one write set.
        /// </summary>
        private static bool TryBuildSubmitSet(
            CardSettlementRequest request,
            uint activeSeatOrdinal,
            CardHand hand,
            int effectiveBonus,
            out CardWriteSet writes,
            out CardSettlementStatus status)
        {
            writes = CardWriteSet.Empty;
            int count = request.CandidateCount;
            if (count < SetCardCount)
            {
                status = CardSettlementStatus.RejectedEmptyCandidateSet;
                return false;
            }

            if (count > SetCardCount)
            {
                status = CardSettlementStatus.RejectedDuplicateCard;
                return false;
            }

            // count == SetCardCount and CandidateCount is derived from the same list TryCandidate reads, so every
            // index below is present; the validation reads earlier candidates again instead of copying them.
            for (int i = 0; i < SetCardCount; i++)
            {
                request.TryCandidate(i, out CardId candidate);
                if (candidate.IsNone)
                {
                    status = CardSettlementStatus.RejectedEmptyCandidateSet;
                    return false;
                }

                for (int j = 0; j < i; j++)
                {
                    request.TryCandidate(j, out CardId earlier);
                    if (earlier.Equals(candidate))
                    {
                        status = CardSettlementStatus.RejectedDuplicateCard;
                        return false;
                    }
                }

                if (!hand.Holds(candidate))
                {
                    status = CardSettlementStatus.RejectedCardNotHeld;
                    return false;
                }
            }

            if (request.Seat.Value != activeSeatOrdinal)
            {
                status = CardSettlementStatus.RejectedWrongTurn;
                return false;
            }

            CardWrite[] effect = new CardWrite[SetCardCount + 2];
            for (int i = 0; i < SetCardCount; i++)
            {
                request.TryCandidate(i, out CardId candidate);
                effect[i] = new CardWrite(CardWriteKind.RemoveCard, request.Seat, candidate, 0);
            }

            effect[SetCardCount] = new CardWrite(
                CardWriteKind.AddScore, request.Seat, default(CardId), SetScoreDelta(effectiveBonus));
            effect[SetCardCount + 1] = new CardWrite(
                CardWriteKind.AdvanceTable, request.Seat, default(CardId), 1);
            return Finish(effect, out writes, out status);
        }

        /// <summary>
        /// A Transfer moves exactly one held card. The seat's own seat is not a legal receiver, and that identity
        /// check precedes the capacity check so a self-addressed request is reported as an unknown seat rather
        /// than as a full hand. The committed set is remove, add, advance.
        /// </summary>
        private static bool TryBuildTransfer(
            CardSettlementRequest request,
            CardHand hand,
            CardHand counterpartyHand,
            out CardWriteSet writes,
            out CardSettlementStatus status)
        {
            writes = CardWriteSet.Empty;
            int count = request.CandidateCount;
            if (count < 1)
            {
                status = CardSettlementStatus.RejectedEmptyCandidateSet;
                return false;
            }

            if (count > 1)
            {
                status = CardSettlementStatus.RejectedDuplicateCard;
                return false;
            }

            if (request.Counterparty.Equals(request.Seat))
            {
                status = CardSettlementStatus.RejectedUnknownSeat;
                return false;
            }

            request.TryCandidate(0, out CardId card);
            if (!hand.Holds(card))
            {
                status = CardSettlementStatus.RejectedCardNotHeld;
                return false;
            }

            if (counterpartyHand.Count >= MaxHandCards)
            {
                status = CardSettlementStatus.RejectedSeatFull;
                return false;
            }

            CardWrite[] effect = new CardWrite[3];
            effect[0] = new CardWrite(CardWriteKind.RemoveCard, request.Seat, card, 0);
            effect[1] = new CardWrite(CardWriteKind.AddCard, request.Counterparty, card, 0);
            effect[2] = new CardWrite(CardWriteKind.AdvanceTable, request.Seat, default(CardId), 1);
            return Finish(effect, out writes, out status);
        }

        /// <summary>
        /// Canonicalizes the effect into a write set. An effect larger than <see cref="MaxWriteEntries"/> rejects
        /// with <see cref="CardSettlementStatus.RejectedBoundedOverflow"/> and writes nothing.
        /// </summary>
        private static bool Finish(CardWrite[] effect, out CardWriteSet writes, out CardSettlementStatus status)
        {
            if (CardWriteSet.TryCreate(effect, MaxWriteEntries, out CardWriteSet canonical))
            {
                writes = canonical;
                status = CardSettlementStatus.Committed;
                return true;
            }

            writes = CardWriteSet.Empty;
            status = CardSettlementStatus.RejectedBoundedOverflow;
            return false;
        }
    }
}
