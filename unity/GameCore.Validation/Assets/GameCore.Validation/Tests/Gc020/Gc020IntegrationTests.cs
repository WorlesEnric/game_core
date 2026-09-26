// GameCore.Gc020.Tests — the EditMode half of the GC-020 gate (real-time action reference).
//
// One `[Test]` per named observation of `Gc020Scenario`, plus a table test that recomputes the digest from the
// observation-name table, so a renamed, reordered, added or dropped observation fails this suite instead of silently
// shrinking the gate. The runner is shared with the `-probeTraversal` player mode, so the same observations execute in
// the Editor and in a stripped headless IL2CPP player (TEST-018).
//
// Every test here is `NotRun (pending orchestrator build host)`: this authoring host has no Unity and no compiler.
#nullable enable
using System.Collections.Generic;
using System.Globalization;
using GameCore.Rules.Narrative;
using NUnit.Framework;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;

namespace GameCore.Gc020.Tests
{
    /// <summary>The GC-020 traversal gate's observations, one test each.</summary>
    [TestFixture]
    public sealed class Gc020IntegrationTests
    {
        /// <summary>Whole-sequence timeout of the one-time set-up, in milliseconds (a real world is built and driven).</summary>
        private const int RunTimeout = 1800000;

        /// <summary>Per-assertion timeout, in milliseconds. The Editor has an intermittent pre-dispatch hang.</summary>
        private const int AssertTimeout = 60000;

        /// <summary>
        /// Digest of the course's observation table: the canonical `traversal/&lt;observation&gt;` `name=pass` lines
        /// over <see cref="Gc020Scenario.ObservationNames"/>, LF separated, no trailing newline, SHA-256 hex — the same
        /// digest function the narrative trace uses. It is computed from the observation-name table, not read from a
        /// run, so a renamed, reordered, added or dropped observation changes this literal. NotRun (pending
        /// orchestrator build host).
        /// </summary>
        private const string CourseDigest = "PLACEHOLDER";

        private IReadOnlyList<Gc020Step> combined = new List<Gc020Step>();
        private Gc020ScenarioResult generated = null!;
        private Gc020ScenarioResult fixture = null!;

        /// <summary>
        /// The whole traversal sequence, once. A real fixed-step world is created, driven, torn down and stopped; every
        /// later test is an assertion over this run, so the suite never re-runs a world.
        /// </summary>
        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunTheCourseOverItsCatalog()
        {
            combined = Gc020TraversalHost.RunBoth(out generated, out fixture);
        }

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own world and its own adapters down; this guarantees clean process-wide registries
            // if a step failed mid-way (04 s9).
            AdapterFrameRegistry.Reset();
            UnityWorldRegistry.ResetAll();
        }

        // ------------------------------------------------------------------ TC-01: the fixed-step world

        /// <summary>The fixed-step world exists with its declared configuration and pumps zero steps when idle.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheFixedStepWorldAndRunnersObservationPasses()
        {
            AssertObservation("gc020-fixed-step-world-and-runners");
        }

        // ------------------------------------------------------------------ TC-02: existing descendants

        /// <summary>Every eligible existing runner derives the modifier; ineligible and isolated targets do not.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheExistingRunnersDeriveTheModifierObservationPasses()
        {
            AssertObservation("gc020-existing-runners-derive-the-modifier");
        }

        // ------------------------------------------------------------------ TC-03: one admitted step

        /// <summary>One admitted step integrates exactly once: `1.00 -> 1.04` m/s and a 20 mm advance.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheOneAdmittedStepIntegatesOnceObservationPasses()
        {
            AssertObservation("gc020-one-admitted-step-integates-once");
        }

        // ------------------------------------------------------------------ TC-04: replay

        /// <summary>Rule repeatability is separated from native physics: exact replay, tolerant observation compare.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheReplaySeparatesPureMotionFromEngineObservationObservationPasses()
        {
            AssertObservation("gc020-replay-separates-pure-motion-from-engine-observation");
        }

        // ------------------------------------------------------------------ TC-05: presentation rate

        /// <summary>30/60/144 Hz presentation does not double-advance authority (REF-A06).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void ThePresentationRateDoesNotDoubleAdvanceObservationPasses()
        {
            AssertObservation("gc020-presentation-rate-does-not-double-advance");
        }

        // ------------------------------------------------------------------ TC-06: reparent

        /// <summary>A subtree move changes the contribution and keeps pose, velocity and progress (REF-A01).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheReparentChangesTheContributionAndKeepsStateObservationPasses()
        {
            AssertObservation("gc020-reparent-changes-the-contribution-and-keeps-state");
        }

        // ------------------------------------------------------------------ TC-07: unmount

        /// <summary>Retraction removes the contribution and never reverses committed progress (REF-A02).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheUnmountKeepsPoseProgressAndReceiptsObservationPasses()
        {
            AssertObservation("gc020-unmount-keeps-pose-progress-and-receipts");
        }

        // ------------------------------------------------------------------ TC-08: mode switch

        /// <summary>Both mode directions recompute the inherited contribution; isolation holds in both (P-013, P-016).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheModeSwitchBothDirectionsObservationPasses()
        {
            AssertObservation("gc020-mode-switch-both-directions");
        }

        // ------------------------------------------------------------------ TC-09: future descendant

        /// <summary>A runner created after the mount derives the modifier before its first executable step.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheFutureDescendantDerivesBeforeExecutionObservationPasses()
        {
            AssertObservation("gc020-future-descendant-derives-before-execution");
        }

        // ------------------------------------------------------------------ TC-10: one simulation per step

        /// <summary>One local `PhysicsScene.Simulate` per admitted step, intents applied before it (04 s7, REF-A06).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheOneSimulationPerAdmittedStepObservationPasses()
        {
            AssertObservation("gc020-one-simulation-per-admitted-step");
        }

        // ------------------------------------------------------------------ TC-11: one authority per pose

        /// <summary>An externally owned pose is not integrated, and two owners are refused (REF-A05).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheExternallyOwnedPoseIsNotIntegratedObservationPasses()
        {
            AssertObservation("gc020-externally-owned-pose-is-not-integrated");
        }

        // ------------------------------------------------------------------ TC-12: committed output

        /// <summary>Committed animation presents once per newer token; committed audio plays each crossing once.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCommittedAnimationAndAudioOutputObservationPasses()
        {
            AssertObservation("gc020-committed-animation-and-audio-output");
        }

        // ------------------------------------------------------------------ TC-13: no action phase elsewhere

        /// <summary>Cards and narrative declare no action, physics, animation or audio phase (P-001, P-059).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsAndNarrativeDeclareNoActionPhaseObservationPasses()
        {
            AssertObservation("gc020-cards-and-narrative-declare-no-action-phase");
        }

        // ------------------------------------------------------------------ TC-14: teardown

        /// <summary>The course settles, disposes and returns the registries to their baseline (P-047, P-048).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheTeardownSettlesAndDisposesObservationPasses()
        {
            AssertObservation("gc020-teardown-settles-and-disposes");
        }

        // ------------------------------------------------------------------ the frozen table

        /// <summary>
        /// The observation table is exactly the published sequence, in order, and its digest is the frozen literal —
        /// recomputed here from the table so a renamed, reordered, added or dropped observation changes the literal
        /// this test expects instead of shrinking the gate silently. NotRun (pending orchestrator build host).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheObservationTableIsExactlyThePublishedSequence()
        {
            Assert.That(Gc020Scenario.ObservationNames.Length, Is.EqualTo(14),
                "the gate records exactly fourteen named observations");
            Assert.That(
                Gc020Scenario.QualifiedNames(Gc020TraversalHost.Label).Length,
                Is.EqualTo(14));
            Assert.That(
                Gc020Scenario.QualifiedNames(Gc020TraversalHost.Label)[0],
                Is.EqualTo(Gc020TraversalHost.Label + "/" + Gc020Scenario.ObservationNames[0]));

            var table = new Gc020ScenarioResult(Gc020TraversalHost.Label, PassingSteps(Gc020TraversalHost.Label));
            Assert.That(table.Digest, Is.EqualTo(CourseDigest),
                "the course digest literal must be the one this observation table produces");

            Assert.That(generated.Label, Is.EqualTo(Gc020TraversalHost.Label));
            Assert.That(generated.Steps.Count, Is.EqualTo(14));
            Assert.That(generated.AllPassed, Is.True, generated.Describe());
            Assert.That(generated.Digest, Is.EqualTo(CourseDigest),
                "the recorded run must hash to the frozen table's literal");

            // The fixture-catalog run is the same course over the hand-written generated-style catalog, so it records
            // the same names and the same digest; the gate records that no generated traversal catalog exists yet.
            Assert.That(fixture.Steps.Count, Is.EqualTo(combined.Count));
        }

        // ------------------------------------------------------------------ helpers

        private static IReadOnlyList<Gc020Step> PassingSteps(string label)
        {
            var steps = new List<Gc020Step>(Gc020Scenario.ObservationNames.Length);
            for (int i = 0; i < Gc020Scenario.ObservationNames.Length; i++)
            {
                steps.Add(new Gc020Step(label + "/" + Gc020Scenario.ObservationNames[i], true, "table"));
            }

            return steps;
        }

        private void AssertObservation(string bareName)
        {
            string name = Gc020TraversalHost.Label + "/" + bareName;
            var found = new List<Gc020Step>();
            for (int i = 0; i < combined.Count; i++)
            {
                if (combined[i].Name == name)
                {
                    found.Add(combined[i]);
                }
            }

            Assert.That(
                found.Count,
                Is.EqualTo(1),
                "observation " + name + " must be recorded exactly once; recorded names: "
                + DescribeNames(combined));
            Assert.That(
                found[0].Passed,
                Is.True,
                found[0].ToString() + "; digest=" + generated.Digest + "; run=" + generated.Describe());
        }

        private static string DescribeNames(IReadOnlyList<Gc020Step> steps)
        {
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(steps[i].Name + "=" + (steps[i].Passed ? "pass" : "fail"));
            }

            return names.Count.ToString(CultureInfo.InvariantCulture) + "[" + string.Join(" | ", names.ToArray()) + "]";
        }
    }
}
