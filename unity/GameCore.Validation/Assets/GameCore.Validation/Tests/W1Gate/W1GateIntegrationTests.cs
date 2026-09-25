#nullable enable
using System.Collections.Generic;
using GameCore.Unity.Fixtures;
using GameCore.Validation.Generated;
using GameCore.Validation.ProbeHost;
using GameCore.Unity.Runtime;
using NUnit.Framework;

namespace GameCore.W1Gate.Tests
{
    /// <summary>
    /// Wave 1 integration gate, EditMode half (docs/game-core/09-implementation-guide.md, "Wave 1 — Shared seams
    /// and one owned world"):
    ///
    ///   "Integrate catalog/DTOs, control host and actual Unity world driver. Create two worlds, admit one
    ///    operation and execute a guarded fixture stage; prove a thrown postwrite exception stops the next stage
    ///    and publication."
    ///
    /// The scenario itself is shared with the standalone player probe (`-probeW1Gate`) and runs only real modules:
    /// the production immutable catalog (GC-003) over generated registrations, the real `CompositionHost` control
    /// lane (GC-004) and real owned `Unity.Entities.World`s driven by the guarded dispatch groups (GC-005). No
    /// reference seam or test double participates. This fixture asserts on the facts the scenario observed, so a
    /// regression in the integration is reported by value, not only by a boolean.
    ///
    /// Two catalog inputs are exercised: the committed generated catalog (`ProbeCatalog`, real compiler output) and
    /// the fixture's hand-written generated-style table.
    /// </summary>
    [TestFixture]
    public sealed class W1GateIntegrationTests
    {
        private static W1GateScenarioResult generated = null!;
        private static W1GateScenarioResult fixture = null!;

        [OneTimeSetUp]
        public void RunTheGateOncePerCatalog()
        {
            generated = W1GateScenarioHost.RunGeneratedCatalog();
            fixture = W1GateScenarioHost.RunFixtureCatalog();
        }

        [TearDown]
        public void TearDown()
        {
            // The scenario tears its own worlds down; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>Every named observation of the generated-catalog run must pass.</summary>
        [Test]
        public void EveryGateCheckPassesOverTheGeneratedCatalog()
        {
            AssertStepsPassed(generated);
        }

        /// <summary>Every named observation of the fixture-catalog run must pass.</summary>
        [Test]
        public void EveryGateCheckPassesOverTheFixtureCatalog()
        {
            AssertStepsPassed(fixture);
        }

        /// <summary>The gate ran over the committed compiler output, not a substitute (GC-003 ownership).</summary>
        [Test]
        public void TheGeneratedRunUsesTheCommittedGeneratedCatalog()
        {
            Assert.That(
                generated.Facts.CatalogFingerprint,
                Is.EqualTo(ProbeCatalog.CatalogFingerprint),
                "the gate must run over the committed generated catalog's own fingerprint (P-028).");
            Assert.That(generated.Facts.CatalogFactoryCount, Is.GreaterThan(0));
            Assert.That(
                generated.Facts.CatalogAcceptedDeclarations,
                Is.EqualTo(1),
                "the generated catalog must accept the gate's declared plugin");
            Assert.That(
                generated.Facts.CatalogRejectedDeclarations,
                Is.EqualTo(1),
                "a declaration whose factory key the catalog does not register must be refused (P-009)");
            Assert.That(
                generated.Facts.ManifestMissCount,
                Is.GreaterThan(0),
                "an unregistered plugin type must be reported as a miss, never substituted (P-009)");
        }

        /// <summary>Two real owned worlds exist, and the second one commits no step at all.</summary>
        [Test]
        public void TwoOwnedWorldsAreCreatedAndTheSecondStaysIdle()
        {
            W1GateFacts facts = generated.Facts;

            Assert.That(facts.RegistryBeforeCreate, Is.GreaterThanOrEqualTo(0));
            Assert.That(
                facts.RegistryCountAfterCreate,
                Is.EqualTo(facts.RegistryBeforeCreate + 2),
                "the gate creates exactly two owned worlds (P-002, P-004)");
            Assert.That(facts.WorldASession, Is.Not.EqualTo(facts.WorldBSession), "each world has its own session");
            // P-006 has one publication series. World A published its initial assembly as revision/epoch 1 (05 s2)
            // and its lane was seeded from that, so the admitted operation published revision 2 — and the world
            // published assembly epoch 2 as well, which is the equality GC-008 now asserts.
            Assert.That(facts.LaneARevision, Is.EqualTo(2UL), "the admitted operation published revision 2");
            Assert.That(facts.LaneAEpoch, Is.EqualTo(2UL), "revision and epoch name the same publication (P-006)");
            Assert.That(facts.WorldAEpoch, Is.EqualTo(facts.LaneAEpoch),
                "the world publishes the composition epoch its operation reported (P-006)");
            Assert.That(facts.CompositionMatchesWorldEpoch, Is.True, "the composition and published series are one");
            Assert.That(facts.LaneBRevision, Is.EqualTo(1UL),
                "world B's lane admitted nothing and still reports the assembly its world published");
            Assert.That(facts.WorldBSteps, Is.EqualTo(0UL), "an idle command-driven world commits zero steps (P-036)");
            Assert.That(facts.WorldBStepGroupDispatchRuns, Is.EqualTo(0), "no step group dispatch runs when idle");
            Assert.That(facts.WorldBPublishedImages, Is.EqualTo(1), "only the initial assembly is published");
            Assert.That(
                facts.WorldBIngressCount,
                Is.EqualTo(facts.WorldBPumpCount),
                "ingress dispatches on every routed frame of an idle world (04 s3)");
            Assert.That(
                facts.WorldBOutputCount,
                Is.EqualTo(facts.WorldBPumpCount),
                "presentation dispatches on every routed frame of an idle world (04 s3)");
        }

        /// <summary>One admitted operation becomes one committed, guarded step in world A.</summary>
        [Test]
        public void TheAdmittedOperationExecutesOneGuardedStage()
        {
            W1GateFacts facts = generated.Facts;

            Assert.That(facts.OperationOneOutcome, Is.EqualTo("Published"), "the control lane published a step");
            Assert.That(facts.WorldASteps, Is.EqualTo(1UL), "one admitted command is exactly one logical step (P-037)");
            Assert.That(facts.FirstStepAcceptCount, Is.EqualTo(1));
            Assert.That(facts.FirstStepSettleCount, Is.EqualTo(1));
            Assert.That(facts.FirstStepProjectCount, Is.EqualTo(1));
            Assert.That(facts.FirstStepCounterValue, Is.EqualTo(111), "the ordered stages wrote their authoritative values");
            Assert.That(facts.StepOneImagePublished, Is.True, "the committed step image is published (P-044)");
            Assert.That(facts.LastPublishedStep, Is.EqualTo(1UL));
            Assert.That(facts.OutstandingJobsAfterFirstStep, Is.EqualTo(0), "the committed step settled its jobs");
        }

        /// <summary>
        /// The fail-stop proof: the throwing stage wrote, the next stage did not run, the world faulted and the
        /// failed step published nothing (P-031, P-044).
        /// </summary>
        [Test]
        public void AThrownPostwriteExceptionStopsTheNextStageAndPublication()
        {
            W1GateFacts facts = generated.Facts;

            Assert.That(facts.FaultCount, Is.EqualTo(2), "the fault stage ran on both admitted commands");
            Assert.That(
                facts.ProjectCount,
                Is.EqualTo(1),
                "the stage after the throwing stage must not run again after the fault (P-031)");
            Assert.That(facts.CounterValue, Is.EqualTo(222), "the throwing stage wrote before it threw");
            Assert.That(facts.WorldASteps, Is.EqualTo(1UL), "the failed step never advances LogicalStepId (P-044)");
            Assert.That(facts.FaultedStepImagePublished, Is.False, "the failed step publishes no image");
            Assert.That(facts.LastPublishedStep, Is.EqualTo(1UL), "the last published image is the committed step");
            Assert.That(facts.WorldAPublishedImages, Is.EqualTo(2), "only the initial and the committed step images exist");
            Assert.That(
                facts.OutstandingJobsBeforeTeardown,
                Is.GreaterThan(0),
                "the failed step keeps its pending job tracked until safe teardown (P-047)");
            Assert.That(facts.WorldLifecycleAfterFault, Is.EqualTo("Faulted"));
            Assert.That(facts.QuarantinedJobs, Is.GreaterThan(0), "the failed step's job is quarantined (P-047)");
            Assert.That(facts.RetainedHandles, Is.GreaterThan(0), "its handle stays tracked until teardown");
        }

        /// <summary>The lane's result and the world's fault are reported side by side, without a false rollback.</summary>
        [Test]
        public void TheStatusOfTheOperationAndTheWorldFaultAreReportedHonestly()
        {
            W1GateFacts facts = generated.Facts;

            Assert.That(
                facts.OperationTwoOutcome,
                Is.EqualTo("Published"),
                "the composition publication of the second operation did happen");
            Assert.That(
                facts.OperationTwoResultRetained,
                Is.True,
                "the published operation result is not restated as a failure by a later execution fault (P-031)");
            Assert.That(facts.LaneAuditRevisionAfterFault, Is.EqualTo(3UL),
                "the lane still reports what it published: the second operation's composition revision 3");
            Assert.That(facts.CompositionMatchesWorldEpoch, Is.True,
                "the faulted world still agrees with the lane it was joined to (P-006)");
            Assert.That(facts.WorldFaultCount, Is.EqualTo(1), "the world faults exactly once");
            Assert.That(facts.WorldRefusalCount, Is.EqualTo(1), "a faulted world accepts no further work");
            Assert.That(facts.WorldRefusalCode, Is.EqualTo("ApplyFault"));
            Assert.That(
                facts.LaneRowsAfterRefusal,
                Is.EqualTo(2),
                "a refused admission creates no ledger row: the lane holds the two real operations only");
        }

        /// <summary>Pending work is settled before storage is released, and no host survives the gate.</summary>
        [Test]
        public void TeardownSettlesPendingWorkAndLeavesNoHostRegistered()
        {
            W1GateFacts facts = generated.Facts;

            Assert.That(facts.SettledJobs, Is.EqualTo(facts.RetainedHandles));
            Assert.That(facts.OutstandingJobsAfterTeardown, Is.EqualTo(0));
            Assert.That(facts.RetainedResourcesAfterTeardown, Is.EqualTo(0));
            Assert.That(
                facts.RegistryCountAfterTeardown,
                Is.EqualTo(facts.RegistryBeforeCreate),
                "teardown removes both worlds and leaves no static host reference (04 s9)");
        }

        private static void AssertStepsPassed(W1GateScenarioResult result)
        {
            Assert.That(result.Steps, Is.Not.Empty, "the gate must record its observations");

            var failures = new List<string>();
            for (int i = 0; i < result.Steps.Count; i++)
            {
                W1GateStep step = result.Steps[i];
                if (!step.Passed)
                {
                    failures.Add(step.Name + " -> " + step.Detail);
                }
            }

            Assert.That(
                failures,
                Is.Empty,
                "every gate observation must pass; failures: " + string.Join(" | ", failures));
        }
    }
}
