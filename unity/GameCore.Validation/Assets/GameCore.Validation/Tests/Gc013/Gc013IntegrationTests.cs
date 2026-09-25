#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Gc013.Tests
{
    /// <summary>
    /// GC-013 wave-4 transitions, EditMode half (docs/game-core/09-implementation-guide.md, Wave 4 — "integrate indexed
    /// move/mode changes ... Demonstrate both mode directions, a subtree move preserving state ...").
    ///
    /// The scenarios themselves (`Gc013Scenario` over `Gc013NarrativeHost` and `Gc013CardsHost`) are shared with the
    /// standalone player probe (`-probeGc013`) and run only real modules: GC-004's `CompositionHost` and its control
    /// lane (wired to the production `DerivationModeSwitchValidator`, so a switch whose proposed closure the kernel
    /// refuses keeps the old mode), GC-006's derivation over the committed composition through GC-013's incremental
    /// engine, GC-007's ownership validation, GC-009's schedule compiler and GC-008's planner and publisher into a real
    /// `Unity.Entities.World`. No seam fixture participates.
    ///
    /// Each case runs the whole sequence over the committed generated catalog *and* over the family's hand-written
    /// generated-style catalog. Both runs record the same observation names with the same verdicts, so one digest
    /// literal per family is the whole claim: every named observation passed, over both catalogs, in order.
    /// </summary>
    [TestFixture]
    public sealed class Gc013IntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family suites use.</summary>
        private const string FixturePrefix = Gc013Scenario.FixtureRunPrefix;

        /// <summary>One family's combined-catalog runner, in the shape both hosts expose.</summary>
        private delegate IReadOnlyList<Gc013Step> RunBothCatalogs(
            out Gc013ScenarioResult generated,
            out Gc013ScenarioResult fixture);

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own world down; this guarantees a clean registry if a step failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// The narrative family: the chapter providers mounted in Automatic, the village branch moved under the
        /// chapter-two provider, both propagation-mode directions applied, a future villager spawned in Conservative
        /// and an exclusive conflict refused with the old mode and membership intact (P-013, P-014, P-025).
        /// </summary>
        [Test]
        public void TheNarrativeFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(Gc013NarrativeHost.Label, Gc013NarrativeHost.RunBoth, ProbeGc013.NarrativeDigest);
        }

        /// <summary>
        /// The card family: the two scoring providers mounted in Automatic, seat A's scope moved into the quiet league,
        /// both propagation-mode directions applied, a future seat spawned in Conservative and the overlapping
        /// draw-policy pair's conflict refused with the old mode and membership intact (P-013, P-014, P-025).
        /// </summary>
        [Test]
        public void TheCardFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(Gc013CardsHost.Label, Gc013CardsHost.RunBoth, ProbeGc013.CardsDigest);
        }

        private static void AssertFamily(string label, RunBothCatalogs run, string expectedDigest)
        {
            IReadOnlyList<Gc013Step> combined = run(out Gc013ScenarioResult generated, out Gc013ScenarioResult fixture);

            Assert.That(generated.Steps.Count, Is.EqualTo(Gc013Scenario.ObservationNames.Length),
                "the generated-catalog run must record every named observation exactly once.");
            Assert.That(fixture.Steps.Count, Is.EqualTo(Gc013Scenario.ObservationNames.Length),
                "the fixture-catalog run must record every named observation exactly once.");
            Assert.That(fixture.AllPassed, Is.True, fixture.Describe());

            // The digest is over the observation names and their pass flags, so asserting the literal asserts both:
            // the named sequence really ran, in order, and every step of it passed (P-008).
            Assert.That(generated.Digest, Is.EqualTo(expectedDigest),
                "the generated-catalog run must record the named GC-013 observations, all passing: " + generated.Describe());
            Assert.That(fixture.Digest, Is.EqualTo(expectedDigest),
                "the fixture-catalog run must record the named GC-013 observations, all passing: " + fixture.Describe());

            string[] expected = Gc013Scenario.QualifiedNames(label);
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

            Assert.That(failed, Is.Empty, "failed GC-013 observations: " + string.Join(" | ", failed.ToArray()));
        }

        private static List<Gc013Step> RunOf(IReadOnlyList<Gc013Step> combined, string prefix)
        {
            var steps = new List<Gc013Step>();
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

        private static List<string> Names(IReadOnlyList<Gc013Step> combined, string prefix)
        {
            List<Gc013Step> steps = RunOf(combined, prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0 ? steps[i].Name : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }
    }
}
