// GameCore.Derivation tests — the 07 reference compositions (GC-006, REF-C01…C06, REF-N01…N05).
//
// These are the acceptance rows of `07-reference-compositions.md` that belong to derivation: which targets receive
// a derived capability, which do not, what changes on a mount, a spawn, a retraction, a reparent and a mode switch,
// and how an isolation boundary or an exclusive conflict behaves. Gameplay (scoring, quest facts, sessions) is not
// this task's scope; the rows asserted here are the assembly-visible ones.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class ReferenceCompositionTests
    {
        [Test]
        public void RefC01_FestivalScoresEveryEligibleSeatAndNeitherTheScoreboardNorTheIsolatedPracticeSeat()
        {
            DerivationResult result = Cards(CardComposition.Builder());

            Assert.That(DerivationAssert.HasCapability(result, Seat(CardComposition.SeatB), CardComposition.SetBonus), Is.True);
            Assert.That(
                DerivationAssert.Int32Value(DerivationAssert.SlotOf(result, Seat(CardComposition.SeatC), CardComposition.SetBonus)),
                Is.EqualTo(CardComposition.QuietBonus),
                "League B derives the quiet league's +1: a sibling branch never inherits the festival provider (P-011).");
            Assert.That(
                DerivationAssert.HasCapability(result, Seat(CardComposition.SeatB), CardComposition.MarketTarget),
                Is.False,
                "Only the market table recipe receives the market binding (07 s2.1).");
            Assert.That(
                DerivationAssert.HasCapability(result, Seat(CardComposition.Scoreboard), CardComposition.SetBonus),
                Is.False,
                "A scoreboard view recipe is ineligible (07 s2.1).");
            Assert.That(
                DerivationAssert.HasCapability(result, Seat(CardComposition.PracticeSeat), CardComposition.SetBonus),
                Is.False,
                "The practice seat is eligible but isolated from cards.SetBonus (REF-C01).");
        }

        [Test]
        public void RefC01_SpawnSeatDUnderLeagueAReceivesTheSameDerivedContributionWithoutAnImport()
        {
            FixtureBuilder builder = CardComposition.Builder();
            CardComposition.AddSeat(builder, CardComposition.SeatD, CardComposition.SeatAScope);

            DerivationResult result = Cards(builder);

            EffectiveSlot bonus = DerivationAssert.SlotOf(result, Seat(CardComposition.SeatD), CardComposition.SetBonus);
            Assert.That(DerivationAssert.Int32Value(bonus), Is.EqualTo(CardComposition.FestivalBonus));

            DerivationSnapshot snapshot = result.Snapshot;
            Assert.That(snapshot.TryGetTarget(Seat(CardComposition.SeatD), out DerivationTarget? seat), Is.True);
            Assert.That(seat!.Descriptor.Imports.Count, Is.EqualTo(0), "No per-instance import is written (REF-C01).");
        }

        [Test]
        public void RefC04_MountingANestedFestivalAddsItsBonusAndRetractingTheAncestorLeavesTheNestedOne()
        {
            FixtureBuilder builder = CardComposition.Builder(mountNestedFestival: true);
            DerivationResult nested = Cards(builder);

            EffectiveSlot combined = DerivationAssert.SlotOf(nested, Seat(CardComposition.SeatA), CardComposition.SetBonus);
            Assert.That(
                DerivationAssert.Int32Value(combined),
                Is.EqualTo(CardComposition.FestivalBonus + CardComposition.NestedFestivalBonus),
                "A nested festival with +3 on top of the ancestor +2 is +5 (REF-C04).");
            Assert.That(combined.Support.Count, Is.EqualTo(2), "One provenance record per source (07 s2.1).");

            builder.RemoveInstall(CardComposition.FestivalScoring);
            DerivationResult after = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(2UL), AssemblyEpoch.Second).ToSnapshot(),
                CardComposition.ValueSource(),
                DerivationOptions.Default,
                nested));

            EffectiveSlot remaining = DerivationAssert.SlotOf(after, Seat(CardComposition.SeatA), CardComposition.SetBonus);
            Assert.That(DerivationAssert.Int32Value(remaining), Is.EqualTo(CardComposition.NestedFestivalBonus));
            Assert.That(remaining.Support.Count, Is.EqualTo(1), "Only the departing source's entry is removed (REF-C04).");
            Assert.That(after.Delta!.Removed.Count, Is.EqualTo(1));
        }

        [Test]
        public void RefC05_MovingASeatToTheQuietLeagueChangesTheBonusAndPreservesStableIdentity()
        {
            FixtureBuilder builder = CardComposition.Builder();
            DerivationResult before = Cards(builder);
            Assert.That(
                DerivationAssert.Int32Value(DerivationAssert.SlotOf(before, Seat(CardComposition.SeatA), CardComposition.SetBonus)),
                Is.EqualTo(CardComposition.FestivalBonus));

            builder.MoveTarget(CardComposition.SeatA, CardComposition.SeatCScope);
            DerivationResult after = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(2UL), AssemblyEpoch.Second).ToSnapshot(),
                CardComposition.ValueSource(),
                DerivationOptions.Default,
                before));

            Assert.That(
                DerivationAssert.Int32Value(DerivationAssert.SlotOf(after, Seat(CardComposition.SeatA), CardComposition.SetBonus)),
                Is.EqualTo(CardComposition.QuietBonus),
                "The moved seat now derives the quiet league's +1 (REF-C05).");

            TargetAssembly moved = after.AssemblyOf(Seat(CardComposition.SeatA))!;
            Assert.That(moved.Target, Is.EqualTo(Seat(CardComposition.SeatA)), "The stable target id survives the move.");
            Assert.That(
                moved.BaseRecipe,
                Is.EqualTo(before.AssemblyOf(Seat(CardComposition.SeatA))!.BaseRecipe),
                "The recipe is unchanged; only the inherited support moved (P-025).");
            Assert.That(
                after.Delta!.AffectedTargets,
                Does.Contain(Seat(CardComposition.SeatA)),
                "The move reports the affected target, and unrelated seats keep their value.");
            Assert.That(
                DerivationAssert.Int32Value(DerivationAssert.SlotOf(after, Seat(CardComposition.SeatB), CardComposition.SetBonus)),
                Is.EqualTo(CardComposition.FestivalBonus),
                "Untouched siblings are not rescanned into a different value (P-023).");
        }

        [Test]
        public void RefC05_ModeSwitchBetweenAutomaticAndConservativeKeepsOnlyTheOptedInSeat()
        {
            FixtureBuilder builder = CardComposition.Builder();
            // Only seat A carries the complete opt-in naming the festival provider and capability (07 s2.4).
            builder.AddTargetOptIn(
                CardComposition.SeatA, CardComposition.SetBonus, CardComposition.FestivalScoring);

            FixtureValueSource values = CardComposition.ValueSource();
            DerivationResult automatic = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                values,
                DerivationOptions.Default,
                null));

            DerivationResult conservative = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Conservative, new CompositionRevision(2UL), AssemblyEpoch.Second).ToSnapshot(),
                values,
                DerivationOptions.Default,
                automatic));

            Assert.That(DerivationAssert.HasCapability(automatic, Seat(CardComposition.SeatB), CardComposition.SetBonus), Is.True);
            Assert.That(
                DerivationAssert.HasCapability(conservative, Seat(CardComposition.SeatB), CardComposition.SetBonus),
                Is.False,
                "B has no import and no opt-in, so Conservative retracts its contribution (07 s2.4).");
            Assert.That(
                DerivationAssert.HasCapability(conservative, Seat(CardComposition.SeatA), CardComposition.SetBonus),
                Is.True,
                "A keeps its bonus through the complete target opt-in (P-013).");
            Assert.That(
                DerivationAssert.HasCapability(conservative, Seat(CardComposition.PracticeSeat), CardComposition.SetBonus),
                Is.False,
                "The isolation boundary stays effective in both modes (TEST-006).");
        }

        [Test]
        public void RefC06_AnUnresolvedExclusiveConflictRejectsTheWholeProposalAndKeepsTheOldAssembly()
        {
            FixtureBuilder builder = CardComposition.Builder(mountDrawPolicyConflict: true);
            DerivationSnapshot snapshot = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), AssemblyEpoch.Second)
                .ToSnapshot();

            DerivationResult published = Cards(CardComposition.Builder());
            DerivationResult rejected = DerivationAssert.Rejected(
                DerivationEngine.Derive(snapshot, CardComposition.ValueSource(), DerivationOptions.Default, published),
                DerivationRejectionKind.CompositionConflict);

            Assert.That(rejected.DiagnosticCode, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(
                rejected.Delta,
                Is.Null,
                "The old mode, epoch and assembly remain published: a rejected plan publishes nothing (REF-C06).");
            Assert.That(published.Accepted, Is.True);
            Assert.That(
                DerivationAssert.HasCapability(published, Seat(CardComposition.SeatA), CardComposition.SetBonus),
                Is.True,
                "The prior assembly is still usable (TEST-007).");
        }

        [Test]
        public void RefN01_AChapterBindsItsOwnBranchWithItsOwnDefinitions()
        {
            DerivationResult result = Narrative(NarrativeComposition.Builder());

            Assert.That(
                DerivationAssert.IdValue(DerivationAssert.SlotOf(
                    result, Target(NarrativeComposition.Sailor), NarrativeComposition.ConversationBinding)),
                Is.EqualTo(FixtureIds.Id("chapter-two.dialogue-graph")),
                "Chapter Two binds its own branch; Chapter One cannot derive into a sibling chapter (07 s3.1).");
            Assert.That(
                DerivationAssert.HasCapability(result, Target(NarrativeComposition.Sailor), NarrativeComposition.RewardBinding),
                Is.True,
                "A compatible Chapter Two villager receives Chapter Two's complete chain.");
        }

        [Test]
        public void RefN01_ChapterBindsCompatibleDescendantsAndFutureVillagersButNotPropsIsolationOrSiblings()
        {
            FixtureBuilder builder = NarrativeComposition.Builder();
            builder.Target("npc-newcomer", NarrativeComposition.Village, NarrativeComposition.VillagerRecipe);

            DerivationResult result = Narrative(builder);

            Assert.That(DerivationAssert.HasCapability(result, Target(NarrativeComposition.Mara), NarrativeComposition.ConversationBinding), Is.True);
            Assert.That(DerivationAssert.HasCapability(result, Target("npc-newcomer"), NarrativeComposition.ConversationBinding), Is.True);
            Assert.That(
                DerivationAssert.HasCapability(result, Target("npc-newcomer"), NarrativeComposition.RewardBinding),
                Is.True,
                "A future villager receives the complete chain, including the higher strata (P-021, P-024).");
            Assert.That(
                DerivationAssert.HasCapability(result, Target(NarrativeComposition.GateEast), NarrativeComposition.GateConditionBinding),
                Is.True);
            Assert.That(
                DerivationAssert.HasCapability(result, Target(NarrativeComposition.CrowdProp), NarrativeComposition.ConversationBinding),
                Is.False,
                "A decorative crowd prop is ineligible: no inference from its name or proximity (07 s3.1).");
            Assert.That(
                DerivationAssert.HasCapability(result, Target(NarrativeComposition.Display), NarrativeComposition.ConversationBinding),
                Is.False,
                "The museum target is compatible but isolated (07 s3.1).");
            Assert.That(
                DerivationAssert.HasCapability(result, Target(NarrativeComposition.EncounterOak), NarrativeComposition.RewardBinding),
                Is.False,
                "An encounter recipe is not a villager recipe: the reward chain does not reach it (07 s3.1).");
        }

        [Test]
        public void RefN02_TheOrderedEncounterHooksComeOutInTheirDeclaredOrder()
        {
            DerivationResult result = Narrative(NarrativeComposition.Builder());

            EffectiveSlot hooks = DerivationAssert.SlotOf(
                result, Target(NarrativeComposition.EncounterOak), NarrativeComposition.EncounterHookBinding);

            Assert.That(hooks.Values.Count, Is.EqualTo(2));
            Assert.That(
                DerivationAssert.IdValueAt(hooks, 0),
                Is.EqualTo(FixtureIds.Id("chapter-one.hook.begin")),
                "BeginScene precedes OfferChoice through declared hook edges (07 s3.1).");
            Assert.That(DerivationAssert.IdValueAt(hooks, 1), Is.EqualTo(FixtureIds.Id("chapter-one.hook.offer")));
        }

        [Test]
        public void RefN04_MovingVillageToChapterTwoRebindsTheVillagersAndKeepsTheirIdentity()
        {
            FixtureBuilder builder = NarrativeComposition.Builder();
            DerivationResult before = Narrative(builder);

            builder.MoveTarget(NarrativeComposition.Mara, NarrativeComposition.Harbor);
            DerivationResult after = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(2UL), AssemblyEpoch.Second).ToSnapshot(),
                NarrativeComposition.ValueSource(),
                DerivationOptions.Default,
                before));

            EffectiveSlot binding = DerivationAssert.SlotOf(
                after, Target(NarrativeComposition.Mara), NarrativeComposition.ConversationBinding);
            Assert.That(
                DerivationAssert.IdValue(binding),
                Is.EqualTo(FixtureIds.Id("chapter-two.dialogue-graph")),
                "New bindings select the new chapter's definitions (REF-N04).");
            Assert.That(
                DerivationAssert.HasCapability(after, Target(NarrativeComposition.Mara), NarrativeComposition.RewardBinding),
                Is.True,
                "The moved target keeps its stable identity and the whole higher-stratum chain (P-025).");
            Assert.That(
                after.Delta!.AffectedTargets,
                Does.Contain(Target(NarrativeComposition.Mara)),
                "The move reports the moved target as affected.");
            Assert.That(
                DerivationAssert.HasCapability(after, Target(NarrativeComposition.GateEast), NarrativeComposition.GateConditionBinding),
                Is.True,
                "Targets that did not move keep their derived support (P-025).");
        }

        [Test]
        public void RefN05_ARemovedAndReintroducedChapterLeavesTheTargetsUnboundThenBoundAgain()
        {
            FixtureBuilder builder = NarrativeComposition.Builder();
            DerivationResult bound = Narrative(builder);

            builder.RemoveInstall(NarrativeComposition.ChapterOneInstall);
            DerivationResult unbound = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(2UL), AssemblyEpoch.Second).ToSnapshot(),
                NarrativeComposition.ValueSource(),
                DerivationOptions.Default,
                bound));

            Assert.That(
                DerivationAssert.HasCapability(unbound, Target(NarrativeComposition.Mara), NarrativeComposition.ConversationBinding),
                Is.False,
                "Unmounting the chapter retracts its derived bindings (REF-N05).");
            Assert.That(
                unbound.AssemblyOf(Target(NarrativeComposition.Mara))!.IsBaseOnly,
                Is.True,
                "The target keeps its base recipe and waits to be rebound (REF-N05).");
            Assert.That(unbound.Delta!.Removed.Count, Is.GreaterThan(0));

            // Reintroducing the same installation identity rebinds without a new target identity (P-017).
            builder.Install(
                NarrativeComposition.ChapterOneInstall,
                NarrativeComposition.ChapterOne,
                0,
                NarrativeComposition.ChapterOneRules("chapter-one"),
                state: InstallationState.Active);
            NarrativeComposition.RegisterHookKeys(builder, "chapter-one");

            DerivationResult rebound = DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(3UL), AssemblyEpoch.First).ToSnapshot(),
                NarrativeComposition.ValueSource(),
                DerivationOptions.Default,
                unbound));

            Assert.That(
                DerivationProjection.SemanticsText(rebound),
                Is.EqualTo(DerivationProjection.SemanticsText(bound)),
                "Rebinding reintroduces exactly the derived assembly the mount produced (REF-N05).");
        }

        [Test]
        public void RefP02_ExclusionsAndBoundariesHoldInBothModesWithIdentifiedBlockers()
        {
            foreach (PropagationMode mode in new[] { PropagationMode.Automatic, PropagationMode.Conservative })
            {
                FixtureBuilder builder = NarrativeComposition.Builder();
                builder.AddScopeExclusion(
                    NarrativeComposition.ChapterOne,
                    new ExclusionRule(
                        ExclusionTargetKind.Rule,
                        FixtureIds.Rule(NarrativeComposition.DialogueRule("chapter-one")).Value,
                        FixtureIds.Scope(NarrativeComposition.ChapterOne),
                        default(TargetId),
                        true));

                DerivationResult result = DerivationAssert.Accepted(DerivationEngine.Derive(
                    builder.Build(mode, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                    NarrativeComposition.ValueSource(),
                    DerivationOptions.Default,
                    null));

                Assert.That(
                    DerivationAssert.HasCapability(result, Target(NarrativeComposition.Mara), NarrativeComposition.ConversationBinding),
                    Is.False,
                    "The rule exclusion holds in " + mode.ToString() + " (REF-P02).");

                CandidateDecision decision = result.DecisionsOf(
                    Target(NarrativeComposition.Mara),
                    FixtureIds.Capability(NarrativeComposition.ConversationBinding))[0];
                Assert.That(decision.Status, Is.EqualTo(CandidateStatus.Excluded));
                Assert.That(
                    decision.Exclusions.Count,
                    Is.GreaterThan(0),
                    "The explanation identifies the blocker and the originating declaration (REF-P02).");
                Assert.That(decision.Exclusions[0].SourceScope, Is.EqualTo(FixtureIds.Scope(NarrativeComposition.ChapterOne)));
            }
        }

        [Test]
        public void RefP03_TheFiniteNarrativeChainTerminatesWithinTheReferenceBudget()
        {
            DerivationResult result = Narrative(NarrativeComposition.Builder());

            Assert.That(result.Counters.WithinBudget, Is.True);
            Assert.That(result.Counters.ExaminedCandidates, Is.LessThan(PropagationBudget.DefaultMaxExaminedCandidates));
            Assert.That(result.Counters.EmittedContributions, Is.LessThan(PropagationBudget.DefaultMaxEmittedContributions));
            Assert.That(
                result.Assemblies.Count,
                Is.EqualTo(result.Snapshot.Targets.Count),
                "Every target has exactly one assembly after one finite closure (P-021).");
        }

        private static DerivationResult Narrative(FixtureBuilder builder) =>
            DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                NarrativeComposition.ValueSource(),
                DerivationOptions.Default,
                null));

        private static DerivationResult Cards(FixtureBuilder builder) =>
            DerivationAssert.Accepted(DerivationEngine.Derive(
                builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot(),
                CardComposition.ValueSource(),
                DerivationOptions.Default,
                null));

        private static TargetId Seat(string name) => FixtureIds.Target(name);

        private static TargetId Target(string name) => FixtureIds.Target(name);
    }
}
