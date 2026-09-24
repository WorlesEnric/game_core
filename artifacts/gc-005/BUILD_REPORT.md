# GC-005 Linux build report

## Result

**Pass for the GC-005 build-host gates.** Real .NET, Unity Editor, Play Mode and Linux x64 IL2CPP execution completed. This is not full V1 conformance or qualification of other platforms.

| Gate | Status | Observed result |
| --- | --- | --- |
| .NET Release build | Pass | 0 warnings, 0 errors |
| GameCore.Execution.Tests | Pass | 28 passed, 0 failed, 0 skipped |
| W0 GameCore.ProtocolFixtures.Tests | Pass | 10 passed, 0 failed, 0 skipped |
| W0 GameCore.ReferenceSeams.Tests | Pass | 21 passed, 0 failed, 0 skipped; frozen API gate remains green |
| Unity package resolution | Pass | Regenerated lock and Unity-generated metadata committed |
| Unity EditMode runtime tests | Pass | 21 passed, 0 failed, 0 skipped (20 original tests plus direct-disposal regression) |
| Unity PlayMode adapter tests | Pass | 6 passed, 0 failed, 0 skipped |
| Actual Play Mode enter/exit smoke | Pass | 10 cycles with domain reload enabled, 10 disabled; 20 distinct world incarnations |
| Linux x64 IL2CPP / High stripping build | Pass | BuildSummary: Succeeded, 0 errors, **12 warnings**, totalSize 1,048,858,172 bytes |
| GC-001 positive player | Pass | 7 Pass, 0 Fail; process exit 0 |
| GC-001 missing-registration player | Pass (expected negative) | 2 Pass checks plus 1 ExpectedNegative record; process exit 3 |
| GC-005 world-dispatch player | Pass | 7 Pass, 0 Fail; process exit 0 |
| Documentation validator self-test | Pass | 9 isolated positive/negative fixtures |
| Documentation validator | Pass | 14 documents; links, anchors, IDs, traceability, DAG and waves |

Per-test names/results are in `test-summary.json`, the NUnit XML files and the TRX files in `dotnet-results/`. The latest complete .NET run is the three `05_49_03` TRX files. Earlier failing/partial runs are retained, not counted as final passes.

## Host and toolchain

- Ubuntu 24.04, Linux x86_64, kernel `7.0.0-31-generic`; Intel Core i7-12700KF. `host.log` contains `uname -a`.
- .NET SDK **8.0.425**, MSBuild **17.11.48**, runtime **8.0.31**. Full output: `dotnet-info.log`.
- Unity **6000.0.75f1**, revision **26349cd2a5c8**. Linux IL2CPP support was installed and usable; licensing succeeded.
- Resolved Entities **1.4.6**, Burst **1.8.28**, Collections **2.6.6**, Mathematics **1.3.2**, Test Framework **1.6.0**.
- Player: StandaloneLinux64, Release IL2CPP, High managed stripping, .NET Standard 2.1, Burst enabled. Build settings are set and re-read by `BuildProbe`; runtime JSON separately confirms IL2CPP/X64.
- `dotnet/Directory.Build.props` retains C# 9.0 and warnings-as-errors. Pure execution builds as netstandard2.1 with only the reference-seam project reference, no UnityEngine dependency. No language/profile relaxation was made.
- `../toolchain/environment.txt` records native compiler details and player/catalog hashes. Player binaries and Library are not committed; the build scripts reproduce them.

## Commands executed

Commands below ran from the assigned worktree. Variables abbreviate the exact paths recorded in the Unity log headers. The .NET/documentation/player-run subprocess command arrays, exit codes and timings are also in `commands.json`.

```sh
git fetch origin && git checkout gc-005 && git reset --hard origin/gc-005
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$HOME/.dotnet:$PATH
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
export DOTNET=/home/worlesenric/.dotnet/dotnet
PROJECT="$PWD/unity/GameCore.Validation"
RESULTS="$PWD/artifacts/gc-005"

dotnet --info
uname -a
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory "$RESULTS/dotnet-results"

"$UNITY" -batchmode -nographics -quit -projectPath "$PROJECT" \
  -logFile "$PWD/artifacts/toolchain/resolve-gc005.log"
# Repeated after fixes, changing only -logFile to:
# artifacts/gc-005/resolve-second.log
# artifacts/gc-005/resolve-third.log
# artifacts/gc-005/resolve-fourth.log
# artifacts/gc-005/resolve-final.log

"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Runtime.Tests \
  -testResults "$RESULTS/editmode-results.xml" -logFile "$RESULTS/editmode.log"

"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -runTests -testPlatform PlayMode -testFilter GameCore.Unity.Adapters.Tests \
  -testResults "$RESULTS/playmode-results.xml" -logFile "$RESULTS/playmode.log"

"$UNITY" -batchmode -nographics -projectPath "$PROJECT" \
  -executeMethod GameCore.Validation.Editor.PlaySessionSmoke.Run \
  -logFile "$RESULTS/play-session-smoke.log"

python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py

tools/unity/build_probe.sh
tools/unity/run_probe.sh
tools/unity/run_world_probe.sh
```

Execution history:

1. Reset started at `49612fe`. First .NET build failed on three compiler errors; second build exposed one test compiler error. The first test invocation still ran the two W0 suites successfully, but the whole command failed compilation. Next build passed; next test run had 24/28 execution tests passing and four failing. Corrected fixtures then produced 59/59 passing tests across all three suites.
2. Four Unity resolve attempts failed compilation; the fifth succeeded. All failure logs are retained.
3. One EditMode launch was rejected with exit 134 because the successful resolver was still shutting down and held this worktree's project lock. No other worker's Unity process was touched. After its process exited, the original 20 tests passed.
4. Initial PlayMode: 5/6 passed, reset-survivor lifecycle failed (`Running` instead of `Disposed`). After fixing direct disposal, the same six tests passed. Initial XML/log are retained as `initial-playmode-*`.
5. The smoke performed 20 real enter/exit cycles, not 20 direct calls to the reset method. It restores the previous Editor enter-play settings. `play-session-smoke.jsonl` records one result and world incarnation per cycle. It checks fresh bootstrap ownership, one loop route, no fallback, one command/one step, no double updates, continuing idle ingress/output, then no surviving hosts/routes; reload-disabled cycles also inspect the retained host reference after exit for disposed state/storage and settled jobs.
6. Final EditMode rerun after disposal changes and the added regression: 21/21 passed.
7. Player build ran once, succeeded; positive, negative and world-dispatch player processes then ran once each and matched their required exits/results.

## Fixes and rationale

### Pure build and test fixtures (`2e6122d`)

- `PublicationBoundary.cs` and its threading test: `Environment.CurrentManagedThreadId` is a property, not a method. Removed invalid invocation parentheses; no threading behavior changed.
- `WorldResourceLedger.RetireAll`: replaced nonexistent `QuarantinedCount` with `RetainedResourceCount`. After its retirement pass, the only remaining retained resources are quarantined ones; this reports current remaining ownership rather than historical quarantine attempts. Existing ledger tests pass.
- `FixedStepAdmitsWholeStepsAndRetainsTheRemainder`: corrected arithmetic. At 35 ms, 5 ms retained + 10 ms newly elapsed spends 10 ms and leaves 5 ms, not zero. At 40 ms that remainder plus 5 ms funds one step. **00 P-036** forbids dropping debt or increasing step duration.
- `FixedStepCatchUpIsBoundedAndDebtIsNeverDropped`: every pump must obey the configured maximum of three, not just the first. The seven remaining steps now drain over three pumps (3, 3, 1), preserving the one-tick remainder and final total of ten. **00 P-036** explicitly says “at most the limit per pump.” This adds debt/cap assertions rather than relaxing them.
- `ProducerWithoutItsConsumerStageFailsDrainValidation`: added the missing consumer entry to the fixture's plan. Marking stage index 2 as dispatched cannot identify a consumer StageId absent from the table. The test still requires failure before the consumer runs and success after it runs. **00 P-043/P-044** require the declared consumer/drain before commit.
- `SnapshotIsOrderedByResourceIdAndReportsQuarantineBytes`: corrected expected order to the allocated ResourceIds. The first acquired resource (`third`, misleading local name) has ID low word 1; the second (`first`) has low word 2. Resource keys 9/8 do not define ResourceId ordering. **00 P-008** requires canonical stable-ID comparison. Quarantine byte/count assertions are unchanged.

These are the only changed expected values. No test was deleted, ignored, skipped or weakened, and no frozen reference-seam source or API snapshot was changed.

### Unity compile and teardown (`8968f28`)

- Added the missing read-only `WorldPumpResult.Lifecycle` property already assigned by its constructor.
- Removed `readonly` from mutable `NativeArray` fields `stepJobIds`/`stepJobHandles`; their indexer writes are illegal on readonly struct fields.
- Imported `Unity.Jobs` for fixture `IJob` and `GameCore.Execution` for the Editor dispatch-test plan types.
- Added the required direct `Unity.Collections` asmdef reference to the adapter assembly (Entities generator diagnostic DC0061).
- Corrected the nested PlayerLoop type to `Update.ScriptRunBehaviourUpdate`.
- Adapted Entity-returning `FixtureWorldState.Seed` to the `Action<World>` seed callback with a lambda; the returned Entity is intentionally unused.
- **Real lifecycle/ownership defect:** `UnityWorldHost.Dispose` previously called driver/storage disposal directly, leaving lifecycle `Running` and bypassing resource retirement and tracked-job settlement accounting. It now calls the existing `Stop` path with a host-owned disposal operation, and refuses to claim disposal when teardown is blocked. This reuses **00 P-035/P-047/P-048** lifecycle and ownership rules instead of inventing a second teardown sequence.
- Added `DirectDisposalSettlesFaultedJobsAndRetiresTheWorld`: schedules the real throwing fixture, checks retained jobs exist, calls Dispose twice, and checks settled jobs, zero outstanding jobs/resources, disposed lifecycle/storage and registry removal. The pre-existing failing PlayMode reset test now passes unchanged.

### Unity-generated inputs and session smoke (`483526a`)

- Committed regenerated `packages-lock.json`, generated package/reference-seam/Assets `.meta` files, and the generated bootstrap scene (including its metadata).
- Added `PlaySessionSmoke.cs` and the Editor assembly references it uses to make the required reload-enabled/disabled session scenario executable and reproducible. No test-run command uses `-quit`.
- The performance-test framework created temporary run-info/settings JSON and metadata during the build, then removed them in its own cleanup. Their transient metadata was removed from the final tree as well; these were not performance benchmark results.

No design/seam change was needed. The cleanup decision is to retain the reproducible session smoke as requested acceptance tooling rather than a throwaway script. Logs are below 2 MB each, so none required trimming.

## Critical acceptance evidence (TEST-009/011/013/016/018)

The Editor tests and the IL2CPP probe contain actual conditions on Entities-written state, not only logging. Source locations: `WorldHostTests`, `TemporalDriverTests`, `GuardedDispatchTests`, `PlayerLoopIntegrationTests`, and `ProbeWorldDispatch`.

| Acceptance | Editor evidence | IL2CPP observation |
| --- | --- | --- |
| Worlds advance independently | `TwoWorldsAdvanceIndependently` | Different sessions, A=2 steps/accepts, B=1 step/accept; idle B still receives ingress/output |
| Command-driven idle is zero steps | `CommandDrivenIdleExecutesZeroStepsOverManyFrames`, `CommandDrivenHostIgnoresElapsedHostTimeWithoutDemand`, real-frame PlayMode idle test | 60 frames; 0 steps, 0 gameplay dispatch runs, 1 initial image; 60 ingress/output updates |
| No double updates | `NoSystemIsDispatchedTwiceInOneStep`, real-frame command test, reload smoke | 3 steps × 4 entries = 12 dispatches; each stage counter=3; no outstanding jobs |
| Postwrite exception is fail-stop | `ManagedSystemThatWritesThenThrowsStopsTheStepAndFaultsTheWorld`, `AFaultedWorldAcceptsNoFurtherWork` | ApplyFault/Faulted; accept=1, settle=1, fault=1, project=0; counter=111 (writes not rolled back) |
| No failed step/snapshot publication | Same fault test checks outcome, token absence, step, epoch, publication count and missing failed-step image | step=0, epoch=1, publishedImages=1, failedStepImagePublished=False, no returned snapshot |
| Pending work retained through safe teardown | `PendingJobsStayTrackedUntilTeardownCompletesThem`, direct-disposal regression | 2 outstanding/quarantined jobs and retained handles after fault; teardown settles 2, leaves 0 jobs/resources, disposed ECS world, 0 completion failures |
| Fixed-step debt and unmanaged dispatch | `FixedStepHostBoundsCatchUpAndRetainsTheUnspentRemainder`, logical clock test | 4 admitted steps/ticks; 9,600,000 retained ticks after bounded catch-up; World.Time records last executed tick's start |
| Bootstrap and reload ownership | PlayMode bootstrap/reset tests and 20 real cycles | 1 bootstrap, 1 loop route, 1 registered application world, 0 fallbacks |

Raw player results: `../toolchain/probe-world-dispatch.json`, `probe-result.json`, `probe-negative.json`; player logs and build/codegen/environment evidence are beside them.

## Remaining warnings and qualification limits

- No requested gate remains failing, NotRun or Blocked.
- Unity build reports 12 warnings. Logs include CS8604 nullable annotations in `ProbeArguments`/nullable cleanup callers of `ProbeWorldDispatch.ReleaseWorld`, and package Burst BC1371 warnings in Entities forwarding code. These were not suppressed. Editor logs also warn about the fixture unmanaged system creating a query during OnUpdate.
- Unity logs contain transient licensing access-token/cloud-config and headless GTK diagnostics; licensing/compilation/tests/build still completed. They are not claimed as a warning-free run.
- The test inventory is the **GC-005 subset** of the named design suites, not all later-wave cases described in 08. Full snapshot extraction, checkpoint recovery, gameplay assembly compilation, native-crash recovery and other platforms remain outside this task, as in HANDOFF.
- Linux x64 IL2CPP was run; macOS/Windows and cross-platform determinism are NotRun/unqualified. No graphics-surface or performance benchmark claim is made.

## Commits and delivery

Implementation commits: `2e6122d`, `8968f28`, `483526a`; raw execution evidence: `ee753c2`. This report and final generated-file cleanup are committed separately on `gc-005`; delivery uses `git push origin gc-005`. The original implementation-host HANDOFF remains historical (its NotRun statements describe that other host); this report records the executed Linux results.
