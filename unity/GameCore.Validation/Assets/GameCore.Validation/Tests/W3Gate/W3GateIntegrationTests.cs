#nullable enable
using System.Collections.Generic;
using System.IO;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Cards;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;
using GateRules = GameCore.Rules.Narrative.NarrativeGateRules;
using RulesNarrativeFacts = GameCore.Rules.Narrative.NarrativeFacts;

namespace GameCore.W3Gate.Tests
{
    /// <summary>
    /// Wave 3 integration gate, EditMode half (docs/game-core/09-implementation-guide.md, "Wave 3 — Two genuinely
    /// different running compositions"):
    ///
    ///   "Run narrative and cards with the same kernel. Show zero idle command steps, automatic existing/future
    ///    targets, a narrative state change and a card domain transfer. Both Unity-world fixtures must pass before
    ///    provisional generic execution review."
    ///
    /// The scenario itself (`GameCore.Validation.ProbeHost.W3GateScenario`) is shared with the standalone player
    /// probe (`-probeW3Gate`). It runs the real GC-010 narrative composition and the real GC-011 card composition,
    /// each in its own `Unity.Entities.World` in this one process, over their committed generated catalogs — never a
    /// seam fixture. Every case asserts on the facts the gate observed, so an integration regression is reported by
    /// value rather than only by a boolean.
    ///
    /// This assembly adds the one inspection a player cannot make: the build-time `.asmdef` reference audit, which
    /// needs the project tree.
    /// </summary>
    [TestFixture]
    public sealed class W3GateIntegrationTests
    {
        private static W3GateScenarioResult gate = null!;

        [OneTimeSetUp]
        public void RunBothCompositionsOnceInThisProcess()
        {
            gate = W3GateScenario.Run();
        }

        [TearDown]
        public void TearDown()
        {
            // Each composition tears its own world down; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>Every named observation of the gate must pass.</summary>
        [Test]
        public void EveryGateCheckPasses()
        {
            Assert.That(gate.Steps, Is.Not.Empty, "the gate recorded no observation at all.");

            var failed = new List<string>();
            for (int i = 0; i < gate.Steps.Count; i++)
            {
                if (!gate.Steps[i].Passed)
                {
                    failed.Add(gate.Steps[i].ToString());
                }
            }

            Assert.That(failed, Is.Empty, "failed gate checks: " + string.Join(" | ", failed.ToArray()));
            Assert.That(gate.AllPassed, Is.True, gate.Describe());
        }

        /// <summary>
        /// P-001 and 04 section 2, in the running process: no declared kernel assembly references a gameplay, rules,
        /// validation or generated assembly, no kernel assembly is loaded twice, and every loaded gameplay/rules
        /// assembly references the kernel. This is the half of the separation proof a player also makes.
        /// </summary>
        [Test]
        public void NoLoadedKernelAssemblyReferencesAGameplayPackage()
        {
            W3GateFacts facts = gate.Facts;
            Assert.That(facts.KernelForbiddenReferenceCount, Is.Zero,
                "a kernel assembly referenced a gameplay/rules/validation/generated assembly: " + facts.Describe());
            Assert.That(facts.DuplicateKernelAssemblyCount, Is.Zero,
                "a kernel assembly was loaded more than once: " + facts.Describe());
            Assert.That(facts.KernelInspectionFailureCount, Is.Zero,
                "an assembly's references could not be read, so the audit proved nothing about it: " + facts.Describe());
            Assert.That(facts.KernelAssemblyCount, Is.GreaterThanOrEqualTo(6),
                "the loaded kernel set is smaller than the player-visible kernel assemblies: " + facts.Describe());
            Assert.That(facts.GameplayFamilyAssemblyCount, Is.GreaterThanOrEqualTo(6),
                "both families' gameplay and rules assemblies must be loaded by this gate: " + facts.Describe());
            Assert.That(facts.GameplayFamilyOnKernelCount, Is.EqualTo(facts.GameplayFamilyAssemblyCount),
                "every gameplay/rules assembly must reference the kernel (04 section 2): " + facts.Describe());
        }

        /// <summary>
        /// P-001 and 04 section 2, at build time: every assembly definition inside a kernel package references no
        /// gameplay/rules/validation/generated assembly, and every Wave 3 gameplay and rules assembly definition
        /// references the kernel. Discovery-complete over the kernel packages, so a new kernel assembly cannot enter
        /// the tree unaudited.
        /// </summary>
        [Test]
        public void NoKernelAsmdefReferencesAGameplayAssembly()
        {
            string assemblyDirectory =
                Path.GetDirectoryName(typeof(W3GateIntegrationTests).Assembly.Location) ?? string.Empty;
            string? root = KernelAssemblyAudit.TryFindRepositoryRoot(Directory.GetCurrentDirectory())
                ?? KernelAssemblyAudit.TryFindRepositoryRoot(assemblyDirectory);

            Assert.That(root, Is.Not.Null,
                "no repository checkout was found above the working directory (" + Directory.GetCurrentDirectory()
                + ") or the test assembly directory (" + assemblyDirectory + ").");

            AsmdefReferenceReport report = KernelAssemblyAudit.AuditAsmdefReferences(root!);
            Assert.That(report.MissingPackages, Is.Empty,
                "an expected package directory is absent: " + report.Describe());
            Assert.That(report.Violations, Is.Empty,
                "a kernel asmdef references a gameplay assembly: " + report.Describe());
            Assert.That(report.KernelAsmdefs.Count, Is.GreaterThanOrEqualTo(10),
                "the kernel's assembly definitions must all be present: " + report.Describe());
            Assert.That(report.KernelReferenceCount, Is.GreaterThan(0), report.Describe());
            Assert.That(report.GameplayAsmdefs.Count, Is.GreaterThanOrEqualTo(6),
                "both families' asmdefs must be found: " + report.Describe());
            Assert.That(report.GameplayAsmdefsOnKernel.Count, Is.EqualTo(report.GameplayAsmdefs.Count),
                "every gameplay/rules asmdef must reference the kernel: " + report.Describe());
        }

        /// <summary>
        /// 04 section 3: each composition owned one world and left the registry as it found it, and the two worlds
        /// are distinct sessions — two families on one process and one kernel image, never one shared world.
        /// </summary>
        [Test]
        public void BothCompositionsRunInTheirOwnWorldOnOneKernelImage()
        {
            W3GateFacts facts = gate.Facts;
            Assert.That(facts.DistinctWorldSessions, Is.True,
                "the two compositions must run in two distinct worlds (04 section 3): " + facts.Describe());
            Assert.That(facts.RegistryAfterNarrative, Is.EqualTo(facts.RegistryBeforeNarrative), facts.Describe());
            Assert.That(facts.RegistryBeforeCards, Is.EqualTo(facts.RegistryAfterNarrative), facts.Describe());
            Assert.That(facts.RegistryAfterCards, Is.EqualTo(facts.RegistryBeforeCards), facts.Describe());
            Assert.That(facts.Narrative.RegistryAfterCreate, Is.EqualTo(facts.Narrative.RegistryBeforeCreate + 1),
                "the narrative run owned exactly one world: " + facts.Describe());
            Assert.That(facts.KernelImageIdentity, Is.Not.Empty,
                "the loaded kernel image must be identified: " + facts.Describe());
        }

        /// <summary>
        /// The gate's first clause in both families: an idle command-driven world commits no step and dispatches no
        /// stage (P-036, TEST-011; 07 section 6 REF-N06). Each composition's own `*-idle-world-performs-zero-steps`
        /// observation additionally asserts that it published no further image and left no pending demand.
        /// </summary>
        [Test]
        public void NoIdleCommandStepInEitherFamily()
        {
            W3GateFacts facts = gate.Facts;

            Assert.That(facts.Narrative.IdleFrames, Is.GreaterThan(0), facts.Narrative.Describe());
            Assert.That(facts.Narrative.IdleStepsCommitted, Is.Zero, facts.Narrative.Describe());
            Assert.That(facts.Narrative.IdleDispatchRuns, Is.Zero, facts.Narrative.Describe());
            Assert.That(facts.Narrative.PendingDemandAfterIdle, Is.Zero, facts.Narrative.Describe());

            Assert.That(facts.Cards.IdleFrames, Is.GreaterThan(0), facts.Cards.Describe());
            Assert.That(facts.Cards.IdleStepsCommitted, Is.Zero, facts.Cards.Describe());
            Assert.That(facts.Cards.IdleDispatchRuns, Is.Zero, facts.Cards.Describe());
            Assert.That(facts.Cards.PendingDemandAfterIdle, Is.Zero, facts.Cards.Describe());
        }

        /// <summary>
        /// The gate's second clause for the narrative family: the chapter's rules bind every eligible existing
        /// descendant and the future villager with no per-instance import, while the ineligible prop, the isolated
        /// museum target and the sibling chapter's sailor stay untouched (P-013, P-015, P-016, P-024).
        /// </summary>
        [Test]
        public void TheNarrativeChapterReachesExistingAndFutureTargetsAutomatically()
        {
            NarrativeFacts facts = gate.Facts.Narrative;
            Assert.That(facts.DerivedTargetCountAfterChapterOne, Is.EqualTo(3), facts.Describe());
            Assert.That(facts.MaraBindingRowCount, Is.EqualTo(2), facts.Describe());
            Assert.That(facts.MaraDialogueBindingValue, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.MaraChoiceBindingValue, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.MaraBindingIsActive, Is.True, facts.Describe());
            Assert.That(facts.GateBindingRowCount, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.GateBindingValue, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.EncounterBindingRowCount, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.EncounterBindingValue, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.CrowdBindingRowCount, Is.Zero,
                "an ineligible recipe must receive nothing (P-015): " + facts.Describe());
            Assert.That(facts.MuseumBindingRowCount, Is.Zero,
                "a `*` capability boundary must not be bypassed by a compatible recipe (P-016): " + facts.Describe());
            Assert.That(facts.SailorBindingRowCountBeforeChapterTwo, Is.Zero, facts.Describe());
            Assert.That(facts.SailorBindingRowCount, Is.EqualTo(2),
                "the sibling chapter binds only its own subtree: " + facts.Describe());
            Assert.That(facts.MaraStampEpoch, Is.EqualTo(facts.WorldEpochAfterChapterOne), facts.Describe());

            Assert.That(facts.SpawnedBindingRowCount, Is.EqualTo(2),
                "the future target must gain the chapter's binding with no import (P-013, P-024): " + facts.Describe());
            Assert.That(facts.SpawnedBindingValue, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.SpawnedStampPublished, Is.True, facts.Describe());
            Assert.That(facts.SpawnedStampEpoch, Is.EqualTo(facts.WorldEpochAfterSpawn), facts.Describe());
            Assert.That(facts.SpawnedTargetInPublishedView, Is.True, facts.Describe());
            Assert.That(facts.CountersJoinedAfterSpawn, Is.True,
                "P-006 has one publication series, so the lane and world numbers must rejoin: " + facts.Describe());
        }

        /// <summary>
        /// The gate's second clause for the card family: both festival seats inherit `+2`, the quiet league's seat
        /// its own `+1`, the ineligible scoreboard and the isolated practice seat nothing, and the future seat
        /// appears already carrying the modifier (P-013, P-015, P-016, P-024).
        /// </summary>
        [Test]
        public void TheCardModifierReachesExistingAndFutureSeatsAutomatically()
        {
            CardFacts facts = gate.Facts.Cards;
            Assert.That(facts.SeatABonusRowCount, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.SeatABonusIsActive, Is.True, facts.Describe());
            Assert.That(facts.SeatABonusValue, Is.EqualTo(CardVocabulary.FestivalBonus), facts.Describe());
            Assert.That(facts.SeatBBonusValue, Is.EqualTo(CardVocabulary.FestivalBonus), facts.Describe());
            Assert.That(facts.SeatCBonusValue, Is.EqualTo(CardVocabulary.QuietBonus), facts.Describe());
            Assert.That(facts.ScoreboardRowCount, Is.Zero,
                "an ineligible target must receive nothing (P-015): " + facts.Describe());
            Assert.That(facts.PracticeSeatBonusRowCount, Is.Zero,
                "an eligible but isolated seat must receive nothing (P-016): " + facts.Describe());
            Assert.That(facts.CountersJoinedAfterSetup, Is.True, facts.Describe());

            Assert.That(facts.SpawnedSeatBonusValue, Is.EqualTo(CardVocabulary.FestivalBonus),
                "the future seat must inherit the modifier before its first step (P-013, P-024): " + facts.Describe());
            Assert.That(facts.SpawnedSeatStampPublished, Is.True, facts.Describe());
            Assert.That(facts.SpawnedSeatInView, Is.True, facts.Describe());
            Assert.That(facts.CountersJoinedAfterSpawn, Is.True, facts.Describe());
        }

        /// <summary>
        /// The gate's third clause: one admitted choice commits one step, the durable fact reaches its next version,
        /// the gate owner evaluates that committed version and opens, and the committed event page exposes exactly
        /// the two results of that step — so the state change is observed through the committed snapshot, never from
        /// live working storage (P-032, P-044, P-045, TEST-013).
        /// </summary>
        [Test]
        public void TheNarrativeStateChangeIsVisibleInTheCommittedSnapshot()
        {
            NarrativeFacts facts = gate.Facts.Narrative;
            Assert.That(facts.CommandAdmitted, Is.True, facts.Describe());
            Assert.That(facts.StepsAfterCommand, Is.EqualTo(1UL), facts.Describe());
            Assert.That(facts.GateDecisionBeforeCommand, Is.EqualTo(GateRules.Closed), facts.Describe());
            Assert.That(facts.GateDecisionAfterCommand, Is.EqualTo(GateRules.Open),
                "the committed choice must change the authoritative gate outcome (07 section 3.2): " + facts.Describe());
            Assert.That(facts.QuestFactValueAfterCommand, Is.EqualTo(RulesNarrativeFacts.True), facts.Describe());
            Assert.That(facts.QuestFactVersionAfterCommand,
                Is.EqualTo(RulesNarrativeFacts.NextVersion(RulesNarrativeFacts.InitialVersion)), facts.Describe());
            Assert.That(facts.GateEvaluatedFactVersion, Is.EqualTo(facts.QuestFactVersionAfterCommand),
                "the gate must evaluate the committed fact version (P-032): " + facts.Describe());
            Assert.That(facts.CommittedEventCountAfterCommand, Is.EqualTo(2),
                "one step publishes exactly its two results (P-044): " + facts.Describe());
            Assert.That(facts.CommittedEventStepAfterCommand, Is.EqualTo(1UL), facts.Describe());
            Assert.That(facts.CommittedEventEpochAfterCommand, Is.EqualTo(facts.WorldEpochAfterChapterTwo),
                "a committed event names the epoch it was published at (P-030, P-045): " + facts.Describe());
            Assert.That(facts.LedgerCommittedCount, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.LedgerPendingCount, Is.Zero, facts.Describe());
            Assert.That(facts.StepGroupDispatchRuns, Is.GreaterThanOrEqualTo(1), facts.Describe());
            Assert.That(facts.StepGroupDispatchedEntries, Is.GreaterThanOrEqualTo(1), facts.Describe());
        }

        /// <summary>
        /// The gate's fourth clause: the transfer moves exactly one card from the giver into the receiver, the giver
        /// keeps none and the receiver holds one, and the table advanced. The slice's own
        /// `cards-transfer-commits-both-sides` observation asserts the one-step arithmetic and a rejected settlement
        /// that writes nothing; both are reported in the assertion message (P-037, P-044, TEST-013).
        /// </summary>
        [Test]
        public void TheCardDomainTransferCommitsBothSides()
        {
            CardFacts facts = gate.Facts.Cards;
            Assert.That(facts.TransferGiverHandBefore - facts.TransferGiverHandAfter, Is.EqualTo(1),
                "the giver must lose exactly the transferred card (P-044): " + facts.Describe());
            Assert.That(facts.TransferReceiverHandAfter - facts.TransferReceiverHandBefore, Is.EqualTo(1),
                "the receiver must gain exactly one card (P-044): " + facts.Describe());
            Assert.That(facts.TransferGainedCard, Is.EqualTo(1), facts.Describe());
            Assert.That(facts.TransferLostCard, Is.Zero,
                "the giver must not still hold the moved card: " + facts.Describe());
            Assert.That(facts.StepsAfterTransfer, Is.GreaterThanOrEqualTo(1UL), facts.Describe());
            Assert.That(facts.TableVersionAfterTransfer, Is.GreaterThanOrEqualTo(1U), facts.Describe());
            Assert.That(facts.RejectedDecisionCount, Is.GreaterThanOrEqualTo(1),
                "the rejected settlement must have been observed in the same world: " + facts.Describe());
            Assert.That(facts.DuplicateRejections, Is.GreaterThanOrEqualTo(1),
                "the duplicate command must have been counted once: " + facts.Describe());
        }

        /// <summary>
        /// Both slices ran to completion in this process: every observation of both compositions passed, each over
        /// its committed generated catalog whose fingerprint literal the run reports (P-028, P-059).
        /// </summary>
        [Test]
        public void BothCompositionsCompletedWithoutAFailedObservation()
        {
            W3GateFacts facts = gate.Facts;
            Assert.That(facts.NarrativeFailedStepCount, Is.Zero, facts.Narrative.Describe());
            Assert.That(facts.NarrativeStepCount, Is.EqualTo(11),
                "the narrative scenario records eleven observations: " + facts.Narrative.Describe());
            Assert.That(facts.Narrative.CatalogFingerprint, Is.Not.Empty, facts.Narrative.Describe());
            Assert.That(facts.CardFailedStepCount, Is.Zero, facts.Cards.Describe());
            Assert.That(facts.CardStepCount, Is.EqualTo(13),
                "the card scenario records thirteen observations: " + facts.Cards.Describe());
            Assert.That(facts.Cards.CatalogFingerprint, Is.Not.Empty, facts.Cards.Describe());
            Assert.That(facts.Narrative.CatalogFingerprint, Is.Not.EqualTo(facts.Cards.CatalogFingerprint),
                "the two families run over two different catalogs: " + facts.Describe());
        }
    }
}
