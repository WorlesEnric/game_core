# GC-017 — fault injection at every apply and cancellation boundary

`artifacts/faults/` holds GC-017's evidence surface: the machine-readable matrix (`boundaries.json`), the trace line
format (`trace-format.md`), and the directory tree the gate writes its results into (`unity/`, `toolchain/`, `trx/`).

* Task: GC-017 — "Inject failure at every apply and cancellation boundary"
  (`docs/game-core/09-implementation-guide.md#gc-017`), Wave 5.
* Matrix: **TEST-016** — Fault injection and recovery boundaries
  (`docs/game-core/08-validation-and-performance.md#test-016`), ten rows.
* Normative protocols: P-002, P-027, P-028, P-029, P-030, P-031, P-035, P-047, P-048, P-049, P-050, P-051, P-052.
* Non-goals (from the task): no native crash containment, no arbitrary history replay, no memory undo journal.

## Status

**Pass (Linux qualification build), with limits noted below.**

The full dotnet solution passed 750/750 tests; Unity EditMode passed 807/807 and PlayMode passed 6/6.
The qualification IL2CPP player passed all 62 GC-017 observations on five independent launches.
All 29 cases indexed in `boundaries.json` were matched to a passing XML case or player observation.
The separate IL2CPP release build omitted the explicit fault-qualification marker; its compiler response
omitted `GAMECORE_FAULT_INJECTION`, and generated C++ returned false without reaching a latch.
This verifies the reach path, not complete removal of fault types and their per-world allocation.

## What GC-017 proves

Deterministic, named latches — not probabilities — at every TEST-016 injection point in the apply and cancellation
path: `validation`, `acquisition`, `fence`, `migration`, `first-live-write`, `structural-playback`,
`gate-installation`, `cleanup`. One latch per world (`UnityWorldHost.Faults`) is shared by the assembly publisher,
the execution driver and the staged-resource gate, so a boundary armed by a test is the boundary the real code path
reaches. Two claims follow and each is asserted twice — once in the Editor and once in a stripped player:

* **Prewrite refusal** (validation, acquisition, fence, migration): the operation is refused, the staged leases and
  scratch are reclaimed, and the prior published epoch, its live state and its registrations stay active.
* **Postwrite fail-stop** (first live write, structural playback, gate installation): the world enters `Faulted`,
  nothing publishes an epoch or an image, the last committed image stays the only safe observation and the world is
  never resumed on partially changed storage. No ECB rollback is claimed anywhere.
* **Cancellation** is raced against the serialized cutoff: `Cancelled` before it, `TooLate` after it, and neither
  ever claims a rollback.
* **Cleanup** with a throwing disposer records the failed release, keeps ownership that cannot be proven safe
  retained and quarantined, and lets independent cleanup proceed (P-048).
* **Recovery** from initial definitions creates a new world (fresh `WorldId`, epoch `First`, step `Zero`); an old
  callback arriving after it is rejected and its own resources are released. GC-017 evidences the initial-definition
  form only — the checkpoint form is GC-018/GC-027.

## Commands

### The whole gate

```bash
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/run_gc017_gate.sh
```

Required: `UNITY` (exit 2 without it, and exit 2 when it is not executable). Optional: `DOTNET` (default `dotnet`),
`PYTHON` (default `python3`), `UNITY_PROJECT` (default `unity/GameCore.Validation`), `ARTIFACTS` (default
`artifacts/faults`), `PROBE_RUNS` (default `5`), `UNITY_TIMEOUT` (default `1800`).

The gate runs, in order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` with TRX into
`${ARTIFACTS}/trx`; the Unity project resolve; the EditMode suite; the PlayMode suite; the StandaloneLinux64 IL2CPP
player build through `tools/unity/build_probe.sh`; `tools/unity/run_gc017_faults_probe.sh`; then
`python3 tools/validate_game_core_docs.py --self-test` and `python3 tools/validate_game_core_docs.py`. Every Unity
invocation is wrapped in `timeout --signal=TERM --kill-after=60`; a timeout is retried exactly once and a second
timeout is fatal. `Pass` in that output means the player process reported it — the archived JSON is the evidence.

Any single artifact can be inspected on its own, e.g.

```bash
python3 -m json.tool artifacts/faults/toolchain/probe-gc017-faults.json
```

### The player probe alone

```bash
tools/unity/run_gc017_faults_probe.sh
```

Optional environment: `PROBE_PLAYER` (default
`unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64`; the script exits 2 when it is missing or not
executable), `PROBE_RUNS` (default `5`), `UNITY_PROJECT`, `ARTIFACTS` (default `artifacts/faults/toolchain`).
It sources `tools/unity/probe_runs.sh`, so each of the `PROBE_RUNS` runs must exit 0, write valid JSON and report no
failing step; run 1 writes the canonical result and log, runs 2..N write `.run<N>` siblings, and the script fails if
any single run is not clean. The player is launched as:

```bash
"${PROBE_PLAYER}" -batchmode -nographics -logFile "${log_file}" -probeFaults -probeResult "${result_file}"
```

`-quit` is deliberately never passed to the player: the probe exits itself through `Application.Quit` with a code
that encodes its result (0 = pass).

### The Editor half alone (no `-quit` on a test run)

```bash
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"${UNITY}" -batchmode -nographics \
  -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode \
  -testResults artifacts/faults/unity/editmode-results.xml \
  -logFile artifacts/faults/unity/editmode.log
```

`GameCore.Faults.Tests` is in that suite. It recomputes both digest literals from
`FaultScenario.QualifiedNames(label)` and compares them with `FaultScenario.ObservationNames`, so a renamed,
reordered or dropped observation fails the suite instead of shrinking it.

## Artifact inventory

| Artifact | Contents | Status |
| --- | --- | --- |
| `toolchain/probe-gc017-faults.json` | 62 passing observations, both families and catalogs plus digests | Pass (62/62) |
| `toolchain/probe-gc017-faults.json.run<N>` | runs 2–5 of the same 62-case probe | Pass (4/4 repeat runs) |
| `toolchain/player-gc017-faults.log` (+ `.run<N>`) | the player's own log per run | Pass (5 player exits) |
| `unity/editmode-results.xml` | all testable EditMode assemblies including faults and recovery | Pass (807/807) |
| `unity/playmode-results.xml` | all testable PlayMode assemblies | Pass (6/6) |
| `unity/resolve.log`, `unity/editmode.log`, `unity/playmode.log` | Unity project resolution and test invocations | Pass |
| `toolchain/codegen.log`, `toolchain/build.log`, `toolchain/environment.txt` | generated catalog, IL2CPP build and toolchain fingerprint | Pass |
| `trx/` | whole plain-dotnet solution, 11 projects | Pass (750/750) |
| `validator-self-test.log`, `validator.log` | documentation validator | Pass |
| `boundaries.json` | machine-readable TEST-016 case index | Pass (29/29 indexed cases) |
| `trace-format.md` | trace grammar, descriptive rather than runnable | Reference |

`boundaries.json` is the file to read for the row-by-row claim; it lists, per row, the `FaultBoundary`, the injection
point and required observation quoted from TEST-016, the case that evidences it, its artifact and its status.

## Digest literals

The 15-observation sequence is frozen by `FaultScenario.ObservationNames`; `FaultScenarioResult.Digest` is the
SHA-256 over the LF-joined `name=pass` lines (no trailing newline). All 15 passing yields:

| Family | Digest |
| --- | --- |
| `narrative` | `701a3c286098501456390975bbdc7e4bdb7218d3094f23e39e61b3744fa52b61` |
| `cards` | `5cd97d38a1023fe0c8b5239d611061e5696454be9ec7cc68d440506810201732` |

In the probe result, the label of the check reports both catalogs, e.g.
`generatedDigest=<literal>; fixtureDigest=<literal>; expectedDigest=<literal>; observations=15`. Both must be the
literal above *and* `AllPassed` must hold for each run.

## What the probe script asserts

`tools/unity/run_gc017_faults_probe.sh` first checks the JSON is valid and that `"task": "GC-017"`,
`"mode": "Faults"` and `"result": "Pass"` are present with no `"status": "Fail"`. It then requires all 15
observation names twice per family — `"<family>/<name>"` and `"fixture:<family>/<name>"` — for `narrative` and
`cards`, plus `gc017-narrative-digest` and `gc017-cards-digest`, and both digest literals. Finally it requires these
substrings, which only a run that really reached the boundary can contain:

```
structuralWrites=0            crossedLiveWriteBoundary=False   crossedLiveWriteBoundary=True
worldState=Faulted            stagedLeasesReleased=true        cancelOutcome=Cancelled
cancelOutcome=TooLate         armed=True                       boundaryReaches=
injected=                     traceRecord=boundary=validation  fired=1
retainedStaged=1              recovered=True                   disposition=DiscardForeignWorld
```

They are defined by the scenario's own step detail contract (see `trace-format.md` §4); in particular
`traceRecord=` carries one verbatim `FaultRecord.ToLine()`, so TEST-016 row 2's "failure includes operation ID and
provenance" is archived as text, not asserted as a bare pass flag.

## Boundary coverage (mirrors `boundaries.json`)

| Row | `FaultBoundary` | Case | Status |
| --- | --- | --- | --- |
| 1 | `validation` | `narrative/gc017-validation-fault-rejects-and-keeps-the-old-assembly`; `…Faults.FaultBoundaryTests.AnInjectedValidationFaultRejectsBeforeAnyLiveWrite` | Pass |
| 2 | `acquisition` | `narrative/gc017-acquisition-fault-releases-staged-leases`; `…FaultBoundaryTests.AnInjectedAcquisitionFaultRefusesTheLeaseAndReleasesWhatWasStaged` | Pass |
| 3 | — (cutoff race) | `narrative/gc017-cancellation-before-the-cutoff-releases-staged-work`; `narrative/gc017-cancellation-after-the-cutoff-is-too-late-and-keeps-the-publication` | Pass |
| 4 | `fence` | `…LifecycleFaultTests.AJobHeldInFlightWhileUnloadBeginsIsFencedAndItsResourceQuarantined`; `narrative/gc017-fence-fault-settles-handles-and-keeps-the-old-assembly`; `…FaultBoundaryTests.AnInjectedFenceFaultSettlesTrackedHandlesAndKeepsTheOldAssembly`; `narrative/gc017-teardown-settles-and-disposes` | Pass |
| 5 | `migration`, `first-live-write`, `gate-installation` | the three `narrative/gc017-…` observations plus `…FaultBoundaryTests.AnInjectedMigrationFaultAndTheOriginalPrewriteSwitchBothPreserveTheOldAssembly`, `…AnInjectedFirstLiveWriteFaultAndTheOriginalPostwriteSwitchBothFaultTheWorld`, `…AnInjectedGateInstallationFaultFaultsAfterTheApplyStage` | Pass |
| 6 | `structural-playback` | `narrative/gc017-structural-playback-fault-stops-the-step-commit`; `…GuardedDispatchFaultTests.AStructuralPlaybackFaultStopsTheStepCommitAndKeepsTheQuarantine`, `…AThrowingGuardedSystemStopsTheNextRegisteredStageAndPublishesNothing`, `…AStockGroupSwallowsTheSameExceptionAndTheGuardedGroupDoesNot` | Pass |
| 7 | — (adapter stage, GC-019) | `…GuardedDispatchFaultTests.AFailingOutputGroupDoesNotRewindTheCommittedStep` | Pass |
| 8 | `cleanup` | `narrative/gc017-cleanup-fault-retains-staged-ownership`; `narrative/gc017-cleanup-boundary-releases-what-a-refusal-staged`; `…FaultBoundaryTests.AnInjectedCleanupFaultRetainsStagedOwnershipInsteadOfReportingRelease`; `…LifecycleFaultTests.AThrowingDisposerIsRecordedAndOtherCleanupStillProceeds` | Pass |
| 9 | — (callback gate) | `narrative/gc017-old-callback-after-recovery-is-rejected` | Pass |
| 10 | — (recovery seam) | `narrative/gc017-recovery-from-initial-definitions-into-a-new-world`; `…Recovery.InitialDefinitionRecoveryTests.AFailedReferenceRepairNeverExposesARunningWorld` | Pass (initial definitions only) |

`…Faults.FaultBoundaryTests` and its siblings are `GameCore.Unity.Runtime.Tests.Faults.*`;
`…Recovery.InitialDefinitionRecoveryTests` is `GameCore.Unity.Runtime.Tests.Recovery.InitialDefinitionRecoveryTests`.
Row 10's note: **GC-017 evidences the initial-definition form of this row; the checkpoint form is GC-018/GC-027.**

The probe's `gc017-initial-world-lane-and-observer` observation is the frame every row runs inside, not an injection
row, so it carries no entry in `boundaries.json`.
