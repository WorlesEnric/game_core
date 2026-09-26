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
//   P-012  unexpected provider failure                    (`LifecycleController.FailProvider`,
//                                                          `FaultBoundary.ProviderDeactivationPublication`)
//
// Normative anchors: P-002 (one host per world; the runner builds each world through the registry), P-008 (the
// observation names and their verdicts are the digest, so a renamed, reordered, added or dropped step fails here
// instead of shrinking the gate), P-029/P-030/P-031 (the refusal and fault shapes), P-035 (a world is `Running`
// only after its initial validated publication), P-047/P-048 (every world this fixture's run creates is settled
// and disposed, and `UnityWorldRegistry.ResetAll()` guarantees it even after a step failed mid-way), P-049/P-051
// (recovery and the serialized cancellation cutoff).
//
// The runner (`FaultScenario` over `FaultScenarioHost`) is shared with the standalone player probe, so the same
// seventeen observations execute in the Editor and in a stripped player. Both digest literals below are recomputed
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

        /// <summary>Digest the narrative run must report over its seventeen named observations, all passing (P-008).</summary>
        private const string NarrativeDigest = "3a3be6bbd26a5824bdd47a62226023da5da43e7c10fbf3ddae35b3ea3f1a5930";

        /// <summary>Digest the card run must report over its seventeen named observations, all passing (P-008).</summary>
        private const string CardsDigest = "eea66452527f87a9ff145e492420a0075a7ba59aba760e92569e322e48bdc1ec";

        /// <summary>
        /// P-012's first half, exactly as `FaultScenario.ObservationNames` freezes it: the safe dependency-closure
        /// deactivation published, with the failed provider absent from the new revision and epoch. The literal is
        /// repeated here instead of taken from the scenario types, because the whole point of the pin is that a
        /// rename anywhere fails this suite rather than shrinking the gate (P-008).
        /// </summary>
        private const string ProviderFailureDeactivation = "gc017-provider-failure-publishes-a-safe-deactivation";

        /// <summary>
        /// P-012's second half: the deactivation could not publish, so admission closed and the world faulted.
        /// </summary>
        private const string ProviderFailureFaultedWorld =
            "gc017-provider-failure-that-cannot-publish-faults-the-world";

        /// <summary>Index the frozen table gives P-012's first half: after the recovery rows, before teardown.</summary>
        private const int ProviderFailureIndex = 14;

        /// <summary>
        /// Every observation of the frozen table, in execution order, as literals. The table cannot be renamed,
        /// reordered, extended or shortened without this array failing, which is what keeps the run, both digest
        /// literals and the standalone probe's step list in step with each other (P-008).
        /// </summary>
        private static readonly string[] FrozenNames =
        {
            "gc017-initial-world-lane-and-observer",
            "gc017-validation-fault-rejects-and-keeps-the-old-assembly",
            "gc017-acquisition-fault-releases-staged-leases",
            "gc017-cancellation-before-the-cutoff-releases-staged-work",
            "gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication",
            "gc017-prewrite-migration-fault-preserves-live-state",
            "gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image",
            "gc017-structural-playback-fault-stops-the-step-commit",
            "gc017-gate-installation-fault-stops-after-live-writes",
            "gc017-fence-fault-settles-handles-and-keeps-the-old-assembly",
            "gc017-cleanup-fault-retains-staged-ownership",
            "gc017-cleanup-boundary-releases-what-a-refusal-staged",
            "gc017-recovery-from-initial-definitions-into-a-new-world",
            "gc017-old-callback-after-recovery-is-rejected",
            ProviderFailureDeactivation,
            ProviderFailureFaultedWorld,
            "gc017-teardown-settles-and-disposes",
        };

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
        /// incarnation, a callback from the old incarnation discarded by the recovered world, and P-012's unexpected
        /// provider failure on worlds of its own — the safe dependency-closure deactivation, and the fail-stop that
        /// applies when that deactivation cannot publish (P-012, P-029, P-031, P-047, P-049, P-051).
        /// </summary>
        [Test]
        public void TheNarrativeFamilyPassesEveryObservationOverBothCatalogs()
        {
            AssertFamily(W4GateNarrativeHost.Label, FaultScenarioHost.RunNarrative, NarrativeDigest);
        }

        /// <summary>
        /// The card family: the same seventeen observations over the card market's own revision.
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
            Assert.That(FaultScenario.ObservationNames, Is.EqualTo(FrozenNames),
                "the frozen table must be exactly these names in this order (P-008)");
            Assert.That(FaultScenario.ObservationNames.Length, Is.EqualTo(17),
                "the fault run records exactly seventeen named observations");
            Assert.That(FaultScenario.QualifiedNames(W4GateNarrativeHost.Label).Length, Is.EqualTo(17));
            Assert.That(FaultScenario.QualifiedNames(W4GateNarrativeHost.Label)[0],
                Is.EqualTo(W4GateNarrativeHost.Label + "/" + FaultScenario.ObservationNames[0]));
            Assert.That(FaultScenario.ObservationNames[ProviderFailureIndex],
                Is.EqualTo(ProviderFailureDeactivation),
                "P-012's first half runs after the recovery rows and before teardown");
            Assert.That(FaultScenario.ObservationNames[ProviderFailureIndex + 1],
                Is.EqualTo(ProviderFailureFaultedWorld),
                "P-012's second half follows its first half directly");
            Assert.That(FaultScenario.ObservationNames[FrozenNames.Length - 1],
                Is.EqualTo("gc017-teardown-settles-and-disposes"),
                "teardown is still the last observation, so the appended pair did not displace it");

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

        /// <summary>
        /// P-012's first half, pinned by name at the index the frozen table gives it, for the narrative family. The
        /// run itself — that this observation is recorded, in this order, and passes — is asserted by
        /// <see cref="TheNarrativeFamilyPassesEveryObservationOverBothCatalogs"/>; what this test adds is the
        /// literal pin, so a rename, reorder, addition or drop of P-012's observation fails a test that names it.
        /// </summary>
        [Test]
        public void TheNarrativeFamilyPinsTheProviderFailureDeactivation() =>
            AssertObservation(
                W4GateNarrativeHost.Label, ProviderFailureIndex, ProviderFailureDeactivation);

        /// <summary>P-012's second half, pinned by name at its frozen index, for the narrative family.</summary>
        [Test]
        public void TheNarrativeFamilyPinsTheProviderFailureThatFaultsTheWorld() =>
            AssertObservation(
                W4GateNarrativeHost.Label, ProviderFailureIndex + 1, ProviderFailureFaultedWorld);

        /// <summary>P-012's first half, pinned by name at the index the frozen table gives it, for the card family.</summary>
        [Test]
        public void TheCardsFamilyPinsTheProviderFailureDeactivation() =>
            AssertObservation(
                W4GateCardsHost.Label, ProviderFailureIndex, ProviderFailureDeactivation);

        /// <summary>P-012's second half, pinned by name at its frozen index, for the card family.</summary>
        [Test]
        public void TheCardsFamilyPinsTheProviderFailureThatFaultsTheWorld() =>
            AssertObservation(
                W4GateCardsHost.Label, ProviderFailureIndex + 1, ProviderFailureFaultedWorld);

        /// <summary>
        /// One family's pin of one observation: the frozen table carries this literal at this index, the family's
        /// qualified name carries it at the same index, and the name appears exactly once in the table — so a
        /// rename, a reorder, a duplicate or a dropped observation fails here by name (P-008).
        /// </summary>
        private static void AssertObservation(string label, int index, string bareName)
        {
            Assert.That(FaultScenario.ObservationNames.Length, Is.GreaterThan(index),
                "the frozen table must still carry an entry at index " + index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.That(FaultScenario.ObservationNames[index], Is.EqualTo(bareName),
                "the frozen table's entry at index " + index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + " must be " + bareName);
            Assert.That(FaultScenario.QualifiedNames(label)[index], Is.EqualTo(label + "/" + bareName),
                "this family's qualified name carries the observation at the same index");

            int occurrences = 0;
            for (int i = 0; i < FaultScenario.ObservationNames.Length; i++)
            {
                if (string.Equals(FaultScenario.ObservationNames[i], bareName, System.StringComparison.Ordinal))
                {
                    occurrences++;
                }
            }

            Assert.That(occurrences, Is.EqualTo(1),
                "the observation is recorded exactly once, so it cannot be duplicated into the digest");
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
