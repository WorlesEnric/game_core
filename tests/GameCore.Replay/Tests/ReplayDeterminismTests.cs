// GameCore.Replay tests — the 10,000-step integer fixture over the real derivation engine (GC-023, TEST-022).
//
// The acceptance criteria this suite defends, verbatim from TEST-022 and GC-023's brief:
//
//   * "Record at least 10,000 logical steps for a pure integer-rule fixture" — the recorded trace has 10,000 steps
//     and every step commits exactly one logical step id.
//   * "Replay the same admitted order/batches with shuffled internal producer/completion order and supported worker
//     counts of 1, 2 and 4" — the four worker counts and a shuffled-completion run all compare equal, step by step.
//   * "Compare canonical state, decisions, committed event identities and capability provenance after every step."
//   * "A test that changes host admission or batch boundaries is a different input trace and does not require
//     identical gameplay results" — a mutated trace is reported as a different input, never as a replay failure.
//   * "Map fresh runtime WorldId values to a fixture world ordinal only in comparison output" — the ordinal map is
//     a comparison structure and the runtime identity is untouched.
//
// Sources in this folder run as plain-dotnet tests and as Unity EditMode tests.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using GameCore.Derivation;
using NUnit.Framework;

namespace GameCore.Replay.Tests
{
    [TestFixture]
    public sealed class ReplayFixtureTests
    {
        private static ReplayFixtureShape SmallShape =>
            new ReplayFixtureShape(
                branches: 2,
                targetsPerBranch: 3,
                isolatedTargets: 2,
                providersPerBranch: 2,
                steps: 240,
                maxWorkers: 4,
                catalogRevision: 1U);

        [Test]
        public void TheGeneratorIsDeterministicAndTheRecordHashesAreStable()
        {
            ReplayFixtureShape shape = SmallShape;
            ReplayTrace first = IntegerFixtureGenerator.Generate("t", 4242U, shape);
            ReplayTrace second = IntegerFixtureGenerator.Generate("t", 4242U, shape);
            ReplayTrace other = IntegerFixtureGenerator.Generate("t", 4243U, shape);

            Assert.That(second.Describe(), Is.EqualTo(first.Describe()));
            Assert.That(second.CatalogHash.Equals(first.CatalogHash), Is.True);
            Assert.That(other.CatalogHash.Equals(first.CatalogHash), Is.True, "the seed is not part of the shape");
            Assert.That(other.Steps[0].ProducerSeed, Is.Not.EqualTo(first.Steps[0].ProducerSeed));
            Assert.That(first.Steps.Count, Is.EqualTo(shape.Steps));
        }

        [Test]
        public void ACompositionOperationIsRecordedAndTheRestOfTheStepsAreCarried()
        {
            // The recorded script places a composition operation every `providerStride` steps, and the 997-step
            // default only fits the 10,000-step reference shape: at stride 16 this 240-step shape records fourteen
            // operations covering all four branches of the ordinal switch, including a mount and a move or a mode
            // switch, which is what the assertions below need.
            ReplayTrace trace = IntegerFixtureGenerator.Generate("t", 7U, SmallShape, providerStride: 16);

            // The recorded script interleaves composition operations with unchanged steps: TEST-023's zero-work case
            // needs steps where nothing changes, and the script must actually contain them.
            Assert.That(trace.CompositionStepCount, Is.GreaterThan(0));
            Assert.That(trace.CompositionStepCount, Is.LessThan(trace.Steps.Count));

            bool sawMount = false;
            bool sawMoveOrMode = false;
            for (int i = 0; i < trace.Steps.Count; i++)
            {
                if (trace.Steps[i].Operation == ReplayOperationKind.MountProvider)
                {
                    sawMount = true;
                }

                if (trace.Steps[i].Operation == ReplayOperationKind.MoveTarget
                    || trace.Steps[i].Operation == ReplayOperationKind.SwitchMode)
                {
                    sawMoveOrMode = true;
                }
            }

            Assert.That(sawMount, Is.True, "the script must mount a provider");
            Assert.That(sawMoveOrMode, Is.True, "the script must move a target or switch the propagation mode");
        }

        [Test]
        public void SupportedWorkerCountsCoverOneTwoFourAndTheMaximum()
        {
            IReadOnlyList<int> counts = WorkerSchedule.SupportedWorkerCounts(8);
            Assert.That(counts, Is.EqualTo(new[] { 1, 2, 4, 8 }));
            Assert.That(WorkerSchedule.SupportedWorkerCounts(4), Is.EqualTo(new[] { 1, 2, 4 }));
            Assert.That(WorkerSchedule.WorkersForStep(0, 4), Is.EqualTo(1));
            Assert.That(WorkerSchedule.WorkersForStep(1, 4), Is.EqualTo(2));
            Assert.That(WorkerSchedule.WorkersForStep(2, 4), Is.EqualTo(4));
            Assert.That(WorkerSchedule.WorkersForStep(3, 4), Is.EqualTo(1));
        }

        [Test]
        public void TheProducerOrdersArePermutationsAndTheCompletionOrderReallyShuffles()
        {
            int[] start = WorkerSchedule.StartOrder(9, 4, 1234U);
            int[] completion = WorkerSchedule.CompletionOrder(9, 1234U);
            Assert.That(IsPermutation(start), Is.True, "the start order is a permutation of the producers");
            Assert.That(IsPermutation(completion), Is.True, "the completion order is a permutation of the producers");

            bool differs = false;
            for (int i = 0; i < completion.Length; i++)
            {
                if (completion[i] != i)
                {
                    differs = true;
                    break;
                }
            }

            Assert.That(differs, Is.True, "a seeded completion order must actually reorder independent work");
            Assert.That(WorkerSchedule.CompletionOrder(9, 1234U), Is.EqualTo(completion), "and it is reproducible");
        }

        [Test]
        public void TheEmptyTraceProducesEmptyHashesRatherThanThrowing()
        {
            ReplayTrace empty = new ReplayTrace("empty", 1U, SmallShape, Array.Empty<ReplayStepRecord>(), null);
            ReplayRun run = new ReplayRunner(ReplayOptions.Reference).Run(empty);
            Assert.That(run.Steps.Count, Is.EqualTo(0));
            Assert.That(run.CommittedStepCount, Is.EqualTo(0));
            Assert.That(run.Hashes.FinalState.IsEmpty, Is.True);
        }

        private static bool IsPermutation(int[] values)
        {
            var seen = new bool[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] < 0 || values[i] >= values.Length || seen[values[i]])
                {
                    return false;
                }

                seen[values[i]] = true;
            }

            return true;
        }
    }

    [TestFixture]
    public sealed class ReplayDeterminismTests
    {
        /// <summary>
        /// The reference configuration of the sweep. The trace is the 10,000-step record TEST-022 names; the shape
        /// keeps the world small so the sweep measures the replay, not the fixture's size (10,000 comparisons of
        /// state, decisions, provenance and event identities).
        /// </summary>
        private const string RecordLabel = "integer-10000";

        private const uint Seed = 20260923U;

        private static ReplayFixtureShape RecordShape => IntegerFixtureGenerator.DefaultShape;

        private static ReplayTrace Record() =>
            IntegerFixtureGenerator.Generate(RecordLabel, Seed, RecordShape);

        [Test]
        public void TheRecordedTraceHasTenThousandSteps()
        {
            ReplayTrace trace = Record();
            Assert.That(trace.Steps.Count, Is.EqualTo(10000));
            Assert.That(trace.Steps[9999].Step, Is.EqualTo(9999));
            Assert.That(trace.Steps[9999].SealedBatch, Is.True);
            Assert.That(trace.CompositionStepCount, Is.GreaterThan(5), "the script must exercise composition changes");
            Assert.That(trace.Describe(), Does.Contain("steps=10000"));
        }

        [Test]
        public void TheTenThousandStepFixtureHashesIdenticallyAcrossWorkerCounts()
        {
            ReplayTrace trace = Record();
            ReplayRun one = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);

            IReadOnlyList<int> counts = WorkerSchedule.SupportedWorkerCounts(trace.Shape.MaxWorkers);
            for (int i = 0; i < counts.Count; i++)
            {
                int workers = counts[i];
                ReplayRun run = new ReplayRunner(new ReplayOptions(
                    workers, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);
                ReplayComparison comparison = ReplayComparison.Compare(one, run);
                Assert.That(comparison.SameInput, Is.True, comparison.Describe());
                Assert.That(comparison.Equal, Is.True,
                    "workers=" + workers + " diverged: " + comparison.Describe());
                Assert.That(run.Workers, Is.EqualTo(workers));
            }
        }

        [Test]
        public void ShuffledProducerAndCompletionOrderDoesNotChangeTheHashes()
        {
            ReplayTrace trace = Record();
            ReplayRun ordered = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);
            ReplayRun shuffled = new ReplayRunner(new ReplayOptions(
                4, 977U, true, TelemetryRetention.Off, 1000000, 0)).Run(trace);

            ReplayComparison comparison = ReplayComparison.Compare(ordered, shuffled);
            Assert.That(comparison.SameInput, Is.True, comparison.Describe());
            Assert.That(comparison.Equal, Is.True,
                "a shuffled producer/completion order must not change a policy result: " + comparison.Describe());
            Assert.That(shuffled.ProducerShuffles, Is.GreaterThan(0), "the run must actually have shuffled");
            Assert.That(shuffled.SourceIdentity, Is.Not.EqualTo(ordered.SourceIdentity));
        }

        [Test]
        public void ADifferentAdmittedOrderIsADifferentInputTraceAndIsReportedAsSuch()
        {
            ReplayTrace trace = Record();
            // Rebuild the record with one admission sequence changed: TEST-022 calls this a different input trace.
            var mutated = new ReplayStepRecord[trace.Steps.Count];
            for (int i = 0; i < mutated.Length; i++)
            {
                ReplayStepRecord record = trace.Steps[i];
                mutated[i] = i == 5
                    ? new ReplayStepRecord(
                        record.Step,
                        new AdmissionSequence(record.Admission.Value + 1UL),
                        record.SealedBatch,
                        record.AdmittedCommands + 1,
                        record.Operation,
                        record.OperationArgument,
                        record.ProducerSeed,
                        record.CompletionSeed,
                        record.Workers)
                    : record;
            }

            ReplayTrace other = new ReplayTrace(trace.Label, trace.FixtureSeed, trace.Shape, mutated, null);
            ReplayRun first = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);
            ReplayRun second = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(other);

            ReplayComparison comparison = ReplayComparison.Compare(first, second);
            Assert.That(comparison.SameInput, Is.False);
            Assert.That(comparison.Equal, Is.False);
            Assert.That(comparison.Detail, Does.Contain("different recorded inputs"));
        }

        [Test]
        public void EveryStepCommitsExactlyOneLogicalStepAndTheStepChainIsStable()
        {
            ReplayTrace trace = IntegerFixtureGenerator.Generate("chain", 11U,
                new ReplayFixtureShape(2, 2, 1, 2, 64, 4, 1U), providerStride: 8);
            ReplayOptions options = new ReplayOptions(2, 0U, false, TelemetryRetention.Off, 1000000, 0);
            ReplayRun run = new ReplayRunner(options).Run(trace);

            Assert.That(run.Steps.Count, Is.EqualTo(trace.Steps.Count));
            for (int i = 0; i < run.Steps.Count; i++)
            {
                Assert.That(run.Steps[i].LogicalStep.Value, Is.EqualTo((ulong)i + 1UL),
                    "P-006: one step id per committed step, no gaps");
            }

            ReplayRun again = new ReplayRunner(options).Run(trace);
            Assert.That(again.Hashes.StateChain.Equals(run.Hashes.StateChain), Is.True);
            Assert.That(again.Hashes.EventIdentities.Equals(run.Hashes.EventIdentities), Is.True);
        }

        [Test]
        public void UnchangedCompositionStepsCarryTheResultAndDoNoControlWork()
        {
            ReplayTrace trace = IntegerFixtureGenerator.Generate("carry", 3U,
                new ReplayFixtureShape(2, 2, 1, 2, 40, 4, 1U), providerStride: 8);
            ReplayRun run = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);

            Assert.That(run.CarriedStepCount, Is.GreaterThan(0), "the script must contain unchanged steps");

            // Every step that changed nothing minted no decision at all: the engine carried the previous result,
            // and TEST-023's acceptance is exactly that a carried step does no control-plane work.
            for (int i = 0; i < run.Steps.Count; i++)
            {
                ReplayStepResult step = run.Steps[i];
                if (step.DerivedWork)
                {
                    continue;
                }

                Assert.That(step.Derivation, Is.Not.Null);
                Assert.That(step.Derivation!.Counters.ExaminedCandidates, Is.EqualTo(0),
                    "a carried step examines no candidate (P-023)");
                Assert.That(step.Derivation.Counters.StrataEvaluated, Is.EqualTo(0));
                Assert.That(step.Derivation.Counters.ControlNodesVisited, Is.EqualTo(0));
            }
        }

        [Test]
        public void TheIntegerRuleStageIsIntegralAndSaturating()
        {
            // The fixture target derives two integer slots (the stratum-0 counter and the stratum-1 weight), and
            // for payload 2 both values are 2, so one step adds 4 to a target's state.
            TargetAssembly assembly = AssemblyOf(2);
            Assert.That(IntegerRuleStage.AppliedCount(assembly), Is.EqualTo(2));
            Assert.That(IntegerRuleStage.Apply(assembly, 0, 1000000), Is.EqualTo(4));
            Assert.That(IntegerRuleStage.Apply(assembly, 5, 1000000), Is.EqualTo(9));
            Assert.That(IntegerRuleStage.Apply(assembly, 999999, 1000000), Is.EqualTo(1000000),
                "the fixture saturates rather than wrapping");
        }

        private static TargetAssembly AssemblyOf(int value)
        {
            DerivationSnapshot snapshot = IntegerFixtureGenerator
                .Builder(new ReplayFixtureShape(1, 1, 0, 1, 1, 1, 1U))
                .Install(
                    IntegerFixtureGenerator.ProviderName(0, 0),
                    IntegerFixtureGenerator.Branch(0),
                    0,
                    IntegerFixtureGenerator.ProviderRules(IntegerFixtureGenerator.ProviderName(0, 0), value),
                    InstallationState.Active)
                .Build(PropagationMode.Automatic, CompositionRevision.First, AssemblyEpoch.First)
                .ToSnapshot();
            DerivationResult result = DerivationEngine.Derive(
                snapshot,
                IntegerFixtureGenerator.ValueSource(),
                new DerivationOptions(null, null, null, null, true),
                null);
            Assert.That(result.Accepted, Is.True, "the fixture composition must derive");
            Assert.That(result.Assemblies.Count, Is.GreaterThan(0), "the fixture must have a target");
            return result.Assemblies[0];
        }
    }

    [TestFixture]
    public sealed class WorldOrdinalTests
    {
        [Test]
        public void TheOrdinalMapNormalizesComparisonTextOnly()
        {
            var firstSession = new WorldId(new Id128(0xAAAAUL, 0xBBBBUL));
            var secondSession = new WorldId(new Id128(0xCCCCUL, 0xDDDDUL));
            var map = new WorldOrdinalMap();
            Assert.That(map.Register(firstSession, 0), Is.EqualTo(0));
            Assert.That(map.Register(firstSession, 7), Is.EqualTo(0), "the first registration wins");
            Assert.That(map.Register(secondSession, 1), Is.EqualTo(1));
            Assert.That(map.Count, Is.EqualTo(2));

            string text = "world=" + firstSession.Session.ToString() + ";other=" + secondSession.Session.ToString();
            string normalized = map.Normalize(text);
            Assert.That(normalized, Does.Contain("world#0"));
            Assert.That(normalized, Does.Contain("world#1"));
            Assert.That(normalized, Does.Not.Contain(firstSession.Session.ToString()));

            // Two runs with different session identities normalize to the same text, which is what lets a
            // comparison of two sessions be about the fixture rather than about the session.
            var otherMap = new WorldOrdinalMap();
            otherMap.Register(new WorldId(new Id128(0x1111UL, 0x2222UL)), 0);
            string otherText = otherMap.Normalize(
                "world=" + new WorldId(new Id128(0x1111UL, 0x2222UL)).Session.ToString());
            Assert.That(otherText, Is.EqualTo("world=world#0"));

            Assert.That(map.OrdinalOf(new WorldId(new Id128(0x99UL, 0x99UL))), Is.EqualTo(-1));
        }

        [Test]
        public void TheRuntimeIdentityIsStillTheRealOne()
        {
            // The normalization never rewrites a runtime identity: two sessions still differ, which is what keeps
            // deduplication and stale-handle validation intact (TEST-022's explicit warning).
            var first = new WorldId(new Id128(1UL, 1UL));
            var second = new WorldId(new Id128(1UL, 1UL));
            Assert.That(first.Session.Equals(second.Session), Is.True, "the same session is the same session");
            Assert.That(first.Session.Equals(new Id128(1UL, 2UL)), Is.False);
            Assert.That(new WorldOrdinalMap().Count, Is.EqualTo(0), "no comparison structure exists until one is made");
        }

        [Test]
        public void TheExclusionListIsDeclaredData()
        {
            Assert.That(ExcludedFields.All.Count, Is.EqualTo(6));
            Assert.That(ExcludedFields.All, Does.Contain(ExcludedFields.DiagnosticCounters));
            Assert.That(ExcludedFields.All, Does.Contain(ExcludedFields.NativeLayout));
            Assert.That(ExcludedFields.All, Does.Contain(ExcludedFields.WorkerScheduling));
        }

        [Test]
        public void ADiagnosticCounterCannotChangeACorrectnessHash()
        {
            ReplayTrace trace = IntegerFixtureGenerator.Generate("cost", 5U,
                new ReplayFixtureShape(2, 2, 1, 2, 32, 2, 1U), providerStride: 8);
            ReplayRun quiet = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Off, 1000000, 0)).Run(trace);
            ReplayRun loud = new ReplayRunner(new ReplayOptions(
                1, 0U, false, TelemetryRetention.Default, 1000000, 0)).Run(trace);

            // One run retained a cost trace and the other retained none. Every *correctness* hash must be
            // identical, which is what "excluding diagnostic counters" means concretely; the counter chain is a
            // cost artifact of the retention policy and is deliberately allowed to differ.
            Assert.That(loud.Telemetry, Is.Not.Null);
            Assert.That(loud.Telemetry!.Frames.Count, Is.GreaterThan(0));
            Assert.That(quiet.Telemetry!.Frames.Count, Is.EqualTo(0));
            Assert.That(loud.Hashes.StateChain.Equals(quiet.Hashes.StateChain), Is.True);
            Assert.That(loud.Hashes.FinalState.Equals(quiet.Hashes.FinalState), Is.True);
            Assert.That(loud.Hashes.FinalDecisions.Equals(quiet.Hashes.FinalDecisions), Is.True);
            Assert.That(loud.Hashes.FinalProvenance.Equals(quiet.Hashes.FinalProvenance), Is.True);
            Assert.That(loud.Hashes.EventIdentities.Equals(quiet.Hashes.EventIdentities), Is.True);
        }
    }
}
