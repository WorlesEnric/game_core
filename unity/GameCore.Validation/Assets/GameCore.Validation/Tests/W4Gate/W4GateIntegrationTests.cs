#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.W4Gate.Tests
{
    /// <summary>
    /// Wave 4 integration gate, EditMode half (docs/game-core/09-implementation-guide.md, Wave 4 — "Run both families
    /// in IL2CPP, then integrate indexed move/mode changes, service-closure lifecycle and slot policies. Demonstrate
    /// both mode directions, a subtree move preserving state, suspend/resume and provider loss/unload.").
    ///
    /// The runner (`W4GateScenario` over `W4GateNarrativeHost` and `W4GateCardsHost`) is shared with the standalone
    /// player probe (`-probeW4Gate`), and it runs only real modules: GC-004's `CompositionHost` and its control lane
    /// (wired to GC-013's production `DerivationModeSwitchValidator`, so a mode switch whose proposed closure the
    /// kernel refuses keeps the old mode), GC-006's derivation through GC-013's incremental engine inside
    /// `DerivedAssemblyPipeline`, GC-007's ownership validation, GC-009's schedule compiler, GC-008's planner and
    /// publisher into a real `Unity.Entities.World`, GC-014's `LifecycleController` and its P-046/P-048 coordinator
    /// over the same three seams, and GC-015's `StateMigrationPipeline` over the plan the real publisher applies. No
    /// seam fixture participates.
    ///
    /// Each case runs the whole sequence over the committed generated catalog *and* over the family's hand-written
    /// generated-style catalog. Both runs record the same observation names with the same verdicts, so one digest
    /// literal per family is the whole claim: every named observation passed, over both catalogs, in order.
    /// </summary>
    [TestFixture]
    public sealed class W4GateIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family suites use.</summary>
        private const string FixturePrefix = W4GateScenario.FixtureRunPrefix;

        /// <summary>One family's combined-catalog runner, in the shape both hosts expose.</summary>
        private delegate IReadOnlyList<W4GateStep> RunBothCatalogs(
            out W4GateScenarioResult generated,
            out W4GateScenarioResult fixture);

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own world down; this guarantees a clean registry if a step failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// The narrative family: the chapter provider mounted and deriving into a real world, both propagation-mode
        /// directions with an existing and a future target, the village branch moved under the chapter-two provider
        /// with its live state and its complete binding set preserved, the chapter provider suspended and resumed, the
        /// required-service consumer made to wait and then resumed, an unload disposed in reverse acquisition order,
        /// and every declared slot policy executed against real storage (P-013, P-014, P-025, P-032, P-046, P-048).
        /// </summary>
        [Test]
        public void TheNarrativeFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(W4GateNarrativeHost.Label, W4GateNarrativeHost.RunBoth, ProbeW4Gate.NarrativeDigest);
        }

        /// <summary>
        /// The card family: the same sequence over the card market — the scoring provider mounted, both mode
        /// directions with an existing and a future seat, a league branch reparented with its state and bindings
        /// preserved, suspend/resume, required-provider loss and return, an unload with reverse-order disposal, and
        /// every declared slot policy executed against real storage.
        /// </summary>
        [Test]
        public void TheCardFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(W4GateCardsHost.Label, W4GateCardsHost.RunBoth, ProbeW4Gate.CardsDigest);
        }

        /// <summary>
        /// The two digest literals the probe, this suite and `run_w4_gate_probe.sh` all assert are computed from the
        /// observation-name table, not read from a run: a renamed, reordered, added or dropped observation changes the
        /// literal this test expects, so the gate cannot silently shrink.
        /// </summary>
        [Test]
        public void TheGateObservationTableIsExactlyThePublishedSequence()
        {
            Assert.That(W4GateScenario.ObservationNames.Length, Is.EqualTo(17),
                "the gate records exactly seventeen named observations");
            Assert.That(W4GateScenario.QualifiedNames(W4GateNarrativeHost.Label).Length, Is.EqualTo(17));
            Assert.That(W4GateScenario.QualifiedNames(W4GateNarrativeHost.Label)[0],
                Is.EqualTo(W4GateNarrativeHost.Label + "/" + W4GateScenario.ObservationNames[0]));

            var narrative = new W4GateScenarioResult(
                W4GateNarrativeHost.Label,
                PassingSteps(W4GateNarrativeHost.Label));
            var cards = new W4GateScenarioResult(
                W4GateCardsHost.Label,
                PassingSteps(W4GateCardsHost.Label));

            Assert.That(narrative.Digest, Is.EqualTo(ProbeW4Gate.NarrativeDigest),
                "the narrative digest literal must be the one this observation table produces");
            Assert.That(cards.Digest, Is.EqualTo(ProbeW4Gate.CardsDigest),
                "the card digest literal must be the one this observation table produces");
            Assert.That(narrative.Digest, Is.Not.EqualTo(cards.Digest),
                "the two families must not share one literal, or a family could report the other's run");
        }

        private static IReadOnlyList<W4GateStep> PassingSteps(string label)
        {
            string[] names = W4GateScenario.QualifiedNames(label);
            var steps = new List<W4GateStep>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                steps.Add(new W4GateStep(names[i], true, "expected-sequence"));
            }

            return steps;
        }

        private static void AssertFamily(string label, RunBothCatalogs run, string expectedDigest)
        {
            IReadOnlyList<W4GateStep> combined = run(
                out W4GateScenarioResult generated, out W4GateScenarioResult fixture);

            Assert.That(generated.Steps.Count, Is.EqualTo(W4GateScenario.ObservationNames.Length),
                "the generated-catalog run must record every named observation exactly once.");
            Assert.That(fixture.Steps.Count, Is.EqualTo(W4GateScenario.ObservationNames.Length),
                "the fixture-catalog run must record every named observation exactly once.");
            Assert.That(fixture.AllPassed, Is.True, fixture.Describe());

            // The digest is over the observation names and their pass flags, so asserting the literal asserts both:
            // the named sequence really ran, in order, and every step of it passed (P-008).
            Assert.That(generated.Digest, Is.EqualTo(expectedDigest),
                "the generated-catalog run must record the named W4-gate observations, all passing: "
                + generated.Describe());
            Assert.That(fixture.Digest, Is.EqualTo(expectedDigest),
                "the fixture-catalog run must record the named W4-gate observations, all passing: "
                + fixture.Describe());

            string[] expected = W4GateScenario.QualifiedNames(label);
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

            Assert.That(failed, Is.Empty, "failed W4-gate observations: " + string.Join(" | ", failed.ToArray()));
        }

        private static List<W4GateStep> RunOf(IReadOnlyList<W4GateStep> combined, string prefix)
        {
            var steps = new List<W4GateStep>();
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

        private static List<string> Names(IReadOnlyList<W4GateStep> combined, string prefix)
        {
            List<W4GateStep> steps = RunOf(combined, prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0 ? steps[i].Name : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }
    }
}
