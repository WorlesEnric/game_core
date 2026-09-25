# W2-GATE build report

**Result: Pass.** Final end-to-end `tools/run_w2_gate.sh` exited 0 on the fixed source tree. Starting revision was `5d3429b` on `origin/w2-gate`; implementation fixes are committed as `4ca04f6`, documentation and the Unity-resolved lockfile as `db17290`. The final gate ran before these commits with the same source content. No probe exited with a signal; the previously reported intermittent exit 139 did not reproduce in the 25 final player invocations. Its historical root cause is therefore not proven by this run.

## Host and toolchain

- Ubuntu 24.04, Linux x86_64 kernel 7.0.0-31-generic, Intel i7-12700KF; GCC 13.3.0, Clang 18.1.3, GNU ld 2.42 (`toolchain/environment.txt`).
- .NET SDK 8.0.425, MSBuild 17.11.48, runtime 8.0.31; Unity Editor 6000.0.75f1 (26349cd2a5c8). Unity player target StandaloneLinux64, IL2CPP, x64, High managed stripping (declared and re-read by `BuildProbe`; player reports IL2CPP and architecture in probe JSON).
- Pure solution targets .NET Standard 2.1; Unity compile invokes C# 9. No `UnityEngine` dependency was introduced into the pure assemblies.

## Commands and final results

From repository root, with `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`:

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet PROBE_RUNS=5 tools/run_w2_gate.sh
python3 tools/check_game_core_csharp.py
```

The gate ran the exact following commands in order (the script sets `ARTIFACTS` to `artifacts/w2-gate`, `UNITY_PROJECT` to `unity/GameCore.Validation`):

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w2-gate/trx
$UNITY -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/w2-gate/unity/resolve.log
$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/w2-gate/unity/editmode-results.xml -logFile artifacts/w2-gate/unity/editmode.log
$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/w2-gate/unity/playmode-results.xml -logFile artifacts/w2-gate/unity/playmode.log
UNITY=$UNITY ARTIFACTS=artifacts/w2-gate/toolchain tools/unity/build_probe.sh
PROBE_RUNS=5 tools/unity/run_probe.sh both
PROBE_RUNS=5 tools/unity/run_world_probe.sh
PROBE_RUNS=5 tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 tools/unity/run_w2_gate_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

All final steps **Pass**. The dotnet build has 0 warnings and 0 errors. Dotnet test: **500 Pass / 0 Fail / 0 Skipped**, by assembly:

| Suite | Pass | Fail | Skipped |
| --- | ---: | ---: | ---: |
| GameCore.ProtocolFixtures.Production.Tests | 10 | 0 | 0 |
| GameCore.Content.Compiler.Tests | 40 | 0 | 0 |
| GameCore.ProtocolFixtures.Tests | 10 | 0 | 0 |
| GameCore.Composition.Tests | 99 | 0 | 0 |
| GameCore.Contracts.Tests | 45 | 0 | 0 |
| GameCore.ReferenceSeams.Tests | 21 | 0 | 0 |
| GameCore.Execution.Tests | 58 | 0 | 0 |
| GameCore.Planning.Tests | 129 | 0 | 0 |
| GameCore.Derivation.Tests | 88 | 0 | 0 |

Unity package resolve/import: **Pass**, with regenerated `Packages/packages-lock.json` committed; Unity did not rewrite tracked `.meta` files. EditMode: **408 Pass / 0 Fail / 0 Skipped**, by assembly:

| Suite | Pass | Fail | Skipped |
| --- | ---: | ---: | ---: |
| GameCore.Composition.Tests | 99 | 0 | 0 |
| GameCore.Derivation.Tests | 88 | 0 | 0 |
| GameCore.Planning.Tests | 129 | 0 | 0 |
| GameCore.Unity.Assembly.Tests | 10 | 0 | 0 |
| GameCore.Unity.Messages.Tests | 10 | 0 | 0 |
| GameCore.Unity.Runtime.Tests | 53 | 0 | 0 |
| GameCore.W1Gate.Tests | 8 | 0 | 0 |
| GameCore.W2Gate.Tests | 11 | 0 | 0 |

PlayMode: **6 Pass / 0 Fail / 0 Skipped** (`GameCore.Unity.Adapters.Tests`). IL2CPP player build: **Pass**, including generated-catalog byte-identity check. Player probes: five clean process exits each, **25/25**, with no crash or `Fail` step: GC-001 positive 5× Pass/exit 0; GC-001 missing-registration negative 5× ExpectedNegative/exit 3; GC-005 world dispatch 5× Pass/exit 0; W1-GATE 5× Pass/exit 0; W2-GATE 5× Pass/exit 0. Each W2 player invocation executes generated and fixture catalogs: 11 named scenario observations per catalog (22 Pass records in each JSON). Documentation validator and self-test: Pass; C# static scan: 196 files, ok.

The real gate path is `CompositionHost.SubmitEdit/Drain` → `CompositionDerivationInput`/`DerivationEngine` → `DerivedCompositionProposal` → `OwnershipSchedulePipeline` (`OwnerAuthorityValidator`/`SlotPolicyValidator` and `ScheduleCompiler`) → `AssemblyPlanner`/`AssemblyPublisher` → `UnityWorldHost.Submit`/`WorldMessagePlane` and `WorldTimeDriver`/`UnityExecutionDriver` → committed event plus `AssemblyPublisher.Spawn`. The player JSON records one mounted revision/epoch 2, two eligible target rows, four migrated slots, five dispatched entries, one committed command and matching event, publication/spawn at epoch 3 with lane/world counters joined, and eight idle frames with zero new steps. Both generated and fixture catalog runs use this same module path; no private per-task descriptor replaces these components in the W2 gate scenario. Private suites remain independently run.

## Defects fixed

- `ScheduleCompilerTests.cs` lived below `GameCore.Planning`, so an unqualified `CompiledSchedule` resolved to the older planner type rather than the semantic compiler type; moved the test namespace outside that containing namespace while retaining its existing fixtures. This removed 57 dotnet compilation errors without removing assertions.
- Unity runtime integration had collisions between `Contracts.CompositionProposal` and `Planning.CompositionProposal`, and between planner and scheduling `CompiledSchedule`; qualified the intended types in the bridges and compiler adapter.
- W2 fixture declarations and world used missing `ISpawnApplier`/`SpawnRecipe` imports, incorrectly nested `Unity.Entities`, passed `FactoryKey` instead of `Id128` field identity, resolved the wrong compiled-schedule type, and decoded `StepMessage.Schema` instead of `PayloadSchema`. Corrected those types and fields. The scenario and player host likewise needed valid imports, definite assignment, a string representation of the 128-bit world session, and valid teardown locals.
- `OwnershipSchedulePipeline` validated a declared schema migration from destination version 2 to itself; `SlotPolicyValidator` correctly returned `PreserveRuntimeState`, so every W2 assembly was refused. Validate the declared preceding source version to the destination against the registered executor; the planner separately validates live slot versions. The current catalog's registered 1→2 migration now executes. **Design seam:** the manifest carries the destination version and migration key, not an explicit source version, so this gate checks the preceding version; a future non-adjacent migration needs an explicit declared source/pair rather than guessing one.
- GC-006 returns an assembly for every live target, including base-only ineligible targets. The gate now counts only non-base-only assemblies when asserting the two eligible targets; it did not change the expected count. More importantly, a later provider with no matching live target still produced four unchanged assemblies: `DerivedAssemblyPipeline` now uses GC-006's real empty `Delta` to return `NoTargetChange` before publishing, preserving the composition publication for the future spawn.
- The idle gate fact reported the world's cumulative dispatch count (two legitimate earlier steps), not dispatches made during the eight idle frames. It now records the difference and checks zero while also asserting unchanged step, image, demand and pending wakes. No expected test value was changed, and no test was skipped/ignored/deleted.
- Added `GameCore.Derivation` to the `GameCore.Unity.Runtime` allowed-reference row in `docs/game-core/04-unity-integration.md` §2 and committed Unity's generated lockfile.

## Evidence and limits

Final source/result evidence is in `artifacts/w2-gate/dotnet-{build,test}.log`, `trx/*.trx` (only the final nine TRX files), `unity/{resolve,editmode,playmode}.log`, `unity/{editmode,playmode}-results.xml`, `toolchain/{codegen,build}.log`, `toolchain/environment.txt`, `toolchain/probe-*.json*`, `toolchain/player-*.log*`, and validator logs. Every evidence file is below 2 MB. The zero-length wrapper `unity-resolve.log` and `unity-playmode.log` are normal: Unity writes its specified `-logFile` instead. Earlier failed attempts were investigated and fixed; their overwritten stage logs are not presented as final passing evidence. No known final test failure or blocked required gate remains. A clean five-run probe sweep did not establish the cause of the historical intermittent native crash, only that it did not recur here.
