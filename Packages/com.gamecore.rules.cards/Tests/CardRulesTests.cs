// GameCore.Rules.Cards tests — the settlement rules of the card table (GC-011).
//
// These are the deterministic rule cases of 07 s2 and s10.4: the exact write order of a committed command, the
// all-or-nothing property of every rejection, the documented arithmetic, and the order-independence of a contest
// over all 24 permutations of four candidates. Nothing here reads a clock, a random source, a dictionary order or
// the file system, so the same cases run under NUnit in the Editor and under plain dotnet.
#nullable enable
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameCore.Rules.Cards.Tests
{
    [TestFixture]
    public sealed class CardRulesTests
    {
        private const uint TableVersion = 3U;
        private const uint ActiveSeat = 1U;

        private static readonly SeatOrdinal SeatOne = new SeatOrdinal(1U);
        private static readonly SeatOrdinal SeatTwo = new SeatOrdinal(2U);
        private static readonly SeatOrdinal SeatNine = new SeatOrdinal(9U);

        private static readonly CardId Alpha = new CardId(11UL);
        private static readonly CardId Beta = new CardId(22UL);
        private static readonly CardId Gamma = new CardId(33UL);
        private static readonly CardId Delta = new CardId(44UL);
        private static readonly CardId Epsilon = new CardId(55UL);
        private static readonly CardId Absent = new CardId(99UL);

        private static readonly CardId Contested = new CardId(77UL);

        [Test]
        public void SubmitSetCommitsRemovalsInAscendingCardOrderThenScoreThenAdvance()
        {
            CardHand hand = Hand(SeatOne, Gamma, Alpha, Beta, Absent);
            CardSettlementRequest request = Submit(SeatOne, TableVersion, new[] { Beta, Gamma, Alpha });

            bool committed = CardSetRules.TryBuildSettlement(
                request,
                ActiveSeat,
                TableVersion,
                hand,
                default(CardHand),
                2,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.True);
            Assert.That(status, Is.EqualTo(CardSettlementStatus.Committed));
            Assert.That(writes.Count, Is.EqualTo(5), "Three removals, one score and one table advance.");
            Assert.That(writes.MaxEntries, Is.EqualTo(CardSetRules.MaxWriteEntries));

            Assert.That(writes.Write(0).Kind, Is.EqualTo(CardWriteKind.RemoveCard));
            Assert.That(writes.Write(0).Card, Is.EqualTo(Alpha), "Removals are written in ascending card order.");
            Assert.That(writes.Write(0).Seat, Is.EqualTo(SeatOne));
            Assert.That(writes.Write(1).Kind, Is.EqualTo(CardWriteKind.RemoveCard));
            Assert.That(writes.Write(1).Card, Is.EqualTo(Beta));
            Assert.That(writes.Write(2).Kind, Is.EqualTo(CardWriteKind.RemoveCard));
            Assert.That(writes.Write(2).Card, Is.EqualTo(Gamma));

            Assert.That(writes.Write(3).Kind, Is.EqualTo(CardWriteKind.AddScore));
            Assert.That(writes.Write(3).Seat, Is.EqualTo(SeatOne));
            Assert.That(writes.Write(3).Amount, Is.EqualTo(12), "10 base plus the +2 festival bonus.");
            Assert.That(writes.Write(3).Amount, Is.EqualTo(CardSetRules.SetScoreDelta(2)));
            Assert.That(writes.Write(3).Card.IsNone, Is.True);

            Assert.That(writes.Write(4).Kind, Is.EqualTo(CardWriteKind.AdvanceTable));
            Assert.That(writes.Write(4).Seat, Is.EqualTo(SeatOne));
            Assert.That(writes.Write(4).Amount, Is.EqualTo(1), "One committed command advances one table version.");

            Assert.That(writes.ContainsCard(Beta), Is.True);
            Assert.That(writes.ContainsCard(Absent), Is.False);
            Assert.That(
                writes.Write(0).ToString(),
                Is.EqualTo("RemoveCard(seat=1,card=000000000000000b,amount=0)"));
            Assert.That(writes.Describe(), Does.StartWith("writes=5[RemoveCard"));

            Assert.That(hand.Count, Is.EqualTo(4), "The settlement never mutates the hand it was checked against.");
            Assert.That(hand.Holds(Beta), Is.True);
        }

        [Test]
        public void TransferCommitsRemoveThenAddThenAdvance()
        {
            CardHand hand = Hand(SeatOne, Alpha, Beta);
            CardHand receiver = Hand(SeatTwo, Gamma);
            CardSettlementRequest request = Transfer(SeatOne, SeatTwo, TableVersion, new[] { Alpha });

            bool committed = CardSetRules.TryBuildSettlement(
                request,
                ActiveSeat,
                TableVersion,
                hand,
                receiver,
                0,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.True);
            Assert.That(status, Is.EqualTo(CardSettlementStatus.Committed));
            Assert.That(writes.Count, Is.EqualTo(3));

            Assert.That(writes.Write(0).Kind, Is.EqualTo(CardWriteKind.RemoveCard));
            Assert.That(writes.Write(0).Seat, Is.EqualTo(SeatOne));
            Assert.That(writes.Write(0).Card, Is.EqualTo(Alpha));
            Assert.That(writes.Write(1).Kind, Is.EqualTo(CardWriteKind.AddCard));
            Assert.That(writes.Write(1).Seat, Is.EqualTo(SeatTwo));
            Assert.That(writes.Write(1).Card, Is.EqualTo(Alpha));
            Assert.That(writes.Write(2).Kind, Is.EqualTo(CardWriteKind.AdvanceTable));
            Assert.That(writes.Write(2).Seat, Is.EqualTo(SeatOne));
            Assert.That(writes.Describe(), Does.StartWith("writes=3[RemoveCard"));
            Assert.That(writes.ContainsCard(Alpha), Is.True);

            Assert.That(hand.Count, Is.EqualTo(2));
            Assert.That(receiver.Count, Is.EqualTo(1));
        }

        [Test]
        public void EveryRejectedSettlementWritesNothing()
        {
            CardHand hand = Hand(SeatOne, Alpha, Beta, Gamma);

            void Rejects(
                string name,
                CardSettlementRequest request,
                uint activeSeat,
                uint version,
                CardHand subject,
                CardHand other,
                CardSettlementStatus expected)
            {
                bool committed = CardSetRules.TryBuildSettlement(
                    request, activeSeat, version, subject, other, 2, out CardWriteSet writes, out CardSettlementStatus status);

                Assert.That(committed, Is.False, name);
                Assert.That(status, Is.EqualTo(expected), name);
                Assert.That(writes.Count, Is.EqualTo(0), name + ": a rejection writes nothing (07 s10.4).");
                Assert.That(writes, Is.SameAs(CardWriteSet.Empty), name + ": the canonical empty set is returned.");
                Assert.That(hand.Count, Is.EqualTo(3), name + ": the hand is never mutated.");
                Assert.That(hand.Holds(Alpha), Is.True, name);
            }

            Rejects(
                "stale table version",
                Submit(SeatOne, TableVersion + 1U, new[] { Alpha, Beta, Gamma }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedStaleTableVersion);
            Rejects(
                "hand of another seat",
                Submit(SeatTwo, TableVersion, new[] { Alpha, Beta, Gamma }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedUnknownSeat);
            Rejects(
                "too few candidates",
                Submit(SeatOne, TableVersion, new[] { Alpha, Beta }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedEmptyCandidateSet);
            Rejects(
                "no candidate at all",
                Submit(SeatOne, TableVersion, Array.Empty<CardId>()),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedEmptyCandidateSet);
            Rejects(
                "invalid card identity",
                Submit(SeatOne, TableVersion, new[] { Alpha, Beta, default(CardId) }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedEmptyCandidateSet);
            Rejects(
                "duplicated candidate",
                Submit(SeatOne, TableVersion, new[] { Alpha, Alpha, Beta }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedDuplicateCard);
            Rejects(
                "card the seat does not hold",
                Submit(SeatOne, TableVersion, new[] { Alpha, Beta, Absent }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedCardNotHeld);
            Rejects(
                "out of turn",
                Submit(SeatOne, TableVersion, new[] { Alpha, Beta, Gamma }),
                2U, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedWrongTurn);
            Rejects(
                "more candidates than the bound allows",
                Submit(SeatOne, TableVersion, new[] { Alpha, Beta, Gamma, Delta, Epsilon }),
                ActiveSeat, TableVersion, hand, default(CardHand),
                CardSettlementStatus.RejectedBoundedOverflow);

            Rejects(
                "transfer without a candidate",
                Transfer(SeatOne, SeatTwo, TableVersion, Array.Empty<CardId>()),
                ActiveSeat, TableVersion, hand, Hand(SeatTwo, Delta),
                CardSettlementStatus.RejectedEmptyCandidateSet);
            Rejects(
                "transfer with several candidates",
                Transfer(SeatOne, SeatTwo, TableVersion, new[] { Alpha, Beta }),
                ActiveSeat, TableVersion, hand, Hand(SeatTwo, Delta),
                CardSettlementStatus.RejectedDuplicateCard);
            Rejects(
                "transfer of a card the seat does not hold",
                Transfer(SeatOne, SeatTwo, TableVersion, new[] { Absent }),
                ActiveSeat, TableVersion, hand, Hand(SeatTwo, Delta),
                CardSettlementStatus.RejectedCardNotHeld);
            Rejects(
                "transfer addressed to the issuing seat",
                Transfer(SeatOne, SeatOne, TableVersion, new[] { Alpha }),
                ActiveSeat, TableVersion, hand, hand,
                CardSettlementStatus.RejectedUnknownSeat);
            Rejects(
                "transfer into a full hand",
                Transfer(SeatOne, SeatTwo, TableVersion, new[] { Alpha }),
                ActiveSeat, TableVersion, hand, FullHand(SeatTwo),
                CardSettlementStatus.RejectedSeatFull);
            Rejects(
                "contest settled as if it were one hand",
                new CardSettlementRequest(
                    CardCommandKind.Contest, 4UL, SeatOne, SeatTwo, TableVersion, new[] { Alpha }),
                ActiveSeat, TableVersion, hand, hand,
                CardSettlementStatus.RejectedEmptyCandidateSet);
        }

        [Test]
        public void TheTableVersionIsCheckedBeforeEverythingElse()
        {
            CardHand hand = Hand(SeatTwo, Beta, Gamma, Delta);

            bool committed = CardSetRules.TryBuildSettlement(
                Submit(SeatOne, TableVersion + 1U, new[] { Beta, Gamma, Delta }),
                ActiveSeat,
                TableVersion,
                hand,
                default(CardHand),
                2,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.False);
            Assert.That(
                status,
                Is.EqualTo(CardSettlementStatus.RejectedStaleTableVersion),
                "A command authored against another table version is reported as stale, not as a seat mismatch.");
            Assert.That(writes.Count, Is.EqualTo(0));
        }

        [Test]
        public void AMalformedSetIsReportedBeforeTheTurnViolation()
        {
            CardHand hand = Hand(SeatOne, Alpha, Beta, Gamma);

            bool committed = CardSetRules.TryBuildSettlement(
                Submit(SeatOne, TableVersion, new[] { Alpha, Alpha, Beta }),
                2U,
                TableVersion,
                hand,
                default(CardHand),
                2,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.False);
            Assert.That(status, Is.EqualTo(CardSettlementStatus.RejectedDuplicateCard));
            Assert.That(writes.Count, Is.EqualTo(0));
        }

        [Test]
        public void TheBoundedCandidateCheckPrecedesTheCommandRules()
        {
            CardHand hand = Hand(SeatOne, Alpha);

            bool committed = CardSetRules.TryBuildSettlement(
                Transfer(SeatOne, SeatTwo, TableVersion, new[] { Absent, Delta, Epsilon, Gamma, Beta }),
                ActiveSeat,
                TableVersion,
                hand,
                Hand(SeatTwo, Delta),
                0,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.False);
            Assert.That(
                status,
                Is.EqualTo(CardSettlementStatus.RejectedBoundedOverflow),
                "More than MaxCandidates candidates is refused before any per-kind rule runs.");
            Assert.That(writes.Count, Is.EqualTo(0));
        }

        [Test]
        public void ASelfAddressedTransferIsRejectedEvenWhenTheHandIsFull()
        {
            CardHand hand = Hand(SeatOne, Alpha);

            bool committed = CardSetRules.TryBuildSettlement(
                Transfer(SeatOne, SeatOne, TableVersion, new[] { Alpha }),
                ActiveSeat,
                TableVersion,
                hand,
                FullHand(SeatOne),
                0,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.False);
            Assert.That(
                status,
                Is.EqualTo(CardSettlementStatus.RejectedUnknownSeat),
                "An invalid receiver is an identity problem, not a capacity problem.");
            Assert.That(writes.Count, Is.EqualTo(0));
        }

        [Test]
        public void ATransferIntoAHandAtCapacityIsAcceptedOnlyBelowTheBound()
        {
            CardHand hand = Hand(SeatOne, Alpha);
            CardHand receiver = Hand(SeatTwo, new CardId[CardSetRules.MaxHandCards - 1]);

            bool committed = CardSetRules.TryBuildSettlement(
                Transfer(SeatOne, SeatTwo, TableVersion, new[] { Alpha }),
                ActiveSeat,
                TableVersion,
                hand,
                receiver,
                0,
                out CardWriteSet writes,
                out CardSettlementStatus status);

            Assert.That(committed, Is.True);
            Assert.That(status, Is.EqualTo(CardSettlementStatus.Committed));
            Assert.That(receiver.Count, Is.EqualTo(CardSetRules.MaxHandCards - 1));
            Assert.That(writes.Write(1).Seat, Is.EqualTo(SeatTwo));
        }

        [Test]
        public void ContestResolutionIsIndependentOfCandidateOrder()
        {
            SeatOrdinal[] seats = { new SeatOrdinal(3U), new SeatOrdinal(1U), new SeatOrdinal(2U), new SeatOrdinal(0U) };
            ulong[] sequences = { 40UL, 30UL, 20UL, 10UL };
            CardHand holder = Hand(SeatNine, Contested);

            List<int[]> orders = Permutations(seats.Length);
            Assert.That(orders.Count, Is.EqualTo(24), "Four candidates have 24 permutations; all of them are checked.");

            for (int p = 0; p < orders.Count; p++)
            {
                int[] order = orders[p];
                List<SeatOrdinal> permutedSeats = new List<SeatOrdinal>(order.Length);
                List<ulong> permutedSequences = new List<ulong>(order.Length);
                for (int i = 0; i < order.Length; i++)
                {
                    permutedSeats.Add(seats[order[i]]);
                    permutedSequences.Add(sequences[order[i]]);
                }

                CardContestRequest request = new CardContestRequest(
                    7UL, TableVersion, Contested, holder.Seat, permutedSeats, permutedSequences);

                bool resolved = CardSetRules.TryResolveContest(
                    request, TableVersion, holder, out CardContestResolution resolution, out CardSettlementStatus status);

                Assert.That(resolved, Is.True, "permutation " + p.ToString());
                Assert.That(status, Is.EqualTo(CardSettlementStatus.Committed), "permutation " + p.ToString());
                Assert.That(resolution.Winner, Is.EqualTo(new SeatOrdinal(0U)), "permutation " + p.ToString());
                Assert.That(resolution.WinningSequence, Is.EqualTo(10UL), "permutation " + p.ToString());
                Assert.That(resolution.CandidateCount, Is.EqualTo(4), "permutation " + p.ToString());
                Assert.That(resolution.Describe(), Is.EqualTo("contest(winner=0,sequence=10,candidates=4)"));
            }
        }

        [Test]
        public void TheContestWinnerIsTheSmallestSeatThenTheSmallestSequence()
        {
            CardHand holder = Hand(SeatNine, Contested);

            CardContestRequest seatDecides = new CardContestRequest(
                1UL,
                TableVersion,
                Contested,
                holder.Seat,
                new[] { new SeatOrdinal(2U), new SeatOrdinal(1U) },
                new[] { 1UL, 9UL });

            bool resolved = CardSetRules.TryResolveContest(
                seatDecides, TableVersion, holder, out CardContestResolution resolution, out CardSettlementStatus status);

            Assert.That(resolved, Is.True);
            Assert.That(status, Is.EqualTo(CardSettlementStatus.Committed));
            Assert.That(resolution.Winner, Is.EqualTo(new SeatOrdinal(1U)), "The seat ordinal is compared first.");
            Assert.That(resolution.WinningSequence, Is.EqualTo(9UL));

            CardContestRequest sequenceDecides = new CardContestRequest(
                2UL,
                TableVersion,
                Contested,
                holder.Seat,
                new[] { new SeatOrdinal(2U), new SeatOrdinal(2U) },
                new[] { 7UL, 3UL });

            bool tied = CardSetRules.TryResolveContest(
                sequenceDecides, TableVersion, holder, out CardContestResolution tieResolution, out CardSettlementStatus tieStatus);

            Assert.That(tied, Is.True);
            Assert.That(tieStatus, Is.EqualTo(CardSettlementStatus.Committed));
            Assert.That(tieResolution.Winner, Is.EqualTo(new SeatOrdinal(2U)));
            Assert.That(tieResolution.WinningSequence, Is.EqualTo(3UL), "A tie on seat is broken by sequence.");
        }

        [Test]
        public void AContestWithoutPairedCandidatesIsRejected()
        {
            CardHand holder = Hand(SeatNine, Contested);
            CardContestRequest request = new CardContestRequest(
                1UL,
                TableVersion,
                Contested,
                holder.Seat,
                new[] { SeatOne, SeatTwo },
                new[] { 1UL });

            Assert.That(request.CandidateCount, Is.EqualTo(1), "Only paired entries are candidates.");
            Assert.That(request.TryCandidate(1, out SeatOrdinal _, out ulong _), Is.False);
            Assert.That(
                request.TryCandidate(0, out SeatOrdinal seat, out ulong sequence),
                Is.True);
            Assert.That(seat, Is.EqualTo(SeatOne));
            Assert.That(sequence, Is.EqualTo(1UL));

            CardContestRequest empty = new CardContestRequest(
                2UL, TableVersion, Contested, holder.Seat, null, null);
            Assert.That(empty.CandidateCount, Is.EqualTo(0));

            bool resolved = CardSetRules.TryResolveContest(
                empty, TableVersion, holder, out CardContestResolution resolution, out CardSettlementStatus status);

            Assert.That(resolved, Is.False);
            Assert.That(status, Is.EqualTo(CardSettlementStatus.RejectedEmptyCandidateSet));
            Assert.That(resolution.Winner, Is.EqualTo(default(SeatOrdinal)), "A rejection returns a default resolution.");
            Assert.That(resolution.CandidateCount, Is.EqualTo(0));
        }

        [Test]
        public void ContestRejectionsAreReportedInOrder()
        {
            CardHand holder = Hand(SeatNine, Contested);
            CardHand withoutTheCard = Hand(SeatNine, Delta);
            CardContestRequest request = new CardContestRequest(
                1UL, TableVersion, Contested, holder.Seat, new[] { SeatOne }, new[] { 1UL });

            Assert.That(
                CardSetRules.TryResolveContest(
                    request, TableVersion + 1U, holder, out CardContestResolution _, out CardSettlementStatus stale),
                Is.False);
            Assert.That(stale, Is.EqualTo(CardSettlementStatus.RejectedStaleTableVersion));

            Assert.That(
                CardSetRules.TryResolveContest(
                    request, TableVersion, withoutTheCard, out CardContestResolution _, out CardSettlementStatus notHeld),
                Is.False);
            Assert.That(notHeld, Is.EqualTo(CardSettlementStatus.RejectedCardNotHeld));

            CardContestRequest overBound = new CardContestRequest(
                2UL,
                TableVersion,
                Contested,
                holder.Seat,
                new[] { SeatOne, SeatTwo, SeatNine, SeatOne, SeatTwo },
                new[] { 1UL, 2UL, 3UL, 4UL, 5UL });

            Assert.That(
                CardSetRules.TryResolveContest(
                    overBound, TableVersion, holder, out CardContestResolution _, out CardSettlementStatus overflow),
                Is.False);
            Assert.That(overflow, Is.EqualTo(CardSettlementStatus.RejectedBoundedOverflow));
        }

        [Test]
        public void SetScoreDeltaAddsTheEffectiveBonusAndNeverWraps()
        {
            Assert.That(CardSetRules.SetScoreDelta(0), Is.EqualTo(CardSetRules.BaseSetScore));
            Assert.That(CardSetRules.SetScoreDelta(2), Is.EqualTo(12));
            Assert.That(CardSetRules.SetScoreDelta(3), Is.EqualTo(13));
            Assert.That(CardSetRules.SetScoreDelta(5), Is.EqualTo(15), "A festival under a festival gives +5 (07 s2.1).");
            Assert.That(CardSetRules.SetScoreDelta(-10), Is.EqualTo(0));

            Assert.Throws<OverflowException>(() => CardSetRules.SetScoreDelta(int.MaxValue));
            Assert.Throws<OverflowException>(() => CardSetRules.SetScoreDelta(int.MaxValue - 5));
            Assert.That(CardSetRules.SetScoreDelta(int.MaxValue - CardSetRules.BaseSetScore), Is.EqualTo(int.MaxValue));
        }

        [Test]
        public void TryReduceBonusFoldsTheContributionsInAscendingIndexOrder()
        {
            Assert.That(CardSetRules.TryReduceBonus(null, out int empty), Is.True);
            Assert.That(empty, Is.EqualTo(0), "A missing contribution list reduces to 0.");
            Assert.That(CardSetRules.TryReduceBonus(Array.Empty<int>(), out int none), Is.True);
            Assert.That(none, Is.EqualTo(0));

            Assert.That(CardSetRules.TryReduceBonus(new[] { 2 }, out int single), Is.True);
            Assert.That(single, Is.EqualTo(2), "A single +2 contribution is the festival bonus of 07 s2.1.");
            Assert.That(CardSetRules.TryReduceBonus(new[] { 2, 3 }, out int festival), Is.True);
            Assert.That(festival, Is.EqualTo(5), "The nested festival adds a third provenance record worth +3.");
            Assert.That(CardSetRules.TryReduceBonus(new[] { 3, 2 }, out int nested), Is.True);
            Assert.That(nested, Is.EqualTo(5));
            Assert.That(CardSetRules.TryReduceBonus(new[] { 2, -1, 3 }, out int mixed), Is.True);
            Assert.That(mixed, Is.EqualTo(4));

            Assert.That(CardSetRules.TryReduceBonus(new[] { int.MaxValue }, out int max), Is.True);
            Assert.That(max, Is.EqualTo(int.MaxValue));

            Assert.That(CardSetRules.TryReduceBonus(new[] { int.MaxValue, 1 }, out int overflowed), Is.False);
            Assert.That(overflowed, Is.EqualTo(0), "An overflowing fold exposes no partial sum.");
            Assert.That(CardSetRules.TryReduceBonus(new[] { 1, int.MaxValue }, out int overflowedLate), Is.False);
            Assert.That(overflowedLate, Is.EqualTo(0));
            Assert.That(CardSetRules.TryReduceBonus(new[] { -1, int.MinValue }, out int underflowed), Is.False);
            Assert.That(underflowed, Is.EqualTo(0));
            Assert.That(CardSetRules.TryReduceBonus(new[] { int.MinValue }, out int min), Is.True);
            Assert.That(min, Is.EqualTo(int.MinValue));
        }

        [Test]
        public void TheDocumentedBoundsAreUnchanged()
        {
            Assert.That(CardSetRules.SetCardCount, Is.EqualTo(3));
            Assert.That(CardSetRules.BaseSetScore, Is.EqualTo(10));
            Assert.That(CardSetRules.MaxHandCards, Is.EqualTo(8));
            Assert.That(CardSetRules.MaxCandidates, Is.EqualTo(4));
            Assert.That(CardSetRules.MaxWriteEntries, Is.EqualTo(12));
        }

        [Test]
        public void TheRegisteredReducerReportsItsKeyAndNameAndDelegates()
        {
            CardSetBonusReducer reducer = new CardSetBonusReducer(CardVocabulary.BonusReducerKey);

            Assert.That(reducer.Key, Is.EqualTo(CardVocabulary.BonusReducerKey));
            Assert.That(reducer.StableName, Is.EqualTo("cards.reducer.int32-sum"));

            Assert.That(reducer.TryReduce(new[] { 2, 3 }, out int effective, out string failure), Is.True);
            Assert.That(effective, Is.EqualTo(5));
            Assert.That(failure, Is.Empty);
            Assert.That(effective, Is.EqualTo(CardSetRules.SetScoreDelta(5) - CardSetRules.BaseSetScore));

            Assert.That(reducer.TryReduce(new[] { int.MaxValue, 1 }, out int overflowed, out string overflowFailure), Is.False);
            Assert.That(overflowed, Is.EqualTo(0));
            Assert.That(overflowFailure, Is.Not.Empty);
            Assert.That(overflowFailure, Does.StartWith("cards.reducer.int32-sum"));

            Assert.That(reducer.TryReduce(null, out int none, out string noFailure), Is.True);
            Assert.That(none, Is.EqualTo(0));
            Assert.That(noFailure, Is.Empty);
        }

        [Test]
        public void TheAlwaysPredicateReportsItsKeyAndNameAndAcceptsEveryTarget()
        {
            CardAlwaysPredicate predicate = new CardAlwaysPredicate(CardVocabulary.AlwaysPredicateKey);

            Assert.That(predicate.Key, Is.EqualTo(CardVocabulary.AlwaysPredicateKey));
            Assert.That(predicate.StableName, Is.EqualTo("cards.predicate.always"));
            Assert.That(predicate.IsMatch(null), Is.True, "A null tag list is accepted, so no seat is skipped.");
            Assert.That(predicate.IsMatch(new[] { "card-seat" }), Is.True);
            Assert.That(predicate.IsMatch(Array.Empty<string>()), Is.True);
        }

        [Test]
        public void CardHandToleratesNullAndReportsMembership()
        {
            CardHand empty = new CardHand(SeatOne, null);
            Assert.That(empty.Seat, Is.EqualTo(SeatOne));
            Assert.That(empty.Count, Is.EqualTo(0));
            Assert.That(empty.Holds(Alpha), Is.False);
            Assert.That(empty.IndexOf(Alpha), Is.EqualTo(-1));

            CardHand hand = Hand(SeatOne, Beta, Alpha);
            Assert.That(hand.Count, Is.EqualTo(2));
            Assert.That(hand.Card(0), Is.EqualTo(Beta));
            Assert.That(hand.Card(1), Is.EqualTo(Alpha));
            Assert.That(hand.IndexOf(Alpha), Is.EqualTo(1));
            Assert.That(hand.Holds(Alpha), Is.True);
            Assert.That(hand.Holds(Absent), Is.False);
            Assert.That(hand.Holds(default(CardId)), Is.False, "The invalid card is never held.");
            Assert.Throws<ArgumentOutOfRangeException>(() => hand.Card(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => empty.Card(0));

            CardHand unset = default(CardHand);
            Assert.That(unset.Count, Is.EqualTo(0));
            Assert.That(unset.Holds(Alpha), Is.False);
        }

        [Test]
        public void TheEmptyWriteSetIsCanonicalAndBounded()
        {
            Assert.That(CardWriteSet.Empty.Count, Is.EqualTo(0));
            Assert.That(CardWriteSet.Empty.MaxEntries, Is.EqualTo(CardSetRules.MaxWriteEntries));
            Assert.That(CardWriteSet.Empty.Describe(), Is.EqualTo("writes=0[]"));
            Assert.That(CardWriteSet.Empty.ContainsCard(Alpha), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => CardWriteSet.Empty.Write(0));
        }

        private static CardHand Hand(SeatOrdinal seat, params CardId[] cards) => new CardHand(seat, cards);

        private static CardHand FullHand(SeatOrdinal seat)
        {
            CardId[] cards = new CardId[CardSetRules.MaxHandCards];
            for (int i = 0; i < cards.Length; i++)
            {
                cards[i] = new CardId((ulong)(200 + i));
            }

            return new CardHand(seat, cards);
        }

        private static CardSettlementRequest Submit(
            SeatOrdinal seat, uint expectedTableVersion, IReadOnlyList<CardId>? candidates) =>
            new CardSettlementRequest(
                CardCommandKind.SubmitSet, 1UL, seat, default(SeatOrdinal), expectedTableVersion, candidates);

        private static CardSettlementRequest Transfer(
            SeatOrdinal seat, SeatOrdinal counterparty, uint expectedTableVersion, IReadOnlyList<CardId>? candidates) =>
            new CardSettlementRequest(CardCommandKind.Transfer, 1UL, seat, counterparty, expectedTableVersion, candidates);

        private static List<int[]> Permutations(int count)
        {
            int[] current = new int[count];
            for (int i = 0; i < count; i++)
            {
                current[i] = i;
            }

            List<int[]> results = new List<int[]>();
            Permute(current, 0, results);
            return results;
        }

        private static void Permute(int[] current, int index, List<int[]> results)
        {
            if (index == current.Length)
            {
                results.Add((int[])current.Clone());
                return;
            }

            for (int i = index; i < current.Length; i++)
            {
                int swap = current[index];
                current[index] = current[i];
                current[i] = swap;
                Permute(current, index + 1, results);
                swap = current[index];
                current[index] = current[i];
                current[i] = swap;
            }
        }
    }
}
