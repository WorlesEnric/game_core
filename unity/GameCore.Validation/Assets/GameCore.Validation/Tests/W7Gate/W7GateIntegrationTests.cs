// GameCore.W7Gate.Tests — the EditMode half of the Wave 7 integration gate.
//
// One `[Test]` per observation of the frozen table (`W7GateScenario.ObservationNames()`, thirteen names), plus the
// digest, table and registry tests that recompute their claims from that table rather than reading them off the run.
// The whole gate runs ONCE, in `[OneTimeSetUp]`; every test is a pure assertion over the recorded steps, so the suite
// is a statement about ONE revision's behaviour rather than about how many times the gate was executed.
//
// The observation names are never written in this file: each test reads its name out of the frozen table at a fixed
// position, so the suite cannot pin a name the scenario stopped recording. The digest test is the falsifiability
// mechanism the earlier gates use — the literal is recomputed from the table alone and compared with the value
// `ProbeW7Gate` quotes — so a renamed, reordered, added or dropped observation (or a run that recorded a failing
// step) cannot report the digest the observation table implies (P-008, TEST-022).
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.W7Gate.Tests
{
    [TestFixture]
    public sealed class W7GateIntegrationTests
    {
        /// <summary>The gate's own run: 10,000-target derivations and three real recovery sequences.</summary>
        private const int RunTimeout = 1800000;

        private const int AssertTimeout = 60000;

        /// <summary>The one recorded run every test in this fixture asserts over.</summary>
        private W7GateScenarioResult result = null!;

        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunOnce()
        {
            result = W7GateScenario.Run();
        }

        [TearDown]
        [Timeout(AssertTimeout)]
        public void TearDown()
        {
            // The gate tears every world down itself, so this asserts the run's teardown instead of performing it: a
            // world still registered after the run is a failure of the gate, not something this suite quietly cleans
            // up (P-048).
            int registered = UnityWorldRegistry.Count;
            var remaining = new List<string>();
            foreach (UnityWorldHost host in UnityWorldRegistry.Hosts)
            {
                remaining.Add(host.World + ":" + host.Lifecycle);
            }

            Assert.That(registered, Is.EqualTo(0),
                "the Wave 7 gate left " + registered.ToString(CultureInfo.InvariantCulture)
                + " world(s) registered: " + string.Join(",", remaining.ToArray())
                + "; the gate's own teardown is part of what its run claims (P-048)");
        }

        // ------------------------------------------------------------------ the frozen table's process group

        // The frozen table's emission order is the four process observations, then the three recovery observations of
        // each family in `W7GateScenario.Families()` order (narrative, cards, traversal). The numbers below are
        // positions in that one table.

        /// <summary>GC-025's catalog coverage re-runs over the merged kernel (TEST-001, TEST-020).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCatalogCoverageRepassesOnTheMergedKernel() => AssertObservation(0);

        /// <summary>GC-012/TEST-008's incremental-versus-clean equivalence holds at the declared scale.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheIncrementalDerivationMatchesACleanDerivation() => AssertObservation(1);

        /// <summary>Every probe mode this repository has added still parses out of the merged `ProbeArguments`.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheProbeModeDispatchKeepsEveryMode() => AssertObservation(2);

        /// <summary>08's ten provisional budgets are still the ten rows this build records (TEST-023).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheDeclaredBudgetRowsAreTheRecordedTen() => AssertObservation(3);

        // ------------------------------------------------------------------ the recovery groups

        /// <summary>The narrative genre's GC-027 recovery sequence re-runs and passes on the merged kernel.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeRecoverySequenceRepasses() => AssertObservation(4);

        /// <summary>The narrative postwrite-apply fault point still exposes no destination (P-031, P-049).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeRecoveryPostwriteApplyFault() => AssertObservation(5);

        /// <summary>The narrative restart rebuilds from the store alone, with no in-process state (P-049).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeRecoveryRestartWithoutInProcessState() => AssertObservation(6);

        /// <summary>The card market's GC-027 recovery sequence re-runs and passes on the merged kernel.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsRecoverySequenceRepasses() => AssertObservation(7);

        /// <summary>The card postwrite-apply fault point still exposes no destination (P-031, P-049).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsRecoveryPostwriteApplyFault() => AssertObservation(8);

        /// <summary>The card restart rebuilds from the store alone, with no in-process state (P-049).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsRecoveryRestartWithoutInProcessState() => AssertObservation(9);

        /// <summary>The traversal course's GC-027 recovery sequence re-runs and passes on the merged kernel.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalRecoverySequenceRepasses() => AssertObservation(10);

        /// <summary>The traversal postwrite-apply fault point still exposes no destination (P-031, P-049).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalRecoveryPostwriteApplyFault() => AssertObservation(11);

        /// <summary>The traversal restart rebuilds from the store alone, with no in-process state (P-049).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheTraversalRecoveryRestartWithoutInProcessState() => AssertObservation(12);

        // ------------------------------------------------------------------ the table, the digest and the run

        /// <summary>
        /// The falsifiability check: the literal is recomputed from the frozen table alone and compared with the value
        /// the player probe quotes, so a renamed, reordered, added or dropped observation changes the table's digest
        /// rather than a value that follows the code (P-008, TEST-022).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheFrozenDigestIsTheOneTheObservationTableImplies()
        {
            string implied = W7GateScenario.ExpectedDigest();
            Assert.That(implied, Is.EqualTo(ProbeW7Gate.ExpectedDigest),
                "the quoted literal must be the digest the frozen observation table alone implies");

            W7GateStep digestStep = StepOf(W7GateScenario.DigestStepName);
            Assert.That(digestStep.Passed, Is.True, digestStep.Detail);
        }

        /// <summary>The recorded sequence is exactly the frozen table, in order, plus the digest step at its end.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheRecordedRunIsTheFrozenTablePlusItsDigestStep()
        {
            string[] table = W7GateScenario.ObservationNames();
            Assert.That(result.Steps.Count, Is.EqualTo(table.Length + 1), result.Describe());
            Assert.That(result.AllPassed, Is.True, result.Describe());
            for (int i = 0; i < table.Length; i++)
            {
                Assert.That(result.Steps[i].Name, Is.EqualTo(table[i]),
                    "step " + i.ToString(CultureInfo.InvariantCulture)
                    + " is the frozen table's, in its emission order");
            }

            Assert.That(result.Steps[result.Steps.Count - 1].Name, Is.EqualTo(W7GateScenario.DigestStepName),
                "the last recorded step is the digest step over the table it is deliberately not part of");
        }

        /// <summary>The player probe and this suite are one claim: both read the one frozen table.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheProbeAndTheSuiteReadTheOneFrozenTable()
        {
            Assert.That(ProbeW7Gate.ExpectedObservations, Is.EqualTo(W7GateScenario.ObservationNames()),
                "the probe's expected observations are the frozen table this suite asserts, so neither half can "
                + "cover for a table that drifted away from what the other one checks");
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// Asserts one recorded observation by its position in the frozen table: the name is read from the table
        /// rather than written here, so the suite cannot pass on a name the scenario stopped recording, and a failure
        /// carries the step's own detail (the values the observation was computed from).
        /// </summary>
        private void AssertObservation(int index)
        {
            string name = W7GateScenario.ObservationNames()[index];
            W7GateStep step = StepOf(name);
            Assert.That(step.Passed, Is.True, step.Detail);
        }

        /// <summary>The recorded step with this name; a run that recorded no such step fails with the run's shape.</summary>
        private W7GateStep StepOf(string name)
        {
            for (int i = 0; i < result.Steps.Count; i++)
            {
                if (string.Equals(result.Steps[i].Name, name, StringComparison.Ordinal))
                {
                    return result.Steps[i];
                }
            }

            throw new InvalidOperationException("the run recorded no observation named '" + name
                + "'; " + result.Describe());
        }
    }
}
