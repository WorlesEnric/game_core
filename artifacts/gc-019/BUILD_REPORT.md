# GC-019 Linux build and test report

## Host and toolchain

- Branch `gc-019`; worktree synchronized first with `git fetch origin && git checkout gc-019 && git reset --hard origin/gc-019` at `c521d3d`.
- Linux x86_64, kernel `7.0.0-31-generic`, Intel i7-12700KF; Ubuntu GCC 13.3.0 and Clang 18.1.3.
- .NET SDK `8.0.425` (`$HOME/.dotnet/dotnet`), Unity Editor `6000.0.75f1`; Linux64 IL2CPP, High managed stripping, Burst enabled. Player settings and hashes are recorded in `toolchain/environment.txt` and the probe JSON. Unity resolved the committed `unity/GameCore.Validation/Packages/packages-lock.json` without changing it; the meta generator created zero files.
- The runtime assembly remains C# 9 / netstandard2.1-compatible; pure adapter sources and fixtures contain no UnityEngine reference.

## Exact successful commands

From the repository root, with `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, and `DOTNET_CLI_TELEMETRY_OPTOUT=1`:

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-019/trx-final
python3 tools/check_game_core_csharp.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
python3 tools/make_unity_metas.py
```

Unity invocations below used `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity` and absolute artifact paths resolved from the repository root. Each editor invocation was guarded by `timeout --signal=TERM --kill-after=60 1800`; the probe harness guards **every** player invocation with `timeout --signal=TERM --kill-after=10 600`. No run hung or needed a debugger.

```sh
$UNITY -version
$UNITY -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/gc-019/unity-resolve.log
$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/gc-019/editmode-results.xml -logFile artifacts/gc-019/editmode.log
$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/gc-019/playmode-results.xml -logFile artifacts/gc-019/playmode.log
UNITY=$UNITY ARTIFACTS=artifacts/gc-019/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 ARTIFACTS=artifacts/gc-019/toolchain tools/unity/run_gc019_probe.sh
```

The build script guards both code generation and the IL2CPP build with the same 1800-second watchdog. The EditMode and PlayMode commands were repeated after fixes; committed XML/logs are the final runs. All earlier player modes were also invoked with `PROBE_RUNS=5 ARTIFACTS=artifacts/gc-019/toolchain`: `tools/unity/run_probe.sh`, `run_world_probe.sh`, `run_w1_gate_probe.sh`, `run_w2_gate_probe.sh`, `run_narrative_probe.sh`, `run_cards_probe.sh`, `run_w3_gate_probe.sh`, `run_w4_profile_probe.sh`, `run_gc013_probe.sh`, `run_w4_gate_probe.sh`. Those harnesses all exited zero, and their per-run JSON and logs are under `toolchain/`.

## Results (final revision)

| Gate | Pass | Fail | NotRun | Blocked | Evidence |
| --- | ---: | ---: | ---: | ---: | --- |
| Complete dotnet solution build | 1 | 0 | 0 | 0 | build command exit 0 |
| Complete dotnet solution tests (12 assemblies) | 780 | 0 | 0 | 0 | `trx-final/` |
| Complete Unity EditMode suite (includes GC-019 12 tests and adapter contracts) | 801 | 0 | 0 | 0 | `editmode-results.xml`, `editmode.log` |
| Complete Unity PlayMode suite (includes four GC-019 adapter frame tests) | 10 | 0 | 0 | 0 | `playmode-results.xml`, `playmode.log` |
| Linux64 IL2CPP player build | 1 | 0 | 0 | 0 | `toolchain/build.log`, `toolchain/environment.txt` |
| `-probeGc019`, five processes | 5 | 0 | 0 | 0 | `toolchain/probe-gc019.json` and `.run2`–`.run5` (46 passing named outcomes per process) |
| Earlier player modes: 11 modes × 5 processes (positive/negative toolchain + nine other harnesses) | 55 | 0 | 0 | 0 | `toolchain/probe*.json*`, corresponding player logs |
| Static C# check; doc self-test; doc validation; meta generator | 4 | 0 | 0 | 0 | console: 389 C# files; 9 self-test fixtures; 14 docs; zero metas created |

The initial untouched sources did **not** pass: the solution first failed to compile, initial dotnet tests ran 777/780, initial Unity EditMode ran 792/801, and initial PlayMode ran 9/10. Those failures were repaired; none was skipped or ignored. No test expected value was changed. The first five player processes themselves reported `Pass`, but the GC-019 harness correctly returned failure because it searched for `physicsStages=; physicsSystems=` while the actual passing observation explicitly reported `physicsStages=<none>; physicsSystems=<none>`. The harness now checks the actual explicit no-physics evidence, and all five processes and its acceptance checks pass on rerun.

## Fixes and reasons

1. Move `IDeviceInputSource` from Unity-only `WorldAdapterFrame.cs` into pure `InputBinding.cs`, so the shared fixture compiling in dotnet can implement the same port; correct the fixture's nullable dictionary out value. Add the direct `GameCore.Planning` asmdef reference required by `LiveAssemblyImageBuilder` and qualify `UnityEngine.Input` to avoid collision with the adapter's `Input` namespace.
2. Correct GC-019 probe/test references to the existing `Gc013NarrativeHost`/`Gc013CardsHost` partial classes, add the narrative fixture namespace for `W1GateKeys`, and rename a shadowing `live` local. These were compilation errors in the newly authored qualification path.
3. Make the application pump enabled on session reset. The prior reset left a test-disabled static switch disabled across a new session; the PlayMode reset contract failed. No second PlayerLoop route was added.
4. In `TypedInputIngress`, count actual sequence regressions and distinguish evicted-key retries from new regressions using a bounded eviction tombstone set. The old branch mislabeled an expired retry and could not satisfy the declared idempotency result. In `ViewRegistry`, do not compare an unpresented view's default scope with a committed scope when counting visual-only reparenting; presented views retain strict comparison. These repaired all three dotnet adapter test failures without changing assertions.
5. Attach and retire each genre's real gameplay stage runtime in the GC-019 world. Without the narrative/card module the system did not consume the reliable command buffer, the first step faulted the world, and every subsequent publication failed. The scenario now maps live targets and binds card table/seats, then disposes that run's module at teardown. The existing GC-013 family construction and single world remain in use; observation names/digests and pass criteria stay unchanged.
6. Evaluate the `view destruction leaves gameplay state intact` snapshot invariant **before** issuing the deliberate follow-up command. The original check compared rows, ledger and step after a real gameplay step; it failed precisely because gameplay correctly continued. The check still requires the unmodified state immediately after view destruction and then independently requires the next command to commit exactly one step. Preserve `rowsUnchanged`/`scopesUnchanged` in probe details.
7. Match the explicit `<none>` physics-stage/system output in the player harness rather than an empty field. The scenario and expected state were already correct; no test or production behavior was weakened.

## Scope and remaining gaps

The GC-019 player ran real card/narrative worlds headlessly across generated and fixture catalogs, including stale input/asset completion fencing, resource-ledger retirement, snapshot presentation, no-views gameplay, visual-only reparenting and absence of a physics stage. `artifacts/gates/w4-generic-profile/inventory.json` records **only** evidenced P-002 and P-034 deltas; both rows remain `Partial`. TEST-002/015/018/019/020 in their full V1 breadth are **not** declared complete: 1,000 lifecycle cycles, 100 delayed callbacks, domain-reload session matrix, physics/animation/audio implementations and bake-versus-runtime recipe comparisons belong to later gates. No remaining failure was observed in the gates actually run here.
