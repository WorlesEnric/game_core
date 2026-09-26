# GC-022 Linux build and test report

## Verdict

**Pass** on branch `gc-022` at evidence commit `5d97811`: full `tools/run_gc022.sh` gate exited 0 with 1,000 generated-catalog mount/unmount cycles per family, five repetitions of every standalone probe, both Unity test modes, and all four Play Mode reload combinations. No accepted run was reduced to 100 cycles. The committed result files and logs under this directory are the evidence. The historical 57 unstacked GC-016 allocations are not a current-run attribution; the initial GC-022 EditMode run exposed 81 stack-attributed GameCore leaks, which were fixed and measured at zero on the accepted rerun.

## Host and toolchain

- Host: Linux x86_64, kernel `7.0.0-31-generic`, Ubuntu 24.04, GCC 13.3.0, Clang 18.1.3, GNU ld 2.42 (`toolchain/environment.txt`).
- .NET SDK: 8.0.425 at `$HOME/.dotnet/dotnet`; VSTest 17.11.1; pure assemblies target .NET Standard 2.1 and C# 9. `dotnet build` reported 0 warnings/0 errors.
- Editor/player: Unity 6000.0.75f1; StandaloneLinux64, IL2CPP, High managed stripping (`toolchain/build.log` and player JSON). Native leak mode `EnabledWithStackTrace` was requested for Editor and applied by the lifecycle player (`probe-lifecycle-stress.json`). Unity resolve produced/confirmed `unity/GameCore.Validation/Packages/packages-lock.json`; no lock or `.meta` changes were generated.
- Revision used by the passing gate: `81f3c6a` (the subsequent `5d97811` commit archives only gate evidence and policy). Probe executable SHA-256 `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`; catalog SHA-256 `5298bbc853b35424c7b7e8300b7c39afe6e27ec493027a22a47336a5cb567625` (`toolchain/environment.txt`).

## Exact accepted commands

From this worktree with `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`:

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
GC_LIFECYCLE_STRESS_CYCLES=1000 PROBE_RUNS=5 tools/run_gc022.sh
```

The gate executes these relevant commands (all Unity invocations have `timeout --signal=TERM --kill-after=60 1800`, all player invocations `timeout --signal=TERM --kill-after=10 600` via `tools/unity/probe_runs.sh`):

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-022/trx
$HOME/.dotnet/dotnet test dotnet/tests/GameCore.Composition.Tests/GameCore.Composition.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~GameCore.Composition.Tests.LifecycleStressTests' --logger trx --results-directory artifacts/gc-022/trx-stress
# Unity: -batchmode -nographics -quit -projectPath unity/GameCore.Validation (resolve)
# Unity: -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode|PlayMode -testResults <absolute xml> -logFile <absolute log>
# Unity: -executeMethod CardCatalogGenerator.GenerateCatalog, CheckpointCatalogGenerator.GenerateCatalog; tools/unity/build_probe.sh generates ProbeCatalog and builds the IL2CPP player.
# Each probe script under tools/unity/ runs its mode five times; tools/unity/run_lifecycle_playmode_matrix.sh runs four combinations, ten cycles each.
python3 tools/attribute_native_leaks.py --log artifacts/gc-022/unity/editmode.log --log artifacts/gc-022/unity/playmode.log --log artifacts/gc-022/toolchain/player-lifecycle-stress.log --policy artifacts/gc-022/leak/policy.md --out artifacts/gc-022/leak/attribution.json
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

Exact per-Editor commands and logs are in `tools/run_gc022.sh`, `playmode-matrix/commands.txt`, and `unity/` and `toolchain/`. Initial preparation was `git fetch origin && git checkout gc-022 && git reset --hard origin/gc-022` in this worktree only.

## Results

| Run | Result | Evidence |
|---|---|---|
| Whole .NET solution build | **Pass**, 0 warnings, 0 errors | Gate output; all netstandard2.1 projects compiled |
| Whole .NET solution test | **Pass**, 990/990, 0 failed, 0 skipped across 12 assemblies | `trx/*.trx` (one latest passing result per assembly) |
| Focused pure lifecycle stress | **Pass**, 12/12; 1,000 cycles × five acquisitions = 5,000 disposed once | `trx-stress/*.trx` |
| Unity package resolve | **Pass**, compilation complete | `unity/resolve.log` |
| Unity EditMode | **Pass**, 945/945, 0 failed, 0 skipped; two families × generated/fixture catalogs, generated runs 1,000 cycles each | `unity/editmode-results.xml`, `unity/editmode.log` |
| Unity PlayMode | **Pass**, 10/10, 0 failed, 0 skipped | `unity/playmode-results.xml`, `unity/playmode.log` |
| IL2CPP StandaloneLinux64 player | **Pass**, High stripping; all three catalogs regenerated with identical committed bytes | `toolchain/build.log`, `toolchain/codegen.log`, `unity/card-codegen.log`, `unity/checkpoint-codegen.log` |
| All standalone probe modes | **Pass**, 16 result modes × 5 = 80 process runs, including positive and expected-negative probes; each process result/exit checked by its script | `toolchain/probe-*.json[.run2..run5]`, `toolchain/player-*.log[.run2..run5]` |
| Lifecycle standalone stress | **Pass**, 5/5 process runs; `resolvedCycleCount=1000`, native mode `EnabledWithStackTrace`; 52 probe steps per result | `toolchain/probe-lifecycle-stress.json[.run2..run5]`, `toolchain/native-leak-attribution.json` |
| Play Mode matrix | **Pass**, domain reload on/off × scene reload on/off, 10/10 each = 40/40 cycles; 4/4 combinations, zero watchdog kills in four attempts | `playmode-matrix/matrix-summary.json`, each combination summary/JSONL/log |
| Native leak attribution | **Pass**, 0 headers/blocks/allocations across accepted EditMode, PlayMode and lifecycle player logs; five lifecycle player logs individually total 0 | `leak/attribution.json`, `toolchain/native-leak-attribution.json` |
| Static C#, contract parity, shell and leak tool self-test | **Pass**, 474 C# files, unchanged five allowed contract additions, 18/18 leak parser self-tests | `static-csharp.log`, `static-contracts.log`, `leak/self-test.log` |
| Documentation validation | **Pass**, self-test 9 fixtures and full 14-document validation | `validator-self-test.log`, `validator.log` |

The gate's documentation validator prints its generic notice that *it* runs no Unity build or gameplay tests; that notice does not describe the preceding gate steps. The legacy Editor pre-dispatch hang occurred **0 times** in the recorded matrix (four attempts, zero watchdog kills) and **0 observed times** in the other successful watchdog-wrapped Editor commands. No GDB capture was needed because no invocation exceeded the no-progress threshold. The built player was actually launched headless, not inferred from its build.

## Defects fixed after actual failures

1. **First Unity resolve failed to compile.** Corrected scenario namespaces and types (`W1GateKeys`, narrative/card fixture catalogs and payloads, clock types, `CapabilityRef`, `OwnerId.FromRaw`) and made matrix `wallClockMs` serialize a `long` losslessly. Pure stress fixture now opens the concrete managed gate at publication and counts only successful disposer callbacks in `DisposedOrder`. The original full .NET run failed two stress assertions; after fixes all 990 pass. No expected values were changed to turn failures green.
2. **Unbounded `ResourceLedger` and `JobFenceRegistry`.** Reproduced the growth with the original 1,000-cycle and completed-job tests. `ResourceLedger` now drops disposed leases/delegates immediately, retains at most 1,024 oldest-retired-first diagnostic records, and reports `EvictedRetiredCount` (5,000 lifetime retirements, 3,976 evictions, zero retained/live after 1,000 cycles). Teardown releases settled fence records after safe retirement and after explicit quarantine release; completed-job test proves `TrackedCount=0` after every unload and 50 automatic releases. Outstanding jobs and quarantined references are never evicted to meet a bound. Strict P-048 dispose-at-most-once remains intact; a failed disposer remains quarantined and is never falsely reported disposed. `resource-policy.md` states the bounds and eviction rules.
3. **First full EditMode run failed 27/945.** The stress scenario's default control-lane queue held 256 entries but result retention was 4,096 with no step expiry; its 257th operation was refused at cycle 129. The scenario now chooses the same 256 queue bound with 64 retained results, so admission continues without increasing capacity. A failed-release removal also left its committed installation `Retiring` after explicit release; `CompositionHost.SettleRetiredInstall` and `LifecycleController.ReleaseQuarantine` complete the lawful `Retiring → Disposed` edge only when no reference remains. The second EditMode run reached all observations with `failed=0` but 26 assertions failed because combined fixture steps were prefixed twice; removed the duplicate prefix and compared sequence contents rather than list implementation types. The accepted third run passed 945/945 with the four frozen digests unchanged.
4. **Native leak attribution initially misparsed Unity indexed callstacks.** The first stack-enabled failing EditMode run emitted 49 blocks/81 allocations; `tools/attribute_native_leaks.py` initially called them unattributed because it only recognized address-leading frames. Parser now recognizes `#N (Mono JIT Code) ...` frames; rerun classified **81/81 GameCore-owned**, zero unattributed, with 41 distinct signatures. All allocations originated at eager `NativeDependencyTable` slot allocation, including schedules that never store a job fence; `NativeDependencyTable` now allocates only on first `Store`. Accepted EditMode and full gate showed zero persistent allocations. No policy bound masks a GameCore-owned allocation; `leak/policy.md` records the fix and clean result.
5. **No Unity package lock or `.meta` change** was needed after successful resolve. No documented test expected value, frozen digest, test skip, `[Ignore]` or weakened check was introduced. The list/array assertion was changed to sequence comparison because both carry identical expected names; not an expected-value change.

## Remaining limits

No failing or blocked required GC-022 gate step remains. The composition-side quarantine byte capacity still receives zero-byte entries because `ResourceLedger.Acquire` has no byte argument; the entry bound applies, while `WorldResourceLedger` records actual bytes. A disposer that throws on its one permitted attempt stays quarantined permanently; this is strict P-048, not an automatic retry. Those limitations are explicit in `resource-policy.md`, not reported as leaks or waived tests. The archived GC-016 57-allocation log cannot retroactively gain callstacks; the current stack-enabled 81-allocation finding was attributed and driven to zero.
