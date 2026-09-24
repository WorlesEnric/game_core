# GC-005 HANDOFF — owned Unity worlds and guarded execution dispatch (Wave 1)

Branch: `gc-005` (worktree `/Users/yangcao/wkspace/gc-wt/gc-005`).

**Status of every executable check below: NotRun (pending orchestrator build host).** This machine has no
Unity, no .NET SDK and no Mono, so nothing in this change set has been compiled, imported or executed. No Unity
meta files were generated here either (see "Build-host actions").

## 1. Summary

Delivered the Wave 1 execution host: one owned `Unity.Entities.World` per protocol world, one update path, and
an enforceable failure boundary.

1. **`Packages/com.gamecore.unity.runtime`** (assembly `GameCore.Unity.Runtime`, plus the fixture assembly
   `GameCore.Unity.Fixtures`):
   - `WorldHost.cs` — `UnityWorldHost` (per-world ownership: lifecycle, step/epoch authority, demand, clock,
     resource ledger, initial assembly publication, pause/resume, stop, disposal) and `UnityWorldRegistry`
     (live-host index; a repeated session returns the same world, a second world with the same session is never
     created).
   - `Execution/GuardedDispatch.cs` — `GuardedSystemGroup` (a `ComponentSystemGroup` subclass whose
     `OnCreate` sets `EnableSystemSorting = false` and whose `OnUpdate` iterates an explicit ordered dispatch
     table and **never** calls `base.OnUpdate()`), the concrete `GameCoreIngressGroup` / `GameCoreStepGroup` /
     `GameCoreOutputGroup` boundaries, the per-stage `NativeFenceTable`, `RetainedJobHandles` and the
     `IGuardedDispatchSink` fault/job hooks. Managed `SystemBase.Update()`, unmanaged
     `SystemHandle.Update(World.Unmanaged)` and nested infrastructure groups are all dispatched through the
     same guarded loop; the first exception latches the world fault, retains the step's pending job behind
     quarantine and stops the remaining systems of that step.
   - `Execution/UnityExecutionDriver.cs` — `IExecutionDriver`: step admission, ordered dispatch, commit-time
     drain validation, step-fence completion, non-throwing `LogicalStepId` advance plus image publication, and
     the fault latch.
   - `Execution/*` + `Runtime/Pure/**` — `ITemporalAccumulator` (fixed-step debt, command-driven demand, pause
     that adds no debt), `GuardedDispatchPlan`, `WorldResourceLedger` (reverse-dependency retirement, quarantine,
     job tracking), the bounded `StepPublicationStore` + `ObservationHub`, `StepFingerprint`, `IdSequence` and
     the main-thread guard. The `Pure` subtree is engine-free and is compiled both into the Unity assembly and
     by a plain-dotnet project.
   - `Fixtures/Runtime/*` — the tiny hand-written "generated-style" registration table (`ManagedSystemRegistration<T>`,
     `UnmanagedSystemRegistration<T>`, `InfrastructureGroupRegistration<T>`), the fixture stages (including a
     managed stage that writes authoritative state, schedules a real Burst job and then throws, and an unmanaged
     fixed-step stage) and the seeded world state.
2. **`Packages/com.gamecore.unity.adapters`** (assembly `GameCore.Unity.Adapters`):
   - the single application `ICustomBootstrap` (`GameCoreApplicationBootstrap`),
   - an idempotent, recursive PlayerLoop installer that inserts exactly one marker node before
     `Update.ScriptRunBehaviourUpdate` and never uses `ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop`,
   - `GameCoreApplicationPump` (one frame, every registered world, each with its own clock source and
     reentrancy guard; ingress and presentation keep running on paused/idle frames),
   - `GameCoreApplicationReset` with `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]`, plus the
     Editor-only play-mode hook.
3. **Unity tests**: EditMode world/dispatch/temporal suites in the runtime package, PlayMode PlayerLoop/reset/idle
   suites in the adapters package, added to `unity/GameCore.Validation` through `testables`.
4. **Standalone probe path**: `-probeWorldDispatch` mode in the existing qualification player
   (`ProbeWorldDispatch.cs`, `ProbeComposition.cs`) with `tools/unity/run_world_probe.sh`; the GC-001 probes and
   their positive/negative modes are unchanged.
5. **Frozen-surface usability in Unity**: `tests/GameCore.ReferenceSeams/package.json` +
   `GameCore.Contracts.asmdef` (assembly name `GameCore.Contracts`, `noEngineReferences: true`), referenced from
   the validation manifest as `file:../../../tests/GameCore.ReferenceSeams`.

## 2. Files created

Runtime package (`Packages/com.gamecore.unity.runtime`):

- `package.json`
- `Runtime/GameCore.Unity.Runtime.asmdef`
- `Runtime/WorldHost.cs` — `UnityWorldHost`, `UnityWorldRegistry`, `WorldPumpResult`, `IWorldExecutionContext`
- `Runtime/UnityWorldRegistration.cs` — registration, stage descriptors, dispatch catalog, typed registrations
- `Runtime/Execution/GuardedDispatch.cs` — guarded groups, `NativeFenceTable`, `RetainedJobHandles`, sink contract
- `Runtime/Execution/UnityExecutionDriver.cs`
- `Runtime/Pure/Execution/TemporalAccumulator.cs`
- `Runtime/Pure/Execution/GuardedDispatchPlan.cs`
- `Runtime/Pure/Execution/WorldResourceLedger.cs`
- `Runtime/Pure/Execution/PublicationBoundary.cs` — `PublishedStepImage`, `StepFingerprint`,
  `StepPublicationStore`, `ObservationHub`, `GameCoreThreading`
- `Fixtures/Runtime/GameCore.Unity.Fixtures.asmdef`
- `Fixtures/Runtime/FixtureComponents.cs`, `FixtureKeys.cs`, `FixtureSystems.cs`, `FixtureRegistration.cs`
- `Tests/Runtime/GameCore.Unity.Runtime.Tests.asmdef`
- `Tests/Runtime/WorldHostTests.cs`, `GuardedDispatchTests.cs`, `TemporalDriverTests.cs`

Adapters package (`Packages/com.gamecore.unity.adapters`):

- `package.json`
- `Runtime/GameCore.Unity.Adapters.asmdef`
- `Runtime/PlayerLoop/GameCorePlayerLoopInstaller.cs` — installer + `GameCorePumpLoop` marker
- `Runtime/PlayerLoop/GameCoreApplicationPump.cs`
- `Runtime/PlayerLoop/GameCoreApplicationComposition.cs` — composition root + session id reservation
- `Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs`
- `Runtime/PlayerLoop/GameCoreApplicationReset.cs`
- `Tests/Runtime/GameCore.Unity.Adapters.Tests.asmdef`
- `Tests/Runtime/PlayerLoopIntegrationTests.cs`

Plain dotnet (Unity-free execution core):

- `dotnet/src/GameCore.Execution/GameCore.Execution.csproj`
- `dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj`
- `dotnet/tests/GameCore.Execution.Tests/TemporalAccumulatorTests.cs`
- `dotnet/tests/GameCore.Execution.Tests/GuardedDispatchPlanTests.cs`
- `dotnet/tests/GameCore.Execution.Tests/WorldResourceLedgerTests.cs`
- `dotnet/tests/GameCore.Execution.Tests/PublicationBoundaryTests.cs`

Reference-seam Unity packaging:

- `tests/GameCore.ReferenceSeams/package.json`
- `tests/GameCore.ReferenceSeams/GameCore.Contracts.asmdef`

Probe and tooling:

- `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeWorldDispatch.cs` (probes + `ProbeComposition`)
- `tools/unity/run_world_probe.sh`

Evidence:

- `artifacts/gc-005/HANDOFF.md` (this file)

## 3. Files modified (shared; each change is minimal and explained)

| File | Change | Why |
|---|---|---|
| `unity/GameCore.Validation/Packages/manifest.json` | added three `file:` package entries and a `testables` array | the validation project must see the reference seam, the runtime and the adapters, and must run the package test assemblies (W1 instruction) |
| `unity/GameCore.Validation/Assets/link.xml` | added a `GameCore.Unity.Fixtures` assembly preserve and six `GameCore.Unity.Adapters` type roots | the pump is reached only through a native-invoked `PlayerLoopSystem.updateDelegate`, and the fixture stages only through keys resolved at runtime; High stripping must not remove them |
| `.../Runtime/GameCore.Validation.ProbeHost.asmdef` | added four package references | the probe assembly now uses the runtime/fixture/adapters assemblies and the frozen contract seam |
| `.../Runtime/ProbeArguments.cs` | added `-probeWorldDispatch` | new standalone probe mode; no existing argument changed |
| `.../Runtime/ProbeReport.cs` | task id is a parameter defaulting to `GC-001` | one report shape, GC-005 evidence labelled `GC-005`; GC-001 output is byte-identical |
| `.../Runtime/ProbeRunner.cs` | added the `WorldDispatch` branch | runs `ProbeWorldDispatch.Run` and keeps the GC-001 positive/negative paths untouched |
| `dotnet/GameCore.sln` | added two projects (`GameCore.Execution`, `GameCore.Execution.Tests`) | plain-dotnet build/test of the engine-free execution core |
| `dotnet/README.md` | added the two rows and one paragraph | the project table is the documented index of `dotnet/` |
| `tests/GameCore.ReferenceSeams/README.md` | added a "Unity packaging" section | records why the W1 seam is also a Unity package and that it adds no API |

No file under `tests/GameCore.ReferenceSeams/*.cs` or `api/` was touched.

## 4. Exact commands for the Linux build host

**NotRun here.** Run from the repository root.

### 4.1 Plain dotnet (fast, no Unity)

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx
```

Expected: `GameCore.Execution` and `GameCore.Execution.Tests` build, and the four new pure fixtures
(`TemporalAccumulatorTests`, `GuardedDispatchPlanTests`, `WorldResourceLedgerTests`, `PublicationBoundaryTests`)
pass alongside the W0 suites.

### 4.2 Unity package resolve (regenerates the lock)

```sh
UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
"$UNITY" -batchmode -nographics -quit \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -logFile "$PWD/artifacts/toolchain/resolve-gc005.log"
```

Then commit the regenerated `unity/GameCore.Validation/Packages/packages-lock.json` and any `.meta` files Unity
created for the new `Assets/GameCore.Validation/Runtime/ProbeWorldDispatch.cs` (see "Build-host actions").

### 4.3 EditMode tests (runtime package)

```sh
"$UNITY" -batchmode -nographics \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -runTests -testPlatform EditMode \
  -testFilter GameCore.Unity.Runtime.Tests \
  -testResults "$PWD/artifacts/gc-005/editmode-results.xml" \
  -logFile "$PWD/artifacts/gc-005/editmode.log"
```

Do not add `-quit` to a test-run command (04 section 10).

### 4.4 PlayMode tests (adapters package)

```sh
"$UNITY" -batchmode -nographics \
  -projectPath "$PWD/unity/GameCore.Validation" \
  -runTests -testPlatform PlayMode \
  -testFilter GameCore.Unity.Adapters.Tests \
  -testResults "$PWD/artifacts/gc-005/playmode-results.xml" \
  -logFile "$PWD/artifacts/gc-005/playmode.log"
```

Repeat with Play Mode enter/exit and with domain reload disabled for the reset smoke case (TEST-018); the reset
path itself is exercised by `PlayerLoopIntegrationTests.ResetForNewSessionRemovesRoutesAndDisposesSurvivingHosts`,
which calls exactly the method the `RuntimeInitializeOnLoadMethod` attribute invokes.

### 4.5 Standalone IL2CPP player probe

```sh
tools/unity/build_probe.sh          # GC-001 build entry point; now also compiles the GC-005 assemblies
tools/unity/run_probe.sh            # GC-001 probes, unchanged: 7 Pass / 0 Fail, exit 0; negative exit 3
tools/unity/run_world_probe.sh      # GC-005 world-dispatch probe, exit 0
```

`run_world_probe.sh` validates strict JSON, requires `"task": "GC-005"`, `"mode": "WorldDispatch"`,
`"result": "Pass"`, no `"status": "Fail"`, and the presence of all seven expected probe steps.

### 4.6 Documentation gate

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

## 5. Requirement and test coverage mapping

| Requirement (00) | Where it is implemented | Executable evidence |
|---|---|---|
| P-002 participants/authority | `UnityWorldHost` is the only writer of lifecycle, epoch, step, demand, clock and publication; the driver reaches it through the internal `IWorldExecutionContext` | `WorldHostTests.CreationPublishesEpochOneAndStepZero`, `RepeatedCreationOfTheSameSessionReturnsTheSameWorld`, `WorldHostTests.Stop...` |
| P-005 runtime handles | world session is the handle scope; foreign/stale world requests are refused (`StaleHandle`); counter overflow refuses instead of wrapping (`TryIncrement`, ledger/time-debt overflow) | `GuardedDispatchTests.DispatchThroughTheSeamRejectsAStaleEpoch`, `dotnet` `IdSequenceIsDeterministicAndNeverWraps`, `FixedStepRejectsABackwardsHostClock` |
| P-031 apply failure / fail-stop | `GuardedSystemGroup.DispatchRun` catches the first system exception, records and quarantines the pending job, latches the fault and stops the step; `UnityWorldHost.EnterFaulted` publishes nothing and refuses all further work | `GuardedDispatchTests.ManagedSystemThatWritesThenThrowsStopsTheStepAndFaultsTheWorld`, `AFaultedWorldAcceptsNoFurtherWork`, `PendingJobsStayTrackedUntilTeardownCompletesThem`, probe `world-guarded-fail-stop` (+ `-teardown`) |
| P-035 world lifecycle | `Created → Running ↔ Paused → Stopping → Disposed`, `Faulted` from any nonterminal state; creation is not exposed before the initial assembly publication; repeated create returns the same world | `WorldHostTests.PauseChangesHostStatusOnlyAndAddsNoDebt`, `InvalidLifecycleRequestsAreRejectedWithoutMutation`, `StopClosesIngressSettlesJobsAndDisposesStorage` |
| P-036 temporal models | `FixedStepAccumulator` (exact debt, bounded catch-up, no lengthened step) and `CommandDrivenAccumulator` (demand only, no implicit simulation seconds); host clock origin captured at the first routable pump | `dotnet` `TemporalAccumulatorTests` (7 cases), `TemporalDriverTests` (4 cases), probe `world-command-driven-idle-zero-steps`, `world-fixed-step-debt-and-clock` |
| P-041 concurrency/structural work | per-stage `NativeFenceTable`: incoming stage edges combine with the system's own dependency, the output replaces the stage fence, a skipped/disabled system forwards the incoming fence and produces no new work; `RetainedJobHandles` keeps unfinished jobs until teardown | `GuardedDispatchTests.ADisabledSystemForwardsItsFenceAndProducesNoNewWork`, `PendingJobsStayTrackedUntilTeardownCompletesThem`, `TemporalDriverTests` fence assertions |
| P-044 commit | dispatch → drain validation → step-fence completion → non-throwing step advance and image publication together; a failure after writes faults and never retries | `WorldHostTests.NoSystemIsDispatchedTwiceInOneStep`, `GuardedDispatchTests.UnconsumedDeclaredBufferFaultsTheWorldInsteadOfPublishingPartialSuccess`, probe `world-no-system-double-update` |
| P-047 in-flight lifetime | a failed step's job is retained (`RetainJobByQuarantine`) while its buffers stay valid; `Stop` settles every retained handle before storage is released | `GuardedDispatchTests.PendingJobsStayTrackedUntilTeardownCompletesThem`, probe teardown step |
| P-048 teardown | resource retirement runs dependents-first with every independent cleanup attempted, a failed releaser is quarantined, a blocked stop reports `TeardownBlocked` with retained ids, and disposal happens only after jobs settle | `WorldResourceLedgerTests` (7 cases), `WorldHostTests.Stop...`, `PendingJobs...` |
| P-058 V1 profile | Entities 1.4.6 APIs only, verified against the package sources in this session (`ComponentSystemGroup.EnableSystemSorting`, `UpdateAllSystems` exception swallowing, `SystemState.Dependency`, `SystemHandle.Update(World.Unmanaged)`, `World.Time/SetTime`, `PlayerLoop.GetCurrentPlayerLoop/SetPlayerLoop`); the probe runs the same code in the IL2CPP player | probe `world-*` steps in the built player; EditMode/PlayMode suites in the Editor |

Suites (08) the change set contributes to: **TEST-009** (step/snapshot publication boundary), **TEST-011**
(temporal models, idle worlds, pause), **TEST-013** (direct writes, ordered dispatch, drain), **TEST-016**
(named fault injection at the dispatch boundary), **TEST-018** (Unity worlds, bootstrap, PlayMode, guarded
dispatch fail-stop).

## 6. Decisions, assumptions and doc ambiguities

1. **Engine-free execution core as a second compilation.** `Runtime/Pure/**` (namespace `GameCore.Execution`)
   holds the pure logic (temporal accumulator, dispatch plan, resource ledger, publication boundary) and is
   compiled twice: into `GameCore.Unity.Runtime` for Unity, and by `dotnet/src/GameCore.Execution` for plain
   dotnet tests. 00/01 name no such assembly; the alternative — testing temporal/ledger policy only inside Unity
   EditMode tests — would have left the dotnet half of the W1 gate with nothing to run. No Unity type appears in
   those files (checked mechanically).
2. **Clock origin.** A world's simulation clock starts at its first routable pump (`HostTimeOrigin`), so host
   time before the world existed is not debt. Without this, a player world created 12 s into a session would
   start with 12 s of instant catch-up debt. `PumpFrame` still receives absolute host ticks from the pump; the
   host converts them to elapsed ticks.
3. **`StepAdvanceRequest.RetainedDebt` is an expectation, not a second authority.** The accumulator owns
   retained time; a request carrying a different debt is refused as `StalePlan` (P-036 keeps one debt record).
4. **Drain validation compares producer keys with the consumer stage.** `BufferBinding` carries no lifetime, so
   the rule is: if any declared producer ran, the consumer stage must have run in the same step; otherwise the
   commit fails with `MissingDependency`. `Step`-lifetime semantics are assumed for every declared binding
   (GC-006+ owns buffer lifetimes).
5. **Job records and handles.** A dispatch entry records a ledger job only when it replaced or extended its
   dependency handle (i.e. it produced new work); the step fence completes those records at commit. A failing
   step's handles are retained and settled at teardown. Job dependency registration relies on declared
   component access (the fixture job writes through a read-write `ComponentLookup<T>`), exactly as 04 section 4
   requires; a system that schedules against an undeclared external container would not be covered by Unity's
   safety manager alone.
6. **`StepFingerprint` scope is explicit, not gameplay state.** `StepCommitEvent.StateHash` hashes the world
   incarnation, epoch, committed step and dispatched-entry count with SHA-256 over 40 canonical bytes. Extracting
   real state into the image is GC-008's live-publication work; the constant scope string is recorded beside the
   value so it cannot be mistaken for gameplay-state equality. Per-step SHA-256 is a known cost for TEST-023.
7. **Publication store is deliberately minimal.** `StepPublicationStore` retains a bounded ring of committed
   images, returns `Acquired`/`Expired`/`Backpressure`/`ForeignWorld` as values, and never exposes writable ECS
   data. Full retention policy, event cursors and outbox semantics are GC-008/GC-014 scope.
8. **`GameCore.Engine`-free namespace vs assembly name.** The pure files sit in the Unity package directory but
   declare namespace `GameCore.Execution`; the dotnet project's assembly name is `GameCore.Execution`, while
   Unity compiles the same sources into `GameCore.Unity.Runtime`. Both names are documented in `dotnet/README.md`.
9. **Fixture assembly inside the runtime package.** The brief requires the Unity tests *and* the IL2CPP probe to
   use the same fixture systems, so the fixture lives in a runtime assembly
   (`Packages/com.gamecore.unity.runtime/Fixtures/Runtime/`, assembly `GameCore.Unity.Fixtures`) rather than in a
   `Tests/` asmdef (test assemblies are not linked into a player build). It is a qualification fixture, not a
   supported gameplay assembly.
10. **`allowUnsafeCode: true` on every assembly that contains systems.** The Entities source generator/ILPP emits
    `unsafe` registration code for system assemblies; Unity's own test asmdefs set the same flag.
11. **Application composition root.** The bootstrap reads `GameCoreApplicationComposition.RootFactory`, which the
    application assigns from its own `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` method (the probe and
    PlayMode tests do this in `ProbeComposition`). If no factory is registered — or if world creation fails — the
    bootstrap creates an infrastructure-only world instead of returning `false`, because returning `false` would
    let Unity create a default world containing every auto-created system, which is exactly the second update
    path 04 section 3 forbids. Both fallbacks are counted (`FallbackCount`) and reported, never silent.
12. **The application root factory is not cleared by the reset path.** It is application configuration, not
    session state; clearing it would break a session whose own `SubsystemRegistration` method ran before ours.
13. **Presentation/ingress dispatch per host frame.** Ingress and output groups are dispatched on every routable
    pump frame (including paused and idle frames), which is what 04 section 3 requires; ordering within a frame
    is ingress → admitted steps → output.
14. **`MaxStepsPerPump` for a command-driven world** defaults to 1 (one command or wake per logical step, P-037)
    because `FixedStepSettings` is a fixed-step-only configuration.
15. **`ITemporalAccumulator.MaxStepsPerPump`** was added to the engine-free interface so the driver can bound a
    caller-supplied `RequestedSteps` without knowing which accumulator it holds.

## 7. Known gaps

- Nothing has been compiled or executed on this host. The most likely first-failure candidates are
  (a) a Unity `.meta`/package resolve issue for the two new packages, (b) an asmdef reference name mismatch, and
  (c) a C# 9 syntax or nullability slip in the two largest files (`WorldHost.cs`, `GuardedDispatch.cs`). All of
  them are contained and mechanical.
- `InfrastructureGroupRegistration<T>` and the `InfrastructureGroup` dispatch kind are implemented and exercised
  (`GuardedDispatchTests.AnInfrastructureGroupEntryDispatchesItsOwnBoundTable`) but no fixture plan uses a nested
  group by default; the frame-level arrangement of adapter groups remains the host's.
- No native-crash recovery, no physics loop and no native job cancellation: explicit non-goals of GC-005.
- Full snapshot extraction, event cursors, retention policy and outbox semantics are GC-008+ scope; the W1 store
  is the step boundary only.
- Only the Linux x86_64 IL2CPP player is targeted. macOS ARM64 remains unqualified.
- Per-step SHA-256 for the state hash and per-step `World.SetTime` (which touches the time singleton) are
  deliberate V1 simplifications to measure in TEST-023 rather than optimise blind.
- The per-frame pump uses a reused host scratch list; no allocation is added per frame per world by the
  host itself (the publication boundary does allocate the 40-byte fingerprint record + SHA-256 per step).

## 8. Proposed seam changes

**None.** No file under `tests/GameCore.ReferenceSeams` other than `README.md` was modified, and no frozen type,
member or signature was changed or requested. Everything GC-005 needed (`IWorldHost`, `IExecutionDriver`,
`OrderedDispatchTable`, `StepCommitEvent`, `WorldResourceLedgerSnapshot`, `IWorldLifecycleObserver`,
`IObservationReader`, `ISnapshotLease`) was implemented as-is.

Two additions live entirely on my side of the seam and are visible only to my packages: the internal
`IWorldExecutionContext` (host authority surface for the driver) and `ISystemDispatchCatalog` /
`SystemDispatchTarget` (key-to-instance resolution for the generated registry). The frozen seam's
`SystemDispatchKind.InfrastructureGroup` is honoured: an entry of that kind invokes the named group's own
guarded table.

## 9. Build-host actions

1. Open/resolve `unity/GameCore.Validation` once in the pinned Editor, then commit the regenerated
   `Packages/packages-lock.json` and every `.meta` file Unity creates for the three new `file:` packages and
   `Assets/GameCore.Validation/Runtime/ProbeWorldDispatch.cs`. No `.meta` file could be produced on this host
   (no Unity), and `git status` therefore shows `Packages/` as a wholly untracked directory.
2. Run the dotnet and Unity commands in section 4 in that order; archive the raw logs under
   `artifacts/gc-005/` (the runner script already writes its result and player log under `artifacts/toolchain/`).
3. If the Editor reports a package or asmdef error, the reference names to check first are
   `GameCore.Contracts` (provided by `com.gamecore.reference-seams` during W1), `GameCore.Unity.Runtime`,
   `GameCore.Unity.Fixtures` and `GameCore.Unity.Adapters`.
