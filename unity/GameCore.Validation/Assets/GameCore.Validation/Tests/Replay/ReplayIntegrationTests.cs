// GameCore.Replay integration tests — the GC-023 scenario inside the Unity Editor (GC-023).
//
// The scenario itself lives in `GameCore.Validation.ProbeHost` so the `-probeReplay` player probe runs exactly these
// checks in the shipped runtime; this suite is the Editor half, and it is deliberately a thin table over the named
// observations rather than a second implementation:
//
//   * one `[Test]` per named observation, so a failure names the behaviour that broke;
//   * a table test that recomputes the observation digest from the scenario's own name list, so a renamed or dropped
//     observation fails the suite instead of silently shrinking the evidence (the same guard the Wave 4 and Wave 5
//     gates use);
//   * a case count test, so the world half really sampled every owner it claims to sample.
//
// Unity long tests can be interrupted by the Editor's intermittent pre-dispatch hang, so the fixture is generated
// once per fixture and the whole run is bounded by a timeout.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Replay;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Validation.Replay
{
    [TestFixture]
    [Timeout(900000)]
    public sealed class ReplayIntegrationTests
    {
        private static ReplayScenarioResult result = null!;

        [OneTimeSetUp]
        public void RunTheScenarioOnce()
        {
            result = ReplayScenario.RunAll();
            TestContext.Out.WriteLine(result.Describe());
            for (int i = 0; i < result.Steps.Count; i++)
            {
                TestContext.Out.WriteLine(result.Steps[i].ToString());
            }
        }

        [Test]
        public void TheScenarioReportsEveryNamedObservation()
        {
            Assert.That(result.Steps.Count, Is.EqualTo(ReplayScenario.AllNames.Count),
                "the scenario must report exactly the names it declares");
            for (int i = 0; i < ReplayScenario.AllNames.Count; i++)
            {
                Assert.That(result.Steps[i].Name, Is.EqualTo(ReplayScenario.AllNames[i]));
            }
        }

        [Test]
        public void EveryObservationPassed()
        {
            for (int i = 0; i < result.Steps.Count; i++)
            {
                Assert.That(result.Steps[i].Passed, Is.True, result.Steps[i].ToString());
            }
        }

        [Test]
        public void TheObservationTableIsTheOneTheDigestDescribes()
        {
            string recomputed = ReplayScenario.DigestOf(result.Steps);
            Assert.That(recomputed, Is.EqualTo(result.Digest));
            Assert.That(result.Digest.Length, Is.EqualTo(64), "the digest is a 32-byte content hash in hex");
            Assert.That(result.AllPassed, Is.True);
        }

        [Test]
        public void TheWorldHalfSamplesEveryOwnerOfALiveWorld()
        {
            // Section counts are the sampler's own evidence: a world whose message plane or event store was missing
            // would report fewer sections, so the digest would change even if every other observation passed.
            Assert.That(ReplayScenario.WorldNames.Count, Is.EqualTo(5));
            Assert.That(ReplayScenario.ReplayNames.Count, Is.EqualTo(7));
            Assert.That(ReplayScenario.JobsNames.Count, Is.EqualTo(3));
            Assert.That(ReplayScenario.AllNames.Count, Is.EqualTo(15));
            Assert.That(ReplayScenario.IdleFrames, Is.GreaterThan(0));
        }

        [Test]
        public void TheRealJobsObservationsCoverEverySupportedWorkerCount()
        {
            // The gap this file closes: the modeled worker counts vary a managed scheduler, so the real-Unity-jobs
            // observations must exist and must be driven at the three TEST-022 counts plus the target's own maximum.
            Assert.That(ReplayScenario.JobsNames.Count, Is.EqualTo(3));
            IReadOnlyList<int> counts = ReplayScenario.JobsWorkerCounts(out int maximum);
            Assert.That(counts, Does.Contain(1));
            Assert.That(counts, Does.Contain(2));
            Assert.That(counts, Does.Contain(4));
            Assert.That(maximum, Is.GreaterThanOrEqualTo(1), "the player must report a job-worker ceiling");
            Assert.That(ReplayScenario.PublishSeeds.Length, Is.GreaterThan(1),
                "more than one publish permutation is what makes the canonical-merge claim non-vacuous");
        }

        [Test]
        public void TheRecordedFixtureIsTheTenThousandStepOne()
        {
            ReplayTrace trace = IntegerFixtureGenerator.Generate(
                ReplayScenario.RecordLabel,
                ReplayScenario.FixtureSeed,
                IntegerFixtureGenerator.DefaultShape);
            Assert.That(trace.Steps.Count, Is.EqualTo(10000));
            Assert.That(trace.CatalogHash.IsEmpty, Is.False);
        }

        [Test]
        public void TheRawBenchmarkTraceOfTheWorldRunIsReadable()
        {
            Assert.That(result.RawTrace.Length, Is.GreaterThan(0), "the world half must produce a raw trace");
            Assert.That(
                BenchmarkTrace.TryRead(result.RawTrace, out IReadOnlyList<BenchmarkTrace.RawFrame> frames, out string failure),
                Is.True,
                failure);
            Assert.That(frames.Count, Is.GreaterThan(ReplayScenario.IdleFrames),
                "one frame per observation, so the trace is longer than the idle loop");
            Assert.That(result.JsonTrace, Does.Contain("\"chainHash\""));
        }

        [Test]
        public void TheDigestIsStableForTheSameRecordedInput()
        {
            // The digest is over the observation table, and the table is a function of the fixture, so two runs
            // report the same value; a digest that moved would mean the scenario is not reproducible.
            ReplayScenarioResult again = ReplayScenario.RunAll();
            Assert.That(again.Digest, Is.EqualTo(result.Digest));
            Assert.That(again.Steps.Count, Is.EqualTo(result.Steps.Count));
        }
    }
}
