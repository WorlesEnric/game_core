// GameCore.CatalogCoverage.Tests — the EditMode half of GC-025's catalog coverage.
//
// One `[Test]` per named observation of `CatalogCoverageScenario`, plus a table test that recomputes the digest from
// the observation-name table, so a renamed, reordered, added or dropped observation fails this suite instead of
// silently shrinking the gate. The sequence is shared with the `-probeCatalogCoverage` player mode, so the same
// observations execute in the Editor and in a stripped headless IL2CPP player (TEST-018); the player run is the one
// that makes the stripping clauses meaningful, because a registration the linker dropped fails the generated
// coverage companion there and not in the Editor.
//
// Every test here is `NotRun (pending orchestrator build host)`: this authoring host has no Unity and no compiler.
#nullable enable
using System.Collections.Generic;
using GameCore.Rules.Narrative;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.CatalogCoverage.Tests
{
    /// <summary>The GC-025 catalog coverage observations, one test each.</summary>
    [TestFixture]
    public sealed class CatalogCoverageIntegrationTests
    {
        /// <summary>Whole-sequence timeout of the one-time set-up, in milliseconds (real worlds are built and driven).</summary>
        private const int RunTimeout = 1800000;

        /// <summary>Per-assertion timeout, in milliseconds. The Editor has an intermittent pre-dispatch hang.</summary>
        private const int AssertTimeout = 60000;

        /// <summary>
        /// Digest of the coverage observation table: the canonical `catalogCoverage/&lt;observation&gt;` `name=pass`
        /// lines over <see cref="CatalogCoverageScenario.ObservationNames"/>, LF separated, no trailing newline,
        /// SHA-256 hex — the same digest function every other family result uses. It is computed from the
        /// observation-name table, not read from a run, so a renamed, reordered, added or dropped observation changes
        /// this literal. NotRun (pending orchestrator build host).
        /// </summary>
        private const string CoverageDigest =
            "77bc74196ec96b076bcc63b007cc0f57f5322121bad69f36c0e280d0576fbbb2";

        private CatalogCoverageResult run = null!;

        /// <summary>
        /// The whole sequence once. Real traversal course worlds are created, driven, stopped and restarted and the
        /// linked fixtures are exercised; every later test is an assertion over this run, so the suite never re-runs
        /// a world.
        /// </summary>
        [OneTimeSetUp]
        [Timeout(RunTimeout)]
        public void RunTheCoverageSequence()
        {
            run = CatalogCoverageScenario.Run();
        }

        private string DetailOf(string observation)
        {
            string qualified = CatalogCoverageScenario.Label + "/" + observation;
            for (int i = 0; i < run.Steps.Count; i++)
            {
                if (string.Equals(run.Steps[i].Name, qualified, System.StringComparison.Ordinal))
                {
                    return run.Steps[i].Passed ? string.Empty : run.Steps[i].Detail;
                }
            }

            return "the sequence recorded no observation named " + qualified;
        }

        /// <summary>Every observation in the frozen table was recorded, exactly once, and passed.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void EveryObservationOfTheFrozenTableWasRecordedAndPassed()
        {
            Assert.That(run.Steps.Count, Is.EqualTo(CatalogCoverageScenario.ObservationNames.Length));
            for (int i = 0; i < CatalogCoverageScenario.ObservationNames.Length; i++)
            {
                Assert.That(
                    run.Steps[i].Name,
                    Is.EqualTo(CatalogCoverageScenario.Label + "/" + CatalogCoverageScenario.ObservationNames[i]));
                Assert.That(run.Steps[i].Passed, Is.True, run.Steps[i].Detail);
            }
        }

        /// <summary>
        /// The digest is a function of the observation-name table alone, so the literal this suite freezes and the
        /// player mode's literal are the same value and cannot drift apart (P-008).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheDigestIsExactlyTheFrozenLiteral()
        {
            IReadOnlyList<string> names = CatalogCoverageScenario.QualifiedNames();
            var lines = new List<string>(names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                lines.Add(names[i] + "=pass");
            }

            Assert.That(NarrativeDigest.OfLines(lines), Is.EqualTo(CoverageDigest));
            Assert.That(run.Digest, Is.EqualTo(CoverageDigest));
            Assert.That(run.AllPassed, Is.True, run.Describe());
        }

        /// <summary>The committed reachability manifest matches the live generated catalogs of this build.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheReachabilityManifestMatchesTheLiveCatalogs()
        {
            Assert.That(DetailOf("catalog-reachability-manifest"), Is.Empty);
            Assert.That(CatalogReachability.Registrations.Length + CatalogReachability.Serializers.Length, Is.GreaterThan(0));
        }

        /// <summary>Every mandatory root of every catalog resolves by its derived key in this build.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void EveryManifestRootResolvesByItsDerivedKey()
        {
            Assert.That(DetailOf("catalog-coverage-registration-lookup"), Is.Empty);
        }

        /// <summary>The closed-generic roots of the probe catalog execute through the generated root method.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheClosedGenericRootsExecute()
        {
            Assert.That(DetailOf("catalog-coverage-closed-generic-roots"), Is.Empty);
            Assert.That(DetailOf("catalog-coverage-probe-catalog"), Is.Empty);
        }

        /// <summary>The generated traversal catalog fingerprints identically to its hand-written counterpart.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheGeneratedTraversalCatalogEqualsTheHandWrittenTable()
        {
            Assert.That(DetailOf("catalog-coverage-traversal-generated-catalog"), Is.Empty);
            Assert.That(DetailOf("catalog-coverage-traversal-catalog"), Is.Empty);
        }

        /// <summary>The linked-but-inactive plugins mount late, by key, in the built player.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheInactivePluginsMountLateByKey()
        {
            Assert.That(DetailOf("catalog-coverage-late-mount-inactive-plugin"), Is.Empty);
        }

        /// <summary>
        /// The editor-baked and runtime-recipe materializations of the traversal course agree on definition, base
        /// layout, descriptor tag, applier registration, recipe-catalog fingerprint, run digest and the canonical
        /// numbers of the run (TEST-020).
        /// </summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheBakedAndRuntimeRecipesAreEquivalent()
        {
            Assert.That(DetailOf("catalog-coverage-bake-runtime-parity"), Is.Empty);
        }

        /// <summary>A recipe the catalog does not declare, and a declared recipe at another revision, are refused.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void UnknownAndStaleRecipesAreRefused()
        {
            Assert.That(DetailOf("catalog-coverage-unknown-recipe-refused"), Is.Empty);
        }

        /// <summary>A stopped world host is unregistered and a fresh one is a new session (P-035).</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheWorldHostStopsAndRestarts()
        {
            Assert.That(DetailOf("catalog-coverage-world-stop-restart"), Is.Empty);
        }

        /// <summary>The headless run matches the pure-rule canonical fixtures and the frozen traversal digest.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheHeadlessRunMatchesThePureRuleFixtures()
        {
            Assert.That(DetailOf("catalog-coverage-headless-canonical"), Is.Empty);
        }

        /// <summary>The player mode's literal and this suite's literal are the same value.</summary>
        [Test]
        [Timeout(AssertTimeout)]
        public void TheProbeModeFreezesTheSameLiteral()
        {
            Assert.That(ProbeCatalogCoverage.ExpectedDigest, Is.EqualTo(CoverageDigest));
            Assert.That(ProbeCatalogCoverage.ExpectedObservations, Is.EqualTo(CatalogCoverageScenario.ObservationNames.Length));
        }
    }
}
