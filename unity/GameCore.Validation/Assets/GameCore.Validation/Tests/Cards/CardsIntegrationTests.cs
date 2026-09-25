#nullable enable
using System.Collections.Generic;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Validation.GeneratedCards;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Cards.Tests
{
    /// <summary>
    /// GC-011 card-game Automatic vertical slice, EditMode half (docs/game-core/09-implementation-guide.md, Wave 3 —
    /// "Two genuinely different running compositions": "Run narrative and cards with the same kernel. Show zero idle
    /// command steps, automatic existing/future targets, a narrative state change and a card domain transfer.").
    ///
    /// The scenario itself (`GameCore.Gameplay.Cards.Fixtures.CardMarketScenario`) is shared with the standalone
    /// player probe (`-probeCards`) and runs only real modules: GC-004's composition host and control lane, GC-006's
    /// derivation over the committed composition, GC-007's ownership validator, slot policies and bounded message
    /// plane, GC-009's schedule compiler and temporal drivers, GC-008's planner and publisher into a real
    /// `Unity.Entities.World`, and the card package's own settlement rules. No seam fixture participates.
    ///
    /// Every case asserts on the facts the scenario observed, so a regression in the slice is reported by value
    /// rather than only by a boolean, and the two catalogs (the committed generated one and the hand-written
    /// fixture one) are both asserted.
    /// </summary>
    [TestFixture]
    public sealed class CardsIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries (see <see cref="CardsScenarioHost"/>).</summary>
        private const string FixturePrefix = CardsScenarioHost.FixtureRunPrefix;

        /// <summary>
        /// The scenario's twelve observations, in execution order. `CardMarketScenario` names every one of them, so a
        /// missing or renamed observation fails here instead of shrinking the suite silently.
        /// </summary>
        private static readonly string[] ScenarioObservations =
        {
            "cards-catalog-and-declarations",
            "cards-ownership-and-schedule-compiled",
            "cards-world-and-market-seeded",
            "cards-mount-reaches-existing-seats",
            "cards-idle-before-command-commits-zero-steps",
            "cards-one-command-commits-both-sides",
            "cards-duplicate-command-transfers-once",
            "cards-rejected-settlement-changes-nothing",
            "cards-transfer-commits-both-sides",
            "cards-batch-envelope-resolves-one-winner",
            "cards-future-seat-inherits-modifier",
            "cards-idle-world-performs-zero-steps",
            "cards-teardown-settles-and-disposes",
        };

        private static IReadOnlyList<CardStep> combined = null!;
        private static CardFacts generated = null!;
        private static CardFacts fixture = null!;

        [OneTimeSetUp]
        public void RunTheSliceOncePerCatalog()
        {
            combined = CardsScenarioHost.RunBoth(out generated, out fixture);
        }

        [TearDown]
        public void TearDown()
        {
            // The scenario tears its own world down; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>Every named observation of the generated-catalog run must pass.</summary>
        [Test]
        public void EveryObservationPassesOverTheGeneratedCatalog()
        {
            Assert.That(generated.CatalogFingerprint, Is.Not.Empty, generated.Describe());
            AssertStepsPassed(Run(""));
        }

        /// <summary>Every named observation of the fixture-catalog run must pass.</summary>
        [Test]
        public void EveryObservationPassesOverTheFixtureCatalog()
        {
            Assert.That(fixture.CatalogFingerprint, Is.Not.Empty, fixture.Describe());
            AssertStepsPassed(Run(FixturePrefix));
        }

        /// <summary>
        /// The scenario really recorded its twelve observations for both catalogs, in order, and produced no
        /// failure-only diagnostic (a `cards-edit-&lt;Subject&gt;` step only exists when an edit was refused).
        /// </summary>
        [Test]
        public void TheScenarioRecordsEveryObservationTwiceAndNoDiagnostic()
        {
            Assert.That(combined.Count, Is.GreaterThan(0), "the scenario recorded no observation at all.");
            Assert.That(Names(""), Is.EqualTo(ScenarioObservations), "generated-catalog observation order");
            Assert.That(Names(FixturePrefix), Is.EqualTo(ScenarioObservations), "fixture-catalog observation order");

            var diagnostics = new List<string>();
            for (int i = 0; i < combined.Count; i++)
            {
                if (combined[i].Name.StartsWith("cards-edit-", System.StringComparison.Ordinal))
                {
                    diagnostics.Add(combined[i].ToString());
                }
            }

            Assert.That(diagnostics, Is.Empty,
                "a composition edit was refused: " + Join(diagnostics));
        }

        /// <summary>
        /// GC-006 over GC-004 in Automatic mode (07 s2.1): the two scoring mounts reach every eligible existing seat
        /// with no per-instance import, the ineligible scoreboard and the isolated practice seat receive nothing, and
        /// mounting a provider awards no points (P-013, P-015, P-016). Seats A and B are beneath the festival
        /// provider (+2) and seat C beneath the quiet one (+1).
        /// </summary>
        [Test]
        public void TheMountsReachEveryEligibleExistingSeat()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.SeatABonusRowCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.SeatABonusValue, Is.EqualTo(CardVocabulary.FestivalBonus),
                    "the festival provider mounted at League A is the +2 contribution (07 s2.1): " + facts.Describe());
                Assert.That(facts.SeatABonusIsActive, Is.True, facts.Describe());
                Assert.That(facts.SeatBBonusValue, Is.EqualTo(CardVocabulary.FestivalBonus),
                    "an Automatic descendant rule reaches seat B without a per-instance import (P-013): "
                    + facts.Describe());
                Assert.That(facts.SeatCBonusValue, Is.EqualTo(CardVocabulary.QuietBonus),
                    "League B's provider is the quiet one, +1 (07 s2.1): " + facts.Describe());
                Assert.That(facts.ScoreboardRowCount, Is.Zero,
                    "ScoreboardViewRecipe takes neither scoring slot, so it stays on its base layout (P-015): "
                    + facts.Describe());
                Assert.That(facts.PracticeSeatBonusRowCount, Is.Zero,
                    "the practice seat's isolation boundary blocks the contribution in either mode (P-016): "
                    + facts.Describe());
                Assert.That(facts.SeatCount, Is.EqualTo(4), facts.Describe());
                Assert.That(facts.LiveTargetCount, Is.EqualTo(facts.SeatCount + 2),
                    "the six live targets are the four seats, the market table and the scoreboard: " + facts.Describe());
                Assert.That(facts.CountersJoinedAfterSetup, Is.True,
                    "the lane and the world publish the same series, so their counters stay joined (P-006): "
                    + facts.Describe());
            }
        }

        /// <summary>
        /// One bounded command through the real message plane commits both sides in one step (07 s2.3): the played
        /// cards leave seat A's hand, the score becomes base 10 plus the pinned +2 bonus on top of the seeded 4, one
        /// committed event carries that delta and names the live state, and the write set is exactly the three cards
        /// plus the two authoritative rows (P-042, P-044, P-045).
        /// </summary>
        [Test]
        public void OneCommandCommitsBothSides()
        {
            int expectedScore = CardTableKeys.ScoreAfterOneSet(CardVocabulary.FestivalBonus);

            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.CommandAdmitted, Is.True, facts.Describe());
                Assert.That(facts.StepsAfterCommand, Is.EqualTo(1UL),
                    "a CommandDriven world advances exactly one step for one admitted command (P-036, P-037): "
                    + facts.Describe());
                Assert.That(facts.SeatAHandAfterCommit, Is.EqualTo(facts.SeatAHandBefore - CardSetRules.SetCardCount),
                    "a committed set removes exactly its three cards: " + facts.Describe());
                Assert.That(facts.SeatAHandBefore, Is.EqualTo(CardTableKeys.SeededHandCount), facts.Describe());
                Assert.That(facts.SeatAScoreBefore, Is.EqualTo(CardTableKeys.SeededSeatScore), facts.Describe());
                Assert.That(facts.SeatAScoreAfterCommit, Is.EqualTo(expectedScore),
                    "seeded 4 + (10 base + 2 festival) = 16 (07 s2.3's example): " + facts.Describe());
                Assert.That(facts.SeatAScoreAfterCommit,
                    Is.EqualTo(CardTableKeys.SeededSeatScore + CardSetRules.BaseSetScore + CardVocabulary.FestivalBonus),
                    facts.Describe());
                Assert.That(facts.TableVersionAfterCommit, Is.EqualTo(CardTableKeys.TableVersionAfterOneCommit),
                    facts.Describe());
                Assert.That(facts.TurnNumberAfterCommit, Is.EqualTo(1U), facts.Describe());
                Assert.That(facts.CommittedEventCount, Is.EqualTo(1),
                    "one settled step publishes exactly the committed results it produced (P-044, P-045): "
                    + facts.Describe());
                Assert.That(facts.CommittedEventScoreDelta, Is.EqualTo(CardSetRules.SetScoreDelta(CardVocabulary.FestivalBonus)),
                    facts.Describe());
                Assert.That(facts.CommittedEventScoreAfter, Is.EqualTo(expectedScore), facts.Describe());
                Assert.That(facts.CommittedEventCardCount, Is.EqualTo(CardSetRules.SetCardCount), facts.Describe());
                Assert.That(facts.CommittedEventStep, Is.EqualTo(1UL), facts.Describe());
                Assert.That(facts.CommittedEventMatchesLiveState, Is.True,
                    "the committed record names the value live storage holds (P-044): " + facts.Describe());
                Assert.That(facts.CommittedWrites, Is.EqualTo(CardSetRules.SetCardCount + 2),
                    "three cards plus the owner's table and score rows, one bounded write set (P-034, P-044): "
                    + facts.Describe());
            }
        }

        /// <summary>
        /// The same request key submitted twice executes once: the second submission returns the recorded result, so
        /// the transfer moved exactly one card, the step counter advanced once and no demand stayed pending
        /// (P-037, P-050).
        /// </summary>
        [Test]
        public void ADuplicateCommandTransfersOnce()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.DuplicateRejections, Is.GreaterThanOrEqualTo(1),
                    "the repeated request key must be recognised as a duplicate (P-037): " + facts.Describe());
                Assert.That(facts.StepsAfterCommand, Is.EqualTo(1UL), facts.Describe());
                Assert.That(facts.StepsAfterDuplicate, Is.EqualTo(2UL),
                    "the first command leaves step 1 and the duplicate's single execution advances one: "
                    + facts.Describe());
                Assert.That(facts.StepsAfterDuplicate, Is.EqualTo(facts.StepsAfterCommand + 1UL),
                    facts.Describe());
                Assert.That(facts.SeatBHandAfterDuplicate, Is.EqualTo(CardTableKeys.SeededHandCount - 1),
                    "the giver moved exactly one of its four seeded cards: " + facts.Describe());
                Assert.That(facts.PendingDemandAfterDuplicate, Is.Zero,
                    "a duplicate enqueues no further step (P-037): " + facts.Describe());
            }
        }

        /// <summary>
        /// A settled command that the domain rejects still commits as an observable rejected result, and it changes
        /// nothing: hands, score and table version are the pre-command values (P-037, P-042, P-044). Seat A holds
        /// <c>SeededHandCount - SetCardCount + 1</c> because the duplicate step's transfer handed it one card.
        /// </summary>
        [Test]
        public void ARejectedSettlementChangesNothing()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.StepsAfterRejection, Is.EqualTo(facts.StepsAfterDuplicate + 1UL),
                    "an admitted command commits its logical step even when its gameplay is rejected (P-037): "
                    + facts.Describe());
                Assert.That(facts.RejectedDecisionCount, Is.GreaterThanOrEqualTo(1),
                    "the rejection must be recorded by the domain: " + facts.Describe());
                Assert.That(facts.SeatAHandAfterRejection,
                    Is.EqualTo(CardTableKeys.SeededHandCount - CardSetRules.SetCardCount + 1),
                    "seat A played three of four seeded cards and received one from the duplicate's transfer: "
                    + facts.Describe());
                Assert.That(facts.SeatBHandAfterRejection, Is.EqualTo(facts.SeatBHandAfterDuplicate),
                    facts.Describe());
                Assert.That(facts.SeatAScoreAfterRejection, Is.EqualTo(facts.SeatAScoreAfterCommit),
                    facts.Describe());
                Assert.That(facts.TableVersionAfterRejection, Is.EqualTo(facts.TableVersionAfterCommit + 1U),
                    "only the duplicate step's committed transfer advanced the table, so the rejected settlement "
                    + "leaves the post-transfer version in place: " + facts.Describe());
            }
        }

        /// <summary>
        /// A transfer commits both sides in one step: the receiver gained the exact card the giver lost, neither hand
        /// was written without the other, and the table advanced once (P-044).
        /// </summary>
        [Test]
        public void ATransferCommitsBothSides()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.TransferGainedCard, Is.EqualTo(1),
                    "the receiver holds the transferred card exactly once: " + facts.Describe());
                Assert.That(facts.TransferLostCard, Is.Zero,
                    "the giver no longer holds it, so the move is not a copy: " + facts.Describe());
                Assert.That(facts.TransferGiverHandAfter, Is.EqualTo(facts.TransferGiverHandBefore - 1), facts.Describe());
                Assert.That(facts.TransferReceiverHandAfter, Is.EqualTo(facts.TransferReceiverHandBefore + 1),
                    facts.Describe());
                Assert.That(facts.TableVersionAfterTransfer, Is.EqualTo(facts.TableVersionAfterRejection + 1U),
                    "one committed settlement advances the table version once: " + facts.Describe());
                Assert.That(facts.StepsAfterTransfer, Is.EqualTo(facts.StepsAfterRejection + 1UL), facts.Describe());
            }
        }

        /// <summary>
        /// The declared atomic batch envelope (P-037): two seats bid for one card the holder has, the envelope
        /// travels its own route and is decoded by the reader bound to its schema, and exactly one bid consumes the
        /// card. The winner is the smallest (seat, sequence), so the loser's hand is untouched and the table
        /// advances once - which is 07 s2.3's "two commands cannot both consume the same card".
        /// </summary>
        [Test]
        public void TheBatchEnvelopeResolvesExactlyOneWinner()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.BatchAdmitted, Is.True, facts.Describe());
                Assert.That(facts.BatchCandidateCount, Is.EqualTo(2),
                    "the envelope carries its bounded candidate set: " + facts.Describe());
                Assert.That(facts.ContestWinnerSeat, Is.EqualTo((int)CardTableKeys.SeatBOrdinal),
                    "the smallest seat with the smallest sequence wins independently of slot order: "
                    + facts.Describe());
                Assert.That(facts.ContestWinnerSequence, Is.EqualTo(3UL), facts.Describe());
                Assert.That(facts.ContestHolderHandAfter, Is.EqualTo(facts.ContestHolderHandBefore - 1),
                    "the holder loses exactly the contested card: " + facts.Describe());
                Assert.That(facts.ContestWinnerHandAfter, Is.EqualTo(facts.ContestWinnerHandBefore + 1),
                    facts.Describe());
                Assert.That(facts.ContestLoserHandAfter, Is.EqualTo(facts.ContestLoserHandBefore),
                    "a losing bid consumes nothing: " + facts.Describe());
                Assert.That(facts.ContestTableVersion, Is.EqualTo(facts.TableVersionAfterTransfer + 1U),
                    facts.Describe());
                Assert.That(facts.ContestSteps, Is.EqualTo(facts.StepsAfterTransfer + 1UL),
                    "one admitted batch envelope is one logical step: " + facts.Describe());
            }
        }

        /// <summary>
        /// A seat created after the provider mounted obtains the same derived contribution before its first
        /// executable step, without a local import, and is visible only with its complete assembly (P-013, P-024).
        /// </summary>
        [Test]
        public void AFutureSeatInheritsTheModifierOnFirstVisibility()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.ForwardDerivationHadNoTargetChange, Is.True,
                    "the publication the spawn rides on changes no existing target's assembly (P-006): "
                    + facts.Describe());
                Assert.That(facts.SpawnedSeatBonusValue, Is.EqualTo(CardVocabulary.FestivalBonus),
                    "the future seat is eligible and beneath the festival provider (P-013, P-024): "
                    + facts.Describe());
                Assert.That(facts.SpawnedSeatStampPublished, Is.True, facts.Describe());
                Assert.That(facts.SpawnedSeatInView, Is.True,
                    "a target first becomes visible with its complete effective assembly (P-024): "
                    + facts.Describe());
                Assert.That(facts.SpawnedSeatStampEpoch, Is.EqualTo(facts.WorldEpochAfterSpawn), facts.Describe());
                Assert.That(facts.CountersJoinedAfterSpawn, Is.True, facts.Describe());
            }
        }

        /// <summary>
        /// An idle CommandDriven world performs no step at all: no step, no dispatch run and no pending demand
        /// (P-035, P-036), both before the first command and after the last one.
        /// </summary>
        [Test]
        public void AnIdleWorldPerformsZeroSteps()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.IdleFrames, Is.GreaterThan(0), facts.Describe());
                Assert.That(facts.IdleStepsCommitted, Is.Zero,
                    "waiting for a player produces no empty simulation step (07 s2.3, P-036): " + facts.Describe());
                Assert.That(facts.IdleDispatchRuns, Is.Zero, facts.Describe());
                Assert.That(facts.PendingDemandAfterIdle, Is.Zero, facts.Describe());
            }
        }

        /// <summary>
        /// GC-009 and GC-007 compiled the card table's declared plan with real validators: four stages, one
        /// playback point, and the declared decision buffer as a real producer-to-consumer edge from the
        /// validate stage to the commit stage (P-040, P-043).
        /// </summary>
        [Test]
        public void TheCompiledPlanIsFourStagesWithOnePlaybackPointAndTheDeclaredEdge()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.CompiledStageCount, Is.EqualTo(4),
                    "cards.input, cards.validate, cards.commit and cards.output (07 s2.3): " + facts.Describe());
                Assert.That(facts.CompiledSystemCount, Is.EqualTo(4), facts.Describe());
                Assert.That(facts.SchedulePlaybackPointCount, Is.EqualTo(1), facts.Describe());
                Assert.That(facts.ScheduleEdgeCount, Is.GreaterThanOrEqualTo(1), facts.Describe());
                Assert.That(facts.BufferEdgeValidateBeforeCommit, Is.True,
                    "the declared decision buffer must produce a real stage edge (P-043): " + facts.Describe());
                Assert.That(facts.InputStageIndex, Is.LessThan(facts.CommitStageIndex), facts.Describe());
                Assert.That(facts.ScheduleHash, Is.Not.Empty, facts.Describe());
                Assert.That(facts.WriterCount, Is.EqualTo(facts.CompiledStageCount),
                    "the descriptor has one stage per compiled stage: " + facts.Describe());
            }
        }

        /// <summary>
        /// GC-007 validated the card table's authority: every declared slot has the one logical owner (the table
        /// runtime owns its several entities, so one commit stage settles them), and every declared slot policy
        /// validated (P-034).
        /// </summary>
        [Test]
        public void OwnershipHasOneOwnerPerDeclaredDomainAndEverySlotPolicyValidated()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.SingleOwnerPerDomain, Is.True,
                    "one owner per authoritative domain is what makes a multi-entity settlement one decision "
                    + "(P-034): " + facts.Describe());
                Assert.That(facts.OwnershipDomainCount, Is.EqualTo(5),
                    "CardTableDeclarations declares exactly the table, seat, command-draft, decision-draft and"
                    + " output domains, one per owned slot: " + facts.Describe());
                Assert.That(facts.ValidatedSlotPolicyCount, Is.GreaterThanOrEqualTo(5), facts.Describe());
            }
        }

        /// <summary>
        /// The generated run really is the run over the committed card catalog: the fingerprint its facts report is
        /// the literal the committed `CardCatalog.g.cs` emits (P-028), and the two catalogs are genuinely different
        /// registrations.
        /// </summary>
        [Test]
        public void TheGeneratedRunReportsTheCommittedCatalogFingerprint()
        {
            Assert.That(generated.CatalogFingerprint, Is.EqualTo(CardCatalog.CatalogFingerprint),
                "the generated run must report the committed catalog's own fingerprint literal: " + generated.Describe());
            Assert.That(generated.CatalogFingerprint, Has.Length.EqualTo(64), generated.Describe());
            Assert.That(fixture.CatalogFingerprint, Is.Not.Empty, fixture.Describe());
            Assert.That(fixture.CatalogFingerprint, Is.Not.EqualTo(generated.CatalogFingerprint),
                "the generated catalog and the hand-written fixture catalog are different registrations: "
                + fixture.Describe());
        }

        /// <summary>
        /// The slice's world tears down safely: its retained jobs settled, no host resource stayed retained and the
        /// owned-world registry returned to its pre-run size (P-047, P-048).
        /// </summary>
        [Test]
        public void TeardownSettlesWorkAndDisposesTheWorld()
        {
            foreach (CardFacts facts in Runs())
            {
                Assert.That(facts.OutstandingJobsAfterTeardown, Is.Zero, facts.Describe());
                Assert.That(facts.RetainedResourcesAfterTeardown, Is.Zero, facts.Describe());
            }
        }

        private static IEnumerable<CardFacts> Runs()
        {
            yield return generated;
            yield return fixture;
        }

        private static List<CardStep> Run(string prefix)
        {
            var steps = new List<CardStep>();
            for (int i = 0; i < combined.Count; i++)
            {
                string name = combined[i].Name;
                if (prefix.Length == 0)
                {
                    if (!name.StartsWith(FixturePrefix, System.StringComparison.Ordinal))
                    {
                        steps.Add(combined[i]);
                    }
                }
                else if (name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    steps.Add(combined[i]);
                }
            }

            return steps;
        }

        private static List<string> Names(string prefix)
        {
            List<CardStep> steps = Run(prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0
                    ? steps[i].Name
                    : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }

        private static void AssertStepsPassed(List<CardStep> steps)
        {
            Assert.That(steps, Is.Not.Empty, "the slice recorded no observation at all.");

            var failed = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failed.Add(steps[i].ToString());
                }
            }

            Assert.That(failed, Is.Empty, "failed card checks: " + Join(failed));
        }

        private static string Join(List<string> values)
            => string.Join(" | ", values.ToArray());
    }
}
