#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Gc019.Tests
{
    /// <summary>
    /// GC-019 adapter gate, EditMode half (docs/game-core/09-implementation-guide.md, Wave 5 — GC-019; tests
    /// TEST-002, TEST-015, TEST-018, TEST-019, TEST-020).
    ///
    /// The runner (`Gc019Scenario` over `Gc013NarrativeHost` and `Gc013CardsHost`) runs only real modules: the GC-013
    /// world construction (GC-004's `CompositionHost` lane wired to GC-013's production mode-switch validator, GC-006's
    /// derivation through the incremental engine inside `DerivedAssemblyPipeline`, GC-007's ownership validation,
    /// GC-009's schedule compiler, GC-008's planner and publisher into a real `Unity.Entities.World`), GC-019's real
    /// adapters (`TypedInputIngress`, `PendingInputCompletionTable`, `AssetLeaseTable`, `ViewRegistry`,
    /// `CommittedOutputPresenter`, `WorldAdapterFrame`) over the world's own command port, callback gate and resource
    /// ledger, and the adapters package's deterministic fixtures. No seam fixture replaces a kernel module.
    ///
    /// Each observation runs the whole sequence over the committed generated catalog *and* over the family's
    /// hand-written generated-style catalog. Both runs record the same observation names with the same verdicts, so
    /// the digest over the observation table is the whole claim: every named observation passed, over both catalogs,
    /// in order. This suite compiles and asserts; it has not been executed on the authoring host.
    /// </summary>
    [TestFixture]
    public sealed class Gc019IntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family suites use.</summary>
        private const string FixturePrefix = Gc019Scenario.FixtureRunPrefix;

        /// <summary>
        /// Milliseconds the one-time in-process runs may take: four real worlds (two families, two catalogs), each
        /// with its own scene-free ECS world, publications and pumps. NotRun (pending orchestrator build host).
        /// </summary>
        private const int RunTimeout = 1800000;

        /// <summary>Milliseconds one assertion-only test may take; they read the cached runs.</summary>
        private const int AssertTimeout = 60000;

        /// <summary>
        /// Digest of the narrative family's observation table: the canonical `name=pass` lines of
        /// `narrative/&lt;observation&gt;` over <see cref="Gc019Scenario.ObservationNames"/>, LF separated, no trailing
        /// newline, SHA-256 hex — the same digest function the narrative trace uses. It is computed from the
        /// observation-name table, not read from a run, so a renamed, reordered, added or dropped observation changes
        /// this literal and the gate cannot silently shrink. NotRun (pending orchestrator build host).
        /// </summary>
        private const string NarrativeDigest = "c812ccdce22cee6098d3f8dba5c23cfb744c7644aa83a99ae24bb790fbdde837";

        /// <summary>The card family's digest literal, computed the same way over `cards/&lt;observation&gt;`.</summary>
        private const string CardsDigest = "deaff62c2a643221511524063dc0a23cb80b0fe071a3b00c8245fbd1fc6097ce";

        private IReadOnlyList<Gc019Step> narrativeCombined = new List<Gc019Step>();
        private IReadOnlyList<Gc019Step> cardsCombined = new List<Gc019Step>();
        private Gc019ScenarioResult narrativeGenerated = null!;
        private Gc019ScenarioResult narrativeFixture = null!;
        private Gc019ScenarioResult cardsGenerated = null!;
        private Gc019ScenarioResult cardsFixture = null!;

        /// <summary>
        /// The whole sequence, once per family per catalog. Four real worlds are built, presented, torn down and
        /// stopped; every later test is an assertion over these runs, so the suite never re-runs a world.
        /// </summary>
        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunBothFamiliesOverBothCatalogs()
        {
            narrativeCombined = Gc013NarrativeHost.RunBothGc019(out narrativeGenerated, out narrativeFixture);
            cardsCombined = Gc013CardsHost.RunBothGc019(out cardsGenerated, out cardsFixture);
        }

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own world and its own adapter frame down; this guarantees clean process-wide
            // registries if a step failed mid-way.
            AdapterFrameRegistry.Reset();
            UnityWorldRegistry.ResetAll();
        }

        // ------------------------------------------------------------------ TC-01: the committed world and its targets

        /// <summary>
        /// World, lane, live targets and the committed assembly: the adapter subjects exist and the family's own
        /// provider mounted and published rows (P-030, P-042).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheWorldAndCommittedTargetsObservationPasses()
        {
            AssertObservation("gc019-world-and-committed-targets");
        }

        // ------------------------------------------------------------------ TC-02: one sampled input command

        /// <summary>
        /// A typed input sample becomes exactly one committed command, its retransmission becomes none, and gameplay
        /// committed a result for it (P-037, P-042, P-050).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheInputSampleBecomesACommittedCommandObservationPasses()
        {
            AssertObservation("gc019-input-sample-becomes-a-committed-command");
        }

        // ------------------------------------------------------------------ TC-03: a stale input completion

        /// <summary>
        /// A delayed input completion whose activation was retired is discarded, releases its staged acquisition and
        /// reaches no command port (P-007, P-047).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheRetiredActivationInputCompletionIsDiscardedObservationPasses()
        {
            AssertObservation("gc019-input-completion-from-a-retired-activation-is-discarded");
        }

        // ------------------------------------------------------------------ TC-04: an asset lease under a live token

        /// <summary>
        /// An asset lease completed under a live activation becomes readable data and is recorded in the world's
        /// resource ledger (P-007, P-029, P-048).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheAssetLeaseUnderALiveTokenObservationPasses()
        {
            AssertObservation("gc019-asset-lease-completes-under-a-live-token");
        }

        // ------------------------------------------------------------------ TC-05: a late asset completion

        /// <summary>
        /// Completions that arrive after the activation was retired, and after the table was retired, install
        /// nothing and still release their own acquisition (P-007, P-047, P-048).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheLateAssetCompletionIsRefusedObservationPasses()
        {
            AssertObservation("gc019-late-asset-completion-cannot-write-a-retired-world");
        }

        // ------------------------------------------------------------------ TC-06: presentation of committed output

        /// <summary>
        /// Every presented value equals the published binding row of its target, capability and slot, and every
        /// presented composition parent equals the committed scope (P-010, P-034, P-045).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void ThePresentationOfTheCommittedSnapshotObservationPasses()
        {
            AssertObservation("gc019-presentation-reads-the-committed-snapshot");
        }

        // ------------------------------------------------------------------ TC-07: an idle world presents

        /// <summary>
        /// Idle host frames commit no step and still present, and a pass whose token is not newer applies nothing
        /// (P-036, P-045).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheIdleWorldPresentationObservationPasses()
        {
            AssertObservation("gc019-idle-world-presents-without-stepping");
        }

        // ------------------------------------------------------------------ TC-08: view destruction leaves gameplay intact

        /// <summary>
        /// Destroying every view changes no published row, no revision, no epoch, no step and no ledger record, and a
        /// real command still commits (P-003, P-024).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheViewDestructionLeavesGameplayIntactObservationPasses()
        {
            AssertObservation("gc019-view-destruction-leaves-gameplay-intact");
        }

        // ------------------------------------------------------------------ TC-09: a visual reparent moves no composition

        /// <summary>
        /// A visual reparent moves one Transform parent pointer and leaves the committed composition parent and the
        /// whole scope tree unchanged (P-010, 04 s6).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheVisualReparentLeavesCompositionUnchangedObservationPasses()
        {
            AssertObservation("gc019-visual-reparent-does-not-move-composition");
        }

        // ------------------------------------------------------------------ TC-10: no external authority

        /// <summary>
        /// No external authority is declared for these worlds, an intent is refused as ECS-owned, and the compiled
        /// schedule contains no physics stage (P-034, P-059, TEST-019).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheExternalAuthorityAbsenceObservationPasses()
        {
            AssertObservation("gc019-does-not-declare-external-authority");
        }

        // ------------------------------------------------------------------ TC-11: adapter teardown

        /// <summary>
        /// The adapter frame retires its leases and views, the registry forgets it, and the world stops with no
        /// outstanding job, no retained resource and the owned-world registry back at its baseline (P-048, TEST-015).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheAdapterTeardownParticipatesInLifecycleObservationPasses()
        {
            AssertObservation("gc019-adapter-teardown-participates-in-lifecycle");
        }

        // ------------------------------------------------------------------ the observation table and its digests

        /// <summary>
        /// The eleven named observations are the whole gate: both catalog runs of both families record exactly these
        /// names, in this order, once each, and the digest each run reports is the digest this table produces — so a
        /// renamed, reordered, added or dropped observation changes the literal this test expects instead of shrinking
        /// the gate silently. NotRun (pending orchestrator build host).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheObservationTableIsExactlyThePublishedSequence()
        {
            Assert.That(Gc019Scenario.ObservationNames.Length, Is.EqualTo(11),
                "the gate records exactly eleven named observations");
            Assert.That(Gc019Scenario.QualifiedNames(Gc013NarrativeHost.Label).Length, Is.EqualTo(11));
            Assert.That(Gc019Scenario.QualifiedNames(Gc013NarrativeHost.Label)[0],
                Is.EqualTo(Gc013NarrativeHost.Label + "/" + Gc019Scenario.ObservationNames[0]));

            // The literals are the table's own digests, not a recorded run's: an all-passing run built from the
            // observation-name table must hash to them.
            var tableNarrative = new Gc019ScenarioResult(
                Gc013NarrativeHost.Label,
                PassingSteps(Gc013NarrativeHost.Label));
            var tableCards = new Gc019ScenarioResult(
                Gc013CardsHost.Label,
                PassingSteps(Gc013CardsHost.Label));
            Assert.That(tableNarrative.Digest, Is.EqualTo(NarrativeDigest),
                "the narrative digest literal must be the one this observation table produces");
            Assert.That(tableCards.Digest, Is.EqualTo(CardsDigest),
                "the card digest literal must be the one this observation table produces");

            // The real runs: the same named sequence, in order, per catalog, and every one of them all-passing.
            AssertFamily(Gc013NarrativeHost.Label, narrativeCombined, narrativeGenerated, narrativeFixture, NarrativeDigest);
            AssertFamily(Gc013CardsHost.Label, cardsCombined, cardsGenerated, cardsFixture, CardsDigest);

            Assert.That(narrativeGenerated.Digest, Is.Not.EqualTo(cardsGenerated.Digest),
                "the two families must not share one literal, or a family could report the other's run");
        }

        // ------------------------------------------------------------------ helpers

        private static IReadOnlyList<Gc019Step> PassingSteps(string label)
        {
            string[] names = Gc019Scenario.QualifiedNames(label);
            var steps = new List<Gc019Step>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                steps.Add(new Gc019Step(names[i], true, "expected-sequence"));
            }

            return steps;
        }

        private void AssertObservation(string bareName)
        {
            AssertObservationFor(Gc013NarrativeHost.Label, narrativeCombined, bareName);
            AssertObservationFor(Gc013CardsHost.Label, cardsCombined, bareName);
        }

        private static void AssertObservationFor(string label, IReadOnlyList<Gc019Step> combined, string bareName)
        {
            string qualified = label + "/" + bareName;
            string prefixed = FixturePrefix + qualified;
            int recorded = 0;
            var failures = new List<string>();
            for (int i = 0; i < combined.Count; i++)
            {
                Gc019Step step = combined[i];
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
                label + ": the observation '" + bareName + "' must be recorded once per catalog, once unprefixed and"
                + " once under '" + FixturePrefix + "'");
            Assert.That(failures, Is.Empty, label + ": failed observations: " + string.Join(" | ", failures.ToArray()));
        }

        private static void AssertFamily(
            string label,
            IReadOnlyList<Gc019Step> combined,
            Gc019ScenarioResult generated,
            Gc019ScenarioResult fixture,
            string expectedDigest)
        {
            Assert.That(generated.Steps.Count, Is.EqualTo(Gc019Scenario.ObservationNames.Length),
                "the generated-catalog run must record every named observation exactly once.");
            Assert.That(fixture.Steps.Count, Is.EqualTo(Gc019Scenario.ObservationNames.Length),
                "the fixture-catalog run must record every named observation exactly once.");
            Assert.That(fixture.AllPassed, Is.True, fixture.Describe());
            Assert.That(generated.AllPassed, Is.True, generated.Describe());

            // The digest is over the observation names and their pass flags, so asserting the literal asserts both:
            // the named sequence really ran, in order, and every step of it passed.
            Assert.That(generated.Digest, Is.EqualTo(expectedDigest),
                "the generated-catalog run must record the named GC-019 observations, all passing: " + generated.Describe());
            Assert.That(fixture.Digest, Is.EqualTo(expectedDigest),
                "the fixture-catalog run must record the named GC-019 observations, all passing: " + fixture.Describe());

            string[] expected = Gc019Scenario.QualifiedNames(label);
            Assert.That(combined.Count, Is.EqualTo(expected.Length * 2),
                "both catalogs must record every observation: " + combined.Count + " of " + (expected.Length * 2));
            Assert.That(Names(combined, string.Empty), Is.EqualTo(expected), "generated-catalog observation order");
            Assert.That(Names(combined, FixturePrefix), Is.EqualTo(expected), "fixture-catalog observation order");
        }

        private static List<Gc019Step> RunOf(IReadOnlyList<Gc019Step> combined, string prefix)
        {
            var steps = new List<Gc019Step>();
            for (int i = 0; i < combined.Count; i++)
            {
                string name = combined[i].Name;
                if (prefix.Length == 0)
                {
                    if (!name.StartsWith(FixturePrefix, System.StringComparison.Ordinal))
                    {
                        steps.Add(combined[i]);
                    }
                }
                else if (name.StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    steps.Add(combined[i]);
                }
            }

            return steps;
        }

        private static List<string> Names(IReadOnlyList<Gc019Step> combined, string prefix)
        {
            List<Gc019Step> steps = RunOf(combined, prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0 ? steps[i].Name : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }
    }
}
