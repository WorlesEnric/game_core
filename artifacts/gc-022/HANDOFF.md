# GC-022 HANDOFF — complete unload stress, callback and Play Mode lifecycle proof (Wave 6)

Branch `gc-022` (worktree `/Users/yangcao/wkspace/gc-wt/gc-022`), based on `main` (`1cafced`, the Wave 5
integration gate).

**Status of every build, test and player result in this document: `NotRun (pending orchestrator build host)`.**
This host has no .NET SDK, no Unity, no Mono and no C# compiler, so nothing in this change set has been compiled,
imported, executed or built here. What did run is record-level only and is recorded verbatim in
`artifacts/gc-022/static-checks.log`: the host-side C# checker (474 files), the W0 contract-surface parity check
(unchanged five allowed additions — GC-022 added **no** `GameCore.Contracts` type, member or enum value), the
documentation validator (self-test + full), the leak attributor's 16-check self-test, `bash -n` over the three new
or changed shell scripts, a repository-wide `.meta` GUID uniqueness scan and `.cs`↔`.meta` coverage scan, an
independent recomputation of all four digest literals, and three mechanical member-resolution scans. **None of
those is a build or a test result.**

Commits on this branch (oldest first):

| Commit | Contents |
|---|---|
| `8a786e2` | the pure lifecycle stress rig and its acceptance suite (`Packages/com.gamecore.composition/Tests/Lifecycle/`) — the lead's slice |
| `47799e6` | the Play Mode reload matrix Editor script and its runner — slice W2 |
| `8d3048f` | the `-probeLifecycleStress` player mode, native leak attribution and the leak resource policy — slice W3 |
| `7036cc1` | the runtime lifecycle-stress scenario over one real family world — slice W1 |
| `8b9157e` | the lifecycle-stress EditMode suite over both families and both catalogs — slice W1 |

45 files, +8182/−3. Nothing pushed; no branch switched; nothing rebased.

## 1. Summary

GC-022's sentence is: *"Run 1,000 mount/unmount cycles, 100 delayed completions, stalled jobs, throwing disposers
and required-provider churn. Exercise domain reload on/off plus scene reload settings, stop/recreate and headless
cleanup. Trace every acquisition to retirement or quarantine."* Its acceptance adds: *"Lease/system/callback/view
counts return to baseline after bounded retention; no stale result writes authority. Stalled jobs retain reachable
buffers; independent cleanup continues after a disposer error. Loop nodes/subscriptions do not accumulate."*

The delivery splits that across four layers, each proving what it can prove **on the real modules**, with no seam
fixture and no hand-rolled model:

1. **The lifecycle mechanism, at 1,000 cycles** (`Packages/com.gamecore.composition/Tests/Lifecycle/`). The P-046
   state machine, the resource ledger, the tracked-job fence, the bounded quarantine registry, the callback gate
   and the P-048 teardown sequencer live in `GameCore.Composition`, which is engine-free by construction
   (`01-architecture.md` §1). One `LifecycleStressRig` drives the **production** types — a mount publication's
   identity/gate/acquisition steps exactly as `InstallationLifecycleCoordinator.ApplyTransition` performs them, and
   the unmount through `InstallationLifecycleCoordinator.Unload`, i.e. the same O-07 path a real removal takes.
   This is where TEST-015's literal quantity lives: *"Mount/unmount a dependency chain 1,000 times. Each plugin
   acquires a service lease, system registration, asset lease, callback and subscription."*
2. **A real Unity world, both families** (`unity/.../Runtime/LifecycleStress*.cs` +
   `Tests/LifecycleStress/GameCore.LifecycleStress.Tests`). One real narrative world and one real card world are
   built through the proven construction sequence (`CompositionHost` → `WorldCompositionBridge` →
   `DerivedAssemblyPipeline` → `WorldTimeDriver` → `LifecycleController`), cycled `GC_LIFECYCLE_STRESS_CYCLES`
   times (default 1,000) through the real mount/unmount payloads, and then driven through the four failure shapes.
   The blocked-teardown case deliberately asks the **coordinator** rather than `LifecycleController.Unload`, which
   completes blocking jobs first by design; the scenario comments say why.
3. **Play Mode reload matrix** (`Editor/LifecyclePlayModeMatrix.cs` + `tools/unity/run_lifecycle_playmode_matrix.sh`).
   The 2×2 of {domain reload, scene reload}, ten real enter/exit cycles each, one Editor process per combination so
   the unresolved pre-dispatch hang cannot lose the other three, JSONL appended per cycle so a watchdog kill still
   names the combination and cycle it died at, and the hang recorded as a **frequency**, never a pass.
4. **Headless + native leaks** (`Runtime/ProbeLifecycleStress.cs`, `tools/unity/run_lifecycle_stress_probe.sh`,
   `tools/attribute_native_leaks.py`, `artifacts/gc-022/leak/`). The same scenario runs in the IL2CPP player under
   `PROBE_RUNS` repetitions, reports the resolved cycle count and the player's own leak-detection mode, and every
   run log is attributed by a tool that fails on a GameCore-owned, unattributed or unbounded allocation.

The single gate is `tools/run_gc022.sh` (§6).

## 2. Files created

### 2.1 Pure composition half (lead — `8a786e2`)

| File | Contents |
|---|---|
| `Packages/com.gamecore.composition/Tests/Lifecycle/LifecycleStressRig.cs` | `LifecycleStressRole` (the five TEST-015 roles), `StressActivation`, `StressLease`, `LifecycleStressCycle`, `StressResourceFactory` (counted, scriptable-throwing `IManagedResourceFactory`), `LifecycleStressRig` (the real coordinator + ledgers + gate, with `MountAndUnload`, `Acquire`/`AcquireAll`, `RecordsOf`, `RetainedRecordsOf`, `TokenFor`). |
| `Packages/com.gamecore.composition/Tests/Lifecycle/LifecycleStressTests.cs` | 12 tests: the 1,000-cycle baseline proof, per-role tracing, 100 delayed completions, a foreign-incarnation completion, a stalled job, a throwing disposer, a non-retryable failed release, 10 provider-churn rounds, quarantine exhaustion, the job-history growth measurement, the ledger-history measurement, and the zero-acquisition case. |

### 2.2 Unity-world half (slice W1 — `7036cc1`, `8b9157e`)

| File | Contents |
|---|---|
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/LifecycleStressScenario.cs` | `LifecycleStressStep`, `LifecycleStressResult`, `LifecycleStressScenario` (the frozen 12 names, `CycleCount`, `Families()`, `QualifiedNames`, `RunGeneratedCatalog`, `RunFixtureCatalog`, `Run`), and the `Executor` that builds one real family world and runs the twelve observations in the frozen order, with a final ordering pass that turns an unrecorded observation into a **failing** step. |
| `.../Runtime/LifecycleStressFamily.cs` | `LifecycleStressDeclarations`, `ILifecycleStressFamily`, `LifecycleStressManifestSource`, `ScriptedResourceLease`, `LifecycleStressResourceFactory` — the family seam and the scriptable factory both families share. |
| `.../Runtime/LifecycleStressNarrativeHost.cs` | `Gc013NarrativeHost` partial: `NarrativeFamily : ILifecycleStressFamily`, the four stress manifests, the two digest literals, the three run entry points. |
| `.../Runtime/LifecycleStressCardsHost.cs` | The card twin. |
| `.../Tests/LifecycleStress/GameCore.LifecycleStress.Tests.asmdef` | Editor-only test assembly, `GameCore.LifecycleStress.Tests`. |
| `.../Tests/LifecycleStress/LifecycleStressIntegrationTests.cs` | EditMode suite: 12 observation tests as `[TestCase]` per family, cached `[OneTimeSetUp]` runs, a digest/table agreement test, an unknown-label refusal test; `[Timeout(3600000)]` on the counted-cycle fixture and `[Timeout(60000)]` on the assertions. |

### 2.3 Play Mode matrix (slice W2 — `47799e6`)

| File | Contents |
|---|---|
| `unity/GameCore.Validation/Assets/GameCore.Validation/Editor/LifecyclePlayModeMatrix.cs` | `-executeMethod`-drivable `[InitializeOnLoad]` matrix: four combinations, ten cycles each, per-session fresh `WorldId`, one registry world, one loop route, zero bootstrap fallbacks, disposed/storage-free/settled per exit, incremental JSONL then one JSON summary per invocation. |
| `tools/unity/run_lifecycle_playmode_matrix.sh` | One Editor process per combination under `timeout --signal=TERM --kill-after=60`, one retry on 124/137, `*.timeout` markers, a final table, non-zero exit when any combination is not `Pass`. |
| `artifacts/gc-022/playmode-matrix/README.md` | The exact command, the evidence layout, the exit codes and the hang-as-frequency rule. |

### 2.4 Player probe and leak attribution (slice W3 — `8d3048f`)

| File | Contents |
|---|---|
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeLifecycleStress.cs` | The `-probeLifecycleStress` mode: one step per scenario observation per family, both digest steps, a step naming the resolved cycle count and a step naming the process's own `NativeLeakDetection.Mode`. |
| `tools/unity/run_lifecycle_stress_probe.sh` | The player launcher: `PROBE_RUNS` runs under `probe_runs.sh`, mode/task/result greps, every required step fragment, both digest lines, then the attributor over every run log. |
| `tools/attribute_native_leaks.py` | The leak attributor: parses Unity's leak blocks and their callstacks, classifies `gamecore` / `unity-engine` / `third-party` / `unattributed`, emits per-class JSON with signature counts, exits non-zero on a GameCore-owned, unattributed or policy-unbounded allocation, and carries a 16-check `--self-test`. |
| `artifacts/gc-022/leak/README.md`, `leak/policy.md` | The run recipe and the native-allocation policy: every native allocation is driven to zero or listed with its frame signature and bound. |
| `artifacts/gc-022/leak/attribution-gc016-editor-baseline.json` | The reproduced GC-016 baseline: **1 block / 57 allocations / `unattributed`**, with the reason `stack-trace-disabled` — see §5.1. |
| `artifacts/gc-022/leak/fixtures/*` | The attributor's own falsifiability fixtures (GameCore stack, Unity-only, third-party, near-miss comment, truncated, address-only, count mismatch, empty, policy variants, baseline list). |

### 2.5 Gate and artifacts (lead)

| File | Contents |
|---|---|
| `tools/run_gc022.sh` | The gate: static checks → dotnet build/test (+ the focused stress filter) → Unity resolve/EditMode/PlayMode → catalog codegen → IL2CPP player → every player probe incl. `-probeLifecycleStress` → the Play Mode matrix → leak attribution → the docs validator. Exports `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2` for every Editor invocation. |
| `artifacts/gc-022/resource-policy.md` | The DoD's resource policy: every retained structure with its bound, the two unbounded growth findings, the permanent-quarantine semantics, and what a bounded cache must state. |
| `artifacts/gc-022/static-checks.log` | Every host-side check and cross-check, verbatim. |
| `artifacts/gc-022/HANDOFF.md` | This document. |

## 3. Files modified outside the new set

| File | Change | Why it is safe / why it is not a `shared:` commit |
|---|---|---|
| `unity/.../Runtime/ProbeArguments.cs` (+18) | One flag constant, one ctor parameter, one assignment, one property, one `IsProbeInvocation` term on its **own new line**, one local, one `else if` branch, one extra ctor argument inserted on a new line **before** the frozen contract line. | Additive; the two strings `prepare_gc017_release_project.replace_once` matches verbatim (`ProbeArguments.cs`:177 and :274) each still occur exactly once — verified in `static-checks.log` §4. |
| `unity/.../Runtime/ProbeRunner.cs` (+10) | `CreateReport` gets one independent `if` block; the dispatch gets one `else if` block. | Additive and additive-only; the release clone strips both through the release script. |
| `tools/unity/prepare_gc017_release_project.py` (+46/−3) | Teaches the release clone to remove the GC-022 mode, its probe file and its scenario files, exactly as it already does for Faults and the Wave 5 gate. | The GC-022 stress probe is a qualification fixture, not a shipping entry point. All 28 `replace_once` expectations were simulated against the real files and each matched exactly once; the simulated clone balances and carries no `LifecycleStress`/`GC-022` residue. |

**No file under `Packages/` other than the two new test files was touched, and no shared kernel file owned by
another Wave 6 task was touched at all.** No `shared:`-prefixed commit was needed.

## 4. Contract changes

**No `GameCore.Contracts` change.** GC-022 added no type, member, enum value or optional constructor parameter to
the frozen shared contracts: `tools/check_contract_surface_parity.py` still reports exactly the five pre-existing
additions (`EnvelopeError` 16/17, `FactoryKind.Handler`, `StateDispositionKind.RetainDormant`/`Reset`), and
`dotnet/tests/GameCore.Contracts.Tests`' API-compatibility allow-list therefore needs no edit. No plan DTO was
changed either.

Additions **outside** `GameCore.Contracts`, all in test/qualification or probe-host surface:

* `GameCore.Composition.Tests.LifecycleStress*` — the pure rig and suite (test assembly only).
* `GameCore.Validation.ProbeHost.LifecycleStress*` — the scenario, the family contract and the two family
  adapters (qualification project).
* `GameCore.Validation.ProbeHost.ProbeLifecycleStress` and the `-probeLifecycleStress` mode.
* `GameCore.Validation.Editor.LifecyclePlayModeMatrix` (Editor-only).
* `tools/attribute_native_leaks.py`, `tools/unity/run_lifecycle_stress_probe.sh`,
  `tools/unity/run_lifecycle_playmode_matrix.sh`, `tools/run_gc022.sh`.

## 5. The leak item: 57 persistent native allocations

**What was inherited.** GC-016's build report records `Leak Detected : Persistent allocates 57 individual
allocations` in the Editor shutdown log with no attribution. GC-022 must enable native leak detection with full
stack traces, attribute every one, and drive them to zero or document each as a bounded cache/quarantine.

**What was established on this host.** The 57-allocation block was re-parsed out of the archived GC-016 log with
the new attributor and reproduced as `artifacts/gc-022/leak/attribution-gc016-editor-baseline.json`:

```
1 block, 57 allocations, class = unattributed, reason = stack-trace-disabled, exit 2
```

That is a **positive** result, not a failed attribution: the archived log carries no callstacks because that run
did not have leak detection's full-stack-trace mode on, and the attributor refuses to report an unparsed block as
zero. The attributor's self-test pins exactly this case (`a stack-trace-disabled report reports 57 unattributed
allocations`).

**What GC-022 added to make attribution possible.**

* Editor: every Unity Editor invocation in `tools/run_gc022.sh` (and in the probe runner) exports
  `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2`. The Collections package ships
  `Unity.Collections.Editor/CLILeakDetectionSwitcher.cs`, an `[InitializeOnLoadMethod]` that reads that variable
  and assigns `Unity.Collections.NativeLeakDetection.Mode = NativeLeakDetectionMode.EnabledWithStackTrace`.
* Player: the probe sets the same mode through the public runtime API (`Unity.Collections.NativeLeakDetection`,
  `UnityEngine.CoreModule`), logs the resulting `Mode`, and reports it as a probe step. It cannot throw: a failure
  becomes a step detail.
* Attribution: `tools/attribute_native_leaks.py` classifies each block and each allocation as `gamecore`,
  `unity-engine`, `third-party` or `unattributed`, states the distinct frame signatures with counts, and exits
  non-zero when a GameCore-owned allocation exists, when a block stays unattributed, or when a non-GameCore
  signature is not named with a bound in `--policy`.

**Status: NotRun (pending orchestrator build host).** The attribution itself cannot be produced here. The exact
commands, in order:

```sh
# 1. Reproduce the inherited baseline (already committed; reruns in seconds).
python3 tools/attribute_native_leaks.py \
  --log artifacts/gc-016/unity/editmode.log \
  --out artifacts/gc-022/leak/attribution-gc016-editor-baseline.json

# 2. Re-run the Editor suite with full stack traces on.
UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2 timeout --signal=TERM --kill-after=60 1800 \
  "$UNITY" -batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" \
  -runTests -testPlatform EditMode \
  -testResults "$PWD/artifacts/gc-022/leak/unity/editmode-results.xml" \
  -logFile "$PWD/artifacts/gc-022/leak/unity/editmode.log"

# 3. Attribute the delta against the inherited baseline, with the policy.
python3 tools/attribute_native_leaks.py \
  --log artifacts/gc-022/leak/unity/editmode.log \
  --baseline artifacts/gc-022/leak/attribution-gc016-editor-baseline.json \
  --policy artifacts/gc-022/leak/policy.md \
  --out artifacts/gc-022/leak/editor-native-leak-attribution.json

# 4. The same two steps for PlayMode, and for the player log of every -probeLifecycleStress run.
```

`tools/run_gc022.sh` step 9 runs the strict form (no baseline, so a regression cannot be masked) and fails the
gate on any GameCore-owned, unattributed or unbounded allocation. **GC-022 does not claim any allocation was
attributed or driven to zero**; it claims the machinery that makes attribution possible, the reproduced baseline,
and a gate that cannot pass while 57 allocations remain unattributed.

## 6. Exact commands for the Linux build host

Everything from the repository root. Nothing below has been run.

### 6.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  GC_LIFECYCLE_STRESS_CYCLES=1000 PROBE_RUNS=5 tools/run_gc022.sh
```

In order: the static checks; `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` (trx into
`artifacts/gc-022/trx`) plus the focused stress filter (`artifacts/gc-022/trx-stress`); the Unity resolve; EditMode
(every testable package, `GameCore.LifecycleStress.Tests`, the pure stress suite's Unity side); PlayMode; the card
and checkpoint catalog code generation; `tools/unity/build_probe.sh` (probe catalog + StandaloneLinux64 IL2CPP
player with High stripping); the byte-identity check of all three committed catalogs; every player probe
`PROBE_RUNS` times including `-probeLifecycleStress`; the four Play Mode matrix combinations; the leak
attribution; the documentation validator. Every Unity Editor invocation is wrapped in `timeout` with one logged
retry on a timeout, and `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2` is exported for all of them.

### 6.2 This task's suites alone

```sh
# Pure half: the 1,000-cycle quantity, fast and deterministic.
dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release \
  --filter "FullyQualifiedName~GameCore.Composition.Tests.LifecycleStressTests"

# Unity half: both families, both catalogs, on real worlds.
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform EditMode \
  -testFilter GameCore.LifecycleStress.Tests \
  -testResults "$PWD/artifacts/gc-022/unity/lifecycle-stress-editmode.xml" \
  -logFile "$PWD/artifacts/gc-022/unity/lifecycle-stress-editmode.log"
```

Do not add `-quit` to a `-runTests` command (04 §10). `-testResults` with a relative path resolves against the
Unity **project** path, so pass an absolute path.

```sh
# The player proof, PROBE_RUNS times per family and catalog, with leak detection on.
PROBE_RUNS=5 GC_LIFECYCLE_STRESS_CYCLES=1000 UNITY_PROJECT=unity/GameCore.Validation \
  ARTIFACTS=artifacts/gc-022/toolchain tools/unity/run_lifecycle_stress_probe.sh

# The Play Mode reload matrix on its own (four Editor processes; a hang is recorded, not hidden).
UNITY="$UNITY" MATRIX_COMBINATIONS="reload-on-scene-on" MATRIX_CYCLES=2 \
  tools/unity/run_lifecycle_playmode_matrix.sh   # quick smoke first, ~1 Editor process
UNITY="$UNITY" tools/unity/run_lifecycle_playmode_matrix.sh
```

### 6.3 Cost of the counted cycles, and how to dial it

`GC_LIFECYCLE_STRESS_CYCLES` (default 1000) is read once by the scenario and decides how many real mount/unmount
publications the Unity-world and player runs perform. The pure suite always runs exactly 1,000 cycles, because
there a cycle is a ledger operation rather than an ECS assembly publication. If 1,000 real publications per family
per catalog prove too slow on the build host, run
`GC_LIFECYCLE_STRESS_CYCLES=100 tools/run_gc022.sh` and record the reduced count in the BUILD_REPORT — the
scenario reports the count it actually used as its own probe step (`lifecycle-stress-cycle-count`), so a reduced
run cannot be mistaken for the 1,000-cycle claim. **Do not report a gate that ran 100 cycles as the 1,000-cycle
gate.**

## 7. Requirement → implementation → test mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| P-003 three kinds of change; retracting does not undo committed gameplay | teardown retracts only the installation's own leases and reports them; nothing rewinds committed state | `AThousandMountUnmountCyclesReturnEveryLiveCounterToBaseline`, `EveryAcquisitionOfACycleIsTracedToRetirementOrQuarantineByKind`, `lifecycle-stress-acquisitions-traced-to-retirement` |
| P-007 references and leases; a stale completion cannot reacquire authority; bounded retention refuses rather than overwrites | `CallbackGate.Evaluate` order (world → fence → liveness → generation/epoch); `ManagedResourceGate` closes irreversibly at retirement | `AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement`, `ACompletionStampedByAnotherWorldIncarnationIsDiscarded`, `lifecycle-stress-delayed-completions-are-discarded` |
| P-012 dependency closure: provider loss waits, provider return resumes, in the same publication | `ActivationLedger.WaitForDependencies` / `ResumeFromWaiting` + a real P-048 pass for the retraction | `RequiredProviderChurnWaitsAndResumesTheConsumerOnEveryCycle`, `lifecycle-stress-required-provider-churn` |
| P-035 world lifecycle, pause/resume without an epoch change, disposal ≠ scene unload | `UnityWorldHost.Stop`/`Dispose`, `UnityWorldRegistry` | `AnUnloadOfACycleThatNeverRanRetainsNothing`, `lifecycle-stress-registry-returns-to-baseline`, the Play Mode matrix's per-session disposal assertions |
| P-046 installation lifecycle and every transition edge | `InstallationStateMachine` + `ActivationLedger` over 1,000 real cycles | `AThousandMountUnmountCyclesReturnEveryLiveCounterToBaseline` (`RefusedTransitionCount == 0`), `RequiredProviderChurnWaitsAndResumesTheConsumerOnEveryCycle`, `lifecycle-stress-cycles-complete` |
| P-047 in-flight lifetime: jobs finish before release, callbacks gated at dispatch and completion | `TeardownSequencer` step 1/3, `GatedCallbackPath`, `LifecycleJobFence` | `AStalledJobRetainsItsReachableBuffersUntilItCompletes`, `AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement`, `lifecycle-stress-stalled-job-retains-buffers` |
| P-048 teardown order, dispose at most once, attempt all independent cleanup, quarantine not timeout | `TeardownSequencer.Unload` steps 1–6, `ResourceLedger.RetireInstance`, `QuarantineRegistry` | `AThrowingDisposerIsQuarantinedWhileIndependentCleanupContinues`, `AFailedReleaseIsNeverRetriedAndItsQuarantineStaysBounded`, `QuarantineExhaustionRefusesAdmissionInsteadOfDroppingReferences`, `lifecycle-stress-throwing-disposer-keeps-cleanup-going` |
| P-049 recovery limits: no unsafe release, bounded retry, no hidden replay | no release path exists that a timeout can reach | `AFailedReleaseIsNeverRetriedAndItsQuarantineStaysBounded` (the retry is refused and reported), `resource-policy.md` §2.3 |
| P-050 cancellation and idempotency: repeat retrieves, never re-executes | repeated unload and repeated settle are idempotent | `AnUnloadOfACycleThatNeverRanRetainsNothing` (the repeated pass releases nothing a second time) |
| P-058 V1 profile: IL2CPP and stripping are build acceptance requirements | the same scenario runs in the stripped player | `-probeLifecycleStress` (`tools/unity/run_lifecycle_stress_probe.sh`) |
| P-060 evidence and release status | `artifacts/gc-022/{static-checks.log,resource-policy.md,HANDOFF.md}`, `leak/` | the whole artifact set; every executable line here is explicitly `NotRun` |
| TEST-001 toolchain / generated registration / IL2CPP | unchanged; GC-022 adds one more mode to the existing player | `tools/run_gc022.sh` steps 6–7 |
| TEST-015 lifecycle and managed resource teardown | the rig, the scenario and the four failure shapes | the 1,000-cycle test, per-role tracing, stalled job, throwing disposer, provider churn, `lifecycle-stress-*` observations |
| TEST-016 fault injection and recovery boundaries (rows: job in flight, one disposer throws, old callback after restart) | in-flight job fence, scripted disposer, retired-activation completions | `AStalledJobRetainsItsReachableBuffersUntilItCompletes`, `AThrowingDisposerIsQuarantinedWhileIndependentCleanupContinues`, `AHundredDelayedCompletionsCannotWriteAuthorityAfterRetirement`, the corresponding scenario observations |
| TEST-018 Unity worlds, bootstrap and Play Mode | the 2×2 reload matrix, ten cycles each, one route, fresh incarnation per session | `Editor/LifecyclePlayModeMatrix.cs`, `artifacts/gc-022/playmode-matrix/`, `lifecycle-stress-loop-nodes-do-not-accumulate` |
| TEST-023 performance, bounded memory, architecture regressions | the two growth measurements, the live-count baselines, the leak attributor | `CompletedJobRecordsAccumulateUntilExplicitlyReleased`, `RetiredLedgerRecordsAreRetainedAndTheRetainedBoundIsReported`, `attribute_native_leaks.py --self-test`, `resource-policy.md` §2.2 |
| O-03 Mount / O-04 ActivateOrResume / O-06 Suspend-retraction / O-07 Unmount / O-19 StopWorld / O-24 CompleteAsyncWork | the same production paths the existing gates use | the 12 observations per family per catalog, and the pure suite |

### 7.1 The frozen surface a later task may rely on

`GameCore.Validation.ProbeHost.LifecycleStressStep`, `LifecycleStressResult` and `LifecycleStressScenario`
(the exact surface is Contract A of `local://gc022-contract.md`, reproduced in `static-checks.log` §2), the 12
observation names, and the four digest literals. After this commit they are additive-only. The four literals were
recomputed independently three ways (the lead's preimage recomputation, the family hosts, the probe) and agree —
see `static-checks.log` §3.

## 8. Known gaps, assumptions and doc ambiguities

1. **Nothing was compiled or executed on this host.** The highest-risk items, in the order a compiler would find
   them: (a) `LifecycleStressScenario.cs` is 2,317 hand-written lines across four modules and two family seams; it
   mirrors the proven `NarrativeLifecycleScenario.BuildWorld` / `CardLifecycleScenario.WorldAndProvider` sequence
   line by line, and its 82 distinct production `Type.Member` calls were mechanically checked to name declared
   members, but only a compiler settles it; (b) the pure rig depends on `ResourceLedger.Acquire`'s
   acquisition-stamp filter matching the coordinator's own stamp — the same assumption the existing
   `TeardownAndQuarantineTests` rig makes; (c) the scenario's four stress manifests must be accepted by the real
   catalog and derivation, which no host-side check can prove.
2. **The 1,000-cycle quantity is proven at full mechanism fidelity in the pure layer** (real coordinator, ledger,
   fence, quarantine and sequencer) and at configurable count on real Unity worlds. A reduced
   `GC_LIFECYCLE_STRESS_CYCLES` run must be reported as reduced; the probe reports the count it used.
3. **Two retained structures are unbounded** and are reported rather than fixed: `ResourceLedger`'s record tables
   and `JobFenceRegistry`'s job table (whose `Release` has no production call site). `resource-policy.md` §2.2
   states the growth, the consequences and two proposed minimal fixes. GC-022 changed no production file, because
   a defect cannot be "reproduced" on a host that cannot run; the build host must confirm or refute each finding
   and the owner of GC-004/GC-014 must decide the fix. **This is the one item that could block a TEST-023 "bounded
   memory" promotion.**
4. **A failed release is permanent, by design as read.** `ManagedResourceLease.Dispose` records the attempt before
   running the disposer and refuses a repeat, so a transient disposer failure pins the lease forever and the
   quarantine registry's entry ceiling is the only containment. GC-022 asserts this reading; if P-048/P-049 intend
   that a *transient* failure is retryable under bounded attempts, `ManagedResourceLease` needs an explicit
   retry and the registry needs abandoned-reference accounting. Recorded as an open protocol question, not decided
   here.
5. **The composition-side quarantine byte bound is unreachable today.** `ResourceLedger.Acquire` has no byte
   parameter and records `Bytes = 0`, so `QuarantineRegistry.Bytes` is always zero on that path and only the entry
   count bounds it. The Unity ledger (`WorldResourceLedger.Acquire(..., ulong bytes)`) does record bytes. Reported
   as a reporting gap, not a leak.
6. **Doc ambiguity: what "1,000 mount/unmount cycles" counts.** A cycle could be an installation mount/unmount, a
   world create/destroy, or a full scenario run. Read here as an installation mount/unmount on one live world
   (O-03 + O-07), which is what TEST-015's sentence describes and what the pure suite and the Unity scenario both
   implement. The world-incarnation variant is covered separately by the reload matrix and by the existing
   lifecycle scenarios' own teardown.
7. **Doc ambiguity: `P-048`'s "dispose at most once" versus `P-049`'s retryable transient failures.** See item 4;
   the two readings differ and the protocol does not pick one. GC-022 implements the strict reading (one attempt)
   and documents the consequence.
8. **The Play Mode matrix's exit-code contract is reasoned, not executed.** The four combinations, the per-cycle
   JSONL and the watchdog markers are syntax-checked and their embedded Python was exercised against synthetic
   evidence, but the orchestration was never run end to end on this host.
9. **`-probeLifecycleStress` does not exist in the marker-free release clone** by design (the release script strips
   it), so a release-player probe loop must not expect it. The validation player and the Editor suites are where
   this mode runs.
10. **The Editor's unresolved intermittent pre-dispatch hang remains unresolved.** Every Unity invocation in this
    change set's scripts is watchdog-bounded and retried once on 124/137, and the matrix records the hang as a
    frequency. A hung invocation is never a pass.

## 9. Inventory proposals (proposals only — nothing promoted here)

`artifacts/gates/w4-generic-profile/inventory.json` was **not edited**. Per the brief, the rows this task newly
evidences are proposed here for the build host to promote after the gate runs. Each proposal names the evidence
that would carry it; a row may only be promoted from an archived passing run.

| Row | Current | Proposed | Evidence that would carry it |
|---|---|---|---|
| `P-048` | Implemented+Evidenced | unchanged, delta recorded | The 1,000-cycle baseline test, the stalled-job test, the throwing-disposer test, the exhaustion test, the pure `dotnet` trx plus the EditMode XML, plus `-probeLifecycleStress` in the player. No promotion: the row is already promoted, and the delta is the repeated-teardown scale plus the declared quarantine bound. |
| `P-047` | Partial | **may promote after the gate** | 100 delayed completions discarded, the in-flight job fence holding exactly its reachable buffer, `GatedCallbackPath` refusing at a retired lease's closed gate, and the same three on a real narrative and card world: `artifacts/gc-022/unity/lifecycle-stress-editmode.xml`, `artifacts/gc-022/toolchain/probe-lifecycle-stress.json`. |
| `P-046` | Partial | **may promote after the gate** | 1,000 real cycles with `RefusedTransitionCount == 0`, wait/resume on every churn round, every installation reaching `Disposed` only when nothing is retained. |
| `P-007` | Partial | **may promote after the gate** | 100 stale completions discarded with no resource released or reacquired, a foreign incarnation refused, a lease whose gate is closed dropping dispatch before the callback gate is asked. |
| `P-003` | Partial | unchanged, delta recorded | Acquisitions traced to retirement or quarantine by kind, and teardown retracting only the installation's own leases. |
| `P-012` | Partial | unchanged, delta recorded | 10 provider-churn rounds with the consumer waiting and resuming on every round and never removed. |
| `P-035` | Implemented+Evidenced | unchanged, delta recorded | Registry back to its pre-create baseline, `Disposed` host, disposed storage, zero retained resources and zero outstanding jobs after every cycle; the reload matrix adds per-session fresh incarnations across all four reload settings. |
| `P-050` | Partial | unchanged, delta recorded | A repeated unload and a repeated settle release nothing a second time. |
| `P-060` | Partial | unchanged, delta recorded | The artifact set of this task, including the reproduced 57-allocation leak baseline and the policy. **The leak attribution is the open half**: `P-060` should not move until the Editor run with `UNITY_JOBS_NATIVE_LEAK_DETECTION_MODE=2` has attributed every allocation. |
| `P-058` | Implemented+Evidenced | unchanged, delta recorded | One additional probe mode executing the stress scenario in the stripped IL2CPP player. |
| `P-049` | Partial | **no change proposed** | GC-022 adds no recovery-session evidence and must not be read as closing the `O-22`/bounded-retry gap GC-018 and the W5 gate already recorded. |
| `O-03`, `O-04`, `O-07`, `O-19` | Implemented+Evidenced | unchanged, delta recorded | The same production paths, now driven at scale (1,000 cycles) and in the player. |
| `O-06` | Partial | unchanged, delta recorded | The churn rounds retract the consumer's contribution through a real P-048 pass; the observation names the retraction. |
| `O-24` | Partial | **may promote after the gate** | Exactly 100 delayed completions producing zero dispatches, with the decision values recorded per batch. |

## 10. What the build host should look at first

1. `dotnet build` errors in the two new pure test files (they are the smallest, most mechanical surface).
2. Unity import errors in `LifecycleStress{Scenario,Family,NarrativeHost,CardsHost}.cs` — the highest-risk file is
   the scenario; its construction sequence mirrors the two proven family scenarios, so a divergence there is a
   real finding about the family APIs rather than about this task's design.
3. The EditMode result XML for `GameCore.LifecycleStress.Tests`: a failing observation carries its own `Describe()`
   text, which names the family, the resolved cycle count and the counters, so no re-run is needed to diagnose it.
4. The Play Mode matrix summary for the hang frequency, and the leak attribution for the 57 allocations.
5. The two unbounded-growth findings of §8 item 3, which are the only items here that could need a production
   change — in the owner module, not in this task's files.
