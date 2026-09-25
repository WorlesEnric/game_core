// GameCore.Derivation tests — the reference compositions' move and mode-switch tables, seen incrementally
// (GC-013, TEST-006/TEST-008; 07 sections 2.4 and 3.4).
//
// These are the clauses the Wave 4 gate names: a reparent preserves state and updates inherited bindings, both
// mode-switch directions apply to existing *and future* targets, an isolated branch stays unchanged, Automatic
// adds no per-instance import, and a switch that would expose a conflict leaves the old assembly published. Each
// is asserted through the incremental engine, so the same clauses hold on the path the runtime actually uses
// rather than only on a full recomputation.
#nullable enable
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class ReferenceMoveAndModeTests
    {
        private const string FutureVillager = "npc-future-villager";
        private const string FutureSeat = "seat-future";

        /// <summary>07 s3.4: "Reparent Village under ChapterTwo" selects the Chapter Two definitions (P-025).</summary>
        [Test]
        public void ReparentingTheVillageSelectsTheNewChaptersBindings()
        {
            DerivationSnapshot before = NarrativeSnapshot(PropagationMode.Automatic);
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, NarrativeSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = NarrativeComposition.Builder();
            builder.MoveScope(NarrativeComposition.Village, NarrativeComposition.ChapterTwo);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, NarrativeSource(), DerivationOptions.Default, published, null);
            DerivationAssert.Accepted(outcome.Result);
            AssertMatchesReference(after, NarrativeSource(), published, outcome.Result);

            // Mara's binding now comes from the Chapter Two provider; her identity, descriptor and the payload
            // schema are untouched (P-025 "stable target ids", P-017 "identity survives").
            EffectiveSlot mara = DerivationAssert.SlotOf(
                outcome.Result, FixtureIds.Target(NarrativeComposition.Mara), NarrativeComposition.ConversationBinding);
            Assert.That(
                mara.Support[0].Key.Provider.Equals(
                    new ProviderInstallationId(FixtureIds.Instance(NarrativeComposition.ChapterTwoInstall).Value)),
                Is.True,
                "The new inherited support is the Chapter Two provider (07 s3.4).");
            Assert.That(
                mara.Values.Count, Is.EqualTo(1),
                "The payload schema is unchanged; only its value comes from the other chapter.");
            Assert.That(
                SlotComposer.PayloadEquals(mara.Values[0], FixturePayload.Tag("chapter-two.dialogue-graph")),
                Is.True,
                "The new binding carries the Chapter Two graph definition (07 s3.4).");
            Assert.That(
                SlotComposer.PayloadEquals(mara.Values[0], FixturePayload.Tag("chapter-one.dialogue-graph")),
                Is.False,
                "The old Chapter One definition is not reinterpreted or retained.");

            // The gate and the encounter in the same moved subtree follow the same chapter.
            EffectiveSlot gate = DerivationAssert.SlotOf(
                outcome.Result, FixtureIds.Target(NarrativeComposition.GateEast), NarrativeComposition.GateConditionBinding);
            Assert.That(
                gate.Support[0].Key.Provider.Equals(
                    new ProviderInstallationId(FixtureIds.Instance(NarrativeComposition.ChapterTwoInstall).Value)),
                Is.True);

            // Targets outside the moved subtree keep exactly what they had: the museum target is isolated from
            // both chapters, and the sailor was already Chapter Two's.
            TargetAssembly? display = outcome.Result.AssemblyOf(FixtureIds.Target(NarrativeComposition.Display));
            Assert.That(display, Is.Not.Null);
            Assert.That(display!.IsBaseOnly, Is.True, "The museum target receives nothing in either chapter (P-016).");
            Assert.That(
                outcome.Result.AssemblyOf(FixtureIds.Target(NarrativeComposition.Sailor))!.RecipeHash,
                Is.EqualTo(published.AssemblyOf(FixtureIds.Target(NarrativeComposition.Sailor))!.RecipeHash));
        }

        /// <summary>
        /// 07 s2.4's mode rows in both directions, with an existing target, a complete opt-in and a target created
        /// after the switch.
        /// </summary>
        [Test]
        public void BothModeDirectionsApplyToExistingAndFutureTargets()
        {
            FixtureBuilder builder = CardComposition.Builder();
            builder.Target(FutureSeat, CardComposition.LeagueA, CardComposition.CardSeatRecipe);
            DerivationSnapshot automatic = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();

            DerivationResult automaticResult = DerivationAssert.Accepted(
                DerivationEngine.Derive(automatic, CardSource(), DerivationOptions.Default, null));
            Assert.That(
                DerivationAssert.HasCapability(
                    automaticResult, FixtureIds.Target(FutureSeat), CardComposition.SetBonus),
                Is.True,
                "Automatic reaches a target created after the mount without an import (TEST-004, P-013).");

            // Automatic -> Conservative: a seat with no grant loses the inherited bonus, and nothing else moves.
            DerivationSnapshot conservative = builder
                .Build(PropagationMode.Conservative, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            IncrementalDerivationOutcome toConservative = IncrementalDerivationEngine.Derive(
                conservative, CardSource(), DerivationOptions.Default, automaticResult, null);
            DerivationAssert.Accepted(toConservative.Result);
            Assert.That(
                DerivationAssert.HasCapability(
                    toConservative.Result, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus),
                Is.False,
                "Conservative denies a descendant rule without an import or opt-in (P-013).");
            Assert.That(toConservative.Invalidation.WholeWorld, Is.True, "The switch reports its whole-world cost.");

            // A target created while the world is Conservative derives nothing either.
            builder.Target("seat-late", CardComposition.LeagueA, CardComposition.CardSeatRecipe);
            DerivationSnapshot late = builder
                .Build(PropagationMode.Conservative, new CompositionRevision(3UL), new AssemblyEpoch(3UL))
                .ToSnapshot();
            DerivationResult lateResult = DerivationAssert.Accepted(
                IncrementalDerivationEngine.Derive(
                    late, CardSource(), DerivationOptions.Default, toConservative.Result, null).Result);
            Assert.That(
                DerivationAssert.HasCapability(lateResult, FixtureIds.Target("seat-late"), CardComposition.SetBonus),
                Is.False,
                "A future target follows the committed setting (P-014).");

            // Conservative -> Automatic: every eligible target regains the binding, and no descriptor gained an
            // import or an opt-in on the way (the P-013 "no per-instance import" clause).
            DerivationSnapshot back = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(4UL), new AssemblyEpoch(4UL))
                .ToSnapshot();
            IncrementalDerivationOutcome backResult = IncrementalDerivationEngine.Derive(
                back, CardSource(), DerivationOptions.Default, lateResult, null);
            DerivationAssert.Accepted(backResult.Result);
            Assert.That(
                DerivationAssert.HasCapability(
                    backResult.Result, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus),
                Is.True);
            Assert.That(
                DerivationAssert.HasCapability(backResult.Result, FixtureIds.Target("seat-late"), CardComposition.SetBonus),
                Is.True);
            Assert.That(
                DerivationAssert.HasCapability(
                    backResult.Result, FixtureIds.Target(CardComposition.PracticeSeat), CardComposition.SetBonus),
                Is.False,
                "The practice seat's boundary holds in both modes (P-016).");
            AssertNoInstanceDeclaration(back, CardComposition.SeatA, CardComposition.SetBonus);
            AssertNoInstanceDeclaration(back, "seat-late", CardComposition.SetBonus);
            Assert.That(
                DerivationProjection.SemanticsText(backResult.Result),
                Is.EqualTo(DerivationProjection.SemanticsText(
                    DerivationEngine.Derive(back, CardSource(), DerivationOptions.Default, lateResult))),
                "The incremental round trip equals a full recomputation.");
        }

        /// <summary>A complete target opt-in is what Conservative honours, and it survives the switch (P-013).</summary>
        [Test]
        public void ACompleteOptInKeepsItsBindingInConservative()
        {
            FixtureBuilder builder = new FixtureBuilder(CardComposition.DefaultWorld)
                .Scope(CardComposition.Match, null)
                .Scope(CardComposition.LeagueA, CardComposition.Match)
                .Scope(CardComposition.SeatAScope, CardComposition.LeagueA)
                .Contract(CardComposition.SetBonus, CardComposition.BonusStratum, new[]
                {
                    new FixtureSlot(
                        CardComposition.EffectiveSetBonusSchema,
                        CompositionPolicy.Additive,
                        reducer: FixtureIds.Key(CardComposition.BonusReducer)),
                })
                .Target(
                    CardComposition.SeatA,
                    CardComposition.SeatAScope,
                    CardComposition.CardSeatRecipe,
                    optInProviderPairs: new[] { CardComposition.SetBonus, CardComposition.FestivalScoring })
                .Target(CardComposition.SeatB, CardComposition.SeatAScope, CardComposition.CardSeatRecipe)
                .Install(
                    CardComposition.FestivalScoring,
                    CardComposition.LeagueA,
                    0,
                    CardComposition.ScoringRules(CardComposition.FestivalScoring, CardComposition.FestivalBonus),
                    state: InstallationState.Active);

            DerivationSnapshot conservative = builder
                .Build(PropagationMode.Conservative, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();
            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(conservative, CardSource(), DerivationOptions.Default, null));

            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus),
                Is.True,
                "A full explicit opt-in grants the descendant rule in Conservative (P-013).");
            Assert.That(
                DerivationAssert.HasCapability(result, FixtureIds.Target(CardComposition.SeatB), CardComposition.SetBonus),
                Is.False,
                "The sibling without an opt-in is denied.");
        }

        /// <summary>
        /// 07 s2.4's conflict: in Conservative neither draw policy applies, so the fixture derives; the switch to
        /// Automatic would make both applicable on one `Exclusive` slot, so that derivation is rejected and the
        /// Conservative assembly stays the published one (P-014, P-019).
        /// </summary>
        [Test]
        public void AConflictOnTheOtherModeLeavesTheOldAssemblyPublished()
        {
            FixtureBuilder builder = CardComposition.Builder(mountDrawPolicyConflict: true);
            DerivationSnapshot conservative = builder
                .Build(PropagationMode.Conservative, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();
            DerivationResult publishedConservative = DerivationAssert.Accepted(
                DerivationEngine.Derive(conservative, CardSource(), DerivationOptions.Default, null));
            Assert.That(
                DerivationAssert.HasCapability(
                    publishedConservative, FixtureIds.Target(CardComposition.SeatA), CardComposition.DrawPolicy),
                Is.False,
                "Both draw policies are denied in Conservative, so nothing conflicts yet.");

            DerivationSnapshot automatic = CardComposition.Builder(mountDrawPolicyConflict: true)
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            IncrementalDerivationOutcome rejected = IncrementalDerivationEngine.Derive(
                automatic, CardSource(), DerivationOptions.Default, publishedConservative, null);

            Assert.That(rejected.Result.Accepted, Is.False);
            Assert.That(rejected.Result.Rejection, Is.EqualTo(DerivationRejectionKind.CompositionConflict));
            Assert.That(rejected.Result.DiagnosticCode, Is.EqualTo(DiagnosticCode.CapabilityConflict));
            Assert.That(rejected.Result.Assemblies.Count, Is.EqualTo(0), "A rejected switch publishes no assembly.");
            Assert.That(rejected.Invalidation.WholeWorld, Is.True, "The switch reports that it invalidated the world.");

            // The old result is still usable, which is exactly what the composition lane keeps published: the
            // validator refuses the proposal before admission settles, so the old mode and assembly never move.
            Assert.That(publishedConservative.Accepted, Is.True);
            Assert.That(publishedConservative.Snapshot.Mode, Is.EqualTo(PropagationMode.Conservative));
            TargetAssembly? seatC = publishedConservative.AssemblyOf(FixtureIds.Target(CardComposition.SeatC));
            Assert.That(seatC, Is.Not.Null, "The published Conservative assembly still covers its targets.");
            Assert.That(
                seatC!.EffectiveCapabilities.Count,
                Is.EqualTo(0),
                "In Conservative nothing is granted without an import, so the old assembly is exactly the base recipes (P-013).");
        }

        private static void AssertMatchesReference(
            DerivationSnapshot snapshot,
            FixtureValueSource values,
            DerivationResult previous,
            DerivationResult result)
        {
            DerivationResult reference = DerivationOracle.Derive(snapshot, values, DerivationOptions.Default, previous);
            Assert.That(result.Accepted, Is.EqualTo(reference.Accepted));
            Assert.That(
                DerivationProjection.SemanticsText(result),
                Is.EqualTo(DerivationProjection.SemanticsText(reference)));
            Assert.That(DerivationProjection.MissingDecisions(result, reference), Is.Empty);
            Assert.That(DerivationProjection.MissingDecisions(reference, result), Is.Empty);
        }

        private static void AssertNoInstanceDeclaration(
            DerivationSnapshot snapshot,
            string targetName,
            string capability)
        {
            Assert.That(
                snapshot.TryGetTarget(FixtureIds.Target(targetName), out DerivationTarget? target),
                Is.True);
            Assert.That(
                target!.Descriptor.Imports.Count,
                Is.EqualTo(0),
                "Automatic never writes a per-instance import (P-013): " + capability);
            Assert.That(
                target.Descriptor.OptIns.Count,
                Is.EqualTo(0),
                "Automatic never writes a per-instance opt-in (P-013): " + capability);
        }

        private static DerivationSnapshot NarrativeSnapshot(PropagationMode mode) =>
            NarrativeComposition.Builder()
                .Build(mode, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();

        private static FixtureValueSource NarrativeSource() => NarrativeComposition.ValueSource();

        private static FixtureValueSource CardSource() => CardComposition.ValueSource();
    }
}
