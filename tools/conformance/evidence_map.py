"""GC-028 conformance evidence map: clauses -> concrete executable evidence.

This module is *data*, not a report. It says which executable artifact would evidence each normative
clause of `docs/game-core/00-core-protocols.md` (P-001..P-060, O-01..O-26) and nothing about whether
that artifact passed. `tools/conformance/build_evidence_index.py` reads the actual result files
(TRX, Unity NUnit3 XML, probe JSON, host/release check JSON) and derives every status from them; a
reference in this file that resolves to no result is reported as an `unresolved-reference` problem
and fails the build of the index. Statuses are therefore never hand-typed here.

Reference syntax (resolved by `build_evidence_index.py`):

  dotnet:<FQN>          a recorded NUnit result in a vstest TRX (class.method, no arguments)
  unity:<FQN>           a recorded result in the Unity EditMode/PlayMode NUnit3 XML
  probe:<mode>/<name>   a named probe observation in a player probe result JSON; `<name>` may be a
                        bare family observation (`recovery-smoke-...`) or a label-qualified one
  check:<key>           one archived host-side or release-side check document (see CHECKS)

C# / assembly names in this file were read from the recorded w7-gate results, not from memory.
"""

# ---------------------------------------------------------------------------------------------------
# Check documents: key -> (evidence-root-relative path candidates, extractor, expected).
# The extractor returns the value the check recorded; `expected` is what a passing document holds.
# A missing file is NotRun; a present file whose extractor disagrees with `expected` is Fail.
# ---------------------------------------------------------------------------------------------------
CHECKS = {
    "genre-audit": {
        "paths": ["genre-audit.json", "../gc-024/genre-audit.json"],
        "field": "clean",
        "expected": True,
        "describes": "no kernel assembly/project/source references a gameplay, rules or generated assembly "
                     "and no kernel source names a genre type (P-001, P-057, P-059)",
    },
    "budget-record": {
        "paths": ["host/budget-record.json"],
        "field": "verdict",
        "expected": "pass",
        "describes": "the ten provisional budget rows exist in the decision record with a decision and "
                     "existing evidence paths, and the owner deferral of full-duration timing is stated (P-060, TEST-023)",
    },
    "compatibility-report": {
        "root": "repo",
        "paths": ["artifacts/conformance/compatibility.json"],
        "field": "protocolVersion",
        "expected": "1.0",
        "describes": "the protocol/catalog compatibility report exists and still declares protocol 1.0 "
                     "(docs/game-core/00-core-protocols.md section 8; GC-028)",
    },

    "benchmark-diagnostic": {
        "paths": ["benchmark/summarize.json"],
        "field": "counts.gatesFailed",
        "expected": 0,
        "describes": "the short benchmark correctness diagnostic completed with zero failing correctness gates (TEST-023)",
    },
    "budget-decisions": {
        "paths": ["../performance/BUDGET_DECISIONS.md"],
        "field": "exists",
        "expected": True,
        "describes": "the budget decision record exists and carries the owner deferral sentence (P-060)",
    },
    "catalog-byte-identity": {
        "paths": ["static-checks.log"],
        "contains": "every committed catalog and coverage file reproduces byte for byte from its committed description",
        "describes": "the committed catalogs regenerate byte-identically from their committed descriptions (P-028, P-058)",
    },
    "contract-surface-parity": {
        "paths": ["static-checks.log"],
        "contains": "every snapshotted enum and enum value exists in the production sources with the same numeric value",
        "describes": "the compiled production contract surface is a strict superset of the frozen W0 seam snapshot (P-054)",
    },
    "gate-sources": {
        "paths": ["host/gate-sources.json"],
        "field": "status",
        "expected": "Pass",
        "describes": "the frozen observation tables recompute from their own sources (P-008 evidence discipline)",
    },
    "docs-validator": {
        "paths": ["validator.log"],
        "field": "exists",
        "expected": True,
        "describes": "the documentation validator ran over every required document (TEST-024)",
    },
    "docs-validator-self-test": {
        "paths": ["validator-self-test.log"],
        "field": "exists",
        "expected": True,
        "describes": "the documentation validator's own positive/negative fixtures ran (TEST-024)",
    },
    "clone-surface": {
        "paths": ["release/clone-surface.json"],
        "field": "status",
        "expected": "Pass",
        "describes": "the marker-free release clone carries no qualification-only mode or package (P-058)",
    },
    "release-gate-surface": {
        "paths": ["release-gate-surface.json"],
        "field": "status",
        "expected": "Pass",
        "describes": "the release player omits every qualification marker and keeps the declared shipping modes (P-058)",
    },
    "release-fault-surface": {
        "paths": ["release-fault-surface.json"],
        "field": "status",
        "expected": "Pass",
        "describes": "the fault latches are compiled out of a release build (P-031/P-058 release half)",
    },
    "release-player-surface": {
        "paths": ["release-player-surface.json"],
        "field": "status",
        "expected": "Pass",
        "describes": "the built release player's own managed/generated sources carry no qualification marker (P-060)",
    },
    "telemetry-release-surface": {
        "paths": ["release/telemetry-release-surface.json"],
        "field": "status",
        "expected": "Pass",
        "describes": "the telemetry instrumentation is absent from a release build (P-060)",
    },
    "link-xml": {
        "paths": ["host/link-xml-qualification.json"],
        "field": "problems",
        "expected": 0,
        "describes": "link.xml preserves exactly the permitted roots under High managed stripping (P-058)",
    },
    "native-leak-attribution": {
        "paths": ["toolchain/native-leak-attribution.json"],
        "field": "verdict",
        "expected": "clean",
        "describes": "every retained player log's native leak header is attributed (TEST-023, P-048)",
    },
    "player-environment": {
        "paths": ["toolchain/environment.txt"],
        "field": "exists",
        "expected": True,
        "describes": "the qualification player's exact profile (Editor, packages, backend, stripping, hashes) is recorded (P-058, P-060)",
    },
    "probe-result": {
        "paths": ["toolchain/probe-catalog-coverage.json"],
        "field": "result",
        "expected": "Pass",
        "describes": "the qualification player resolves every committed generated catalog entry (TEST-001, TEST-020)",
    },
}


# Test classes this task added. A reference to one of them cannot resolve against a gate tree recorded before
# this task existed; `build_evidence_index.py` reports those as `NotRun` with that reason instead of treating them
# as a mistake in the map. They resolve as soon as `tools/conformance/run_test_matrix.sh` has recorded a run.
GC028_ADDED = (
    "GameCore.Contracts.Tests.RequiredDiagnosticCodeTests",
    "GameCore.Contracts.Tests.ServiceBindingLeaseTests",
    "GameCore.Execution.Tests.ReadPortConformanceTests",
    "GameCore.Planning.Scheduling.Tests.BufferProducerConformanceTests",
    "GameCore.Composition.Tests.ProviderFailureTests",
    "GameCore.Composition.Tests.ActivationLedgerTests.AnUnexpectedFailureOfALiveActivation",
    "GameCore.Composition.Tests.ActivationLedgerTests.OnlyALiveActivationCanFailUnexpectedly",
    "GameCore.Composition.Tests.ActivationLedgerTests.AFailedActivationCanOnlyRetryOrRetire",
    "GameCore.Composition.Tests.ActivationLedgerTests.ReportingTheSameLiveFailureTwiceIsIdempotent",
)


# Exact references this task adds for other schemes (a Unity test name, a probe observation). They cannot exist in a
# tree recorded before this task, so they are reported as pending rather than as a wrong name; they resolve as soon
# as `tools/conformance/run_test_matrix.sh` has recorded a run of the fresh revision.
GC028_ADDED_REFERENCES = (
    "probe:Faults/narrative/gc017-provider-failure-that-cannot-publish-faults-the-world",
    "probe:Faults/cards/gc017-provider-failure-that-cannot-publish-faults-the-world",
    "probe:Faults/narrative/gc017-provider-failure-publishes-a-safe-deactivation",
    "probe:Faults/cards/gc017-provider-failure-publishes-a-safe-deactivation",
    "probe:Faults/fixture:narrative/gc017-provider-failure-that-cannot-publish-faults-the-world",
    "probe:Faults/fixture:cards/gc017-provider-failure-that-cannot-publish-faults-the-world",
    "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheNarrativeFamilyPinsTheProviderFailureDeactivation",
    "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheCardsFamilyPinsTheProviderFailureDeactivation",
    "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheNarrativeFamilyPinsTheProviderFailureThatFaultsTheWorld",
    "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheCardsFamilyPinsTheProviderFailureThatFaultsTheWorld",
)


def is_added_by_gc028(reference):
    """True when a reference names a test this task added rather than a mistake in the map."""
    text = str(reference)
    if text in GC028_ADDED_REFERENCES:
        return True
    dotnet_added = text.startswith("dotnet:") and (
        "GameCore.Contracts.Tests.RequiredDiagnosticCodeTests" in text
        or "GameCore.Contracts.Tests.ServiceBindingLeaseTests" in text
        or "GameCore.Execution.Tests.ReadPortConformanceTests" in text
        or "GameCore.Planning.Scheduling.Tests.BufferProducerConformanceTests" in text
        or "GameCore.Composition.Tests.ProviderFailureTests" in text
        or "GameCore.Composition.Tests.ActivationLedgerTests.AnUnexpectedFailureOfALiveActivation" in text
        or "GameCore.Composition.Tests.ActivationLedgerTests.OnlyALiveActivationCanFailUnexpectedly" in text
        or "GameCore.Composition.Tests.ActivationLedgerTests.AFailedActivationCanOnlyRetryOrRetire" in text
        or "GameCore.Composition.Tests.ActivationLedgerTests.ReportingTheSameLiveFailureTwiceIsIdempotent" in text
        or "GameCore.Composition.Tests.InstallationLifecycleTests.EveryStatePairMatchesTheLifecycleDiagramExactly"
           in text)
    if dotnet_added:
        return True
    return False


def C(clause, *evidence, **kwargs):
    """One normative clause and the concrete executable evidence that would discharge it."""
    row = {"clause": clause, "evidence": list(evidence)}
    if kwargs.get("deferred"):
        row["deferred"] = kwargs["deferred"]
    if kwargs.get("blocked"):
        row["blocked"] = kwargs["blocked"]
    if kwargs.get("note"):
        row["note"] = kwargs["note"]
    return row


def R(title, clauses, **kwargs):
    row = {"title": title, "clauses": clauses}
    if kwargs.get("owner"):
        row["owner"] = kwargs["owner"]
    return row


# ---------------------------------------------------------------------------------------------------
# P-001..P-060. One row per requirement; one clause row per normative sentence group.
# ---------------------------------------------------------------------------------------------------
REQUIREMENTS = {
    "P-001": R("Genre independence and boundaries", [
        C("kernel provides composition, identity, assembly, execution coordination, lifecycle and observation "
          "contracts without requiring an actor, action, turn, combat, physics, animation, resource or reward schema",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.NoScheduleRequiresACombatPhysicsOrAnimationStage",
          "unity:GameCore.Gc020.Tests.Gc020IntegrationTests.TheCardsAndNarrativeDeclareNoActionPhaseObservationPasses",
          "probe:Conformance/conformance/cross/no-action-surface-in-card-or-narrative",
          "check:genre-audit"),
        C("a scope tree, installation graph, execution graph, ECS entity graph, Transform hierarchy and network "
          "topology are separate structures; no one-to-one mapping between plugins, scopes and entities is required",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.AVisualReparentDoesNotMoveComposition",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.OneMountDerivesTheSameBindingForEveryMatchingTarget",
          "unity:GameCore.Gc019.Tests.Gc019IntegrationTests.TheVisualReparentLeavesCompositionUnchangedObservationPasses",
          "probe:Traversal/gc020-presentation-rate-does-not-double-advance"),
        C("a plugin MAY introduce any of those domain concepts through registered contracts",
          "dotnet:GameCore.Rules.Narrative.Tests.GenreNeutralityTests.EveryRegisteredNameIsNeutralOnItsOwnAndNoneIsRegisteredTwice",
          "unity:GameCore.Gc019.Tests.Gc019IntegrationTests.TheExternalAuthorityAbsenceObservationPasses",
          "probe:Conformance/conformance/genre-audit"),
        C("V1 executes only trusted precompiled plugins in one process; isolation means composition visibility, "
          "not a security sandbox",
          "unity:GameCore.CatalogCoverage.Tests.CatalogCoverageIntegrationTests.TheInactivePluginsMountLateByKey",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-late-mount-inactive-plugin",
          "probe:Positive/late-mount-linked-inactive-plugin"),
    ]),
    "P-002": R("Participants and authority", [
        C("WorldHost is the sole authority for world lifecycle, command admission, composition revision order "
          "and assembly publication",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.RepeatedCreationOfTheSameSessionReturnsTheSameWorld",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.StopClosesIngressSettlesJobsAndDisposesStorage",
          "dotnet:GameCore.Composition.Tests.LaneSeedTests.ASeedJoinsTheLaneToTheWorldsPublishedAssembly",
          "probe:WorldDispatch/world-bootstrap-and-loop-route"),
        C("CompositionHost keeps the desired scope/install graph on its serialized control lane; AssemblyPlanner is a "
          "pure consumer of immutable snapshots; AssemblyPublisher applies a validated plan at a safe boundary",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.TheBaseRecheckComparesRevisionAndEpochWithoutMutatingThePlan",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AStalePlanIsRejectedBeforeAnyWrite",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.SeveralAdmittedProposalsPublishInAdmissionOrder"),
        C("PluginInstance proposes declarations and owns managed leases, never arbitrary writes to a live ECS world",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.RetiringAnInstanceDisposesEachLeaseOnceInReverseAcquisitionOrder"),
        C("StateOwner is a registered logical authority whose systems write declared state; ExecutionDriver runs the "
          "compiled graph; EngineAdapter admits observations and presents committed output; Observer reads snapshots",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADeclaredWriterWithoutAnOwnerRejects",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.WellFormedPlanProjectsOntoTheEpochBoundTable",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ThePresenterReadsCommittedOutputAndRemovesOrphanedViews",
          "dotnet:GameCore.Execution.Tests.Observation.WorldObservationTests.ABoundaryLeaseCarriesTheImageItsEventsAndTheQueueFactsTogether"),
        C("within a world, publication and step execution MUST NOT overlap",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AConcurrentObserverNeverSeesAMixedAssembly",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.MainThreadDisciplineIsExplicitlyCaptured",
          "probe:Conformance/conformance/cards/mode-directions/setup-0"),
    ]),
    "P-003": R("Three distinct kinds of change", [
        C("ManagedResourceEffect acquires/releases subscriptions, service registrations, asset leases or system "
          "registrations; it has a lifetime and cleanup",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.ResourcesAreDisposedAtMostOnce",
          "dotnet:GameCore.Composition.Tests.LifecycleStressTests.EveryAcquisitionOfACycleIsTracedToRetirementOrQuarantineByKind",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ALeaseWithAConsumerIsRetainedAndAFailingReleaseIsQuarantined"),
        C("CapabilityContribution is a declarative input with provenance and retraction",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.RetractingOneOfTwoSupportersPreservesTheSurvivorAndReportsTheLostSupport",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.ALosingCandidateIsProvenanceAndNeverSupport",
          "probe:W4Gate/w4-slot-remove-derived-drops-the-row"),
        C("GameplayEffect is plugin-defined authoritative state change that follows the owner's commit policy",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.SetScoreDeltaAddsTheEffectiveBonusAndNeverWraps",
          "probe:Cards/cards-transfer-commits-both-sides",
          "probe:Cards/cards-one-command-commits-both-sides"),
        C("the kernel MUST NOT expose a common reversible Effect API; retracting a contribution or disposing a "
          "resource does not undo committed gameplay",
          "probe:Gc021/gc021-no-universal-effect-api",
          "probe:Cards/cards-rejected-settlement-changes-nothing",
          "probe:Conformance/conformance/cross/reward-scoring-unmount-keeps-card",
          "probe:Conformance/conformance/cards/unmount-festival"),
    ]),
    "P-004": R("Stable identities", [
        C("WorldId is a fresh 128-bit session ID, never reused, including on checkpoint restore",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.TwoWorldsAdvanceIndependently",
          "probe:RecoverySmoke/recovery-smoke-recover-publishes-a-new-session",
          "probe:Gc018/gc018-restore-happens-into-a-new-unexposed-world",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.RestoringIntoTheCapturedSessionIsRefused"),
        C("WorldDefinitionId, ScopeId, TargetId, PluginTypeId, PluginInstanceId, CapabilityId, SchemaId, OwnerId, "
          "StageId and DefinitionId are stable 128-bit IDs with a catalog name for diagnostics; names are not "
          "runtime identity",
          "dotnet:GameCore.Contracts.Tests.IdentityDerivationTests.DerivationIsDeterministicAndRejectsNonCanonicalNames",
          "dotnet:GameCore.Contracts.Tests.IdentityDerivationTests.DerivationMatchesTheCommittedProbeKeyLiterals",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.Id128ParsingAcceptsOnlyTheCanonicalLowercaseForm"),
        C("one world rejects duplicate live stable IDs in a category",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.ADuplicateLiveIdentityIsAConflict",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.DuplicateScopeIdentityIsRejectedAsAnOwnershipConflict",
          "dotnet:GameCore.Contracts.Tests.CheckpointIdentityTableTests.ASecondIdentityInOneCategoryIsRejectedAndTheFirstStaysAuthoritative"),
        C("installation keys and target spawn keys are explicitly stored in content/checkpoints/replay records; "
          "ordering MUST NOT depend on creation thread or a randomly regenerated installation key",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ResolutionIsIndependentOfMountInsertionOrderForFixedSeeds",
          "dotnet:GameCore.Contracts.Tests.CheckpointRecordRoundTripTests.InstallRecordRoundTripsIdentityScopeConfigHashAndLifecycle",
          "dotnet:GameCore.Contracts.Tests.CatalogBuildTests.CanonicalOrderIsIndependentOfRegistrationOrder",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.CanonicalMapOrderIsIndependentOfInsertionOrderAndReportsDuplicates"),
        C("a target is an assembly recipient represented by one primary ECS entity; auxiliary entities are "
          "recipe-owned and need no extra scopes",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.LiveTargetInsertionAndRetirementMatchAppendAndSortOracle",
          "probe:Cards/cards-ownership-and-schedule-compiled",
          "probe:Conformance/conformance/traversal/numeric-acceleration-sequence/world"),
    ]),
    "P-005": R("Runtime handles", [
        C("TargetHandle contains WorldId, slot and generation; ScopeHandle adds a scope generation; PluginHandle "
          "adds an installation generation",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.AFreshSlotAllocatesGenerationOneAndResolves",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.HandlesAreListedInSlotOrderForTheFence",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.HandleGenerationZeroIsNeverAllocatedAndSlotsAreUnsigned"),
        C("every dereference MUST validate world, generation, liveness and expected category, plus activation epoch "
          "when execution authority is required",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.StaleAndForeignHandlesNeverResolve",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.AHandleTakenBeforeARetireIsRejectedAfterwards",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.ForeignWorldOperationCannotMutateThisScopeTree",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.CallbackGateRejectsForeignWorldAndStaleActivation"),
        C("installation generation changes on unmount/remount, not ordinary reconfigure, suspend/resume or in-place "
          "package replacement; those change ActivationEpoch",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.RemountingAStableInstanceIdentityAdvancesItsInstallationGeneration",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.SuspendWalksThroughQuiescingAndResumeRecordsANewAttempt",
          "probe:W4Gate/w4-lane-epoch-equals-world-epoch-throughout"),
        C("destroy/recreate invalidates handles even when a stable ID is restored",
          "probe:RecoverySmoke/recovery-smoke-recovered-world-is-authoritative",
          "unity:GameCore.Unity.Runtime.Tests.Recovery.InitialDefinitionRecoveryTests.RecoveryFromInitialDefinitionsCreatesAFreshIncarnationAndNeverResumesTheSource",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ACompletionFromAStaleActivationIsDiscardedAndReleased"),
        C("generations and epochs use unsigned 64-bit counters; overflow rejects further allocation/reconfiguration "
          "and requires world recreation, never wraparound",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.AnExhaustedGenerationCounterIsRefusedRatherThanWrapped",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.IdSequenceIsDeterministicAndNeverWraps",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.VersionDomainsUseTypedCounters",
          "dotnet:GameCore.ProtocolFixtures.Tests.ProtocolFixtureTests.CounterOracleRefusesOverflowForEveryCounter"),
        C("runtime handles, Unity Entity, pointers and native container views MUST NOT be persisted",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ActiveAndDormantStateRoundTripsWithoutHandles",
          "probe:Recovery/gc027-restored-world-uses-different-native-handles",
          "probe:Gc018/gc018-restore-recreates-state-at-different-native-indices"),
    ]),
    "P-006": R("Version domains", [
        C("CompositionRevision increments only when a composition proposal publishes; it orders scope, membership, "
          "mode, configuration and installation changes",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PublicationMovesRevisionAndEpochButNeverTheLogicalStep",
          "dotnet:GameCore.Composition.Tests.LaneSeedTests.EveryPublicationMovesRevisionAndEpochTogether",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.StaleExpectedRevisionIsRejectedWithoutPublishing"),
        C("AssemblyEpoch increments at the same publication, including lifecycle changes that do not alter component "
          "structure",
          "probe:W4Gate/w4-lane-epoch-equals-world-epoch-throughout",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.SuspensionClosesAuthorityAndExplicitResumeUsesAFreshActivation",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.TerminalRetransmissionKeepsItsOriginalHandle"),
        C("ActivationEpoch changes whenever an installation loses or gains execution authority, rebinds or reconfigures",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.CommitMovesTheCandidateInAndDisplacesTheRunningActivationToRetiring",
          "probe:W2Gate/gate2-provider-mounted-and-published"),
        C("SchemaVersion and DefinitionRevision describe content compatibility, not liveness",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ASchemaIdentityChangeIsNotAMigration",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.AMatchingSchemaVersionOnlyRetainsAndASchemaChangeMigrates"),
        C("LogicalStepId increments only at successful simulation commit, and a publication never increments a step",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PublicationMovesRevisionAndEpochButNeverTheLogicalStep",
          "dotnet:GameCore.Replay.Tests.ReplayDeterminismTests.EveryStepCommitsExactlyOneLogicalStepAndTheStepChainIsStable",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.CreationPublishesEpochOneAndStepZero"),
        C("SnapshotToken = (WorldId, AssemblyEpoch, LogicalStepId) identifies one committed observation image",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.PublishingOneStepExposesExactlyOneImage",
          "dotnet:GameCore.Execution.Tests.Observation.SnapshotRetentionTests.PublishingExposesExactlyOneCompleteImagePerStep",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.AForeignWorldsImageIsNeverAcceptedAsAFirstPresentation"),
        C("a no-op proposal returns NoChange without increments",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.NoChangeIncrementsNothing",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.NoChangeIsAnOutcomeOfATerminalNonErrorState",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.APlanThatChangesNothingPublishesNoEpoch"),
    ]),
    "P-007": R("References and leases", [
        C("persistent references use a stable ID plus required schema/version or definition revision",
          "dotnet:GameCore.Contracts.Tests.CheckpointIdentityTableTests.EveryCheckpointReferenceIsAnIdentityAReaderCanLookUp",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.CatalogLookupReportsMissingKeysAndUnsupportedVersions",
          "dotnet:GameCore.Contracts.Tests.CatalogBuildTests.SchemaLookupDistinguishesUnknownIdentityFromUnacceptedVersion"),
        C("runtime service bindings contain contract ID, provider ID, activation epoch and a lease",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.BindingsNameTheContractAndProviderThatLeftAndReturned",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication"),
        C("service bindings resolve once per assembly, not per target per frame",
          "dotnet:GameCore.Replay.Tests.TelemetryBuildSwitchTests.ServiceStringLookupsAreCountedWhereResolutionHappensAndNowhereElse",
          "dotnet:GameCore.Replay.Tests.TelemetryBuildSwitchTests.ACarriedIncrementalDerivationDoesNoControlWork",
          "dotnet:GameCore.Contracts.Tests.ServiceBindingLeaseTests.TwoResolutionsOfOneProviderAreTheSameBindingValueAndKeepTheirLease",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.ReplayingTheSameDerivationProducesTheSameResultHash",
          note="the counter that proves the frequency claim is `service-string-lookups`, placed at the one "
               "string-keyed resolution in production and asserted to stay zero across a 10,000-step replay; there "
               "is no separate binding-resolution counter, so the argument is the lookup counter plus the "
               "binding's own once-per-assembly lease identity."),
        C("an async work token contains WorldId, installation generation, activation epoch and operation ID; a stale "
          "completion can release its own resources but MUST NOT publish state or reacquire execution authority",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ACompletionFromAStaleActivationIsDiscardedAndReleased",
          "dotnet:GameCore.Composition.Tests.LifecycleStressTests.ACompletionStampedByAnotherWorldIncarnationIsDiscarded",
          "probe:Gc019/gc019-late-asset-completion-cannot-write-a-retired-world",
          "probe:Recovery/gc027-recovered-world-refuses-an-old-session-observation"),
        C("read leases pin immutable snapshots, not live writable components; bounded retention rejects new leases "
          "with SnapshotBackpressure rather than overwriting leased memory",
          "dotnet:GameCore.Execution.Tests.Observation.SnapshotRetentionTests.ALeasedImageIsPinnedAndNeverEvictedOrOverwritten",
          "dotnet:GameCore.Execution.Tests.Observation.SnapshotRetentionTests.LeasePoolBackpressureIsAValueThatNeverOverwritesLeasedMemory",
          "probe:W5Gate/w5gate-pinned-snapshots-are-read-only"),
    ]),
    "P-008": R("Stable ordering and determinism boundary", [
        C("comparisons use canonical big-endian stable-ID bytes, ordinal identifiers and explicit numeric keys",
          "dotnet:GameCore.Contracts.Tests.CheckpointFormatTests.CanonicalId32CollationIsBigEndianAndOrderSensitive",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.EqualPriorityAndDepthAreBrokenByAscendingProviderIdentity",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.IndependentStagesAreOrderedByCanonicalStageIdBytes"),
        C("registration timing, dictionary enumeration, chunk order, worker index and ECB insertion timing MUST NOT "
          "decide precedence or semantic arbitration",
          "dotnet:GameCore.Replay.Tests.ReplayDeterminismTests.ShuffledProducerAndCompletionOrderDoesNotChangeTheHashes",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ShuffledDeclarationOrderCompilesToTheSameOrderAndHash",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.EveryPolicyProducesTheSameResultUnderEveryInsertionPermutation",
          "dotnet:GameCore.ProtocolFixtures.Tests.ProtocolFixtureTests.ShuffledInsertionOrdersProduceIdenticalCanonicalOutput",
          "probe:Replay/replay-hashes-identically-under-shuffled-producers"),
        C("repeatability is required for pure integer/fixed-rule fixtures given identical build/catalog, composition, "
          "ordered admitted input, seed streams and recorded engine observations",
          "dotnet:GameCore.Replay.Tests.ReplayDeterminismTests.TheTenThousandStepFixtureHashesIdenticallyAcrossWorkerCounts",
          "dotnet:GameCore.Replay.Tests.ReplayFixtureTests.TheGeneratorIsDeterministicAndTheRecordHashesAreStable",
          "probe:Replay/replay-trace-records-ten-thousand-steps"),
        C("this is not cross-platform floating-point, physics, external I/O or network lockstep determinism",
          "dotnet:GameCore.Replay.Tests.ObservationReplayTests.APhysicsDivergenceIsReportedAsAComparisonAndNotAsARuleRegression",
          "probe:Traversal/gc020-replay-separates-pure-motion-from-engine-observation",
          "probe:Conformance/conformance/traversal/trace-digest"),
    ]),
    "P-009": R("Manifest", [
        C("a PluginManifest MUST declare protocol major/minor range, exact package/content revision, stable type and "
          "installation IDs, required/optional services and versions, service exports, capability rules and "
          "contracts, state slots and owners, execution stages, access sets, buffer contracts, resource factories, "
          "generated registration keys and per-slot lifecycle/migration policies, with an empty category explicit",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.ValidManifestIsAccepted",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.UnsupportedProtocolRangeRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.UnknownRequiredFeatureRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.StateSlotWithoutAnOwnerRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.SystemWithoutAnAccessSetRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.BufferWithoutAPositiveCapacityRejects"),
        C("assets and configuration are immutable validated definitions",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ComposedDocumentHasAStableCanonicalHashAndRoundTrips",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.UndeclaredConfigurationFieldIsRejected",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ADeclarationWithoutAnOwnerRejectsBeforeAnyRequest"),
        C("the catalog rejects duplicate IDs, unknown schema/contract versions, missing precompiled factories, "
          "unsupported required protocol features, undeclared access or missing policies before activation",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.DuplicateCapabilityContractRejectsAsCapabilityConflict",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.MissingPrecompiledFactoryKeyRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.UnacceptedSchemaVersionRejectsAsUnsupportedVersion",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.OverlappingWriteAccessWithoutAnEdgeRejects",
          "dotnet:GameCore.Contracts.Tests.CatalogBuildTests.DuplicateFactoryKeyRejectsWithOwnershipConflict",
          "dotnet:GameCore.Contracts.Tests.CatalogBuildTests.SchemaWithoutRegisteredSerializerRejectsAsMissingDependency"),
        C("absence of a currently active compatible provider for a known contract is a runtime dependency wait under "
          "P-012, not an unknown-catalog-version error",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.IncompatibleContractVersionLeavesTheConsumerWaiting",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.WaitingInstallationActivatesWhenItsProviderAppearsAndWaitsAgainWhenItLeaves",
          "probe:W4Gate/w4-required-provider-loss-makes-consumers-wait"),
        C("dynamic player mounting selects catalog entries and does not load new CLR code",
          "unity:GameCore.CatalogCoverage.Tests.CatalogCoverageIntegrationTests.TheInactivePluginsMountLateByKey",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-closed-generic-roots",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-unknown-recipe-refused",
          "check:contract-surface-parity"),
    ]),
    "P-010": R("Scope membership", [
        C("scopes form one rooted acyclic tree per world; a live target has exactly one OwnerScopeId and many "
          "targets can share it",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.RootScopeIsTheOnlyScopeWithoutAParent",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.CreatedScopeCarriesMembershipAndInheritsItsParentDepth",
          "unity:GameCore.Cards.Tests.CardsIntegrationTests.OwnershipHasOneOwnerPerDeclaredDomainAndEverySlotPolicyValidated"),
        C("a plugin is installed at one scope; a scope may have many installations",
          "dotnet:GameCore.Composition.Tests.DeclaredScopeTreeTests.ADeclaredTreeOpensTheCommittedCompositionWithEveryDeclaredScope",
          "probe:W4Gate/w4-extra-providers-mount-onto-the-same-revision"),
        C("descendant selection includes targets in the provider's own scope by default (SelfAndDescendants); "
          "DescendantsOnly is an explicit selector",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.ADescendantsRuleStillUsesTheModeGateForATargetInTheProvidersOwnScope",
          "dotnet:GameCore.Derivation.Tests.ReferenceCompositionTests.RefN01_AChapterBindsItsOwnBranchWithItsOwnDefinitions",
          "probe:Conformance/conformance/narrative/chapter-mount-and-future-descendant/world"),
        C("removing a nonempty scope requires an explicit ReparentTo destination or DestroySubtree disposition with "
          "all affected targets and installations in the plan; cross-world reparenting is invalid",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.RemovingANonemptyScopeRequiresAnExplicitSubtreeDisposition",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.AnEmptyScopeCanBeRemovedAfterItsMembersAreReparentedOut",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.ReparentCyclesAndCrossAncestryMovesAreRejected"),
        C("scope membership and parent changes are composition operations, never a side effect of moving a Transform",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.AVisualReparentDoesNotMoveComposition",
          "unity:GameCore.Gc019.Tests.Gc019IntegrationTests.TheVisualReparentLeavesCompositionUnchangedObservationPasses"),
    ]),
    "P-011": R("Service visibility", [
        C("service resolution is identical in both modes and a consumer declares each service dependency",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ServiceResolutionIsIdenticalInBothPropagationModes",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.DeclaredVersionRangeAcceptsBothEndpointsAndRejectsJustOutside"),
        C("a provider is private to its scope unless ExportToDescendants is set; world services are explicitly "
          "imported by manifest; a service-isolation boundary blocks ancestor providers for named contracts",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.PrivateProviderInAnAncestorIsInvisible",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ExportedProviderReachesDescendantsIncludingTheProvidersOwnScopeTargets",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ServiceIsolationBoundaryBlocksAncestorProvidersButNotBoundaryProviders"),
        C("the nearest visible provider wins only if its manifest declares OverrideAncestor; otherwise multiple "
          "visible single-binding providers are ServiceConflict; same-scope duplicate single bindings always conflict",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.NearestAncestorProviderWinsWhenItDeclaresTheOverride",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.TwoVisibleAncestorProvidersConflictWithoutAnOverride",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.SameScopeDuplicateSingleBindingsAlwaysConflict",
          "dotnet:GameCore.Composition.Tests.DiagnosticContractTests.AServiceConflictDiagnosticNamesTheOperationPhaseInvolvedProvidersAndRetry"),
        C("a MultiBinding contract returns all visible providers in registered stable order",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.MultiBindingReturnsEveryVisibleProviderInCanonicalOrder",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.AMixedSingleAndMultiDeclarationOfOneContractIsAConflict"),
        C("explicit provider selection is allowed inside the visibility boundary; sibling search and reflection "
          "fallback are forbidden; optional absence becomes a declared None/fallback; required dependency cycles "
          "reject the proposal",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ExplicitSelectionIsHonoredInsideTheBoundaryAndNeverFallsBack",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.SiblingScopesNeverSeeEachOthersProviders",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.SelfOnlyDomainNeverResolvesAnAncestorProvider",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.OptionalAbsenceIsExplicitAndRebindsToItsDeclaredFallback",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.RequiredDependencyCycleRejectsTheWholeProposal"),
    ]),
    "P-012": R("Dependency closure", [
        C("required service providers prepare before consumers and retire after consumers",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.RetirementRunsConsumersBeforeTheirProviders",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.RetirementRunsFromDependentsTowardDependencies"),
        C("loss of a required provider automatically makes affected consumers WaitingForDependencies and retracts "
          "their active contributions in the same plan; optional bindings rebind to their declared fallback",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.RemovingARequiredProviderMakesItsConsumerWaitInTheSamePlan",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.RequiredProviderLossRetractsTheWholeChainAndReturnReactivatesIt",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.AnOptionalSelectedDependencyRebindsToItsDeclaredFallbackWhenTheSelectedProviderLeaves",
          "probe:W4Gate/w4-required-provider-loss-makes-consumers-wait"),
        C("consumers resume automatically when valid dependencies return",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.ReturningTheProviderResumesTheConsumerThatWaited",
          "probe:W4Gate/w4-required-provider-return-resumes-consumers",
          "probe:W4Gate/w4-required-provider-loss-makes-consumers-wait"),
        C("a policy or migration failure in that closure rejects the entire proposal, retaining the old provider and "
          "old active assembly",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AValidatorRefusalRejectsTheSwitchAndKeepsTheOldModeAndRevision",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.AFailingMigrationRejectsItsInputWithoutWritingScratch",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.ARejectedPlanHasAnEmptyClosureDelta"),
        C("an existing provider that fails unexpectedly cannot be kept active: the kernel prompts a safe "
          "dependency-closure deactivation — the failed provider and everything depending on it leave the new "
          "assembly in one publication, with no invisible partial success",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.AnUnexpectedFailureOfALiveActivationIsPermittedAndRecordsItsReason",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.OnlyALiveActivationCanFailUnexpectedly",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.AFailedActivationCanOnlyRetryOrRetire",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.ReportingTheSameLiveFailureTwiceIsIdempotent",
          "dotnet:GameCore.Composition.Tests.InstallationLifecycleTests.EveryStatePairMatchesTheLifecycleDiagramExactly",
          "dotnet:GameCore.Composition.Tests.ProviderFailureTests.AnUnexpectedProviderFailurePublishesASafeDeactivationOfTheProviderAndItsDependents",
          "dotnet:GameCore.Composition.Tests.ProviderFailureTests.AProviderFailureReportIsHonestAboutTheRecordedCauseAndNeverClaimsAPartialPublication",
          "dotnet:GameCore.Composition.Tests.ProviderFailureTests.AProviderThatIsNotActiveCannotReportAnUnexpectedFailure",
          "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheNarrativeFamilyPinsTheProviderFailureDeactivation",
          "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheCardsFamilyPinsTheProviderFailureDeactivation",
          "probe:Faults/narrative/gc017-provider-failure-publishes-a-safe-deactivation",
          "probe:Faults/cards/gc017-provider-failure-publishes-a-safe-deactivation",
          note="implemented by GC-028: `InstallationStateMachine` gained the `Active -> Failed` edge (06 s1's "
               "diagram and prose record it), `ActivationLedger.FailActive` records the failure with a diagnostic, "
               "and `CompositionHost.FailActiveProvider` publishes the deactivation through the ordinary validated "
               "lane. A failed installation exposes no bindings, so its consumers move to WaitingForDependencies "
               "in that same publication."),
        C("the world stops admission and faults if that safe deactivation cannot publish; there is no invisible "
          "partial success and no timeout-based unsafe release",
          "dotnet:GameCore.Composition.Tests.ProviderFailureTests.ADeactivationThatCannotPublishLeavesTheOldAssemblyAndReportsTheRefusal",
          "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheNarrativeFamilyPinsTheProviderFailureThatFaultsTheWorld",
          "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheCardsFamilyPinsTheProviderFailureThatFaultsTheWorld",
          "unity:GameCore.Faults.Tests.FaultScenarioIntegrationTests.TheFrozenObservationTableMatchesBothDigestLiterals",
          "probe:Faults/narrative/gc017-provider-failure-that-cannot-publish-faults-the-world",
          "probe:Faults/cards/gc017-provider-failure-that-cannot-publish-faults-the-world",
          "note: the Unity observation asserts admission closed, no epoch or image published after the fault, the "
               "world Faulted with DiagnosticCode.ProviderFailed, PumpFrame refusing to pump, and a checkpoint "
               "restore into a new session through InitialDefinitionRecovery.Recover; the EditMode suite pins both "
               "new observation names per family so a rename or reorder fails rather than shrinking the table "
               "(P-008)."),
    ]),
    "P-013": R("Mode semantics", [
        C("PropagationMode is one world-level setting: Automatic (default) or Conservative",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.ModeSwitchPublishesOneSettingAndKeepsAutomaticAsTheDefault",
          "dotnet:GameCore.Composition.Tests.LaneSeedTests.AnInconsistentSeedIsRefused"),
        C("in Automatic, every rule with PropagationReach = Descendants applies to all eligible targets within its "
          "scope domain, including future targets, without target imports; LocalOnly rules never propagate",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.AutomaticGrantsEligibleDescendantsWithoutAnyImport",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.AutomaticAppliesToAFutureDescendantWithNoPerInstanceImport",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.LocalOnlyIsPermittedAtItsInstallationScopeInBothModesAndNeverReachesDescendants",
          "probe:W4Gate/w4-automatic-inheritance-and-the-future-target"),
        C("in Conservative such a descendant rule applies only with ExportToDescendants plus an explicit import, or "
          "with a full explicit TargetOptIn naming provider installation and capability",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.ConservativeDeniesADescendantWithoutExportImportOrOptIn",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.ConservativeGrantsAnExportedCapabilityImportedByTheTargetScope",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.ConservativeGrantsAnExportedCapabilityThatTheTargetImports",
          "dotnet:GameCore.Derivation.Tests.ReferenceMoveAndModeTests.ACompleteOptInKeepsItsBindingInConservative"),
        C("the mode gate also applies to same-scope targets selected by a Descendants rule's SelfAndDescendants "
          "selector; opt-in is not permission to bypass eligibility, isolation, exclusions, ownership or conflicts",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.ADescendantsRuleStillUsesTheModeGateForATargetInTheProvidersOwnScope",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.AnOptInCannotGrantAnIneligibleTarget",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.AnOptInOrImportCannotReopenABoundaryInEitherMode"),
        C("imports/opt-ins are data and remain stored when inactive; Automatic MUST NOT manufacture or persist them",
          "dotnet:GameCore.Derivation.Tests.ModeGrantTests.SwitchingModesChangesOnlyGatedRulesAndKeepsLocalOnlyContributionsAndStableState",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.ConservativeGrantDataIsStoredAndValidatedAgainstRealProviders",
          "dotnet:GameCore.Derivation.Tests.ReferenceCompositionTests.RefC01_SpawnSeatDUnderLeagueAReceivesTheSameDerivedContributionWithoutAnImport"),
        C("mode changes do not alter services, scheduling safety or gameplay rules",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ServiceResolutionIsIdenticalInBothPropagationModes",
          "probe:Gc013/narrative/gc013-mode-switch-automatic-to-conservative"),
    ]),
    "P-014": R("Mode transition", [
        C("switching mode is an ordinary atomic composition proposal against an expected revision",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.BothModeSwitchDirectionsPublishAndAreReported",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.StaleExpectedRevisionIsRejectedWithoutPublishing",
          "probe:W4Gate/w4-mode-automatic-to-conservative-retracts-existing-and-future"),
        C("recompute all affected inherited contributions, bindings, state dispositions and execution dependencies",
          "dotnet:GameCore.Derivation.Tests.IncrementalAgreementTests.AModeSwitchReportsAWholeWorldInvalidation",
          "dotnet:GameCore.Derivation.Tests.ReferenceMoveAndModeTests.BothModeDirectionsApplyToExistingAndFutureTargets",
          "probe:W4Gate/w4-mode-conservative-to-automatic-restores-existing-and-future"),
        C("a conflict, missing migration or exceeded budget rejects the switch and keeps the old mode/assembly",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AValidatorRefusalRejectsTheSwitchAndKeepsTheOldModeAndRevision",
          "dotnet:GameCore.Derivation.Tests.ReferenceMoveAndModeTests.AConflictOnTheOtherModeLeavesTheOldAssemblyPublished",
          "probe:Gc013/narrative/gc013-exclusive-conflict-preserves-mode-and-membership",
          "probe:Conformance/conformance/cards/exclusive-conflict-rejected"),
        C("a successful switch changes mode and derived assembly at the same publication; future descendants follow "
          "it immediately",
          "dotnet:GameCore.Derivation.Tests.ReferenceCompositionTests.RefC05_ModeSwitchBetweenAutomaticAndConservativeKeepsOnlyTheOptedInSeat",
          "probe:Gc013/narrative/gc013-future-target-in-conservative-derives-nothing",
          "probe:Conformance/conformance/cards/mode-directions/world"),
        C("a mode switch is not an invitation to request per-object confirmation; elapsed time never approves a "
          "larger budget",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ALargerBudgetIsAnExplicitConfigurationChangeThatSucceeds",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.CandidateQuotaExhaustionRejectsWithCountsAndCausesAndNoPartialClosure",
          "probe:Conformance/conformance/narrative/mode-conservative"),
    ]),
    "P-015": R("Eligibility", [
        C("targets advertise immutable, reusable compatibility descriptors from type schemas, prefab/template "
          "recipes, asset adapters, tags and explicit declarations",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.AnInvalidDescriptorRejectsThePlan",
          "dotnet:GameCore.Derivation.Tests.InvalidationLocalityTests.ADescriptorChangeOnOneTargetTouchesNoOtherTarget",
          "probe:Cards/cards-mount-reaches-existing-seats"),
        C("each rule specifies accepted contract/schema versions, scope reach and predicates over declared "
          "descriptor fields or lower-stratum capabilities",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.AnIneligibleSelectorVersionIsReportedWithItsOwnStatus",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AMissingLowerStratumPrerequisiteLeavesTheDependentRuleRejectedWithAWitness"),
        C("eligibility cannot depend on arbitrary live mutable ECS values, wall time or callbacks with side effects",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.ReplayingTheSameDerivationProducesTheSameResultHash",
          "dotnet:GameCore.Derivation.Tests.OracleAgreementTests.TheOracleAndTheEngineAgreeOnTheReferenceCompositions"),
        C("a gameplay state change can enqueue an explicit descriptor update for a later publication",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AnExclusionEditAndAnImportEditAreBothReportedAsScopeFacts",
          "dotnet:GameCore.Derivation.Tests.InvalidationLocalityTests.ADescriptorChangeOnOneTargetTouchesNoOtherTarget"),
        C("instance declarations are supported but MUST NOT be necessary for ordinary Automatic descendants of a "
          "compatible recipe; unrecognized targets remain unchanged and get an Ineligible explanation, not a guessed "
          "adapter",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.ATargetOutsideEveryCandidatePopulationGetsNoInventedExplanationButStaysUnchanged",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.AnIneligibleRecipeReceivesNothing",
          "probe:Conformance/conformance/cards/scoring-lifecycle/world",
          "probe:Cards/cards-future-seat-inherits-modifier"),
    ]),
    "P-016": R("Isolation and exclusions", [
        C("each scope has independent named ServiceIsolation and CapabilityIsolation sets; * means all contracts",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.IsolationIsStoredPerScopeAndVisibleInTheSnapshot",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.IsolationSetCannotNameContractsWhileDeclaringAll",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.AWildcardBoundaryBlocksEveryCapabilityBelowIt"),
        C("a capability boundary blocks outside rules on the boundary scope and its descendants; providers installed "
          "at or below it still work",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.ACapabilityBoundaryBlocksAnOutsideRuleForTheBoundarySubtree",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.AProviderInstalledInsideTheBoundaryStillWorks",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.ANamedBoundaryBlocksOnlyTheNamedCapability"),
        C("exclusions can target a capability, rule or provider on one target or scope subtree; denial along the "
          "propagation path wins over imports, opt-ins and selection overrides in both modes",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.ATargetExclusionOfOneCapabilityLeavesUnrelatedCapabilitiesAvailable",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.ARuleExclusionDeniesOnlyThatRule",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.AProviderExclusionOnAScopeSubtreeStopsOnlyThatProvidersContribution",
          "probe:Conformance/conformance/traversal/exclude-runner-a"),
        C("a descendant cannot reopen an ancestor boundary; the host must edit that boundary",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.AnOptInOrImportCannotReopenABoundaryInEitherMode",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.AnIsolationBoundaryIsRecordedAsTheOriginatingBlocker"),
        C("sibling branches do not inherit each other's providers",
          "dotnet:GameCore.Derivation.Tests.IsolationAndExclusionTests.ASiblingBranchIsNeitherBlockedNorGrantedByAnotherBranchesBoundary",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.SiblingScopesNeverSeeEachOthersProviders",
          "probe:Gc013/narrative/gc013-isolated-branch-unchanged-across-the-reparent"),
    ]),
    "P-017": R("Contribution identity and ownership", [
        C("each candidate has ContributionId = (PluginInstanceId, RuleId, TargetId, CapabilityId, OutputSlot), an "
          "immutable payload revision and provenance",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.ReconfiguringAPayloadChangesTheValueWhileTheContributionIdentitySurvives",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.TheIdenticalSupportIsNotASecondSupport",
          "probe:W4Gate/w4-extra-providers-mount-onto-the-same-revision"),
        C("its identity survives configuration changes; removed/recreated installations get a new generation",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.RemountingAStableInstanceIdentityAdvancesItsInstallationGeneration",
          "probe:Conformance/conformance/cards/reconfigure-festival"),
        C("a catalog CapabilityContract declares output slots, schema, composition policy and reducer version",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.CapabilitySlotWithoutCompositionPolicyRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.RuleOutputtingAnUndeclaredCapabilityRejects"),
        C("all candidates for a target/slot are composed before assembly",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.UnmountRetractsExactlyTheUnmountingProvidersSupport",
          "probe:W4Gate/w4-slot-reset-uses-the-manifest-permission"),
        C("support from multiple contributions is a set of IDs, not a boolean owned by the last plugin",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.RemovingOneOfTwoSupportsKeepsTheComponentActive",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.SupportedSlotsAreReportedInCanonicalOrder",
          "probe:W4Profile/w4-additive-multi-supporter-slot-in-a-live-world"),
        C("a plugin cannot remove another plugin's contribution or components it does not own",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.RetractingASupportTheSlotDoesNotHaveChangesNothing",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ASharedComponentRejectsRemovalWhileItsRecipeStillRequiresIt",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.ABaseRecipeRequirementKeepsTheComponentAfterTheFinalSupportLeaves"),
    ]),
    "P-018": R("Precedence", [
        C("eligible candidates sort by higher signed Priority, then nearer provider scope (greater depth), then "
          "ascending PluginInstanceId, RuleId and OutputSlot bytes",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.AHigherExplicitPriorityBeatsANearerProviderScope",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.EqualPriorityIsBrokenByTheNearerProviderScope",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.EqualPriorityAndDepthAreBrokenByAscendingProviderIdentity"),
        C("priorities are bounded 32-bit manifest values",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.EqualPriorityAndDepthAreBrokenByAscendingProviderIdentity",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.ALowerPriorityDeclarationCannotDisplaceAHigherPriorityEffectiveRow"),
        C("this total rank orders declared replacement/reduction choices; it does not silently resolve exclusive or "
          "incompatible conflicts",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.ExclusiveRejectsMoreThanOneCandidateEvenWhenRanksDiffer",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.ExclusiveWithTwoCandidatesRejectsAndNamesThem"),
        C("explicit target/scope overrides are versioned composition data; SelectProvider names an eligible candidate "
          "and is legal only for Replace or Exclusive; missing selection rejects the plan rather than falling back",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.AnExplicitSelectionOverrideChoosesTheEligibleCandidate",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.ASelectionOverrideForANonSelectablePolicyIsACatalogError",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.AnOverrideNamingAnIneligibleCandidateRejectsInsteadOfFallingBack",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.TwoOverridesNamingDifferentProvidersForOneSlotAreAmbiguous",
          "dotnet:GameCore.Derivation.Tests.PrecedenceAndSupportTests.AScopeAddressedOverrideAppliesToTheSubtreeWhenDeclaredSo"),
        C("explanations include losing candidates and rank components; neither node insertion order nor worker "
          "timing participates",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.ALosingCandidateIsProvenanceAndNeverSupport",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.ACompactRecordSetReconstructsWinnersLosersAndExclusions",
          "probe:Cards/cards-batch-envelope-resolves-one-winner"),
    ]),
    "P-019": R("Composition policies", [
        C("every output slot MUST choose exactly one policy; mixed policies for the same contract/slot are catalog errors",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.ReplaceChoosesTheHighestRankedCandidateAndKeepsTheLosersAsProvenance",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.CapabilitySlotWithoutCompositionPolicyRejects",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.EveryPolicyProducesTheSameResultUnderEveryInsertionPermutation"),
        C("Additive: all unique contributions folded in precedence order by the declared reducer, or canonical set "
          "union; the reducer validates overflow/range and an error rejects the whole proposal",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.AdditiveFoldsEveryContributionThroughTheRegisteredReducer",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.AdditiveWithoutAReducerProducesTheCanonicalSetUnion",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.AdditiveReducerOverflowRejectsTheWholeProposal",
          "unity:GameCore.W4Profile.Tests.W4ProfileIntegrationTests.TheAdditiveSlotCarriesItsComposedValueAndEverySupporter"),
        C("Replace: highest-ranked candidate unless a valid SelectProvider overrides it; lower-ranked candidates "
          "remain provenance, not active support",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.ReplaceChoosesTheHighestRankedCandidateAndKeepsTheLosersAsProvenance",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.HigherPriorityWinsAndTheLoserStaysProvenance"),
        C("Ordered: all candidates topologically sorted by declared before/after keys with precedence as ready-set "
          "tie-breaker; unknown required key or cycle rejects and optional key edges apply only when the endpoint exists",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.OrderedSortsByDeclaredKeys",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.OrderedRejectsARequiredKeyWithoutAnEndpoint",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.OrderedRejectsADeclaredCycleWithItsCandidatesAsWitnesses",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.OrderedDropsAnOptionalEdgeWhoseEndpointDoesNotExist",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.OrderedRejectsTwoCandidatesClaimingTheSameOrderingKey"),
        C("Exclusive: exactly zero or one candidate, or the explicitly selected eligible winner; more than one "
          "without selection rejects even if ranks differ, and losing providers remain inspectable",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.ExclusiveAcceptsTheExplicitlySelectedEligibleWinner",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.ExclusiveRejectsMoreThanOneCandidateEvenWhenRanksDiffer",
          "dotnet:GameCore.Derivation.Tests.ReferenceCompositionTests.RefC06_AnUnresolvedExclusiveConflictRejectsTheWholeProposalAndKeepsTheOldAssembly"),
        C("Incompatible: cross-capability incompatibility sets contain at most one active member; a conflict rejects, "
          "and priority cannot destroy an incompatible capability",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.IncompatibleAcceptsASingleActiveMember",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.IncompatibleRejectsASecondActiveMemberOfTheSet",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.CrossCapabilityIncompatibilityRejectsTheWholeProposalWhenBothWouldBeActive"),
        C("a reducer is pure, bounded, versioned and supplied by that contract's package; the kernel does not infer "
          "numerical semantics",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.TheRegisteredReducerReportsItsKeyAndNameAndDelegates",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AnUnregisteredPredicateOrReducerIsReportedRatherThanSubstituted"),
    ]),
    "P-020": R("Configuration and overrides", [
        C("contract fields explicitly use scalar replacement, canonical set union or a registered bounded reducer; "
          "arrays are ordered values unless specified otherwise",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.EveryFieldKindRoundTripsThroughTheCanonicalDocument",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.FieldMaskDistinguishesNullFromMissingAndUnionsSets"),
        C("schema defaults, inherited contributions and local configuration patches are separate provenance layers, "
          "in that order; a local patch has an explicit mask of fields; null/missing are distinct",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.FieldMaskDistinguishesNullFromMissingAndUnionsSets",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.MountStoresTheComposedEffectiveConfiguration",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.DeclaredConfigurationHashMustDescribeTheComposedDocument"),
        C("reconfiguration changes effective configuration only and MUST NOT reset mutable state or rewrite committed "
          "history except through an explicit registered migration/reset disposition",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ReconfigurationPreservesRuntimeState",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ReconfigurationPreservesTheRuntimeValueAndStagesNothing",
          "probe:Conformance/conformance/cards/reconfigure-festival",
          "probe:Cards/cards-future-seat-inherits-modifier"),
        C("no generic deep merge is permitted",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ConfigDocumentRejectsDuplicateFieldKeys",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.UndeclaredConfigurationFieldIsRejected"),
    ]),
}

# ---------------------------------------------------------------------------------------------------
# P-021..P-060, appended as separate entries of the same mapping (one dict, populated in two blocks so
# the file stays readable). `REQUIREMENTS` is the union of both blocks.
# ---------------------------------------------------------------------------------------------------
REQUIREMENTS.update({
    "P-021": R("Termination", [
        C("the catalog assigns capability contracts strata 0..31 and a rule may read static descriptors and "
          "capabilities in strictly lower strata only, with outputs occupying its declared stratum",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.CapabilityStratumOutsideTheDeclaredRangeRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.RuleReadingItsOwnStratumRejects",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AFiniteCapabilityChainTerminatesAtTheHighestStratum",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AMissingLowerStratumPrerequisiteLeavesTheDependentRuleRejectedWithAWitness"),
        C("rules cannot create targets, scopes, plugins, rules or live gameplay effects during derivation",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ASelfProducingRuleIsRejected",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AMutuallyDependentPairOfRulesIsRejectedAsACatalogError"),
        C("one rule emits at most the manifest's fixed bounded output slots per target and recursive terms/new IDs "
          "derived from prior output are forbidden",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ARuleThatWouldEmitMoreSlotsThanTheContractDeclaresIsRejected",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.EveryPolicyProducesTheSameResultUnderEveryInsertionPermutation"),
        C("each stratum executes once over canonical candidates with no unbounded fixed-point loop, and composition "
          "within a stratum cannot feed eligibility in the same stratum",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AFiniteCapabilityChainTerminatesAtTheHighestStratum",
          "dotnet:GameCore.Derivation.Tests.OracleAgreementTests.TheOracleAndTheEngineAgreeOnTheReferenceCompositions",
          "dotnet:GameCore.Derivation.Tests.OracleAgreementTests.TheRuntimeAndTheOracleAgreeOnRandomCompositionsWithoutAnyOperationSequence"),
    ]),
    "P-022": R("Budgets", [
        C("PropagationBudget limits examined candidates, emitted contributions, affected targets, temporary bytes, "
          "wall-clock preparation deadline and safe-point apply cost estimate, with the provisional reference limits",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.CandidateQuotaExhaustionRejectsWithCountsAndCausesAndNoPartialClosure",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ContributionQuotaExhaustionRejectsBeforeAnyPartialResult",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.TemporaryByteQuotaExhaustionRejects",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.PreparationDeadlineExhaustionRejectsWithTheElapsedValue",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ApplyCostEstimateExhaustionRejects",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.APlanBeyondTheConfiguredHardBudgetRejectsWithItsCounts"),
        C("planning can yield between bounded batches while the old epoch continues",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ARejectedProposalLeavesThePreviouslyPublishedResultUsable",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.ScratchReservesWithinItsBudgetAndRefusesBeyondIt"),
        C("exceeding any hard count/memory/deadline yields BudgetExceeded with top fan-out causes and no partial "
          "propagation publishes",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.CandidateQuotaExhaustionRejectsWithCountsAndCausesAndNoPartialClosure",
          "dotnet:GameCore.Planning.Tests.TargetSlotLedgerTests.AFullLedgerRejectsWithABudgetCodeRatherThanOverwriting",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.TheAssetTableIsBoundedByCountAndByBytes"),
        C("an apply-time overrun emits a metric but cannot safely abort halfway: finish or fault",
          "unity:GameCore.Benchmarks.Tests.BenchmarkWorkloadTests.TheElevenDeclaredIdsExistInReportOrder",
          "unity:GameCore.Benchmarks.Tests.PerformanceBudgetTests.AnUnproducedMeasurementIsNotMeasuredAndNeverAPass",
          "probe:Faults/narrative/gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image"),
        C("a caller may configure a larger limit, schedule a paused-world publication or split independent changes; "
          "the kernel never silently truncates eligible descendants",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ALargerBudgetIsAnExplicitConfigurationChangeThatSucceeds",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.ScratchReleasesInReverseReservationOrderAndReportsTheCount"),
    ]),
    "P-023": R("Incrementality", [
        C("the implementation MUST index scope ancestry/membership, descriptor-to-target matches, "
          "provider/rule-to-contributions, capability reverse dependencies, service consumers and state-slot support",
          "dotnet:GameCore.Derivation.Tests.InvalidationLocalityTests.ADescriptorChangeOnOneTargetTouchesNoOtherTarget",
          "dotnet:GameCore.Derivation.Tests.InvalidationLocalityTests.ABoundaryEditOnOneScopeLeavesSiblingsUntouched",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AnIsolationEditIsVisibleAsAChangeRatherThanAnEmptyDelta"),
        C("an edit invalidates its dependency closure; derivation examines indexed candidates and changed paths, not "
          "the entire tree on every logical/render step",
          "dotnet:GameCore.Derivation.Tests.InvalidationLocalityTests.ADescriptorChangeOnOneTargetTouchesNoOtherTarget",
          "dotnet:GameCore.Replay.Tests.ReplayDeterminismTests.UnchangedCompositionStepsCarryTheResultAndDoNoControlWork",
          "probe:Replay/replay-world-idle-pump-does-zero-control-work"),
        C("a world mode switch can invalidate the world; new targets, descriptor changes, boundary edits, service "
          "changes and moves all enter the same invalidation process",
          "dotnet:GameCore.Derivation.Tests.IncrementalAgreementTests.AModeSwitchReportsAWholeWorldInvalidation",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AScopeReparentReportsTheMoveAndItsNewParent",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AModeSwitchPublishesAWholeWorldChangeSet"),
        C("an independent full recomputation oracle MUST match the incremental effective assembly/provenance for "
          "randomized operation sequences",
          "dotnet:GameCore.Derivation.Tests.IncrementalAgreementTests.TheIncrementalEngineMatchesTheOracleOnEverySeedAndStep",
          "dotnet:GameCore.Derivation.Tests.IncrementalAgreementTests.TheIncrementalEngineAlsoAgreesWithAFullRecomputationOnExplanations",
          "dotnet:GameCore.Derivation.Tests.OracleAgreementTests.TheRuntimeAndTheOracleAgreeOnEverySeedAndStep",
          "dotnet:GameCore.Replay.Tests.DifferentialPropagationTests.TheSweepAgreesWithTheOracleOnEverySeedAndStep",
          "dotnet:GameCore.Benchmarks.Tests.BenchmarkFixtureDerivationTests.MountingOneUpdateProviderAffectsExactlyTheTagSelectedTargets(10000)",
          "probe:W7Gate/w7-incremental-derivation-matches-a-clean-derivation"),
        C("the indexes are derived data and rebuildable, not a second source of authoritative gameplay state",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.TheCacheIsBoundedAndItsEvictionIsObservable",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.AnEditOutsideTheTargetPathLeavesTheVariantReusable",
          "dotnet:GameCore.Replay.Tests.TelemetryBuildSwitchTests.ACarriedIncrementalDerivationDoesNoControlWork"),
    ]),
    "P-024": R("Spawn and despawn", [
        C("a precompiled SpawnRecipe contains base schema/layout, immutable definitions, descriptor, required asset "
          "leases, auxiliary entity recipe and derivation template",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.TheCompiledScheduleFollowsTheDescriptorOrderAndCarriesItsBuffers",
          "probe:Narrative/narrative-forward-provider-and-spawned-target",
          "probe:Cards/cards-future-seat-inherits-modifier"),
        C("cache derived variants by recipe revision, scope inheritance fingerprint, mode and catalog hash",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.AnUnchangedInheritanceIsAHitAndAChangedOneIsAStaleRecomputation",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.AModeSwitchAndACatalogChangeAlsoChangeTheFingerprint",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.ASpawnUnderANewScopeResolvesTheSameInheritanceAsItsSiblings",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.APayloadReconfigurationStalesTheVariant"),
        C("on spawn publication validate against the current composition revision; a stale variant is recomputed or "
          "rejected StalePlan, never published half-assembled",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.IsCurrentRejectsAVariantResolvedUnderAnotherFingerprint",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AFutureSpawnAppearsFullyAssembledInItsFirstVisibleEpoch",
          "probe:Conformance/conformance/cards/spawn-seat-d",
          "probe:Conformance/conformance/narrative/spawn-villager"),
        C("a target first becomes query-visible with its complete effective assembly and ownership bindings, with no "
          "frame requiring manual descendant wiring",
          "probe:W2Gate/gate2-derived-layout-in-entities",
          "probe:W3Gate/w3-automatic-existing-and-future-targets-in-both",
          "probe:Conformance/conformance/traversal/numeric-acceleration-sequence/world"),
        C("despawn retracts target contributions, closes its command route, fences users and destroys only "
          "recipe-owned entities/resources; already committed events keep stable IDs, not dangling handles",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.ADespawnedHandleIsRejectedForeverAndOnlyItsOwnStorageIsDestroyed",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.LiveTargetInsertionAndRetirementMatchAppendAndSortOracle",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.ReleaseInstanceReleasesOnlyThatInstancesEntries"),
    ]),
    "P-025": R("Reparenting and provider change", [
        C("a subtree move diffs the union of old and new ancestor rule/service sets plus descendant overrides and "
          "dependency closure",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AScopeReparentReportsTheMoveAndItsNewParent",
          "dotnet:GameCore.Derivation.Tests.ReferenceMoveAndModeTests.ReparentingTheVillageSelectsTheNewChaptersBindings",
          "probe:Conformance/conformance/cards/subtree-move/world"),
        C("membership, contributions, bindings and parent pointers publish together",
          "probe:W4Gate/w4-subtree-move-preserves-state-and-switches-binding",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.ReparentPreservesDescendantIdentityAndRecomputesDepth",
          "dotnet:GameCore.Derivation.Tests.ReferenceCompositionTests.RefC05_MovingASeatToTheQuietLeagueChangesTheBonusAndPreservesStableIdentity"),
        C("unrelated targets retain values and state; old inherited configuration is removed, new configuration is "
          "composed, and target-local state is preserved according to its slot policy",
          "probe:Gc013/narrative/gc013-reparent-preserves-target-state-and-inheritance",
          "probe:Gc013/narrative/gc013-isolated-branch-unchanged-across-the-reparent",
          "probe:Traversal/gc020-reparent-changes-the-contribution-and-keeps-state",
          "dotnet:GameCore.Derivation.Tests.ReferenceCompositionTests.RefN04_MovingVillageToChapterTwoRebindsTheVillagersAndKeepsTheirIdentity"),
        C("replacing a provider under the same installation identity increments activation epoch and revision; "
          "different identities require explicit replacement mapping if state is to transfer",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.AnOwnerTransferThatContradictsTheDeclaredPolicyRejects",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.AnExplicitOwnerTransferNeedsTheDeclaredTransferPolicy",
          "probe:Conformance/conformance/cards/provider-loss/world"),
        C("cross-world movement uses checkpoint/export plus a new world's spawn/restore contract, not this operation",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.ReparentCyclesAndCrossAncestryMovesAreRejected",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.ADocumentWithTwoRootScopesIsRefused",
          "probe:RecoverySmoke/recovery-smoke-restart-publishes-a-new-session"),
    ]),
    "P-026": R("Explanation", [
        C("Explain(TargetId, CapabilityId, SnapshotToken) returns matching and rejected rules, source scope path, "
          "descriptor evidence, exclusions/boundaries, mode gate, stratum, candidates, composition decisions, "
          "support IDs, resulting recipe hash and state disposition",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.AnExplanationNamesTheProviderRuleScopePathStratumAndRecipeHash",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.AnIneligibleSelectorVersionIsReportedWithItsOwnStatus",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.AnIsolationBoundaryIsRecordedAsTheOriginatingBlocker",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceExplanationReaderTests.ExplainAnswersTheFrozenSeamWithWinnersLosersAndKeys"),
        C("records may be interned/compacted but remain reconstructable for the retained epoch",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.ACompactRecordSetReconstructsWinnersLosersAndExclusions",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.EnumerationOrderCannotChangeTheRetainedFormOrItsDigest",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.RetentionDropsOldEpochsAndReportsTheExpiry",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.ARandomizedSmallOperationSequenceKeepsEveryExplanationReconstructable"),
        C("diagnostics report affected counts and invalidation reasons without retaining an unbounded full string "
          "tree per entity",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.TheCountersReportCostAndIndexVisitsWithoutUnboundedStrings",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.DeclaredBoundsRefuseInsteadOfTruncating",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceExplanationReaderTests.StagedPagesAreLabeledAndMatchTheirReconstruction"),
        C("CompositionPublished and CompositionRejected include operation ID, old/new revision, plan hash, counts "
          "and diagnostic keys",
          "dotnet:GameCore.Composition.Tests.Diagnostics.DiagnosticPrecedenceTests.FeedRecordsPublicationAndRejectionPayloadsUnderRetrievalKeys",
          "dotnet:GameCore.Composition.Tests.Diagnostics.DiagnosticPrecedenceTests.TypedFieldsDriveTheTotalOrder",
          "dotnet:GameCore.Composition.Tests.Diagnostics.DiagnosticPrecedenceTests.OrderIsStableWhenEverySummaryIsRewritten",
          "probe:Conformance/conformance/cards/trace-digest"),
    ]),
    "P-027": R("Change plans", [
        C("a proposal specifies expected CompositionRevision, catalog hash, operation ID and canonical input hash",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.TheSameInputsAlwaysProduceTheSameHash",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationInputHashIsDomainSeparatedFromEditInputs"),
        C("ChangePlan contains an immutable before/after composition diff, contribution delta, service binding "
          "delta, ownership map, compiled execution plan, structural operations, state dispositions, resource "
          "acquisitions/releases, invalidation set, budgets and a content hash",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.DescribeIsAStableEvidenceFormThatNamesTheOperation",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.ThePlanHashIsIndependentOfMountDeclarationOrder",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.TheReportIsCanonicalAndStableAcrossInputPermutations"),
        C("it has states Draft -> Validated -> Prepared -> Applying -> Published with alternatives Rejected, "
          "Cancelled or Faulted, and only one plan per world may enter Applying; concurrent proposals serialize by "
          "host admission sequence and never merge accidentally",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.TheLegalSequenceReachesPublishedWithBothOutcomes",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.ATerminalPlanRefusesEveryFurtherTransition",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.PublishingRequiresAPublishedOutcome",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.RejectionIsOnlyLegalBeforeLiveWritesAreClaimed",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.SeveralAdmittedProposalsPublishInAdmissionOrder",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.DirectPublicationCannotOvertakeAnEarlierProposal"),
        C("plan preparation can overlap simulation because it does not read live mutable state",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.TheBaseRecheckComparesRevisionAndEpochWithoutMutatingThePlan",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.StagedLeasesStayInertUntilPublication"),
    ]),
    "P-028": R("Validation", [
        C("before preparation validate catalog/version compatibility, acyclic scope/service/stage graphs, "
          "eligibility/strata, conflicts, support/removal policies, generated factories, asset availability "
          "contracts, state ownership, buffer producer/consumer contracts, budgets and expected revision",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.StageCycleRejectsAsCycle",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ABufferWhoseActiveProducersHaveNoConsumerRejects",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADeclaredRequiredCycleRejects",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.RemovingTheFinalSupportIsRejectedWhenTheDeclarationDoesNotPermitIt",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.AStaleExpectedRevisionRejectsWithoutInstallingAnything",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.AnInvalidDescriptorRejectsThePlan"),
        C("validation reports related diagnostics but does not guarantee allocations or native operations cannot "
          "fail later",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ADeclarationSetWithSeveralConflictsReportsTheCanonicallyFirstCode",
          "dotnet:GameCore.Composition.Tests.DiagnosticContractTests.EveryReachableRefusalCarriesItsCodePhaseOperationAndClassification"),
        C("at application recheck revision, world/lifecycle state, resource readiness and state-dependent migration "
          "preconditions; stale plans reject without mutation",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AStaleOrRepeatedCompositionPublicationIsRefusedBeforeAnyWrite",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.AFailedAcquisitionIsCountedAndLateAcquisitionIsTooLate",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.ACatalogFingerprintMismatchRefusesUnlessTheCallerRelaxesIt",
          "probe:Gc018/gc018-unknown-required-schema-rejects-restore"),
        C("a host may regenerate with a new operation ID and new expected revision",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.ConflictingReuseIsRejectedAndKeepsTheOriginalResult",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.AReusedKeyWithDifferentInputIsAnIdempotencyConflict",
          "probe:W1Gate/gate-operation-status-reports-fault-honestly"),
    ]),
    "P-029": R("Preparation", [
        C("allocate unpublished bindings, immutable blob/config tables, inert system instances where possible, "
          "resource leases and migration storage",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.StagedLeasesStayInertUntilPublication",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.AMigrationRunsOnTheCopyAndReportsItsValue"),
        C("registrations/subscriptions acquired here MUST remain gated and cannot emit gameplay commands until "
          "publication",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.StagedLeasesStayInertUntilPublication",
          "probe:Faults/narrative/gc017-acquisition-fault-releases-staged-leases"),
        C("the world may keep running its old assembly",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.APrewriteMigrationFailurePreservesTheOldAssemblyAndKeepsRunning",
          "probe:Faults/narrative/gc017-prewrite-migration-fault-preserves-live-state"),
        C("after admission closes and jobs drain, copy only state slots needed for migration into bounded scratch "
          "storage and run pure fallible migrations there before the first live write",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.ScratchReservesWithinItsBudgetAndRefusesBeyondIt",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ADeclaredVersionChangeRunsOnTheCopyInsideTheScratch",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.AFailingMigrationRejectsItsInputWithoutWritingScratch"),
        C("a preparation/migration failure releases staged leases in reverse dependency order and leaves the old "
          "assembly intact",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.FailedPreparationReleasesEarlierStagedAcquisitions",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.ReleasingStagedLeasesRunsInReverseOrderAndAggregatesFailures",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.QuarantinedLeasesAreReportedAsRetainedRatherThanReleased"),
        C("public callbacks with irreversible side effects are not allowed as preparation actions",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.ResourceLedgerRetiresInReverseOrderAndQuarantinesTheFailedRelease",
          "probe:Faults/narrative/gc017-cleanup-boundary-releases-what-a-refusal-staged"),
    ]),
    "P-030": R("Publication", [
        C("at an end-of-step or idle boundary close step and command admission temporarily, drain old world jobs "
          "and adapter readers, revalidate, stage migrations, apply structural/state changes, install new binding "
          "tables and execution graph, preinstall/validate new ingress gates closed, then construct the complete "
          "observation image and result record",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ANonCommittedBoundaryIsRefusedWithTooLateAndNothingIsRead",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.OnePublicationChangesEveryTargetInOneVisibleEpoch",
          "probe:W2Gate/gate2-one-bounded-command-committed",
          "probe:W5Gate/w5gate-one-world-with-retained-observation"),
        C("a nonthrowing serialized commit switches the published revision/epoch/mode/image and active gate table "
          "together and reopens admission, and no fallible plugin callback is allowed in the pointer-switch "
          "operation",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.TheCompositionAndPublishedSeriesAreOneAfterEveryPublication",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PublicationMovesRevisionAndEpochButNeverTheLogicalStep",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.WellFormedPlanProjectsOntoTheEpochBoundTable"),
        C("gate checks derive authority from that committed table",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.WellFormedPlanProjectsOntoTheEpochBoundTable",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.DispatchThroughTheSeamRejectsAStaleEpoch",
          "probe:W4Gate/w4-lane-epoch-equals-world-epoch-throughout"),
        C("no observer or system sees a mixture of old/new assembly",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AConcurrentObserverNeverSeesAMixedAssembly",
          "dotnet:GameCore.Execution.Tests.Observation.ConcurrentObservationTests.ConcurrentReadersAlwaysLeaseACompleteImageOfTheirOwnToken",
          "probe:W5Gate/w5gate-pinned-snapshots-are-read-only",
          "probe:Conformance/conformance/cards/mode-directions/setup-0"),
        C("delivery of postcommit notifications is separate and its failure cannot unpublish",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.ASubscriberFailureIsCountedAndCannotUndoTheCommit",
          "unity:GameCore.Unity.Runtime.Tests.Faults.GuardedDispatchFaultTests.AFailingOutputGroupDoesNotRewindTheCommittedStep",
          "probe:Faults/narrative/gc017-gate-installation-fault-stops-after-live-writes"),
    ]),
    "P-031": R("Apply failure", [
        C("if anything fails after the first live write (including ECB playback, allocation, native API or "
          "activation gate installation), the world enters Faulted, admission remains closed, no epoch or new "
          "snapshot is published and no simulation resumes",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.APostwriteFailureFaultsTheWorldWithoutPublishingOrResuming",
          "unity:GameCore.Unity.Runtime.Tests.Faults.GuardedDispatchFaultTests.AThrowingGuardedSystemStopsTheNextRegisteredStageAndPublishesNothing",
          "unity:GameCore.Unity.Runtime.Tests.Faults.FaultBoundaryTests.AnInjectedFirstLiveWriteFaultAndTheOriginalPostwriteSwitchBothFaultTheWorld",
          "probe:Faults/narrative/gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image",
          "probe:W1Gate/gate-thrown-postwrite-exception-fails-stop"),
        C("its last committed immutable snapshot is inspectable but its live ECS storage is unavailable except to "
          "controlled teardown/recovery",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.AFaultedWorldAcceptsNoFurtherWork",
          "probe:W1Gate/gate-faulted-world-refuses-admission",
          "probe:W5Gate/w5gate-postwrite-fault-fail-stops-the-world"),
        C("V1 does not implement a universal undo journal; recovery recreates a world from a checkpoint or initial "
          "definitions, and an external gameplay side effect cannot be erased by this procedure",
          "probe:Recovery/gc027-source-world-captures-and-publishes-a-verified-checkpoint",
          "unity:GameCore.Unity.Runtime.Tests.Recovery.InitialDefinitionRecoveryTests.RecoveryFromInitialDefinitionsCreatesAFreshIncarnationAndNeverResumesTheSource",
          "probe:Gc021/gc021-reward-obligation-is-durable-and-idempotent"),
        C("a partially applied plan is never retried against the same live storage",
          "probe:RecoverySmoke/recovery-smoke-refuses-a-live-source",
          "probe:Recovery/gc027-transient-failure-is-retried-under-the-host-bound",
          "dotnet:GameCore.Recovery.Fixtures.Tests.RecoveryPolicyTests.ACorrectnessFailureNeverAllowsARetryWithAttemptsLeft"),
    ]),
    "P-032": R("State dispositions", [
        C("every state slot identifies (TargetId, OwnerId, SlotId), schema/version, initialization policy, "
          "configuration-update policy, owner-transfer policy and last-support-loss policy",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ACompleteDeclarationWithNoMigrationExecutorValidates",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ADeclarationThatClaimsTheWrongOwnerForKeyRejects",
          "dotnet:GameCore.Contracts.Tests.CheckpointRecordRoundTripTests.SlotRecordRoundTripsItsStateSlotKeyAndDormantFlag"),
        C("existing state defaults to Preserve when the owner/schema stays compatible; a missing compatible policy "
          "is a validation error, not implicit zero initialization",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.AnUndeclaredSlotIsNeverImplicitlyInitialized",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ALostSupportPolicyMustMatchTheDeclaration",
          "probe:W4Gate/w4-slot-preserve-keeps-the-non-default-value"),
        C("a version change uses a registered Migrate function; Reset requires an explicit manifest-supported "
          "proposal field and reason",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.AVersionChangeWithoutARegisteredMigrationIsMigrationRequired",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.AManifestSupportedResetProducesAResettablePolicy",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.AManifestWithoutResetSupportRefusesAnExecutedReset",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.APermittedResetWithoutAReasonIsRefused",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.AManifestSupportedResetWithoutAReasonIsADeclarationError",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ADeclaredResetWithAReasonWritesTheDeclaredInitialValue",
          "probe:W4Gate/w4-slot-reset-uses-the-manifest-permission"),
        C("last-support loss MUST declare one of RemoveDerived, PreserveDormant or TransferTo a named available "
          "owner with a migration",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.TheLastSupportLossFollowsTheDeclaredPolicyExactly",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.RemovingTheLastSupportOfDerivedStateRetractsIt",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.TheLastSupportLossOfDurableStateRetainsItDormant",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.TheLastSupportLossOfATransferableSlotNamesItsDestination",
          "probe:W4Gate/w4-slot-preserve-dormant-retains-without-an-active-writer",
          "probe:W4Gate/w4-slot-remove-derived-drops-the-row",
          "probe:W4Gate/w4-slot-transfer-to-moves-the-value-to-the-named-owner"),
    ]),
    "P-033": R("Shared support and component lifetime", [
        C("derived component existence is governed by the union of effective support sets and the slot policy; "
          "removing one provider removes exactly its support",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.TheFinalSupportLossFollowsTheDeclaredPolicy",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.RemovingOneOfTwoSupportsKeepsTheOtherSupportAndTheValue",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.UnmountRetractsExactlyTheUnmountingProvidersSupport"),
        C("final removal is allowed only if no other capability/recipe requires the component; base recipe "
          "components are not owned by propagated providers",
          "dotnet:GameCore.Planning.Tests.Ownership.SupportSetRegistryTests.ABaseRecipeRequirementKeepsTheComponentAfterTheFinalSupportLeaves",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ASharedComponentRejectsRemovalWhileItsRecipeStillRequiresIt",
          "unity:GameCore.W4Profile.Tests.W4ProfileIntegrationTests.TheAdditiveSlotCarriesItsComposedValueAndEverySupporter"),
        C("where one ECS component implements several slots, the generated layout declares a single physical owner "
          "and a field mapping, or splits storage",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.OneLayoutImplementingSeveralSlotsIsASharedComponentWithOneOwner",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.TwoLayoutsOfOneSchemaAreRecordedAsSplitStorage",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.AValidLayoutResolvesThePhysicalOwnerAndTheFieldSlot"),
        C("competing arbitrary component initializers are rejected",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.TwoPhysicalOwnersOfOneComponentReject",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.TwoSlotsClaimingOneFieldReject",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.OneLayoutDeclaringOneFieldTwiceRejects"),
        C("runtime state is not recomputed from defaults on every assembly diff",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ReconfigurationPreservesRuntimeState",
          "probe:Cards/cards-duplicate-command-transfers-once",
          "probe:Conformance/conformance/cards/reconfigure-festival"),
    ]),
    "P-034": R("State authority", [
        C("each authoritative state domain has one OwnerId and its registered systems may write only their declared "
          "slots/components and partitions",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADeclaredWriterWithoutAnOwnerRejects",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.TwoOwnersWithDifferentDomainsAreLegal",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.ASlotOwnerDisagreeingWithThePhysicalOwnerRejects"),
        C("multiple systems under that owner need dependency order or provably disjoint generated partitions; V1 "
          "accepts disjointness only from validated, mutually exclusive PartitionId assignments, not arbitrary "
          "query-predicate claims",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADeclaredRequiredEdgeOrdersTwoWritersOfOneDomain",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.TwoValidatedDisjointPartitionsOfOneOwnerAreLegal",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.TwoDifferentDeclaredPartitionsAreProvablyDisjoint",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.TwoWholeSchemaWritersAreNotProvablyDisjoint"),
        C("overlapping writers from different owners reject assembly",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.TwoUndeclaredWritersOfOneDomainReject",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.OverlappingWritersWithoutOrderOrPartitionRejectAsAmbiguous",
          "dotnet:GameCore.Planning.Tests.Ownership.PartitionAndComponentOwnershipTests.TheSameDeclaredPartitionIsAnOverlap"),
        C("an explicit ownership transfer of a state slot is representable and validated",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.AnExplicitOwnerTransferNeedsTheDeclaredTransferPolicy",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ATransferDestinationThatAnotherOwnerAlreadyHoldsIsRefused",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ATransferToAnUndeclaredOwnerIsRefused",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.ATransferToTheSameOwnerIsRefused",
          "note: the transfer expression V1 provides is the state-slot OwnerTransferValidator (P-025/P-032); the "
                  "writer-domain validator has no transfer input, so a cross-owner writer overlap always rejects. "
                  "The normative 'unless ownership is explicitly transferred' is a permission, and the obligation "
                  "it qualifies is the rejection covered by the clause above.",
          ),
        C("DI services expose read snapshots and commands, not independent mutable mirrors of ECS state; an "
          "engine-owned physical domain is declared as external authority, its ECS data is stamped observation, and "
          "ECS submits intent through that adapter",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.AnExternallyOwnedDomainHasOneOwnerAndRefusesGameplayWrites",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.AWorldWithNoExternalDomainNeedsNoPhysicsAdapter",
          "unity:GameCore.Gc019.Tests.Gc019IntegrationTests.TheExternalAuthorityAbsenceObservationPasses",
          "probe:Traversal/gc020-externally-owned-pose-is-not-integrated",
          "probe:Gc019/gc019-does-not-declare-external-authority"),
    ]),
})

# ---------------------------------------------------------------------------------------------------
# P-035..P-060.
# ---------------------------------------------------------------------------------------------------
REQUIREMENTS.update({
    "P-035": R("World lifecycle", [
        C("a world is Created -> Running <-> Paused -> Stopping -> Disposed, with Faulted reachable from any "
          "nonterminal state and recoverable only through recreation",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.InvalidLifecycleRequestsAreRejectedWithoutMutation",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.StopClosesIngressSettlesJobsAndDisposesStorage",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.AFaultedWorldAcceptsNoFurtherWork",
          "probe:WorldDispatch/world-guarded-fail-stop",
          "probe:W1Gate/gate-faulted-world-refuses-admission"),
        C("created worlds become Running only after an initial validated assembly publication",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.CreationPublishesEpochOneAndStepZero",
          "probe:W1Gate/gate-two-owned-worlds"),
        C("O-26 pauses/resumes at a committed boundary; paused worlds accept bounded queued commands and "
          "composition proposals but do not advance simulation; pause/resume changes the separately queried host "
          "lifecycle status, not AssemblyEpoch or LogicalStepId",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.PauseChangesHostStatusOnlyAndAddsNoDebt",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepPauseAddsNoDebtAndPreservesTheAccumulator",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.AFullPendingQueueBackpressuresInsteadOfDroppingInput",
          "probe:W4Gate/w4-lane-epoch-equals-world-epoch-throughout"),
        C("world identity scopes handles, commands, jobs, services, event cursors and resources",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.ForeignWorldOperationCannotMutateThisScopeTree",
          "dotnet:GameCore.Execution.Tests.Observation.SnapshotRetentionTests.ForeignWorldTokensAreRefusedAsValuesAndAtPublication",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.ARequestOfAnotherWorldIncarnationIsRefused",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.UnknownResourcesAndJobsAreRefusedInsteadOfInvented"),
        C("world disposal is not scene unload; scene adapters explicitly request the applicable operations",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.DestroyingAViewDestroysOnlyTheView",
          "probe:Gc019/gc019-view-destruction-leaves-gameplay-intact",
          "probe:Gc019/gc019-adapter-teardown-participates-in-lifecycle"),
    ]),
    "P-036": R("Temporal models", [
        C("a world chooses FixedStep or CommandDriven at creation; changing it requires checkpoint/recreation in V1",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.InvalidLifecycleRequestsAreRejectedWithoutMutation",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FactoriesRejectAnInvalidConfiguration",
          "probe:WorldDispatch/world-fixed-step-debt-and-clock"),
        C("FixedStep configuration declares positive step duration, a scaled/unscaled host clock source and a "
          "catch-up limit; the host admits at most the limit per pump, retains remaining debt and emits TimeDebt, "
          "never silently increasing step duration or discarding debt",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepAdmitsWholeStepsAndRetainsTheRemainder",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepCatchUpIsBoundedAndDebtIsNeverDropped",
          "unity:GameCore.Unity.Runtime.Tests.TemporalDriverTests.FixedStepHostBoundsCatchUpAndRetainsTheUnspentRemainder",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.FixedStepRetainsDebtAcrossFramesAndBoundsCatchUp",
          "probe:Traversal/gc020-one-simulation-per-admitted-step"),
        C("pause freezes simulation accumulation",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepPauseAddsNoDebtAndPreservesTheAccumulator",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.TimeDebtRefusesToBecomeNegativeOrWrap"),
        C("CommandDriven advances only for accepted commands or explicit registered WakeRequests; no inputs/wakes "
          "means zero logical steps",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.CommandDrivenWorldExecutesZeroStepsWithoutDemand",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.CommandDrivenIdleExecutesZeroStepsOverManyFrames",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.WakingACommandDrivenWorldAdvancesWithoutAPlayerCommand",
          "probe:WorldDispatch/world-command-driven-idle-zero-steps"),
        C("rendering, host callbacks, resource cleanup and composition processing may continue independently, and "
          "no kernel rate, turn or physics phase is prescribed",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.NoScheduleRequiresACombatPhysicsOrAnimationStage",
          "probe:Traversal/gc020-presentation-rate-does-not-double-advance",
          "probe:W4Profile/w4-both-families-mount-late-and-leave-no-world"),
    ]),
    "P-037": R("Step admission", [
        C("an input cutoff seals each step's batch with a monotonic host-assigned admission sequence",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.TheInputSealIsMonotonicAndCarriesTheAdmittedPrefix",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.ACommandArrivingAfterTheCutoffWaitsForTheNextStep",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.OneAdmittedCommandAdvancesExactlyOneStepAndConsumesItsSeal"),
        C("CommandDriven admits one pending command or one wake per logical step by default; a domain can declare an "
          "atomic batch envelope as one command; FixedStep consumes the sealed prefix up to its configured capacity "
          "and retains the remainder",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.CommandDrivenWorldAdmitsDemandUpToItsPerPumpLimit",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepAdmitsWholeStepsAndRetainsTheRemainder",
          "probe:Cards/cards-batch-envelope-resolves-one-winner"),
        C("duplicate request keys return their recorded result, not another execution; a domain rejection still "
          "commits an observable rejected command result in a logical step",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.ADuplicateRequestKeyReturnsItsRecordedResult",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ADuplicateCommandReturnsItsRecordedResultAndExecutesOnce",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.TerminalResultsDistinguishCommitmentFromRejectionAndCancellation",
          "probe:Cards/cards-rejected-settlement-changes-nothing"),
        C("composition publishes before the next input seal; queued inputs are revalidated against current "
          "bindings, schema, authority and target liveness, without retaining stale component pointers",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.AnUnconsumedNextStepQueueCarriesForwardWithTerminalCancellations",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ExpectedDomainVersionRefusesStaleCommandsAndChangesIdempotencyIdentity",
          "probe:Gc018/gc018-queued-commands-are-dispositioned-not-omitted",
          "note: the terminal outcome of a carried or retired row is `Cancelled(RouteRetired)` per P-047; the "
                  "`Rejected` outcome covers an input refused by admission-time revalidation. The two sentences are "
                  "reconciled in 00 P-037 (GC-028) and both halves are asserted by the two refusals above."),
        C("terminally invalid inputs finish as Rejected without an empty CommandDriven step",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ACommandThatOverflowsItsIngressLaneIsRefusedBeforeTheStep",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.CapacityBackpressureRecordsNothing"),
    ]),
    "P-038": R("Clocks", [
        C("LogicalStepId, fixed-step simulation duration, monotonic host timestamps and plugin-local clocks are "
          "distinct",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.VersionDomainsUseTypedCounters",
          "unity:GameCore.Unity.Runtime.Tests.TemporalDriverTests.FixedStepHostSuppliesTheLogicalClockToWorldTime",
          "dotnet:GameCore.Replay.Tests.TelemetrySchemaTests.DurationsConvertHostTicksWithoutReadingAClock"),
        C("CommandDriven has no implicit elapsed simulation seconds; its commands can explicitly advance domain time",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.CommandDrivenWorldExecutesZeroStepsWithoutDemand",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.ADomainClockWakeFiresOnlyWhenTheDomainAdvancesItsClock",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.AFixedDurationClockAdvancesOnlyByCommittedStepTime"),
        C("a plugin local clock registers how it advances, pauses and persists and can schedule a typed wake; "
          "ordinary wall-clock reads are unavailable inside repeatable rule functions",
          "dotnet:GameCore.Contracts.Tests.CheckpointRecordRoundTripTests.ClockDeclarationRoundTripsItsDeclarationFields",
          "dotnet:GameCore.Contracts.Tests.CheckpointRecordRoundTripTests.ClockWakeRoundTripsItsWakeIdentityAndPayloadSchema",
          "dotnet:GameCore.Contracts.Tests.CheckpointIdentityTableTests.ClockDeclarationsAreIndexedButWakeRowsAreNot",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.PauseAppliesEachClocksDeclaredWakePolicyAndAddsNoDebt"),
        C("wake sources have a finite queue/budget and an explicit clock type; pausing cancels/defers wakes "
          "according to the declared clock policy; no timer callback mutates ECS directly",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.ARegisteredWakeAdvancesACommandDrivenWorldWithoutAPlayerCommand",
          "probe:Replay/replay-world-wake-commits-one-step-and-samples-durations",
          "probe:W2Gate/gate2-registered-wake-advances-one-step"),
    ]),
    "P-039": R("Stage registration", [
        C("a stage declares namespaced StageId, version, owner package, system factory keys, activation membership, "
          "Before/After dependencies (required or optional), read/write sets, buffer ports and host affinity",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.TheContractShapedPlanCarriesStageNodesSystemNodesAndTheSemanticHash",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.DeclaredEdgesAndBufferEdgesProduceOneStableTopologicalOrder",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ABufferBindsItsProducersAndConsumerCanonically"),
        C("each system entry declares its own access sets and explicit system-key dependencies within its stage",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.AnExplicitInnerEdgeOrdersTwoOverlappingSystems",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.EverySystemCarriesItsStageFenceIndexAndItsStagePredecessors",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.SystemWithoutAnAccessSetRejects"),
        C("shared stage declarations coalesce only if their contract/version matches; system instances have their "
          "own unique keys and declared world/per-partition multiplicity; a world owns at most one system instance "
          "per key",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.TwoCompatibleStageDeclarationsCoalesceIntoOneStage",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.AnIdenticalSystemDeclarationInTwoCoalescedStagesIsMergedOnce",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.TwoConflictingDeclarationsOfOneSystemKeyReject",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.CoalescedDeclarationsMustAgreeOnVersionOwnerAndAffinity"),
        C("missing required stages/systems fail; optional edges disappear when absent",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.OptionalStageEdgesDisappearWhileRequiredOnesRejectWhenAbsent",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.RequiredInnerSystemEdgesMustNameASystemOfThatStage",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.TwoConflictingDeclarationsOfOneSystemKeyRejectEvenWhenTheirAccessSetIsIdentical"),
        C("the kernel's boundaries BeginStep and PublishStep are synchronization mechanisms and not game phases",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.EmptyPlanIsAValidNoOp",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.NoScheduleRequiresACombatPhysicsOrAnimationStage",
          "probe:Traversal/gc020-cards-and-narrative-declare-no-action-phase"),
    ]),
    "P-040": R("Execution plan", [
        C("compile stage edges, each stage's internal system DAG, buffer producer-before-consumer edges, structural "
          "playback boundaries and explicit owner ordering into a DAG",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.DeclaredEdgesAndBufferEdgesProduceOneStableTopologicalOrder",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ABufferBindsItsProducersAndConsumerCanonically",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.AnEmptyDeclarationSetCompilesToAnEmptyValidSchedule",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.IndependentStagesAreOrderedByCanonicalStageIdBytes"),
        C("stage incoming dependencies precede all its root system entries and its outgoing completion follows all "
          "leaves; both levels and their access sets enter validation and the plan hash",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.EverySystemCarriesItsStageFenceIndexAndItsStagePredecessors",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ChangingOnlyAnAccessSetChangesThePlanHash"),
        C("independent ready nodes are ordered by ascending StageId then system key for trace stability and may "
          "execute concurrently",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.IndependentSystemsInsideOneStageAreOrderedByCanonicalKeyBytes",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ShuffledDeclarationOrderCompilesToTheSameOrderAndHash",
          "probe:W2Gate/gate2-ordered-dispatch-and-dependent-read"),
        C("overlapping read/write accesses require a directed path in that expanded DAG or validated disjoint "
          "partitions, including systems coalesced into one stage",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.TwoWritersInsideOneStageWithoutAnEdgeReject",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ValidDisjointPartitionsMayOverlapWithoutAnEdge",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.AnUnpartitionedWriterConflictsWithAPartitionedOneOnTheSameSchema"),
        C("a write/write or read/write conflict without a semantic edge is AmbiguousOrder; the scheduler MUST NOT "
          "invent gameplay order",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.UnorderedWriterConflictRejectsWithBothDeclarationsAsWitness",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ReadOnlyOverlapBetweenUnorderedSystemsIsLegal",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.OverlappingWritersWithoutOrderOrPartitionRejectAsAmbiguous"),
    ]),
    "P-041": R("Concurrency and structural work", [
        C("within a stage, systems can update owned components directly and schedule jobs",
          "unity:GameCore.Unity.Runtime.Tests.Time.ScheduleExecutionTests.NeitherDisjointJobReceivesTheOthersHandleAndBothWritesSurvive",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADirectOwnerUpdateOfAnExistingComponentNeedsNoQueueOrPartition",
          "probe:Traversal/gc020-one-admitted-step-integrates-once"),
        C("the execution driver tracks all handles, including jobs using native containers not tracked by ECS "
          "component safety, and no detached job may retain world memory",
          "unity:GameCore.Unity.Runtime.Tests.Time.NativeDependencyTests.TheStepFenceCarriesTheNonComponentHandle",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.UnfinishedJobsStayTrackedAndQuarantined",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.PendingJobsStayTrackedUntilTeardownCompletesThem"),
        C("structural operations record into a declared stage-local deferred buffer and play back after its "
          "producers finish and before dependent readers",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.DeferredStructuralPlaybackIsCanonicalAndWaitsForItsProducer",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.ADeclaredDeferredBufferRefusesAnUndeclaredProducerAndOverflow",
          "probe:W2Gate/gate2-one-bounded-command-committed"),
        C("parallel playback keys use stable target/request order when order is semantically relevant",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.TheConsumingOwnerTakesTheSealedBatchInCanonicalOrder",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.LiveTargetInsertionAndRetirementMatchAppendAndSortOracle"),
        C("an ECB/deferred structural buffer is not a gameplay transaction and is not a reason to route all writes "
          "through a global queue",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADirectOwnerUpdateOfAnExistingComponentNeedsNoQueueOrPartition",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.SetScoreDeltaAddsTheEffectiveBonusAndNeverWraps",
          "note: P-044 states the same limit from the commit side; the cards suite is the full-domain statement that "
                  "the transfer is atomic because its owner says so, not because of ECB playback."),
    ]),
    "P-042": R("Commands and requests", [
        C("an external CommandEnvelope carries request key (WorldId, IssuerId, IssuerSequence), target stable IDs, "
          "payload schema/revision, expected domain version if needed and an admission result token",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.AFreshAdmissionIsAcceptedAndNotYetCommitted",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.AMessageForAnotherOwnerOrProducerIsRefused",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ExpectedDomainVersionRefusesStaleCommandsAndChangesIdempotencyIdentity"),
        C("the host validates envelope/route/capacity and the state owner validates gameplay",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ACommandThatOverflowsItsIngressLaneIsRefusedBeforeTheStep",
          "probe:Cards/cards-one-command-commits-both-sides"),
        C("a Request is a typed transient inter-system message routed to a named owner and consumer step/stage, not "
          "a committed fact; within an active stage owner-internal computation may use direct writes instead of "
          "requests, and cross-owner mutation requires a request or explicit transfer contract",
          "dotnet:GameCore.Execution.Tests.CommandPayloadReaderTests.ARegisteredReaderDecodesItsSchemaAndAMissIsReported",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.AReaderOfAnotherOwnerIsNotAWriter",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.AMessageForAnotherOwnerOrProducerIsRefused"),
        C("RequestResult is Accepted, Rejected, Cancelled or Committed with reason and optional committed event "
          "cursor",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.TerminalResultsDistinguishCommitmentFromRejectionAndCancellation",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.TheCommittedCursorIsAttachedAfterPublication",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.AdmissionAcceptanceIsDistinguishableFromGameplayCommitment"),
        C("admission acceptance is not gameplay success",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.AFreshAdmissionIsAcceptedAndNotYetCommitted",
          "probe:Cards/cards-rejected-settlement-changes-nothing",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.CompositionHostDoubleAdmitsStaleRejectsRetainsAndExpires"),
    ]),
    "P-043": R("Buffers and bounded work", [
        C("each buffer contract declares schema, lifetime (Stage, Step or bounded NextStep), producer keys, exactly "
          "one consuming owner, consumption stage, order key, capacity, overflow behavior and drain/cancel policy",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.ADeclaredLifetimePairAndDuplicateIdentityReject",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ABufferBindsItsProducersAndConsumerCanonically",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ChangingOnlyABufferLifetimeOrCapacityChangesThePlanHash",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.BufferWithoutAPositiveCapacityRejects"),
        C("duplicate producers are legal when registered; fan-out uses explicit immutable read ports",
          "dotnet:GameCore.Execution.Tests.ReadPortConformanceTests.SeveralReadersMayFanOutFromOneBufferWithoutBecomingItsOwner",
          "dotnet:GameCore.Execution.Tests.ReadPortConformanceTests.AReadPortForADeclaredBufferIsAdmitted",
          "dotnet:GameCore.Execution.Tests.ReadPortConformanceTests.AReadPortForAnUndeclaredBufferIsRefusedAndNamesIt",
          "dotnet:GameCore.Planning.Scheduling.Tests.BufferProducerConformanceTests.SeveralRegisteredProducersBindToTheOneConsumerInCanonicalOrder",
          "dotnet:GameCore.Planning.Scheduling.Tests.BufferProducerConformanceTests.DeclarationOrderOfSeveralProducersChangesNeitherTheBindingNorTheHash"),
        C("required gameplay input overflow rejects the affected command/batch before mutation; no silent drop",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.AFullReliableBufferRefusesWithoutMutatingItself",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.OverflowRejectsBeforeMutationAndLeavesStateUnchanged",
          "probe:Gc021/gc021-capacity-exhaustion-is-never-a-silent-drop"),
        C("diagnostic/presentation buffers may use explicit lossy policies with counters",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.ALossyBufferDropsAndCountsInsteadOfRefusing"),
        C("no consumer reads a buffer before producer completion; unconsumed step buffers fail commit validation",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.DeferredStructuralPlaybackIsCanonicalAndWaitsForItsProducer",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.UndrainedReliableRowsFailTheCommitValidation",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.AReliableBufferThatCannotBeDrainedFaultsInsteadOfPublishing",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.ProducerWithoutItsConsumerStageFailsDrainValidation"),
        C("bounded next-step queues retain stamped stable references and are revalidated after composition changes",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.AnUnconsumedNextStepQueueCarriesForwardWithTerminalCancellations",
          "probe:W3Gate/w3-card-domain-transfer-committed-atomically"),
        C("cycles between stages/ports require explicit next-step scheduling",
          "dotnet:GameCore.Planning.Scheduling.Tests.BufferProducerConformanceTests.ABufferEdgeThatClosesAStageCycleIsRejectedAsACycle",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.AStageThatDependsOnItselfIsRejectedAsACycle"),
        C("within its declared stage a domain owner may use a finite worklist/reaction loop with explicit ordering, "
          "reserved capacity, maximum iterations and prewrite overflow rejection, and it cannot dynamically reenter "
          "the stage graph; the kernel neither mandates nor bans bounded same-step domain computation",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.ContestResolutionIsIndependentOfCandidateOrder",
          "dotnet:GameCore.Rules.Traversal.Tests.TraversalAccelerationTests.AnOverflowingFoldIsRefusedWithAReason",
          "probe:Traversal/gc020-one-simulation-per-admitted-step"),
    ]),
    "P-044": R("Commit and domain transactions", [
        C("a plugin may implement multi-entity atomic decisions by gathering intents, validating versions/resources, "
          "computing a bounded write set and applying it under its single owner before emitting tentative internal "
          "receipts",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.TransferCommitsRemoveThenAddThenAdvance",
          "probe:Cards/cards-transfer-commits-both-sides",
          "probe:Conformance/conformance/cards/scoring-lifecycle/world"),
        C("the kernel does not infer rollback, fairness, economic conservation, combat ordering or atomicity from "
          "ECBs",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.SetScoreDeltaAddsTheEffectiveBonusAndNeverWraps",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADirectOwnerUpdateOfAnExistingComponentNeedsNoQueueOrPartition",
          "probe:Cards/cards-rejected-settlement-changes-nothing"),
        C("a successful logical step completes all required work, validates required drains, prepares committed "
          "events and a consistent snapshot, then nonthrowingly advances LogicalStepId and exposes output together",
          "dotnet:GameCore.Execution.Tests.CommittedEventStoreTests.OneBuildProducesCanonicallyOrderedEventsForTheCommittedStep",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.PublishingOneStepExposesExactlyOneImage",
          "probe:W3Gate/w3-narrative-state-change-through-committed-snapshot"),
        C("if unexpected failure follows authoritative writes before that publication, fault the world; do not "
          "retry the half-step",
          "unity:GameCore.Unity.Runtime.Tests.Faults.GuardedDispatchFaultTests.AThrowingGuardedSystemStopsTheNextRegisteredStageAndPublishesNothing",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.ManagedSystemThatWritesThenThrowsStopsTheStepAndFaultsTheWorld",
          "probe:Faults/narrative/gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image"),
        C("postpublication subscriber-delivery errors do not undo the commit",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.ASubscriberFailureIsCountedAndCannotUndoTheCommit",
          "unity:GameCore.Unity.Runtime.Tests.Faults.GuardedDispatchFaultTests.AFailingOutputGroupDoesNotRewindTheCommittedStep",
          "probe:Gc019/gc019-input-completion-from-a-retired-activation-is-discarded"),
    ]),
    "P-045": R("Observation and external output", [
        C("observers see immutable images at (epoch, step) publication only and cannot use inspection to obtain "
          "writable ECS references",
          "dotnet:GameCore.Execution.Tests.Observation.WorldObservationTests.ABoundaryLeaseCarriesTheImageItsEventsAndTheQueueFactsTogether",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ThePresenterReadsCommittedOutputAndRemovesOrphanedViews",
          "probe:Gc019/gc019-presentation-reads-the-committed-snapshot",
          "probe:W5Gate/w5gate-pinned-snapshots-are-read-only"),
        C("CommittedEvent records stable IDs, schema/revision, step, epoch, event sequence and causal request",
          "dotnet:GameCore.Execution.Tests.CommittedEventStoreTests.OneBuildProducesCanonicallyOrderedEventsForTheCommittedStep",
          "dotnet:GameCore.Contracts.Tests.CheckpointFormatTests.RecordCodecRejectsTrailingBytesAndAnotherSchema"),
        C("event cursors and snapshots have configured retention bounds; lagging readers receive CursorExpired and "
          "resynchronize from a snapshot",
          "dotnet:GameCore.Execution.Tests.Observation.WorldObservationTests.EventRetentionReportsGapsAndCountsRedeliveries",
          "dotnet:GameCore.Execution.Tests.Observation.WorldObservationTests.ResynchronizationNamesTheNewestImageAndTheGapItCannotRecover",
          "probe:Conformance/conformance/cards/exclusion-and-suspension/world"),
        C("internal publication is once per committed operation/step in a live world; subscriber delivery is "
          "at-least-once within retention, using (WorldId, event sequence) deduplication",
          "dotnet:GameCore.Execution.Tests.CommittedEventStoreTests.ARepeatedPageReadIsCountedAsARedelivery",
          "dotnet:GameCore.Execution.Tests.Observation.DelayedConsumerDeliveryTests.OneConsumerSeesEachIdentityOnceAndARetryOfTheSamePageIsSuppressed",
          "unity:GameCore.Observation.Tests.ObservationCursorAndProvenanceTests.DelayedConsumerDelivery_DeliversEachCommittedEventOnceAndRecoversAfterExpiry(\"cards\")"),
        C("irreversible output adapters consume only committed events, use explicit external idempotency keys and "
          "persist an outbox when delivery must survive crashes",
          "dotnet:GameCore.Execution.Tests.Delivery.DeliveryKeyTests.TwoDestinationsNeverShareAnExternalIdempotencyKey",
          "dotnet:GameCore.Execution.Tests.Delivery.DurableDeliveryAdapterTests.RedeliveryAfterAcknowledgmentLossAppliesTheDestinationMutationOnce",
          "probe:Gc021/gc021-reward-obligation-is-durable-and-idempotent",
          "probe:Conformance/conformance/cross/reward-flow/world"),
    ]),
    "P-046": R("Installation lifecycle", [
        C("installed instances use Registered, WaitingForDependencies, Preparing, Active, Quiescing, Suspended, "
          "Retiring, Disposed and Failed",
          "dotnet:GameCore.Composition.Tests.InstallationLifecycleTests.EveryStatePairMatchesTheLifecycleDiagramExactly",
          "dotnet:GameCore.Composition.Tests.InstallationLifecycleTests.NoStateIsItsOwnSuccessorAndDisposedIsTerminal",
          "dotnet:GameCore.Composition.Tests.InstallationLifecycleTests.RefusalIsAValueAndContributionAuthorityFollowsTheState"),
        C("mount is declarative registration plus attempted activation; missing required dependencies yields a "
          "published waiting instance with no active contributions, not a half-active plugin",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.WaitingInstallationActivatesWhenItsProviderAppearsAndWaitsAgainWhenItLeaves",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.RemovingARequiredProviderMakesItsConsumerWaitInTheSamePlan",
          "probe:W4Gate/w4-required-provider-loss-makes-consumers-wait"),
        C("activation requires dependency closure and publication; explicit suspension stops ingress/execution and "
          "retracts active contributions using slot policies while retaining installation and configuration, and "
          "resume rederives against current ancestry",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.WaitingForDependenciesRetractsOnceAndResumeFromWaitingRestoresAuthority",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.SuspendWalksThroughQuiescingAndResumeRecordsANewAttempt",
          "probe:W4Gate/w4-suspend-retracts-and-resume-restores",
          "probe:Conformance/conformance/cards/resume-festival"),
        C("replacement/reconfiguration stages a candidate activation while the old Active activation continues, and "
          "success transitions old -> Quiescing -> Retiring and candidate -> Active at one publication with the "
          "installation generation unchanged for an in-place update",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.StagingACandidateKeepsTheRunningActivationInCharge",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.CommitMovesTheCandidateInAndDisplacesTheRunningActivationToRetiring",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.RemountingAStableInstanceIdentityAdvancesItsInstallationGeneration",
          "probe:Conformance/conformance/cards/reconfigure-festival"),
        C("voluntary teardown failure before mutation restores old Active gates; failures after mutation follow "
          "P-031; failed preparation of a new instance is observable and retryable only by an explicit new operation",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.AbortingACandidateFailsItAndLeavesTheRunningActivationInCharge",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.ARefusedEdgeCountsTheRefusalAndLeavesTheStoredStateUntouched",
          "probe:Faults/narrative/gc017-validation-fault-rejects-and-keeps-the-old-assembly",
          "probe:Faults/narrative/gc017-gate-installation-fault-stops-after-live-writes"),
    ]),
    "P-047": R("In-flight lifetime", [
        C("quiescing closes new managed callbacks/commands from the retiring activation and freezes affected admission",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.SuspensionClosesAuthorityAndExplicitResumeUsesAFreshActivation",
          "unity:GameCore.Unity.Runtime.Tests.Faults.LifecycleFaultTests.AJobHeldInFlightWhileUnloadBeginsIsFencedAndItsResourceQuarantined"),
        C("already executing jobs MUST finish before storage or code-owned resources are released; job cancellation "
          "is cooperative intent, not memory reclamation",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.StalledJobBlocksStopInsteadOfBeingFreedOnTimeout",
          "probe:LifecycleStress/lifecycle-stress-stalled-job-retains-buffers"),
        C("accepted but unexecuted commands to a suspended/removed route finish Cancelled(RouteRetired) unless the "
          "port explicitly declares stable-ID rebinding to a compatible owner",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.AnUnconsumedNextStepQueueCarriesForwardWithTerminalCancellations"),
        C("in-flight stage/step execution reaches its commit/fault boundary before composition apply",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.DirectDisposalSettlesFaultedJobsAndRetiresTheWorld",
          "probe:Faults/narrative/gc017-fence-fault-settles-handles-and-keeps-the-old-assembly"),
        C("an async completion with an old epoch is discarded and its staged leases released, even if the stable "
          "plugin ID now exists again; callback gates are checked on dispatch and on completion",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ACompletionFromAStaleActivationIsDiscardedAndReleased",
          "dotnet:GameCore.Composition.Tests.LifecycleStressTests.AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement",
          "probe:LifecycleStress/lifecycle-stress-delayed-completions-are-discarded",
          "probe:Conformance/conformance/cross/reward-unmount-pending"),
    ]),
    "P-048": R("Teardown", [
        C("teardown order is: close ingress; settle current step; fence tracked readers/jobs; retract/publicize the "
          "new assembly; retire old resources in reverse dependency order; within an instance dispose leases in "
          "reverse acquisition order",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.TheTeardownReportNamesTheSixP048StepsInOrder",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.TeardownDisposesLeasesInReverseAcquisitionOrderAndSettlesCleanly",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.RetirementRunsFromDependentsTowardDependencies",
          "probe:W4Gate/w4-unload-disposes-in-reverse-acquisition-order"),
        C("dispose each lease at most once, attempt all independent cleanup and aggregate failures",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.ResourcesAreDisposedAtMostOnce",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.AFailingDisposerIsQuarantinedWhileOtherCleanupStillRuns",
          "probe:LifecycleStress/lifecycle-stress-throwing-disposer-keeps-cleanup-going"),
        C("resources still reachable by unfinished work are quarantined and retained; elapsed timeout only reports "
          "TeardownBlocked, never authorizes free",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.QuarantineRetainsUntilItsUsersEndAndThenAllowsRetirement",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.SnapshotIsOrderedByResourceIdAndReportsQuarantineBytes",
          "probe:LifecycleStress/lifecycle-stress-stalled-job-retains-buffers",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.SnapshotIsOrderedByResourceIdAndReportsQuarantineBytes"),
        C("retired cleanup failure reports PublishedWithCleanupErrors and keeps tracked quarantined resources, not a "
          "false Disposed",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.UnmountPublishesCleanupErrorsWithRetainedReferences",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.AFaultedStepSettlementReportsTheTeardownAsUnsettled",
          "probe:Faults/narrative/gc017-cleanup-fault-retains-staged-ownership"),
        C("a failing subscription disconnector remains behind a closed epoch gate; world shutdown disposes owned ECS "
          "storage only after its users end, and a nonterminating native job may require process termination",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.AThrowingDisposerKeepsItsReferenceAndIndependentCleanupContinues",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.StopClosesIngressSettlesJobsAndDisposesStorage",
          "probe:W1Gate/gate-teardown-settles-and-disposes"),
    ]),
    "P-049": R("Recovery limits", [
        C("pre-mutation failure leaves the old published world usable, with staged resources cleaned or quarantined",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.APrewriteMigrationFailurePreservesTheOldAssemblyAndKeepsRunning",
          "probe:Faults/narrative/gc017-prewrite-migration-fault-preserves-live-state",
          "probe:Faults/narrative/gc017-cancellation-before-the-cutoff-releases-staged-work"),
        C("post-mutation/step failure halts simulation, preserving the last good observation image and fault record",
          "unity:GameCore.Unity.Runtime.Tests.Faults.FaultBoundaryTests.AnInjectedFirstLiveWriteFaultAndTheOriginalPostwriteSwitchBothFaultTheWorld",
          "probe:Recovery/gc027-postwrite-apply-fault-never-exposes-a-destination",
          "probe:Recovery/gc027-postwrite-apply-fault-never-exposes-a-destination"),
        C("recovery creates a new WorldId from a verified checkpoint or initial catalog, validates/rebuilds "
          "composition and recipes, restores state, then reopens admission; old callbacks/handles never become valid",
          "unity:GameCore.Unity.Runtime.Tests.Recovery.InitialDefinitionRecoveryTests.RecoveryFromInitialDefinitionsCreatesAFreshIncarnationAndNeverResumesTheSource",
          "probe:Recovery/gc027-recovery-publishes-a-new-session-with-the-captured-state",
          "probe:Recovery/gc027-recovered-world-refuses-an-old-session-observation",
          "probe:RecoverySmoke/recovery-smoke-recovered-world-is-authoritative"),
        C("no hidden automatic replay of external side effects is allowed",
          "dotnet:GameCore.Execution.Tests.Delivery.DurableDeliveryAdapterTests.ACrashAfterTheAcknowledgementLeavesItSettledAndARedeliveryDoingNothing",
          "probe:Recovery/gc027-outbox-delivery-fault-redelivers-with-one-destination-effect",
          "probe:Gc021/gc021-redelivery-applies-the-mutation-once"),
        C("retryable transient resource failures use host-configured bounded attempts with new operation IDs; "
          "correctness failures require changed input/catalog; V1 provides checkpoint recovery, not historical "
          "gameplay undo or arbitrary live code replacement",
          "dotnet:GameCore.Recovery.Fixtures.Tests.RecoveryPolicyTests.AHostBoundOfThreeAllowsTwoRetriesForARetryableFailure",
          "dotnet:GameCore.Recovery.Fixtures.Tests.RecoveryPolicyTests.ACorrectnessFailureNeverAllowsARetryWithAttemptsLeft",
          "dotnet:GameCore.Recovery.Fixtures.Tests.RecoveryPolicyTests.NoHostSettingMeansExactlyOneAttempt",
          "probe:Recovery/gc027-transient-failure-is-retried-under-the-host-bound"),
    ]),
    "P-050": R("Cancellation and idempotency", [
        C("every mutating public operation has an OperationId = (WorldId, IssuerId, IssuerSequence) and canonical "
          "input hash; a command's request key is that same operation identity",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.AdmittedCancellationBindsItsIdentityAndKeepsTheIssuerSequenceInStep",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationInputHashIsDomainSeparatedFromEditInputs"),
        C("first submissions from one issuer use strictly increasing sequences across its control operations and "
          "commands; retransmissions may reuse earlier IDs",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.ReorderedIssuerSequenceIsRefused",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.AnOutOfOrderIssuerSequenceIsRefused",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationRetransmissionCoalescesToTheOriginalOutcomeWithoutRepeatingTheCutoff"),
        C("create/restore use a caller-reserved fresh WorldId before allocation and the bounded host ledger retains "
          "the reservation through failed/cancelled attempts for its process lifetime",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.ASettledAttemptKeepsItsTerminalOutcomeAndAFailedAttemptIsNeverForgotten",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.ARetransmittedRestoreReturnsTheSameFreshReservation",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.ReusingAReservedSessionIsRefusedWithStaleHandle"),
        C("internal suboperations use the parent's operation ID plus a bounded work ordinal",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.IdSequenceIsDeterministicAndNeverWraps",
          "dotnet:GameCore.Composition.Tests.Diagnostics.StagedOperationStatusTests.AHandleReadResolvesTheSameStatusAsTheIdentityRead"),
        C("a per-session ledger returns the same pending/terminal result for the same ID/hash and the same retained "
          "ID with different input yields IdempotencyConflict; the ledger has configured capacity and terminal "
          "entries may be explicitly acknowledged/evicted, after which an absent ID at/below the issuer's retained "
          "admission high-water mark returns ResultExpired and cannot execute",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.SameOperationIdAndSameInputCoalesceToOnePublication",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.ConflictingReuseIsRejectedAndKeepsTheOriginalResult",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.ResultsExpireByCountAndAnExpiredOperationCannotReexecute",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.BoundedResultRetentionTurnsDroppedIdentitiesIntoResultExpired",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.ForgottenAndUnseenLowerSequencesRemainExpired"),
        C("issuer high-water marks persist until world disposal; new issuer registration is bounded and old issuer "
          "IDs cannot be reused to reset their sequence",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.LaneCapacityRefusesFurtherAdmission",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.CapacityBackpressureRecordsNothing"),
        C("restart creates a new world/session and durable replay safety requires the checkpoint/outbox contract",
          "probe:RecoverySmoke/recovery-smoke-restart-publishes-a-new-session",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.ASettledAttemptKeepsItsTerminalOutcomeAndAFailedAttemptIsNeverForgotten"),
        C("cancellation is accepted before Applying or command execution admission, cleans staged work and returns "
          "Cancelled; at/after that cutoff it returns TooLate and the actual operation must be awaited; retrying a "
          "rejected revision requires a new operation ID, never mutating the old request",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.CancellingAStagedOperationReleasesItsGatedLeases",
          "probe:Faults/narrative/gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication"),
    ]),
    "P-051": R("Operation discipline", [
        C("section 10 is the complete V1 kernel operation set; all mutating operations execute on, or enqueue to, "
          "the host control lane, and plugins submit proposals rather than calling live ECS mutation APIs",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.SeveralAdmittedProposalsPublishInAdmissionOrder",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.DirectPublicationCannotOvertakeAnEarlierProposal",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.CompositionHostDoubleAdmitsStaleRejectsRetainsAndExpires"),
        C("each accepted operation returns a handle immediately, then a terminal result with diagnostic IDs",
          "dotnet:GameCore.Composition.Tests.Diagnostics.StagedOperationStatusTests.AStagedProposalIsReportedAsStagedAndNeverAsPublishedState",
          "dotnet:GameCore.Composition.Tests.Diagnostics.StagedOperationStatusTests.ARefusedOperationReportsItsOwnCodeAndNoPublishedImage",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.TerminalRetransmissionKeepsItsOriginalHandle"),
        C("status/query methods are pure and cancellable without changing the world",
          "dotnet:GameCore.Composition.Tests.Diagnostics.StagedOperationStatusTests.AnUnknownOperationIsDistinctFromAnExpiredOne",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.TheExplainReaderPagesBoundedRecordsAndLabelsItsSource"),
        C("unsupported operation variants reject before acquisition",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.SeamSubmissionRequiresTheDeclaredKindToMatchTheSubject",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.UndecodableProposalIsRejectedThroughTheFrozenSeam"),
        C("a queued cancellation and publication race is resolved by the serialized control lane at the stated "
          "cutoff, never by thread timing inside jobs; failure/cancellation outcomes MUST be observable even when "
          "no gameplay step occurs",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.ConflictingCancellationReuseKeepsTheOriginalAndLeavesTheSecondTargetUntouched",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancelledTargetRetentionStartsAtCancellationRatherThanWorldCreation",
          "probe:W1Gate/gate-operation-status-reports-fault-honestly",
          "probe:Conformance/conformance/cards/exclusive-conflict-rejected"),
    ]),
    "P-052": R("Diagnostics", [
        C("errors have a stable code, world/operation/plan identity, phase, involved rule/stage/schema IDs, "
          "provenance pointers, counts/budgets and retry classification",
          "dotnet:GameCore.Contracts.Tests.RequiredDiagnosticCodeTests.EveryDocumentedCodeExistsIsNamedExactlyAndAppearsInTheExposedList",
          "dotnet:GameCore.Composition.Tests.DiagnosticContractTests.EveryReachableRefusalCarriesItsCodePhaseOperationAndClassification",
          "dotnet:GameCore.Composition.Tests.DiagnosticContractTests.AValidationRejectionDiagnosticCarriesTheOperationAndAClassification"),
        C("the required codes include StaleHandle, StalePlan, MissingDependency, ServiceConflict, "
          "CapabilityConflict, AmbiguousOrder, Cycle, Ineligible, UnsupportedVersion, OwnershipConflict, "
          "BudgetExceeded, MigrationRequired, ResourceUnavailable, Cancelled, TooLate, IdempotencyConflict, "
          "ResultExpired, ApplyFault, TeardownBlocked and CursorExpired",
          "dotnet:GameCore.Contracts.Tests.RequiredDiagnosticCodeTests.TheDocumentedCodesAreExposedInTheRequirementOrder",
          "dotnet:GameCore.Contracts.Tests.RequiredDiagnosticCodeTests.NoneIsAValueButNeverARequiredCode",
          "dotnet:GameCore.Contracts.Tests.RequiredDiagnosticCodeTests.EveryEnumValueIsMappedAndEitherRequiredOrTheDocumentedP007Addition",
          "dotnet:GameCore.Contracts.Tests.RequiredDiagnosticCodeTests.AnUnknownNameIsRefusedRatherThanCoerced"),
        C("a rejection MUST explain the smallest known conflicting set or a bounded summary with retrieval key",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.OrderedRejectsADeclaredCycleWithItsCandidatesAsWitnesses",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.TwoUndeclaredWritersOfOneDomainReject",
          "dotnet:GameCore.Composition.Tests.Diagnostics.DiagnosticPrecedenceTests.RegistryInternsBoundsAndOrdersItsPayloads",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.DeclaredBoundsRefuseInsteadOfTruncating"),
        C("logging never changes precedence or world state",
          "dotnet:GameCore.Composition.Tests.Diagnostics.DiagnosticPrecedenceTests.WordingNeverChangesIdentityOrPosition",
          "dotnet:GameCore.Composition.Tests.Diagnostics.DiagnosticPrecedenceTests.FeedRetentionDropsTheOldestRecordAndCountsIt",
          "probe:Conformance/conformance/genre-audit"),
    ]),
    "P-053": R("Checkpoint", [
        C("checkpoints are taken at a committed boundary after required jobs complete",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ACaptureAtACommittedBoundaryProducesAVerifiedDocument",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ANonCommittedBoundaryIsRefusedWithTooLateAndNothingIsRead",
          "probe:W5Gate/w5gate-checkpoint-from-the-committed-boundary",
          "probe:Gc018/gc018-committed-boundary-capture"),
        C("they contain catalog/protocol fingerprints, world definition/temporal settings, step/time/debt, stable "
          "scope/install/target IDs, descriptors, explicit imports/overrides/exclusions, mode, definitions, active "
          "and dormant authoritative state, owner versions, RNG streams, bounded pending next-step messages and "
          "external outbox/dedup cursors when used",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.TheHeaderEchoesTheCommittedBoundaryAndItsCatalogFingerprint",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ActiveAndDormantStateRoundTripsWithoutHandles",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.ASelfConsistentDocumentPlansIntoAFreshSessionWithItsDormantState",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.APlanCarriesTheOutboxRowsTheDocumentDeclares",
          "probe:Gc018/gc018-restore-preserves-mode-imports-and-exclusions",
          "probe:Gc018/gc018-restore-continues-clocks-rng-and-cursors"),
        C("raw host timestamps are normalized to declared clock policy",
          "dotnet:GameCore.Contracts.Tests.CheckpointRecordRoundTripTests.ClockDeclarationRoundTripsItsDeclarationFields",
          "dotnet:GameCore.Replay.Tests.TelemetrySchemaTests.DurationsConvertHostTicksWithoutReadingAClock"),
        C("new host commands wait during capture; already queued external commands are either included with "
          "ledger/cutoff or explicitly rejected before capture according to the checkpoint option, never ambiguously "
          "omitted",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.IncludeQueuedCarriesEveryCommandIntoTheDocument",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.RejectQueuedAccountsForEveryCommandSoNoneIsAmbiguouslyOmitted",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.DeclaredQueueFactsThatContradictTheCopiedQueueRefuseTheCapture",
          "probe:Gc018/gc018-queued-commands-are-dispositioned-not-omitted"),
        C("restore validates everything into a new unexposed world before publication",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.EveryRefusalShapeYieldsNoPartialPlanAndReplaysIdentically",
          "probe:Gc018/gc018-restore-happens-into-a-new-unexposed-world",
          "probe:Recovery/gc027-reference-repair-fault-never-builds-a-destination"),
    ]),
    "P-054": R("Serialization", [
        C("wire/save data uses schema IDs, integer versions, explicit field IDs, canonical byte order, length "
          "bounds, null semantics and reference tables; no CLR assembly name or engine handle serves as its identity",
          "dotnet:GameCore.Contracts.Tests.CheckpointContainerTests.MinimalDocumentRoundTripsItsHeaderCountsAndChecksum",
          "dotnet:GameCore.Contracts.Tests.CheckpointFormatTests.TryKindOfFieldAcceptsExactlyTheThirteenRecordFieldIds",
          "dotnet:GameCore.Contracts.Tests.CheckpointFormatTests.CanonicalId32CollationIsBigEndianAndOrderSensitive",
          "dotnet:GameCore.Contracts.Tests.CheckpointRecordRoundTripTests.MessageRecordWithAnAbsentPayloadRoundTripsAsTheDeclaredNoneState"),
        C("unknown required fields/schema versions reject; explicitly optional presentation fields can be skipped",
          "dotnet:GameCore.Contracts.Tests.CheckpointContainerTests.ADocumentDeclaringAnUnknownRequiredFeatureIsRefused",
          "dotnet:GameCore.Recovery.Fixtures.Tests.CheckpointStoreTests.ADeclaredVersionThisBuildDoesNotImplementIsRefusedWithUnsupportedVersion",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.AProtocolMajorTwoDocumentIsRefusedWithUnsupportedVersionBeforeItReachesThePlanner",
          "probe:Gc018/gc018-unknown-required-schema-rejects-restore"),
        C("generated serializers and registered directed version migrations replace reflection-based type construction",
          "dotnet:GameCore.Contracts.Tests.CheckpointCodecSetTests.ACompleteCodecSetCoversEveryDeclaredKindExactlyOnce",
          "dotnet:GameCore.Content.Compiler.Tests.CheckpointCatalogTests.EveryCheckpointSchemaIsDeclaredRequiredWithAscendingFieldIds",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-closed-generic-roots"),
        C("migration paths must be unique for a requested source/target pair; ambiguity is rejected",
          "dotnet:GameCore.Contracts.Tests.CheckpointMigrationTests.TwoChainsToTheSameDestinationAreAmbiguous",
          "dotnet:GameCore.Contracts.Tests.CheckpointMigrationTests.PlanningEveryMigrationRefusesAnAmbiguousGraph",
          "dotnet:GameCore.Contracts.Tests.CheckpointMigrationTests.AUniqueChainIsPlannedInExecutionOrder",
          "probe:Gc018/gc018-ambiguous-migration-rejects-restore"),
        C("pure definitions/rules can be reused elsewhere; Unity assets, Entity layouts, Jobs graphs, physics "
          "observations and presentation semantics require engine-specific conversion or reimplementation",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.ValidManifestIsAccepted",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.AWorldWithNoExternalDomainNeedsNoPhysicsAdapter",
          "probe:Recovery/gc027-recovered-engine-physics-is-reseeded-not-continued",
          "probe:W6Gate/traversal/w6-carries-the-optional-engine-surface"),
    ]),
    "P-055": R("Protocol evolution", [
        C("V1 protocol is 1.0; manifests declare supported major plus min/max minor and required feature IDs",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.UnsupportedProtocolRangeRejects",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.UnknownRequiredFeatureRejects",
          "dotnet:GameCore.Contracts.Tests.CheckpointFormatTests.KnownFeatureIdsHoldExactlyTheDerivedRequiredFeature",
          "check:compatibility-report"),
        C("major changes are incompatible and a minor extension may add optional fields/operations only without "
          "changing existing semantics; unknown required features reject before mount",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.UnknownRequiredFeatureRejects",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.AProtocolMajorTwoDocumentIsRefusedWithUnsupportedVersionBeforeItReachesThePlanner",
          "probe:MissingRegistration/missing-registration-detected"),
        C("changes to reducers, precedence, stage contracts, serialization or state policy require their own "
          "version updates and may require protocol major review",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ChangingOnlyAStageVersionOrAffinityChangesThePlanHash",
          "dotnet:GameCore.Derivation.Tests.CompositionPolicyTests.EveryPolicyProducesTheSameResultUnderEveryInsertionPermutation",
          "dotnet:GameCore.Contracts.Tests.ApiCompatibilityTests.ProductionSurfaceIsAStrictSupersetOfTheFrozenSeamSnapshot"),
        C("old document compatibility is not a goal, and no runtime silently falls back to Conservative, old combat "
          "phases or a different ECS backend on mismatch",
          "dotnet:GameCore.Contracts.Tests.ApiCompatibilityTests.ProductionListingIsDeterministicAndEngineFree",
          "check:genre-audit",
          "check:contract-surface-parity"),
    ]),
    "P-056": R("Extension points", [
        C("registered capability reducers, schemas, state owners, time policies within a selected temporal model, "
          "stage contracts, request ports, asset/presentation/input adapters, serializer migrations and pure rule "
          "functions are supported V1 seams",
          "dotnet:GameCore.Rules.Cards.Tests.CardRulesTests.TheRegisteredReducerReportsItsKeyAndNameAndDelegates",
          "dotnet:GameCore.Contracts.Tests.CheckpointMigrationTests.AWellFormedGraphWithoutDuplicatesReportsCleanRegistration",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PayloadEncodingRoundTripsEveryField",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-registration-lookup"),
        C("trusted extensions still obey memory lifetime, authority, stage access, finite derivation and "
          "publication requirements",
          "dotnet:GameCore.Composition.Tests.LifecycleStressTests.EveryAcquisitionOfACycleIsTracedToRetirementOrQuarantineByKind",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.ADeclaredWriterWithoutAnOwnerRejects",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.TwoConflictingDeclarationsOfOneSystemKeyReject",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.AFiniteCapabilityChainTerminatesAtTheHighestStratum"),
        C("arbitrary target-generating derivation, runtime code download, security sandboxing, universal engine/ECS "
          "adapters, cross-world atomic transactions, distributed consensus and general speculative execution are "
          "deferred, not empty mandatory interfaces",
          "dotnet:GameCore.Derivation.Tests.TerminationAndBudgetTests.ASelfProducingRuleIsRejected",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-unknown-recipe-refused",
          "check:genre-audit"),
    ]),
    "P-057": R("Conformance", [
        C("a conforming implementation implements all P-001 through P-060, the mandatory operation set, both "
          "propagation modes and both temporal models",
          "check:genre-audit",
          "probe:W3Gate/w3-zero-idle-command-steps-in-both"),
        C("contract suites use a managed model/oracle; real execution suites use Unity Entities worlds and "
          "standalone players; a mock-only implementation or successful compilation is insufficient",
          "dotnet:GameCore.Derivation.Tests.OracleAgreementTests.TheRuntimeAndTheOracleAgreeOnEverySeedAndStep",
          "unity:GameCore.W7Gate.Tests.W7GateIntegrationTests.TheConformanceTablesRepass",
          "probe:Positive/generated-aot-roots",
          "probe:Conformance/conformance/revision"),
        C("the test catalogue and task registry trace every requirement to executable acceptance work",
          "check:docs-validator",
          "check:docs-validator-self-test",
          "dotnet:GameCore.ReferenceConformance.Tests.ConformanceDocGapTests.ThisRevisionRecordsNoGapBecause070276sClaimsAreAllCarried"),
        C("optional gameplay packages do not become kernel dependencies merely because fixtures use them",
          "dotnet:GameCore.ReferenceConformance.Tests.GenreAuditDocumentTests.TheForbiddenTokenListNamesTheGenreTypesTheKernelMustNotKnow",
          "check:genre-audit",
          "probe:W3Gate/w3-kernel-assemblies-reference-no-gameplay"),
    ]),
    "P-058": R("V1 implementation profile", [
        C("C# plus Unity Entities is the only V1 ECS backend; Unity Jobs and Burst apply to eligible hot paths and "
          "managed composition remains outside Burst",
          "probe:Positive/entities-world-entity-query",
          "probe:Positive/burst-generic-job",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.NoScheduleRequiresACombatPhysicsOrAnimationStage",
          "check:genre-audit"),
        C("IL2CPP and managed stripping are explicit build acceptance requirements with generated "
          "registration/serializer/generic-root coverage",
          "check:player-environment",
          "probe:MissingRegistration/missing-registration-detected",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-closed-generic-roots",
          "check:link-xml"),
        C("an exact editor/package/native-toolchain baseline is specified in 04",
          "check:player-environment",
          "dotnet:GameCore.Content.Compiler.Tests.CommittedProbeCatalogTests.CommittedGeneratedCatalogMatchesAFreshGeneration",
          "check:catalog-byte-identity"),
        C("compatibility evidence must include official support constraints and a resolved package lock plus actual "
          "player execution; C# syntax or target framework compatibility alone does not pass the gate",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-headless-canonical",
          "probe:WorldDispatch/world-two-worlds-independent",
          "check:clone-surface",
          "check:release-gate-surface"),
    ]),
    "P-059": R("Genre validation before freeze", [
        C("the card, narrative and real-time-action reference compositions MUST pass without adding mandatory kernel "
          "gameplay schemas or phases",
          "dotnet:GameCore.ReferenceConformance.Tests.ReferenceTableTests.EveryRowOfEveryTableIsExecutedByItsScript",
          "unity:GameCore.Validation.ProbeHost.Tests.ConformanceIntegrationTests.EveryTranscribedTableIsExecutedByTheRunThatClaimsIt",
          "probe:Conformance/conformance/cards/verdict",
          "probe:Conformance/conformance/narrative/verdict",
          "probe:Conformance/conformance/traversal/verdict"),
        C("at least cards and narrative execute before provisional generic execution freeze; action and the "
          "cross-family composition execute before V1 conformance freeze",
          "probe:Conformance/conformance/cross/verdict",
          "probe:Traversal/gc020-teardown-settles-and-disposes",
          "dotnet:GameCore.ReferenceConformance.Tests.CrossGraphValidationTests.EveryDeclaredGraphFaultIsRefusedByTheKernelWithItsDeclaringPluginNamed"),
        C("each exercises automatic existing/future descendants, mount/unmount, reparent, mode changes, state "
          "retention and ownership",
          "probe:Conformance/conformance/cards/subtree-move/world",
          "probe:Conformance/conformance/narrative/unmount-with-live-session/world",
          "probe:Conformance/conformance/traversal/mode-directions/world",
          "probe:W4Gate/w4-subtree-move-preserves-state-and-switches-binding"),
        C("a necessary example-specific rule belongs to its package; a general missing mechanism changes this "
          "protocol and its tests explicitly",
          "dotnet:GameCore.ReferenceConformance.Tests.ConformanceDocGapTests.AnyGapThatIsEverDeclaredMustNameItsRowItsClauseItsEvidenceAndItsResolution",
          "dotnet:GameCore.Rules.Narrative.Tests.GenreNeutralityTests.TheRegisteredNameSetIsGenreNeutral",
          "check:genre-audit"),
    ]),
    "P-060": R("Evidence and release status", [
        C("the implementation records the exact build/catalog versions, resolved dependencies, native compiler, "
          "architecture, backend/stripping settings, test commands, logs, fixture hashes and measurements",
          "check:player-environment",
          "check:budget-record",
          "check:release-player-surface",
          "check:native-leak-attribution"),
        C("budget numbers are provisional until measured on a named machine",
          "dotnet:GameCore.Benchmarks.Tests.PerformanceBudgetTests.TheTenDeclaredRowsExistExactlyOnceEachInTableOrder",
          "check:benchmark-diagnostic",
          "check:budget-decisions",
          "check:budget-record",
          deferred="TEST-023 full-duration p95/p99 timing qualification is deferred by project-owner decision "
                   "(2026-09-26). The correctness gates passed in a short diagnostic; the timing rows are reported "
                   "as Deferred, never as Pass, and artifacts/performance/BUDGET_DECISIONS.md records it."),
        C("documentation validation does not imply Unity tests passed and V1 completion requires the "
          "dependency-ordered gates, including failures/unload, IL2CPP, checkpoint recovery and no per-frame scope "
          "traversal",
          "check:docs-validator",
          "probe:LifecycleStress/lifecycle-stress-counters-return-to-baseline",
          "probe:Replay/replay-world-idle-pump-does-zero-control-work",
          "probe:RecoverySmoke/recovery-smoke-teardown-is-clean"),
        C("unknown product platforms/load targets remain explicit, not assumed team size or commercial requirements",
          "dotnet:GameCore.Benchmarks.Tests.BenchmarkFixtureTests.TheDeclaredProviderCountsAndInstallationsAgreeWithTheShape",
          "check:docs-validator",
          "check:release-gate-surface",
          "note: 04 section 1's unqualified-target table and artifacts/baseline/unsupported-targets.md carry the "
                  "explicit unknowns; this row asserts only that the repository keeps stating them."),
    ]),
})

# ---------------------------------------------------------------------------------------------------
# O-01..O-26. One row per catalogued operation. Each row maps the catalogue's own fields — preconditions,
# ordering/visibility, postconditions, failure and cancellation/retry — to concrete executable evidence, plus
# the required suites the 08 coverage table names.
# ---------------------------------------------------------------------------------------------------
OPERATIONS = {
    "O-01": R("CreateWorld", [
        C("preconditions: serialized control lane; unique session and a supported catalog/profile; no execution "
          "before the initial publication",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.CreationPublishesEpochOneAndStepZero",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FactoriesRejectAnInvalidConfiguration",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.AnAllZeroSessionIsAnInvalidReservation",
          "probe:W1Gate/gate-catalog-and-manifest-source"),
        C("ordering and visibility: invalid catalog rejects without world exposure",
          "dotnet:GameCore.Contracts.Tests.CatalogBuildTests.BuildAcceptsConsistentTablesAndReportsNoDiagnostics",
          "probe:MissingRegistration/missing-registration-report",
          "probe:W1Gate/gate-two-owned-worlds"),
        C("failure and cancellation: cancel before allocation publication; repeat returns the same world",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.RepeatedCreationOfTheSameSessionReturnsTheSameWorld",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.ARetransmittedRestoreReturnsTheSameFreshReservation"),
        C("postconditions: a new WorldId and a created world whose handles scope to that session (probe: two "
          "worlds/restore never share runtime handles)",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.TwoWorldsAdvanceIndependently",
          "probe:WorldDispatch/world-two-worlds-independent",
          "probe:Gc018/gc018-restore-recreates-state-at-different-native-indices"),
    ], suites=["TEST-002", "TEST-018"]),
    "O-02": R("EditScopes", [
        C("preconditions: control lane to boundary; same world, parent exists, no ancestry cycle, creation IDs "
          "reserved privately",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.RootScopeIsTheOnlyScopeWithoutAParent",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.CreatedScopeCarriesMembershipAndInheritsItsParentDepth",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.ReparentCyclesAndCrossAncestryMovesAreRejected"),
        C("ordering and visibility: a successful publication updates membership and all derived closure together",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AScopeReparentReportsTheMoveAndItsNewParent",
          "dotnet:GameCore.Composition.Tests.DeclaredScopeTreeTests.ADeclaredTreeOpensTheCommittedCompositionWithEveryDeclaredScope",
          "probe:W1Gate/gate-control-lanes-linked"),
        C("failure and cancellation: reject a cycle, a cross-world move, a nonempty drop lacking a disposition or a "
          "policy conflict; cancel before Applying",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.RemovingANonemptyScopeRequiresAnExplicitSubtreeDisposition",
          "dotnet:GameCore.Composition.Tests.ScopeTreeTests.AnEmptyScopeCanBeRemovedAfterItsMembersAreReparentedOut",
          "probe:Conformance/conformance/cards/exclusion-and-suspension/world"),
        C("postconditions: moving a branch changes inheritance and retains slot state (the catalogue's probe)",
          "probe:W4Gate/w4-subtree-move-preserves-state-and-switches-binding",
          "probe:Gc013/narrative/gc013-reparent-preserves-target-state-and-inheritance",
          "probe:Conformance/conformance/narrative/reparent-village"),
    ], suites=["TEST-006", "TEST-008"]),
    "O-03": R("Mount", [
        C("preconditions: lane, prepare, then boundary; precompiled catalog key and a valid scope with the "
          "dependency graph validated and no live callbacks during preparation",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication",
          "dotnet:GameCore.Contracts.Tests.ManifestValidationTests.MissingPrecompiledFactoryKeyRejects",
          "probe:Positive/late-mount-linked-inactive-plugin"),
        C("ordering and visibility: Active or WaitingForDependencies only after publication",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.WaitingInstallationActivatesWhenItsProviderAppearsAndWaitsAgainWhenItLeaves",
          "probe:W2Gate/gate2-provider-mounted-and-published"),
        C("failure: a missing service produces Waiting; an invalid config/graph rejects; a failed new factory "
          "reports a Failed attempt",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.IncompatibleContractVersionLeavesTheConsumerWaiting",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.AbortingACandidateFailsItAndLeavesTheRunningActivationInCharge",
          "probe:W4Gate/w4-required-provider-loss-makes-consumers-wait"),
        C("postconditions: one mount changes all eligible existing and future descendants (the catalogue's probe)",
          "probe:Conformance/conformance/narrative/chapter-mount-and-future-descendant/world",
          "probe:Cards/cards-mount-reaches-existing-seats",
          "probe:Cards/cards-future-seat-inherits-modifier",
          "probe:LifecycleStress/lifecycle-stress-required-provider-churn"),
    ], suites=["TEST-003", "TEST-004", "TEST-015"]),
    "O-04": R("ActivateOrResume", [
        C("preconditions: lane to boundary; not Disposed; an explicit suspend flag cleared only on a requested "
          "resume; all required dependencies ready",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.StagingIsRefusedForASuspendedActivation",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.WaitingForDependenciesRetractsOnceAndResumeFromWaitingRestoresAuthority",
          "probe:W4Gate/w4-required-provider-return-resumes-consumers"),
        C("ordering and visibility: staged activation; the epoch changes only on success",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.FirstActivationIsPermittedAndALiveDuplicateIdentityIsRefused",
          "probe:W4Gate/w4-suspend-retracts-and-resume-restores"),
        C("failure: still-missing dependencies remain Waiting; a preparation failure retains the prior inactive "
          "state; retry needs a new operation",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.ARefusedEdgeCountsTheRefusalAndLeavesTheStoredStateUntouched",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.TerminalRetransmissionKeepsItsOriginalHandle"),
        C("postconditions: a restored provider activates waiting dependents without target wiring (the catalogue's "
          "probe)",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.ReturningTheProviderResumesTheConsumerThatWaited",
          "probe:W4Gate/w4-required-provider-return-resumes-consumers",
          "probe:Conformance/conformance/cards/resume-festival"),
    ], suites=["TEST-003", "TEST-015"]),
    "O-05": R("ReconfigureOrReplace", [
        C("preconditions: prepare while the old activation stays active, then boundary; the installation generation "
          "stays unchanged for an in-place replacement",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.StagingACandidateKeepsTheRunningActivationInCharge",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.RemountingAStableInstanceIdentityAdvancesItsInstallationGeneration"),
        C("ordering and visibility: schema/owner compatibility and an explicit replacement map validated; the "
          "contribution closure is replaced atomically",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.CommitMovesTheCandidateInAndDisplacesTheRunningActivationToRetiring",
          "probe:Conformance/conformance/cards/reconfigure-festival"),
        C("failure: a missing migration rejects and the old activation remains Active; a postwrite failure faults; "
          "cancel before Applying leaves the old activation",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.AMissingOrMismatchedHandlerIsRefusedWithItsOwnCode",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy",
          "probe:Faults/narrative/gc017-prewrite-migration-fault-preserves-live-state"),
        C("postconditions: numerical config changes preserve runtime counters and invalidate late callbacks (the "
          "catalogue's probe)",
          "dotnet:GameCore.Composition.Tests.ConfigurationTests.ReconfigurePreservesStateAndIncrementsOnlyTheActivationEpoch",
          "probe:Cards/cards-duplicate-command-transfers-once",
          "probe:Gc019/gc019-input-completion-from-a-retired-activation-is-discarded"),
    ], suites=["TEST-005", "TEST-010", "TEST-015"]),
    "O-06": R("Suspend", [
        C("preconditions: lane to boundary; the dependency closure is included; ingress closes before the fence",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.SuspensionClosesAuthorityAndExplicitResumeUsesAFreshActivation",
          "probe:Conformance/conformance/cards/exclusion-and-suspension/world"),
        C("ordering and visibility: Suspended with retained definition/slot dispositions; all contributions from "
          "the suspended activation retract",
          "dotnet:GameCore.Composition.Tests.ActivationLedgerTests.SuspendWalksThroughQuiescingAndResumeRecordsANewAttempt",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.ASuspendPublicationCarriesItsEdgeAndItsRetractedConsumer",
          "probe:W4Gate/w4-suspend-retracts-and-resume-restores"),
        C("failure: an untransferable state policy rejects while the old activation stays active; a blocked fence "
          "stays pending with TeardownBlocked; TooLate after Applying",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.TransferToWithoutATransferPolicyRejects",
          "probe:Faults/narrative/gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication"),
        C("postconditions: no suspended executor runs and dormant data survives resume (the catalogue's probe)",
          "dotnet:GameCore.Planning.Tests.StatePolicyTests.TheLastSupportLossOfDurableStateRetainsItDormant",
          "probe:W4Gate/w4-slot-preserve-dormant-retains-without-an-active-writer",
          "probe:Conformance/conformance/narrative/resume-chapter"),
    ], suites=["TEST-010", "TEST-015"]),
    "O-07": R("Unmount", [
        C("preconditions: lane to boundary, then cleanup; dependents wait or rebind and ownership dispositions apply",
          "dotnet:GameCore.Composition.Tests.ServiceClosureDeltaTests.RemovingARequiredProviderMakesItsConsumerWaitInTheSamePlan",
          "probe:W4Gate/w4-slot-transfer-to-moves-the-value-to-the-named-owner"),
        C("ordering and visibility: a removed installation plus a terminal teardown record; no other supports are "
          "deleted",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.UnmountRetractsExactlyTheUnmountingProvidersSupport",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.ReleaseInstanceReleasesOnlyThatInstancesEntries",
          "probe:Conformance/conformance/cards/unmount-festival"),
        C("failure: a missing required disposition rejects; cleanup errors retain quarantine after a successful "
          "publication; repeat retrieves the removal outcome and never recreates",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.UnmountPublishesCleanupErrorsWithRetainedReferences",
          "dotnet:GameCore.Composition.Tests.BuildHostRegressionTests.TerminalRetransmissionKeepsItsOriginalHandle",
          "probe:Conformance/conformance/traversal/unmount-after-progress/world"),
        C("postconditions: one of two supports removed, a committed reward remains and a late callback is discarded "
          "(the catalogue's probe)",
          "probe:Cards/cards-rejected-settlement-changes-nothing",
          "probe:Conformance/conformance/cross/reward-scoring-unmount-keeps-card",
          "probe:Faults/narrative/gc017-old-callback-after-recovery-is-rejected"),
    ], suites=["TEST-005", "TEST-015", "TEST-016"]),
    "O-08": R("SetMode", [
        C("preconditions: lane to boundary with the entire affected closure derived and the setting published with "
          "the assembly",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.BothModeSwitchDirectionsPublishAndAreReported",
          "dotnet:GameCore.Derivation.Tests.IncrementalAgreementTests.AModeSwitchReportsAWholeWorldInvalidation",
          "probe:W4Gate/w4-mode-automatic-to-conservative-retracts-existing-and-future"),
        C("ordering and visibility: the same setting with no other edits is NoChange",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.RepeatingTheCurrentModeIsNoChangeAndReportsNothing",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.ModeSwitchPublishesOneSettingAndKeepsAutomaticAsTheDefault"),
        C("failure: a conflict or budget rejects while the old mode is retained; cancel before Applying",
          "dotnet:GameCore.Composition.Tests.IncrementalInvalidationTests.AValidatorRefusalRejectsTheSwitchAndKeepsTheOldModeAndRevision",
          "dotnet:GameCore.Derivation.Tests.ReferenceMoveAndModeTests.AConflictOnTheOtherModeLeavesTheOldAssemblyPublished",
          "probe:Gc013/narrative/gc013-exclusive-conflict-preserves-mode-and-membership"),
        C("postconditions: a switch conflict leaves prior capabilities intact and a later spawn follows the "
          "committed setting (the catalogue's probe)",
          "probe:W4Gate/w4-mode-conservative-to-automatic-restores-existing-and-future",
          "probe:Conformance/conformance/cards/mode-directions/world",
          "probe:Conformance/conformance/traversal/mode-directions/world"),
    ], suites=["TEST-006", "TEST-009"]),
    "O-09": R("BuildPlan", [
        C("preconditions: control-lane snapshot and pure background work with the strata/index closure and "
          "service/execution/state validation complete",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.TheBaseRecheckComparesRevisionAndEpochWithoutMutatingThePlan",
          "dotnet:GameCore.Planning.Tests.Ownership.OwnerAuthorityValidatorTests.TheReportIsCanonicalAndStableAcrossInputPermutations",
          "dotnet:GameCore.Planning.Tests.Ownership.SlotPolicyValidatorTests.ACompleteDeclarationWithNoMigrationExecutorValidates"),
        C("ordering and visibility: the plan hash includes all semantic inputs",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.ThePlanHashIsIndependentOfMountDeclarationOrder",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ChangingOnlyAnAccessSetChangesThePlanHash",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.PlanHashIsCanonicalForTheSameDeclarationAndBaseRevision"),
        C("failure: a pure validation or budget failure has no live effect and same snapshot/input gives the same "
          "hash; cancellable between batches",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.APlanBeyondTheConfiguredHardBudgetRejectsWithItsCounts",
          "dotnet:GameCore.Planning.Tests.AssemblyPlannerTests.AStaleExpectedRevisionRejectsWithoutInstallingAnything",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.AFreshPlanStartsInDraftAndIsNotTerminal"),
        C("postconditions: the incremental plan hash equals the full oracle (the catalogue's probe)",
          "dotnet:GameCore.Derivation.Tests.IncrementalAgreementTests.TheIncrementalEngineMatchesTheOracleOnEverySeedAndStep",
          "probe:W7Gate/w7-incremental-derivation-matches-a-clean-derivation"),
    ], suites=["TEST-005", "TEST-007", "TEST-008", "TEST-009"]),
    "O-10": R("PreparePlan", [
        C("preconditions: control lane or asynchronous I/O with the expected base unchanged, or the plan becomes "
          "stale; callbacks gated and resources invisible",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.StagedLeasesStayInertUntilPublication",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication"),
        C("ordering and visibility: a validated plan becomes a prepared plan with leases and bounded scratch",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.ScratchReservesWithinItsBudgetAndRefusesBeyondIt",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.ScratchReleasesInReverseReservationOrderAndReportsTheCount"),
        C("failure: missing assets or timeouts release/track staged resources; cancellation is supported until "
          "Applying; resources never leak on a failed retry",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.FailedPreparationReleasesEarlierStagedAcquisitions",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.CancellingAStagedOperationReleasesItsGatedLeases",
          "probe:Faults/narrative/gc017-acquisition-fault-releases-staged-leases"),
        C("postconditions: a staged subscription cannot submit gameplay (the catalogue's probe)",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.PreparedLeaseStaysInertUntilPublication",
          "probe:Faults/narrative/gc017-cancellation-before-the-cutoff-releases-staged-work"),
    ], suites=["TEST-009", "TEST-016"]),
    "O-11": R("PublishPlan", [
        C("preconditions: a boundary; seal admission, complete users, recheck the revision, migrate scratch, apply "
          "and then expose all tables and the snapshot",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.OnePublicationChangesEveryTargetInOneVisibleEpoch",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.TheCompositionAndPublishedSeriesAreOneAfterEveryPublication",
          "probe:W2Gate/gate2-one-bounded-command-committed"),
        C("failure: a stale plan rejects prewrite; a failed migration retains the old assembly; any postwrite fault "
          "halts the world; cancellation is TooLate at Applying",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AStalePlanIsRejectedBeforeAnyWrite",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy",
          "probe:Faults/narrative/gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image"),
        C("postconditions: a concurrent observer sees exactly the old or the new image, and an injected ECB failure "
          "sees neither a partial new image nor resumed execution (the catalogue's probe)",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AConcurrentObserverNeverSeesAMixedAssembly",
          "dotnet:GameCore.Execution.Tests.Observation.ConcurrentObservationTests.ConcurrentReadersAlwaysLeaseACompleteImageOfTheirOwnToken",
          "probe:W5Gate/w5gate-postwrite-fault-fail-stops-the-world"),
    ], suites=["TEST-009", "TEST-016"]),
    "O-12": R("SpawnOrDespawn", [
        C("preconditions: a boundary via a composition proposal; domain systems request it for the next boundary "
          "and never for a recursive closure; spawn uses the current recipe fingerprint",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.AnUnchangedInheritanceIsAHitAndAChangedOneIsAStaleRecomputation",
          "probe:W2Gate/gate2-forward-provider-and-spawned-target"),
        C("ordering and visibility: the target first becomes query-visible with its complete effective recipe on the "
          "first visible epoch",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AFutureSpawnAppearsFullyAssembledInItsFirstVisibleEpoch",
          "probe:Conformance/conformance/cards/spawn-seat-d",
          "probe:Conformance/conformance/traversal/spawn-runner-c"),
        C("failure: an unknown or stale recipe/handle rejects; cancel before Applying",
          "dotnet:GameCore.Derivation.Tests.DerivedRecipeCacheTests.IsCurrentRejectsAVariantResolvedUnderAnotherFingerprint",
          "probe:CatalogCoverage/catalogCoverage/catalog-coverage-unknown-recipe-refused"),
        C("postconditions: despawn closes the route, fences auxiliaries and destroys only recipe-owned storage (the "
          "catalogue's probe: a spawn prepared before a mode/provider edit rederives instead of exposing stale "
          "capabilities)",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.ADespawnedHandleIsRejectedForeverAndOnlyItsOwnStorageIsDestroyed",
          "probe:Conformance/conformance/cards/provider-loss/world",
          "probe:W4Gate/w4-automatic-inheritance-and-the-future-target"),
    ], suites=["TEST-004", "TEST-020"]),
    "O-13": R("SubmitCommand", [
        C("preconditions: control lane; validate world/sequence/schema/route/capacity and dedup before enqueue, "
          "with no domain mutation until the admitted step",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.UnknownAndRetiredRoutesAndSchemaMismatchesAreRefused",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ExpectedDomainVersionRefusesStaleCommandsAndChangesIdempotencyIdentity",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.CapacityBackpressureRecordsNothing"),
        C("ordering and visibility: an envelope becomes an admission/result handle",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.PendingRowsAreReportedInCanonicalAdmissionOrder",
          "probe:W2Gate/gate2-one-bounded-command-committed"),
        C("failure: malformed or retired routes reject; a full reliable buffer backpressures; preexecution "
          "cancellation returns Cancelled; a duplicate retrieves its result",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.ADuplicateRequestKeyReturnsItsRecordedResult",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ACommandThatOverflowsItsIngressLaneIsRefusedBeforeTheStep",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate"),
        C("postconditions: a repeated transfer command changes inventory once (the catalogue's probe)",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.ADuplicateCommandReturnsItsRecordedResultAndExecutesOnce",
          "probe:Cards/cards-duplicate-command-transfers-once",
          "probe:Cards/cards-one-command-commits-both-sides"),
    ], suites=["TEST-002", "TEST-013"]),
    "O-14": R("Advance", [
        C("preconditions: control lane then boundary then execution stage; Running with no plan Applying; FixedStep "
          "capacity/catch-up or CommandDriven demand; the input prefix is sealed",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.CommandDrivenIdleExecutesZeroStepsOverManyFrames",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepCatchUpIsBoundedAndDebtIsNeverDropped",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.OneAdmittedCommandAdvancesExactlyOneStepAndConsumesItsSeal"),
        C("ordering and visibility: zero or more step attempts",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.CommandDrivenWorldAdmitsDemandUpToItsPerPumpLimit",
          "probe:W2Gate/gate2-idle-world-performs-zero-steps"),
        C("failure: paused or no demand returns NoWork; excessive debt is observable; a timing error rejects the "
          "host sample; once executing, cancellation is TooLate",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepRejectsABackwardsHostClock",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.ExecutionDriverRetainsTimeDebtAndStopsDispatchAtTheFailingEntry"),
        C("postconditions: one idle second yields zero command-driven steps and a bounded fixed catch-up (the "
          "catalogue's probe)",
          "probe:WorldDispatch/world-command-driven-idle-zero-steps",
          "probe:WorldDispatch/world-fixed-step-debt-and-clock",
          "probe:Cards/cards-idle-world-performs-zero-steps"),
    ], suites=["TEST-011", "TEST-022"]),
    "O-15": R("ExecuteStage", [
        C("preconditions: an execution stage; DAG predecessors and dependencies satisfied, owner/access/epoch "
          "valid and all produced handles registered",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.WellFormedPlanProjectsOntoTheEpochBoundTable",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.DispatchThroughTheSeamRejectsAStaleEpoch",
          "unity:GameCore.Unity.Runtime.Tests.Time.ScheduleDispatchAdapterTests.TheAdapterUsesTheCompiledOrderAndNotTheDeclarationOrder"),
        C("ordering and visibility: a sealed step, bindings and buffers become tracked jobs and deferred operations",
          "unity:GameCore.Unity.Runtime.Tests.Time.ScheduleExecutionTests.NeitherDisjointJobReceivesTheOthersHandleAndBothWritesSurvive",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.DeferredStructuralPlaybackIsCanonicalAndWaitsForItsProducer",
          "probe:W2Gate/gate2-ordered-dispatch-and-dependent-read"),
        C("failure: an expected domain rejection emits a result without a write; an unexpected exception after "
          "writes faults the step and the world",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.ManagedSystemThatWritesThenThrowsStopsTheStepAndFaultsTheWorld",
          "unity:GameCore.Unity.Runtime.Tests.Faults.GuardedDispatchFaultTests.AStructuralPlaybackFaultStopsTheStepCommitAndKeepsTheQuarantine",
          "probe:Faults/narrative/gc017-structural-playback-fault-stops-the-step-commit"),
        C("postconditions: an internal stage cannot independently retry or cancel memory users, and conflicting "
          "stages reject at assembly while independent jobs overlap without missing dependencies (the catalogue's "
          "probe)",
          "dotnet:GameCore.Planning.Scheduling.Tests.ScheduleCompilerTests.ValidDisjointPartitionsMayOverlapWithoutAnEdge",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.PendingJobsStayTrackedUntilTeardownCompletesThem",
          "probe:Conformance/conformance/traversal/numeric-acceleration-sequence/world"),
    ], suites=["TEST-012", "TEST-013", "TEST-018"]),
    "O-16": R("CommitStep", [
        C("preconditions: a boundary; required producers/consumers/structural buffers complete and all public data "
          "consistent",
          "dotnet:GameCore.Execution.Tests.BoundedMessageBufferTests.UndrainedReliableRowsFailTheCommitValidation",
          "dotnet:GameCore.Execution.Tests.GuardedDispatchPlanTests.ProducerWithoutItsConsumerStageFailsDrainValidation"),
        C("ordering and visibility: events and the image are exposed together as a step id, committed events and "
          "one snapshot",
          "dotnet:GameCore.Execution.Tests.CommittedEventStoreTests.OneBuildProducesCanonicallyOrderedEventsForTheCommittedStep",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.PublishingOneStepExposesExactlyOneImage",
          "probe:W3Gate/w3-narrative-state-change-through-committed-snapshot"),
        C("failure: unconsumed reliable data or a failure faults rather than publishing partial success; a mutated "
          "step is never retried",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.UnconsumedDeclaredBufferFaultsTheWorldInsteadOfPublishingPartialSuccess",
          "unity:GameCore.Unity.Runtime.Tests.Messages.WorldMessagePlaneTests.AReliableBufferThatCannotBeDrainedFaultsInsteadOfPublishing",
          "dotnet:GameCore.Planning.Tests.PlanStateMachineTests.RejectionIsOnlyLegalBeforeLiveWritesAreClaimed"),
        C("postconditions: a card trade emits no success event on a rejected staged write set (the catalogue's "
          "probe)",
          "probe:Cards/cards-rejected-settlement-changes-nothing",
          "probe:Conformance/conformance/cross/reward-enqueue",
          "probe:Conformance/conformance/cards/scoring-lifecycle/world"),
    ], suites=["TEST-013", "TEST-014"]),
    "O-17": R("Observe", [
        C("preconditions: any host thread through the synchronized publication pointer; a token retained with no "
          "direct mutable state access; disposal releases the lease",
          "dotnet:GameCore.Execution.Tests.PublicationBoundaryTests.MainThreadDisciplineIsExplicitlyCaptured",
          "dotnet:GameCore.Execution.Tests.Observation.SnapshotRetentionTests.RetainedIsABoundedCopyAReaderCanEnumerateSafely",
          "probe:Gc019/gc019-presentation-reads-the-committed-snapshot"),
        C("ordering and visibility: an immutable lease or page",
          "dotnet:GameCore.Execution.Tests.Observation.WorldObservationTests.ABoundaryLeaseCarriesTheImageItsEventsAndTheQueueFactsTogether",
          "probe:W5Gate/w5gate-pinned-snapshots-are-read-only"),
        C("failure: an expired cursor/token returns an explicit error or resync token and allocation pressure "
          "backpressures; read cancellation releases the lease; repeated reads of the same token are identical",
          "dotnet:GameCore.Execution.Tests.Observation.WorldObservationTests.ResynchronizationNamesTheNewestImageAndTheGapItCannotRecover",
          "dotnet:GameCore.Execution.Tests.Observation.SnapshotRetentionTests.LeasePoolBackpressureIsAValueThatNeverOverwritesLeasedMemory",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.ObservationReaderReturnsExpiryAndBackpressureInsteadOfThrowing"),
        C("postconditions: an observer during a reconfigure never sees a mixed epoch (the catalogue's probe)",
          "unity:GameCore.Unity.Runtime.Tests.Assembly.AssemblyPublisherTests.AConcurrentObserverNeverSeesAMixedAssembly",
          "probe:Conformance/conformance/cards/mode-directions/setup-0",
          "probe:Gc019/gc019-presentation-reads-the-committed-snapshot"),
    ], suites=["TEST-014"]),
    "O-18": R("CancelOperation", [
        C("preconditions: control lane with a known world ledger, decided against the Applying or execution cutoff, "
          "with pending staged work invalidated",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.AdmittedCancellationBindsItsIdentityAndKeepsTheIssuerSequenceInStep",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.CancellingAStagedOperationReleasesItsGatedLeases"),
        C("ordering and visibility: Cancelled or TooLate plus status",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationDecidedAtTheApplyingLatchIsTooLateAndRecordsWhy"),
        C("failure: an unknown or expired ID is an explicit error; repeated cancel returns the same terminal "
          "result; too-late cancellation never implies rollback",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.CancellingAnExpiredOperationReportsExpiry",
          "dotnet:GameCore.Composition.Tests.CancellationIdentityTests.CancellationRetransmissionCoalescesToTheOriginalOutcomeWithoutRepeatingTheCutoff",
          "probe:Faults/narrative/gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication"),
        C("postconditions: race permutations produce one legal outcome (the catalogue's probe)",
          "dotnet:GameCore.Execution.Tests.RequestLedgerTests.CancellingPendingWorkLeavesSettledRowsUntouched",
          "probe:Faults/narrative/gc017-cancellation-before-the-cutoff-releases-staged-work"),
    ], suites=["TEST-002", "TEST-009", "TEST-016"]),
    "O-19": R("StopWorld", [
        C("preconditions: control lane to boundary; close all ingress, settle jobs, cancel queued commands and "
          "retract assembly and reverse dependency resources",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.StopClosesIngressSettlesJobsAndDisposesStorage",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.SettleStepDecidesWhetherTheWorldIsAskedToReachAStepBoundary"),
        C("ordering and visibility: Stopping then Disposed, or a blocked/quarantined result",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.QuarantineRetainsUntilItsUsersEndAndThenAllowsRetirement",
          "probe:W1Gate/gate-teardown-settles-and-disposes"),
        C("failure: repeated stop joins the same attempt; no cancellation once stopping; stuck users remain pinned; "
          "cleanup failure is observable",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.AFaultedStepSettlementReportsTheTeardownAsUnsettled",
          "unity:GameCore.Unity.Runtime.Tests.GuardedDispatchTests.DirectDisposalSettlesFaultedJobsAndRetiresTheWorld"),
        C("postconditions: a stalled job prevents native buffer free (the catalogue's probe)",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.StalledJobBlocksStopInsteadOfBeingFreedOnTimeout",
          "probe:LifecycleStress/lifecycle-stress-stalled-job-retains-buffers"),
    ], suites=["TEST-015", "TEST-016", "TEST-018"]),
    "O-20": R("CaptureCheckpoint", [
        C("preconditions: a boundary with stable state, bounded data, an explicit queue/outbox disposition and all "
          "required serializers available; copy then serialize off lane",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ACaptureAtACommittedBoundaryProducesAVerifiedDocument",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.AnIncompleteCodecSetIsRefusedBeforeAnythingIsRead",
          "probe:Gc018/gc018-committed-boundary-capture"),
        C("ordering and visibility: a versioned blob and checksum",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.TheHeaderCarriesTheContentRevisionCountAndBothProtocolBytes",
          "probe:RecoverySmoke/recovery-smoke-published-envelope-reloads-from-disk"),
        C("failure: copy or serialization errors produce no checkpoint; cancel before the completed file "
          "publication and remove the temporary artifact",
          "dotnet:GameCore.Recovery.Fixtures.Tests.CheckpointStoreTests.ASuccessfulPublishLeavesNoTemporaryArtifactBehind",
          "probe:Recovery/gc027-capture-copy-fault-produces-no-checkpoint",
          "probe:Recovery/gc027-publication-fault-keeps-the-previous-document"),
        C("postconditions: active and dormant state round-trip without handles (the catalogue's probe)",
          "dotnet:GameCore.Execution.Tests.CheckpointCaptureTests.ActiveAndDormantStateRoundTripsWithoutHandles",
          "probe:Gc018/gc018-restore-preserves-dormant-slots",
          "probe:Recovery/gc027-active-and-dormant-state-survive-the-recovery"),
    ], suites=["TEST-017"]),
    "O-21": R("RestoreCheckpoint", [
        C("preconditions: control lane and an unexposed new world reaching a boundary; validate schema/catalog, "
          "rebuild identities/composition, repair references and restore state and cursors before Running",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.ASelfConsistentDocumentPlansIntoAFreshSessionWithItsDormantState",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.ACatalogFingerprintMismatchRefusesUnlessTheCallerRelaxesIt",
          "probe:Gc018/gc018-restore-happens-into-a-new-unexposed-world"),
        C("ordering and visibility: a new WorldId and a fully published restored world",
          "probe:RecoverySmoke/recovery-smoke-recover-publishes-a-new-session",
          "probe:Gc018/gc018-restored-world-publishes-and-advances"),
        C("failure: an unsupported schema/migration rejects without affecting the existing world; cancel destroys "
          "the staged world; a duplicate returns the same restored session",
          "dotnet:GameCore.Execution.Tests.CheckpointRestorePlanTests.AnAmbiguousMigrationGraphIsRefusedWithTheMigrationPlanCode",
          "dotnet:GameCore.Execution.Tests.RestoreReservationLedgerTests.ARetransmittedRestoreReturnsTheSameFreshReservation",
          "probe:Gc018/gc018-corrupt-reference-rejects-restore"),
        C("postconditions: an old callback cannot target a restored entity (the catalogue's probe)",
          "probe:Gc018/gc018-old-callbacks-cannot-target-the-new-session",
          "probe:RecoverySmoke/recovery-smoke-recovered-world-is-authoritative",
          "probe:Recovery/gc027-recovered-world-refuses-an-old-session-observation"),
    ], suites=["TEST-002", "TEST-017"]),
    "O-22": R("RecoverWorld", [
        C("preconditions: control lane; requires an explicit source and reuses the restore/create procedures; old "
          "storage never resumes and blocked old resources are retained until safe",
          "probe:RecoverySmoke/recovery-smoke-refuses-a-live-source",
          "probe:RecoverySmoke/recovery-smoke-refuses-a-stopped-source",
          "unity:GameCore.Unity.Runtime.Tests.Recovery.InitialDefinitionRecoveryTests.ARecoveryFromALiveWorldIsRefused"),
        C("ordering and visibility: a stopped old world plus a new session",
          "probe:RecoverySmoke/recovery-smoke-restart-publishes-a-new-session",
          "probe:Recovery/gc027-restart-from-the-store-recovers-without-in-process-state"),
        C("failure: a failure leaves the old world Faulted and reports the new attempt's failure; no implicit "
          "effects replay; cancel only before the new publication",
          "unity:GameCore.Unity.Runtime.Tests.Recovery.InitialDefinitionRecoveryTests.AFailedReferenceRepairNeverExposesARunningWorld",
          "probe:Recovery/gc027-recovery-publication-fault-keeps-the-registry-unchanged",
          "probe:Recovery/gc027-restart-without-a-document-or-incompatible-content-exposes-nothing"),
        C("postconditions: a mid-apply fault recovery yields a new identity and the last checkpoint state (the "
          "catalogue's probe)",
          "probe:Recovery/gc027-postwrite-apply-fault-never-exposes-a-destination",
          "probe:Recovery/gc027-recovered-world-steps-its-engine-once-per-admitted-step",
          "probe:RecoverySmoke/recovery-smoke-teardown-is-clean"),
    ], suites=["TEST-016", "TEST-017"]),
    "O-23": R("BindServiceOrAcquireLease", [
        C("preconditions: during prepare or an authorized active control callback with visibility/dependency rules "
          "checked and the owner and disposer recorded before the value is exposed",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.PrivateProviderInAnAncestorIsInvisible",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ServiceIsolationBoundaryBlocksAncestorProvidersButNotBoundaryProviders",
          "dotnet:GameCore.Contracts.Tests.ServiceBindingLeaseTests.ABindingCarriesContractProviderActivationEpochAndLease"),
        C("ordering and visibility: an epoch-bound binding or lease",
          "dotnet:GameCore.Contracts.Tests.ServiceBindingLeaseTests.ARebindIsObservableAsADifferentActivationEpochUnderTheSameLeaseIdentity",
          "dotnet:GameCore.Composition.Tests.ResourceGateTests.RetiringAnInstanceDisposesEachLeaseOnceInReverseAcquisitionOrder"),
        C("failure: a conflicting or missing binding rejects; a late acquired result releases immediately; a "
          "repeated resource lease request by the same operation/key coalesces",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.SameScopeDuplicateSingleBindingsAlwaysConflict",
          "dotnet:GameCore.Planning.Tests.MigrationAndAcquisitionTests.AFailedAcquisitionIsCountedAndLateAcquisitionIsTooLate",
          "dotnet:GameCore.Composition.Tests.DiagnosticContractTests.AMissingRequiredProviderDiagnosticNamesTheContractAndZeroProviders"),
        C("postconditions: disposal happens once and service isolation is unchanged between modes (the catalogue's "
          "probe)",
          "dotnet:GameCore.Execution.Tests.WorldResourceLedgerTests.ResourcesAreDisposedAtMostOnce",
          "dotnet:GameCore.Composition.Tests.ServiceResolutionTests.ServiceResolutionIsIdenticalInBothPropagationModes",
          "probe:LifecycleStress/lifecycle-stress-acquisitions-traced-to-retirement"),
    ], suites=["TEST-003", "TEST-015"]),
    "O-24": R("CompleteAsyncWork", [
        C("preconditions: control lane; verify session/generation/activation/operation before installing data; an "
          "active gameplay completion becomes a typed command, not a direct ECS write",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ACompletionFromAStaleActivationIsDiscardedAndReleased",
          "dotnet:GameCore.ReferenceSeams.Tests.SeamContractTests.CallbackGateRejectsForeignWorldAndStaleActivation"),
        C("ordering and visibility: an accepted staged result or a discarded completion",
          "dotnet:GameCore.Composition.Tests.LifecycleStressTests.ACompletionStampedByAnotherWorldIncarnationIsDiscarded",
          "probe:Gc019/gc019-late-asset-completion-cannot-write-a-retired-world"),
        C("failure: a stale or cancelled result is discarded with cleanup and a duplicate completion idempotently "
          "ignores or releases only the newly supplied lease reference, with no auto resurrection",
          "dotnet:GameCore.Execution.Tests.Delivery.DurableOutboxTests.CommittingOneObligationTwiceTracksItOnce",
          "dotnet:GameCore.Adapters.Tests.AdapterContractTests.ALateAssetCompletionAfterRetirementInstallsNothing",
          "probe:Gc019/gc019-input-completion-from-a-retired-activation-is-discarded"),
        C("postconditions: 100 late completions yield zero stale writes (the catalogue's probe)",
          "dotnet:GameCore.Composition.Tests.LifecycleStressTests.AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement",
          "probe:LifecycleStress/lifecycle-stress-delayed-completions-are-discarded",
          "probe:Recovery/gc027-recovered-world-refuses-an-old-session-observation"),
    ], suites=["TEST-002", "TEST-015"]),
    "O-25": R("ExplainOrInspectOperation", [
        C("preconditions: a pure immutable query that distinguishes published state from staged status explicitly",
          "dotnet:GameCore.Composition.Tests.Diagnostics.StagedOperationStatusTests.AStagedProposalIsReportedAsStagedAndNeverAsPublishedState",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.TheExplainReaderPagesBoundedRecordsAndLabelsItsSource"),
        C("ordering and visibility: an explanation, status or diagnostics",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceExplanationReaderTests.ExplainAnswersTheFrozenSeamWithWinnersLosersAndKeys",
          "probe:Narrative/narrative-chapter-one-mounted-and-published"),
        C("failure: an expired or unknown token is explicit and cancellation has no semantic effect; the same "
          "retained token gives the same explanation",
          "dotnet:GameCore.Composition.Tests.Diagnostics.StagedOperationStatusTests.AHandleReadResolvesTheSameStatusAsTheIdentityRead",
          "dotnet:GameCore.Composition.Tests.Diagnostics.ProvenanceStoreTests.RepublishingOneTokenReplacesItInPlace",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.ARandomizedSmallOperationSequenceKeepsEveryExplanationReconstructable"),
        C("postconditions: every effective capability has complete provenance and losing or excluded candidate "
          "reasons (the catalogue's probe)",
          "dotnet:GameCore.Derivation.Tests.ProvenanceTests.AnExplanationNamesTheProviderRuleScopePathStratumAndRecipeHash",
          "unity:GameCore.Observation.Tests.ObservationCursorAndProvenanceTests.DerivationProvenance_ReconstructsEveryEffectiveCapability(\"cards\")",
          "probe:Conformance/conformance/narrative/trace-digest"),
    ], suites=["TEST-004", "TEST-005", "TEST-014"]),
    "O-26": R("SetWorldRunState", [
        C("preconditions: control lane to boundary; only Running<->Paused, waiting for the current step commit or "
          "fault first, with commands queueing within capacity while paused",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.InvalidLifecycleRequestsAreRejectedWithoutMutation",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.AFullPendingQueueBackpressuresInsteadOfDroppingInput"),
        C("ordering and visibility: a new host lifecycle status plus an unchanged current snapshot token; retained "
          "simulation debt with the host-time sample origin reset on resume so paused elapsed time adds no debt; "
          "same state returns NoChange and no step or epoch changes merely for pausing",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.PauseChangesHostStatusOnlyAndAddsNoDebt",
          "dotnet:GameCore.Execution.Tests.TemporalAccumulatorTests.FixedStepPauseAddsNoDebtAndPreservesTheAccumulator",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.PauseAppliesEachClocksDeclaredWakePolicyAndAddsNoDebt"),
        C("failure: Faulted/Stopping/Disposed rejects; cancellable until the state commit and then TooLate; a "
          "repeat retrieves the result",
          "unity:GameCore.Unity.Runtime.Tests.WorldHostTests.InvalidLifecycleRequestsAreRejectedWithoutMutation",
          "dotnet:GameCore.Composition.Tests.ControlLaneTests.CancelBeforeTheCutoffIsCancelledAndImmediatelyAfterIsTooLate"),
        C("postconditions: a pause during a job waits, a paused duration adds no steps or debt, and a resume "
          "processes retained debt and inputs under the usual limits (the catalogue's probe)",
          "dotnet:GameCore.Composition.Tests.TeardownAndQuarantineTests.ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes",
          "unity:GameCore.Unity.Runtime.Tests.Time.TemporalDriverTests.FixedStepRetainsDebtAcrossFramesAndBoundsCatchUp",
          "probe:Traversal/gc020-one-simulation-per-admitted-step",
          "note: 08's phrase \"pause while a tracked job is executing\" is a Unity-host boundary; the dotnet half "
                  "proves the job pin and the debt arithmetic, the Unity half proves the host status change does "
                  "not touch epoch or step. The archive holds no single run that pauses a world with a tracked job "
                  "in flight (see the task hand-off)."),
    ], suites=["TEST-009", "TEST-011", "TEST-018"]),
}
