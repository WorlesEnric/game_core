# GC-017 handoff — inject failure at every apply and cancellation boundary (Wave 5)

Branch `gc-017` (worktree `/Users/yangcao/wkspace/gc-wt/gc-017`), starting from `main` = `f28a4af` (Wave 4 gate).

**Linux build-host verification:** see `artifacts/gc-017/BUILD_REPORT.md` for executed checks, fixes and the
remaining release-latch allocation defect. The `NotRun` statement below describes the original authoring host only.

**Status of every executable check this change set adds: `NotRun (pending orchestrator build host)`.** This host has no
Unity, no .NET SDK, no Mono and no C# compiler, so nothing here has been compiled, imported or executed. What *did* run
on this host is in §8. None of it is a build, an import, a test or a player run.

## 1. Summary

Deterministic fault latches at every TEST-016 boundary in the apply and cancellation path, a real cross-family
scenario that arms each one and observes the protocol outcome, the package-level regression suites, the cancella-
tion/cutoff race, and recovery from initial definitions into a new world. The latches are compiled into the
qualification project and the player; a shipping build omits the explicit fault-qualification marker, though
some latch metadata and per-world allocation still remain (see BUILD_REPORT.md).

## 2. Files created

### Production (the latch and the recovery contract)

| Path | Contents |
| --- | --- |
| `Packages/com.gamecore.unity.runtime/Runtime/Faults/FaultBoundaries.cs` (+ `.meta`, folder `.meta`) | `FaultBoundary` (the eight boundaries), `FaultBoundaryText`, `FaultRecord`/`FaultTrace` (ordered provenance: boundary, operation, plan hash, injected flag), `FaultInjectedException`, `FaultCompilation` (`Symbol`, `IsCompiledIn`) and the internal `FaultReach.Reach`/`FaultReach.Refuse` helpers. **The whole body is inside one `#if GAMECORE_FAULT_INJECTION`**; only the namespace declaration stays outside, so a release compilation of this file contributes nothing but an empty namespace. |
| `Packages/com.gamecore.unity.runtime/Runtime/Faults/AssemblyFaultInjection.cs` (+ `.meta`) | The latch itself, moved out of `AssemblyPublisher.cs` in the release-surface round. Same public name and namespace, so no caller changed; the move makes the entire latch engine-free and therefore compilable by plain dotnet in both configurations, which is what `dotnet/src/GameCore.Faults.ReleaseCheck` uses. Guarded exactly like `FaultBoundaries.cs`. |
| `dotnet/src/GameCore.Faults.ReleaseCheck/GameCore.Faults.ReleaseCheck.csproj` | The qualification-only project that compiles `Runtime/Faults/**` twice (Release must be empty, Qualification must carry everything). Deliberately outside `dotnet/GameCore.sln` so the solution build stays the shipping shape. |
| `tools/check_release_fault_free.py`, `tools/check_player_fault_free.py` | The release-surface checks (see §10). |
| `Packages/com.gamecore.unity.runtime/Runtime/Recovery/InitialDefinitionRecovery.cs` (+ `.meta`, folder `.meta`) | `IRecoveryRepair`, `RecoveryRequest`, `RecoveryReport`, `InitialDefinitionRecovery.Recover`. |

### Unity fault fixtures and the player probe

| Path | Contents |
| --- | --- |
| `unity/.../Runtime/FaultScenarioStep.cs` + `.meta` | `FaultScenarioStep`, `FaultScenarioResult` (digest over `name=pass|fail` lines). |
| `unity/.../Runtime/FaultScenario.cs` + `.meta` | `FaultScenario`: `ObservationNames` (the 15 frozen names), `QualifiedNames`, `Run(IW4GateFamily)` and the executor that drives the whole sequence over one real family world. |
| `unity/.../Runtime/FaultScenarioHost.cs` + `.meta` | The four single-catalog entry points plus the two `out`-pair helpers, over both families. |
| `unity/.../Runtime/ProbeFaults.cs` + `.meta` | The `-probeFaults` player mode and both digest literals. |
| `unity/.../Tests/Faults/` (+ folder `.meta`), `GameCore.Faults.Tests.asmdef` + `.meta`, `FaultScenarioIntegrationTests.cs` + `.meta` | The EditMode half: one case per family, one that recomputes both digest literals from `FaultScenario.QualifiedNames`. |

### Package regression suites

| Path | Contents |
| --- | --- |
| `Packages/.../Tests/Faults/` (+ folder `.meta`), `GameCore.Unity.Faults.Tests.asmdef` + `.meta`, `FaultBoundaryTests.cs`, `GuardedDispatchFaultTests.cs`, `LifecycleFaultTests.cs` (+ metas) | The publisher/driver/gate latch boundaries, the guarded-dispatch fail-stop rows (including the stock-group contrast) and the lifecycle rows (in-flight fence, throwing disposer). |
| `Packages/.../Tests/Recovery/` (+ folder `.meta`), `GameCore.Unity.Recovery.Tests.asmdef` + `.meta`, `RecoveryFixture.cs`, `InitialDefinitionRecoveryTests.cs` (+ metas) | The initial-definition recovery suite and its fixture. |

### Tooling and evidence

| Path | Contents |
| --- | --- |
| `tools/run_gc017_gate.sh` | The gate sequence; every Unity invocation bounded by `timeout`, a timeout retried exactly once. Step 7 is now the release-surface check. |
| `tools/unity/run_gc017_faults_probe.sh` | The player-probe harness: `PROBE_RUNS` runs, strict JSON, all 62 required step fragments, both digest literals, 15 clause fragments. |
| `artifacts/faults/README.md`, `boundaries.json`, `trace-format.md` | The ten TEST-016 rows mapped to their cases and evidence paths (29 rows), the trace grammar, and the exact commands. |
| `artifacts/faults/release-surface.md` | The release-surface requirement, the mechanism, the three checks and the authoring-host self-test. |
| `artifacts/gc-017/HANDOFF.md` | This file. |

## 3. Files modified

| Path | Change | Why |
| --- | --- | --- |
| `Packages/.../Runtime/Assembly/AssemblyPublisher.cs` | `AssemblyFaultInjection` gained `Arm`/`Disarm`/`IsArmed`/`TryReach`/`TryRefuse`/`Trace`/`ReachCountOf` alongside the two original booleans; `Faults` now returns the world's latch; `Publish` reaches the validation, acquisition, fence, first-live-write, gate-installation and cleanup boundaries; `StampTargets` moved inside the postwrite guard; `PrewriteRefusal`/`ReachPrewriteFault` added; the header and inline step list renumbered. | The only place a validated plan becomes visible storage is where the apply boundaries live (P-002, P-029..P-031). |
| `Packages/.../Runtime/Assembly/AssemblyPublisher.cs` (release round) | `AssemblyFaultInjection` moved to `Runtime/Faults/`; the `Faults` property, the ctor assignment and every reach/legacy call site are inside `#if`; the seven identical legacy first-live-write sites collapsed into one `[Conditional("GAMECORE_FAULT_INJECTION")]`-masked helper called from unconditioned code, so those call sites vanish from a release compilation. | No latch code and no latch cost outside the qualification build. |
| `Packages/.../Runtime/Execution/UnityExecutionDriver.cs` | Reaches `FaultBoundary.StructuralPlayback` between the step's systems and its commit; guarded. | TEST-016 row 6 needs an injection point after the step's writes and before its publication. |
| `Packages/.../Runtime/Integration/StagedResourceGate.cs` | Optional latch (3-arg ctor); acquisition and cleanup boundaries refuse as values; `InjectionRefusalCount`/`InjectionReleaseRefusalCount` kept apart from `BudgetExceededCount`. The latch field, that constructor and both counters are inside `#if`. | TEST-016 rows 2 and 8; a budget reading must not absorb a fault refusal; a shipping gate has no latch field at all. |
| `Packages/.../Runtime/WorldHost.cs` | `UnityWorldHost.Faults` + `IWorldExecutionContext.Faults`, both inside `#if` — including the `new AssemblyFaultInjection()` — so a shipping world allocates no latch. | One latch per world, shared by its publisher, driver and gate. |
| `Packages/.../Runtime/GameCore.Unity.Runtime.asmdef` | `versionDefines` on the **marker package `com.gamecore.fault-qualification`** defines `GAMECORE_FAULT_INJECTION`. | The marker is referenced only by the validation project's manifest, so a shipping project cannot obtain the symbol — keying it on `com.unity.test-framework` did not work, because Unity packages resolve the Test Framework transitively. |
| `Packages/com.gamecore.planning/Runtime/Plans/PlanStateMachine.cs` | `HasCrossedLiveWriteBoundary` includes `PlanPhase.Faulted`. | `shared:` — see §5. |
| `unity/.../Runtime/ProbeArguments.cs`, `ProbeRunner.cs` | The `-probeFaults` flag, its property, `IsProbeInvocation`, `Parse`, the report identity (`GC-017`/`Faults`) and the dispatch arm. | One new probe mode; every existing arm untouched. |

## 4. Requirement / test coverage map

| Requirement / test | Where implemented | Where observed |
| --- | --- | --- |
| P-002 participants and authority | `UnityWorldHost.Faults` is per-world; `InitialDefinitionRecovery` is a static entry that never becomes a second authority | the fault scenario's step 1; every recovery case |
| P-004 stable identities, fresh WorldId | `RecoveryRequest.IsValid`, `RecoveryReport.DestinationIsFreshIncarnation` | `gc017-recovery-...`, `ARecoveryWithAStaleSourceHandleIsRefused` |
| P-005 runtime handles | `TryResolveHandle` on the recovered world's publisher | `gc017-recovery-...` (source handle never resolves in the destination) |
| P-027 plan states | `PlanStateMachine` driven by the publisher across the faulted boundary | `FaultBoundaryTests.AnInjectedFirstLiveWriteFault...` |
| P-028 validation, stale plans | validation boundary placed after the prepared check and before the recheck | `gc017-validation-...`, `AnInjectedValidationFaultRejectsBeforeAnyLiveWrite` |
| P-029 preparation, scratch migration, release | migration boundary on scratch; `PrewriteRefusal` releases in reverse order | `gc017-prewrite-migration-...`, `AnInjectedMigrationFaultAndTheOriginalPrewriteSwitchBothPreserveTheOldAssembly` |
| P-030 the fence and the one commit | fence and gate-installation boundaries | `gc017-fence-...`, `gc017-gate-installation-...` |
| P-031 apply failure faults the world | first-live-write, structural-playback and gate-installation boundaries | `gc017-postwrite-...`, `gc017-structural-playback-...` |
| P-035 lifecycle | recovery exposes a world only after its initial publication; a destination that never became Running is disposed | `AFailedReferenceRepairNeverExposesARunningWorld`, `RecoveryFromInitialDefinitions...` |
| P-041 structural work | the structural-playback boundary sits at the step boundary | `AStructuralPlaybackFaultStopsTheStepCommitAndKeepsTheQuarantine` |
| P-047 in-flight lifetime | job fence + quarantine; an old callback is discarded by world identity | `AJobHeldInFlightWhileUnloadBeginsIsFencedAndItsResourceQuarantined`, `gc017-old-callback-...` |
| P-048 teardown and aggregated cleanup | cleanup boundary; throwing disposer | `AThrowingDisposerIsRecordedAndOtherCleanupStillProceeds`, `gc017-cleanup-...` |
| P-049 recovery limits | `InitialDefinitionRecovery` in full | all eight recovery cases |
| P-050 identity and idempotency | operation identity in every `FaultRecord`; destination reservation rules | `ARecoveryIntoAnAlreadyOwnedDestinationSessionIsRefused`, `TheLatchRecordsEveryReachInOrderWithProvenance` |
| P-051 the serialized cutoff | cancellation before/after `Drain`; `TooLate` never claims rollback | `gc017-cancellation-before/after-...` |
| P-052 diagnostics | `FaultRecord.ToLine()` carries boundary, operation and plan hash | `artifacts/faults/trace-format.md`, the probe's `traceRecord=` clause |
| TEST-009 | prepared plans, atomic visibility, cancellation | the observer in step 1; both cancellation steps |
| TEST-016 | all ten rows — see the table below | the fault scenario + the package suites |
| TEST-018 | guarded zero-cost compilation switch; world dispatch fail-stop | `AStockGroupSwallowsTheSameExceptionAndTheGuardedGroupDoesNot`, `AThrowingGuardedSystemStopsTheNextRegisteredStageAndPublishesNothing` |

### 4.1 TEST-016 boundary → case (the required table)

| # | Boundary | Case |
| --- | --- | --- |
| 1 | validation | `narrative/gc017-validation-fault-rejects-and-keeps-the-old-assembly`, `cards/...`; `FaultBoundaryTests.AnInjectedValidationFaultRejectsBeforeAnyLiveWrite` |
| 2 | acquisition | `gc017-acquisition-fault-releases-staged-leases` (both families); `FaultBoundaryTests.AnInjectedAcquisitionFaultRefusesTheLeaseAndReleasesWhatWasStaged` |
| 3 | cancellation vs the cutoff (not a latch) | `gc017-cancellation-before-the-cutoff-releases-staged-work`, `gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication` |
| 4 | fence / job held in flight while unload begins | `gc017-fence-fault-settles-handles-and-keeps-the-old-assembly`; `LifecycleFaultTests.AJobHeldInFlightWhileUnloadBeginsIsFencedAndItsResourceQuarantined` |
| 5 | migration / after the first authoritative mutation | `gc017-prewrite-migration-fault-preserves-live-state`, `gc017-postwrite-fault-faults-the-world-and-keeps-the-last-image`, `gc017-gate-installation-fault-stops-after-live-writes`; `FaultBoundaryTests.{AnInjectedMigrationFaultAndTheOriginalPrewriteSwitchBothPreserveTheOldAssembly, AnInjectedFirstLiveWriteFaultAndTheOriginalPostwriteSwitchBothFaultTheWorld, AnInjectedGateInstallationFaultFaultsAfterTheApplyStage}` |
| 6 | authoritative system throws after a partial update | `gc017-structural-playback-fault-stops-the-step-commit`; `GuardedDispatchFaultTests.AThrowingGuardedSystemStopsTheNextRegisteredStageAndPublishesNothing` (+ the stock-group contrast) |
| 7 | adapter output fails after a simulation commit | `GuardedDispatchFaultTests.AFailingOutputGroupDoesNotRewindTheCommittedStep` |
| 8 | one disposer throws | `gc017-cleanup-fault-retains-staged-ownership`, `gc017-cleanup-boundary-releases-what-a-refusal-staged`; `FaultBoundaryTests.AnInjectedCleanupFaultRetainsStagedOwnershipInsteadOfReportingRelease`; `LifecycleFaultTests.AThrowingDisposerIsRecordedAndOtherCleanupStillProceeds` |
| 9 | old callback arrives after restart | `gc017-old-callback-after-recovery-is-rejected` |
| 10 | restore fails during reference repair | `InitialDefinitionRecoveryTests.AFailedReferenceRepairNeverExposesARunningWorld` — the **initial-definition** form; the checkpoint form is GC-018/GC-027 and is not claimed here |

## 5. Contract changes (`shared:` commits)

Four commits touch shared surfaces; all are additive or a strict widening, and each is in its own commit.

1. **`Faults` on the world and the publisher.** New members only: `UnityWorldHost.Faults`,
   `IWorldExecutionContext.Faults`, `AssemblyPublisher.Faults` (was a per-publisher instance; now the world's, which
   is a behavioural change only for a caller that armed one publisher's latch and expected another publisher in the
   same world to be unaffected — no such caller exists). `StagedResourceGate` gained a 3-arg overload; the 2-arg
   constructor is unchanged.
2. **`AssemblyFaultInjection` additions.** `Arm`/`Disarm`/`DisarmAll`/`IsArmed`/`TryReach`/`TryRefuse`/`Trace`/
   `ReachCount`/`ReachCountOf`/`InjectedCount`/`ArmedBoundaryCount`/`IsCompiledIn`/`Describe`. The two existing
   booleans and their two counters are untouched, and the existing GC-008 tests that use them still describe the
   same behaviour.
3. **`StagedResourceGate` counters.** `InjectionRefusalCount`/`InjectionReleaseRefusalCount` are new;
   `BudgetExceededCount` no longer increments on an injected refusal. This is the one shared change that *narrows*
   an existing counter's meaning, and it is the correct reading of P-022 (a budget is a sizing limit, not a fault).
4. **`PlanStateMachine.HasCrossedLiveWriteBoundary` now includes `Faulted`.** P-031 forbids re-applying a partially
   applied plan; `TryFault` is legal only from `Applying`, so a faulted plan has by definition crossed the boundary.
   Reporting `false` let a caller conclude that no live write happened. The three existing assertions
   (`PlanStateMachineTests` Draft `false`, `AssemblyPlannerTests`, `AssemblyPublisherTests`) are unaffected: none of
   them asserts the predicate on a faulted plan, and the Draft case is still `false`.

**Release-surface round.** One further shared change, and it is a narrowing rather than an addition:
`AssemblyFaultInjection` moved from `AssemblyPublisher.cs` to `Runtime/Faults/AssemblyFaultInjection.cs` — same
public name, same `GameCore.Unity.Runtime` namespace, same members, so no caller changed. What *is* different is that
in a compilation without `GAMECORE_FAULT_INJECTION` these members no longer exist at all rather than existing
inertly: `AssemblyPublisher.Faults`, `UnityWorldHost.Faults`, `IWorldExecutionContext.Faults`,
`StagedResourceGate`'s 3-argument constructor, `StagedResourceGate.InjectionRefusalCount` /
`InjectionReleaseRefusalCount`, `FaultCompilation.IsCompiledIn`, `FaultReach`, and every latch type. That is the
point of the round: a shipping consumer cannot name them. No shipping caller ever existed — this whole surface was
introduced by GC-017 in this wave.

No public contract was renamed or removed. `GameCore.Contracts` and the plan DTOs are unchanged.

## 6. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed.** The highest-risk items, in the order a compiler would find them:
   (a) the fault scenario is 2100 lines of hand-written Unity-free code across five large modules and two family
   hosts — every external call site was audited against its declaration (§8) and eleven defects were fixed, but only
   a compiler settles it; (b) `FaultReach` is `internal`, so it must only be reached from inside `GameCore.Unity.Runtime`
   (it is); (c) the probe's 15 clause fragments were co-designed with `ProbeFaults` and cross-checked against the
   scenario's detail strings, but a detail that takes an early-return path would not contain them.
2. **The scenario drives the publisher directly for the prewrite observations.** A prewrite refusal happens after
   `TryAdoptLanePublication` recorded the adoption and before any commit clears it, so the pair is left
   adopted-and-pending and a second `TryAdoptLanePublication` for it is refused `StalePlan` (P-006). The runner
   therefore builds the plan for that one pair with the real `Derive` + `AssemblyPlanner.Build` +
   `AssemblyPublisher.Publish` — the same shape `W4GateScenario.PublishPolicyCase` uses — and carries the module's
   own proposal from the pair's first derivation (`WorldChain.PendingProposal`) rather than re-deriving, because a
   later `Derive` against an unchanged composition legitimately returns `NoTargetChange` with no proposal.
   `pipeline.PublishDerived` is still used where it is the honest path. Nothing is re-implemented and no second
   interpretation of the composition is introduced; recorded because it differs from the task's most obvious reading.
3. **The recoverable window is "owned and not Running".** `UnityWorldHost.Stop` disposes the storage *and* removes the
   world from `UnityWorldRegistry`, so a `Disposed` source session is genuinely unregistered and recovery refuses it
   `StaleHandle` — there is no live fault record left to recover from. `RecoveryFromInitialDefinitions...` and
   `AStoppedWorldIsRefusedWhileAFaultedOwnedWorldRecovers` assert exactly that. A caller that wants recovery after a
   stop must recover from the checkpoint, which is GC-018's.
4. **Recovery restores initial state only.** No gameplay state, clock or RNG stream crosses a recovery; carrying
   committed state is checkpoint restore, deliberately not implemented here (GC-017's definition of done says the
   supported recovery source is explicitly initial definitions until checkpoint work integrates).
5. **Release compilation required an explicit qualification switch.** The original `versionDefines` entry on
   `com.unity.test-framework` did not compile the latches out of a shipping project: Unity Entities and other
   dependencies pull that package transitively, so a test-framework-free manifest still set the symbol. The
   validation project now explicitly depends on `com.gamecore.fault-qualification`, and the runtime asmdef defines
   `GAMECORE_FAULT_INJECTION` only when that package is installed. The separate release check removes the marker,
   builds an IL2CPP player, and inspects the actual C# 9 compiler defines and generated C++ reach functions.
6. **`artifacts/gates/w4-generic-profile/inventory.json` is not updated.** GC-017 evidences fault boundaries, not a
   generic-profile row; the orchestrator promotes rows it has run. Proposals, if wanted: `P-049` and `P-052` move
   from Partial toward Implemented on the strength of `artifacts/faults/` **once the gate has run** — not before.
7. **`dotnet/README.md` was not updated** (no new dotnet project was created; the fault and recovery suites are
   Unity-only). Recorded as a deliberate omission rather than a silent one.
8. **Row 10 is the initial-definition form only.** TEST-016's last row also covers "checkpoint restore fails during
   reference repair"; `IRecoveryRepair` is the seam for it and GC-018/GC-027 own the checkpoint source.
9. **No native crash containment, no arbitrary history replay, no memory undo journal** — GC-017's stated non-goals.

## 7. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 7.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_gc017_gate.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` (trx into `artifacts/faults/trx`); the Unity
resolve; EditMode; PlayMode; the IL2CPP player through `tools/unity/build_probe.sh`; the `-probeFaults` probe; the
release-surface check; the documentation validator.

The release-surface step is the one this round added, and it is the only place GC-017's release claim is settled:

```sh
python3 tools/check_release_fault_free.py --dotnet "$DOTNET" --json artifacts/faults/release-surface.json
```

To also settle it against a **built** release player — one whose project manifest omits
`com.gamecore.fault-qualification` — build that player (the build report's recipe: copy the validation project, drop
the marker and the direct Test Framework dependencies, build StandaloneLinux64 IL2CPP Release/High) and then:

```sh
python3 tools/check_player_fault_free.py --player <release-player-dir> --il2cpp <generated-cpp-dir> \
  --json artifacts/faults/release-player-surface.json
```

or pass `RELEASE_PLAYER` / `RELEASE_IL2CPP` to `tools/run_gc017_gate.sh` so step 7 runs it in place. Without them the
gate prints `release-player: NOT RUN (RELEASE_PLAYER unset)` rather than implying it inspected a player.

### 7.2 The GC-017 suites alone

```sh
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Faults.Tests \
  -testResults artifacts/faults/unity/faults-editmode.xml -logFile artifacts/faults/unity/faults-editmode.log

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Faults.Tests \
  -testResults artifacts/faults/unity/package-faults-editmode.xml -logFile artifacts/faults/unity/package-faults-editmode.log

"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Recovery.Tests \
  -testResults artifacts/faults/unity/recovery-editmode.xml -logFile artifacts/faults/unity/recovery-editmode.log
```

Do not add `-quit` to a `-runTests` command (04 §10).

### 7.3 The probe alone

```sh
PROBE_RUNS=5 ARTIFACTS=artifacts/faults/toolchain tools/unity/run_gc017_faults_probe.sh
```

### 7.4 The pure half

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release
```

`FaultBoundaries.cs` and `InitialDefinitionRecovery.cs` reference `UnityEngine`/`Unity.Entities` through
`UnityWorldHost`, so they are Unity-only and are not part of the dotnet solution; `FaultInjectedException`,
`FaultCompilation` and the `FaultRecord`/`FaultTrace` shapes are engine-free but live in that file, so the dotnet
half does not compile them. `PlanStateMachine.cs` **is** in the dotnet solution and its change is covered by
`GameCore.Planning.Tests` (`PlanStateMachineTests`).

## 8. What actually ran on this host

```sh
python3 tools/check_game_core_csharp.py                 # checked 371 C# file(s); ok
python3 tools/validate_game_core_docs.py --self-test    # 9 isolated fixtures passed
python3 tools/validate_game_core_docs.py                # 14 documents; links, anchors, IDs, traceability, DAG, waves
bash -n tools/run_gc017_gate.sh tools/unity/run_gc017_faults_probe.sh   # clean
python3 -m json.tool artifacts/faults/boundaries.json   # clean, 29 rows, rows 1..10
# .meta GUID uniqueness over the whole worktree: 588 metas, 588 unique, 0 duplicates
# digest literals recomputed independently from the frozen observation table:
#   narrative 701a3c286098501456390975bbdc7e4bdb7218d3094f23e39e61b3744fa52b61
#   cards     5cd97d38a1023fe0c8b5239d611061e5696454be9ec7cc68d440506810201732
# independent read-only audits: 585 external call sites in FaultScenario, 961 across the five test files;
#   11 defects found and fixed (4 compile-blocking), plus 5 assertions made discriminating
```

Release-surface round, on this host:

```sh
python3 tools/check_release_fault_free.py --no-build
# PASS. releaseLatchReferences=0 in all four boundary owners; qualificationLatchReferences 17/3/1/1;
# 2 runtime latch sources evaluate to an empty namespace in release; the apply-path mask is intact;
# no manifest other than the validation project's references the marker package.
python3 tools/check_player_fault_free.py --player <synthetic player dir>     # self-test, see below
# falsifiability self-test: an unguarded `FaultReach.Refuse(Faults, FaultBoundary.Fence, ...)` injected into
#   AssemblyPublisher.Publish made the check FAIL ("release …/AssemblyPublisher.cs still references
#   FaultBoundary, FaultReach"); restoring the file returned it to PASS. The player tool fails on an assembly
#   containing `AssemblyFaultInjection` and passes on one containing nothing.
python3 tools/check_game_core_csharp.py                 # checked 372 C# file(s); ok
bash -n tools/run_gc017_gate.sh                         # clean
# .meta GUID uniqueness over the whole worktree: 591 metas, 591 unique, 0 duplicates
```

None of that is a build, an import, a test or a player run. The compiled-assembly half of the release check needs
the .NET SDK and the release-player half needs a release-configuration player; both are for the build host.

## 9. Fixes applied after the read-only audits

The audited findings and their fixes (each is in the tree and in the commit message):

1. `FaultScenario.cs` — a unary `+` applied to a string literal in the fence step's detail argument (CS0023).
2. `FaultScenario.cs` — `disarmed` read in the acquisition step but declared in the sibling validation step (CS0103);
   the step now computes and reports both `armedAtReach` and `disarmed`.
3. `FaultScenario.cs` — `WorldId` passed as `AsyncWorkToken`'s first argument, which is an `OperationId` (CS1503);
   the token now carries a real operation for the source world.
4. `FaultScenario.cs` — an `out`-variable declared inside a short-circuiting `&&` chain read afterwards (CS0165); the
   observer check is now invoked unconditionally.
5. `FaultScenario.cs` — `BuildThePendingPlan` re-derived an unchanged composition and got `NoTargetChange` with a
   null proposal, which would have failed four observations. The module's own proposal is now carried from the pair's
   first derivation.
6. `FaultScenario.cs` — a nullable dereference in the acquisition step (CS8602) and two conjuncts that could not fail
   (`DrainedHandles >= 0`, `staging.Plan != null`) replaced with checks that can; the "three controls" comment
   corrected to the two the method implements.
7. `FaultBoundaryTests.cs` — asserted `BudgetExceededCount` for an injected refusal; now
   `InjectionRefusalCount`, with the ceiling counter asserted `0`.
8. `FaultBoundaryTests.cs` — two `ReachCount` expectations were one low because a staged gate's value refusal counts
   a reach; corrected to 12 and 13.
9. `FaultBoundaryTests.cs` — asserted `plan.State.HasCrossedLiveWriteBoundary` while the plan was `Faulted`, which is
   the shared-contract fix in §5.4.
10. `FaultBoundaryTests.cs` — a self-referential `PlanHash.IsEmpty` check replaced by `hashA != hashB`.
11. `GuardedDispatchFaultTests.cs` — asserted a `FaultDetail` substring the driver can never record on that path
    (`OnDispatchFaulted` early-returns once latched); now asserts the first latch's real detail.
12. `RecoveryFixture.cs` — a vacuous `PostWriteInjections == 0` now also asserts both legacy switches are false and
    the migration counter is zero, making the enumerated-path claim falsifiable.
13. `LifecycleFaultTests.cs` — the recording binding's step counters were incremented but never asserted; both cases
    now assert them (2/2/2/2 across two passes, and 1/0/1/1 for the publication-boundary pass).
14. `FaultScenario.cs` — the pending-proposal cache was written only where the validation step and
    `BuildThePendingPlan` could reach it, so the fence and both cleanup steps fell back to a proposal whose base pair
    had moved on and the planner rejected it `StalePlan`. `NotePendingProposal` is now called at the migration step
    too (the derivation that leaves *that* pair pending), and `PendingProposalFor` returns a cached proposal only
    while its `(ExpectedRevision, BaseEpoch)` pair still equals the pair `AssemblyPlanner.Build` is about to be given.
15. `FaultScenario.cs` — the fence step asserted an *absolute* `ReachCountOf(Fence) == 1`, but the fence boundary sits
    on the common path of every publication that gets past the acquisition boundary, so the count was already 3 before
    this step's own reach. It now captures the count before arming and asserts the delta is 1, which is what the step
    actually proves and which survives a step reorder. The same absolute form was checked at every other reach site
    and is sound there: the gate-installation and structural-playback chains have exactly one publication (or one step)
    after arming, the migration step asserts `>= 1`, and the validation, acquisition and cleanup sites only log theirs.
16. `FaultScenario.cs` — the fence step asserted `MatchesPublishedAssembly`, which is true only after an assembly
    committed; a prewrite refusal deliberately leaves the lane one publication ahead of the world, so that term could
    never hold. Replaced with `PendingRefusalHeld`, which asserts the state a refusal really leaves (the pair is still
    adopted, the world published nothing, and the lane is exactly one publication ahead on both counters).

## 10. Release-surface round: the acceptance defect the first Linux build found

**The finding** (`artifacts/gc-017/BUILD_REPORT.md`, "Shipping build limit: latch exclusion is incomplete"): a
define-less IL2CPP player's generated C++ returned `false` from the reach helpers, but still contained the empty
reach functions, the boundary metadata, `AssemblyFaultInjection` and a per-world latch allocation. The reach path was
verified; the requirement — **no latch code and no latch cost in release** — was not.

**What changed.** Everything latch-shaped is now inside `#if GAMECORE_FAULT_INJECTION`, and the symbol is keyed on the
empty marker package `com.gamecore.fault-qualification`, which only the validation project's manifest references
(keying it on `com.unity.test-framework` could not work: Unity packages resolve the Test Framework transitively, so
the symbol survived in a project that had removed its direct dependency — the build report says so explicitly).

| Requirement | Mechanism | Where |
| --- | --- | --- |
| no per-world latch object/array/dictionary | the `Faults` member, its `new AssemblyFaultInjection()` and the latch's two `bool[]`/`int[]` are inside `#if`; so is `StagedResourceGate`'s latch field | `WorldHost.cs`, `AssemblyFaultInjection.cs`, `StagedResourceGate.cs` |
| no boundary metadata in a release assembly | the name table, the enum and every trace type are inside `#if`; the two files evaluate to a bare namespace declaration | `FaultBoundaries.cs`, `AssemblyFaultInjection.cs` |
| reach calls compile to nothing | every typed reach site is inside `#if`; the one apply-path call that sits in unconditioned code is masked by `[Conditional("GAMECORE_FAULT_INJECTION")]`, so the call site — argument evaluation included — is removed and the method is left as a discarded empty stub | `AssemblyPublisher.cs`, `UnityExecutionDriver.cs`, `StagedResourceGate.cs` |

The namespace declaration deliberately stays outside the guard: it emits no metadata, and keeping it is what makes the
assembly's `using GameCore.Unity.Runtime.Faults;` directives valid in the release configuration instead of a
missing-namespace error.

**Why `#if` and not `[Conditional]` everywhere.** `[Conditional]` can only remove a call whose *arguments* still
compile, and every reach site passes `FaultBoundary.X`; once the enum is gone, those call sites cannot compile at all,
so they must be guarded. `[Conditional]` is used exactly where it buys something: the apply loop's single per-write
call, whose arguments are plain locals. `FaultCompilation.IsCompiledIn` is now the constant `true` rather than an
`#if`-driven stub, because the type exists only when the latch does — a shipping build has nothing to ask.

**The check.** `tools/check_release_fault_free.py` (three halves, each independently failable) plus
`tools/check_player_fault_free.py` for a built release player; both are wired into `tools/run_gc017_gate.sh` step 7,
and the player half is announced as `NOT RUN` rather than silently skipped when `RELEASE_PLAYER` is unset.
`artifacts/faults/release-surface.md` is the full account, including the falsifiability self-test.

**Behaviour where the symbol *is* defined is unchanged**, which is what keeps the 29 cases passing: the reach
sequence, the trace contents and the injected counters are byte-for-byte what they were, and the one refactor on the
apply path (seven identical legacy first-live-write sites collapsed into one helper) preserves the firing condition
at every site.

**Residual limits.** (1) The compiled-assembly and release-player halves are `NotRun` here; the gate runs them. (2)
The check's source half strips comments, so a doc comment may name a latch type while code may not — deliberate, and
stated in the tool. (3) A release player must be built from a project that does not reference the marker package; the
gate cannot build one itself without a second Unity project, so it inspects one when given and says so when not.
