// GameCore.Faults.Tests — the GC-017 fault matrix's EditMode half.
//
// TEST-016 rows, from `docs/game-core/08-validation-and-performance.md`: this fixture is the Unity-Editor half of
// the same run the standalone probe archives, and it asserts every row through the *runner* rather than through a
// per-row unit test, because a row's meaning is what the real apply or cancellation path does when one named
// boundary is armed — not that a boolean can be flipped.
//
//   row 1  validation / enumeration refusal              (`FaultBoundary.Validation`)
//   row 2  resource acquisition or plan preparation      (`FaultBoundary.Acquisition`)
//   row 3  cancellation before and after the cutoff      (`CompositionHost.Cancel`, P-051)
//   row 4  the publication fence                         (`FaultBoundary.Fence`)
//   row 5  migration, the first live write and the gate installation
//                                                        (`FaultBoundary.Migration`, `FirstLiveWrite`,
//                                                         `GateInstallation`)
//   row 6  structural playback before the step commit    (`FaultBoundary.StructuralPlayback`)
//   row 8  cleanup of a refused operation's staged work   (`FaultBoundary.Cleanup`)
//   row 10 recovery from initial definitions             (`InitialDefinitionRecovery`, P-049)
//
// Normative anchors: P-002 (one host per world; the runner builds each world through the registry), P-008 (the
// observation names and their verdicts are the digest, so a renamed, reordered, added or dropped step fails here
// instead of shrinking the gate), P-029/P-030/P-031 (the refusal and fault shapes), P-035 (a world is `Running`
// only after its initial validated publication), P-047/P-048 (every world this fixture's run creates is settled
// and disposed, and `UnityWorldRegistry.ResetAll()` guarantees it even after a step failed mid-way), P-049/P-051
// (recovery and the serialized cancellation cutoff).
//
// The runner (`FaultScenario` over `FaultScenarioHost`) is shared with the standalone player probe, so the same
// fifteen observations execute in the Editor and in a stripped player. Both digest literals below are recomputed
// from `FaultScenario.QualifiedNames(label)` rather than read from a run, so an observation table that drifted
// cannot report them.
#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Faults.Tests
{
    [TestFixture]
    [Timeout(600000)]
    public sealed class FaultScenarioIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family suites use.</summary>
        private const string FixturePrefix = FaultScenario.FixtureRunPrefix;

        /// <summary>Digest the narrative run must report over its fifteen named observations, all passing (P-008).</summary>
        private const string NarrativeDigest = "701a3c286098501456390975bbdc7e4bdb7218d3094f23e39e61b3744fa52b61";

        /// <summary>Digest the card run must report over its fifteen named observations, all passing (P-008).</summary>
        private const string CardsDigest = "5cd97d38a1023fe0c8b5239d611061e5696454be9ec7cc68d440506810201732";

        /// <summary>One family's combined-catalog runner, in the shape both hosts expose.</summary>
        private delegate IReadOnlyList<FaultScenarioStep> RunBothCatalogs(
            out FaultScenarioResult generated,
            out FaultScenarioResult fixture);

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own worlds down; this guarantees a clean registry if a step failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>
        /// The narrative family: a real world and its lane, an armed refusal at each prewrite boundary, both
        /// cancellation directions against the serialized cutoff, a prewrite migration, a postwrite fault on a world
        /// of its own, a structural-playback fault that stops a step commit, a cleanup refusal that retains what it
        /// staged and the falsifying counterpart that releases it, recovery from the faulted world into a new
        /// incarnation, and a callback from the old incarnation discarded by the recovered world (P-029, P-031,
        /// P-047, P-049, P-051).
        /// </summary>
        [Test]
        public void TheNarrativeFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(W4GateNarrativeHost.Label, FaultScenarioHost.RunNarrative, NarrativeDigest);
        }

        /// <summary>
        /// The card family: the same fifteen observations over the card market's own revision.
        /// </summary>
        [Test]
        public void TheCardsFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(W4GateCardsHost.Label, FaultScenarioHost.RunCards, CardsDigest);
        }

        /// <summary>
        /// The two digest literals this suite and the standalone probe both assert are computed from the observation
        /// table, not read from a run: a renamed, reordered, added or dropped observation changes the literal this
        /// test expects, so the fault matrix cannot silently shrink.
        /// </summary>
        [Test]
        public void TheFrozenObservationTableMatchesBothDigestLiterals()
        {
            Assert.That(FaultScenario.ObservationNames.Length, Is.EqualTo(15),
                "the fault run records exactly fifteen named observations");
            Assert.That(FaultScenario.QualifiedNames(W4GateNarrativeHost.Label).Length, Is.EqualTo(15));
            Assert.That(FaultScenario.QualifiedNames(W4GateNarrativeHost.Label)[0],
                Is.EqualTo(W4GateNarrativeHost.Label + "/" + FaultScenario.ObservationNames[0]));

            var narrative = new FaultScenarioResult(
                W4GateNarrativeHost.Label, PassingSteps(W4GateNarrativeHost.Label));
            var cards = new FaultScenarioResult(
                W4GateCardsHost.Label, PassingSteps(W4GateCardsHost.Label));

            Assert.That(narrative.Digest, Is.EqualTo(NarrativeDigest),
                "the narrative digest literal must be the one this observation table produces");
            Assert.That(cards.Digest, Is.EqualTo(CardsDigest),
                "the card digest literal must be the one this observation table produces");
            Assert.That(narrative.Digest, Is.Not.EqualTo(cards.Digest),
                "the two families must not share one literal, or a family could report the other's run");
        }

        private static IReadOnlyList<FaultScenarioStep> PassingSteps(string label)
        {
            string[] names = FaultScenario.QualifiedNames(label);
            var steps = new List<FaultScenarioStep>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                steps.Add(new FaultScenarioStep(names[i], true, "expected-sequence"));
            }

            return steps;
        }

        private static void AssertFamily(string label, RunBothCatalogs run, string expectedDigest)
        {
            IReadOnlyList<FaultScenarioStep> combined = run(
                out FaultScenarioResult generated, out FaultScenarioResult fixture);

            string[] expected = FaultScenario.QualifiedNames(label);
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

            Assert.That(failed, Is.Empty, "failed observations: " + string.Join(" | ", failed.ToArray()));
        }

        /// <summary>The observations of one half of a combined run: the unprefixed ones, or the prefixed ones.</summary>
        private static List<FaultScenarioStep> RunOf(IReadOnlyList<FaultScenarioStep> combined, string prefix)
        {
            var steps = new List<FaultScenarioStep>();
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

        private static List<string> Names(IReadOnlyList<FaultScenarioStep> combined, string prefix)
        {
            List<FaultScenarioStep> steps = RunOf(combined, prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0 ? steps[i].Name : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }
    }
}
