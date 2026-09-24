# Wave 1 integration gate — Linux build report

**Result: Pass on the tested revision** `688fd4d7cbb0b87a987edcb5b9618c7a3a8efdb0` (the code/lock revision; this report and evidence are committed afterward). `tools/run_w1_gate.sh` completed with exit 0 after the fixes below. Earlier attempts stopped on Unity compilation, then on three EditMode failures; those results are not represented as passing runs. The latest complete run is the evidence cited below.

## Host and profile

- Host: Linux x86_64, Ubuntu 24.04, kernel `7.0.0-31-generic`, Intel i7-12700KF.
- .NET SDK `8.0.425`; Unity Editor `6000.0.75f1`; Python `3.12.3`; Git `2.43.0`.
- Player: StandaloneLinux64 x64, IL2CPP, High managed stripping; Burst enabled. Native tools recorded by `toolchain/environment.txt`: GCC `13.3.0`, Clang `18.1.3`, GNU ld `2.42`.
- Pure projects built for `netstandard2.1` and Unity compilation used C# 9. No UnityEngine/Unity.Entities references were found in pure assembly source; project/asmdef/manifest dependency search found no production reference to `GameCore.ReferenceSeams`, `GameCore.TestFixtures`, or the old seam package. The W0 seam remains deliberately referenced by `GameCore.ProtocolFixtures` and its own test-only project.
- Generated catalog SHA-256: `2f0e85d0d7c96b0b05404a0b5c7cc1639e625fe44a13e5d83e5f3c2d2406c005`; generated catalog fingerprint: `a4ea6f9190c40054b6733e6bcdfe060f478e77f0f3f8a0020c5a341381d353e7`. Resolved package-lock SHA-256: `c6ddd46d3c125c76ac67b2616f9910558c0b2f31d8ac46cc65f898e403b46df9`. Source solution SHA-256: `cc8cfe8b555d59263d274af372e645f710aa3fc74efd099e427f03eba2f41fb8`.

## Commands actually run

From the `w1-gate` worktree:

```sh
git fetch origin && git checkout w1-gate && git reset --hard origin/w1-gate
/home/worlesenric/.dotnet/dotnet --version
/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -version
uname -a && git --version && python3 --version
# Environment for each gate invocation:
UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET_ROOT=/home/worlesenric/.dotnet \
PATH=/home/worlesenric/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
DOTNET_CLI_TELEMETRY_OPTOUT=1 tools/run_w1_gate.sh
```

The gate command was invoked five times: first three runs stopped at Unity resolve on C# errors, the fourth at EditMode (119/122), and the fifth completed. No isolated suite was substituted for the final full run. Its steps, as executed by the script, were:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w1-gate/trx
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation -logFile artifacts/w1-gate/unity/resolve.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults artifacts/w1-gate/unity/editmode-results.xml -logFile artifacts/w1-gate/unity/editmode.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults artifacts/w1-gate/unity/playmode-results.xml -logFile artifacts/w1-gate/unity/playmode.log
UNITY="$UNITY" ARTIFACTS=artifacts/w1-gate/toolchain tools/unity/build_probe.sh
ARTIFACTS=artifacts/w1-gate/toolchain tools/unity/run_probe.sh both
ARTIFACTS=artifacts/w1-gate/toolchain tools/unity/run_world_probe.sh
ARTIFACTS=artifacts/w1-gate/toolchain tools/unity/run_w1_gate_probe.sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

The script supplies absolute project, player, and artifact paths to its children. Unity test commands intentionally omit `-quit`, as required by the runner. The build step generated the catalog and built the IL2CPP player; the gate's `git diff --exit-code` check confirmed the committed generated catalog was byte-identical to codegen output.

## Final results

| Run/suite | Pass | Fail | Skipped/NotRun | Evidence |
| --- | ---: | ---: | ---: | --- |
| Whole `dotnet/GameCore.sln` Release build | 15 projects built, 0 warnings | 0 errors | 0 | `dotnet-build.log` |
| `GameCore.ReferenceSeams.Tests` (W0 seam/API) | 21 | 0 | 0 | `trx/*.trx` |
| `GameCore.ProtocolFixtures.Tests` (W0 fixture oracle) | 10 | 0 | 0 | `trx/*.trx` |
| `GameCore.Contracts.Tests` | 45 | 0 | 0 | `trx/*.trx` |
| `GameCore.Content.Compiler.Tests` | 40 | 0 | 0 | `trx/*.trx` |
| `GameCore.ProtocolFixtures.Production.Tests` | 10 | 0 | 0 | `trx/*.trx` |
| `GameCore.Composition.Tests` | 93 | 0 | 0 | `trx/*.trx` |
| `GameCore.Execution.Tests` | 28 | 0 | 0 | `trx/*.trx` |
| **.NET total, seven projects** | **247** | **0** | **0** | `dotnet-test.log`, seven latest-run TRX files |
| Unity package resolve / script import | Pass | 0 | 0 | `unity/resolve.log` |
| EditMode `GameCore.Composition.Tests` | 93 | 0 | 0 | `unity/editmode-results.xml` |
| EditMode `GameCore.Unity.Runtime.Tests` | 21 | 0 | 0 | same XML |
| EditMode `GameCore.W1Gate.Tests` | 8 | 0 | 0 | same XML |
| **EditMode total** | **122** | **0** | **0** | same XML |
| PlayMode `GameCore.Unity.Adapters.Tests` | 6 | 0 | 0 | `unity/playmode-results.xml` |
| Generated catalog + Linux64 IL2CPP player build | Pass | 0 | 0 | `toolchain/codegen.log`, `toolchain/build.log` |
| GC-001 positive probe | 8 observations | 0 | 0 | `toolchain/probe-result.json` (exit 0) |
| GC-001 missing-registration negative probe | 2 Pass + 1 ExpectedNegative | 0 unexpected | 0 | `toolchain/probe-negative.json` (expected exit 3) |
| GC-005 world-dispatch probe | 7 observations | 0 | 0 | `toolchain/probe-world-dispatch.json` |
| W1-GATE player probe | 23 observations | 0 | 0 | `toolchain/probe-w1-gate.json` |
| Documentation self-test / validator | 9 fixture cases / 14 documents | 0 | 0 | `validator-self-test.log`, `validator.log` |

The W1 player result is `task=W1-GATE`, `mode=W1Gate`, `result=Pass`, with 23 passing observations: ten generated-catalog steps, ten fixture-catalog steps, derivation, and two facts digests. Its JSON records two distinct worlds, one first operation admitted through the CompositionHost lane, world A's guarded step at logical step 1 with counter 111 and world B idle at step 0. The next operation's injected postwrite failure left the next stage at one execution and counter 222, preserved the last published image at step 1, faulted world A and quarantined two job handles; teardown settled both, left zero outstanding jobs/resources and restored the registry count. The same scenario ran in the 8 passing Editor W1Gate tests. This is the W1 demand handoff, not GC-008 plan-to-ECS live publication: composition lane revision/epoch reached 2 while the world's assembly epoch remained 1.

## Repairs and decisions

1. Added the `GameCore.Execution` import for `IdSequence` and the integration namespace import for `CatalogPluginDeclaration`. Corrected the serializer's owner (`W1GateCatalog.SerializerKey`), added the missing `LaneARevision` fact, passed the concrete `ImmutableCatalog` where serializer lookup is required (the narrower `ICatalog` does not expose it), and used the already-generated `Id128` for the world session. These were compiler errors on first Unity import; no public contract or reference seam changed.
2. Corrected the first-stage observation invariant: before the first committed step the world has **one** initial published image, not two. The observable result afterward was already two images and step 1. This changes a mistaken internal pre-step expected value, not the behavioral requirement; the W1 gate text in 09 and TEST-018 in 08 require the initial image and one committed image, while failed steps must publish none.
3. On a postwrite fault, the world now clears pending command/wake demand as the lifecycle enters `Faulted`. The failed command cannot be executed or retried and admission is closed; leaving `PendingDemand=1` falsely presented it as runnable. Jobs are still quarantined and tracked until teardown. The Editor test failure exposed this genuine lifecycle-state defect.
4. Captured separate first-step stage counts/value before the injected second operation. The gate fact's live ECS counts intentionally become 2 after the fault; the first-step test formerly read those final counts while claiming they were first-step observations. It now asserts values captured at the successful step. No test was skipped, weakened or deleted and no behavior expected value was changed.
5. Committed the package manager's regenerated `packages-lock.json` and 33 Unity-created composition `.meta` files. No synthetic metadata or lockfile entries were authored.

## Remaining scope and evidence hygiene

No failing or blocked check within `tools/run_w1_gate.sh`. This report does not claim the broader TEST-018 10× reload-on/off cycles, 1,000 unload cycles, benchmarks, or GC-008 live publication; those are outside this Wave 1 executable gate. The latest run's seven TRX files replace 28 superseded earlier-run TRX files. All committed logs are below 2 MB per file; player binaries and Unity Library/Builds are not committed. Raw logs contain expected injected fault diagnostics and a transient Unity cloud-config timeout, but the final test XMLs and probe JSONs have zero unexpected failures.
