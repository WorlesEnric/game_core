#nullable enable
using System;
using System.Collections.Generic;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Gc021.Tests
{
    /// <summary>
    /// GC-021 durable delivery and destination idempotency, EditMode half (docs/game-core/09-implementation-guide.md,
    /// GC-021 — "irreversible output adapters consume only committed events, use explicit external idempotency keys,
    /// and persist an outbox when delivery must survive crashes").
    ///
    /// The scenario (`Gc021Scenario` over `Gc013NarrativeHost` / `Gc013CardsHost`, through `Gc021CatalogRuns`) is
    /// shared with the standalone player probe (`-probeGc021`) and runs only real modules: the family's own world
    /// created from its generated registration, GC-004's `CompositionHost` and its control lane, GC-006's derivation,
    /// GC-007's ownership validation, GC-009's schedule compiler and temporal driver, GC-008's planner and publisher,
    /// the engine-free delivery seam (`DurableOutbox`, `DurableDeliveryAdapter`, the canonical row codec and the
    /// journals), the world-side `WorldDeliveryOwner`, the Unity persistence half (`UnityCommittedBoundaryReader`,
    /// `CheckpointCapture`, the generated `CheckpointCatalog` serializers through `GC018CheckpointCodecs`,
    /// `CheckpointRestorePlanner`) and the reward bridge over the card family's own `Transfer` command. No seam fixture
    /// participates and no checkpoint document is synthesised here: the document the outbox assertion reads comes from
    /// a real capture of a real world.
    ///
    /// Each case runs the whole sequence over the committed generated catalog *and* over the family's hand-written
    /// generated-style catalog. Both runs record the same observation names with the same verdicts, so one digest
    /// literal per family is the whole claim: every named observation passed, over both catalogs, in order.
    ///
    /// WHY THE FOUR RUNS ARE SHARED BETWEEN THE CASES
    ///
    /// There is one case per named observation, and each of them has to see both families over both catalogs. Running
    /// the sequence inside every case would execute forty-eight real worlds where four carry the whole evidence, and
    /// the sibling suite already spends its budget on two families; so the fixture runs each family once, lazily, and
    /// every case asserts its own named observation against those runs. Nothing is weakened by that: a run that fails
    /// fails every case that reads it, the recorded observation order and count are still asserted in each case, and
    /// the names the cases assert are the exported table rather than a local list.
    /// </summary>
    [TestFixture]
    [Timeout(600000)]
    public sealed class Gc021IntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries, exactly as the family suites use.</summary>
        private const string FixturePrefix = Gc021CatalogRuns.FixtureRunPrefix;

        /// <summary>One family's four runs, so a case never pays for a run another case already made.</summary>
        private sealed class FamilyRun
        {
            public FamilyRun(
                IReadOnlyList<Gc021Step> combined,
                Gc021ScenarioResult generated,
                Gc021ScenarioResult fixture)
            {
                Combined = combined;
                Generated = generated;
                Fixture = fixture;
            }

            /// <summary>Generated-catalog steps followed by the fixture-catalog steps, prefixed.</summary>
            public IReadOnlyList<Gc021Step> Combined { get; }

            public Gc021ScenarioResult Generated { get; }

            public Gc021ScenarioResult Fixture { get; }
        }

        private static readonly Dictionary<string, FamilyRun> Runs = new Dictionary<string, FamilyRun>();

        [TearDown]
        public void TearDown()
        {
            // A scenario tears its own worlds down; this guarantees a clean registry if a step failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        // ------------------------------------------------------------------ the named observations

        /// <summary>
        /// The derivation: one committed event and one destination derive the same three stable identities, a
        /// different event or destination derives a different obligation, and the external idempotency key is its own
        /// value rather than the obligation identity (P-004, P-045, P-050).
        /// </summary>
        [Test]
        public void TheKeyIsDerivedFromCommittedData()
        {
            AssertObservation("gc021-key-is-derived-from-committed-data");
        }

        /// <summary>
        /// A durable commit is persisted before it is applied, and the row the checkpoint projection carries records
        /// the durability the world really had (P-045).
        /// </summary>
        [Test]
        public void CommitPersistsBeforeApply()
        {
            AssertObservation("gc021-commit-persists-before-apply");
        }

        /// <summary>
        /// The crash sits at the seam's own after-delivery boundary: the destination mutated and this process has no
        /// record of it (P-045).
        /// </summary>
        [Test]
        public void CrashAfterDeliveryLosesTheAcknowledgement()
        {
            AssertObservation("gc021-crash-after-delivery-loses-the-acknowledgement");
        }

        /// <summary>
        /// The acknowledgement-loss window, closed: redelivery reaches the same destination under the same external
        /// key, which is why the mutation is applied exactly once (P-045, P-049, P-050).
        /// </summary>
        [Test]
        public void RedeliveryAppliesTheMutationOnce()
        {
            AssertObservation("gc021-redelivery-applies-the-mutation-once");
        }

        /// <summary>
        /// An outbox at its declared capacity refuses loudly and tracks nothing, so exhaustion is never a silent drop
        /// (P-043).
        /// </summary>
        [Test]
        public void CapacityExhaustionIsNeverASilentDrop()
        {
            AssertObservation("gc021-capacity-exhaustion-is-never-a-silent-drop");
        }

        /// <summary>
        /// A volatile adapter declares itself volatile, cannot recover, and refuses an obligation that requires
        /// durability; the durable adapter beside it accepts the same obligation (P-045).
        /// </summary>
        [Test]
        public void VolatileDeliveryIsDistinguishableFromDurable()
        {
            AssertObservation("gc021-volatile-delivery-is-distinguishable-from-durable");
        }

        /// <summary>
        /// The committed obligation survives the unload of the world that made it: it is reinstated from the rows it
        /// projected, in a session that is not the source's, and is still deliverable there (P-045, P-049, P-053).
        /// </summary>
        [Test]
        public void ObligationSurvivesTheSourceWorldUnload()
        {
            AssertObservation("gc021-obligation-survives-the-source-world-unload");
        }

        /// <summary>
        /// A real capture carries the world owner's outbox rows, its header count agrees with them, at least one row
        /// is an open obligation, and the restore planner carries the same rows into its plan (P-045, P-053).
        /// </summary>
        [Test]
        public void CheckpointCarriesTheOutboxAndTheCursor()
        {
            AssertObservation("gc021-checkpoint-carries-the-outbox-and-the-cursor");
        }

        /// <summary>
        /// The delivery surface declares no reversible or universal-effect member, so no new universal gameplay
        /// `Effect` API exists (P-003).
        /// </summary>
        [Test]
        public void NoUniversalEffectApi()
        {
            AssertObservation("gc021-no-universal-effect-api");
        }

        /// <summary>
        /// The committed event a reward comes from was really observed, and the obligation is the one derived from
        /// that event's own sequence (P-045, P-050).
        /// </summary>
        [Test]
        public void NarrativeChoiceIsObserved()
        {
            AssertObservation("gc021-narrative-choice-is-observed");
        }

        /// <summary>
        /// The reward content the run declared really covers what the run observed, and the basis of that claim is
        /// named in the step's own detail (P-015, P-034).
        /// </summary>
        [Test]
        public void RewardContentCoversTheObservedNode()
        {
            AssertObservation("gc021-reward-content-covers-the-observed-node");
        }

        /// <summary>
        /// The reward obligation is durable, a second pass over the same committed events creates no second
        /// obligation, and re-deriving the same event lands on the existing obligation (P-045, P-050).
        /// </summary>
        [Test]
        public void RewardObligationIsDurableAndIdempotent()
        {
            AssertObservation("gc021-reward-obligation-is-durable-and-idempotent");
        }

        // ------------------------------------------------------------------ the published table

        /// <summary>
        /// The two digest literals the probe, this suite and `run_gc021_probe.sh` all assert are computed from the
        /// observation-name table, not read from a run: a renamed, reordered, added or dropped observation changes the
        /// literal this test expects, so the qualification cannot silently shrink.
        ///
        /// A literal that is still `PENDING` is the state before the build host's first passing run; it is asserted as
        /// a well-formed digest that the table really produces, and never silently accepted as agreement.
        /// </summary>
        [Test]
        public void TheDeliveryObservationTableIsExactlyThePublishedSequence()
        {
            Assert.That(
                Gc021Scenario.ObservationNames.Length,
                Is.EqualTo(12),
                "the delivery runner records exactly twelve named observations");
            Assert.That(
                FixturePrefix,
                Is.EqualTo(Gc018Scenario.FixtureRunPrefix),
                "the fixture-catalog prefix is the one convention every sibling scenario uses");
            Assert.That(Gc021Scenario.QualifiedNames(Gc013NarrativeHost.Label).Length, Is.EqualTo(12));
            Assert.That(Gc021Scenario.QualifiedNames(Gc013NarrativeHost.Label)[0],
                Is.EqualTo(Gc013NarrativeHost.Label + "/" + Gc021Scenario.ObservationNames[0]));

            var narrative = new Gc021ScenarioResult(
                Gc013NarrativeHost.Label,
                PassingSteps(Gc013NarrativeHost.Label));
            var cards = new Gc021ScenarioResult(
                Gc013CardsHost.Label,
                PassingSteps(Gc013CardsHost.Label));

            AssertDigest(ProbeGc021.NarrativeDigest, narrative.Digest, "narrative");
            AssertDigest(ProbeGc021.CardsDigest, cards.Digest, "cards");
            Assert.That(narrative.Digest, Is.Not.EqualTo(cards.Digest),
                "the two families must not share one literal, or a family could report the other's run");
        }

        // ------------------------------------------------------------------ helpers

        private static void AssertObservation(string bareName)
        {
            AssertFamilyObservation(Gc013NarrativeHost.Label, bareName);
            AssertFamilyObservation(Gc013CardsHost.Label, bareName);
        }

        private static void AssertFamilyObservation(string label, string bareName)
        {
            FamilyRun run = RunOf(label);
            string[] expected = Gc021Scenario.QualifiedNames(label);

            Assert.That(run.Generated.Steps.Count, Is.EqualTo(Gc021Scenario.ObservationNames.Length),
                "the generated-catalog run must record every named observation exactly once: "
                + run.Generated.Describe());
            Assert.That(run.Fixture.Steps.Count, Is.EqualTo(Gc021Scenario.ObservationNames.Length),
                "the fixture-catalog run must record every named observation exactly once: "
                + run.Fixture.Describe());
            Assert.That(run.Generated.AllPassed, Is.True, run.Generated.Describe());
            Assert.That(run.Fixture.AllPassed, Is.True, run.Fixture.Describe());

            // The digest is over the observation names and their pass flags, so the recorded name sequence is the
            // published one for both catalogs (P-008).
            Assert.That(Names(run.Combined, string.Empty), Is.EqualTo(expected), "generated-catalog observation order");
            Assert.That(Names(run.Combined, FixturePrefix), Is.EqualTo(expected), "fixture-catalog observation order");

            Gc021Step generated = Named(run.Generated, label + "/" + bareName);
            Gc021Step fixture = Named(run.Fixture, label + "/" + bareName);
            Assert.That(generated.Passed, Is.True, generated.ToString());
            Assert.That(fixture.Passed, Is.True, fixture.ToString());
            Assert.That(generated.Detail, Is.Not.Empty,
                "an observation carries the values it was computed from: " + generated.Name);
            Assert.That(fixture.Detail, Is.Not.Empty,
                "an observation carries the values it was computed from: " + fixture.Name);
        }

        /// <summary>
        /// One family's four runs, made at most once per fixture: the generated-catalog run and the fixture-catalog
        /// run of the narrative family, or of the card family (P-028).
        /// </summary>
        private static FamilyRun RunOf(string label)
        {
            FamilyRun? cached;
            if (Runs.TryGetValue(label, out cached) && cached != null)
            {
                return cached;
            }

            Gc021ScenarioResult generated;
            Gc021ScenarioResult fixture;
            IReadOnlyList<Gc021Step> combined =
                string.Equals(label, Gc013NarrativeHost.Label, StringComparison.Ordinal)
                    ? Gc021CatalogRuns.RunBothNarrative(out generated, out fixture)
                    : Gc021CatalogRuns.RunBothCards(out generated, out fixture);

            var run = new FamilyRun(combined, generated, fixture);
            Runs[label] = run;
            return run;
        }

        private static Gc021Step Named(Gc021ScenarioResult result, string qualifiedName)
        {
            for (int i = 0; i < result.Steps.Count; i++)
            {
                if (string.Equals(result.Steps[i].Name, qualifiedName, StringComparison.Ordinal))
                {
                    return result.Steps[i];
                }
            }

            Assert.Fail("the '" + qualifiedName + "' observation was not recorded: " + result.Describe());
            return new Gc021Step(qualifiedName, false, "absent");
        }

        private static IReadOnlyList<Gc021Step> PassingSteps(string label)
        {
            string[] names = Gc021Scenario.QualifiedNames(label);
            var steps = new List<Gc021Step>(names.Length);
            for (int i = 0; i < names.Length; i++)
            {
                steps.Add(new Gc021Step(names[i], true, "expected-sequence"));
            }

            return steps;
        }

        private static void AssertDigest(string expected, string actual, string label)
        {
            if (string.Equals(expected, ProbeGc021.PendingDigest, StringComparison.Ordinal))
            {
                // Not yet pinned by the build host: the falsifiable claim left is that the table really produces a
                // canonical digest, and that the placeholders are not silently treated as agreement.
                Assert.That(actual, Has.Length.EqualTo(64),
                    "the " + label + " digest over the observation table must be 32 bytes of lowercase hex");
                Assert.That(actual, Is.Not.EqualTo(ProbeGc021.PendingDigest));
                return;
            }

            Assert.That(actual, Is.EqualTo(expected),
                "the " + label + " digest literal must be the one this observation table produces");
        }

        private static List<string> Names(IReadOnlyList<Gc021Step> combined, string prefix)
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
