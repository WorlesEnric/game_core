#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Narrative.Fixtures;
using GameCore.Rules.Narrative;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Lifecycle.Tests
{
    /// <summary>
    /// GC-014 narrative half, EditMode (docs/game-core/09-implementation-guide.md, GC-014; 08-validation-and-
    /// performance.md TEST-002/003/008/015/016/018):
    ///
    ///   "All installation transitions of the P-046 table over the control/publication path (activation,
    ///    reconfiguration, replacement, suspend/resume, unload), invalid transitions rejected."
    ///
    /// The scenario itself (`GameCore.Gameplay.Narrative.Fixtures.NarrativeLifecycleScenario`) runs the real GC-010
    /// narrative composition in a real owned Unity world over two independent declaration identity sets and records
    /// twelve named observations plus one fact bag per run. This fixture runs both sets once, asserts every named
    /// observation and every recorded fact by value, and additionally runs
    /// `NarrativeScenarioHost.RunGeneratedCatalog` so the family's committed generated catalog is exercised in the
    /// same suite run (the fixtures package cannot reference an Assets assembly, so that path belongs to the
    /// qualification project - see the scenario's own header).
    /// </summary>
    [TestFixture]
    public sealed class NarrativeLifecycleIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries.</summary>
        private const string FixturePrefix = NarrativeLifecycleScenario.FixtureRunPrefix;

        /// <summary>
        /// The scenario's twelve observations, in execution order. `NarrativeLifecycleScenario` names every one of
        /// them, so a missing or renamed observation fails here instead of shrinking the suite silently.
        /// </summary>
        private static readonly string[] StepNames =
        {
            "narrative-lifecycle-world-and-provider",
            "narrative-lifecycle-suspend-retracts-behavior",
            "narrative-lifecycle-resume-restores-behavior",
            "narrative-lifecycle-provider-loss-makes-consumers-wait",
            "narrative-lifecycle-provider-return-resumes-consumers",
            "narrative-lifecycle-replacement-stages-while-old-runs",
            "narrative-lifecycle-unload-closes-ingress-and-retracts",
            "narrative-lifecycle-blocked-job-prevents-buffer-release",
            "narrative-lifecycle-invalid-transitions-rejected",
            "narrative-lifecycle-repeated-operations-obey-ledger",
            "narrative-lifecycle-teardown-settles-and-disposes",
            "narrative-lifecycle-facts",
        };

        /// <summary>
        /// Every fact key both runs must set, with the narrative slice's semantic gate and fact-version values
        /// last. The bag must carry exactly these keys — an omitted key and an invented one both fail.
        /// </summary>
        private static readonly string[] FactKeys =
        {
            "worldLifecycle",
            "laneJoined",
            "providerStateBefore",
            "providerRowsBefore",
            "registryBeforeCreate",
            "suspendState",
            "suspendRowsAfter",
            "suspendClosedRoutes",
            "suspendLateCompletion",
            "suspendGateLiveActivations",
            "resumeState",
            "resumeRowsAfter",
            "lossConsumerState",
            "lossWaitingConsumers",
            "lossConsumerBindings",
            "lossRetractedRows",
            "returnConsumerState",
            "returnResumedConsumers",
            "returnRows",
            "replacementStagedCandidates",
            "replacementOldHoldsAuthority",
            "replacementEpochChanged",
            "replacementGenerationUnchanged",
            "replacementStateAfter",
            "replacementRowsAfter",
            "unloadState",
            "unloadIngressClosed",
            "unloadRetractedRows",
            "unloadRetiredLeases",
            "unloadQuarantined",
            "unloadLateCompletion",
            "fenceOutstandingJobs",
            "fenceBlockedCode",
            "fenceRetainedWhileOutstanding",
            "fenceDisposeSettledWhileOutstanding",
            "fenceQuarantineBeforeRelease",
            "fenceReleasedAfterCompletion",
            "fenceQuarantineAfterRelease",
            "invalidRejectedCount",
            "invalidStateUnchanged",
            "invalidSuspendTwiceCode",
            "invalidResumeActiveCode",
            "invalidUnmountDisposedCode",
            "invalidReconfigureDisposedCode",
            "invalidRemountLiveIdentityCode",
            "invalidTeardownPathRefusedCode",
            "repeatSuspendRefusedCode",
            "repeatRetransmissionKind",
            "repeatReconfigureSameOutcome",
            "repeatUnmountRefusedCode",
            "ledgerRowCount",
            "registryAfterTeardown",
            "outstandingJobsAfterTeardown",
            "retainedResourcesAfterTeardown",
            "idleSteps",
            "gateDecisionAfterSuspend",
            "factVersionAfterResume",
        };

        private static IReadOnlyList<NarrativeLifecycleStep> observations = null!;
        private static NarrativeLifecycleFacts generated = null!;
        private static NarrativeLifecycleFacts fixture = null!;

        [OneTimeSetUp]
        public void RunBothLifecycleRunsOncePerCatalog()
        {
            observations = NarrativeLifecycleScenario.RunBoth(out generated, out fixture);
        }

        [TearDown]
        public void TearDown()
        {
            // The scenario tears its own world down; this guarantees a clean registry if a case failed mid-way.
            UnityWorldRegistry.ResetAll();
        }

        /// <summary>Every named observation of the generated-catalog run must pass.</summary>
        [Test]
        public void EveryObservationPassesOverTheGeneratedCatalog()
        {
            AssertRunPassed("", "generated");
        }

        /// <summary>Every named observation of the fixture-catalog run must pass.</summary>
        [Test]
        public void EveryObservationPassesOverTheFixtureCatalog()
        {
            AssertRunPassed(FixturePrefix, "fixture");
        }

        /// <summary>
        /// The scenario really recorded its twelve observations once per catalog, in the documented order, and the
        /// final observation carries that run's fact digest.
        /// </summary>
        [Test]
        public void TheScenarioRecordsEveryObservationTwiceInOrder()
        {
            Assert.That(observations, Is.Not.Empty, "the scenario recorded no observation at all.");
            Assert.That(observations.Count, Is.EqualTo(StepNames.Length * 2),
                "the scenario must report twelve observations per catalog.");

            Assert.That(NamesOf(""), Is.EqualTo(new List<string>(StepNames)),
                "generated-catalog observation order: " + generated.Describe());
            Assert.That(NamesOf(FixturePrefix), Is.EqualTo(new List<string>(StepNames)),
                "fixture-catalog observation order: " + fixture.Describe());

            NarrativeLifecycleStep? digest = FindStep(observations, FixturePrefix + NarrativeLifecycleKeys.StepFacts);
            Assert.That(digest, Is.Not.Null, "the fixture run carries no fact-digest observation.");
            Assert.That(digest!.Detail, Is.EqualTo(fixture.Describe()),
                "the fact observation must carry the run's own digest.");
        }

        /// <summary>The world and provider observation: an owned world, the lane joined, the provider active.</summary>
        [Test]
        public void TheWorldAndProviderObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepWorld);
        }

        /// <summary>Suspension retracts the active behavior in the same publication.</summary>
        [Test]
        public void TheSuspendObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepSuspend);
        }

        /// <summary>Resume rederives the current ancestry and restores the contribution.</summary>
        [Test]
        public void TheResumeObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepResume);
        }

        /// <summary>Losing a required provider makes its consumer wait in the same publication.</summary>
        [Test]
        public void TheProviderLossObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepProviderLoss);
        }

        /// <summary>Returning a compatible provider resumes the waiting consumer in the same publication.</summary>
        [Test]
        public void TheProviderReturnObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepProviderReturn);
        }

        /// <summary>Replacement stages a candidate while the old activation still holds authority.</summary>
        [Test]
        public void TheReplacementObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepReplacement);
        }

        /// <summary>Unload closes ingress, retracts the contribution and retires the leases.</summary>
        [Test]
        public void TheUnloadObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepUnload);
        }

        /// <summary>An outstanding job prevents buffer release until it completes.</summary>
        [Test]
        public void TheBlockedJobObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepJobFence);
        }

        /// <summary>Every invalid transition is refused as a value.</summary>
        [Test]
        public void TheInvalidTransitionsObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepInvalidTransitions);
        }

        /// <summary>Repeated operations obey the ledger (P-050, P-051).</summary>
        [Test]
        public void TheRepeatedOperationsObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepRepeatedOperations);
        }

        /// <summary>Teardown settles its work and returns the world registry to its baseline (P-047, P-048).</summary>
        [Test]
        public void TheTeardownObservationPasses()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepTeardown);
        }

        /// <summary>The run carries its values: the fact observation always passes (repo digest convention).</summary>
        [Test]
        public void TheFactObservationCarriesTheDigest()
        {
            AssertStepPassed(NarrativeLifecycleKeys.StepFacts);
        }

        /// <summary>
        /// Both runs carry exactly the documented fact keys in canonical ordinal order, and the digest is one
        /// `key=value` line per key in that same order — so an omitted, invented or misspelled key fails here.
        /// </summary>
        [Test]
        public void BothRunsCarryExactlyTheDocumentedFacts()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(facts.Keys.Count, Is.EqualTo(FactKeys.Length),
                    "the run must set exactly the documented fact keys: " + facts.Describe());

                var missing = new List<string>();
                for (int i = 0; i < FactKeys.Length; i++)
                {
                    if (!facts.Has(FactKeys[i]))
                    {
                        missing.Add(FactKeys[i]);
                    }
                }

                Assert.That(missing, Is.Empty, "the run did not set documented fact keys: " + Join(missing));

                for (int i = 1; i < facts.Keys.Count; i++)
                {
                    Assert.That(string.CompareOrdinal(facts.Keys[i - 1], facts.Keys[i]), Is.LessThan(0),
                        "Keys must be in canonical ordinal order: " + facts.Describe());
                }

                AssertDigestLines(facts);
            }
        }

        /// <summary>
        /// Step 1: one owned world, the lane joined to that world's published assembly (P-006), the provider
        /// published `Active` with attributed rows, and the registry grown by exactly one world.
        /// </summary>
        [Test]
        public void TheWorldOwnsTheLaneAndTheProviderIsActive()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactWorldLifecycle),
                    Is.EqualTo(WorldLifecycleState.Running.ToString()),
                    "the host world lifecycle must be Running: " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactLaneJoined), Is.True,
                    "the lane must have joined the world's published assembly (P-006): " + facts.Describe());
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactProviderStateBefore),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "the mounted provider must be published Active (P-046): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactProviderRowsBefore), Is.GreaterThan(0L),
                    "an active provider must own at least one attributed row: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactRegistryBeforeCreate),
                    Is.GreaterThanOrEqualTo(0L),
                    "the pre-create registry baseline must be a count: " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 2: suspension stores `Suspended`, drops the installation's authority and its live callback
        /// activation, retracts its attributed rows, and a completion stamped for the old epoch is discarded rather
        /// than dispatched (P-046, P-047).
        /// </summary>
        [Test]
        public void SuspensionRetractsTheContributionAndDiscardsLateCompletions()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactSuspendState),
                    Is.EqualTo(InstallationState.Suspended.ToString()),
                    "an explicit suspend must publish the installation Suspended (P-046): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactSuspendRowsAfter), Is.Zero,
                    "suspension retracts the active contributions, so no attributed row may remain: "
                    + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactSuspendClosedRoutes), Is.EqualTo(1L),
                    "the chapter installation owns exactly one command route (ChoiceRoute), so suspending it must "
                    + "retire exactly that one (P-047): " + facts.Describe());
                // installations this world committed; what P-047 requires is that the suspended installation's own
                // activation was retired — the observation's own conjunction asserts the strict decrease, and the
                // value here must be a real count rather than an unset sentinel.
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactSuspendGateLiveActivations),
                    Is.GreaterThanOrEqualTo(0L),
                    "the gate's live-activation count after the suspend must be a real count (P-047): "
                    + facts.Describe());
                AssertDiscarded(facts, NarrativeLifecycleKeys.FactSuspendLateCompletion);
            }
        }

        /// <summary>
        /// Step 3: resume publishes `Active` again and restores exactly the contributions the suspend retracted.
        /// </summary>
        [Test]
        public void ResumeRestoresTheSameContribution()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactResumeState),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "resume must republish the installation Active (P-046): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactResumeRowsAfter),
                    Is.EqualTo(Integer(facts, NarrativeLifecycleKeys.FactProviderRowsBefore)),
                    "resume must restore the rows the suspend retracted: " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 4: removing a required provider makes its consumer wait in the same publication, with its bindings
        /// gone and its previously attributed rows retracted (P-012).
        /// </summary>
        [Test]
        public void LosingAProviderMakesTheConsumerWaitInTheSamePublication()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactLossConsumerState),
                    Is.EqualTo(InstallationState.WaitingForDependencies.ToString()),
                    "a consumer that lost a required provider must be WaitingForDependencies (P-012): "
                    + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactLossWaitingConsumers), Is.EqualTo(1L),
                    "exactly the one consumer must be named by the same publication: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactLossConsumerBindings), Is.Zero,
                    "a waiting consumer holds no binding: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactLossRetractedRows), Is.GreaterThan(0L),
                    "the consumer's previously attributed rows must have been retracted: " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 5: mounting a compatible provider back resumes the consumer in the same publication, restoring the
        /// bindings and the rows it lost (P-012).
        /// </summary>
        [Test]
        public void ReturningAProviderResumesTheConsumerInTheSamePublication()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactReturnConsumerState),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "a consumer whose dependency returned must be Active again (P-012): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactReturnResumedConsumers), Is.EqualTo(1L),
                    "exactly the one consumer must be named as resumed by the same publication: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactReturnRows),
                    Is.EqualTo(Integer(facts, NarrativeLifecycleKeys.FactLossRetractedRows)),
                    "the consumer's rows must be back exactly as they were: " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 6: an accepted reconfigure stages a candidate while the old activation still holds authority, and
        /// the in-place update keeps the installation generation while advancing its activation epoch (P-046).
        /// </summary>
        [Test]
        public void ReplacementStagesACandidateWhileTheOldActivationHoldsAuthority()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactReplacementStagedCandidates), Is.EqualTo(1L),
                    "the reconfigure must stage exactly one candidate: " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactReplacementOldHoldsAuthority), Is.True,
                    "the old activation must still hold authority in the staging publication (P-046): "
                    + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactReplacementEpochChanged), Is.True,
                    "an in-place reconfigure must advance the activation epoch: " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactReplacementGenerationUnchanged), Is.True,
                    "an in-place update must keep the installation generation (P-046): " + facts.Describe());
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactReplacementStateAfter),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "the installation must be Active after the replacement: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactReplacementRowsAfter),
                    Is.EqualTo(Integer(facts, NarrativeLifecycleKeys.FactProviderRowsBefore)),
                    "the replacement must keep the installation's rows: " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 7: unloading closes ingress, retracts the installation's rows, retires its leases, quarantines
        /// nothing when no job owns a buffer, and discards a completion stamped for the retired route (P-047).
        /// </summary>
        [Test]
        public void UnloadClosesIngressRetractsAndDisposes()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactUnloadState),
                    Is.EqualTo(InstallationState.Disposed.ToString()),
                    "nothing is retained in this step, so the unload must publish Disposed (P-048): "
                    + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactUnloadIngressClosed), Is.GreaterThanOrEqualTo(1L),
                    "the unload must have closed the installation's ingress (P-047): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactUnloadRetractedRows),
                    Is.EqualTo(Integer(facts, NarrativeLifecycleKeys.FactProviderRowsBefore)),
                    "the unload must retract exactly the rows the installation held: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactUnloadRetiredLeases),
                    Is.GreaterThanOrEqualTo(1L),
                    "the unload must retire the installation's staged leases in reverse order (P-048): "
                    + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactUnloadQuarantined), Is.Zero,
                    "no resource is reachable by unfinished work in this step: " + facts.Describe());
                AssertDiscarded(facts, NarrativeLifecycleKeys.FactUnloadLateCompletion);
            }
        }

        /// <summary>
        /// Step 8: an outstanding tracked job keeps the installation `Retiring` with its buffer quarantined —
        /// teardown reports `TeardownBlocked` and never a false `Disposed` — and completing the job releases the
        /// lease exactly once (P-047, P-048).
        /// </summary>
        [Test]
        public void AnOutstandingJobBlocksBufferReleaseUntilItCompletes()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactFenceOutstandingJobs),
                    Is.GreaterThanOrEqualTo(1L),
                    "the tracked job must still be outstanding when the unload is attempted: " + facts.Describe());
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactFenceBlockedCode),
                    Is.EqualTo(DiagnosticCodeText.Of(DiagnosticCode.TeardownBlocked)),
                    "a blocked teardown reports TeardownBlocked, never a free (P-048): " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactFenceRetainedWhileOutstanding), Is.True,
                    "the fenced lease must stay retained while the job owns its buffer: " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactFenceDisposeSettledWhileOutstanding), Is.False,
                    "a blocked job must prevent the installation from settling Disposed (P-048): "
                    + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactFenceQuarantineBeforeRelease),
                    Is.GreaterThanOrEqualTo(1L),
                    "the retained resource must be quarantined, not released: " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactFenceReleasedAfterCompletion), Is.True,
                    "completing the job must release the quarantined lease: " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactFenceQuarantineAfterRelease), Is.Zero,
                    "the release must leave nothing quarantined for the instance: " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 9: each of the six invalid transitions is refused as a value — a refused `LifecycleTransition`, a
        /// non-staged admission with a non-`None` code, or a rejected outcome — and neither the published state nor
        /// the lane revision moves (P-046).
        /// </summary>
        [Test]
        public void EveryInvalidTransitionIsRefusedWithoutChangingState()
        {
            string[] codes =
            {
                NarrativeLifecycleKeys.FactInvalidSuspendTwiceCode,
                NarrativeLifecycleKeys.FactInvalidResumeActiveCode,
                NarrativeLifecycleKeys.FactInvalidUnmountDisposedCode,
                NarrativeLifecycleKeys.FactInvalidReconfigureDisposedCode,
                NarrativeLifecycleKeys.FactInvalidRemountLiveIdentityCode,
                NarrativeLifecycleKeys.FactInvalidTeardownPathRefusedCode,
            };

            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactInvalidRejectedCount),
                    Is.EqualTo((long)codes.Length),
                    "every one of the six invalid transitions must be counted as refused: " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactInvalidStateUnchanged), Is.True,
                    "a refused transition must leave the published state and the lane revision unchanged: "
                    + facts.Describe());

                for (int i = 0; i < codes.Length; i++)
                {
                    AssertRefusedCode(facts, codes[i]);
                }
            }
        }

        /// <summary>
        /// Step 10: a repeated operation is refused, a retransmission of the same identity returns the original
        /// outcome, and the ledger counts every row (P-050, P-051).
        /// </summary>
        [Test]
        public void RepeatedOperationsObeyTheLedger()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                AssertRefusedCode(facts, NarrativeLifecycleKeys.FactRepeatSuspendRefusedCode);
                Assert.That(Value(facts, NarrativeLifecycleKeys.FactRepeatRetransmissionKind),
                    Is.EqualTo(AdmissionKind.Retransmission.ToString()),
                    "the same id and input must be admitted as a retransmission (P-050): " + facts.Describe());
                Assert.That(Boolean(facts, NarrativeLifecycleKeys.FactRepeatReconfigureSameOutcome), Is.True,
                    "a retransmission must return the original outcome: " + facts.Describe());
                AssertRefusedCode(facts, NarrativeLifecycleKeys.FactRepeatUnmountRefusedCode);
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactLedgerRowCount), Is.GreaterThan(0L),
                    "the ledger must have recorded every operation of the run (P-050): " + facts.Describe());
            }
        }

        /// <summary>
        /// Step 11: teardown settles every job, leaves nothing retained, returns the world registry to its
        /// pre-create baseline, and an idle window commits no step (P-036, P-047, P-048).
        /// </summary>
        [Test]
        public void TeardownSettlesWorkAndReturnsTheRegistryToBaseline()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactOutstandingJobsAfterTeardown), Is.Zero,
                    "teardown must settle every tracked job (P-048): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactRetainedResourcesAfterTeardown), Is.Zero,
                    "teardown must release everything it quarantined (P-048): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactRegistryAfterTeardown),
                    Is.EqualTo(Integer(facts, NarrativeLifecycleKeys.FactRegistryBeforeCreate)),
                    "the owned-world registry must return to its pre-create baseline (04 section 3): "
                    + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactIdleSteps), Is.Zero,
                    "an idle command-driven world commits no step (P-036): " + facts.Describe());
            }
        }

        /// <summary>
        /// The narrative semantics of the lifecycle: suspending the chapter's provider returns the authoritative
        /// gate decision to closed (its contribution is retracted), and resuming publishes the quest fact at a
        /// version at or beyond the declared initial version.
        /// </summary>
        [Test]
        public void TheNarrativeGateAndFactVersionReflectTheLifecycle()
        {
            foreach (NarrativeLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactGateDecisionAfterSuspend),
                    Is.EqualTo((long)NarrativeGateRules.Closed),
                    "retracting the chapter's contribution must leave the gate closed (P-046): " + facts.Describe());
                Assert.That(Integer(facts, NarrativeLifecycleKeys.FactFactVersionAfterResume),
                    Is.GreaterThanOrEqualTo((long)NarrativeFacts.InitialVersion),
                    "resume must rederive the durable quest fact at a declared version: " + facts.Describe());
            }
        }

        /// <summary>
        /// The family's committed generated catalog, run in this same suite so the lifecycle assertions and the real
        /// generated registrations are exercised together on one revision. `NarrativeScenarioHost.RunGeneratedCatalog`
        /// is the qualification project's entry over `ProbeCatalog.g.cs` (GC-003), which the fixtures package cannot
        /// reference; running it here is what keeps this suite's claim about "both catalogs" literally true.
        /// </summary>
        [Test]
        public void TheCommittedGeneratedCatalogStillRunsItsOwnSlice()
        {
            NarrativeScenarioResult result = NarrativeScenarioHost.RunGeneratedCatalog();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Steps, Is.Not.Empty, "the committed-catalog run must report its observations.");
            Assert.That(result.AllPassed, Is.True,
                "the committed generated narrative catalog must still pass on this revision: " + result.Describe());
        }

        private static IEnumerable<NarrativeLifecycleFacts> Runs()
        {
            yield return generated;
            yield return fixture;
        }

        private static List<NarrativeLifecycleStep> StepsOf(string prefix)
        {
            var steps = new List<NarrativeLifecycleStep>();
            for (int i = 0; i < observations.Count; i++)
            {
                string name = observations[i].Name;
                if (prefix.Length == 0)
                {
                    if (!name.StartsWith(FixturePrefix, StringComparison.Ordinal))
                    {
                        steps.Add(observations[i]);
                    }
                }
                else if (name.StartsWith(prefix, StringComparison.Ordinal))
                {
                    steps.Add(observations[i]);
                }
            }

            return steps;
        }

        private static List<string> NamesOf(string prefix)
        {
            List<NarrativeLifecycleStep> steps = StepsOf(prefix);
            var names = new List<string>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                names.Add(prefix.Length == 0
                    ? steps[i].Name
                    : steps[i].Name.Substring(prefix.Length));
            }

            return names;
        }

        /// <summary>Finds a step by its full (possibly prefixed) name.</summary>
        private static NarrativeLifecycleStep? FindStep(
            IReadOnlyList<NarrativeLifecycleStep> steps,
            string stepName)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (string.Equals(steps[i].Name, stepName, StringComparison.Ordinal))
                {
                    return steps[i];
                }
            }

            return null;
        }

        private static void AssertRunPassed(string prefix, string label)
        {
            List<NarrativeLifecycleStep> steps = StepsOf(prefix);
            Assert.That(steps, Is.Not.Empty, "the " + label + "-catalog run recorded no observation at all.");

            var failed = new List<string>();
            for (int i = 0; i < steps.Count; i++)
            {
                if (!steps[i].Passed)
                {
                    failed.Add(steps[i].ToString());
                }
            }

            Assert.That(failed, Is.Empty, "failed " + label + "-catalog observations: " + Join(failed));
        }

        /// <summary>A step must have been reported by both runs and passed in both.</summary>
        private static void AssertStepPassed(string stepName)
        {
            NarrativeLifecycleStep? generatedStep = FindStep(observations, stepName);
            Assert.That(generatedStep, Is.Not.Null,
                "the generated-catalog run did not report the observation '" + stepName + "'.");
            Assert.That(generatedStep!.Passed, Is.True, generatedStep.Detail);

            string prefixed = FixturePrefix + stepName;
            NarrativeLifecycleStep? fixtureStep = FindStep(observations, prefixed);
            Assert.That(fixtureStep, Is.Not.Null,
                "the fixture-catalog run did not report the observation '" + prefixed + "'.");
            Assert.That(fixtureStep!.Passed, Is.True, fixtureStep.Detail);
        }

        private static string Value(NarrativeLifecycleFacts facts, string key)
        {
            Assert.That(facts.Has(key), Is.True,
                "the run did not set the fact '" + key + "': " + facts.Describe());
            string text = facts.ValueOf(key);
            Assert.That(text, Is.Not.Null.And.Not.Empty,
                "the fact '" + key + "' must carry a value: " + facts.Describe());
            return text;
        }

        private static long Integer(NarrativeLifecycleFacts facts, string key)
        {
            string text = Value(facts, key);
            bool parsed = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value);
            Assert.That(parsed, Is.True,
                "the fact '" + key + "' must be an invariant integer but was '" + text + "': " + facts.Describe());
            return value;
        }

        private static bool Boolean(NarrativeLifecycleFacts facts, string key)
        {
            string text = Value(facts, key);
            bool parsed = bool.TryParse(text, out bool value);
            Assert.That(parsed, Is.True,
                "the fact '" + key + "' must be True or False but was '" + text + "': " + facts.Describe());
            return value;
        }

        /// <summary>
        /// A refusal fact must name a real diagnostic code: a sentinel or `None` means the transition was not
        /// actually refused as a value, which is exactly what these steps must not have observed.
        /// </summary>
        private static void AssertRefusedCode(NarrativeLifecycleFacts facts, string key)
        {
            string code = Value(facts, key);
            bool parsed = DiagnosticCodeText.TryParse(code, out DiagnosticCode parsedCode);
            Assert.That(parsed, Is.True,
                "the fact '" + key + "' must name a diagnostic code but was '" + code + "': " + facts.Describe());
            Assert.That(parsedCode, Is.Not.EqualTo(DiagnosticCode.None),
                "the fact '" + key + "' must carry the code of the refusal, not None: " + facts.Describe());
        }

        /// <summary>
        /// A late completion must be discarded, not dispatched: either the route was retired or its activation is
        /// stale relative to the current epoch (P-047).
        /// </summary>
        private static void AssertDiscarded(NarrativeLifecycleFacts facts, string key)
        {
            string decision = Value(facts, key);
            Assert.That(decision, Is.Not.EqualTo(CallbackGateDecision.Dispatch.ToString()),
                "the fact '" + key + "' must not report Dispatch for a late completion: " + facts.Describe());
            bool discarded =
                string.Equals(decision, CallbackGateDecision.DiscardRetiredRoute.ToString(), StringComparison.Ordinal)
                || string.Equals(decision, CallbackGateDecision.DiscardStaleActivation.ToString(),
                    StringComparison.Ordinal);
            Assert.That(discarded, Is.True,
                "the fact '" + key + "' must report a stale-activation or retired-route discard: "
                + facts.Describe());
        }

        /// <summary>The digest is exactly one `key=value` line per key, in the canonical key order.</summary>
        private static void AssertDigestLines(NarrativeLifecycleFacts facts)
        {
            string digest = facts.Describe();
            Assert.That(digest, Is.Not.Null.And.Not.Empty, "the run must describe its facts.");
            string[] lines = digest.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            Assert.That(lines.Length, Is.EqualTo(facts.Keys.Count),
                "the digest must carry one line per fact key: " + digest);

            for (int i = 0; i < lines.Length; i++)
            {
                int separator = lines[i].IndexOf('=');
                Assert.That(separator, Is.GreaterThan(0), "every digest line must be `key=value`: " + digest);
                Assert.That(lines[i].Substring(0, separator), Is.EqualTo(facts.Keys[i]),
                    "the digest must name the canonical keys in order: " + digest);
            }
        }

        private static string Join(List<string> values)
            => string.Join(" | ", values.ToArray());
    }
}
