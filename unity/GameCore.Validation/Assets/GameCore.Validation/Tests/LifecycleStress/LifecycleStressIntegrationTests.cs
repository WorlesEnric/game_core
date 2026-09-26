// GameCore.LifecycleStress.Tests - the GC-022 Unity-world lifecycle-stress EditMode half.
//
// The runner (`LifecycleStressScenario` over `Gc013NarrativeHost` and `Gc013CardsHost`) builds one real world per
// family per catalog and drives the twelve observations of Contract B over it: the counted mount/unmount cycles, one
// hundred delayed completions, a stalled job's retained buffers, a disposer that throws once and required-provider
// churn, ending at the registry and ledger baselines with a digest over the whole table.
//
// The expensive part runs once, in `[OneTimeSetUp]`, for both families and both catalogs - four real worlds, each one
// carrying the counted cycles over the committed generated catalog - and every later test is an assertion over those
// cached results, so no world is re-run to answer a question about it. Every assertion message names the family and
// carries the observed `Describe()` text, so a failure on the build host is diagnosable without re-running the suite.
//
// The counted-cycle fixture carries `[Timeout(3600000)]`. The Editor has an unresolved intermittent pre-dispatch hang
// (`artifacts/gc-014/BUILD_REPORT.md` section "Fixes" item 5) that no `[Timeout]` covers, so the external watchdog
// stays; this suite is NotRun (pending orchestrator build host).
#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.LifecycleStress.Tests
{
    [TestFixture]
    public sealed class LifecycleStressIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the sibling gate suites use.</summary>
        private const string FixturePrefix = LifecycleStressScenario.FixtureRunPrefix;

        /// <summary>
        /// Milliseconds the one-time in-process runs may take: four real worlds (two families, two catalogs), each one
        /// carrying the counted mount/unmount cycles over the committed generated catalog. NotRun (pending
        /// orchestrator build host).
        /// </summary>
        private const int RunTimeout = 3600000;

        /// <summary>Milliseconds one assertion-only test may take; they read the cached runs.</summary>
        private const int AssertTimeout = 60000;

        private IReadOnlyList<LifecycleStressStep> narrativeCombined = new List<LifecycleStressStep>();
        private IReadOnlyList<LifecycleStressStep> cardsCombined = new List<LifecycleStressStep>();
        private LifecycleStressResult narrativeGenerated = null!;
        private LifecycleStressResult narrativeFixture = null!;
        private LifecycleStressResult cardsGenerated = null!;
        private LifecycleStressResult cardsFixture = null!;

        /// <summary>
        /// The whole stress, once per family per catalog: four real worlds are built, cycled, stalled, failed and torn
        /// down. Every later test is an assertion over these runs, so the suite never re-runs a world.
        /// </summary>
        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunBothFamiliesOverBothCatalogs()
        {
            narrativeCombined = Gc013NarrativeHost.RunBothLifecycleStress(out narrativeGenerated, out narrativeFixture);
            cardsCombined = Gc013CardsHost.RunBothLifecycleStress(out cardsGenerated, out cardsFixture);
        }

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own world down; this guarantees the process-wide registry if a step failed midway.
            UnityWorldRegistry.ResetAll();
        }

        // ------------------------------------------------------------------ observation 1: the world baseline

        /// <summary>
        /// One real world of the family, running, with its own scope tree, its lane joined and nothing retained
        /// beyond the world's own storage (P-002, P-030, P-042).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheWorldBaselineObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-world-baseline");

        // ------------------------------------------------------------------ observation 2: the counted cycles

        /// <summary>
        /// The counted mount/unmount cycles on that one live world: every cycle mounts a fresh identity, publishes it
        /// `Active` with attributed rows and a retired lease, and unmounts it to `Disposed` (P-004, P-046, P-048).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(RunTimeout)]
        public void TheCountedCyclesObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-cycles-complete");

        // ------------------------------------------------------------------ observation 3: counters at baseline

        /// <summary>
        /// The lease, callback, job-fence and ledger counts after the cycles: no live lease, no retained reference, no
        /// quarantine, no live activation, no outstanding or failed job, and the world ledger back at its baseline
        /// (P-048).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheCountersReturnToBaselineObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-counters-return-to-baseline");

        // ------------------------------------------------------------------ observation 4: acquisition to retirement

        /// <summary>
        /// The last cycle's acquisitions traced in both ledgers: every world-side record retired, none left open, and
        /// every composition-side record of that installation non-retained (P-048).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheAcquisitionsTracedToRetirementObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-acquisitions-traced-to-retirement");

        // ------------------------------------------------------------------ observation 5: delayed completions

        /// <summary>
        /// One hundred completions stamped for a retired activation - fifty before the same identity is remounted and
        /// fifty after - all discarded, none moving a step, an epoch or a revision, and none releasing a lease twice
        /// (P-005, P-007, P-047).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheDelayedCompletionsAreDiscardedObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-delayed-completions-are-discarded");

        // ------------------------------------------------------------------ observation 6: a stalled job

        /// <summary>
        /// A tracked job that may still reach the installation's staged lease and world-side buffer: the teardown is
        /// `TeardownBlocked`, unsettled, fences the job's resources and leaves the lease quarantined and undisposed;
        /// completion plus an explicit release retires it exactly once and restores the baseline (P-047, P-048).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheStalledJobRetainsBuffersObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-stalled-job-retains-buffers");

        // ------------------------------------------------------------------ observation 7: a throwing disposer

        /// <summary>
        /// A disposer that fails exactly one release: the failed reference stays retained and never reported as
        /// disposed, every other lease of that publication is still retired, and the explicit later release retires
        /// the retained reference exactly once (P-048).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheThrowingDisposerKeepsCleanupGoingObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-throwing-disposer-keeps-cleanup-going");

        // ------------------------------------------------------------------ observation 8: provider churn

        /// <summary>
        /// A required-service provider removed and returned ten times: each removal leaves the consumer
        /// `WaitingForDependencies` in the same publication with no bindings and its rows retracted, and each return
        /// resumes it `Active` with the rows and bindings it had (P-011, P-012).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheRequiredProviderChurnObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-required-provider-churn");

        // ------------------------------------------------------------------ observation 9: loop nodes

        /// <summary>
        /// The player-loop node count and install count of the whole process, unchanged by a stress that installs
        /// nothing: loop nodes and subscriptions do not accumulate (GC-022 acceptance).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheLoopNodesDoNotAccumulateObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-loop-nodes-do-not-accumulate");

        // ------------------------------------------------------------------ observation 10: registry baseline

        /// <summary>
        /// The world's own teardown: no world left in the registry, no entity world, the lifecycle `Disposed`, and
        /// nothing retained or outstanding (P-048).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheRegistryReturnsToBaselineObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-registry-returns-to-baseline");

        // ------------------------------------------------------------------ observation 11: fresh incarnations

        /// <summary>
        /// Every world session identity and every cycle's installation incarnation, distinct and non-default, with the
        /// remounted identity reaching a strictly greater generation (P-005).
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheIncarnationsAreFreshObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-incarnations-are-fresh");

        // ------------------------------------------------------------------ observation 12: the frozen digest

        /// <summary>
        /// The digest over the whole observation table, equal to the family's frozen literal, over both catalogs.
        /// </summary>
        [TestCase(Gc013NarrativeHost.Label)]
        [TestCase(Gc013CardsHost.Label)]
        [Timeout(AssertTimeout)]
        public void TheRepeatableDigestObservationPasses(string family) =>
            AssertObservation(family, "lifecycle-stress-repeatable-digest");

        // ------------------------------------------------------------------ the whole table

        /// <summary>
        /// The result-level digest and the step count of both runs, over both catalogs: the observation table really
        /// is the frozen one, in the frozen order, and none of it failed (P-008).
        /// </summary>
        [TestCase(
            Gc013NarrativeHost.Label,
            Gc013NarrativeHost.LifecycleStressGeneratedDigest,
            Gc013NarrativeHost.LifecycleStressFixtureDigest)]
        [TestCase(
            Gc013CardsHost.Label,
            Gc013CardsHost.LifecycleStressGeneratedDigest,
            Gc013CardsHost.LifecycleStressFixtureDigest)]
        [Timeout(AssertTimeout)]
        public void TheDigestHoldsOverBothCatalogs(string family, string generatedDigest, string fixtureDigest)
        {
            LifecycleStressResult generated = GeneratedOf(family);
            LifecycleStressResult fixture = FixtureOf(family);
            IReadOnlyList<LifecycleStressStep> combined = CombinedOf(family);
            string context = "; " + generated.Describe() + "; " + fixture.Describe();

            Assert.That(generated.Steps.Count, Is.EqualTo(LifecycleStressScenario.ObservationNames.Length),
                family + ": the generated-catalog run must record every named observation exactly once" + context);
            Assert.That(fixture.Steps.Count, Is.EqualTo(LifecycleStressScenario.ObservationNames.Length),
                family + ": the fixture-catalog run must record every named observation exactly once" + context);
            Assert.That(generated.AllPassed, Is.True, family + ": generated-catalog observations" + context);
            Assert.That(fixture.AllPassed, Is.True, family + ": fixture-catalog observations" + context);

            // The digest is over the observation names and their pass flags, so asserting the literal asserts both:
            // the named sequence really ran, in order, and every observation of it passed.
            Assert.That(generated.Digest, Is.EqualTo(generatedDigest),
                family + ": the generated-catalog run's digest" + context);
            Assert.That(fixture.Digest, Is.EqualTo(fixtureDigest),
                family + ": the fixture-catalog run's digest" + context);

            string[] expected = LifecycleStressScenario.QualifiedNames(family);
            Assert.That(combined.Count, Is.EqualTo(expected.Length * 2),
                family + ": both catalogs must record every observation: " + combined.Count + " of "
                + (expected.Length * 2) + context);
            Assert.That(Names(combined, string.Empty), Is.EqualTo(expected),
                family + ": generated-catalog observation order" + context);
            Assert.That(Names(combined, FixturePrefix), Is.EqualTo(expected),
                family + ": fixture-catalog observation order" + context);
        }

        /// <summary>
        /// The frozen observation table: the scenario, both family hosts and both digest literals agree, so a renamed
        /// or dropped observation changes this suite rather than shrinking it. A digest computed here from the name
        /// table is compared with the literal the probe and the suite carry (P-008).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheObservationTablesAndDigestsAgree()
        {
            Assert.That(LifecycleStressScenario.ObservationNames.Length, Is.EqualTo(12),
                "the frozen table has twelve observations (Contract B)");
            Assert.That(LifecycleStressScenario.FixtureRunPrefix, Is.EqualTo("fixture:"),
                "the fixture-catalog prefix is the repo's own convention");
            Assert.That(LifecycleStressScenario.CycleCount, Is.GreaterThan(0),
                "the counted run resolves a positive cycle count");
            Assert.That(
                LifecycleStressScenario.Families(),
                Is.EqualTo(new List<string> { Gc013NarrativeHost.Label, Gc013CardsHost.Label }),
                "the accepted family labels, in the contract's order");
            Assert.That(
                DigestOf(Gc013NarrativeHost.Label),
                Is.EqualTo(Gc013NarrativeHost.LifecycleStressGeneratedDigest),
                "the narrative generated-catalog digest literal is the digest of the frozen name table");
            Assert.That(
                DigestOf(FixturePrefix + Gc013NarrativeHost.Label),
                Is.EqualTo(Gc013NarrativeHost.LifecycleStressFixtureDigest),
                "the narrative fixture-catalog digest literal is the digest of the frozen prefixed name table");
            Assert.That(
                DigestOf(Gc013CardsHost.Label),
                Is.EqualTo(Gc013CardsHost.LifecycleStressGeneratedDigest),
                "the card generated-catalog digest literal is the digest of the frozen name table");
            Assert.That(
                DigestOf(FixturePrefix + Gc013CardsHost.Label),
                Is.EqualTo(Gc013CardsHost.LifecycleStressFixtureDigest),
                "the card fixture-catalog digest literal is the digest of the frozen prefixed name table");
        }

        /// <summary>
        /// A label outside the frozen table is refused naming the accepted labels, rather than answered with a world
        /// nobody asked for (Contract A).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void AnUnknownFamilyLabelIsRefused()
        {
            Assert.That(
                () => LifecycleStressScenario.RunGeneratedCatalog("guilds"),
                Throws.ArgumentException.With.Message.Contains("narrative"),
                "the refusal names the accepted labels");
            Assert.That(
                () => LifecycleStressScenario.RunGeneratedCatalog("guilds"),
                Throws.ArgumentException.With.Message.Contains("cards"),
                "the refusal names the accepted labels");
            Assert.That(
                () => LifecycleStressScenario.RunFixtureCatalog("guilds"),
                Throws.ArgumentException.With.Message.Contains("guilds"),
                "the refusal names the rejected label");
        }

        // ------------------------------------------------------------------ helpers

        private void AssertObservation(string family, string bareName)
        {
            IReadOnlyList<LifecycleStressStep> combined = CombinedOf(family);
            LifecycleStressResult generated = GeneratedOf(family);
            LifecycleStressResult fixture = FixtureOf(family);
            string context = "; " + generated.Describe() + "; " + fixture.Describe();
            string qualified = family + "/" + bareName;
            string prefixed = FixturePrefix + qualified;
            int recorded = 0;
            var failures = new List<string>();
            for (int i = 0; i < combined.Count; i++)
            {
                LifecycleStressStep step = combined[i];
                if (step.Name != qualified && step.Name != prefixed)
                {
                    continue;
                }

                recorded++;
                if (!step.Passed)
                {
                    failures.Add(step.ToString());
                }
            }

            Assert.That(recorded, Is.EqualTo(2),
                family + ": the observation '" + bareName + "' must be recorded once per catalog, once unprefixed and"
                + " once under '" + FixturePrefix + "'" + context);
            Assert.That(failures, Is.Empty,
                family + ": failed observations: " + string.Join(" | ", failures.ToArray()) + context);
        }

        private IReadOnlyList<LifecycleStressStep> CombinedOf(string family) =>
            family == Gc013NarrativeHost.Label ? narrativeCombined : cardsCombined;

        private LifecycleStressResult GeneratedOf(string family) =>
            family == Gc013NarrativeHost.Label ? narrativeGenerated : cardsGenerated;

        private LifecycleStressResult FixtureOf(string family) =>
            family == Gc013NarrativeHost.Label ? narrativeFixture : cardsFixture;

        /// <summary>
        /// The digest the probe and this suite must carry for one qualified label, recomputed from the name table:
        /// `name=pass` lines in the frozen order, through the same digest function every run uses (P-008).
        /// </summary>
        private static string DigestOf(string label)
        {
            string[] names = LifecycleStressScenario.QualifiedNames(label);
            var lines = new List<string>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                lines.Add(names[i] + "=pass");
            }

            return GameCore.Rules.Narrative.NarrativeDigest.OfLines(lines);
        }

        private static List<string> Names(IReadOnlyList<LifecycleStressStep> combined, string prefix)
        {
            var names = new List<string>();
            for (int i = 0; i < combined.Count; i++)
            {
                string name = combined[i].Name;
                if (prefix.Length == 0)
                {
                    if (!name.StartsWith(FixturePrefix, StringComparison.Ordinal))
                    {
                        names.Add(name);
                    }
                }
                else if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    names.Add(name.Substring(prefix.Length));
                }
            }

            return names;
        }
    }
}
