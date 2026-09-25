// GameCore.Derivation tests — invalidation locality, reparent diffing and mode-switch cost (GC-013, TEST-008).
//
// TEST-008's acceptance clause is a claim about *work*: "Index counters show that local changes visit the
// invalidated membership/provider/schema sets; untouched sibling scopes are not rescanned. Global mode switches
// may legitimately invalidate the whole world and must report that cost explicitly."
//
// Each test below therefore asserts both halves: the semantic result (equal to the independent reference
// evaluator) and the counter that proves the incremental path did not walk the untouched part of the world.
#nullable enable
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class InvalidationLocalityTests
    {
        private const string HolidayScoring = "cards.holiday-scoring";
        private const string ExtraSeat = "seat-z";

        [Test]
        public void AProviderChangeInOneBranchDoesNotVisitTheOtherBranch()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            // Mount a second scoring provider under league B only: every league A seat is untouched by it.
            FixtureBuilder builder = CardComposition.Builder();
            builder.Install(
                HolidayScoring,
                CardComposition.LeagueB,
                0,
                CardComposition.ScoringRules(HolidayScoring, 4),
                state: InstallationState.Active);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            DerivationIndexSet previousIndexes = DerivationIndexSet.Build(before);
            DerivationIndexSet nextIndexes = DerivationIndexSet.Build(after);
            InvalidationClosureResult closure = InvalidationClosure.Compute(
                before, after, DerivationChangeSet.Diff(before, after), previousIndexes, nextIndexes);

            Assert.That(
                closure.DirtyScopes,
                Does.Not.Contain(FixtureIds.Scope(CardComposition.SeatAScope)),
                "A league B provider is not a reason to visit a league A seat (P-023).");
            Assert.That(
                closure.DirtyScopes,
                Does.Not.Contain(FixtureIds.Scope(CardComposition.SeatBScope)));
            Assert.That(
                closure.DirtyTargets.Count,
                Is.EqualTo(1),
                "Only the league B seat is in the changed provider's reach domain (P-023).");
            Assert.That(closure.DirtyTargets[0].Equals(FixtureIds.Target(CardComposition.SeatC)), Is.True);
            Assert.That(
                closure.Counters.TargetsVisited,
                Is.LessThanOrEqualTo(3),
                "The closure enumerates the changed rule's population, not the target set.");

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            DerivationResult full = DerivationEngine.Derive(after, CardSource(), DerivationOptions.Default, published);

            Assert.That(outcome.UsedFullRecompute, Is.False);
            Assert.That(outcome.Counters.CarriedTargets, Is.EqualTo(after.Targets.Count - 1));
            Assert.That(
                outcome.Counters.ExaminedCandidates,
                Is.LessThan(full.Counters.ExaminedCandidates),
                "A local edit examines fewer candidates than a full recomputation (P-023).");
            Assert.That(
                outcome.Result.Counters.IndexTargetsVisited,
                Is.LessThan(full.Counters.IndexTargetsVisited),
                "The engine visits fewer indexed targets than a full recomputation.");
            Assert.That(outcome.Counters.SkippedRules, Is.GreaterThan(0), "Rules outside the branch are never enumerated.");
            AssertIncrementalMatchesOracle(after, CardSource(), published, outcome.Result);
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus),
                Is.True,
                "The untouched seat keeps its derived assembly.");
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.SeatC), CardComposition.SetBonus),
                Is.True);
        }

        [Test]
        public void ADescriptorChangeOnOneTargetTouchesNoOtherTarget()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = CardComposition.Builder();
            builder.Target(ExtraSeat, CardComposition.LeagueA, CardComposition.CardSeatRecipe);
            DerivationSnapshot withSeat = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();
            DerivationResult withSeatResult = DerivationAssert.Accepted(
                IncrementalDerivationEngine.Derive(
                    withSeat, CardSource(), DerivationOptions.Default, published, null).Result);

            builder.AddTargetCapability(ExtraSeat, CardComposition.MarketTarget);
            DerivationSnapshot patched = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(3UL), new AssemblyEpoch(3UL))
                .ToSnapshot();

            DerivationChangeSet changeSet = DerivationChangeSet.Diff(withSeat, patched);
            Assert.That(changeSet.DescriptorChangedTargets.Count, Is.EqualTo(1));
            Assert.That(changeSet.DescriptorChangedTargets[0].Equals(FixtureIds.Target(ExtraSeat)), Is.True);
            Assert.That(changeSet.ChangedInstalls.Count, Is.EqualTo(0));
            Assert.That(changeSet.CreatedTargets.Count, Is.EqualTo(0));

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                patched, CardSource(), DerivationOptions.Default, withSeatResult, null);
            Assert.That(outcome.Invalidation.DirtyTargets.Count, Is.EqualTo(1));
            Assert.That(outcome.Invalidation.DirtyTargets[0].Equals(FixtureIds.Target(ExtraSeat)), Is.True);
            Assert.That(
                outcome.Counters.CarriedTargets,
                Is.EqualTo(patched.Targets.Count - 1),
                "Only the patched target is re-derived (P-015).");
            AssertIncrementalMatchesOracle(patched, CardSource(), withSeatResult, outcome.Result);
        }

        [Test]
        public void AModeSwitchReportsTheWholeWorldAsItsCost()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));
            DerivationSnapshot after = CardComposition.Builder()
                .Build(PropagationMode.Conservative, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            InvalidationClosureResult closure = InvalidationClosure.Compute(
                before, after, DerivationChangeSet.Diff(before, after));

            Assert.That(closure.WholeWorld, Is.True, "A mode switch invalidates the world (P-014).");
            Assert.That(closure.Counters.Reasons, Does.Contain(InvalidationReasons.ModeChanged));
            Assert.That(closure.DirtyTargets.Count, Is.EqualTo(after.Targets.Count));
            Assert.That(closure.Counters.WholeWorld, Is.True);

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            Assert.That(outcome.UsedFullRecompute, Is.True, "The cost is paid and reported, not hidden (TEST-008).");
            Assert.That(outcome.Invalidation.WholeWorld, Is.True);
            AssertIncrementalMatchesOracle(after, CardSource(), published, outcome.Result);
        }

        [Test]
        public void ReparentingASubtreeDiffsTheOldAndTheNewProviderClosure()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));
            EffectiveSlot beforeSlot = DerivationAssert.SlotOf(
                published, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus);

            // 07 s2.4: "Reparent SeatA from LeagueA to LeagueB". The league A festival (+2) stops reaching it and
            // league B's quiet scoring (+1) starts.
            FixtureBuilder builder = CardComposition.Builder();
            builder.MoveScope(CardComposition.SeatAScope, CardComposition.LeagueB);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            DerivationChangeSet changeSet = DerivationChangeSet.Diff(before, after);
            Assert.That(changeSet.ScopeMoves.Count, Is.EqualTo(1));
            Assert.That(changeSet.ScopeMoves[0].Scope.Equals(FixtureIds.Scope(CardComposition.SeatAScope)), Is.True);
            Assert.That(changeSet.ScopeMoves[0].OldParent.Equals(FixtureIds.Scope(CardComposition.LeagueA)), Is.True);
            Assert.That(changeSet.ScopeMoves[0].NewParent.Equals(FixtureIds.Scope(CardComposition.LeagueB)), Is.True);
            Assert.That(changeSet.ChangedInstalls.Count, Is.EqualTo(0), "No installation moved, only a scope.");

            DerivationIndexSet previousIndexes = DerivationIndexSet.Build(before);
            DerivationIndexSet nextIndexes = DerivationIndexSet.Build(after);
            InvalidationClosureResult closure = InvalidationClosure.Compute(
                before, after, changeSet, previousIndexes, nextIndexes);

            Assert.That(closure.DirtyTargets.Count, Is.EqualTo(1), "Only the moved scope's seat changed (P-025).");
            Assert.That(closure.DirtyTargets[0].Equals(FixtureIds.Target(CardComposition.SeatA)), Is.True);
            Assert.That(closure.Counters.Reasons, Does.Contain(InvalidationReasons.ScopeReparented));

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            Assert.That(outcome.UsedFullRecompute, Is.False);
            AssertIncrementalMatchesOracle(after, CardSource(), published, outcome.Result);

            EffectiveSlot afterSlot = DerivationAssert.SlotOf(
                outcome.Result, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus);
            Assert.That(
                DerivationAssert.Int32Value(afterSlot),
                Is.EqualTo(CardComposition.QuietBonus),
                "The league B provider now supplies the seat (07 s2.4).");
            Assert.That(
                afterSlot.Support[0].Key.Provider.Equals(
                    new ProviderInstallationId(FixtureIds.Instance(CardComposition.QuietScoring).Value)),
                Is.True,
                "The old inherited support is retracted and the new one recorded (P-017).");
            Assert.That(beforeSlot.Support.Count, Is.EqualTo(1));
            Assert.That(DerivationAssert.Int32Value(beforeSlot), Is.EqualTo(CardComposition.FestivalBonus));

            // The seats that did not move keep exactly the assembly they had: the point of P-025.
            EffectiveSlot seatC = DerivationAssert.SlotOf(
                outcome.Result, FixtureIds.Target(CardComposition.SeatC), CardComposition.SetBonus);
            Assert.That(DerivationAssert.Int32Value(seatC), Is.EqualTo(CardComposition.QuietBonus));
            Assert.That(
                DerivationAssert.Int32Value(DerivationAssert.SlotOf(
                    outcome.Result, FixtureIds.Target(CardComposition.SeatB), CardComposition.SetBonus)),
                Is.EqualTo(CardComposition.FestivalBonus));
        }

        [Test]
        public void TheOldAndNewProviderClosureOfAMovedScopeAreDiffed()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = CardComposition.Builder();
            builder.MoveScope(CardComposition.SeatAScope, CardComposition.LeagueB);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            DerivationIndexSet previousIndexes = DerivationIndexSet.Build(before);
            DerivationIndexSet nextIndexes = DerivationIndexSet.Build(after);
            ProviderClosureDiff diff = ProviderClosureDiff.Compute(
                before,
                previousIndexes,
                after,
                nextIndexes,
                FixtureIds.Scope(CardComposition.SeatAScope));

            Assert.That(diff.IsEmpty, Is.False, "The move changes which providers reach the scope (P-025).");
            Assert.That(ContainsRule(diff.Removed, CardComposition.FestivalScoring + CardComposition.SetBonusSuffix), Is.True);
            Assert.That(ContainsRule(diff.Added, CardComposition.QuietScoring + CardComposition.SetBonusSuffix), Is.True);
            Assert.That(
                ContainsRule(diff.Added, CardComposition.FestivalScoring + CardComposition.SetBonusSuffix),
                Is.False);
            Assert.That(
                ContainsRule(diff.Removed, CardComposition.QuietScoring + CardComposition.SetBonusSuffix),
                Is.False);

            // Nothing on either side changed depth, so nothing is reordered; a move that changed a provider's depth
            // would show up here, because depth is a P-018 rank component.
            Assert.That(diff.RankChanged.Count, Is.EqualTo(0));
            Assert.That(diff.Describe(), Does.Contain("scope="));

            // The seat itself is the only dirty target, and the engine's own delta names the lost support (P-017).
            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            DerivationAssert.Accepted(outcome.Result);
            Assert.That(outcome.Result.Delta!.Slots.Count, Is.EqualTo(1));
            Assert.That(outcome.Result.Delta.Slots[0].LostSupport.Count, Is.EqualTo(1));
            Assert.That(outcome.Result.Delta.Slots[0].Support.Count, Is.EqualTo(1));
        }

        [Test]
        public void ABoundaryEditOnOneScopeLeavesSiblingsUntouched()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = CardComposition.Builder();
            builder.ReplaceScopeIsolation(
                CardComposition.LeagueB,
                new IsolationSet(false, new[] { FixtureIds.Capability(CardComposition.SetBonus).Value }));
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            InvalidationClosureResult closure = InvalidationClosure.Compute(
                before, after, DerivationChangeSet.Diff(before, after));
            Assert.That(closure.Counters.Reasons, Does.Contain(InvalidationReasons.ScopeFactsChanged));
            Assert.That(
                closure.DirtyScopes,
                Does.Not.Contain(FixtureIds.Scope(CardComposition.SeatAScope)),
                "A boundary on league B is not a reason to visit a league A seat (P-016).");

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            AssertIncrementalMatchesOracle(after, CardSource(), published, outcome.Result);
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.SeatA), CardComposition.SetBonus),
                Is.True,
                "The sibling branch is unaffected.");
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.SeatC), CardComposition.SetBonus),
                Is.True,
                "A provider installed at the boundary scope still works beneath it (P-016).");
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.PracticeSeat), CardComposition.SetBonus),
                Is.False,
                "The practice seat's own boundary keeps blocking the league A provider.");
        }

        [Test]
        public void AnOrderingKeyChangeTouchesOnlyThatRulesPopulation()
        {
            DerivationSnapshot before = NarrativeSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, NarrativeSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = NarrativeComposition.Builder();
            // Rewrite chapter one's "begin scene" ordering edges: the encounter recipe is the population of that
            // rule, so nothing else may be re-derived (P-019).
            builder.RuleKey(
                NarrativeComposition.HookBeginRule("chapter-one"),
                NarrativeComposition.EncounterHookBinding,
                NarrativeComposition.BeginSceneKey,
                before: new[] { new FixtureOrderEdge(NarrativeComposition.OfferChoiceKey, false) });
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            DerivationChangeSet changeSet = DerivationChangeSet.Diff(before, after);
            Assert.That(changeSet.ChangedRuleKeys.Count, Is.EqualTo(1));
            Assert.That(changeSet.ChangedInstalls.Count, Is.EqualTo(0));

            InvalidationClosureResult closure = InvalidationClosure.Compute(before, after, changeSet);
            Assert.That(closure.Counters.Reasons, Does.Contain(InvalidationReasons.RuleKeysChanged));
            Assert.That(
                closure.DirtyScopes,
                Does.Not.Contain(FixtureIds.Scope(NarrativeComposition.Harbor)),
                "Chapter two's harbour declares no encounter hook (P-023).");

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, NarrativeSource(), DerivationOptions.Default, published, null);
            AssertIncrementalMatchesOracle(after, NarrativeSource(), published, outcome.Result);
            Assert.That(outcome.Counters.CarriedTargets, Is.GreaterThan(0));
        }

        [Test]
        public void UnmountingTheLastProviderRetractsExactlyItsSupport()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = CardComposition.Builder();
            builder.RemoveInstall(CardComposition.QuietScoring);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            DerivationAssert.Accepted(outcome.Result);
            AssertIncrementalMatchesOracle(after, CardSource(), published, outcome.Result);
            Assert.That(outcome.Result.Delta, Is.Not.Null);
            Assert.That(outcome.Result.Delta!.Removed.Count, Is.GreaterThan(0));
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.SeatC), CardComposition.SetBonus),
                Is.False,
                "The removed provider's contribution is retracted and nothing else (P-017).");
            Assert.That(
                DerivationAssert.HasCapability(outcome.Result, FixtureIds.Target(CardComposition.SeatB), CardComposition.SetBonus),
                Is.True,
                "The surviving support of a different provider is preserved (P-017).");
        }

        [Test]
        public void ANoChangeSnapshotCarriesEveryTargetAndProducesAnEmptyDelta()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            // A publication that changed only the revision and epoch, which is what a rejected proposal leaves
            // behind: nothing about the world moved, so nothing may be recomputed (P-006).
            DerivationSnapshot republication = CardComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(9UL), new AssemblyEpoch(9UL))
                .ToSnapshot();

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                republication, CardSource(), DerivationOptions.Default, published, null);

            Assert.That(outcome.Invalidation.IsEmpty, Is.True);
            Assert.That(outcome.Counters.ExaminedCandidates, Is.EqualTo(0));
            Assert.That(outcome.Counters.CarriedTargets, Is.EqualTo(republication.Targets.Count));
            Assert.That(outcome.Result.Delta, Is.Not.Null);
            Assert.That(outcome.Result.Delta!.IsEmpty, Is.True);
            Assert.That(outcome.Result.Delta!.Slots.Count, Is.EqualTo(0));
            Assert.That(
                DerivationProjection.SemanticsText(outcome.Result),
                Is.EqualTo(DerivationProjection.SemanticsText(published)));
        }

        [Test]
        public void ACarriedTargetKeepsItsProvenanceAndSupportExactly()
        {
            DerivationSnapshot before = CardSnapshot();
            DerivationResult published = DerivationAssert.Accepted(
                DerivationEngine.Derive(before, CardSource(), DerivationOptions.Default, null));

            FixtureBuilder builder = CardComposition.Builder();
            builder.Install(
                HolidayScoring,
                CardComposition.LeagueB,
                0,
                CardComposition.ScoringRules(HolidayScoring, 4),
                state: InstallationState.Active);
            DerivationSnapshot after = builder
                .Build(PropagationMode.Automatic, new CompositionRevision(2UL), new AssemblyEpoch(2UL))
                .ToSnapshot();

            IncrementalDerivationOutcome outcome = IncrementalDerivationEngine.Derive(
                after, CardSource(), DerivationOptions.Default, published, null);
            DerivationResult reference = DerivationOracle.Derive(after, CardSource(), DerivationOptions.Default, published);
            DerivationAssert.Accepted(outcome.Result);

            // SeatB is carried: its support identity, slot hash and recipe hash must equal the reference's, which
            // is the P-025 promise that unrelated targets retain their values.
            EffectiveSlot carried = DerivationAssert.SlotOf(
                outcome.Result, FixtureIds.Target(CardComposition.SeatB), CardComposition.SetBonus);
            EffectiveSlot recomputed = DerivationAssert.SlotOf(
                reference, FixtureIds.Target(CardComposition.SeatB), CardComposition.SetBonus);
            Assert.That(carried.Hash.Equals(recomputed.Hash), Is.True);
            Assert.That(carried.Support.Count, Is.EqualTo(recomputed.Support.Count));
            Assert.That(carried.Support[0].Key.Equals(recomputed.Support[0].Key), Is.True);
            Assert.That(
                outcome.Result.AssemblyOf(FixtureIds.Target(CardComposition.SeatB))!.RecipeHash,
                Is.EqualTo(reference.AssemblyOf(FixtureIds.Target(CardComposition.SeatB))!.RecipeHash));
            Assert.That(
                outcome.Result.ResultHash.Equals(reference.ResultHash),
                Is.True,
                "The canonical result hash of an incremental derivation equals a full recomputation's (P-027).");
        }

        private static void AssertIncrementalMatchesOracle(
            DerivationSnapshot snapshot,
            FixtureValueSource values,
            DerivationResult previous,
            DerivationResult result)
        {
            DerivationResult reference = DerivationOracle.Derive(snapshot, values, DerivationOptions.Default, previous);
            Assert.That(result.Accepted, Is.EqualTo(reference.Accepted));
            Assert.That(result.Rejection, Is.EqualTo(reference.Rejection));
            if (!result.Accepted)
            {
                return;
            }

            Assert.That(
                DerivationProjection.SemanticsText(result),
                Is.EqualTo(DerivationProjection.SemanticsText(reference)));
            Assert.That(DerivationProjection.MissingDecisions(result, reference), Is.Empty);
            Assert.That(DerivationProjection.MissingDecisions(reference, result), Is.Empty);
        }

        private static bool ContainsRule(IReadOnlyList<IndexedRule> rules, string ruleName)
        {
            RuleId wanted = FixtureIds.Rule(ruleName);
            for (int i = 0; i < rules.Count; i++)
            {
                if (rules[i].Rule.RuleId.Equals(wanted))
                {
                    return true;
                }
            }

            return false;
        }

        private static DerivationSnapshot CardSnapshot() =>
            CardComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();

        private static FixtureValueSource CardSource() => CardComposition.ValueSource();

        private static DerivationSnapshot NarrativeSnapshot() =>
            NarrativeComposition.Builder()
                .Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First)
                .ToSnapshot();

        private static FixtureValueSource NarrativeSource() => NarrativeComposition.ValueSource();
    }
}
