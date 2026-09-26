// GameCore.Replay tests — engine observation replay versus native-physics comparison, and the differential
// propagation sweep with its deterministic reducer (GC-023, TEST-008/TEST-022).
//
// The two halves of TEST-022's last paragraph are tested as two separate claims, because conflating them is exactly
// the mistake the requirement warns about:
//
//   * Rule repeatability is asserted over a *recording*: the same recorded observations always produce the same
//     rule decisions and the same rule hash, with no physics engine involved.
//   * Native-physics comparison is asserted as a *comparison result*: a different physics recording diverges, the
//     divergence is reported with its first index and maximum component delta, and the rule half over a recording is
//     still identical — so a physics divergence never masquerades as a rule regression.
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Contracts;
using NUnit.Framework;

namespace GameCore.Replay.Tests
{
    [TestFixture]
    public sealed class ObservationReplayTests
    {
        private static ReplayTrace Trace(int steps) =>
            IntegerFixtureGenerator.Generate("observations", 99U,
                new ReplayFixtureShape(2, 2, 1, 2, steps, 2, 1U));

        [Test]
        public void ARecordingReplaysToTheSameRuleDecisionsEveryTime()
        {
            ReplayTrace trace = Trace(64);
            IReadOnlyList<RecordedObservation> recording = ObservationRecorder.Record(trace, 1234U);
            Assert.That(recording.Count, Is.EqualTo(trace.Steps.Count));

            ObservationReplayResult first = ObservationReplay.Replay(recording);
            ObservationReplayResult second = ObservationReplay.Replay(recording);
            Assert.That(ObservationReplay.RuleReplaysMatch(first, second), Is.True);
            Assert.That(second.RuleHash.Equals(first.RuleHash), Is.True);
            Assert.That(second.FinalState, Is.EqualTo(first.FinalState));
            Assert.That(first.Decisions.Count, Is.EqualTo(recording.Count));
        }

        [Test]
        public void TheRecorderIsDeterministicInItsSeed()
        {
            ReplayTrace trace = Trace(16);
            IReadOnlyList<RecordedObservation> first = ObservationRecorder.Record(trace, 7U);
            IReadOnlyList<RecordedObservation> second = ObservationRecorder.Record(trace, 7U);
            IReadOnlyList<RecordedObservation> other = ObservationRecorder.Record(trace, 8U);

            Assert.That(ObservationReplay.Replay(second).ObservationHash.Equals(
                ObservationReplay.Replay(first).ObservationHash), Is.True);
            Assert.That(ObservationReplay.Replay(other).ObservationHash.Equals(
                ObservationReplay.Replay(first).ObservationHash), Is.False,
                "a different physics seed is a different recording");
        }

        [Test]
        public void APhysicsDivergenceIsReportedAsAComparisonAndNotAsARuleRegression()
        {
            ReplayTrace trace = Trace(32);
            IReadOnlyList<RecordedObservation> left = ObservationRecorder.Record(trace, 5U);
            IReadOnlyList<RecordedObservation> right = ObservationRecorder.Record(trace, 6U);

            NativePhysicsComparison comparison = NativePhysicsComparison.Compare(left, right);
            Assert.That(comparison.ComparedCount, Is.EqualTo(32));
            Assert.That(comparison.Identical, Is.False);
            Assert.That(comparison.MaxComponentDelta, Is.GreaterThan(0));
            Assert.That(comparison.Detail, Does.Contain("firstDivergence"));
            Assert.That(comparison.Describe(), Does.Contain("native-physics"));

            // The rule half is a function of a recording, not of the physics that produced it: replaying each
            // recording is internally identical even though the two physics results disagree.
            Assert.That(ObservationReplay.RuleReplaysMatch(
                ObservationReplay.Replay(left), ObservationReplay.Replay(left)), Is.True);
            Assert.That(ObservationReplay.RuleReplaysMatch(
                ObservationReplay.Replay(right), ObservationReplay.Replay(right)), Is.True);
        }

        [Test]
        public void IdenticalRecordingsCompareIdenticallyAndLengthMismatchesAreReported()
        {
            ReplayTrace trace = Trace(8);
            IReadOnlyList<RecordedObservation> recording = ObservationRecorder.Record(trace, 42U);
            NativePhysicsComparison same = NativePhysicsComparison.Compare(recording, recording);
            Assert.That(same.Identical, Is.True);
            Assert.That(same.FirstDivergence, Is.EqualTo(-1));
            Assert.That(same.MaxComponentDelta, Is.EqualTo(0));

            var shorter = new List<RecordedObservation>();
            for (int i = 0; i < 4; i++)
            {
                shorter.Add(recording[i]);
            }

            NativePhysicsComparison mismatch = NativePhysicsComparison.Compare(recording, shorter);
            Assert.That(mismatch.Identical, Is.False);
            Assert.That(mismatch.Detail, Does.Contain("lengthsDiffer"));
        }

        [Test]
        public void AnEmptyRecordingReplaysToEmptyResults()
        {
            ObservationReplayResult result = ObservationReplay.Replay(Array.Empty<RecordedObservation>());
            Assert.That(result.ObservationCount, Is.EqualTo(0));
            Assert.That(result.FinalState, Is.EqualTo(0));
            Assert.That(result.RuleHash.IsEmpty, Is.False, "an empty recording still has a well-defined hash");
        }
    }

    [TestFixture]
    public sealed class DifferentialPropagationTests
    {
        private static ReplayFixtureShape Shape =>
            new ReplayFixtureShape(2, 2, 1, 2, 1, 2, 1U);

        [Test]
        public void TheSweepAgreesWithTheOracleOnEverySeedAndStep()
        {
            // A clean sweep: 24 seeds x 40 operations, each step compared through the effective-state projection.
            // A failure here would carry its seed and its reduced counterexample (see the next test), which is what
            // makes this sweep usable as evidence rather than as a long silence.
            var sweep = new DifferentialPropagation(Shape);
            DifferentialSweepResult result = sweep.Sweep(seeds: 24, steps: 40);
            Assert.That(result.Failures.Count, Is.EqualTo(0), DescribeFailures(result));
            Assert.That(result.CleanSeeds, Is.EqualTo(24));
            Assert.That(result.AcceptedComparisons, Is.EqualTo(24 * 40));
            Assert.That(result.Describe(), Does.Contain("failures=0"));
        }

        [Test]
        public void TheScriptsCoverTheOperationVocabularyAndAreReproducible()
        {
            var sweep = new DifferentialPropagation(Shape);
            IReadOnlyList<DifferentialOperation> first = sweep.ScriptFor(17U, 60);
            IReadOnlyList<DifferentialOperation> second = sweep.ScriptFor(17U, 60);
            Assert.That(first.Count, Is.EqualTo(60));
            for (int i = 0; i < first.Count; i++)
            {
                Assert.That(second[i].Describe(), Is.EqualTo(first[i].Describe()));
            }

            var seen = new HashSet<ReplayOperationKind>();
            for (int i = 0; i < first.Count; i++)
            {
                seen.Add(first[i].Kind);
            }

            Assert.That(seen.Count, Is.GreaterThanOrEqualTo(4), "the vocabulary must be exercised, not just declared");
        }

        [Test]
        public void AnInjectedDivergenceIsDetectedAndReducedToAReproducibleCounterexample()
        {
            // The perturbation makes the projection report a divergence on one operation kind, which is the seam a
            // test needs to prove the reducer produces a real witness. Without this, a passing sweep would also pass
            // against a comparator that never fails.
            // The perturbation fires on three of the seven operation kinds, so every 24-operation seed contains at
            // least one (the probability of missing all three in 24 draws is under one in a hundred thousand).
            var sweep = new DifferentialPropagation(
                Shape,
                (operation, result) =>
                    operation.Kind == ReplayOperationKind.MoveTarget
                    || operation.Kind == ReplayOperationKind.MountProvider
                    || operation.Kind == ReplayOperationKind.SwitchMode);

            DifferentialSweepResult result = sweep.Sweep(seeds: 3, steps: 24);
            Assert.That(result.Failures.Count, Is.EqualTo(3), DescribeFailures(result));

            FailingSeedRecord record = result.Failures[0];
            Assert.That(record.Reduced.Count, Is.GreaterThan(0), "a counterexample is never empty");
            Assert.That(record.Reduced.Count, Is.LessThanOrEqualTo(record.Original.Count));
            Assert.That(record.Failure, Does.Contain("seed"));
            Assert.That(record.Reduced, Has.Some.Matches<DifferentialOperation>(
                operation => operation.Kind == ReplayOperationKind.MoveTarget
                    || operation.Kind == ReplayOperationKind.MountProvider
                    || operation.Kind == ReplayOperationKind.SwitchMode),
                "the reduced script must still contain an operation the perturbation can diverge on");
            Assert.That(record.ReducedScript(), Does.Contain("seed="));

            // Reproducibility: reducing the same seed again yields the same witness, so a report is re-runnable.
            FailingSeedRecord again = sweep.Reduce(record.Seed, sweep.ScriptFor(record.Seed, 24));
            Assert.That(again.Reduced.Count, Is.EqualTo(record.Reduced.Count));
            for (int i = 0; i < again.Reduced.Count; i++)
            {
                Assert.That(again.Reduced[i].Describe(), Is.EqualTo(record.Reduced[i].Describe()));
            }

            // And the reduced witness still fails when it is run on its own.
            string? failure = sweep.Run(record.Reduced, record.Seed, out _, out _, out int failingStep);
            Assert.That(failure, Is.Not.Null, "the reduced counterexample must still fail");
            Assert.That(failingStep, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void ReducingAScriptThatDoesNotFailIsRefused()
        {
            var sweep = new DifferentialPropagation(Shape);
            IReadOnlyList<DifferentialOperation> script = sweep.ScriptFor(1U, 4);
            Assert.Throws<InvalidOperationException>(() => sweep.Reduce(1U, script));
        }

        [Test]
        public void SweepingASingleSeedReturnsNullWhenItAgrees()
        {
            var sweep = new DifferentialPropagation(Shape);
            Assert.That(sweep.RunSeed(23U, 30), Is.Null);
        }

        private static string DescribeFailures(DifferentialSweepResult result)
        {
            var text = new System.Text.StringBuilder(result.Describe());
            for (int i = 0; i < result.Failures.Count; i++)
            {
                text.Append('\n').Append(result.Failures[i].Describe());
                text.Append('\n').Append(result.Failures[i].ReducedScript());
            }

            return text.ToString();
        }
    }
}
