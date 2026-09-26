// GameCore.Benchmarks tests — the real derivation engine driven over the declared fixture (GC-026).
//
// The fixture generator's shape is only half of TEST-023: the load has to be loadable. This suite derives the
// declared 1,000-scope/10,000-target fixture with the real `DerivationEngine`, then drives the four update sizes of
// 08 (1, 100, 10,000 targets and a whole-world mode switch), the 1,000-target spawn under an already-active
// provider and the 100-target reparent through the incremental engine, and asserts *what changed* rather than that
// something ran:
//
//   * the affected target count is exact and tag-selected, not the rule's whole candidate domain;
//   * an unrelated target's effective values are carried unchanged (P-025);
//   * a whole-world invalidation says so instead of hiding behind a fast incremental number (P-014, TEST-008);
//   * the invalidation counters and the derivation counters agree.
//
// WHY THE CONFIGURED BUDGET. `PropagationBudget.Reference` carries P-022's provisional apply-cost guardrail of
// 8,000 us, and the engine checks it after assembly: the base fixture estimates 2 us per assembled target plus 3 us
// per written slot, so the declared 10,000-target world cannot pass it and the stock budget refuses the fixture with
// `BudgetExceeded`/`ApplyCostEstimate`. That refusal is asserted first, below, so it is visible in the test result
// rather than implied by a comment. P-022 permits a caller to configure a larger limit — and 08's fixture is a
// declared diagnostic load rather than a product plan — so every derivation here uses
// `BenchmarkFixture.SuggestedBudget`, the fixture's own shape-derived budget, which the player's benchmark scenario
// uses too: one definition, so the scenario and this suite cannot disagree about what "the fixture was accepted"
// means. The reference guardrails stay visible through `ReferenceApplyCostMicroseconds`, and the configured budget
// is asserted to be at least as permissive as them in every dimension.
//
// WHY THE SIZE-1 DIRTY SET IS NOT ONE TARGET. The frozen fixture header says it plainly: a tag-selected rule
// narrows the *affected* set, never its *candidate domain* — `DerivationSnapshot.TargetsInReach` selects by declared
// schema, and every fixture target declares one of the three family schemas. A newly mounted update provider at the
// world root therefore puts every target in its population, so `Counters.DirtyTargets` is the world for that mount
// and the evidence of a local publication is the *affected* set plus `UsedFullRecompute == false`. The 1%-of-world
// bound this suite asserts is the reparent case, where the closure really is bounded by the moved subtree.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GameCore.Contracts;
using GameCore.Derivation;
using GameCore.Derivation.Fixtures;
using NUnit.Framework;

namespace GameCore.Benchmarks.Tests
{
    [TestFixture]
    public sealed class BenchmarkFixtureDerivationTests
    {
        /// <summary>P-022's provisional apply-cost guardrail, quoted so the refusals below can be diagnosed in one run.</summary>
        private const long ReferenceApplyCostMicroseconds = PropagationBudget.DefaultMaxApplyCostEstimateMicroseconds;

        /// <summary>
        /// The fixture's own budget is cached in one instance so the options and the assertions below observe the
        /// same object rather than two equal ones: <see cref="BenchmarkFixture.SuggestedBudget"/> is a computed
        /// property, so reading it twice yields two budgets with equal limits.
        /// </summary>
        private static PropagationBudget? sharedBudget;

        private static BenchmarkFixture? sharedFixture;
        private static DerivationOptions? sharedOptions;
        private static DerivationResult? sharedBase;

        [Test]
        public void TheStockBudgetRefusesTheDeclaredFixtureAndTheConfiguredBudgetIsTheOneUsed()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationSnapshot snapshot = fixture.Builder().Build();

            Assert.That(
                DerivationOptions.Default.Budget,
                Is.SameAs(PropagationBudget.Reference),
                "the default options carry the reference guardrails");

            DerivationResult refused = DerivationEngine.Derive(snapshot, fixture.Values, DerivationOptions.Default, null);
            Assert.That(
                refused.Accepted,
                Is.False,
                "a 10,000-target apply estimate exceeds the reference limit of " + ReferenceApplyCostMicroseconds.ToString()
                + " us; " + Describe(refused));
            Assert.That(refused.Rejection, Is.EqualTo(DerivationRejectionKind.BudgetExceeded), Describe(refused));
            Assert.That(
                refused.Counters.ExceededDimension,
                Is.EqualTo(BudgetDimension.ApplyCostEstimate),
                "the refusal must be the provisional apply-cost guardrail and nothing else; " + Describe(refused));
            Assert.That(refused.Counters.WithinBudget, Is.False);
            Assert.That(refused.Counters.ExceededLimit, Is.EqualTo(ReferenceApplyCostMicroseconds), Describe(refused));
            Assert.That(refused.Counters.ExceededCount, Is.GreaterThan(ReferenceApplyCostMicroseconds), Describe(refused));
            Assert.That(refused.Assemblies.Count, Is.EqualTo(0), "a rejected proposal publishes no partial assembly (P-021, P-022)");
            Assert.That(refused.ValidationProblems.Count, Is.EqualTo(0), "the refusal is a budget, not a catalog problem");
            Assert.That(refused.DiagnosticCode, Is.EqualTo(DiagnosticCode.BudgetExceeded));

            PropagationBudget configured = ConfiguredBudget();
            Assert.That(
                configured,
                Is.Not.SameAs(PropagationBudget.Reference),
                "the fixture's budget is its own object, not the reference guardrails");
            Assert.That(
                configured.MaxExaminedCandidates,
                Is.GreaterThanOrEqualTo(PropagationBudget.Reference.MaxExaminedCandidates),
                "a fixture-derived budget is never tighter than the reference guardrail in any dimension");
            Assert.That(
                configured.MaxEmittedContributions,
                Is.GreaterThanOrEqualTo(PropagationBudget.Reference.MaxEmittedContributions));
            Assert.That(
                configured.MaxAffectedTargets,
                Is.GreaterThanOrEqualTo(PropagationBudget.Reference.MaxAffectedTargets));
            Assert.That(
                configured.MaxTemporaryBytes,
                Is.GreaterThanOrEqualTo(PropagationBudget.Reference.MaxTemporaryBytes));
            Assert.That(
                configured.PreparationDeadlineMilliseconds,
                Is.GreaterThanOrEqualTo(PropagationBudget.Reference.PreparationDeadlineMilliseconds));
            Assert.That(
                configured.MaxApplyCostEstimateMicroseconds,
                Is.GreaterThan(ReferenceApplyCostMicroseconds),
                "P-022 permits a caller to configure a larger limit than the reference guardrail, and the declared"
                + " 10,000-target load needs one for the apply-cost estimate alone");
            PropagationBudget shape = Fixture().SuggestedBudget;
            Assert.That(
                configured.MaxExaminedCandidates,
                Is.EqualTo(shape.MaxExaminedCandidates),
                "the cached budget is the fixture's own shape-derived budget, not a hand-picked multiplier");
            Assert.That(configured.MaxEmittedContributions, Is.EqualTo(shape.MaxEmittedContributions));
            Assert.That(configured.MaxAffectedTargets, Is.EqualTo(shape.MaxAffectedTargets));
            Assert.That(configured.MaxTemporaryBytes, Is.EqualTo(shape.MaxTemporaryBytes));
            Assert.That(configured.PreparationDeadlineMilliseconds, Is.EqualTo(shape.PreparationDeadlineMilliseconds));
            Assert.That(configured.MaxApplyCostEstimateMicroseconds, Is.EqualTo(shape.MaxApplyCostEstimateMicroseconds));
            Assert.That(shape, Is.Not.SameAs(configured), "and it is recomputed from the fixture rather than cached by the fixture");
            DerivationOptions options = Options();
            Assert.That(options.Budget, Is.SameAs(configured), "the derivation uses the budget the caller configured");

            DerivationResult accepted = Base();
            AssertAccepted(accepted, "base derivation of the declared fixture");
            Assert.That(accepted.Counters.WithinBudget, Is.True, Describe(accepted));
            Assert.That(accepted.Counters.ExceededDimension, Is.EqualTo(BudgetDimension.None), Describe(accepted));
            Assert.That(accepted.Counters.ExceededCount, Is.EqualTo(0L), Describe(accepted));

            long estimate = DerivationEngine.EstimateApplyCostMicroseconds(accepted.Assemblies, accepted.Delta);
            Assert.That(
                estimate,
                Is.GreaterThan(ReferenceApplyCostMicroseconds),
                "the recorded apply-cost estimate of " + estimate.ToString()
                + " us is why the reference budget refuses the declared fixture");
            Assert.That(
                estimate,
                Is.LessThanOrEqualTo(configured.MaxApplyCostEstimateMicroseconds),
                "and the configured limit of " + configured.MaxApplyCostEstimateMicroseconds.ToString()
                + " us is why it admits it (estimate " + estimate.ToString() + " us)");

            Assert.That(accepted.Rejection, Is.EqualTo(DerivationRejectionKind.None), Describe(accepted));
            Assert.That(accepted.Assemblies.Count, Is.EqualTo(fixture.Targets.Count), "a first derivation assembles every target");
            Assert.That(accepted.Delta, Is.Null, "a first derivation has no previous result to diff");
            Assert.That(accepted.ResultHash.IsEmpty, Is.False, "an accepted result carries its canonical hash");
            Assert.That(accepted.Counters.RulesEvaluated, Is.GreaterThan(0), Describe(accepted));
            Assert.That(accepted.Counters.ExaminedCandidates, Is.GreaterThan(0), Describe(accepted));
            Assert.That(accepted.Counters.EmittedContributions, Is.GreaterThan(0), Describe(accepted));
            Assert.That(accepted.Decisions.Count, Is.GreaterThan(0), "every candidate evaluation is reported (P-026)");
        }

        [Test]
        public void EveryGeneratedTargetIsAssembledInItsOwnScope()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();

            Assert.That(baseResult.Assemblies.Count, Is.EqualTo(fixture.Targets.Count));
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                DerivationTarget target = fixture.Targets[t];
                TargetAssembly? found = baseResult.AssemblyOf(target.Target);
                Assert.That(found, Is.Not.Null, "target " + t.ToString() + " has an assembly");
                TargetAssembly assembly = found!;
                Assert.That(assembly.Scope.Equals(target.Scope), Is.True, "target " + t.ToString() + " is assembled in its owner scope");
                Assert.That(assembly.BaseRecipe.Equals(target.Descriptor.Recipe), Is.True, "the base recipe is the descriptor's recipe");
                Assert.That(assembly.RecipeHash.IsEmpty, Is.False, "the derived variant hash is computed, so P-024 can cache by it");
                Assert.That(assembly.EffectiveCapabilities.Count, Is.EqualTo(assembly.Slots.Count), "one effective capability per slot");
            }

            // The declared contract ids exist in the accepted table, whether or not a target ends up carrying them.
            Assert.That(baseResult.Snapshot.Contracts.Find(fixture.SharedCapability), Is.Not.Null);
            Assert.That(baseResult.Snapshot.Contracts.Find(fixture.LocalCapability), Is.Not.Null);
            Assert.That(baseResult.Snapshot.Contracts.Find(fixture.UpdateCapability), Is.Not.Null);
        }

        [Test]
        public void TheSharedCapabilityIsAbsentOnTheExcludedSubtreeTheExcludedTargetAndTheIsolatedLeaf()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();

            Assert.That(
                HasCapability(baseResult, BenchmarkIds.Target(5), fixture.SharedCapability),
                Is.False,
                "the first leaf's declared subtree exclusion keeps the shared capability off its targets (P-016)");
            Assert.That(
                HasCapability(baseResult, BenchmarkIds.Target(1), fixture.SharedCapability),
                Is.False,
                "the declared single target's own exclusion keeps the shared capability off it");

            TargetId isolated = FirstTargetOwnedBy(fixture, fixture.LeafScopes[BenchmarkFixture.ReparentLeafCount]);
            Assert.That(
                HasCapability(baseResult, isolated, fixture.SharedCapability),
                Is.False,
                "the declared named capability boundary blocks the shared capability on that leaf's targets (P-016)");

            TargetId witness = FirstTargetOutside(fixture, fixture.LeafScopes[0], fixture.LeafScopes[BenchmarkFixture.ReparentLeafCount]);
            Assert.That(
                HasCapability(baseResult, witness, fixture.SharedCapability),
                Is.True,
                "an ordinary target inherits the shared capability from the root and its group providers");
            Assert.That(
                HasCapability(baseResult, witness, fixture.UpdateCapability),
                Is.False,
                "the base fixture mounts no update provider, so no target carries that capability yet");

            int slots = 0;
            int empty = 0;
            for (int t = 0; t < baseResult.Assemblies.Count; t++)
            {
                slots += baseResult.Assemblies[t].Slots.Count;
                if (baseResult.Assemblies[t].Slots.Count == 0)
                {
                    empty++;
                }
            }

            Assert.That(
                empty,
                Is.EqualTo(0),
                "every generated target carries at least one effective slot, so the fixture really loads the composition");
            Assert.That(slots, Is.GreaterThanOrEqualTo(fixture.Targets.Count));
        }

        [TestCase(1)]
        [TestCase(100)]
        [TestCase(10000)]
        public void MountingOneUpdateProviderAffectsExactlyTheTagSelectedTargets(int size)
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();
            int expected = fixture.TargetCountForSize(size);
            TargetId witness = BenchmarkIds.Target(expected < fixture.Targets.Count ? expected : 0);

            IncrementalDerivationOutcome outcome = MountUpdate(fixture, baseResult, size);
            AssertAccepted(outcome, "update of size " + size.ToString());

            Assert.That(
                CountWithCapability(baseResult, fixture.UpdateCapability),
                Is.EqualTo(0),
                "the base snapshot carries no update capability anywhere");
            Assert.That(
                CountWithCapability(outcome.Result, fixture.UpdateCapability),
                Is.EqualTo(expected),
                "an update of declared size " + size.ToString() + " affects exactly " + expected.ToString()
                + " targets; " + outcome.Describe());
            Assert.That(
                HasCapability(outcome.Result, BenchmarkIds.Target(0), fixture.UpdateCapability),
                Is.True,
                "the one-target tag is on generated target 0");
            Assert.That(
                HasCapability(outcome.Result, BenchmarkIds.Target(1), fixture.UpdateCapability),
                Is.EqualTo(expected > 1),
                "the update reaches exactly the targets its declared tag selects, and no more");

            GameCore.Derivation.DerivationDelta? delta = outcome.Result.Delta;
            Assert.That(delta, Is.Not.Null, "an incremental derivation against a previous result carries a delta");
            Assert.That(
                delta!.AffectedTargets.Count,
                Is.EqualTo(expected),
                "the delta's affected set is the tag-selected set, not the rule's candidate domain; " + outcome.Describe());
            Assert.That(delta.Added.Count, Is.GreaterThanOrEqualTo(expected), "each affected target gained at least one contribution");

            if (expected < fixture.Targets.Count)
            {
                Assert.That(
                    EffectiveValues(baseResult, witness),
                    Is.EqualTo(EffectiveValues(outcome.Result, witness)),
                    "an unaffected target's effective values are carried unchanged (P-025)");
            }
            else
            {
                Assert.That(
                    EffectiveValues(baseResult, witness),
                    Is.Not.EqualTo(EffectiveValues(outcome.Result, witness)),
                    "a whole-world update changes every target, this one included");
            }

            Assert.That(
                EffectiveValues(baseResult, BenchmarkIds.Target(0)),
                Is.Not.EqualTo(EffectiveValues(outcome.Result, BenchmarkIds.Target(0))),
                "the tagged target's effective values really changed");

            AssertLocality(outcome, "update of size " + size.ToString());
            Assert.That(outcome.UsedFullRecompute, Is.False, "a provider mount is an ordinary edit, so the incremental path ran; " + outcome.Describe());
            Assert.That(outcome.Invalidation.WholeWorld, Is.False, outcome.Describe());
            Assert.That(outcome.Counters.WholeWorld, Is.False, outcome.Describe());
        }

        [Test]
        public void TheReparentWorkloadChangesOnlyTheMovedSubtree()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();
            var branch = new HashSet<Id128>();
            branch.Add(fixture.ReparentScope.Value);
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                if (DeclaredScope(fixture, fixture.LeafScopes[leaf]).Parent.Equals(fixture.ReparentScope))
                {
                    branch.Add(fixture.LeafScopes[leaf].Value);
                }
            }

            int branchTargets = 0;
            TargetId inheritedInBranch = BenchmarkIds.Target(0);
            TargetId excludedInBranch = BenchmarkIds.Target(0);
            bool inheritedFound = false;
            bool excludedFound = false;
            TargetId firstOutside = BenchmarkIds.Target(0);
            bool outsideFound = false;
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                DerivationTarget candidate = fixture.Targets[t];
                if (branch.Contains(candidate.Scope.Value))
                {
                    branchTargets++;
                    // The branch's first leaf declares a subtree exclusion of the shared capability, so its ten
                    // targets inherit nothing from the group provider on either side of the move; every other branch
                    // target changes because the group it inherits from changes.
                    if (candidate.Scope.Equals(fixture.LeafScopes[0]))
                    {
                        if (!excludedFound)
                        {
                            excludedInBranch = candidate.Target;
                            excludedFound = true;
                        }
                    }
                    else if (!inheritedFound)
                    {
                        inheritedInBranch = candidate.Target;
                        inheritedFound = true;
                    }
                }
                else if (!outsideFound)
                {
                    firstOutside = candidate.Target;
                    outsideFound = true;
                }
            }

            Assert.That(inheritedFound, Is.True, "the moved branch has a target that inherits a group provider");
            Assert.That(excludedFound, Is.True, "the moved branch has the declared subtree-excluded leaf");

            Assert.That(branchTargets, Is.EqualTo(fixture.ReparentScopeTargets), "the moved subtree is the declared 100-target one");
            Assert.That(outsideFound, Is.True, "the fixture has targets outside the moved branch");

            DerivationSnapshot snapshot = fixture.Builder()
                .WithVersion(NextRevision(), NextEpoch())
                .ReparentScope(fixture.ReparentScope, fixture.SecondProviderScope)
                .Build();
            IncrementalDerivationOutcome outcome =
                IncrementalDerivationEngine.Derive(snapshot, fixture.Values, Options(), baseResult, null);

            AssertAccepted(outcome, "reparent of the declared subtree");
            Assert.That(outcome.UsedFullRecompute, Is.False, "a move is an ordinary edit, so the incremental path ran; " + outcome.Describe());
            Assert.That(outcome.Invalidation.WholeWorld, Is.False, outcome.Describe());
            Assert.That(outcome.Counters.WholeWorld, Is.False, outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.LessThanOrEqualTo(fixture.ReparentScopeTargets),
                "the closure is bounded by the moved subtree; " + outcome.Describe()
                + "; dirtyTargets=" + outcome.Counters.DirtyTargets.ToString());
            Assert.That(
                outcome.Counters.DirtyTargets * 100,
                Is.LessThanOrEqualTo(fixture.Targets.Count),
                "at most 1% of the world is dirty for a local move; " + outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.LessThan(fixture.Targets.Count),
                "a move must never dirty the whole world; " + outcome.Describe());
            AssertLocality(outcome, "reparent of the declared subtree");

            int changedInBranch = 0;
            int changedOutside = 0;
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                DerivationTarget target = fixture.Targets[t];
                bool changed = !string.Equals(
                    EffectiveValues(baseResult, target.Target),
                    EffectiveValues(outcome.Result, target.Target),
                    StringComparison.Ordinal);
                if (changed && branch.Contains(target.Scope.Value))
                {
                    changedInBranch++;
                }
                else if (changed)
                {
                    changedOutside++;
                }
            }

            Assert.That(
                changedOutside,
                Is.EqualTo(0),
                "a target outside the moved subtree that shares no ancestor with it did not change; "
                + outcome.Describe() + "; changed=" + changedOutside.ToString());
            Assert.That(
                changedInBranch,
                Is.InRange(
                    fixture.ReparentScopeTargets - BenchmarkFixture.ReparentTargetsPerLeaf,
                    fixture.ReparentScopeTargets),
                "the moved subtree's targets changed; the lower bound excludes the branch leaf whose declared subtree"
                + " exclusion keeps the shared capability absent on both sides; changed=" + changedInBranch.ToString());
            Assert.That(
                EffectiveValues(baseResult, inheritedInBranch),
                Is.Not.EqualTo(EffectiveValues(outcome.Result, inheritedInBranch)),
                "a moved target's effective values changed, because the group it inherits from changed");
            Assert.That(
                EffectiveValues(baseResult, excludedInBranch),
                Is.EqualTo(EffectiveValues(outcome.Result, excludedInBranch)),
                "the branch leaf's excluded targets inherit nothing from either group, so the move leaves them unchanged");
            Assert.That(
                EffectiveValues(baseResult, firstOutside),
                Is.EqualTo(EffectiveValues(outcome.Result, firstOutside)),
                "an unrelated target's effective values are carried unchanged (P-025)");
        }

        [Test]
        public void TheSpawnWorkloadDerivesTheCompleteAssemblyOfAThousandNewTargets()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();
            ScopeId host = fixture.GroupScopes[2];
            IReadOnlyList<DerivationTarget> spawned =
                BenchmarkFixtureVariants.SpawnTargets(fixture, host, BenchmarkWorkloads.SpawnTargets, 0);
            IReadOnlyList<TargetId> spawnedIds =
                BenchmarkFixtureVariants.SpawnedTargetIds(BenchmarkWorkloads.SpawnTargets, 0);

            Assert.That(spawned.Count, Is.EqualTo(BenchmarkWorkloads.SpawnTargets));
            Assert.That(spawnedIds.Count, Is.EqualTo(spawned.Count));
            for (int i = 0; i < spawned.Count; i++)
            {
                Assert.That(spawned[i].Target.Equals(spawnedIds[i]), Is.True, "the retire list names the spawned targets (P-024)");
                Assert.That(spawned[i].Scope.Equals(host), Is.True, "every spawned target is owned by the spawn scope");
                Assert.That(spawned[i].Target.IsDefault, Is.False);
            }

            DerivationSnapshot snapshot = fixture.Builder()
                .WithVersion(NextRevision(), NextEpoch())
                .AddTargets(spawned)
                .Build();
            IncrementalDerivationOutcome outcome =
                IncrementalDerivationEngine.Derive(snapshot, fixture.Values, Options(), baseResult, null);

            AssertAccepted(outcome, "spawn of 1000 targets under an active provider");
            Assert.That(outcome.UsedFullRecompute, Is.False, outcome.Describe());
            Assert.That(outcome.Invalidation.WholeWorld, Is.False, outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.EqualTo(spawned.Count),
                "a spawn dirties exactly the created targets (P-024); " + outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.LessThan(fixture.Targets.Count),
                "a spawn is not a whole-world recompute; " + outcome.Describe());
            Assert.That(
                outcome.Counters.CarriedTargets,
                Is.EqualTo(fixture.Targets.Count),
                "every pre-existing target is carried; " + outcome.Describe());
            AssertLocality(outcome, "spawn of 1000 targets");
            Assert.That(outcome.Result.Assemblies.Count, Is.EqualTo(fixture.Targets.Count + spawned.Count));

            for (int i = 0; i < spawned.Count; i++)
            {
                TargetAssembly? found = outcome.Result.AssemblyOf(spawnedIds[i]);
                Assert.That(found, Is.Not.Null, "spawned target " + i.ToString() + " is assembled");
                TargetAssembly assembly = found!;
                Assert.That(assembly.Scope.Equals(host), Is.True);
                Assert.That(
                    assembly.HasCapability(fixture.SharedCapability),
                    Is.True,
                    "spawned target " + i.ToString() + " is first visible with its complete derived assembly (P-024)");
                Assert.That(assembly.HasCapability(fixture.UpdateCapability), Is.False);
                Assert.That(assembly.HasCapability(fixture.LocalCapability), Is.False, "the spawn scope hosts no unique provider");
                Assert.That(assembly.RecipeHash.IsEmpty, Is.False);
            }
        }

        [Test]
        public void AWholeWorldModeSwitchReportsTheWholeWorldAndRecomputesIt()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();

            DerivationSnapshot conservative = fixture.Builder()
                .WithMode(PropagationMode.Conservative)
                .WithVersion(NextRevision(), NextEpoch())
                .Build();
            IncrementalDerivationOutcome outcome =
                IncrementalDerivationEngine.Derive(conservative, fixture.Values, Options(), baseResult, null);

            AssertAccepted(outcome, "whole-world mode switch to Conservative");
            Assert.That(
                outcome.UsedFullRecompute,
                Is.True,
                "a mode switch legitimately invalidates the world and the cost is reported rather than hidden (P-014); "
                + outcome.Describe());
            Assert.That(outcome.Invalidation.WholeWorld, Is.True, outcome.Describe());
            Assert.That(outcome.Counters.WholeWorld, Is.True, outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.EqualTo(fixture.Targets.Count),
                "the whole world is dirty, and saying so is the point; " + outcome.Describe());
            Assert.That(outcome.Counters.CarriedTargets, Is.EqualTo(0), "nothing is carried when the world is recomputed; " + outcome.Describe());
            Assert.That(outcome.Invalidation.DirtyTargets.Count, Is.EqualTo(fixture.Targets.Count));
            Assert.That(
                outcome.Invalidation.ChangeSet.Reasons(),
                Is.Not.Empty,
                "the invalidation names its cause as a stable key (P-052); " + outcome.Describe());
            Assert.That(
                outcome.Invalidation.ChangeSet.ModeChanged,
                Is.True,
                "the change set reports the mode switch rather than the closure inferring it; " + outcome.Describe());

            DerivationSnapshot automatic = fixture.Builder()
                .WithMode(PropagationMode.Automatic)
                .WithVersion(new CompositionRevision(3UL), new AssemblyEpoch(3UL))
                .Build();
            IncrementalDerivationOutcome back =
                IncrementalDerivationEngine.Derive(automatic, fixture.Values, Options(), outcome.Result, null);

            AssertAccepted(back, "whole-world mode switch back to Automatic");
            Assert.That(
                back.UsedFullRecompute || back.Invalidation.WholeWorld,
                Is.True,
                "the switch back is reported the same way as the switch out; usedFullRecompute="
                + (back.UsedFullRecompute ? "true" : "false")
                + "; wholeWorld=" + (back.Invalidation.WholeWorld ? "true" : "false"));
            Assert.That(back.Counters.DirtyTargets, Is.EqualTo(fixture.Targets.Count), back.Describe());
            Assert.That(back.Counters.CarriedTargets, Is.EqualTo(0), back.Describe());
            Assert.That(back.Result.Assemblies.Count, Is.EqualTo(fixture.Targets.Count));
        }

        [Test]
        public void AnUnchangedSnapshotIsCarriedWithoutReDerivingAnything()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();

            // A republished snapshot with the same content and a new revision/epoch: the change set is empty, so the
            // whole previous result is carried and the delta publishes nothing (P-006 `NoChange`).
            DerivationSnapshot republished = fixture.Builder()
                .WithVersion(NextRevision(), NextEpoch())
                .Build();
            IncrementalDerivationOutcome outcome =
                IncrementalDerivationEngine.Derive(republished, fixture.Values, Options(), baseResult, null);

            AssertAccepted(outcome, "unchanged snapshot");
            Assert.That(outcome.UsedFullRecompute, Is.False, outcome.Describe());
            Assert.That(outcome.Invalidation.IsEmpty, Is.True, "nothing can have changed; " + outcome.Describe());
            Assert.That(outcome.Counters.DirtyTargets, Is.EqualTo(0), outcome.Describe());
            Assert.That(outcome.Counters.CarriedTargets, Is.EqualTo(fixture.Targets.Count), outcome.Describe());
            Assert.That(outcome.Counters.CandidateEvaluations, Is.EqualTo(0), "a no-op examines no candidate; " + outcome.Describe());
            Assert.That(outcome.Result.Delta, Is.Not.Null);
            Assert.That(outcome.Result.Delta!.IsEmpty, Is.True, "a no-op publishes nothing (P-006)");
            Assert.That(outcome.Result.Delta.AffectedTargets.Count, Is.EqualTo(0));
            Assert.That(outcome.Result.Assemblies.Count, Is.EqualTo(fixture.Targets.Count));
        }

        [Test]
        public void AnUpdateMountRemovesCleanlyWithoutResidue()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();
            DerivationInstall update = BenchmarkFixtureVariants.UpdateInstall(fixture, 100, 0);

            // Mount it, then publish the fixture again without it: the second snapshot must be exactly the base one
            // again, which is what proves a workload variant leaves no residue for the next workload (P-008).
            DerivationSnapshot mounted = fixture.Builder()
                .WithVersion(NextRevision(), NextEpoch())
                .AddInstall(update)
                .Build();
            IncrementalDerivationOutcome withUpdate =
                IncrementalDerivationEngine.Derive(mounted, fixture.Values, Options(), baseResult, null);
            AssertAccepted(withUpdate, "mount of the size-100 update provider");
            Assert.That(withUpdate.Result.AssemblyOf(BenchmarkIds.Target(0))!.HasCapability(fixture.UpdateCapability), Is.True);

            DerivationSnapshot unmounted = fixture.Builder()
                .WithVersion(new CompositionRevision(3UL), new AssemblyEpoch(3UL))
                .RemoveInstall(update.Instance)
                .Build();
            Assert.That(unmounted.Installs.Count, Is.EqualTo(baseResult.Snapshot.Installs.Count), "the mount is gone");
            IncrementalDerivationOutcome retired =
                IncrementalDerivationEngine.Derive(unmounted, fixture.Values, Options(), withUpdate.Result, null);

            AssertAccepted(retired, "unmount of the size-100 update provider");
            Assert.That(
                CountWithCapability(retired.Result, fixture.UpdateCapability),
                Is.EqualTo(0),
                "the update capability is retracted for every target");
            Assert.That(
                retired.Result.AssemblyOf(BenchmarkIds.Target(0))!.RecipeHash.Equals(
                    baseResult.AssemblyOf(BenchmarkIds.Target(0))!.RecipeHash),
                Is.True,
                "the retracted world is byte-identical to the base publication, so a repetition starts from the same state");
            Assert.That(retired.UsedFullRecompute, Is.False, retired.Describe());
            Assert.That(retired.Invalidation.WholeWorld, Is.False, retired.Describe());
        }

        [Test]
        public void TheIncrementalResultAgreesWithAFullDerivationOfTheSameSnapshot()
        {
            BenchmarkFixture fixture = Fixture();
            DerivationResult baseResult = Base();
            DerivationSnapshot mounted = fixture.Builder()
                .WithVersion(NextRevision(), NextEpoch())
                .AddInstall(BenchmarkFixtureVariants.UpdateInstall(fixture, 100, 0))
                .Build();

            IncrementalDerivationOutcome incremental =
                IncrementalDerivationEngine.Derive(mounted, fixture.Values, Options(), baseResult, null);
            DerivationResult full = DerivationEngine.Derive(mounted, fixture.Values, Options(), null);

            AssertAccepted(incremental, "incremental derivation of the mounted snapshot");
            AssertAccepted(full, "full derivation of the mounted snapshot");
            Assert.That(
                incremental.Result.ResultHash.Equals(full.ResultHash),
                Is.True,
                "P-023's parity claim: the incremental result is the one a full recomputation produces; "
                + incremental.Describe());
            Assert.That(incremental.Result.Assemblies.Count, Is.EqualTo(full.Assemblies.Count));
            Assert.That(
                CountWithCapability(incremental.Result, fixture.UpdateCapability),
                Is.EqualTo(CountWithCapability(full, fixture.UpdateCapability)));
        }

        /// <summary>
        /// The seed series of the equivalence property below: the recorded seed plus two neighbours, so one
        /// property covers the recorded load and observable seed changes rather than one lucky fixture.
        /// </summary>
        private static readonly uint[] PropertySeeds =
        {
            BenchmarkWorkloads.DefaultSeed,
            BenchmarkWorkloads.DefaultSeed + 1U,
            BenchmarkWorkloads.DefaultSeed + 31U,
        };

        private enum LocalEditKind
        {
            LeafMount = 0,
            Reparent = 1,
            Spawn = 2,
            Retire = 3,
        }

        /// <summary>
        /// THE 10,000-TARGET EQUIVALENCE PROPERTY (P-023, TEST-008). For every seed of the recorded series, every
        /// local edit kind is published as a variant of the declared fixture, derived incrementally from an accepted
        /// base publication and derived clean from the same snapshot, and the two must agree on canonical outputs and
        /// provenance — not merely on a count. A disagreement is diagnosed by seed and edit kind.
        ///
        /// The edits are deliberately leaf-local/reparent edits, not the root-mounted update-size workloads: a
        /// root provider's candidate domain is all 10,000 targets even when its predicate affects only one. The
        /// locality assertions here therefore bind only the engine-owned locality counters (`DirtyTargets`,
        /// `CarriedTargets`, `CandidateEvaluations`) and content-level unchanged targets. They do not bind
        /// `TargetsVisited`/`ScopesVisited`/`ControlNodesVisited`, because the incremental path still rebuilds its
        /// index sets over the whole snapshot today; once that kernel work is localized, those control-node bounds
        /// belong in this property too.
        /// </summary>
        [Test]
        public void EveryLocalEditOfTheDeclaredWorldIsIncrementallyEquivalentToACleanDerivation()
        {
            for (int seedIndex = 0; seedIndex < PropertySeeds.Length; seedIndex++)
            {
                uint seed = PropertySeeds[seedIndex];
                BenchmarkFixture fixture = FixtureForSeed(seed);
                Assert.That(
                    fixture.Targets.Count,
                    Is.EqualTo(BenchmarkWorkloads.DefaultTargets),
                    "the property runs at the declared 10,000-target scale for seed "
                    + seed.ToString(CultureInfo.InvariantCulture));
                DerivationOptions options = OptionsForFixture(fixture);
                DerivationResult baseResult = BaseForFixture(fixture, options);
                DerivationResult? spawnedPrevious = null;

                for (int edit = 0; edit < LocalEditCount; edit++)
                {
                    LocalEditKind kind = (LocalEditKind)edit;
                    string label = "seed " + seed.ToString(CultureInfo.InvariantCulture)
                        + " edit " + kind.ToString();
                    DerivationResult previous = PreviousForEdit(
                        fixture, options, baseResult, kind, ref spawnedPrevious);
                    DerivationSnapshot after = ComposeLocalEdit(fixture, kind);

                    IncrementalDerivationOutcome outcome =
                        IncrementalDerivationEngine.Derive(after, fixture.Values, options, previous, null);
                    DerivationResult clean = DerivationEngine.Derive(after, fixture.Values, options, null);
                    if (kind == LocalEditKind.Spawn)
                    {
                        spawnedPrevious = clean;
                    }

                    AssertAccepted(outcome, "incremental derivation of " + label);
                    AssertAccepted(clean, "clean derivation of " + label);
                    Assert.That(
                        outcome.UsedFullRecompute,
                        Is.False,
                        label + " is an ordinary local edit, so the incremental path ran; " + outcome.Describe());
                    Assert.That(outcome.Invalidation.WholeWorld, Is.False, label + "; " + outcome.Describe());
                    Assert.That(outcome.Counters.WholeWorld, Is.False, label + "; " + outcome.Describe());

                    AssertCanonicalResultEqual(outcome.Result, clean, label, outcome.Describe());
                    AssertLocalEditBounded(outcome, clean, fixture, kind, label);
                    AssertUnchangedOutsideEdit(fixture, baseResult, outcome.Result, kind, label);
                }
            }
        }

        private static readonly int LocalEditCount = Enum.GetValues(typeof(LocalEditKind)).Length;

        private static DerivationResult PreviousForEdit(
            BenchmarkFixture fixture,
            DerivationOptions options,
            DerivationResult baseResult,
            LocalEditKind kind,
            ref DerivationResult? spawnedPrevious)
        {
            if (kind != LocalEditKind.Retire)
            {
                return baseResult;
            }

            if (spawnedPrevious == null)
            {
                DerivationSnapshot spawned = ComposeLocalEdit(fixture, LocalEditKind.Spawn);
                spawnedPrevious = DerivationEngine.Derive(spawned, fixture.Values, options, null);
                AssertAccepted(spawnedPrevious!, "clean spawn publication used as the retire base");
            }

            return spawnedPrevious;
        }

        private static DerivationSnapshot ComposeLocalEdit(BenchmarkFixture fixture, LocalEditKind kind)
        {
            BenchmarkSnapshotBuilder builder = fixture.Builder()
                .WithVersion(new CompositionRevision(2UL + (ulong)kind), new AssemblyEpoch(2UL + (ulong)kind));
            switch (kind)
            {
                case LocalEditKind.LeafMount:
                    return builder.AddInstall(LeafLocalInstall(fixture)).Build();
                case LocalEditKind.Reparent:
                    return builder.ReparentScope(fixture.ReparentScope, fixture.SecondProviderScope).Build();
                case LocalEditKind.Spawn:
                    return builder.AddTargets(
                            BenchmarkFixtureVariants.SpawnTargets(
                                fixture, fixture.GroupScopes[2], BenchmarkWorkloads.SpawnTargets, 0))
                        .Build();
                case LocalEditKind.Retire:
                    return builder.RemoveTargets(
                            BenchmarkFixtureVariants.SpawnedTargetIds(BenchmarkWorkloads.SpawnTargets, 0))
                        .Build();
                default:
                    throw new InvalidOperationException("unknown local edit kind " + kind.ToString() + ".");
            }
        }

        private static DerivationInstall LeafLocalInstall(BenchmarkFixture fixture)
        {
            int installIndex = 400000 + (int)(fixture.Scale.Seed % 100000U);
            ScopeId leaf = fixture.LeafScopes[LeafIndexForSeed(fixture)];
            PluginInstanceId instance = BenchmarkIds.Instance(installIndex);
            PluginTypeId pluginType = BenchmarkIds.PluginType(installIndex);
            FactoryKey factoryKey = BenchmarkIds.Key(
                "bench.factory.leaf-local-property-" + installIndex.ToString(CultureInfo.InvariantCulture));
            var rule = new DerivationRule(
                BenchmarkIds.Rule(17, installIndex),
                BenchmarkIds.CapabilityRef(BenchmarkNames.SharedCapability),
                0,
                1U,
                FamilySelectors(),
                BenchmarkIds.Key(BenchmarkNames.AlwaysPredicate),
                null,
                PropagationReach.LocalOnly,
                false,
                installIndex,
                CompositionPolicy.Additive,
                FixturePayload.Int32(1000 + (installIndex % 997)));
            var manifest = new PluginManifest(
                pluginType,
                "1.0.0",
                ContentHash.Empty,
                new SupportedProtocolRange(1, 0, 0),
                null,
                BenchmarkIds.SchemaRef(BenchmarkNames.SharedSchema),
                factoryKey,
                null,
                null,
                new List<CapabilityContract>(),
                new List<DerivationRule> { rule },
                null,
                null,
                null,
                null,
                null);
            var record = new InstallRecord(
                instance,
                pluginType,
                leaf,
                DefinitionRevision.First,
                ContentHash.Empty,
                installIndex,
                InstallationGeneration.First,
                ActivationEpoch.First);
            return new DerivationInstall(record, InstallationState.Active, manifest);
        }

        private static List<SchemaRef> FamilySelectors()
        {
            var selectors = new List<SchemaRef>(BenchmarkFixture.SchemaFamilies);
            for (int family = 0; family < BenchmarkFixture.SchemaFamilies; family++)
            {
                selectors.Add(BenchmarkIds.SchemaRef(BenchmarkNames.FamilySchema(family)));
            }

            return selectors;
        }

        private static int LeafIndexForSeed(BenchmarkFixture fixture)
        {
            int available = fixture.LeafScopes.Count - 2;
            int leaf = (int)(fixture.Scale.Seed % (uint)available) + 1;
            if (leaf >= BenchmarkFixture.ReparentLeafCount)
            {
                leaf++;
            }

            return leaf;
        }

        private static void AssertCanonicalResultEqual(
            DerivationResult incremental,
            DerivationResult clean,
            string label,
            string outcome)
        {
            Assert.That(
                incremental.ResultHash.Equals(clean.ResultHash),
                Is.True,
                label + ": P-023's parity claim is the canonical result hash; incremental="
                + incremental.ResultHash.ToHex() + "; clean=" + clean.ResultHash.ToHex() + "; " + outcome);
            Assert.That(incremental.Assemblies.Count, Is.EqualTo(clean.Assemblies.Count), label);
            for (int i = 0; i < incremental.Assemblies.Count; i++)
            {
                TargetAssembly left = incremental.Assemblies[i];
                TargetAssembly right = clean.Assemblies[i];
                Assert.That(left.Target.Equals(right.Target), Is.True, label + ": assembly order differs at " + i.ToString(CultureInfo.InvariantCulture));
                string leftText = AssemblyText(left);
                string rightText = AssemblyText(right);
                Assert.That(
                    leftText,
                    Is.EqualTo(rightText),
                    label + ": target " + left.Target.ToString()
                    + " differs between incremental and clean assembly text; " + FirstDifference(leftText, rightText));
            }

            string incrementalProvenance = ProvenanceText(incremental);
            string cleanProvenance = ProvenanceText(clean);
            Assert.That(
                incrementalProvenance,
                Is.EqualTo(cleanProvenance),
                label + ": contribution provenance differs; " + FirstDifference(incrementalProvenance, cleanProvenance));
        }

        private static string AssemblyText(TargetAssembly assembly)
        {
            var text = new StringBuilder();
            text.Append("target=").Append(assembly.Target.ToString()).Append('\n');
            text.Append("scope=").Append(assembly.Scope.ToString()).Append('\n');
            text.Append("recipe=").Append(assembly.BaseRecipe.ToString()).Append('\n');
            text.Append("recipeHash=").Append(assembly.RecipeHash.ToHex()).Append('\n');
            for (int s = 0; s < assembly.Slots.Count; s++)
            {
                EffectiveSlot slot = assembly.Slots[s];
                text.Append("slot=").Append(slot.Capability.ToString())
                    .Append('/').Append(slot.Version.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(slot.Stratum.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(slot.Slot.ToString(CultureInfo.InvariantCulture))
                    .Append('/').Append(slot.Schema.ToString())
                    .Append('/').Append(slot.Policy.ToString())
                    .Append('/').Append(slot.Hash.ToHex())
                    .Append('\n');
                AppendContributionLines(text, "support=", slot.Support);
                AppendContributionLines(text, "shadowed=", slot.Shadowed);
            }

            return text.ToString();
        }

        private static string ProvenanceText(DerivationResult result)
        {
            var lines = new List<string>(result.Contributions.Count);
            for (int i = 0; i < result.Contributions.Count; i++)
            {
                lines.Add(ContributionText(result.Contributions[i]));
            }

            lines.Sort(StringComparer.Ordinal);
            var text = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                text.Append("contribution=").Append(lines[i]).Append('\n');
            }

            return text.ToString();
        }

        private static void AppendContributionLines(
            StringBuilder text,
            string prefix,
            IReadOnlyList<CapabilityContribution> contributions)
        {
            for (int i = 0; i < contributions.Count; i++)
            {
                text.Append(prefix).Append(ContributionText(contributions[i])).Append('\n');
            }
        }

        private static string ContributionText(CapabilityContribution contribution) =>
            contribution.Provider.ToString() + '/' + contribution.Rule.ToString() + '/' + contribution.Target.ToString()
            + '/' + contribution.Capability.ToString()
            + '/' + contribution.OutputSlot.ToString(CultureInfo.InvariantCulture)
            + '/' + contribution.Schema.ToString()
            + '/' + contribution.Policy.ToString()
            + '/' + contribution.PayloadHash.ToHex()
            + '/' + contribution.Disposition.ToString();

        private static void AssertLocalEditBounded(
            IncrementalDerivationOutcome outcome,
            DerivationResult clean,
            BenchmarkFixture fixture,
            LocalEditKind kind,
            string label)
        {
            int expectedDirty = ExpectedDirtyTargets(fixture, kind);
            int candidateBound = CandidateEvaluationBound(fixture, kind);
            if (kind == LocalEditKind.Reparent)
            {
                Assert.That(outcome.Counters.DirtyTargets, Is.GreaterThan(0), label + "; " + outcome.Describe());
                Assert.That(outcome.Counters.DirtyTargets, Is.LessThanOrEqualTo(expectedDirty), label + "; " + outcome.Describe());
            }
            else
            {
                Assert.That(outcome.Counters.DirtyTargets, Is.EqualTo(expectedDirty), label + "; " + outcome.Describe());
            }

            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.LessThan(fixture.Targets.Count),
                label + ": a local edit never dirties the world; " + outcome.Describe());
            Assert.That(
                outcome.Counters.CarriedTargets,
                Is.EqualTo(outcome.Result.Assemblies.Count - outcome.Counters.DirtyTargets),
                label + ": every target is either re-derived or carried, never double-counted; " + outcome.Describe());
            Assert.That(
                clean.Counters.ExaminedCandidates,
                Is.GreaterThan(candidateBound),
                label + ": the clean derivation is the full candidate enumeration baseline; clean="
                + clean.Counters.ExaminedCandidates.ToString(CultureInfo.InvariantCulture)
                + "; localBound=" + candidateBound.ToString(CultureInfo.InvariantCulture));
            Assert.That(
                outcome.Counters.CandidateEvaluations,
                Is.LessThanOrEqualTo(candidateBound),
                label + ": dirty-path candidate evaluations are bounded by the local edit, so a full candidate"
                + " enumeration would fail this assertion; " + outcome.Describe()
                + "; bound=" + candidateBound.ToString(CultureInfo.InvariantCulture));
        }

        private static int ExpectedDirtyTargets(BenchmarkFixture fixture, LocalEditKind kind)
        {
            switch (kind)
            {
                case LocalEditKind.LeafMount:
                    return TargetsInScope(fixture, fixture.LeafScopes[LeafIndexForSeed(fixture)]);
                case LocalEditKind.Reparent:
                    return fixture.ReparentScopeTargets;
                case LocalEditKind.Spawn:
                    return BenchmarkWorkloads.SpawnTargets;
                case LocalEditKind.Retire:
                    return 0;
                default:
                    throw new InvalidOperationException("unknown local edit kind " + kind.ToString() + ".");
            }
        }

        private static int CandidateEvaluationBound(BenchmarkFixture fixture, LocalEditKind kind)
        {
            switch (kind)
            {
                case LocalEditKind.LeafMount:
                    int leaf = LeafIndexForSeed(fixture);
                    return TargetsInScope(fixture, fixture.LeafScopes[leaf]) * RulesReachingLeaf(fixture, leaf, true);
                case LocalEditKind.Reparent:
                    return fixture.ReparentScopeTargets * (2 + 2 + UniqueProvidersInReparentBranch(fixture));
                case LocalEditKind.Spawn:
                    return BenchmarkWorkloads.SpawnTargets * 3;
                case LocalEditKind.Retire:
                    return 0;
                default:
                    throw new InvalidOperationException("unknown local edit kind " + kind.ToString() + ".");
            }
        }

        private static int RulesReachingLeaf(BenchmarkFixture fixture, int leaf, bool includesMountedLeafProvider)
        {
            // Root provider + owning group provider; a stride leaf may also host the fixture's unique local provider.
            int rules = 2 + (leaf % BenchmarkFixture.UniqueProviderStride == 0 ? 1 : 0);
            return includesMountedLeafProvider ? rules + 1 : rules;
        }

        private static int UniqueProvidersInReparentBranch(BenchmarkFixture fixture)
        {
            int count = 0;
            for (int leaf = 0; leaf < BenchmarkFixture.ReparentLeafCount && leaf < fixture.LeafScopes.Count; leaf++)
            {
                if (leaf % BenchmarkFixture.UniqueProviderStride == 0)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertUnchangedOutsideEdit(
            BenchmarkFixture fixture,
            DerivationResult baseResult,
            DerivationResult after,
            LocalEditKind kind,
            string label)
        {
            HashSet<Id128> localTargets = LocalEditTargets(fixture, kind);
            var afterByTarget = new Dictionary<Id128, TargetAssembly>(after.Assemblies.Count);
            for (int i = 0; i < after.Assemblies.Count; i++)
            {
                afterByTarget[after.Assemblies[i].Target.Value] = after.Assemblies[i];
            }

            int checkedTargets = 0;
            for (int i = 0; i < baseResult.Assemblies.Count; i++)
            {
                TargetAssembly before = baseResult.Assemblies[i];
                if (localTargets.Contains(before.Target.Value))
                {
                    continue;
                }

                Assert.That(
                    afterByTarget.TryGetValue(before.Target.Value, out TargetAssembly? carried),
                    Is.True,
                    label + ": non-local target " + before.Target.ToString() + " disappeared.");
                Assert.That(
                    carried!.RecipeHash.Equals(before.RecipeHash),
                    Is.True,
                    label + ": non-local target " + before.Target.ToString() + " changed from "
                    + before.RecipeHash.ToHex() + " to " + carried.RecipeHash.ToHex());
                checkedTargets++;
            }

            Assert.That(checkedTargets, Is.GreaterThan(0), label + ": the edit had carried witnesses outside its reach.");
        }

        private static HashSet<Id128> LocalEditTargets(BenchmarkFixture fixture, LocalEditKind kind)
        {
            var targets = new HashSet<Id128>();
            switch (kind)
            {
                case LocalEditKind.LeafMount:
                    CollectScopeTargets(fixture, fixture.LeafScopes[LeafIndexForSeed(fixture)], targets);
                    break;
                case LocalEditKind.Reparent:
                    CollectSubtreeTargets(fixture, fixture.ReparentScope, targets);
                    break;
                case LocalEditKind.Spawn:
                case LocalEditKind.Retire:
                    IReadOnlyList<TargetId> spawned = BenchmarkFixtureVariants.SpawnedTargetIds(
                        BenchmarkWorkloads.SpawnTargets, 0);
                    for (int i = 0; i < spawned.Count; i++)
                    {
                        targets.Add(spawned[i].Value);
                    }

                    break;
                default:
                    throw new InvalidOperationException("unknown local edit kind " + kind.ToString() + ".");
            }

            return targets;
        }

        private static void CollectScopeTargets(BenchmarkFixture fixture, ScopeId scope, HashSet<Id128> into)
        {
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (fixture.Targets[t].Scope.Equals(scope))
                {
                    into.Add(fixture.Targets[t].Target.Value);
                }
            }
        }

        private static void CollectSubtreeTargets(BenchmarkFixture fixture, ScopeId root, HashSet<Id128> into)
        {
            var subtree = new HashSet<Id128> { root.Value };
            for (int leaf = 0; leaf < fixture.LeafScopes.Count; leaf++)
            {
                if (DeclaredScope(fixture, fixture.LeafScopes[leaf]).Parent.Equals(root))
                {
                    subtree.Add(fixture.LeafScopes[leaf].Value);
                }
            }

            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (subtree.Contains(fixture.Targets[t].Scope.Value))
                {
                    into.Add(fixture.Targets[t].Target.Value);
                }
            }
        }

        private static int TargetsInScope(BenchmarkFixture fixture, ScopeId scope)
        {
            int count = 0;
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (fixture.Targets[t].Scope.Equals(scope))
                {
                    count++;
                }
            }

            return count;
        }

        private static BenchmarkFixture FixtureForSeed(uint seed)
        {
            if (seed == BenchmarkWorkloads.DefaultSeed)
            {
                return Fixture();
            }

            return BenchmarkFixtureGenerator.Generate(new BenchmarkScale(
                BenchmarkScale.Default.Scopes,
                BenchmarkScale.Default.Targets,
                BenchmarkScale.Default.LiveScopes,
                BenchmarkScale.Default.LiveTargets,
                seed));
        }

        private static DerivationOptions OptionsForFixture(BenchmarkFixture fixture) =>
            new DerivationOptions(null, null, fixture.SuggestedBudget, null, true);

        private static DerivationResult BaseForFixture(BenchmarkFixture fixture, DerivationOptions options)
        {
            if (fixture.Scale.Seed == BenchmarkWorkloads.DefaultSeed)
            {
                return Base();
            }

            DerivationResult result = DerivationEngine.Derive(fixture.Builder().Build(), fixture.Values, options, null);
            AssertAccepted(
                result,
                "base derivation of seed " + fixture.Scale.Seed.ToString(CultureInfo.InvariantCulture) + " fixture");
            return result;
        }

        private static string FirstDifference(string left, string right)
        {
            int limit = left.Length < right.Length ? left.Length : right.Length;
            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < limit; i++)
            {
                if (left[i] != right[i])
                {
                    return DifferenceAt(left, right, line, lineStart, i);
                }

                if (left[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            if (left.Length == right.Length)
            {
                return "no textual difference";
            }

            return DifferenceAt(left, right, line, lineStart, limit);
        }

        private static string DifferenceAt(string left, string right, int line, int lineStart, int offset)
        {
            string leftLine = LineAt(left, lineStart);
            string rightLine = LineAt(right, lineStart);
            return "line " + line.ToString(CultureInfo.InvariantCulture)
                + ", offset " + offset.ToString(CultureInfo.InvariantCulture)
                + "; incremental='" + leftLine + "'; clean='" + rightLine + "'";
        }

        private static string LineAt(string text, int start)
        {
            if (start >= text.Length)
            {
                return "<end>";
            }

            int end = start;
            while (end < text.Length && text[end] != '\n')
            {
                end++;
            }

            return text.Substring(start, end - start);
        }

        private static BenchmarkFixture Fixture()
        {
            BenchmarkFixture? fixture = sharedFixture;
            if (fixture == null)
            {
                fixture = BenchmarkFixtureGenerator.Generate(BenchmarkScale.Default);
                sharedFixture = fixture;
            }

            return fixture;
        }

        private static DerivationOptions Options()
        {
            DerivationOptions? options = sharedOptions;
            if (options == null)
            {
                options = new DerivationOptions(null, null, ConfiguredBudget(), null, true);
                sharedOptions = options;
            }

            return options;
        }

        private static DerivationResult Base()
        {
            DerivationResult? result = sharedBase;
            if (result == null)
            {
                BenchmarkFixture fixture = Fixture();
                result = DerivationEngine.Derive(fixture.Builder().Build(), fixture.Values, Options(), null);
                AssertAccepted(result, "base derivation of the declared fixture");
                sharedBase = result;
            }

            return result;
        }

        /// <summary>
        /// The budget these tests configure for the declared load: the fixture's own shape-derived
        /// <see cref="BenchmarkFixture.SuggestedBudget"/>, cached so the options and the assertions observe one
        /// instance. One definition, used by the player's benchmark scenario and by this suite, so the two cannot
        /// disagree about what "the fixture was accepted" means.
        /// </summary>
        private static PropagationBudget ConfiguredBudget()
        {
            PropagationBudget? budget = sharedBudget;
            if (budget == null)
            {
                budget = Fixture().SuggestedBudget;
                sharedBudget = budget;
            }

            return budget;
        }

        private static CompositionRevision NextRevision() => new CompositionRevision(2UL);

        private static AssemblyEpoch NextEpoch() => new AssemblyEpoch(2UL);

        private static IncrementalDerivationOutcome MountUpdate(
            BenchmarkFixture fixture,
            DerivationResult baseResult,
            int size)
        {
            DerivationSnapshot snapshot = fixture.Builder()
                .WithVersion(NextRevision(), NextEpoch())
                .AddInstall(BenchmarkFixtureVariants.UpdateInstall(fixture, size, 0))
                .Build();
            return IncrementalDerivationEngine.Derive(snapshot, fixture.Values, Options(), baseResult, null);
        }

        private static void AssertAccepted(DerivationResult result, string label)
        {
            Assert.That(result.Accepted, Is.True, label + " must be accepted; " + Describe(result));
            Assert.That(result.Rejection, Is.EqualTo(DerivationRejectionKind.None), label + "; " + Describe(result));
            Assert.That(result.ValidationProblems.Count, Is.EqualTo(0), label + " has no validation problem; " + Describe(result));
            Assert.That(result.CompositionFailures.Count, Is.EqualTo(0), label + " has no composition failure; " + Describe(result));
        }

        private static void AssertAccepted(IncrementalDerivationOutcome outcome, string label)
        {
            Assert.That(outcome.Result.Accepted, Is.True, label + " must be accepted; " + outcome.Describe());
            Assert.That(
                outcome.Result.Rejection,
                Is.EqualTo(DerivationRejectionKind.None),
                label + "; " + outcome.Describe());
            Assert.That(
                outcome.Result.ValidationProblems.Count,
                Is.EqualTo(0),
                label + " has no validation problem; " + outcome.Describe());
            Assert.That(
                outcome.Result.CompositionFailures.Count,
                Is.EqualTo(0),
                label + " has no composition failure; " + outcome.Describe());
        }

        /// <summary>The invalidation counters a caller can cross-check, recorded in every failure message.</summary>
        private static void AssertLocality(IncrementalDerivationOutcome outcome, string label)
        {
            Assert.That(
                outcome.Invalidation.DirtyTargets.Count,
                Is.EqualTo(outcome.Counters.DirtyTargets),
                label + ": the closure and the counters must report the same dirty target count; " + outcome.Describe());
            Assert.That(
                outcome.Counters.CarriedTargets,
                Is.EqualTo(outcome.Result.Assemblies.Count - outcome.Counters.DirtyTargets),
                label + ": every target is either re-derived or carried, never double-counted; " + outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyTargets,
                Is.LessThanOrEqualTo(outcome.Result.Assemblies.Count),
                label + ": a dirty set is a subset of the published world; " + outcome.Describe());
            Assert.That(
                outcome.Counters.DirtyScopes,
                Is.LessThanOrEqualTo(Fixture().Scopes.Count),
                label + ": a dirty scope set is a subset of the world's scopes; " + outcome.Describe());
            Assert.That(
                outcome.Invalidation.DirtyScopes.Count,
                Is.EqualTo(outcome.Counters.DirtyScopes),
                label + ": the closure and the counters agree on the dirty scope count; " + outcome.Describe());
        }

        private static string Describe(DerivationResult result)
        {
            var text = new StringBuilder();
            text.Append("accepted=").Append(result.Accepted ? "true" : "false");
            text.Append("; rejection=").Append(result.Rejection.ToString());
            text.Append("; diagnostic=").Append(result.DiagnosticCode.ToString());
            text.Append("; counters=").Append(result.Counters.Describe());
            text.Append("; budgetDimension=").Append(result.Counters.ExceededDimension.ToString());
            text.Append("; observed=").Append(result.Counters.ExceededCount.ToString());
            text.Append("; limit=").Append(result.Counters.ExceededLimit.ToString());
            text.Append("; assemblies=").Append(result.Assemblies.Count.ToString());
            text.Append("; problems=").Append(result.ValidationProblems.Count.ToString());
            for (int i = 0; i < result.ValidationProblems.Count; i++)
            {
                text.Append(" | ").Append(result.ValidationProblems[i].ToString());
            }

            text.Append("; failures=").Append(result.CompositionFailures.Count.ToString());
            for (int i = 0; i < result.CompositionFailures.Count; i++)
            {
                text.Append(" | ").Append(result.CompositionFailures[i].ToString());
            }

            return text.ToString();
        }

        private static int CountWithCapability(DerivationResult result, CapabilityId capability)
        {
            int count = 0;
            for (int i = 0; i < result.Assemblies.Count; i++)
            {
                if (result.Assemblies[i].HasCapability(capability))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasCapability(DerivationResult result, TargetId target, CapabilityId capability)
        {
            TargetAssembly? assembly = result.AssemblyOf(target);
            return assembly != null && assembly.HasCapability(capability);
        }

        /// <summary>
        /// Canonical text of one target's effective values: each slot's capability, slot index and value hash. Two
        /// results compare equal exactly when every effective value of that target is unchanged, which is what "the
        /// target did not change" has to mean for a carried assembly.
        /// </summary>
        private static string EffectiveValues(DerivationResult result, TargetId target)
        {
            TargetAssembly? assembly = result.AssemblyOf(target);
            if (assembly == null)
            {
                return "<no assembly>";
            }

            var text = new StringBuilder();
            for (int s = 0; s < assembly.Slots.Count; s++)
            {
                EffectiveSlot slot = assembly.Slots[s];
                text.Append(slot.Capability.ToString())
                    .Append('#')
                    .Append(slot.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture))
                    .Append('=')
                    .Append(slot.Hash.ToHex())
                    .Append(';');
            }

            if (assembly.EffectiveCapabilities.Count != assembly.Slots.Count)
            {
                text.Append("<capability-count-mismatch>");
            }

            return text.ToString();
        }

        private static DerivationScope DeclaredScope(BenchmarkFixture fixture, ScopeId scope)
        {
            for (int i = 0; i < fixture.Scopes.Count; i++)
            {
                if (fixture.Scopes[i].Scope.Equals(scope))
                {
                    return fixture.Scopes[i];
                }
            }

            throw new InvalidOperationException("scope " + scope.ToString() + " is not part of the fixture (P-010).");
        }

        private static TargetId FirstTargetOwnedBy(BenchmarkFixture fixture, ScopeId scope)
        {
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                if (fixture.Targets[t].Scope.Equals(scope))
                {
                    return fixture.Targets[t].Target;
                }
            }

            throw new InvalidOperationException("no target is owned by " + scope.ToString() + " (P-010).");
        }

        private static TargetId FirstTargetOutside(BenchmarkFixture fixture, params ScopeId[] excluded)
        {
            for (int t = 0; t < fixture.Targets.Count; t++)
            {
                bool skip = false;
                for (int e = 0; e < excluded.Length; e++)
                {
                    if (fixture.Targets[t].Scope.Equals(excluded[e]))
                    {
                        skip = true;
                        break;
                    }
                }

                if (!skip)
                {
                    return fixture.Targets[t].Target;
                }
            }

            throw new InvalidOperationException("every generated target is in an excluded scope.");
        }
    }
}
