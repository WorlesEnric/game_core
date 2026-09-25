# GC-014 HANDOFF — complete live lifecycle and required-service closure (Wave 4)

Branch `gc-014` (worktree `/Users/yangcao/wkspace/gc-wt/gc-014`), based on `main` = Waves 0–3 integrated
(`b697ff6`).

**Status of every executable check in this document: `NotRun (pending orchestrator build host)`.** This host has no
Unity, no .NET SDK, no Mono and no C# compiler, so nothing in this change set has been compiled, imported or
executed here. The only things that ran are interpreter-level host checks, recorded verbatim in
`artifacts/gc-014/static-checks.log`: `python3 tools/check_game_core_csharp.py` (309 files, `ok`),
`python3 tools/validate_game_core_docs.py --self-test` and `python3 tools/validate_game_core_docs.py` (both pass),
`bash -n` over all 17 `tools/**/*.sh`, a brace/paren balance scan over every file this task added or edited, and a
repository-wide `.meta` GUID uniqueness scan (465 GUIDs, zero duplicates). None of those is a build or a test result.

## 1. Summary

`docs/game-core/09-implementation-guide.md` §GC-014 asks for five things, and this change delivers all five:

1. **All P-046 installation transitions over the control/publication path, invalid transitions rejected.**
   `ActivationLedger` holds one *current* plus one *staged candidate* activation per installation. Every edge is
   requested through `InstallationStateMachine`, so the P-046/06 §1 diagram is the only authority on what is legal
   and an illegal edge comes back as a value (`LifecycleTransition`, `Code == OwnershipConflict`) with the stored
   state untouched.
2. **Removing a required provider makes consumers WAIT in the same publication; a compatible provider's return
   resumes them.** `InstallationLifecycleCoordinator.Commit` walks one frozen plan: the consumer lands in
   `WaitingForDependencies` with an empty binding list and its contribution retracted, and
   `PublishedOperation.WaitingConsumers` names it — in the *same* `PublishedOperation` as the provider's removal.
   A compatible re-mount makes it `Active` again with its bindings restored, named by
   `PublishedOperation.ResumedConsumers`.
3. **Suspension retracts active behavior; late completions cannot resurrect it; a blocked job prevents buffer
   release.** Suspension publishes `Suspended`, retires the callback-gate activation and closes the installation's
   command routes, so a token minted before the suspend is discarded at dispatch *and* at completion. A tracked job
   that still reaches a lease quarantines that lease: `TeardownReport.Code == TeardownBlocked`,
   `DisposeSettled == false`, `BlockedByJobFence == true`, and the installation stays `Retiring` — never `Disposed`.
4. **Repeated operations obey the ledger.** A retransmission returns the original row (`AdmissionKind.Retransmission`)
   and starts no new work; reusing an operation identity with different input is `IdempotencyConflict`; a repeated
   illegal lifecycle edge is refused without changing state; a repeated commit for one operation is refused
   (`RepeatCommitRefusalCount`).
5. **Teardown reports unreleased/quarantined resources accurately and never claims disposal while a job owns a
   buffer.** `TeardownSequencer` executes the six P-048 steps in order and records each one; `Disposed` is reported
   only when every step completed *and* every resource settled.

Actual Unity-world tests exist for every transition and every invalid transition, in **both** families
(`NarrativeLifecycleScenario`, `CardLifecycleScenario` — twelve named observations each over a real owned world),
asserted by the EditMode assembly `GameCore.Lifecycle.Tests`.

### 1.1 Layering (all of it Unity-free except the last row)

| Layer | Files | What it owns |
|---|---|---|
| Activation | `Packages/com.gamecore.composition/Runtime/Lifecycle/ActivationLedger.cs` | `ActivationAttempt`, the current/candidate pair, and every P-046 transition requested as a value |
| In-flight | `.../Lifecycle/JobFenceRegistry.cs` | Tracked jobs and the resources they may still reach; the fence a teardown honours |
| Quarantine | `.../Lifecycle/QuarantineRegistry.cs` | Bounded, observable retained references; refuses admission at its ceiling instead of dropping them (06 §6) |
| Teardown | `.../Lifecycle/TeardownSequencer.cs` | The P-048 order, one recorded step at a time, plus the explicit quarantine release path |
| Closure | `.../Lifecycle/ServiceClosureDelta.cs` | Waiting/resumed consumers, lifecycle edges and binding changes of one publication (P-012, P-026) |
| Coordination | `.../Lifecycle/InstallationLifecycleCoordinator.cs` | `Stage` / `Commit` / `Abort` over the frozen plan; per-operation idempotence |
| World seam | `.../Lifecycle/LifecycleWorldBinding.cs` | `ILifecycleWorldBinding` (the four steps only a world can do) and `CompositionOnlyLifecycleBinding` |
| Publication | `.../Operations/CompositionHost.cs` | Exposes `Lifecycle`, stages on admission, commits/aborts at the boundary, carries the report on `PublishedOperation` |
| Unity glue | `Packages/com.gamecore.unity.runtime/Runtime/Lifecycle/*.cs` | Ingress closure by declared owner, step settling, user fencing, attributed-row retraction, `JobHandle` fence bridge, and `LifecycleController` |
| Family worlds | `Packages/com.gamecore.gameplay.{narrative,cards}/Fixtures/Runtime/*LifecycleScenario.cs` | The twelve observations per family over a real owned world |
| Suites | `Packages/com.gamecore.composition/Tests/Lifecycle/*.cs`, `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Lifecycle/*` | 28 pure tests + 2×29 EditMode cases |

## 2. Files created

Kernel (`GameCore.Composition`, all Unity-free):

- `Packages/com.gamecore.composition/Runtime/Lifecycle/ActivationLedger.cs`
- `Packages/com.gamecore.composition/Runtime/Lifecycle/JobFenceRegistry.cs`
- `Packages/com.gamecore.composition/Runtime/Lifecycle/QuarantineRegistry.cs`
- `Packages/com.gamecore.composition/Runtime/Lifecycle/TeardownSequencer.cs`
- `Packages/com.gamecore.composition/Runtime/Lifecycle/ServiceClosureDelta.cs`
- `Packages/com.gamecore.composition/Runtime/Lifecycle/InstallationLifecycleCoordinator.cs`
- `Packages/com.gamecore.composition/Runtime/Lifecycle/LifecycleWorldBinding.cs`
- `Packages/com.gamecore.composition/Tests/Lifecycle/ActivationLedgerTests.cs` (11 tests)
- `Packages/com.gamecore.composition/Tests/Lifecycle/TeardownAndQuarantineTests.cs` (9 tests)
- `Packages/com.gamecore.composition/Tests/Lifecycle/ServiceClosureDeltaTests.cs` (8 tests)
- the matching `.meta` files (folder `Lifecycle.meta` under both `Runtime/` and `Tests/`, plus one per `.cs`)

Unity glue (`GameCore.Unity.Runtime`, namespace `GameCore.Unity.Runtime.Lifecycle`):

- `Packages/com.gamecore.unity.runtime/Runtime/Lifecycle/UnityLifecycleWorldBinding.cs`
- `Packages/com.gamecore.unity.runtime/Runtime/Lifecycle/LifecycleJobFence.cs`
- `Packages/com.gamecore.unity.runtime/Runtime/Lifecycle/LifecycleController.cs`
- `Packages/com.gamecore.unity.runtime/Runtime/Lifecycle.meta` and the three `.cs.meta` files

Family worlds:

- `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeLifecycleScenario.cs` (+ `.meta`)
- `Packages/com.gamecore.gameplay.cards/Fixtures/Runtime/CardLifecycleScenario.cs` (+ `.meta`)

Qualification project:

- `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/Lifecycle/GameCore.Lifecycle.Tests.asmdef` (+ `.meta`)
- `.../Tests/Lifecycle/NarrativeLifecycleIntegrationTests.cs` (+ `.meta`)
- `.../Tests/Lifecycle/CardLifecycleIntegrationTests.cs` (+ `.meta`)
- `.../Tests/Lifecycle.meta`

Tooling and evidence:

- `tools/run_gc014_checks.sh`
- `artifacts/gc-014/HANDOFF.md` (this file), `artifacts/gc-014/static-checks.log`

## 3. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `Packages/com.gamecore.composition/Runtime/Operations/CompositionHost.cs` | adds `public InstallationLifecycleCoordinator Lifecycle { get; }`; `SubmitInternal` calls `Lifecycle.Stage(plan)`; `Publish` calls `Lifecycle.Commit`/`Abort` and passes the report to `PublishedOperation`; `Cancel` and `RebuildStaged` abort the affected plan's candidate; `PublishedOperation` gains `Lifecycle`, `WaitingConsumers`, `ResumedConsumers` | one publication must drive the whole P-046 closure, and a caller needs to read it |
| `Packages/com.gamecore.composition/Runtime/Lifecycle/ManagedResources.cs` | adds `ResourceLedger.TryGetRecord(Id128, out WorldResourceRecord)` | teardown reports a retained reference's key/bytes from the ledger instead of guessing |
| `tools/check_game_core_csharp.py` | `Packages/com.gamecore.composition` added to `TARGETS` and its `Runtime` to `engine_free` (commit `shared: …`) | the new kernel lifecycle sources get the host-side balance/forbidden-construct/engine-free checks |
| `Packages/com.gamecore.composition/Tests/Lifecycle/ServiceClosureDeltaTests.cs` | tightened after the kernel fix in §5.1 | pins the de-duplication directly |

**No other shared file changed.** No `.asmdef` was modified: `GameCore.Composition` and `GameCore.Unity.Runtime`
already exist, the family fixtures assemblies already reference what the scenarios need, and the new test assembly is
a new `.asmdef`. `unity/GameCore.Validation/Packages/manifest.json` was **not** touched: no new package was added,
so no `testables` entry and no `packages-lock.json` regeneration is required.

## 4. Contract changes (§ for the orchestrator)

**Additions only; no existing member changed signature or meaning.**

`GameCore.Composition`:

- new public types `ActivationAttempt`, `ActivationLedger`, `TrackedJob`, `JobFenceRegistry`, `QuarantinedResource`,
  `QuarantineAdmission`, `QuarantineRegistry`, `TeardownStep`, `TeardownReport`, `TeardownSequencer`,
  `LifecycleStepSettlement`, `LifecycleIngressClosure`, `ILifecycleWorldBinding`, `CompositionOnlyLifecycleBinding`,
  `ContributionRetraction`, `LifecycleEdge`, `BindingDelta`, `ServiceClosureDelta`, `LifecycleSettings`,
  `StagedCandidate`, `LifecycleStageReport`, `LifecycleCommitReport`, `LifecycleAbortReport`,
  `InstallationLifecycleCoordinator`.
- `CompositionHost` gained `Lifecycle`; `PublishedOperation` gained `Lifecycle`, `WaitingConsumers`,
  `ResumedConsumers` (its constructor gained one parameter — callers inside this repository were all updated).
- `ResourceLedger` gained `TryGetRecord`.
- `ActivationLedger.Retire` is idempotent for an already-`Retiring`/`Disposed` activation.

`GameCore.Unity.Runtime.Lifecycle` (new namespace, new folder):

- `InstallationIngressOwners`, `UnityLifecycleWorldBinding`, `UnityTrackedJob`, `LifecycleJobFence`,
  `LifecycleRequestReport`, `LifecycleController`.

No type in `GameCore.Contracts` changed, so the frozen W0 seam and
`tests/GameCore.ReferenceSeams/api/GameCore.Contracts.api.txt` are untouched — and the `GameCore.Contracts` API
snapshot test therefore needs no regeneration.

## 5. Design decisions, and the one kernel bug this task found and fixed

### 5.1 `plan.RetiredInstances` names displaced *activations*, not removed installations

This is the most important thing in this handoff, because it was wrong first and would have made suspend
unrecoverable.

`CompositionEditApplier.Finish` adds an installation to `plan.RetiredInstances` whenever its previous state was
`Active` and (a) its next state is not `Active`, or (b) its `ActivationEpoch` changed. That covers *three* different
situations: a real removal (after-state `Retiring`/`Disposed`), a suspend or a lost required provider (after-state
`Suspended`/`WaitingForDependencies`), and an in-place replacement (after-state `Active`, new epoch). The first
version of `Commit` treated every entry as a removal, so a suspend ran the P-048 order *and then settled its own
activation to `Disposed`*; a later resume was refused from `Disposed` and could never restore execution authority,
while the published state read `Active`.

Fix (commit `070f8ce`), decided by the P-046/06 §1 text rather than by convenience:

- the P-048 order runs for **every** entry, with that entry's **own** activation stamp, so exactly the leases the
  displaced activation epoch acquired are the ones retired (P-005, P-048);
- `Activations.Retire` / `Settle` run only for an installation whose after-state is `Retiring`/`Disposed`
  (`IsRemoved`, one rule, used by both the coordinator and `ServiceClosureDelta`);
- an in-place replacement therefore emits no installation-level `Active -> Retiring` edge — the retired thing is the
  *activation*, which `LifecycleCommitReport.Teardowns` reports with the old stamp — and its displaced activation is
  not torn down twice;
- `ServiceClosureDelta` emits a disappearance edge and removed bindings only for an actual removal, so a suspend no
  longer reports its own closure twice.

A consequence to know when reading the tests: a `Suspend` plan's `RetiredInstances` still lists the suspended
installation, so any code that treats that list as "removals" is wrong. If a later wave wants a first-class
`DisplacedActivations` field on the plan DTO, this is the seam it belongs on; GC-014 deliberately did not add one
(the plan DTO is a frozen shared contract and the information is derivable from the before/after states).

### 5.2 Where the lifecycle is driven from

`CompositionHost.Publish` is the single publication boundary, so it — not a family scenario — calls
`Lifecycle.Commit(plan, token)` and `Lifecycle.Abort(plan, code)`, and `SubmitInternal` calls `Lifecycle.Stage(plan)`
at admission. That is what makes "in the same publication" a property of the code rather than of a test's ordering.
The Unity-side `LifecycleController` exists only to keep the three seams (control lane, world binding, derived
assembly publication) in the fixed order for a caller; it adds no state of its own.

### 5.3 `TeardownBlocked` is decided by *retention*, not by elapsed time

`TeardownSequencer` never reads a clock: the quarantine count, the fenced-resource set and `cleanup.Failed` decide
the outcome. A failed release is quarantined by `ResourceLedger.Retire` (it sets the record to `Quarantined` and
reports it in `Cleanup.Quarantined`), so a throwing disposer yields `TeardownBlocked`, not `ResourceUnavailable` —
`ResourceUnavailable` is reachable only when an unsettled step boundary is what blocked the pass. The pure suite
asserts both.

### 5.4 The world binding reports "I did not publish the retraction"

`ContributionRetraction` keeps `AttributedRows` (rows the *published assembly* attributes to the installation) apart
from `RetractedRows` (rows this call actually removed) and from `PublishedByBinding`. `GameCore.Unity.Runtime`'s
binding reports attributed rows and `PublishedByBinding == false`, because the retraction is carried by the assembly
publication the operation itself performs; a caller therefore never has to *assume* which of the two published it.

## 6. Exact commands for the Linux build host

Everything runs from the repository root. Nothing here has been run.

### 6.1 One command (GC-014's own surface)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet tools/run_gc014_checks.sh
```

It runs, in order:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-014/trx
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/gc-014/unity/resolve.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testResults artifacts/gc-014/unity/editmode-results.xml -logFile artifacts/gc-014/unity/editmode.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.Lifecycle.Tests \
  -testResults artifacts/gc-014/unity/lifecycle-results.xml -logFile artifacts/gc-014/unity/lifecycle-editmode.log
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

Do not add `-quit` to a test-run command (04 §10).

### 6.2 The pieces on their own

```sh
# the pure lifecycle suites (also compiled by the package glob in the dotnet solution)
dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release \
  --logger trx --results-directory artifacts/gc-014/trx/composition

# the lifecycle world suites alone, with the family slice hosts in the same assembly
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Lifecycle.Tests \
  -testResults artifacts/gc-014/unity/lifecycle-results.xml -logFile artifacts/gc-014/unity/lifecycle-editmode.log

# the surrounding qualification the wave gates already own (recommended for the wave record)
UNITY=<editor> DOTNET=<dotnet> PROBE_RUNS=5 tools/run_w3_gate.sh
```

### 6.3 What to read in the results when something fails

- A lifecycle failure inside a family reports one `*LifecycleStep` per observation, whose `Detail` carries every
  value the verdict was computed from (states, codes, counts, epochs). The EditMode assertion message includes the
  whole fact bag (`Facts.Describe()`), so the XML result is self-explanatory.
- `GameCore.Lifecycle.Tests` runs each family's committed generated catalog through its own host
  (`NarrativeScenarioHost.RunGeneratedCatalog` / `CardsScenarioHost.RunGeneratedCatalog`) in the same assembly, so a
  catalog regression shows up as a lifecycle-suite failure too.
- A pure-suite failure names the exact protocol sentence in the assertion message.

## 7. Requirement and test coverage mapping

| Normative requirement | Where implemented | Where observed |
|---|---|---|
| P-003 (three kinds of change) | `ContributionRetraction` / `TeardownReport` keep contribution retraction, resource disposal and gameplay effects distinct | pure: `AFaultedStepSettlementReportsTheTeardownAsUnsettled`, `SettleStepDecidesWhetherTheWorldIsAskedToReachAStepBoundary` |
| P-005 (generations/epochs; in-place replacement keeps the generation) | `ActivationAttempt.Generation/ActivationEpoch`, `ActivationStamp`, `IsInPlaceReplacement` | both families' `…-replacement-stages-while-old-runs` (`replacementEpochChanged`, `replacementGenerationUnchanged`) |
| P-006 (one publication series) | `CompositionHost.Publish` alone increments revision/epoch and drives the closure | both families' world steps (`laneJoined`), `…-teardown-settles-and-disposes` |
| P-007 (leases and async work tokens) | `ManagedResourceLease`, `ActivationStamp`, `AsyncWorkToken` re-checked at completion | `…-suspend-retracts-behavior` (`suspendLateCompletion` is a discard), `…-unload-closes-ingress-and-retracts` |
| P-011 (service visibility) | unchanged (`ServiceResolver`); the scenarios' required pair is a real `ServiceDependency` with `Required = true` | `…-provider-loss-makes-consumers-wait` |
| P-012 (dependency closure) | `ServiceClosureDelta`, `Commit`'s wait/resume transitions | `…-provider-loss-makes-consumers-wait`, `…-provider-return-resumes-consumers`; pure: `RemovingARequiredProviderMakesItsConsumerWaitInTheSamePlan`, `ReturningTheProviderResumesTheConsumerThatWaited` |
| P-025 (provider change) | `IsInPlaceReplacement`, `StageCandidate`/`CommitCandidate` | both families' `…-replacement-stages-while-old-runs` |
| P-029/P-030 (inert staging, atomic publication) | `ResourcePreparationSet`, `CallbackGate.CloseFence/OpenFence`, `Lifecycle.Stage` before `Commit` | `PreparedLeaseStaysInertUntilPublication` (existing), `…-replacement-stages-while-old-runs`, `StagingACandidateKeepsTheRunningActivationInCharge` |
| P-035 (world lifecycle) | unchanged; the scenarios assert `host.Lifecycle == Running` throughout | `worldLifecycle` fact in both families |
| P-046 (installation lifecycle, all edges) | `ActivationLedger` + `InstallationStateMachine` | pure: all 17 legal edges (`EveryStatePairMatchesTheLifecycleDiagramExactly`), `SuspendWalksThroughQuiescingAndResumeRecordsANewAttempt`, `CommitMovesTheCandidateInAndDisplacesTheRunningActivationToRetiring`, `AbortingACandidateFailsItAndLeavesTheRunningActivationInCharge`, `RetireWalksTheTeardownPathAndSettlingRequiresEveryResourceSettled`; world: all eleven behavioural steps in both families |
| P-047 (in-flight lifetime) | `CallbackGate`, `JobFenceRegistry`, `LifecycleJobFence`, ingress closure in `UnityLifecycleWorldBinding`, `LifecycleController.RefreshIngressOwners` | `…-suspend-retracts-behavior` (gate retired, late token discarded, and in the card family both real command routes of the table runtime genuinely retired then reopened), `…-unload-closes-ingress-and-retracts` (narrative: `DeclaredRoutesAreRetired` over the chapter's real `ChoiceRoute`), `…-blocked-job-prevents-buffer-release`, pure: `ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes` |
| P-048 (teardown order) | `TeardownSequencer` (six named steps) | pure: `TheTeardownReportNamesTheSixP048StepsInOrder`, `TeardownDisposesLeasesInReverseAcquisitionOrderAndSettlesCleanly`, `AThrowingDisposerKeepsItsReferenceAndIndependentCleanupContinues`; world: `…-unload-closes-ingress-and-retracts`, `…-teardown-settles-and-disposes` |
| P-050/P-051 (identity, idempotency, cutoffs) | `OperationLedger` (unchanged) + `InstallationLifecycleCoordinator`'s committed-operation set | `…-repeated-operations-obey-ledger` (`repeatRetransmissionKind`, `repeatSuspendRefusedCode`, `repeatUnmountRefusedCode`, `ledgerRowCount`); pure: `ARefusedEdgeCountsTheRefusalAndLeavesTheStoredStateUntouched` |
| 06 §6 (bounded quarantine registry) | `QuarantineRegistry` | pure: `QuarantineExhaustionAndDuplicateAdmissionAreRefusedWithoutDroppingReferences`, `QuarantineReleaseRemovesExactlyOneReferenceAndItsBytes`, `ReleaseInstanceReleasesOnlyThatInstancesEntries`; world: `…-blocked-job-prevents-buffer-release` |
| 06 §1 (Quiescing keeps the old assembly; prewrite abort reopens gates) | `Quiesce`, `ReopenFromQuiescing`, `Abort` | `AbortingACandidateFailsItAndLeavesTheRunningActivationInCharge`, `StagingIsRefusedForASuspendedActivation` |
| TEST-002 (identities, epochs, stale references) | epochs/generations per attempt, stale tokens discarded | `…-suspend-retracts-behavior`, `…-unload-closes-ingress-and-retracts`, `…-replacement-stages-while-old-runs` |
| TEST-003 (manifests and service resolution) | unchanged resolver; the required pair is declared through real manifests | `…-provider-loss-makes-consumers-wait`, `…-provider-return-resumes-consumers` |
| TEST-008 (incremental invalidation / moves) | GC-013's seam; the lifecycle passes invalidation deltas through it | not owned here (GC-013); the lifecycle scenarios do not move scopes |
| TEST-015 (lifecycle and managed resource teardown) | this task, in full | every lifecycle test named above, plus `…-teardown-settles-and-disposes` returning the registry to baseline |
| TEST-016 (fault injection and recovery boundaries) | `AssemblyFaultInjection` (GC-008) + the blocked-job and throwing-disposer paths here | `ABlockedJobPreventsTheBufferReleaseUntilTheJobCompletes`, `AThrowingDisposerKeepsItsReferenceAndIndependentCleanupContinues`, `AFaultedStepSettlementReportsTheTeardownAsUnsettled` |
| TEST-018 (Unity worlds, bootstrap, disposal) | the scenarios create, drive and dispose a real owned world per run | `…-teardown-settles-and-disposes` (`registryAfterTeardown`, `outstandingJobsAfterTeardown`, `retainedResourcesAfterTeardown`), `…-world-and-provider` (`registryBeforeCreate`), `idleSteps` |
| Operation catalogue O-04/O-05/O-06/O-07/O-18/O-19/O-23/O-24 | the transitions and cutoffs those operations specify | `…-resume-restores-behavior` (O-04), `…-replacement-stages-while-old-runs` (O-05), `…-suspend-retracts-behavior` (O-06), `…-unload-closes-ingress-and-retracts` (O-07), `…-repeated-operations-obey-ledger` (O-18), `…-teardown-settles-and-disposes` (O-19), `…-blocked-job-prevents-buffer-release` (O-23/O-24) |

Test-method counts, for the wave record: `ActivationLedgerTests` 11, `TeardownAndQuarantineTests` 9,
`ServiceClosureDeltaTests` 8 (28 pure); `NarrativeLifecycleIntegrationTests` 29 cases,
`CardLifecycleIntegrationTests` 29 cases (2 committed-catalog cases included), each EditMode case running against
both scenario runs.

## 8. Known gaps, assumptions and doc ambiguities

1. **The family lifecycle scenarios run over their package's generated-style catalog, not the Unity project's
   committed generated catalog.** `GameCore.Gameplay.{Narrative,Cards}.Fixtures` cannot reference an Assets
   assembly (`GameCore.Validation.Generated{ Cards}`), so `RunGeneratedCatalog`/`RunFixtureCatalog` on the *lifecycle*
   scenario differ only in their declaration identity sets. The EditMode suite compensates by additionally running
   each family's own host (`NarrativeScenarioHost.RunGeneratedCatalog`, `CardsScenarioHost.RunGeneratedCatalog`)
   through the committed catalog in the same assembly run, so both paths plus both lifecycle scenarios are covered
   on one revision. The scenarios' headers and the test doc comments state this explicitly; the entry-point *names*
   are kept identical to the family hosts' for readability, which is why the headers had to say it in words.
2. **The required-service pair for steps 4/5 is declared by the scenarios, not by the family vocabularies.**
   Neither family's real declarations contain a required-dependency edge (the narrative chapters declare no service
   export or dependency at all; the card table runtime declares its definition-lookup dependency as *optional*). The
   scenarios therefore install a private provider/consumer pair through real `PluginManifest`s — a real
   `ServiceDependency(Required = true)` resolved by the real `ServiceResolver`, with the provider's own capability
   and installation identity so it cannot contend with the family's own rows. That is a test fixture *around* a real
   mechanism, not a model of one, but a reviewer should know the edge under test is fixture-declared.
3. **Steps 4/5 for cards use `CardTableKeys.LookupContract` as the contract, with the real
   `CardTableDeclarations.RuleLibrary` manifest as the provider.** The consumer's capability
   (`cards.lifecycle-score`) uses the registered Int32-sum reducer and the registered predicate.
4. **Route-level P-047 closure is asymmetric between the families, deliberately.** The narrative scenario adds
   `NarrativeKeys.IngressOwner` (the owner of the slice's real `ChoiceRoute`) to the chapter installation's declared
   owners and asserts `DeclaredRoutesAreRetired`, so its suspend/unload retires a real route. In the card family the
   installation the lifecycle acts on is a *scoring* provider, which declares no state slot and therefore owns no
   command route at all; the family's two real routes belong to the table runtime (`CardTableKeys.TableOwner`). The
   card suspend step therefore closes the table runtime's ingress through the same binding a suspend calls and
   asserts both routes are genuinely retired, and the resume step asserts they are reopened — same mechanism, the
   installation that owns the routes. The step detail and the step's comment say so.
   `LifecycleController.RefreshIngressOwners` exists because of this: an installation mounted after the controller
   was constructed still needs its owners declared, so it is called at construction, before every lifecycle
   submission and after every publication (a redeclaration is idempotent).
5. **`plan.RetiredInstances` names displaced activations as well as removals** (§5.1). Both the coordinator and the
   closure delta decide removal with the same private `IsRemoved` rule. If a reviewer wants the distinction in the
   plan DTO itself, that is a contract change for a later wave; GC-014 deliberately did not make one.
6. **`ActivationLedger.Retire` is idempotent** for an already-`Retiring`/`Disposed` activation (it returns `Permit`
   and changes no counter). The alternative — refusing — would make the coordinator's post-teardown bookkeeping an
   illegal edge for a reason unrelated to the protocol.
7. **`tools/check_game_core_csharp.py` gained `Packages/com.gamecore.composition`** in `TARGETS` and its `Runtime`
   in `engine_free` (separate `shared:` commit). Purely additive: no existing target, rule or set membership changed.
   The package was verified engine-free before being added.
8. **The `GameCore.Unity.Runtime` lifecycle glue is compiled but not separately exercised by a dedicated test
   assembly of its own.** It is exercised end-to-end by the two family scenarios (which is where its behaviour is
   observable: ingress closure, step settling, fencing, attributed rows, the `JobHandle` bridge). A unit-level
   `GameCore.Unity.Runtime.Tests` case for `LifecycleJobFence` would need a real `JobHandle`, i.e. a Unity world;
   the family scenarios already provide one, so no separate fixture was added.
9. **`unity/GameCore.Validation/Packages/packages-lock.json` is untouched and needs no regeneration**: no package
   was added or removed, and the new test assembly lives inside the existing project.
10. **No `.meta` file was authored for `unity/GameCore.Validation` itself or for its `Assets`, `Packages`,
   `ProjectSettings`, `Catalogs` folders** — those have never carried metas in this repository, and adding them now
   would be an unrelated change to the project layout.
11. **No Unity scene or visual inspection was performed**, and the IL2CPP player was not built here. The EditMode
    scenario/assemblies are the exercised surfaces in the commands above; `tools/run_w3_gate.sh` remains the gate
    that builds and runs the player.
12. **Nothing in this change set has been compiled, imported or executed.** The most likely first failures, in
    order: (a) an expectation mismatch in a scenario step's literal (the values a step asserts are computed from the
    run wherever possible — `rows == providerRowsBefore`, `retractedRows == consumerRowsBeforeLoss` — but the fixed
    ones are `providerRows > 0`, `stagedCandidates == 1`, `rejected == 6`, `waiting == 1`, `resumed == 1`);
    (b) the card scenario's `ExpectedProviderRows == 2` (the two League A seats; the practice seat is isolated by
    P-016 and the scoreboard is ineligible by P-015, from GC-011's own suite);
    (c) whether `ConfigComposeResult`'s recomposition in the narrative replacement step produces exactly the hash
    the applier expects (it composes schema defaults → inherited → empty local patch, i.e. the same three layers the
    applier uses);
    (d) whether the narrative scenario's fixture-declared `FencedManifest`/`ConsumerManifest` need a config schema
    the manifest source can resolve (they use `NarrativeKeys.Schema()`-derived ids registered through the scenario's
    own `IPluginManifestSource` wrapper, so the applier's `DefaultLayer` falls back to `ConfigDocument.Empty`);
    (e) the narrative `PublishCarrier()` idiom for a no-target-change publication, copied from the slice's own
    scenario (`NarrativeScenario.cs`, the forward provider) — if that idiom changed, step 6's `carried` flag fails.
