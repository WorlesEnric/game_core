# GC-008 BUILD REPORT

## Host and toolchain

- Host: Linux `worlesenric` x86_64, kernel `7.0.0-31-generic`.
- .NET SDK: `8.0.425` via `/home/worlesenric/.dotnet/dotnet`.
- Unity Editor: `6000.0.75f1` via `/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`.
- Probe build target: `StandaloneLinux64`, IL2CPP, x64, High managed stripping.
- Native tools recorded by `tools/unity/build_probe.sh`: GCC 13.3.0, Clang 18.1.3, GNU ld 2.42.

## Commands run

```sh
git fetch origin && git checkout gc-008 && git reset --hard origin/gc-008

DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  ~/.dotnet/dotnet build dotnet/GameCore.sln -c Release

DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  ~/.dotnet/dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-008/trx

/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath /home/worlesenric/wkspace/gc-wt/gc-008/unity/GameCore.Validation \
  -logFile /home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/unity/resolve.log

/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  -batchmode -nographics \
  -projectPath /home/worlesenric/wkspace/gc-wt/gc-008/unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Assembly.Tests \
  -testResults /home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/unity/assembly-editmode.xml \
  -logFile /home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/unity/assembly-editmode.log

FULL=1 UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  DOTNET=/home/worlesenric/.dotnet/dotnet \
  DOTNET_ROOT=/home/worlesenric/.dotnet \
  PATH=/home/worlesenric/.dotnet:$PATH \
  DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  tools/run_gc008_gate.sh

UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  DOTNET=/home/worlesenric/.dotnet/dotnet \
  DOTNET_ROOT=/home/worlesenric/.dotnet \
  PATH=/home/worlesenric/.dotnet:$PATH \
  DOTNET_CLI_TELEMETRY_OPTOUT=1 \
  ARTIFACTS=/home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/probe \
  tools/unity/build_probe.sh

ARTIFACTS=/home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/probe \
  tools/unity/run_probe.sh && \
ARTIFACTS=/home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/probe \
  tools/unity/run_world_probe.sh && \
ARTIFACTS=/home/worlesenric/wkspace/gc-wt/gc-008/artifacts/gc-008/probe \
  tools/unity/run_w1_gate_probe.sh
```

Intermediate failing compile/test runs were also run while fixing defects; retained evidence under `artifacts/gc-008/` is the final passing evidence.

## Final build and test results

### .NET

- `dotnet build dotnet/GameCore.sln -c Release`: Pass, 0 warnings, 0 errors. Evidence: `artifacts/gc-008/dotnet-build.log`.
- `dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-008/trx`: Pass. Evidence: `artifacts/gc-008/dotnet-test.log` and TRX files.

| Suite | Passed | Failed | Skipped/NotRun |
|---|---:|---:|---:|
| GameCore.Content.Compiler.Tests | 40 | 0 | 0 |
| GameCore.ProtocolFixtures.Tests | 10 | 0 | 0 |
| GameCore.ProtocolFixtures.Production.Tests | 10 | 0 | 0 |
| GameCore.Planning.Tests | 44 | 0 | 0 |
| GameCore.Execution.Tests | 28 | 0 | 0 |
| GameCore.Composition.Tests | 99 | 0 | 0 |
| GameCore.Contracts.Tests | 45 | 0 | 0 |
| GameCore.ReferenceSeams.Tests | 21 | 0 | 0 |
| **Total** | **297** | **0** | **0** |

### Unity Editor tests

Evidence: `artifacts/gc-008/unity/`.

| Suite | Platform | Passed | Failed | Skipped/NotRun |
|---|---|---:|---:|---:|
| GameCore.Unity.Assembly.Tests | EditMode | 10 | 0 | 0 |
| GameCore.Planning.Tests | EditMode | 44 | 0 | 0 |
| Full EditMode | EditMode | 182 | 0 | 0 |
| Full PlayMode | PlayMode | 6 | 0 | 0 |

### Probe/player scripts

Evidence: `artifacts/gc-008/probe/`.

| Script | Result |
|---|---|
| `tools/unity/build_probe.sh` | Pass; built `unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64` |
| `tools/unity/run_probe.sh` positive | Pass, exit 0, result `Pass` |
| `tools/unity/run_probe.sh` negative | Pass, exit 3, result `ExpectedNegative` |
| `tools/unity/run_world_probe.sh` | Pass, result `Pass` |
| `tools/unity/run_w1_gate_probe.sh` | Pass, result `Pass` |

## Fixes made

1. `Packages/com.gamecore.planning/Tests/Plans/AssemblyPlannerTests.cs`
   - Fixed compile-time references from `PlansFixture.CardRecipe` to `PlansFixtureKeys.CardRecipe`.
   - Fixed stale-revision test setup so the world snapshot stays at the fixture revision/epoch while the proposal claims a future revision. The old helper mirrored the proposal into the snapshot, making stale rejection unreachable.
   - Fixed hash-order test setup to compare identical semantic inputs in different order instead of one mount versus two mounts.

2. `Packages/com.gamecore.planning/Runtime/Plans/MigrationScratch.cs`
   - Reordered migration validation so handler lookup and version checks happen before scratch reservation.
   - Released the just-taken reservation if a pure migration handler refuses.
   - Reason: refused or mismatched migrations must stage nothing and must not leave implicit zero state (P-029/P-032).

3. `Packages/com.gamecore.planning/Runtime/Plans/PlanStateMachine.cs`
   - `TryAdvance` now returns the transition's diagnostic code on successful transitions.
   - Reason: `PublishedWithCleanupErrors` records `ResourceUnavailable` in state; the `out` code now reports the same value instead of `None`.

4. `Packages/com.gamecore.composition/Tests/LaneSeedTests.cs`
   - Registered a manifest in the lane seed tests before mounting.
   - Reason: the composition host correctly rejected the mount as `MissingDependency`; the test fixture omitted the catalog registration used by sibling composition tests. Assertions were not weakened.

5. `Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyPublisher.cs`
   - Restored `AssemblySpawnRequest.Target` and `Operation` fields used by spawn publication.
   - Counted stale already-rejected prepared plans in `StalePlanCount`.
   - Moved postwrite fault injection to immediately after the first live write and report the live write count in the fault report.
   - Copied provider provenance into spawned targets' live `CapabilityBinding` rows.

6. `Packages/com.gamecore.unity.runtime/Tests/Assembly/AssemblyTestFixture.cs` and `AssemblyPublisherTests.cs`
   - Added `GameCore.Planning.CompositionProposal` aliases to resolve ambiguity with the contracts type.
   - Reworked prewrite migration retry to reuse the still-pending composition publication rather than adopting a new epoch after a rejected prewrite attempt.

7. `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/W1GateScenario.cs`
   - Preserved first successful publication facts for the `TwoOwnedWorldsAreCreatedAndTheSecondStaysIdle` assertion; the second publication still checks revision/epoch 3 locally without overwriting the facts for the first-publication test.

8. `unity/GameCore.Validation/Packages/packages-lock.json` and Unity `.meta` files
   - Regenerated by Unity package resolve/build and committed as evidence-required generated files.

## Acceptance mapping

- TEST-002: target generations/stale handles pass in `TargetSlotLedgerTests` and Unity despawn handle test; full .NET and Unity EditMode passed.
- TEST-009: multi-target prepared mount and concurrent old-or-new observer pass in `GameCore.Unity.Assembly.Tests`.
- TEST-010: migrated state and preserved old state on prewrite failure pass in planning and Unity publication tests.
- TEST-016: prewrite migration failure and postwrite first-live-write fault pass in Unity publication tests; postwrite fault does not publish/resume.
- TEST-020: runtime spawn recipe path passes; future spawned target appears fully assembled and stale recipe revision is rejected.
- P-006 equality: full W1 EditMode and W1 player probe pass after preserving lane/world epoch equality checks.

## Remaining failures / blocked work

None observed in the commands above. No tests were skipped, ignored, or deleted to get green.
