// GameCore.W6Gate.Tests — the EditMode half of the Wave 6 integration gate (TEST-018, P-060).
//
// One `[Test]` per named observation of each of the three genres, plus the table and digest tests that recompute every
// literal from the frozen name table, plus the registry check that proves the gate tore every world down. The three
// runs happen once, in `[OneTimeSetUp]`; every test is a pure assertion over the recorded steps, so the suite is a
// statement about ONE revision's behaviour rather than about how many times the gate was executed.
//
// The digest test is the falsifiability mechanism the earlier gates use: the literals are recomputed here from
// `W6GateScenario.QualifiedNames(label)` alone, so a renamed, reordered, added or dropped observation — or a gate
// whose run recorded a failing step — cannot report the digest the observation table implies (P-008, TEST-022).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.W6Gate.Tests
{
    [TestFixture]
    public sealed class W6GateIntegrationTests
    {
        private const string FixturePrefix = W6GateScenario.FixtureRunPrefix;

        /// <summary>One full gate run per family and catalog, including the 1,000 counted cycles.</summary>
        private const int RunTimeout = 3600000;

        private const int AssertTimeout = 60000;

        private const string NarrativeGeneratedDigest =
            "4235cea3c22cf93e38307000fcc863edd4ada3e2fc4a3b475ca719765dc17279";

        private const string NarrativeFixtureDigest =
            "ab198371e8d276dcc2833e88e64e278abde57274ea3c68e2497fa2b75f97fa73";

        private const string CardsGeneratedDigest =
            "894a975e4a5eb98d733ec213778ec275e4abe02cb4028271857bd4fa68d6fe80";

        private const string CardsFixtureDigest =
            "9c3d3b5d2f8e959920aa9678e65f56658cbe4a513fcd2895cd458fbda4f9ea14";

        private const string TraversalDigest =
            "ca29b5f7099f9fbc4abeb7031c7b1e35ba39ec1ad39db70dd0f037fa8d45675e";

        private IReadOnlyList<W6GateStep> narrativeCombined = new List<W6GateStep>();

        private IReadOnlyList<W6GateStep> cardsCombined = new List<W6GateStep>();

        private IReadOnlyList<W6GateStep> traversalSteps = new List<W6GateStep>();

        private W6GateScenarioResult narrativeGenerated = null!;

        private W6GateScenarioResult narrativeFixture = null!;

        private W6GateScenarioResult cardsGenerated = null!;

        private W6GateScenarioResult cardsFixture = null!;

        private W6GateScenarioResult traversal = null!;

        private int registryBaseline;

        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunEveryFamilyOverEveryCatalogItOwns()
        {
            registryBaseline = UnityWorldRegistry.Count;

            narrativeCombined = Gc013NarrativeHost.RunBothW6Gate(out narrativeGenerated, out narrativeFixture);
            cardsCombined = Gc013CardsHost.RunBothW6Gate(out cardsGenerated, out cardsFixture);
            traversalSteps = Gc020TraversalHost.RunBothW6Gate(out traversal, out W6GateScenarioResult traversalFixture);
            Assert.That(traversalFixture, Is.SameAs(traversal),
                "this revision has no committed generated traversal catalog, so both out-parameters are the one run");
        }


        [TearDown]
        [Timeout(AssertTimeout)]
        public void TearDown()
        {
            // Every gate-owned world is gone; the PlayMode bootstrap world remains owned by the application.
            int registered = UnityWorldRegistry.Count;
            var remaining = new List<string>();
            foreach (UnityWorldHost host in UnityWorldRegistry.Hosts)
            {
                remaining.Add(host.World + ":" + host.Lifecycle);
            }
            Assert.That(registered, Is.EqualTo(registryBaseline),
                "the Wave 6 gate changed the registered worlds: " + string.Join(",", remaining)
                + "; its teardown is part of the 1,000-cycle claim (P-048)");
        }

        // ------------------------------------------------------------------ the narrative slice

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeDeclaresNoActionPhysicsOrAudioSurface() =>
            AssertObservation(Gc013NarrativeHost.Label, narrativeCombined,
                "w6-declares-no-action-physics-or-audio-surface");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeAdapterAssemblyIsAbsent() =>
            AssertObservation(Gc013NarrativeHost.Label, narrativeCombined, "w6-adapter-assembly-is-absent");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeThousandCycleTeardownIsBounded() =>
            AssertObservation(Gc013NarrativeHost.Label, narrativeCombined, "w6-thousand-cycle-teardown-is-bounded");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeLedgerAndFenceHighWaterMarksAreBounded() =>
            AssertObservation(Gc013NarrativeHost.Label, narrativeCombined,
                "w6-ledger-and-fence-high-water-marks-are-bounded");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeCountersReturnToBaseline() =>
            AssertObservation(Gc013NarrativeHost.Label, narrativeCombined, "w6-counters-return-to-baseline");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeRepeatableDigest() =>
            AssertObservation(Gc013NarrativeHost.Label, narrativeCombined, "w6-repeatable-digest");

        // ------------------------------------------------------------------ the card market

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsDeclareNoActionPhysicsOrAudioSurface() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined, "w6-declares-no-action-physics-or-audio-surface");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsAdapterAssemblyIsAbsent() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined, "w6-adapter-assembly-is-absent");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsRewardDeliveryIsExactlyOnceAcrossAReload() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined,
                "w6-reward-delivery-is-exactly-once-across-a-reload");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsThousandCycleTeardownIsBounded() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined, "w6-thousand-cycle-teardown-is-bounded");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsLedgerAndFenceHighWaterMarksAreBounded() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined,
                "w6-ledger-and-fence-high-water-marks-are-bounded");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsCountersReturnToBaseline() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined, "w6-counters-return-to-baseline");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsRepeatableDigest() =>
            AssertObservation(Gc013CardsHost.Label, cardsCombined, "w6-repeatable-digest");

        // ------------------------------------------------------------------ the traversal course

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalFixedStepCourseRuns() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps, "w6-fixed-step-course-runs");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalTelemetryCountersRecordAdmittedSteps() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps,
                "w6-telemetry-counters-record-admitted-steps");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalOnePhysicsSimulationPerAdmittedStep() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps,
                "w6-one-physics-simulation-per-admitted-step");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalReplayReproducesTheRuleDigest() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps, "w6-replay-reproduces-the-rule-digest");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalCarriesTheOptionalEngineSurface() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps, "w6-carries-the-optional-engine-surface");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalThousandCycleTeardownIsBounded() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps, "w6-thousand-cycle-teardown-is-bounded");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalLedgerAndFenceHighWaterMarksAreBounded() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps,
                "w6-ledger-and-fence-high-water-marks-are-bounded");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalCountersReturnToBaseline() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps, "w6-counters-return-to-baseline");

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalRepeatableDigest() =>
            AssertObservation(Gc020TraversalHost.Label, traversalSteps, "w6-repeatable-digest");

        // ------------------------------------------------------------------ the tables and the digests

        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeDigestHoldsOverBothCatalogs() =>
            AssertFamily(Gc013NarrativeHost.Label, narrativeCombined, narrativeGenerated, narrativeFixture,
                NarrativeGeneratedDigest, NarrativeFixtureDigest);

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsDigestHoldsOverBothCatalogs() =>
            AssertFamily(Gc013CardsHost.Label, cardsCombined, cardsGenerated, cardsFixture,
                CardsGeneratedDigest, CardsFixtureDigest);

        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalDigestHoldsOverItsOneCatalog()
        {
            Assert.That(traversal.Steps.Count, Is.EqualTo(W6GateScenario.TraversalObservationNames.Length));
            Assert.That(traversal.AllPassed, Is.True, traversal.Describe());
            Assert.That(traversal.Digest, Is.EqualTo(TraversalDigest), traversal.Describe());
            Assert.That(traversal.Digest, Is.EqualTo(DigestOf(Gc020TraversalHost.Label)));
            Assert.That(Gc020TraversalHost.GeneratedCatalogPresent, Is.False,
                "this revision has no committed generated traversal catalog; the gate must not imply one ran");
        }

        [Test]
        [Timeout(AssertTimeout)]
        public void TheObservationTablesAndDigestsAgree()
        {
            Assert.That(W6GateScenario.NarrativeObservationNames.Length, Is.EqualTo(6));
            Assert.That(W6GateScenario.CardsObservationNames.Length, Is.EqualTo(7));
            Assert.That(W6GateScenario.TraversalObservationNames.Length, Is.EqualTo(9));
            Assert.That(W6GateScenario.Families().Count, Is.EqualTo(3));

            Assert.That(NarrativeGeneratedDigest, Is.EqualTo(DigestOf(Gc013NarrativeHost.Label)));
            Assert.That(CardsGeneratedDigest, Is.EqualTo(DigestOf(Gc013CardsHost.Label)));
            Assert.That(TraversalDigest, Is.EqualTo(DigestOf(Gc020TraversalHost.Label)));

            Assert.That(NarrativeFixtureDigest, Is.EqualTo(FixtureDigestOf(Gc013NarrativeHost.Label)));
            Assert.That(CardsFixtureDigest, Is.EqualTo(FixtureDigestOf(Gc013CardsHost.Label)));

            Assert.That(Gc013NarrativeHost.W6GateGeneratedDigest, Is.EqualTo(NarrativeGeneratedDigest));
            Assert.That(Gc013NarrativeHost.W6GateFixtureDigest, Is.EqualTo(NarrativeFixtureDigest));
            Assert.That(Gc013CardsHost.W6GateGeneratedDigest, Is.EqualTo(CardsGeneratedDigest));
            Assert.That(Gc013CardsHost.W6GateFixtureDigest, Is.EqualTo(CardsFixtureDigest));
            Assert.That(Gc020TraversalHost.W6GateDigest, Is.EqualTo(TraversalDigest));

            Assert.That(ProbeW6Gate.NarrativeGeneratedDigest, Is.EqualTo(NarrativeGeneratedDigest));
            Assert.That(ProbeW6Gate.NarrativeFixtureDigest, Is.EqualTo(NarrativeFixtureDigest));
            Assert.That(ProbeW6Gate.CardsGeneratedDigest, Is.EqualTo(CardsGeneratedDigest));
            Assert.That(ProbeW6Gate.CardsFixtureDigest, Is.EqualTo(CardsFixtureDigest));
            Assert.That(ProbeW6Gate.TraversalDigest, Is.EqualTo(TraversalDigest));

            Assert.That(FixturePrefix, Is.EqualTo("fixture:"));
        }

        [Test]
        [Timeout(AssertTimeout)]
        public void TheCycleLoopFollowsTheDeclaredVariableAndNothingElseIsRead()
        {
            // The loop's quantity is one named input, so a reduced run reports the count it really used and cannot be
            // mistaken for the 1,000-cycle gate (GC-022's rule, applied to this gate's own loop).
            int resolved = W6GateScenario.CycleCount;
            Assert.That(resolved, Is.GreaterThan(0));
            Assert.That(W6GateScenario.CycleCountVariable, Is.EqualTo("GC_W6_GATE_CYCLES"));
            Assert.That(W6GateScenario.DefaultCycleCount, Is.EqualTo(1000));
            Assert.That(W6GateScenario.CommandDrivenPumpTicks, Is.EqualTo(1000000UL));
        }

        // ------------------------------------------------------------------ helpers

        private static void AssertObservation(string label, IReadOnlyList<W6GateStep> steps, string bareName)
        {
            // One recording per catalog this family owns: the narrative and card slices own a generated and a fixture
            // catalog, and the traversal course owns the one fixture catalog this revision has (GC-025 adds the
            // generated one). The expected count is derived from the label, never hard-coded per test.
            int expected = string.Equals(label, Gc020TraversalHost.Label, StringComparison.Ordinal) ? 1 : 2;

            string qualified = label + "/" + bareName;
            string fixtureQualified = FixturePrefix + qualified;
            var found = new List<W6GateStep>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].Name, qualified, StringComparison.Ordinal)
                    || (expected == 2 && string.Equals(steps[i].Name, fixtureQualified, StringComparison.Ordinal)))
                {
                    found.Add(steps[i]);
                }
            }

            Assert.That(found.Count, Is.EqualTo(expected),
                "this family owns " + expected.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " catalog(s), so the observation is recorded once per catalog; found "
                + found.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
            for (int i = 0; i < found.Count; i++)
            {
                Assert.That(found[i].Passed, Is.True, found[i].ToString());
            }
        }

        private static void AssertFamily(
            string label,
            IReadOnlyList<W6GateStep> combined,
            W6GateScenarioResult generated,
            W6GateScenarioResult fixture,
            string expectedGenerated,
            string expectedFixture)
        {
            int table = W6GateScenario.ObservationNames(label).Length;

            Assert.That(generated.Steps.Count, Is.EqualTo(table), generated.Describe());
            Assert.That(fixture.Steps.Count, Is.EqualTo(table), fixture.Describe());
            Assert.That(generated.AllPassed, Is.True, generated.Describe());
            Assert.That(fixture.AllPassed, Is.True, fixture.Describe());
            Assert.That(generated.Digest, Is.EqualTo(expectedGenerated), generated.Describe());
            Assert.That(fixture.Digest, Is.EqualTo(expectedFixture), fixture.Describe());
            Assert.That(combined.Count, Is.EqualTo(table * 2));

            Assert.That(DigestOf(label), Is.EqualTo(expectedGenerated));
            Assert.That(FixtureDigestOf(label), Is.EqualTo(expectedFixture));
        }

        /// <summary>The digest a passing generated-catalog run of one family must report, from the frozen name table.</summary>
        private static string DigestOf(string label) => Digest(label, string.Empty);

        /// <summary>The digest a passing fixture-catalog run of one family must report.</summary>
        private static string FixtureDigestOf(string label) => Digest(label, FixturePrefix);

        private static string Digest(string label, string prefix)
        {
            string[] names = W6GateScenario.QualifiedNames(label);
            var lines = new List<string>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                lines.Add(prefix + names[i] + "=pass");
            }

            return GameCore.Rules.Narrative.NarrativeDigest.OfLines(lines);
        }
    }
}
