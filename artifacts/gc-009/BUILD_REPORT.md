# GC-009 Linux build and test report

## Host and toolchain

- Ubuntu 24.04, Linux x64, Intel Core i7-12700KF; .NET SDK 8.0.425 / runtime 8.0.31; Unity Editor 6000.0.75f1, Entities 1.4.6, Collections 2.6.6. Player target: StandaloneLinux64 IL2CPP, High managed stripping. The .NET pure projects target .NET Standard 2.1 and the Unity C# compilation uses `-langversion:9.0` (recorded in the Unity compilation log).
- Checkout preparation: `git fetch origin && git checkout gc-009 && git reset --hard origin/gc-009`. All commands below ran in this worktree. .NET commands used `DOTNET_ROOT=$HOME/.dotnet`, `PATH=$HOME/.dotnet:$PATH`, and `DOTNET_CLI_TELEMETRY_OPTOUT=1`.
- Test results below are actual executions, not the authoring host's static-check results. Final raw files are in `artifacts/gc-009/trx-final/`, `editmode-all.xml`, `playmode-all.xml`, and `probe/`; Unity logs are also retained. Each final evidence file is below 2 MB.

## Commands and results

| Command / suite | Pass | Fail | NotRun | Blocked | Evidence |
|---|---:|---:|---:|---:|---|
| `$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release` | Build passed (0 warnings, 0 errors) | 0 | 0 | 0 | Build command output |
| `$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-009/trx-final` | 287 | 0 | 0 | 0 | `trx-final/*.trx` |
| `GameCore.ProtocolFixtures.Production.Tests` | 10 | 0 | 0 | 0 | TRX |
| `GameCore.Execution.Tests` | 28 | 0 | 0 | 0 | TRX |
| `GameCore.Content.Compiler.Tests` | 40 | 0 | 0 | 0 | TRX |
| `GameCore.Planning.Tests` | 40 | 0 | 0 | 0 | TRX |
| `GameCore.ProtocolFixtures.Tests` | 10 | 0 | 0 | 0 | TRX |
| `GameCore.Contracts.Tests` | 45 | 0 | 0 | 0 | TRX |
| `GameCore.ReferenceSeams.Tests` | 21 | 0 | 0 | 0 | TRX |
| `GameCore.Composition.Tests` | 93 | 0 | 0 | 0 | TRX |
| Unity `-batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform EditMode -testResults "$PWD/artifacts/gc-009/editmode-all.xml" -logFile "$PWD/artifacts/gc-009/editmode-all.log"` (no `-quit`) | 189 | 0 | 0 | 0 | `editmode-all.xml`, `.log` |
| `GameCore.Composition.Tests` / `GameCore.Planning.Tests` / `GameCore.Unity.Runtime.Tests` / `GameCore.W1Gate.Tests` (EditMode breakdown) | 93 / 40 / 48 / 8 | 0 | 0 | 0 | `editmode-all.xml` |
| GC-009 Unity `NativeDependencyTests` / `ScheduleDispatchAdapterTests` / `TemporalDriverTests` (inside runtime 48) | 7 / 7 / 13 | 0 | 0 | 0 | `editmode-all.xml` |
| Unity same flags, `-testPlatform PlayMode -testResults "$PWD/artifacts/gc-009/playmode-all.xml" -logFile "$PWD/artifacts/gc-009/playmode-all.log"` | 6 | 0 | 0 | 0 | `playmode-all.xml`, `.log`; `GameCore.Unity.Adapters.Tests` |
| `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity ARTIFACTS="$PWD/artifacts/gc-009/probe" tools/unity/build_probe.sh` | Player built | 0 | 0 | 0 | `probe/codegen.log`, `probe/build.log`, `probe/environment.txt` |
| `ARTIFACTS="$PWD/artifacts/gc-009/probe" tools/unity/run_probe.sh` | Positive Pass; negative ExpectedNegative (expected exit 3) | 0 | 0 | 0 | `probe/probe-result.json`, `probe/probe-negative.json`, player logs |
| Same `ARTIFACTS`, `tools/unity/run_world_probe.sh` | Pass | 0 | 0 | 0 | `probe/probe-world-dispatch.json`, player log |
| Same `ARTIFACTS`, `tools/unity/run_w1_gate_probe.sh` | Pass | 0 | 0 | 0 | `probe/probe-w1-gate.json`, player log |
| `python3 tools/validate_game_core_docs.py --self-test && python3 tools/validate_game_core_docs.py && python3 tools/check_game_core_csharp.py` | Three checks passed (9 self-test fixtures, 14 docs, 68 C# files) | 0 | 0 | 0 | Console output; static checks are not runtime evidence |

The initial dotnet build had 5 scheduler compile errors, then 3 fixture import errors; a subsequent test run failed 6/40 Planning tests due to one invalid cross-stage fixture edge. The first Unity compile failed on writes to a readonly `NativeArray<JobHandle>`. The next Unity EditMode run reported 162/162 passing but **did not discover the 27 new temporal tests**, because `Tests/Time/` was outside the test asmdef under `Tests/Runtime/`. After moving that directory, Unity reported missing fixture keys/imports/argument; the ensuing 189-case run failed one native fence combination test. An additional component-job observation initially asserted 22/11 instead of the fixture's actual 222/111. Each was fixed; only the final green results in the table are acceptance results. No test was ignored, skipped, weakened, or deleted.

## Fixes and why

1. `ScheduleCompiler`: use canonical `StageId` comparison and `SystemNode.Stage.StageIndex`; the unresolved comparer and nonexistent direct property prevented compilation, and the latter is needed for directed access-order checks.
2. Planning fixture: import `GameCore.Planning.Scheduling`; remove an invalid inner `requiredAfter` reference to a system in another stage. Its separate stage-level `requiredAfter` already expresses that edge. No expected values changed.
3. `NativeDependencyTable`: remove `readonly` from the mutable `NativeArray<JobHandle>` struct field. When combining a list of resource slots, omit unproduced/default handles while counting their reads; Unity's multi-handle combination of a real handle and default returned a different handle and violated the declared no-op rule for empty slots. This avoids temporary native allocation as well.
4. Move the new Unity Time tests and their `.meta` under the existing Editor test asmdef (`Tests/Runtime/Time/`), so Unity actually compiles and runs all 27. Supply the missing `GameCore.Execution.Time` imports, `CaptureStage` fixture key, and `perStepCapacity` argument. The prior 162/162 green run was incomplete discovery and is not reported as completion.
5. Add a real dependent component read in the W1 fixture's project stage and assert the observed result after one/two steps. The existing Burst `FixtureWriteJob` assigns `SystemBase.Dependency` and the guarded dispatcher forwards it. The GC-009 non-component `TimeFixtureWriteJob` deliberately does not assign `Dependency`: `NativeDependencyTable.Store` passes the handle to the stage fence and the consumer combines it; it has no ECS `ComponentLookup`, so there is no unregistered component access. Both paths ran in the full 189-case EditMode suite, without a safety-system exception in the final Unity logs. The observed fixture results are 111 then 222, matching its accept/settle/fault arithmetic.
6. Commit Unity-generated Planning `.meta` files and the regenerated `packages-lock.json` to make test discovery and package resolution reproducible.

## Coverage limits and outstanding work

- Round 1 did not demonstrate actual overlap of disjoint jobs or independently delayed structural playback; Round 2 below adds execution evidence for independent job dependencies and a producer-bound playback. The 10,000-step/1-2-4-worker replay remains **NotRun**, as does the W2 integration gate with GC-007/008 real ownership and publisher on this branch.
- No Unity safety exception was found in the final EditMode, PlayMode or player logs. A Unity LicensingClient notification logged `Access token is unavailable; failed to update`, but entitlement resolution, test runs and the IL2CPP player build succeeded. No tests remain failing or blocked in the suites above.
- No changes were made to normative design expected values. The fixture edge was invalid according to 09/P-039, while the dependent-stage stage edge remained present.

## Round 2

Host/toolchain unchanged: Ubuntu 24.04 x64, .NET SDK 8.0.425, Unity 6000.0.75f1. Started with `git fetch origin && git checkout gc-009 && git reset --hard origin/gc-009` at `b0e2509`; corrections committed as `b424139`. .NET commands used `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1`. All commands below ran from the repository root.

| Command / suite | Pass | Fail | NotRun | Blocked | Evidence |
|---|---:|---:|---:|---:|---|
| `$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release` | Build succeeded | 0 | 0 | 0 | Build command output |
| `$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-009/trx-round2` | 287 | 0 | 0 | 0 | `trx-round2/*.trx` |
| ProtocolFixtures.Production / Execution / Content.Compiler / Planning | 10 / 28 / 40 / 40 | 0 | 0 | 0 | TRX counters |
| ProtocolFixtures / Contracts / ReferenceSeams / Composition | 10 / 45 / 21 / 93 | 0 | 0 | 0 | TRX counters |
| `$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform EditMode -testResults "$PWD/artifacts/gc-009/editmode-round2-all.xml" -logFile "$PWD/artifacts/gc-009/editmode-round2-all.log"` | 194 | 0 | 0 | 0 | XML and log |
| Composition / Planning / Unity.Runtime / W1Gate EditMode assemblies | 93 / 40 / 53 / 8 | 0 | 0 | 0 | XML assembly counters |
| New `ScheduleExecutionTests` in Unity.Runtime | 5 | 0 | 0 | 0 | XML fixture counter; negative safety-control case **Passed**, not Ignored |
| Same Unity invocation with `-testPlatform PlayMode` and `playmode-round2-all.xml` / `.log` | 6 | 0 | 0 | 0 | XML and log; Unity.Adapters assembly |
| `python3 tools/check_game_core_csharp.py` | 68 source files checked | 0 | 0 | 0 | Command output |

The first Unity run failed compilation: `SystemSpecOf` accepted one access declaration but playback provided both read and write declarations. Changed the helper to accept a `params AccessDeclaration[]` and construct an `AccessSet` from all accesses; no test expectation changed. A second invocation with `-quit` compiled successfully but emitted no result XML: Unity test runs require omitting `-quit` (09 handoff §4.2). The final full EditMode invocation above discovered and executed all 194 cases.

The disjoint job fixture previously called `.Schedule(default(JobHandle))`, ignoring the incoming dependency it measured. Both jobs now schedule with their observed `SystemBase.Dependency`, so a wrongly forwarded producer would actually serialize the second job; the test still observes default for each, verifies the left handle was already in the dispatcher's fence slot when the right ran, and verifies the later ordered observer combines both handles. This proves absence of a dependency edge and safe independently scheduled work, **not measured simultaneous wall-clock execution**.

Playback compiles a buffer producer→consumer edge, publishes its producer handle only into `NativeDependencyTable`, asserts the playback's incoming native handle equals that producer handle, completes the combined handle, and observes the written component and ECB structural change. The negative unsynchronized-container-read control passed by observing the expected guarded fault; no positive-path job-safety-system errors appeared in either final Unity log. The Unity logs contain a LicensingClient `Access token is unavailable; failed to update` notice but entitlement resolved and both test runs completed. No source changes outside the fixture, no expected-value changes, no player build in this round. The 10,000-step/1-2-4-worker replay and W2 integration gate remain NotRun, not Blocked.
