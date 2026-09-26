#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Adapters;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.W5Gate.Tests
{
    /// <summary>
    /// Wave 5 integration gate, EditMode half (docs/game-core/09-implementation-guide.md, Wave 5 gate; requirements
    /// P-004, P-005, P-007, P-024, P-028..P-032, P-034, P-045, P-047..P-050, P-053, P-055; tests TEST-002, TEST-009,
    /// TEST-010, TEST-014, TEST-015, TEST-016, TEST-017, TEST-018, TEST-019, TEST-022).
    ///
    /// The runner (`W5GateScenario` over `Gc013NarrativeHost` and `Gc013CardsHost`) builds one real world per family
    /// and drives the four finished Wave 5 modules over it: GC-016's retained observation leases, GC-017's
    /// deterministic fault latches inside the real apply path, GC-018's committed-boundary capture and unexposed
    /// restore, and GC-019's input/asset/presentation adapters. No seam fixture replaces a kernel module, and the
    /// checkpoints are written with the committed generated catalog's own serializers.
    ///
    /// Each observation runs the whole sequence over the committed generated catalog *and* over the family's
    /// hand-written generated-style catalog. Both runs record the same observation names with the same verdicts, so
    /// the digest over the observation table is the whole claim: every named observation passed, over both catalogs,
    /// in order. This suite compiles and asserts; it has not been executed on the authoring host.
    /// </summary>
    [TestFixture]
    public sealed class W5GateIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the sibling gate suites use.</summary>
        private const string FixturePrefix = W5GateScenario.FixtureRunPrefix;

        /// <summary>
        /// Milliseconds the one-time in-process runs may take: four real worlds (two families, two catalogs), each one
        /// faulted, captured and restored. NotRun (pending orchestrator build host).
        /// </summary>
        private const int RunTimeout = 3600000;

        /// <summary>Milliseconds one assertion-only test may take; they read the cached runs.</summary>
        private const int AssertTimeout = 60000;

        /// <summary>
        /// Digest of the narrative family's observation table: the canonical `name=pass` lines of
        /// `narrative/&lt;observation&gt;` over <see cref="W5GateScenario.ObservationNames"/>, LF separated, no
        /// trailing newline, SHA-256 hex - the same digest function the narrative trace and every earlier gate use. It
        /// is computed from the observation-name table, not read from a run, so a renamed, reordered, added or dropped
        /// observation changes this literal and the gate cannot silently shrink. NotRun (pending orchestrator build
        /// host).
        /// </summary>
        private const string NarrativeDigest = "768dc415bd79e76adacb2472f7af5383bff25b4da44635a94eb3fa64f4dc250c";

        /// <summary>The card family's digest literal, computed the same way over `cards/&lt;observation&gt;`.</summary>
        private const string CardsDigest = "5740b580c5b807796af5456396195baf761947ff6fc819e173074aeeeef297ff";

        private IReadOnlyList<W5GateStep> narrativeCombined = new List<W5GateStep>();
        private IReadOnlyList<W5GateStep> cardsCombined = new List<W5GateStep>();
        private W5GateScenarioResult narrativeGenerated = null!;
        private W5GateScenarioResult narrativeFixture = null!;
        private W5GateScenarioResult cardsGenerated = null!;
        private W5GateScenarioResult cardsFixture = null!;

        /// <summary>
        /// The whole sequence, once per family per catalog: four real worlds are built, faulted (prewrite and
        /// postwrite), captured, restored and torn down. Every later test is an assertion over these runs, so the
        /// suite never re-runs a world.
        /// </summary>
        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunBothFamiliesOverBothCatalogs()
        {
            narrativeCombined = Gc013NarrativeHost.RunBothW5Gate(out narrativeGenerated, out narrativeFixture);
            cardsCombined = Gc013CardsHost.RunBothW5Gate(out cardsGenerated, out cardsFixture);
        }

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own worlds and its own adapter frame down; this guarantees clean process-wide
            // registries if a step failed mid-way.
            AdapterFrameRegistry.Reset();
            UnityWorldRegistry.ResetAll();
        }

        // ------------------------------------------------------------------ TC-01: one world, retained observation

        /// <summary>
        /// The one real world this gate faults, captures and restores: its live targets, its published assembly, its
        /// declared clock and streams, its dormant row and its registered adapter frame (P-002, P-030, P-042).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheOneWorldObservationPasses()
        {
            AssertObservation("w5gate-one-world-with-retained-observation");
        }

        // ------------------------------------------------------------------ TC-02: read-only pinned snapshots

        /// <summary>
        /// A committed boundary leased through GC-016 is its own token's complete image, is pinned across a real
        /// publication, exposes no writable reference and cannot be altered through the copy a reader holds
        /// (P-007, P-045).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void ThePinnedSnapshotObservationPasses()
        {
            AssertObservation("w5gate-pinned-snapshots-are-read-only");
        }

        // ------------------------------------------------------------------ TC-03: the checkpoint

        /// <summary>
        /// The checkpoint is captured at the committed boundary *through* the observation lease, carries the leased
        /// step, the world's own queued-command disposition and the active and dormant rows, and mutates nothing
        /// (P-051, P-053).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCommittedBoundaryCaptureObservationPasses()
        {
            AssertObservation("w5gate-checkpoint-from-the-committed-boundary");
        }

        // ------------------------------------------------------------------ TC-04: the prewrite fault

        /// <summary>
        /// A fault armed at the validation boundary rejects the publication before any live write and leaves the old
        /// assembly published, running and unchanged, with the latch's own trace naming the boundary, the operation
        /// and the plan (P-028, P-029, P-052).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void ThePrewriteRefusalObservationPasses()
        {
            AssertObservation("w5gate-prewrite-fault-keeps-the-old-assembly");
        }

        // ------------------------------------------------------------------ TC-05: the postwrite fault

        /// <summary>
        /// A fault armed at the first live write faults the world after its structural writes: no epoch, no image, no
        /// step, the last good snapshot still byte-identical, and no later frame or command resumes it
        /// (P-030, P-031).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void ThePostwriteFailStopObservationPasses()
        {
            AssertObservation("w5gate-postwrite-fault-fail-stops-the-world");
        }

        // ------------------------------------------------------------------ TC-06: the restore

        /// <summary>
        /// The captured document restores into a new session, exposed only after validation, carrying the captured
        /// step, mode, scope tree, active and dormant rows, clocks and re-admitted command, while every handle minted
        /// in the faulted session is refused (P-004, P-005, P-032, P-049, P-055).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheNewSessionRestoreObservationPasses()
        {
            AssertObservation("w5gate-restore-into-a-new-session");
        }

        // ------------------------------------------------------------------ TC-07: the adapters on the new world

        /// <summary>
        /// Input, asset and presentation adapters bound to the restored world: a stamped command commits a step, a
        /// bounded lease completes under a live token, presentation reads the restored world's committed image, and
        /// destroying every view leaves its gameplay intact, while the faulted world's token, stamp and image are all
        /// refused (P-024, P-034, P-045, TEST-015, TEST-019).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheRestoredWorldAdaptersObservationPasses()
        {
            AssertObservation("w5gate-adapters-bind-to-the-restored-world");
        }

        // ------------------------------------------------------------------ TC-08: the retired world's callbacks

        /// <summary>
        /// A completion that arrives after the source world's adapter table was retired installs nothing, the retired
        /// world's last committed image is still readable, and the restored world keeps running and presenting
        /// (P-007, P-045, P-047, P-048).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheRetiredWorldCallbackObservationPasses()
        {
            AssertObservation("w5gate-retired-world-callbacks-are-rejected");
        }

        // ------------------------------------------------------------------ the whole table

        /// <summary>
        /// The digest over every observation of the narrative run, over both catalogs.
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheNarrativeDigestHoldsOverBothCatalogs()
        {
            AssertFamily(Gc013NarrativeHost.Label, narrativeCombined, narrativeGenerated, narrativeFixture, NarrativeDigest);
        }

        /// <summary>The digest over every observation of the card run, over both catalogs.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheCardsDigestHoldsOverBothCatalogs()
        {
            AssertFamily(Gc013CardsHost.Label, cardsCombined, cardsGenerated, cardsFixture, CardsDigest);
        }

        /// <summary>
        /// The frozen observation tables: the scenario, both family hosts and both digest literals agree, so a
        /// renamed or dropped observation changes this suite rather than shrinking it. A digest computed here from the
        /// name table is compared with the literal the probe and the suite carry (P-008).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheObservationTablesAndDigestsAgree()
        {
            Assert.That(W5GateScenario.ObservationNames.Length, Is.EqualTo(8));
            Assert.That(NarrativeDigest, Is.EqualTo(DigestOf(Gc013NarrativeHost.Label)));
            Assert.That(CardsDigest, Is.EqualTo(DigestOf(Gc013CardsHost.Label)));
            Assert.That(W5GateScenario.FixtureRunPrefix, Is.EqualTo("fixture:"));
        }

        // ------------------------------------------------------------------ helpers

        private void AssertObservation(string bareName)
        {
            AssertObservationFor(Gc013NarrativeHost.Label, narrativeCombined, bareName);
            AssertObservationFor(Gc013CardsHost.Label, cardsCombined, bareName);
        }

        private static void AssertObservationFor(string label, IReadOnlyList<W5GateStep> combined, string bareName)
        {
            string qualified = label + "/" + bareName;
            string prefixed = FixturePrefix + qualified;
            int recorded = 0;
            var failures = new List<string>();
            for (int i = 0; i < combined.Count; i++)
            {
                W5GateStep step = combined[i];
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
            IReadOnlyList<W5GateStep> combined,
            W5GateScenarioResult generated,
            W5GateScenarioResult fixture,
            string expectedDigest)
        {
            Assert.That(generated.Steps.Count, Is.EqualTo(W5GateScenario.ObservationNames.Length),
                "the generated-catalog run must record every named observation exactly once.");
            Assert.That(fixture.Steps.Count, Is.EqualTo(W5GateScenario.ObservationNames.Length),
                "the fixture-catalog run must record every named observation exactly once.");
            Assert.That(fixture.AllPassed, Is.True, fixture.Describe());
            Assert.That(generated.AllPassed, Is.True, generated.Describe());

            // The digest is over the observation names and their pass flags, so asserting the literal asserts both:
            // the named sequence really ran, in order, and every step of it passed.
            Assert.That(generated.Digest, Is.EqualTo(expectedDigest),
                "the generated-catalog run must record the named Wave 5 observations, all passing: "
                + generated.Describe());
            Assert.That(fixture.Digest, Is.EqualTo(expectedDigest),
                "the fixture-catalog run must record the named Wave 5 observations, all passing: " + fixture.Describe());

            string[] expected = W5GateScenario.QualifiedNames(label);
            Assert.That(combined.Count, Is.EqualTo(expected.Length * 2),
                "both catalogs must record every observation: " + combined.Count + " of " + (expected.Length * 2));
            Assert.That(Names(combined, string.Empty), Is.EqualTo(expected), "generated-catalog observation order");
            Assert.That(Names(combined, FixturePrefix), Is.EqualTo(expected), "fixture-catalog observation order");
        }

        /// <summary>The digest the probe and this suite must carry for one family, recomputed from the name table.</summary>
        private static string DigestOf(string label)
        {
            string[] names = W5GateScenario.QualifiedNames(label);
            var lines = new List<string>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                lines.Add(names[i] + "=pass");
            }

            return GameCore.Rules.Narrative.NarrativeDigest.OfLines(lines);
        }

        private static List<W5GateStep> RunOf(IReadOnlyList<W5GateStep> combined, string prefix)
        {
            var steps = new List<W5GateStep>();
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

        private static List<string> Names(IReadOnlyList<W5GateStep> combined, string prefix)
        {
            List<W5GateStep> steps = RunOf(combined, prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0 ? steps[i].Name : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }
    }
}
