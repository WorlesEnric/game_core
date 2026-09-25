#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Gc018.Tests
{
    /// <summary>
    /// GC-018 checkpoint capture and restore, EditMode half (docs/game-core/09-implementation-guide.md, GC-018 — "a
    /// checkpoint taken at a committed boundary round-trips into a new unexposed world, active and dormant state
    /// included, without handles").
    ///
    /// The scenario (`Gc018Scenario` over `Gc018NarrativeHost` and `Gc018CardsHost`) is shared with the standalone
    /// player probe (`-probeGc018`) and runs only real modules: GC-004's `CompositionHost` and its control lane,
    /// GC-006's derivation through GC-013's incremental engine, GC-007's ownership validation, GC-009's schedule
    /// compiler and temporal drivers, GC-008's planner and publisher into a real `Unity.Entities.World`, and the
    /// Unity persistence half — `UnityCommittedBoundaryReader`, `CheckpointCapture`, the generated
    /// `CheckpointCatalog` serializers through `CheckpointCodecAdapter`, `CheckpointRestorePlanner` and
    /// `CheckpointRestoreExecutor` over a real `IRestoreTargetBuilder`. No seam fixture participates, and no
    /// checkpoint document is synthesised by the test: the documents the refusals act on come from a real capture of
    /// a real world (the migration and reference cases build theirs with the production serializer, which is what
    /// makes them legal inputs rather than malformed ones).
    ///
    /// Each case runs the whole sequence over the committed generated catalog *and* over the family's hand-written
    /// generated-style catalog. Both runs record the same observation names with the same verdicts, so one digest
    /// literal per family is the whole claim: every named observation passed, over both catalogs, in order.
    /// </summary>
    [TestFixture]
    [Timeout(600000)]
    public sealed class Gc018IntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family suites use.</summary>
        private const string FixturePrefix = Gc018Scenario.FixtureRunPrefix;

        /// <summary>One family's combined-catalog runner, in the shape both hosts expose.</summary>
        private delegate IReadOnlyList<Gc018Step> RunBothCatalogs(
            out Gc018ScenarioResult generated,
            out Gc018ScenarioResult fixture);

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own worlds down; this guarantees a clean registry if a step failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// The narrative family: a world captured at its committed boundary with one queued choice command, one
        /// active and one dormant conversation row, a persistent clock and its pending wake, two drawn random
        /// streams and a real composition boundary; then the refusals (outside a boundary, corrupted, truncated,
        /// unknown schema version, ambiguous migration, corrupt reference) and finally the restore into a fresh
        /// unexposed world that keeps the mode, the boundaries, the state and the cursors while refusing every old
        /// handle and request id (P-032, P-049, P-053, P-054).
        /// </summary>
        [Test]
        public void TheNarrativeFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(Gc013NarrativeHost.Label, Gc013NarrativeHost.RunBothGc018, ProbeGc018.NarrativeDigest);
        }

        /// <summary>
        /// The card family: the same sequence over the card market — a queued transfer command, one active
        /// seat-state row and one dormant table-state row, the declared clock and wake, the league boundary, and the
        /// restore into a fresh unexposed world whose seats live at different native indices and whose old handles
        /// are refused.
        /// </summary>
        [Test]
        public void TheCardFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(Gc013CardsHost.Label, Gc013CardsHost.RunBothGc018, ProbeGc018.CardsDigest);
        }

        /// <summary>
        /// The two digest literals the probe, this suite and `run_gc018_probe.sh` all assert are computed from the
        /// observation-name table, not read from a run: a renamed, reordered, added or dropped observation changes
        /// the literal this test expects, so the qualification cannot silently shrink.
        /// </summary>
        [Test]
        public void TheCheckpointObservationTableIsExactlyThePublishedSequence()
        {
            Assert.That(Gc018Scenario.ObservationNames.Length, Is.EqualTo(16),
                "the checkpoint runner records exactly sixteen named observations");
            Assert.That(Gc018Scenario.QualifiedNames(Gc013NarrativeHost.Label).Length, Is.EqualTo(16));
            Assert.That(Gc018Scenario.QualifiedNames(Gc013NarrativeHost.Label)[0],
                Is.EqualTo(Gc013NarrativeHost.Label + "/" + Gc018Scenario.ObservationNames[0]));

            var narrative = new Gc018ScenarioResult(
                Gc013NarrativeHost.Label,
                PassingSteps(Gc013NarrativeHost.Label));
            var cards = new Gc018ScenarioResult(
                Gc013CardsHost.Label,
                PassingSteps(Gc013CardsHost.Label));

            Assert.That(narrative.Digest, Is.EqualTo(ProbeGc018.NarrativeDigest),
                "the narrative digest literal must be the one this observation table produces");
            Assert.That(cards.Digest, Is.EqualTo(ProbeGc018.CardsDigest),
                "the card digest literal must be the one this observation table produces");
            Assert.That(narrative.Digest, Is.Not.EqualTo(cards.Digest),
                "the two families must not share one literal, or a family could report the other's run");
        }

        private static IReadOnlyList<Gc018Step> PassingSteps(string label)
        {
            string[] names = Gc018Scenario.QualifiedNames(label);
            var steps = new List<Gc018Step>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                steps.Add(new Gc018Step(names[i], true, "expected-sequence"));
            }

            return steps;
        }

        private static void AssertFamily(string label, RunBothCatalogs run, string expectedDigest)
        {
            IReadOnlyList<Gc018Step> combined = run(
                out Gc018ScenarioResult generated, out Gc018ScenarioResult fixture);

            Assert.That(generated.Steps.Count, Is.EqualTo(Gc018Scenario.ObservationNames.Length),
                "the generated-catalog run must record every named observation exactly once.");
            Assert.That(fixture.Steps.Count, Is.EqualTo(Gc018Scenario.ObservationNames.Length),
                "the fixture-catalog run must record every named observation exactly once.");
            Assert.That(fixture.AllPassed, Is.True, fixture.Describe());

            // The digest is over the observation names and their pass flags, so asserting the literal asserts both:
            // the named sequence really ran, in order, and every step of it passed (P-008).
            Assert.That(generated.Digest, Is.EqualTo(expectedDigest),
                "the generated-catalog run must record the named checkpoint observations, all passing: "
                + generated.Describe());
            Assert.That(fixture.Digest, Is.EqualTo(expectedDigest),
                "the fixture-catalog run must record the named checkpoint observations, all passing: "
                + fixture.Describe());

            string[] expected = Gc018Scenario.QualifiedNames(label);
            Assert.That(combined.Count, Is.EqualTo(expected.Length * 2),
                "both catalogs must record every observation: " + combined.Count + " of " + (expected.Length * 2));
            Assert.That(Names(combined, string.Empty), Is.EqualTo(expected), "generated-catalog observation order");
            Assert.That(Names(combined, FixturePrefix), Is.EqualTo(expected), "fixture-catalog observation order");

            var failed = new List<string>();
            for (int i = 0; i < combined.Count; i++)
            {
                if (!combined[i].Passed)
                {
                    failed.Add(combined[i].ToString());
                }
            }

            Assert.That(failed, Is.Empty, "failed checkpoint observations: " + string.Join(" | ", failed.ToArray()));
        }

        private static List<Gc018Step> RunOf(IReadOnlyList<Gc018Step> combined, string prefix)
        {
            var steps = new List<Gc018Step>();
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

        private static List<string> Names(IReadOnlyList<Gc018Step> combined, string prefix)
        {
            List<Gc018Step> steps = RunOf(combined, prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0 ? steps[i].Name : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }
    }
}
