# GC-007 Linux build and test report

Host: Ubuntu 24.04, Linux 7.0.0-31-generic x86_64. .NET SDK 8.0.425 (runtime 8.0.31; MSBuild 17.11.48); Unity Editor 6000.0.75f1; Unity Entities 1.4.6, Collections 2.6.6, Burst 1.8.28 from the resolved project manifest/lock. Worktree `gc-007`, synchronized with `git fetch origin && git checkout gc-007 && git reset --hard origin/gc-007` before changes. Dotnet builds retain C# 9 and netstandard2.1; pure assemblies have no UnityEngine dependency.

## Commands and final results

From repository root, with `DOTNET_ROOT=$HOME/.dotnet`, `$HOME/.dotnet` prepended to `PATH`, `DOTNET_CLI_TELEMETRY_OPTOUT=1`, and `UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`:

| Command | Actual outcome |
|---|---|
| `dotnet build dotnet/GameCore.sln -c Release` | Pass, after compile repairs, 0 warnings/errors. |
| `dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-007/trx-final` | Pass: 322/322, 0 failed, 0 skipped. TRX files in `trx-final/`. |
| `$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -quit -logFile artifacts/gc-007/unity-resolve.log` | Pass after Unity compilation repairs; generated `packages-lock.json` and planning `.meta` files committed. |
| `$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode -testResults $PWD/artifacts/gc-007/unity/all-editmode.xml -logFile $PWD/artifacts/gc-007/unity/all-editmode.log` | Pass: 177/177, 0 failed/skipped. |
| `$UNITY -batchmode -nographics -projectPath unity/GameCore.Validation -runTests -testPlatform PlayMode -testResults $PWD/artifacts/gc-007/unity/all-playmode.xml -logFile $PWD/artifacts/gc-007/unity/all-playmode.log` | Pass: 6/6, 0 failed/skipped. |
| `UNITY=$UNITY ARTIFACTS=$PWD/artifacts/gc-007/probe bash tools/unity/build_probe.sh` | Pass: StandaloneLinux64 IL2CPP High-stripping player built; codegen/build/environment evidence under `probe/`. |
| `ARTIFACTS=$PWD/artifacts/gc-007/probe bash tools/unity/run_probe.sh` | Initial run failed: positive mode wrote Pass JSON then process segfaulted with exit 139; negative mode exited expected 3. Positive retry via `bash tools/unity/run_probe.sh positive` exited 0 and reported Pass. First crash is not erased by the retry. |
| `ARTIFACTS=$PWD/artifacts/gc-007/probe bash tools/unity/run_world_probe.sh` | Pass, exit 0, structured GC-005 result Pass. |
| `ARTIFACTS=$PWD/artifacts/gc-007/probe bash tools/unity/run_w1_gate_probe.sh` | Pass, exit 0, structured W1-GATE result Pass. |

.NET per assembly: Contracts 45/45, Composition 93/93, Execution 58/58, Planning 45/45, Content.Compiler 40/40, ReferenceSeams 21/21, ProtocolFixtures 10/10, ProtocolFixtures.Production 10/10. Unity EditMode per assembly: Composition 93/93, Planning 45/45, Messages 10/10, Runtime 21/21, W1Gate 8/8. Unity PlayMode: Adapters 6/6. All are Pass; no Fail, NotRun, or Blocked suites in the final runs. Whole-suite runs include the specifically named GC-007 tests. Unity test runs intentionally omit `-quit`.

## Defects fixed

- `ComponentOwnershipMap`: replace nonexistent `DeclaresField` call with an explicit layout-field lookup; prevents undeclared field claims. `BoundedMessageBuffer`: return empty non-null carry collections. `StepMessageSchedule`: expose actual pending deferred operation count referenced by tests. Test typo `lostyDescriptor` corrected.
- `SlotPolicyValidatorTests`: fixtures choosing `PreserveDormant` now declare dormant retention. This changes no expected values: `SlotAuthorityOptions.Durable()` explicitly disallows dormant retention, as the validator correctly enforced; the cases now exercise the intended migration/reset/configuration path with a valid base declaration. Ledger sequence-violation counter increments on rejected expired/high-water identity. Pending-order test now uses three different issuers, retaining shuffled sequence values without violating per-issuer monotonicity. No tests were skipped, deleted, or weakened.
- Unity compilation: import the pure message namespace in native lanes and host, declare the registration's buffer-ID set and host resource ID, supply a byte-array payload for lane append, add the test assembly's Burst reference, correct fixture seed callback return type and descriptor arguments.
- Real-world defects exposed by Unity tests: duplicate command retransmission no longer adds another command-driven demand; owner merge now accumulates rows from every lane rather than clearing the prior lane; native lane validates row capacity before reserving/scheduling payload writes, completes tracked arena writers before main-thread reads, and chains jobs writing the same arena. Native reliable lane drain is validated at commit instead of checking only the unused managed mirror, so the absent consumer faults without publishing. Message tests now include the host's fault detail in assertions.

## Acceptance evidence and limitations

`OwnerAuthorityValidatorTests.TwoUndeclaredWritersOfOneDomainReject` and `...TwoValidatedDisjointPartitionsOfOneOwnerAreLegal` ran in Planning on both .NET and Unity. `WorldMessagePlaneTests.ValidatedPartitionsExecuteWithRealJobsSafely` validates two partitions then checks actual scheduled jobs, completion and decoded payloads in an Entities world. `...OverflowRejectsBeforeMutationAndLeavesStateUnchanged`, `...ACommandThatOverflowsItsIngressLaneIsRefusedBeforeTheStep`, `...AReliableBufferThatCannotBeDrainedFaultsInsteadOfPublishing`, `...AdmissionAcceptanceIsDistinguishableFromGameplayCommitment`, and `...ShuffledProducerOrderCommitsTheSameResult` passed in Unity. The direct owner write occurs in `MessageOwnerSystem` and its applied state is asserted in the route and overflow tests; pure authority separately accepts direct owner access. Shuffled producer test compares committed event sequence, schema and bytes, not just row count. These are real Unity jobs/worlds, not static-only checks.

The GC-007 slice does not implement the full later-wave TEST-009 prepared multi-target plan protocol, TEST-010 actual migration executors, TEST-013 card/narrative domain transactions and bounded inter-stage cycles, or TEST-014 external side-effect idempotency. This report claims the task's mapped early-slice assertions, not those later integration scenarios. The initial positive player SIGSEGV after writing Pass JSON remains unexplained and was not reproducible on immediate retry; the first process exit was a failure, even though later positive, world and gate runs passed. See player logs and result JSON under `probe/`.
