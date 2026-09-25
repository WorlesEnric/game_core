// GameCore.Derivation tests — termination, strata and budgets (P-021, P-022, P-028, TEST-007).
//
// TEST-007: "Exercise a finite capability chain, a diamond that discovers the same contribution twice, mutually
// dependent capability rules, a self-producing rule, a missing prerequisite and a provider that would exceed each
// configured derivation quota." Acceptance: accepted plans have one canonical finite closure with duplicate
// suppression; invalid cycles or non-convergent rules are rejected with the smallest available witness; budget
// exhaustion produces the declared rejection with counts and provenance and never publishes a truncated closure;
// the old assembly stays usable; raising a budget is an explicit configuration change.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Derivation.Tests
{
    [TestFixture]
    public sealed class TerminationAndBudgetTests
    {
        private const string Root = "term.root";
        private const string BranchA = "term.branch-a";
        private const string BranchB = "term.branch-b";
        private const string Leaf = "term.leaf";
        private const string Target = "term.target";
        private const string Recipe = "term.member-recipe";

        private static WorldId World { get; } = new WorldId(FixtureIds.Id("gamecore.world.termination-tests"));

        [Test]
        public void AFiniteCapabilityChainTerminatesAtTheHighestStratum()
        {
            // The chain of 02 s4: stratum 0 solves "participant", stratum 1 derives a choice from it, stratum 2 an
            // optional reward from the choice. Three strata, one canonical closure, no fixed-point loop.
            FixtureBuilder builder = ChainBuilder();
            DerivationResult result = DerivationAssert.Accepted(DerivationEngine.Derive(
                Snapshot(builder), Source(), DerivationOptions.Default, null));

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(Target), "term.stage0"), Is.True);
            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(Target), "term.stage1"), Is.True);
            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(Target), "term.stage2"), Is.True);
            Assert.That(
                result.Contributions.Count,
                Is.EqualTo(3),
                "One contribution per stratum: there is no unbounded expansion (P-021).");
        }

        [Test]
        public void ADiamondThatDiscoversTheSameContributionTwiceSuppressesTheDuplicate()
        {
            // Two rules of the same installation derive the same capability for the same target with equal rank:
            // the Replace policy keeps one winner and the other becomes shadowed provenance, once.
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Leaf, Root)
                .Contract("term.value", 0, new[] { new FixtureSlot("term.value-schema", CompositionPolicy.Replace) })
                .Target(Target, Leaf, Recipe)
                .Install("term.provider", Root, 0, DiamondRules(), state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Accepted(DerivationEngine.Derive(
                Snapshot(builder), Source(), DerivationOptions.Default, null));

            EffectiveSlot slot = DerivationAssert.SlotOf(result, FixtureIds.Target(Target), "term.value");
            Assert.That(slot.Support.Count, Is.EqualTo(1), "Duplicate discovery yields one support, not two (TEST-007).");
            Assert.That(slot.Shadowed.Count, Is.EqualTo(1));
            Assert.That(result.Contributions.Count, Is.EqualTo(2), "Both candidates stay inspectable as provenance.");
        }

        [Test]
        public void AMutuallyDependentPairOfRulesIsRejectedAsACatalogError()
        {
            // Two rules in the same stratum reading each other: composition within a stratum cannot feed
            // eligibility in that stratum, so this can never converge and is a catalog error (P-021).
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Leaf, Root)
                .Contract("term.value", 1, new[] { new FixtureSlot("term.value-schema", CompositionPolicy.Replace) })
                .Contract("term.other", 1, new[] { new FixtureSlot("term.other-schema", CompositionPolicy.Replace) })
                .Target(Target, Leaf, Recipe)
                .Install("term.provider", Root, 0, MutuallyDependentRules(), state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(Snapshot(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.ValidationFailed);

            bool sawStratumOrder = false;
            for (int i = 0; i < result.ValidationProblems.Count; i++)
            {
                if (result.ValidationProblems[i].Subject == "stratum-order")
                {
                    sawStratumOrder = true;
                }
            }

            Assert.That(sawStratumOrder, Is.True, "A same-stratum read is a catalog error even if a sample converges (P-021).");
            Assert.That(result.ValidationProblems[0].Code, Is.Not.EqualTo(DiagnosticCode.None));
        }

        [Test]
        public void ASelfProducingRuleIsRejected()
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Leaf, Root)
                .Contract("term.value", 1, new[] { new FixtureSlot("term.value-schema", CompositionPolicy.Replace) })
                .Target(Target, Leaf, Recipe)
                .Install("term.provider", Root, 0, SelfProducingRules(), state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(Snapshot(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.ValidationFailed);

            Assert.That(result.ValidationProblems[0].Subject, Is.EqualTo("self-input"));
            Assert.That(result.ValidationProblems[0].Code, Is.EqualTo(DiagnosticCode.Cycle));
        }

        [Test]
        public void AMissingLowerStratumPrerequisiteLeavesTheDependentRuleRejectedWithAWitness()
        {
            // The stratum-1 rule declares a stratum-0 input, but nothing derives that input for this target.
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Leaf, Root)
                .Contract("term.stage0", 0, new[] { new FixtureSlot("term.stage0-schema", CompositionPolicy.Replace) })
                .Contract("term.stage1", 1, new[] { new FixtureSlot("term.stage1-schema", CompositionPolicy.Replace) })
                .Target(Target, Leaf, Recipe)
                .Install(
                    "term.provider",
                    Root,
                    0,
                    new List<DerivationRule>
                    {
                        FixtureBuilder.Rule(
                            "term.dependent",
                            "term.stage1",
                            1,
                            1U,
                            FixtureBuilder.Selector(Recipe),
                            FixtureIds.Key("term.predicate.always"),
                            FixtureBuilder.Inputs("term.stage0"),
                            PropagationReach.SelfAndDescendants,
                            true,
                            0,
                            CompositionPolicy.Replace,
                            FixturePayload.Tag("term.stage1.value")),
                    },
                    state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Accepted(DerivationEngine.Derive(
                Snapshot(builder), Source(), DerivationOptions.Default, null));

            Assert.That(DerivationAssert.HasCapability(result, FixtureIds.Target(Target), "term.stage1"), Is.False);
            IReadOnlyList<CandidateDecision> decisions = result.DecisionsOf(
                FixtureIds.Target(Target), FixtureIds.Capability("term.stage1"));
            Assert.That(decisions.Count, Is.EqualTo(1));
            Assert.That(decisions[0].Status, Is.EqualTo(CandidateStatus.InputMissing));
            Assert.That(
                decisions[0].MissingInputs.Count,
                Is.EqualTo(1),
                "The decision names the missing prerequisite (TEST-007).");
            Assert.That(decisions[0].Diagnostic, Is.EqualTo(DiagnosticCode.MissingDependency));
        }

        [Test]
        public void ARuleDeclaredOutsideTheThirtyTwoStrataIsRejected()
        {
            FixtureBuilder builder = ChainBuilder();
            builder
                .Contract("term.out-of-range", DerivationSnapshot.StratumCount, new[]
                {
                    new FixtureSlot("term.out-of-range-schema", CompositionPolicy.Replace),
                })
                .Install(
                    "term.out-of-range-provider",
                    Root,
                    0,
                    new List<DerivationRule>
                    {
                        FixtureBuilder.Rule(
                            "term.out-of-range",
                            "term.out-of-range",
                            DerivationSnapshot.StratumCount,
                            1U,
                            FixtureBuilder.Selector(Recipe),
                            FixtureIds.Key("term.predicate.always"),
                            null,
                            PropagationReach.SelfAndDescendants,
                            false,
                            0,
                            CompositionPolicy.Replace,
                            FixturePayload.Tag("term.out-of-range.value")),
                    },
                    state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(Snapshot(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.ValidationFailed);

            bool sawStratum = false;
            for (int i = 0; i < result.ValidationProblems.Count; i++)
            {
                if (result.ValidationProblems[i].Subject == "stratum")
                {
                    sawStratum = true;
                    Assert.That(result.ValidationProblems[i].Observed, Is.EqualTo(DerivationSnapshot.StratumCount));
                }
            }

            Assert.That(sawStratum, Is.True, "A contract outside strata 0..31 is rejected, not silently skipped (P-021).");
        }

        [Test]
        public void ARuleThatWouldEmitMoreSlotsThanTheContractDeclaresIsRejected()
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(Leaf, Root)
                .Contract("term.value", 0, new[] { new FixtureSlot("term.value-schema", CompositionPolicy.Replace) })
                .Target(Target, Leaf, Recipe)
                .Install(
                    "term.provider",
                    Root,
                    0,
                    new List<DerivationRule>
                    {
                        FixtureBuilder.Rule(
                            "term.too-many-slots",
                            "term.value",
                            0,
                            2U,
                            FixtureBuilder.Selector(Recipe),
                            FixtureIds.Key("term.predicate.always"),
                            null,
                            PropagationReach.SelfAndDescendants,
                            false,
                            0,
                            CompositionPolicy.Replace,
                            FixturePayload.Tag("term.value.value")),
                    },
                    state: InstallationState.Active);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(Snapshot(builder), Source(), DerivationOptions.Default, null),
                DerivationRejectionKind.ValidationFailed);

            Assert.That(result.ValidationProblems[0].Subject, Is.EqualTo("output-slot-bound"));
        }

        [Test]
        public void CandidateQuotaExhaustionRejectsWithCountsAndCausesAndNoPartialClosure()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationSnapshot snapshot = Snapshot(builder);
            PropagationBudget tiny = new PropagationBudget(1L, 1000L, 1000L, 1L << 20, 2000L, 8000L);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(snapshot, Source(), new DerivationOptions(null, null, tiny, null, true), null),
                DerivationRejectionKind.BudgetExceeded);

            Assert.That(result.DiagnosticCode, Is.EqualTo(DiagnosticCode.BudgetExceeded));
            Assert.That(result.Counters.ExceededDimension, Is.EqualTo(BudgetDimension.ExaminedCandidates));
            Assert.That(result.Counters.ExceededCount, Is.GreaterThan(result.Counters.ExceededLimit));
            Assert.That(result.Counters.TopFanOutCauses.Count, Is.GreaterThan(0), "The rejection names the top causes (P-022).");
        }

        [Test]
        public void ContributionQuotaExhaustionRejectsBeforeAnyPartialResult()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationSnapshot snapshot = Snapshot(builder);
            PropagationBudget tight = new PropagationBudget(1000L, 1L, 1000L, 1L << 20, 2000L, 8000L);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(snapshot, Source(), new DerivationOptions(null, null, tight, null, true), null),
                DerivationRejectionKind.BudgetExceeded);

            Assert.That(result.Counters.ExceededDimension, Is.EqualTo(BudgetDimension.EmittedContributions));
            Assert.That(result.Contributions.Count, Is.EqualTo(0));
        }

        [Test]
        public void TemporaryByteQuotaExhaustionRejects()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationSnapshot snapshot = Snapshot(builder);
            PropagationBudget tight = new PropagationBudget(1000000L, 1000000L, 100000L, 1L, 2000L, 8000L);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(snapshot, Source(), new DerivationOptions(null, null, tight, null, true), null),
                DerivationRejectionKind.BudgetExceeded);

            Assert.That(result.Counters.ExceededDimension, Is.EqualTo(BudgetDimension.TemporaryBytes));
        }

        [Test]
        public void PreparationDeadlineExhaustionRejectsWithTheElapsedValue()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationSnapshot snapshot = Snapshot(builder);
            PropagationBudget tight = new PropagationBudget(1000000L, 1000000L, 100000L, 1L << 30, 2L, 8000L);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(
                    snapshot,
                    Source(),
                    new DerivationOptions(null, null, tight, () => 50L, true),
                    null),
                DerivationRejectionKind.BudgetExceeded);

            Assert.That(result.Counters.ExceededDimension, Is.EqualTo(BudgetDimension.PreparationDeadline));
            Assert.That(result.Counters.ExceededCount, Is.EqualTo(50L));
            Assert.That(result.Counters.ExceededLimit, Is.EqualTo(2L));
        }

        [Test]
        public void ApplyCostEstimateExhaustionRejects()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationSnapshot snapshot = Snapshot(builder);
            PropagationBudget tight = new PropagationBudget(1000000L, 1000000L, 100000L, 1L << 30, 2000L, 1L);

            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(snapshot, Source(), new DerivationOptions(null, null, tight, null, true), null),
                DerivationRejectionKind.BudgetExceeded);

            Assert.That(result.Counters.ExceededDimension, Is.EqualTo(BudgetDimension.ApplyCostEstimate));
        }

        [Test]
        public void ALargerBudgetIsAnExplicitConfigurationChangeThatSucceeds()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationSnapshot snapshot = Snapshot(builder);
            PropagationBudget tiny = new PropagationBudget(1L, 1000L, 1000L, 1L << 20, 2000L, 8000L);
            PropagationBudget larger = new PropagationBudget(1000000L, 1000L, 1000L, 1L << 20, 2000L, 8000L);

            DerivationAssert.Rejected(
                DerivationEngine.Derive(snapshot, Source(), new DerivationOptions(null, null, tiny, null, true), null),
                DerivationRejectionKind.BudgetExceeded);

            DerivationResult result = DerivationAssert.Accepted(
                DerivationEngine.Derive(snapshot, Source(), new DerivationOptions(null, null, larger, null, true), null));

            Assert.That(result.Counters.WithinBudget, Is.True);
            Assert.That(result.Counters.ExaminedCandidates, Is.GreaterThan(0));
        }

        [Test]
        public void ARejectedProposalLeavesThePreviouslyPublishedResultUsable()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationResult published = DerivationAssert.Accepted(DerivationEngine.Derive(
                Snapshot(builder), Source(), DerivationOptions.Default, null));

            // A stale expected revision rejects without mutation (P-028).
            DerivationResult rejected = DerivationAssert.Rejected(
                DerivationEngine.Derive(
                    Snapshot(builder),
                    Source(),
                    new DerivationOptions(new CompositionRevision(9UL), null, PropagationBudget.Reference, null, true),
                    published),
                DerivationRejectionKind.ValidationFailed);

            Assert.That(rejected.ValidationProblems[0].Code, Is.EqualTo(DiagnosticCode.StalePlan));
            Assert.That(rejected.ValidationProblems[0].Subject, Is.EqualTo("expected-revision"));
            Assert.That(published.Accepted, Is.True, "The old assembly is untouched by the rejected proposal (P-028).");
            Assert.That(DerivationAssert.HasCapability(published, FixtureIds.Target(Target), "term.stage2"), Is.True);
            Assert.That(rejected.Delta, Is.Null, "A rejected proposal publishes no delta.");
        }

        [Test]
        public void AnUnregisteredPredicateOrReducerIsReportedRatherThanSubstituted()
        {
            FixtureBuilder builder = ChainBuilder();
            DerivationResult result = DerivationAssert.Rejected(
                DerivationEngine.Derive(
                    Snapshot(builder),
                    EmptyDerivationValueSource.Instance,
                    DerivationOptions.Default,
                    null),
                DerivationRejectionKind.ValidationFailed);

            bool sawPredicate = false;
            for (int i = 0; i < result.ValidationProblems.Count; i++)
            {
                if (result.ValidationProblems[i].Subject == "predicate")
                {
                    sawPredicate = true;
                }
            }

            Assert.That(sawPredicate, Is.True, "A predicate key that resolves to nothing is reported (P-009, P-028).");
        }

        private static FixtureBuilder ChainBuilder()
        {
            FixtureBuilder builder = new FixtureBuilder(World)
                .Scope(Root, null)
                .Scope(BranchA, Root)
                .Scope(BranchB, BranchA)
                .Scope(Leaf, BranchB);

            builder
                .Contract("term.stage0", 0, new[] { new FixtureSlot("term.stage0-schema", CompositionPolicy.Replace) })
                .Contract("term.stage1", 1, new[] { new FixtureSlot("term.stage1-schema", CompositionPolicy.Replace) })
                .Contract("term.stage2", 2, new[] { new FixtureSlot("term.stage2-schema", CompositionPolicy.Replace) })
                .Target(Target, Leaf, Recipe)
                .Install("term.provider", Root, 0, ChainRules(), state: InstallationState.Active);

            return builder;
        }

        private static IReadOnlyList<DerivationRule> ChainRules() =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    "term.stage0",
                    "term.stage0",
                    0,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.stage0.value")),

                FixtureBuilder.Rule(
                    "term.stage1",
                    "term.stage1",
                    1,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    FixtureBuilder.Inputs("term.stage0"),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.stage1.value")),

                FixtureBuilder.Rule(
                    "term.stage2",
                    "term.stage2",
                    2,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    FixtureBuilder.Inputs("term.stage1"),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.stage2.value")),
            };

        private static IReadOnlyList<DerivationRule> DiamondRules() =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    "term.diamond-a",
                    "term.value",
                    0,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.value.a")),

                FixtureBuilder.Rule(
                    "term.diamond-b",
                    "term.value",
                    0,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    null,
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.value.b")),
            };

        private static IReadOnlyList<DerivationRule> MutuallyDependentRules() =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    "term.mutual-a",
                    "term.value",
                    1,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    FixtureBuilder.Inputs("term.other"),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.value.value")),

                FixtureBuilder.Rule(
                    "term.mutual-b",
                    "term.other",
                    1,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    FixtureBuilder.Inputs("term.value"),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.other.value")),
            };

        private static IReadOnlyList<DerivationRule> SelfProducingRules() =>
            new List<DerivationRule>
            {
                FixtureBuilder.Rule(
                    "term.self",
                    "term.value",
                    1,
                    1U,
                    FixtureBuilder.Selector(Recipe),
                    FixtureIds.Key("term.predicate.always"),
                    FixtureBuilder.Inputs("term.value"),
                    PropagationReach.SelfAndDescendants,
                    true,
                    0,
                    CompositionPolicy.Replace,
                    FixturePayload.Tag("term.value.value")),
            };

        private static DerivationSnapshot Snapshot(FixtureBuilder builder) =>
            builder.Build(PropagationMode.Automatic, new CompositionRevision(1UL), AssemblyEpoch.First).ToSnapshot();

        private static FixtureValueSource Source() =>
            new FixtureValueSource().RegisterAlwaysPredicate("term.predicate.always");
    }
}
