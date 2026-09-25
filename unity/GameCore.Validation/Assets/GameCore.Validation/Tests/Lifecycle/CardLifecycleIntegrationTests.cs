#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using GameCore.Composition;
using GameCore.Contracts;
using GameCore.Gameplay.Cards;
using GameCore.Gameplay.Cards.Fixtures;
using GameCore.Unity.Runtime;
using GameCore.Validation.ProbeHost;
using NUnit.Framework;

namespace GameCore.Lifecycle.Tests
{
    /// <summary>
    /// GC-014 card half, EditMode (docs/game-core/09-implementation-guide.md, GC-014; 08-validation-and-
    /// performance.md TEST-002/003/008/015/016/018):
    ///
    ///   "All installation transitions of the P-046 table over the control/publication path (activation,
    ///    reconfiguration, replacement, suspend/resume, unload), invalid transitions rejected."
    ///
    /// The scenario itself (`GameCore.Gameplay.Cards.Fixtures.CardLifecycleScenario`) runs the real GC-011 card
    /// composition in a real owned Unity world over two independent declaration identity sets and records twelve
    /// named observations plus one fact bag per run. This fixture runs both sets once, asserts every named
    /// observation and every recorded fact by value, and additionally runs `CardsScenarioHost.RunGeneratedCatalog`
    /// so the family's committed generated catalog is exercised in the same suite run (the fixtures package cannot
    /// reference an Assets assembly, so that path belongs to the qualification project - see the scenario's header).
    ///
    /// The asserted facts are exactly the ones the frozen GC-014 contract fixes (P-012, P-046, P-047, P-048): the
    /// installation states, the authority and callback-gate retraction on suspend, the in-publication wait and
    /// resume of a consumer that loses and regains its provider, the staged candidate of an in-place replacement,
    /// the ingress closure and lease retirement of an unload, the job fence that blocks buffer release, the refused
    /// invalid transitions, and the ledger behavior of repeated operations.
    /// </summary>
    [TestFixture]
    public sealed class CardLifecycleIntegrationTests
    {
        /// <summary>Step-name prefix the fixture-catalog run carries.</summary>
        private const string FixturePrefix = CardLifecycleScenario.FixtureRunPrefix;

        /// <summary>
        /// The scenario's twelve observations, in execution order. `CardLifecycleScenario` names every one of
        /// them, so a missing or renamed observation fails here instead of shrinking the suite silently.
        /// </summary>
        private static readonly string[] StepNames =
        {
            "cards-lifecycle-world-and-provider",
            "cards-lifecycle-suspend-retracts-behavior",
            "cards-lifecycle-resume-restores-behavior",
            "cards-lifecycle-provider-loss-makes-consumers-wait",
            "cards-lifecycle-provider-return-resumes-consumers",
            "cards-lifecycle-replacement-stages-while-old-runs",
            "cards-lifecycle-unload-closes-ingress-and-retracts",
            "cards-lifecycle-blocked-job-prevents-buffer-release",
            "cards-lifecycle-invalid-transitions-rejected",
            "cards-lifecycle-repeated-operations-obey-ledger",
            "cards-lifecycle-teardown-settles-and-disposes",
            "cards-lifecycle-facts",
        };

        /// <summary>
        /// Every fact key both runs must set, with the card slice's semantic score and table-version values
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
            "scoreAfterUnload",
            "tableVersionAfterUnload",
        };

        private static IReadOnlyList<CardLifecycleStep> observations = null!;
        private static CardLifecycleFacts generated = null!;
        private static CardLifecycleFacts fixture = null!;

        [OneTimeSetUp]
        public void RunBothLifecycleRunsOncePerCatalog()
        {
            observations = CardLifecycleScenario.RunBoth(out generated, out fixture);
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

            CardLifecycleStep? digest = FindStep(observations, FixturePrefix + CardLifecycleKeys.StepFacts);
            Assert.That(digest, Is.Not.Null, "the fixture run carries no fact-digest observation.");
            Assert.That(digest!.Detail, Is.EqualTo(fixture.Describe()),
                "the fact observation must carry the run's own digest.");
        }

        /// <summary>The world and provider observation: an owned world, the lane joined, the provider active.</summary>
        [Test]
        public void TheWorldAndProviderObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepWorld);
        }

        /// <summary>Suspension retracts the active behavior in the same publication.</summary>
        [Test]
        public void TheSuspendObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepSuspend);
        }

        /// <summary>Resume rederives the current ancestry and restores the contribution.</summary>
        [Test]
        public void TheResumeObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepResume);
        }

        /// <summary>Losing a required provider makes its consumer wait in the same publication.</summary>
        [Test]
        public void TheProviderLossObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepProviderLoss);
        }

        /// <summary>Returning a compatible provider resumes the waiting consumer in the same publication.</summary>
        [Test]
        public void TheProviderReturnObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepProviderReturn);
        }

        /// <summary>Replacement stages a candidate while the old activation still holds authority.</summary>
        [Test]
        public void TheReplacementObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepReplacement);
        }

        /// <summary>Unload closes ingress, retracts the contribution and retires the leases.</summary>
        [Test]
        public void TheUnloadObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepUnload);
        }

        /// <summary>An outstanding job prevents buffer release until it completes.</summary>
        [Test]
        public void TheBlockedJobObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepJobFence);
        }

        /// <summary>Every invalid transition is refused as a value.</summary>
        [Test]
        public void TheInvalidTransitionsObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepInvalidTransitions);
        }

        /// <summary>Repeated operations obey the ledger (P-050, P-051).</summary>
        [Test]
        public void TheRepeatedOperationsObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepRepeatedOperations);
        }

        /// <summary>Teardown settles its work and returns the world registry to its baseline (P-047, P-048).</summary>
        [Test]
        public void TheTeardownObservationPasses()
        {
            AssertStepPassed(CardLifecycleKeys.StepTeardown);
        }

        /// <summary>The run carries its values: the fact observation always passes (repo digest convention).</summary>
        [Test]
        public void TheFactObservationCarriesTheDigest()
        {
            AssertStepPassed(CardLifecycleKeys.StepFacts);
        }

        /// <summary>
        /// Both runs carry exactly the documented fact keys in canonical ordinal order, and the digest is one
        /// `key=value` line per key in that same order — so an omitted, invented or misspelled key fails here.
        /// </summary>
        [Test]
        public void BothRunsCarryExactlyTheDocumentedFacts()
        {
            foreach (CardLifecycleFacts facts in Runs())
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, CardLifecycleKeys.FactWorldLifecycle),
                    Is.EqualTo(WorldLifecycleState.Running.ToString()),
                    "the host world lifecycle must be Running: " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactLaneJoined), Is.True,
                    "the lane must have joined the world's published assembly (P-006): " + facts.Describe());
                Assert.That(Value(facts, CardLifecycleKeys.FactProviderStateBefore),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "the mounted provider must be published Active (P-046): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactProviderRowsBefore), Is.GreaterThan(0L),
                    "an active provider must own at least one attributed row: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactRegistryBeforeCreate),
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, CardLifecycleKeys.FactSuspendState),
                    Is.EqualTo(InstallationState.Suspended.ToString()),
                    "an explicit suspend must publish the installation Suspended (P-046): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactSuspendRowsAfter), Is.Zero,
                    "suspension retracts the active contributions, so no attributed row may remain: "
                    + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactSuspendClosedRoutes),
                    Is.GreaterThanOrEqualTo(0L), facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactSuspendGateLiveActivations), Is.Zero,
                    "a suspended installation must hold no live callback activation (P-047): " + facts.Describe());
                AssertDiscarded(facts, CardLifecycleKeys.FactSuspendLateCompletion);
            }
        }

        /// <summary>
        /// Step 3: resume publishes `Active` again and restores exactly the contributions the suspend retracted.
        /// </summary>
        [Test]
        public void ResumeRestoresTheSameContribution()
        {
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, CardLifecycleKeys.FactResumeState),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "resume must republish the installation Active (P-046): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactResumeRowsAfter),
                    Is.EqualTo(Integer(facts, CardLifecycleKeys.FactProviderRowsBefore)),
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, CardLifecycleKeys.FactLossConsumerState),
                    Is.EqualTo(InstallationState.WaitingForDependencies.ToString()),
                    "a consumer that lost a required provider must be WaitingForDependencies (P-012): "
                    + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactLossWaitingConsumers), Is.EqualTo(1L),
                    "exactly the one consumer must be named by the same publication: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactLossConsumerBindings), Is.Zero,
                    "a waiting consumer holds no binding: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactLossRetractedRows), Is.GreaterThan(0L),
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, CardLifecycleKeys.FactReturnConsumerState),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "a consumer whose dependency returned must be Active again (P-012): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactReturnResumedConsumers), Is.EqualTo(1L),
                    "exactly the one consumer must be named as resumed by the same publication: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactReturnRows),
                    Is.EqualTo(Integer(facts, CardLifecycleKeys.FactLossRetractedRows)),
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, CardLifecycleKeys.FactReplacementStagedCandidates), Is.EqualTo(1L),
                    "the reconfigure must stage exactly one candidate: " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactReplacementOldHoldsAuthority), Is.True,
                    "the old activation must still hold authority in the staging publication (P-046): "
                    + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactReplacementEpochChanged), Is.True,
                    "an in-place reconfigure must advance the activation epoch: " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactReplacementGenerationUnchanged), Is.True,
                    "an in-place update must keep the installation generation (P-046): " + facts.Describe());
                Assert.That(Value(facts, CardLifecycleKeys.FactReplacementStateAfter),
                    Is.EqualTo(InstallationState.Active.ToString()),
                    "the installation must be Active after the replacement: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactReplacementRowsAfter),
                    Is.EqualTo(Integer(facts, CardLifecycleKeys.FactProviderRowsBefore)),
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Value(facts, CardLifecycleKeys.FactUnloadState),
                    Is.EqualTo(InstallationState.Disposed.ToString()),
                    "nothing is retained in this step, so the unload must publish Disposed (P-048): "
                    + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactUnloadIngressClosed), Is.GreaterThanOrEqualTo(1L),
                    "the unload must have closed the installation's ingress (P-047): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactUnloadRetractedRows),
                    Is.EqualTo(Integer(facts, CardLifecycleKeys.FactProviderRowsBefore)),
                    "the unload must retract exactly the rows the installation held: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactUnloadRetiredLeases),
                    Is.GreaterThanOrEqualTo(1L),
                    "the unload must retire the installation's staged leases in reverse order (P-048): "
                    + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactUnloadQuarantined), Is.Zero,
                    "no resource is reachable by unfinished work in this step: " + facts.Describe());
                AssertDiscarded(facts, CardLifecycleKeys.FactUnloadLateCompletion);
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, CardLifecycleKeys.FactFenceOutstandingJobs),
                    Is.GreaterThanOrEqualTo(1L),
                    "the tracked job must still be outstanding when the unload is attempted: " + facts.Describe());
                Assert.That(Value(facts, CardLifecycleKeys.FactFenceBlockedCode),
                    Is.EqualTo(DiagnosticCodeText.Of(DiagnosticCode.TeardownBlocked)),
                    "a blocked teardown reports TeardownBlocked, never a free (P-048): " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactFenceRetainedWhileOutstanding), Is.True,
                    "the fenced lease must stay retained while the job owns its buffer: " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactFenceDisposeSettledWhileOutstanding), Is.False,
                    "a blocked job must prevent the installation from settling Disposed (P-048): "
                    + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactFenceQuarantineBeforeRelease),
                    Is.GreaterThanOrEqualTo(1L),
                    "the retained resource must be quarantined, not released: " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactFenceReleasedAfterCompletion), Is.True,
                    "completing the job must release the quarantined lease: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactFenceQuarantineAfterRelease), Is.Zero,
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
                CardLifecycleKeys.FactInvalidSuspendTwiceCode,
                CardLifecycleKeys.FactInvalidResumeActiveCode,
                CardLifecycleKeys.FactInvalidUnmountDisposedCode,
                CardLifecycleKeys.FactInvalidReconfigureDisposedCode,
                CardLifecycleKeys.FactInvalidRemountLiveIdentityCode,
                CardLifecycleKeys.FactInvalidTeardownPathRefusedCode,
            };

            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, CardLifecycleKeys.FactInvalidRejectedCount),
                    Is.EqualTo((long)codes.Length),
                    "every one of the six invalid transitions must be counted as refused: " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactInvalidStateUnchanged), Is.True,
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                AssertRefusedCode(facts, CardLifecycleKeys.FactRepeatSuspendRefusedCode);
                Assert.That(Value(facts, CardLifecycleKeys.FactRepeatRetransmissionKind),
                    Is.EqualTo(AdmissionKind.Retransmission.ToString()),
                    "the same id and input must be admitted as a retransmission (P-050): " + facts.Describe());
                Assert.That(Boolean(facts, CardLifecycleKeys.FactRepeatReconfigureSameOutcome), Is.True,
                    "a retransmission must return the original outcome: " + facts.Describe());
                AssertRefusedCode(facts, CardLifecycleKeys.FactRepeatUnmountRefusedCode);
                Assert.That(Integer(facts, CardLifecycleKeys.FactLedgerRowCount), Is.GreaterThan(0L),
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
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, CardLifecycleKeys.FactOutstandingJobsAfterTeardown), Is.Zero,
                    "teardown must settle every tracked job (P-048): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactRetainedResourcesAfterTeardown), Is.Zero,
                    "teardown must release everything it quarantined (P-048): " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactRegistryAfterTeardown),
                    Is.EqualTo(Integer(facts, CardLifecycleKeys.FactRegistryBeforeCreate)),
                    "the owned-world registry must return to its pre-create baseline (04 section 3): "
                    + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactIdleSteps), Is.Zero,
                    "an idle command-driven world commits no step (P-036): " + facts.Describe());
            }
        }

        /// <summary>
        /// The card semantics of the lifecycle: no command is ever admitted in this scenario, so after the unload
        /// publication the seat still carries its seeded score and the table still carries its seeded version — the
        /// composition publication itself never mutates card storage (P-032, P-044).
        /// </summary>
        [Test]
        public void TheCardScoreAndTableVersionReflectTheLifecycle()
        {
            foreach (CardLifecycleFacts facts in Runs())
            {
                Assert.That(Integer(facts, CardLifecycleKeys.FactScoreAfterUnload),
                    Is.EqualTo((long)CardTableKeys.SeededSeatScore),
                    "no command was admitted, so the seat keeps its seeded score: " + facts.Describe());
                Assert.That(Integer(facts, CardLifecycleKeys.FactTableVersionAfterUnload),
                    Is.EqualTo((long)CardTableKeys.SeededTableVersion),
                    "no settlement committed, so the table keeps its seeded version: " + facts.Describe());
            }
        }

        /// <summary>
        /// The family's committed generated card catalog, run in this same suite so the lifecycle assertions and the
        /// real generated registrations are exercised together on one revision.
        /// `CardsScenarioHost.RunGeneratedCatalog` is the qualification project's entry over `CardCatalog.g.cs`
        /// (GC-011), which the fixtures package cannot reference; running it here is what keeps this suite's claim
        /// about "both catalogs" literally true.
        /// </summary>
        [Test]
        public void TheCommittedGeneratedCatalogStillRunsItsOwnSlice()
        {
            CardScenarioResult result = CardsScenarioHost.RunGeneratedCatalog();
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Steps, Is.Not.Empty, "the committed-catalog run must report its observations.");
            Assert.That(result.AllPassed, Is.True,
                "the committed generated card catalog must still pass on this revision: " + result.Describe());
        }

        private static IEnumerable<CardLifecycleFacts> Runs()
        {
            yield return generated;
            yield return fixture;
        }

        private static List<CardLifecycleStep> StepsOf(string prefix)
        {
            var steps = new List<CardLifecycleStep>();
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
            List<CardLifecycleStep> steps = StepsOf(prefix);
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
        private static CardLifecycleStep? FindStep(
            IReadOnlyList<CardLifecycleStep> steps,
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
            List<CardLifecycleStep> steps = StepsOf(prefix);
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
            CardLifecycleStep? generatedStep = FindStep(observations, stepName);
            Assert.That(generatedStep, Is.Not.Null,
                "the generated-catalog run did not report the observation '" + stepName + "'.");
            Assert.That(generatedStep!.Passed, Is.True, generatedStep.Detail);

            string prefixed = FixturePrefix + stepName;
            CardLifecycleStep? fixtureStep = FindStep(observations, prefixed);
            Assert.That(fixtureStep, Is.Not.Null,
                "the fixture-catalog run did not report the observation '" + prefixed + "'.");
            Assert.That(fixtureStep!.Passed, Is.True, fixtureStep.Detail);
        }

        private static string Value(CardLifecycleFacts facts, string key)
        {
            Assert.That(facts.Has(key), Is.True,
                "the run did not set the fact '" + key + "': " + facts.Describe());
            string text = facts.ValueOf(key);
            Assert.That(text, Is.Not.Null.And.Not.Empty,
                "the fact '" + key + "' must carry a value: " + facts.Describe());
            return text;
        }

        private static long Integer(CardLifecycleFacts facts, string key)
        {
            string text = Value(facts, key);
            bool parsed = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long value);
            Assert.That(parsed, Is.True,
                "the fact '" + key + "' must be an invariant integer but was '" + text + "': " + facts.Describe());
            return value;
        }

        private static bool Boolean(CardLifecycleFacts facts, string key)
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
        private static void AssertRefusedCode(CardLifecycleFacts facts, string key)
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
        private static void AssertDiscarded(CardLifecycleFacts facts, string key)
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
        private static void AssertDigestLines(CardLifecycleFacts facts)
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
