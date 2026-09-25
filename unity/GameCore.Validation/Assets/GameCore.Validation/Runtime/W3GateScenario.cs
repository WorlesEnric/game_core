// GameCore.Validation.ProbeHost — the Wave 3 integration gate scenario (W3-GATE).
//
// The sentence this file implements, from `docs/game-core/09-implementation-guide.md` (Wave 3 — Two genuinely
// different running compositions):
//
//   "Run narrative and cards with the same kernel. Show zero idle command steps, automatic existing/future targets,
//    a narrative state change and a card domain transfer. Both Unity-world fixtures must pass before provisional
//    generic execution review."
//
// Why TWO WORLDS in ONE process, and not one world holding both families:
//
//   * P-010 makes scopes one rooted acyclic tree per world and P-006 gives every world exactly one publication
//     series. The chapter tree (`NarrativeScopes`: story-world -> village / museum / harbor) and the market tree
//     (`CardMarketComposition`: market -> leagues / seats) are two different world definitions with two different
//     roots and two different lanes; mounting both into one world would require one composition to own the other's
//     root, which no requirement asks for.
//   * 04 section 3: "Each protocol world has one owning `UnityWorldHost` and one `Unity.Entities.World`. Multiple
//     protocol worlds require separate hosts and worlds, not global singleton state." Two worlds in one process is
//     therefore the supported way to run two compositions at once, and it is the strongest available proof that one
//     kernel carries both without flattening either of them.
//   * 07 section 5's single world holding chapter and card packages is the CROSS-FAMILY composition, with its own
//     bridge plugin and its own gate (Wave 7); it is deliberately not this gate's claim.
//
// Both compositions run over their committed generated catalog (the production `ImmutableCatalog`, never a seam
// fixture) inside this one process, one after the other, each in its own world. The per-slice probes
// (`-probeNarrative`, `-probeCards`) and the per-slice EditMode assemblies additionally run the fixture catalog, and
// `tools/run_w3_gate.sh` runs all of them on the same revision, so both catalogs stay covered without this gate
// becoming a second copy of the two slices.
//
// The gate reads the two slices' own published facts rather than re-deriving their values: a slice's facts are the
// values its own suite asserts, so the wave gate reports the same numbers the slices do. The one thing neither slice
// can prove alone is the kernel separation, and that is what `KernelAssemblyAudit` adds here.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GateRules = GameCore.Rules.Narrative.NarrativeGateRules;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;

namespace GameCore.Validation.ProbeHost
{
    /// <summary>One named W3 gate observation: what was checked and the values it was checked against.</summary>
    public sealed class W3GateStep
    {
        /// <summary>Builds one observation.</summary>
        public W3GateStep(string name, bool passed, string detail)
        {
            Name = name;
            Passed = passed;
            Detail = detail ?? string.Empty;
        }

        /// <summary>Stable observation name.</summary>
        public string Name { get; }

        /// <summary>Whether the observation held.</summary>
        public bool Passed { get; }

        /// <summary>The observed values the verdict was computed from.</summary>
        public string Detail { get; }

        /// <summary>One-line form.</summary>
        public override string ToString() => Name + ": " + (Passed ? "Pass" : "Fail") + " (" + Detail + ")";
    }

    /// <summary>
    /// Facts the gate observed. Every value is read from live module state at the moment named in its own comment,
    /// and the two nested facts objects are the two slices' own observations, unmodified.
    /// </summary>
    public sealed class W3GateFacts
    {
        /// <summary>Loaded kernel assemblies of the declared set, canonical order, comma-separated.</summary>
        public string KernelAssemblies { get; set; } = string.Empty;

        /// <summary>Identity of the loaded kernel image (`name=fullName` per assembly, newline-separated).</summary>
        public string KernelImageIdentity { get; set; } = string.Empty;

        public int KernelAssemblyCount { get; set; }

        /// <summary>Referenced assemblies examined across the loaded kernel set.</summary>
        public int KernelReferenceCount { get; set; }

        /// <summary>Kernel references into a gameplay, rules, validation or generated assembly; zero is required.</summary>
        public int KernelForbiddenReferenceCount { get; set; }

        public int DuplicateKernelAssemblyCount { get; set; }

        public int KernelInspectionFailureCount { get; set; }

        /// <summary>Loaded gameplay and rules assemblies.</summary>
        public int GameplayFamilyAssemblyCount { get; set; }

        /// <summary>Gameplay/rules assemblies that reference at least one kernel assembly (04 section 2).</summary>
        public int GameplayFamilyOnKernelCount { get; set; }

        public int NarrativeStepCount { get; set; }

        public int NarrativeFailedStepCount { get; set; }

        public int CardStepCount { get; set; }

        public int CardFailedStepCount { get; set; }

        /// <summary>Registered owned worlds immediately before the narrative run.</summary>
        public int RegistryBeforeNarrative { get; set; }

        /// <summary>Registered owned worlds immediately after the narrative run tore its world down.</summary>
        public int RegistryAfterNarrative { get; set; }

        /// <summary>Registered owned worlds immediately before the card run.</summary>
        public int RegistryBeforeCards { get; set; }

        /// <summary>Registered owned worlds immediately after the card run tore its world down.</summary>
        public int RegistryAfterCards { get; set; }

        public string NarrativeWorldSession { get; set; } = string.Empty;

        public string CardsWorldSession { get; set; } = string.Empty;

        public bool DistinctWorldSessions { get; set; }

        // ---------------------------------------------------------------- the gate's own reading of each clause

        /// <summary>Narrative idle frames pumped and the steps they committed (zero is required).</summary>
        public int NarrativeIdleFrames { get; set; }

        public int NarrativeIdleStepsCommitted { get; set; }

        /// <summary>Card idle frames pumped and the steps they committed (zero is required).</summary>
        public int CardIdleFrames { get; set; }

        public int CardIdleStepsCommitted { get; set; }

        /// <summary>Eligible existing narrative targets the chapter's rules bound, and the future villager's rows.</summary>
        public int NarrativeExistingTargetsBound { get; set; }

        public int NarrativeFutureTargetRows { get; set; }

        /// <summary>Eligible existing card seats the scoring provider bound, and the future seat's bonus value.</summary>
        public int CardExistingSeatsBound { get; set; }

        public int CardFutureSeatBonus { get; set; }

        /// <summary>Committed events the accepted narrative choice published, and the fact version the gate read.</summary>
        public int NarrativeCommittedEventCount { get; set; }

        public int NarrativeCommittedFactVersion { get; set; }

        /// <summary>Cards the transfer moved out of the giver and into the receiver.</summary>
        public int CardTransferGiverDelta { get; set; }

        public int CardTransferReceiverDelta { get; set; }

        /// <summary>The narrative run's own observations.</summary>
        public NarrativeFacts Narrative { get; set; } = null!;

        /// <summary>The card run's own observations.</summary>
        public CardFacts Cards { get; set; } = null!;

        /// <summary>One-line digest of every fact this gate observed, so a probe archives values beside verdicts.</summary>
        public string Describe() =>
            "kernelAssemblies=" + Num(KernelAssemblyCount)
            + "; kernelReferences=" + Num(KernelReferenceCount)
            + "; kernelForbiddenReferences=" + Num(KernelForbiddenReferenceCount)
            + "; duplicateKernelAssemblies=" + Num(DuplicateKernelAssemblyCount)
            + "; kernelInspectionFailures=" + Num(KernelInspectionFailureCount)
            + "; gameplayFamilyAssemblies=" + Num(GameplayFamilyAssemblyCount)
            + "; gameplayFamilyOnKernel=" + Num(GameplayFamilyOnKernelCount)
            + "; narrativeSteps=" + Num(NarrativeStepCount) + "/" + Num(NarrativeFailedStepCount) + " failed"
            + "; cardSteps=" + Num(CardStepCount) + "/" + Num(CardFailedStepCount) + " failed"
            + "; registry=" + Num(RegistryBeforeNarrative) + "->" + Num(RegistryAfterNarrative)
            + "|" + Num(RegistryBeforeCards) + "->" + Num(RegistryAfterCards)
            + "; narrativeSession=" + NarrativeWorldSession
            + "; cardsSession=" + CardsWorldSession
            + "; distinctSessions=" + DistinctWorldSessions.ToString()
            + "; narrativeIdle=" + Num(NarrativeIdleFrames) + " frames/" + Num(NarrativeIdleStepsCommitted) + " steps"
            + "; cardIdle=" + Num(CardIdleFrames) + " frames/" + Num(CardIdleStepsCommitted) + " steps"
            + "; narrativeExistingTargets=" + Num(NarrativeExistingTargetsBound)
            + "; narrativeFutureRows=" + Num(NarrativeFutureTargetRows)
            + "; cardExistingSeats=" + Num(CardExistingSeatsBound)
            + "; cardFutureBonus=" + Num(CardFutureSeatBonus)
            + "; narrativeCommittedEvents=" + Num(NarrativeCommittedEventCount)
            + "; narrativeCommittedFactVersion=" + Num(NarrativeCommittedFactVersion)
            + "; cardTransferGiver=" + Num(CardTransferGiverDelta)
            + "; cardTransferReceiver=" + Num(CardTransferReceiverDelta)
            + "; kernelImage=" + KernelImageIdentity.Replace("\n", " ")
            + "; kernelSet=" + KernelAssemblies;

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Full result of one gate run: the named observations plus the facts they were computed from.</summary>
    public sealed class W3GateScenarioResult
    {
        public W3GateScenarioResult(IReadOnlyList<W3GateStep> steps, W3GateFacts facts)
        {
            Steps = steps;
            Facts = facts;
        }

        public IReadOnlyList<W3GateStep> Steps { get; }

        public W3GateFacts Facts { get; }

        /// <summary>True when every observation passed and there was at least one.</summary>
        public bool AllPassed
        {
            get
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    if (!Steps[i].Passed)
                    {
                        return false;
                    }
                }

                return Steps.Count > 0;
            }
        }

        /// <summary>One-line digest naming every failed observation.</summary>
        public string Describe()
        {
            var failed = new List<string>();
            for (int i = 0; i < Steps.Count; i++)
            {
                if (!Steps[i].Passed)
                {
                    failed.Add(Steps[i].Name + " (" + Steps[i].Detail + ")");
                }
            }

            return failed.Count == 0
                ? Steps.Count.ToString(CultureInfo.InvariantCulture) + " W3 gate checks passed"
                : failed.Count.ToString(CultureInfo.InvariantCulture) + " W3 gate check(s) failed: "
                    + string.Join(" | ", failed.ToArray());
        }
    }

    /// <summary>Runs the W3 integration gate over the real narrative and card modules in one process.</summary>
    public static class W3GateScenario
    {
        /// <summary>Player-visible kernel assemblies the loaded-set check requires (the rest are Editor-only).</summary>
        private const int RequiredKernelAssemblyCount = 6;

        /// <summary>Gameplay and rules assemblies the one-way edge check requires.</summary>
        private const int RequiredGameplayFamilyAssemblyCount = 6;

        /// <summary>
        /// Runs the narrative composition and then the card composition, each in its own world, in this process, and
        /// observes the four Wave 3 clauses plus the kernel separation. Both run over their committed generated
        /// catalog, which is the production `ImmutableCatalog` and needs no seam fixture.
        /// </summary>
        public static W3GateScenarioResult Run()
        {
            var steps = new List<W3GateStep>(10);
            var facts = new W3GateFacts();

            // ---- the narrative composition, alone in its own world
            facts.RegistryBeforeNarrative = UnityWorldRegistry.Count;
            NarrativeScenarioResult? narrative = null;
            string narrativeFailure = string.Empty;
            try
            {
                narrative = NarrativeScenarioHost.RunGeneratedCatalog();
            }
            catch (Exception exception)
            {
                narrativeFailure = DescribeException(exception);
            }

            facts.RegistryAfterNarrative = UnityWorldRegistry.Count;

            // ---- the card composition, in a second world, in the same process and on the same kernel assemblies
            facts.RegistryBeforeCards = UnityWorldRegistry.Count;
            CardScenarioResult? cards = null;
            string cardFailure = string.Empty;
            try
            {
                cards = CardsScenarioHost.RunGeneratedCatalog();
            }
            catch (Exception exception)
            {
                cardFailure = DescribeException(exception);
            }

            facts.RegistryAfterCards = UnityWorldRegistry.Count;

            if (narrative != null)
            {
                facts.Narrative = narrative.Facts;
                facts.NarrativeStepCount = narrative.Steps.Count;
                facts.NarrativeFailedStepCount = CountFailures(narrative.Steps);
                facts.NarrativeWorldSession = narrative.Facts.WorldSession;
            }

            if (cards != null)
            {
                facts.Cards = cards.Facts;
                facts.CardStepCount = cards.Steps.Count;
                facts.CardFailedStepCount = CountFailures(cards.Steps);
                facts.CardsWorldSession = cards.Facts.WorldSession;
            }

            facts.DistinctWorldSessions =
                facts.NarrativeWorldSession.Length != 0
                && facts.CardsWorldSession.Length != 0
                && !string.Equals(facts.NarrativeWorldSession, facts.CardsWorldSession, StringComparison.Ordinal);

            // ---- the kernel separation, read after both compositions are loaded
            LoadedAssemblyReport kernel = KernelAssemblyAudit.AuditLoadedAssemblies();
            facts.KernelAssemblies = string.Join(",", ToArray(kernel.KernelAssemblies));
            facts.KernelImageIdentity = kernel.Identity;
            facts.KernelAssemblyCount = kernel.KernelAssemblies.Count;
            facts.KernelReferenceCount = kernel.KernelReferenceCount;
            facts.KernelForbiddenReferenceCount = kernel.ForbiddenReferences.Count;
            facts.DuplicateKernelAssemblyCount = kernel.DuplicateKernelAssemblies.Count;
            facts.KernelInspectionFailureCount = kernel.InspectionFailures.Count;
            facts.GameplayFamilyAssemblyCount = kernel.GameplayFamilyAssemblies.Count;
            facts.GameplayFamilyOnKernelCount = kernel.GameplayFamilyOnKernel.Count;

            steps.Add(new W3GateStep(
                "w3-kernel-assemblies-reference-no-gameplay",
                kernel.Clean
                && kernel.KernelAssemblies.Count >= RequiredKernelAssemblyCount
                && kernel.GameplayFamilyAssemblies.Count >= RequiredGameplayFamilyAssemblyCount
                && kernel.GameplayFamilyOnKernel.Count == kernel.GameplayFamilyAssemblies.Count,
                kernel.Describe()));

            steps.Add(new W3GateStep(
                "w3-narrative-composition-passes-on-this-kernel",
                narrative != null && narrative.AllPassed && narrative.Facts.CatalogFingerprint.Length != 0,
                narrative != null ? narrative.Describe() : narrativeFailure));

            steps.Add(new W3GateStep(
                "w3-card-composition-passes-on-this-kernel",
                cards != null && cards.AllPassed && cards.Facts.CatalogFingerprint.Length != 0,
                cards != null ? cards.Describe() : cardFailure));

            steps.Add(new W3GateStep(
                "w3-two-worlds-on-one-kernel-image",
                RegistryReturnedToBaseline(facts) && facts.DistinctWorldSessions,
                "registryBeforeNarrative=" + Num(facts.RegistryBeforeNarrative)
                + "; registryAfterNarrative=" + Num(facts.RegistryAfterNarrative)
                + "; registryBeforeCards=" + Num(facts.RegistryBeforeCards)
                + "; registryAfterCards=" + Num(facts.RegistryAfterCards)
                + "; narrativeSession=" + facts.NarrativeWorldSession
                + "; cardsSession=" + facts.CardsWorldSession
                + "; distinctSessions=" + facts.DistinctWorldSessions.ToString()
                + "; kernelImage=" + facts.KernelImageIdentity.Replace("\n", " ")));

            steps.Add(new W3GateStep(
                "w3-zero-idle-command-steps-in-both",
                narrative != null && cards != null
                && NarrativeIdleWorldIsStill(narrative.Facts)
                && CardsIdleWorldIsStill(cards.Facts),
                narrative == null || cards == null
                    ? "a composition did not run: " + narrativeFailure + cardFailure
                    : NarrativeIdleDetail(narrative.Facts) + "; " + CardIdleDetail(cards.Facts)));

            steps.Add(new W3GateStep(
                "w3-automatic-existing-and-future-targets-in-both",
                narrative != null && cards != null
                && NarrativeTargetsBindAutomatically(narrative.Facts)
                && CardSeatsBindAutomatically(cards.Facts),
                narrative == null || cards == null
                    ? "a composition did not run: " + narrativeFailure + cardFailure
                    : NarrativeTargetDetail(narrative.Facts) + "; " + CardSeatDetail(cards.Facts)));

            steps.Add(new W3GateStep(
                "w3-narrative-state-change-through-committed-snapshot",
                narrative != null && NarrativeStateChangedThroughCommittedSnapshot(narrative.Facts),
                narrative != null ? NarrativeStateDetail(narrative.Facts) : narrativeFailure));

            steps.Add(new W3GateStep(
                "w3-card-domain-transfer-committed-atomically",
                cards != null && CardTransferIsAtomic(cards.Facts),
                cards != null ? CardTransferDetail(cards.Facts) : cardFailure));

            // The two slices' own observations are archived beside this gate's own reading, so the artifact carries
            // the values every verdict above rests on rather than only the gate's summary of them.
            if (narrative != null)
            {
                facts.NarrativeExistingTargetsBound = narrative.Facts.DerivedTargetCountAfterChapterOne;
                facts.NarrativeFutureTargetRows = narrative.Facts.SpawnedBindingRowCount;
                facts.NarrativeIdleFrames = narrative.Facts.IdleFrames;
                facts.NarrativeIdleStepsCommitted = narrative.Facts.IdleStepsCommitted;
                facts.NarrativeCommittedEventCount = narrative.Facts.CommittedEventCountAfterCommand;
                facts.NarrativeCommittedFactVersion = narrative.Facts.QuestFactVersionAfterCommand;
                steps.Add(new W3GateStep("w3-narrative-facts", true, narrative.Facts.Describe()));
            }

            if (cards != null)
            {
                facts.CardExistingSeatsBound = CountEligibleSeats(cards.Facts);
                facts.CardFutureSeatBonus = cards.Facts.SpawnedSeatBonusValue;
                facts.CardIdleFrames = cards.Facts.IdleFrames;
                facts.CardIdleStepsCommitted = cards.Facts.IdleStepsCommitted;
                facts.CardTransferGiverDelta = cards.Facts.TransferGiverHandBefore - cards.Facts.TransferGiverHandAfter;
                facts.CardTransferReceiverDelta =
                    cards.Facts.TransferReceiverHandAfter - cards.Facts.TransferReceiverHandBefore;
                steps.Add(new W3GateStep("w3-card-facts", true, cards.Facts.Describe()));
            }

            return new W3GateScenarioResult(steps, facts);
        }

        // ---------------------------------------------------------------- clause readings

        /// <summary>
        /// P-006 and 04 section 3: each composition owned exactly one world while it ran, and neither world survived
        /// into the other's run, so the two families never shared host state.
        /// </summary>
        private static bool RegistryReturnedToBaseline(W3GateFacts facts)
        {
            if (facts.Narrative == null || facts.Cards == null)
            {
                return false;
            }

            return facts.RegistryAfterNarrative == facts.RegistryBeforeNarrative
                && facts.RegistryBeforeCards == facts.RegistryAfterNarrative
                && facts.RegistryAfterCards == facts.RegistryBeforeCards
                && facts.Narrative.RegistryAfterCreate == facts.Narrative.RegistryBeforeCreate + 1;
        }

        /// <summary>P-036, TEST-011: an idle command-driven world commits no step and dispatches no stage.</summary>
        private static bool NarrativeIdleWorldIsStill(NarrativeFacts facts) =>
            facts.IdleFrames > 0
            && facts.IdleStepsCommitted == 0
            && facts.IdleDispatchRuns == 0
            && facts.PendingDemandAfterIdle == 0UL;

        /// <summary>08's card variant: eight idle frames commit no step and run no dispatch.</summary>
        private static bool CardsIdleWorldIsStill(CardFacts facts) =>
            facts.IdleFrames > 0
            && facts.IdleStepsCommitted == 0
            && facts.IdleDispatchRuns == 0
            && facts.PendingDemandAfterIdle == 0UL;

        /// <summary>
        /// P-013, P-015, P-016, P-024: the chapter's rules bind every eligible existing descendant and the future
        /// villager with no per-instance import, while the ineligible prop, the isolated museum target and the
        /// sibling chapter's sailor stay untouched.
        /// </summary>
        private static bool NarrativeTargetsBindAutomatically(NarrativeFacts facts) =>
            facts.DerivedTargetCountAfterChapterOne == 3
            && facts.MaraBindingRowCount == 2
            && facts.MaraDialogueBindingValue == 1
            && facts.MaraChoiceBindingValue == 1
            && facts.MaraBindingIsActive
            && facts.GateBindingRowCount == 1
            && facts.GateBindingValue == 1
            && facts.EncounterBindingRowCount == 1
            && facts.EncounterBindingValue == 1
            && facts.CrowdBindingRowCount == 0
            && facts.MuseumBindingRowCount == 0
            && facts.SailorBindingRowCountBeforeChapterTwo == 0
            && facts.SailorBindingRowCount == 2
            && facts.MaraStampEpoch == facts.WorldEpochAfterChapterOne
            && facts.SpawnedBindingRowCount == 2
            && facts.SpawnedBindingValue == 1
            && facts.SpawnedStampPublished
            && facts.SpawnedStampEpoch == facts.WorldEpochAfterSpawn
            && facts.SpawnedTargetInPublishedView
            && facts.CountersJoinedAfterSpawn;

        /// <summary>
        /// P-013, P-015, P-016, P-024 in the card composition: both festival seats receive the inherited `+2`, the
        /// quiet league's seat its own `+1`, the ineligible scoreboard and the isolated practice seat receive
        /// nothing, and the future seat appears already carrying the modifier.
        /// </summary>
        private static bool CardSeatsBindAutomatically(CardFacts facts) =>
            facts.SeatABonusRowCount == 1
            && facts.SeatABonusIsActive
            && facts.SeatABonusValue == CardVocabulary.FestivalBonus
            && facts.SeatBBonusValue == CardVocabulary.FestivalBonus
            && facts.SeatCBonusValue == CardVocabulary.QuietBonus
            && facts.ScoreboardRowCount == 0
            && facts.PracticeSeatBonusRowCount == 0
            && facts.CountersJoinedAfterSetup
            && facts.SpawnedSeatBonusValue == CardVocabulary.FestivalBonus
            && facts.SpawnedSeatStampPublished
            && facts.SpawnedSeatInView
            && facts.CountersJoinedAfterSpawn;

        /// <summary>
        /// P-044, P-045, TEST-013: one admitted choice commits one step, the durable fact reaches its next version,
        /// the gate owner evaluates that committed version and opens, and the committed event page exposes exactly
        /// the two results of that step.
        /// </summary>
        private static bool NarrativeStateChangedThroughCommittedSnapshot(NarrativeFacts facts) =>
            facts.CommandAdmitted
            && facts.StepsAfterCommand == 1UL
            && facts.GateDecisionBeforeCommand == GateRules.Closed
            && facts.GateDecisionAfterCommand == GateRules.Open
            && facts.QuestFactValueAfterCommand == RulesNarrativeFacts.True
            && facts.QuestFactVersionAfterCommand == RulesNarrativeFacts.NextVersion(RulesNarrativeFacts.InitialVersion)
            && facts.GateEvaluatedFactVersion == facts.QuestFactVersionAfterCommand
            && facts.CommittedEventCountAfterCommand == 2
            && facts.CommittedEventStepAfterCommand == 1UL
            && facts.CommittedEventEpochAfterCommand == facts.WorldEpochAfterChapterTwo
            && facts.LedgerCommittedCount == 1
            && facts.LedgerPendingCount == 0
            && facts.StepGroupDispatchRuns >= 1
            && facts.StepGroupDispatchedEntries >= 1;

        /// <summary>
        /// P-044, TEST-013 in the card composition: the transfer moves exactly one card from the giver into the
        /// receiver, the giver keeps none and the receiver holds one, and the table advanced for that single step.
        /// The one-step half of "atomically" is the slice's own `cards-transfer-commits-both-sides` observation
        /// (`StepsAfterTransfer == stepBefore + 1`, asserted inside the run reported above), because the facts expose
        /// the step after the transfer rather than the step before it.
        /// </summary>
        private static bool CardTransferIsAtomic(CardFacts facts) =>
            facts.TransferGiverHandBefore - facts.TransferGiverHandAfter == 1
            && facts.TransferReceiverHandAfter - facts.TransferReceiverHandBefore == 1
            && facts.TransferGainedCard == 1
            && facts.TransferLostCard == 0
            && facts.StepsAfterTransfer >= 1UL
            && facts.TableVersionAfterTransfer >= 1U;

        private static int CountEligibleSeats(CardFacts facts)
        {
            int seats = 0;
            if (facts.SeatABonusRowCount > 0)
            {
                seats++;
            }

            if (facts.SeatBBonusValue == CardVocabulary.FestivalBonus)
            {
                seats++;
            }

            if (facts.SeatCBonusValue == CardVocabulary.QuietBonus)
            {
                seats++;
            }

            return seats;
        }

        // ---------------------------------------------------------------- detail strings

        private static string NarrativeIdleDetail(NarrativeFacts facts) =>
            "narrative frames=" + Num(facts.IdleFrames)
            + "; steps=" + Num(facts.IdleStepsCommitted)
            + "; dispatchRuns=" + Num(facts.IdleDispatchRuns)
            + "; demand=" + facts.PendingDemandAfterIdle.ToString(CultureInfo.InvariantCulture);

        private static string CardIdleDetail(CardFacts facts) =>
            "cards frames=" + Num(facts.IdleFrames)
            + "; steps=" + Num(facts.IdleStepsCommitted)
            + "; dispatchRuns=" + Num(facts.IdleDispatchRuns)
            + "; demand=" + facts.PendingDemandAfterIdle.ToString(CultureInfo.InvariantCulture);

        private static string NarrativeTargetDetail(NarrativeFacts facts) =>
            "derivedTargets=" + Num(facts.DerivedTargetCountAfterChapterOne)
            + "; maraRows=" + Num(facts.MaraBindingRowCount)
            + "; gateRows=" + Num(facts.GateBindingRowCount)
            + "; encounterRows=" + Num(facts.EncounterBindingRowCount)
            + "; crowdRows=" + Num(facts.CrowdBindingRowCount)
            + "; museumRows=" + Num(facts.MuseumBindingRowCount)
            + "; sailorRowsBeforeChapterTwo=" + Num(facts.SailorBindingRowCountBeforeChapterTwo)
            + "; sailorRows=" + Num(facts.SailorBindingRowCount)
            + "; spawnedRows=" + Num(facts.SpawnedBindingRowCount)
            + "; spawnedValue=" + Num(facts.SpawnedBindingValue)
            + "; spawnedPublished=" + facts.SpawnedStampPublished.ToString();

        private static string CardSeatDetail(CardFacts facts) =>
            "seatARows=" + Num(facts.SeatABonusRowCount)
            + "; seatAValue=" + Num(facts.SeatABonusValue)
            + "; seatBValue=" + Num(facts.SeatBBonusValue)
            + "; seatCValue=" + Num(facts.SeatCBonusValue)
            + "; scoreboardRows=" + Num(facts.ScoreboardRowCount)
            + "; practiceRows=" + Num(facts.PracticeSeatBonusRowCount)
            + "; spawnedBonus=" + Num(facts.SpawnedSeatBonusValue)
            + "; spawnedPublished=" + facts.SpawnedSeatStampPublished.ToString();

        private static string NarrativeStateDetail(NarrativeFacts facts) =>
            "admitted=" + facts.CommandAdmitted.ToString()
            + "; steps=" + facts.StepsAfterCommand.ToString(CultureInfo.InvariantCulture)
            + "; gate=" + Num(facts.GateDecisionBeforeCommand) + "->" + Num(facts.GateDecisionAfterCommand)
            + "; fact=" + Num(facts.QuestFactValueAfterCommand) + "@v" + Num(facts.QuestFactVersionAfterCommand)
            + "; evaluatedVersion=" + Num(facts.GateEvaluatedFactVersion)
            + "; committedEvents=" + Num(facts.CommittedEventCountAfterCommand)
            + "; committedEventStep=" + facts.CommittedEventStepAfterCommand.ToString(CultureInfo.InvariantCulture)
            + "; ledger=" + Num(facts.LedgerCommittedCount) + " committed/" + Num(facts.LedgerPendingCount) + " pending"
            + "; dispatchRuns=" + Num(facts.StepGroupDispatchRuns)
            + "; dispatchedEntries=" + Num(facts.StepGroupDispatchedEntries);

        private static string CardTransferDetail(CardFacts facts) =>
            "giver=" + Num(facts.TransferGiverHandBefore) + "->" + Num(facts.TransferGiverHandAfter)
            + "; receiver=" + Num(facts.TransferReceiverHandBefore) + "->" + Num(facts.TransferReceiverHandAfter)
            + "; gained=" + Num(facts.TransferGainedCard)
            + "; stillHeld=" + Num(facts.TransferLostCard)
            + "; tableVersion=" + facts.TableVersionAfterTransfer.ToString(CultureInfo.InvariantCulture)
            + "; steps=" + facts.StepsAfterTransfer.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- small helpers

        private static int CountFailures(IReadOnlyList<NarrativeStep> steps)
        {
            int failed = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failed++;
                }
            }

            return failed;
        }

        private static int CountFailures(IReadOnlyList<CardStep> steps)
        {
            int failed = 0;
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failed++;
                }
            }

            return failed;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var array = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                array[i] = values[i];
            }

            return array;
        }

        private static string DescribeException(Exception exception) =>
            "unhandled " + exception.GetType().FullName + ": " + exception.Message;

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
