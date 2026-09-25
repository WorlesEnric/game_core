# W2-GATE HANDOFF — Wave 2 integration gate

Branch: `w2-gate` (worktree `/Users/yangcao/wkspace/gc-wt/w2-gate`); it merges `gc-006`, `gc-007`, `gc-008` and `gc-009`.

**Status of every executable check below: `NotRun (pending orchestrator build host)`.** This host has no .NET SDK,
no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or executed here.
The only things that ran are interpreter-level host checks, recorded verbatim in `artifacts/w2-gate/static-checks.log`:
`python3 tools/check_game_core_csharp.py` (196 files, `ok`), `python3 tools/validate_game_core_docs.py --self-test`
and `python3 tools/validate_game_core_docs.py` (passed), `bash -n` over the five shell scripts, a `.meta` GUID
uniqueness scan (371 GUIDs, no duplicates) and the source scans described in §8. None of those is a build or a test
result.

Gate sentence implemented (verbatim, `docs/game-core/09-implementation-guide.md`, Wave 2 — Automatic assembly and
generic execution):

> Use all real W2 outputs together: mount a provider, derive a compatible target, publish its real Entities layout
> and compiled schedule, execute one bounded command, observe one consistent result, spawn a future target, and run
> the player smoke path. Independent seam fixtures do not substitute for this integration.

## 1. Summary

The gate replaces every Wave 2 task's private seam fixture with the real module and runs the whole chain in one real
`Unity.Entities.World`:

| Gate clause | Real module that performs it | Where it is driven from |
|---|---|---|
| mount a provider | `CompositionHost.SubmitEdit` + `Drain` (GC-004) over a generated-style catalog declaration (GC-003) | `W2GateScenario.MountProviderAndPublish` |
| derive a compatible target | `DerivationEngine.Derive` (GC-006) over a snapshot frozen from the *committed* composition, then `DerivedCompositionProposal` | `CompositionDerivationInput`, `DerivedAssemblyPipeline.Derive` |
| publish its real Entities layout and compiled schedule | `AssemblyPlanner` + `AssemblyPublisher` (GC-008) over a descriptor built by GC-007's validator and GC-009's compiler | `OwnershipSchedulePipeline`, `DerivedAssemblyPipeline.Publish` |
| execute one bounded command | `UnityWorldHost.Submit` → `WorldMessagePlane` (GC-007) → `UnityExecutionDriver` over the compiled schedule | `W2GateScenario.ExecuteOneBoundedCommand` |
| observe one consistent result | committed event page (`CommittedEventStore`) compared field by field with live ECS storage | same |
| spawn a future target | `AssemblyPublisher.Spawn` (P-024) | `W2GateScenario.PublishForwardProviderAndSpawn` |
| run the player smoke path | `-probeW2Gate` in the IL2CPP player, same scenario | `ProbeW2Gate`, `tools/unity/run_w2_gate_probe.sh` |

Four things were added:

1. **Integration glue** in `Packages/com.gamecore.unity.runtime/Runtime/Integration/` (six new files, extending the
   W1 glue — `WorldCompositionBridge` is reused, not duplicated): the committed composition as a GC-006 input, the
   live-target index and seeder, the derivation result as a GC-008 proposal, the ownership+schedule pipeline that
   produces GC-008's frozen descriptor from the real validators, the whole-chain driver, and a bounded staged-resource
   gate (no implementation of `IPlanResourceGate` existed in production code).
2. **The gate fixture**: `W2GateKeys`/`W2GateDeclarations`/`W2GateWorld` in `GameCore.Unity.Fixtures` (four declared
   stages, five systems, one declared buffer, three owned slots, two providers, a bounded command lane and a real
   job writing a native container) and `W2GateScenario` — the eleven-observation scenario shared by the Editor and
   the player.
3. **The EditMode half and the player half**: `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/W2Gate/`
   (`GameCore.W2Gate.Tests`, 11 cases) and `-probeW2Gate` (`W2GateScenarioHost`, `ProbeW2Gate`, `ProbeArguments`,
   `ProbeRunner`).
4. **Probe-run robustness**: the flaky positive-probe crash (GC-007's build host saw Pass JSON written, then exit
   139) is addressed twice — every probe now runs `PROBE_RUNS` times (default 5) and fails on any crash
   (`tools/unity/probe_runs.sh`), and two plausible native-shutdown causes were fixed: the application PlayerLoop
   node is detached on `Application.quitting`, and a message lane now completes an outstanding payload-writer job
   before freeing its arena.

## 2. Files created

Runtime package `Packages/com.gamecore.unity.runtime` (each file with a committed `.meta`, deterministic GUIDs):

| File | Contents |
|---|---|
| `Runtime/Integration/DerivationInputSource.cs` | `DerivationInputOutcome`, `DerivationInputReport`, `CompositionDerivationInput`: the committed composition (GC-004) as GC-006's immutable indexed snapshot, with two-manifest capability-contract agreement, plus `CanonicalContractText` |
| `Runtime/Integration/LiveTargetIndex.cs` | `LiveTarget`, `LiveTargetIndex` (stable identity, one owner scope, recipe, descriptor resolved through the recipe catalog), `DerivationInputTargets`: the derivation and planner views of the live targets |
| `Runtime/Integration/LiveTargetSeeder.cs` | `LiveTargetSeeder`: creates a real target entity (identity, stamp at the published assembly, binding buffer, slot buffer), seeds live state slots and copies them for planning (P-029) |
| `Runtime/Integration/DerivationProposalBridge.cs` | `IntegrationSlotValues` (the canonical big-endian int32 slot value of 05 §6), `DerivationProposalOutcome`, `DerivationProposalReport`, `DerivedCompositionProposal`: derivation result → GC-008 `CompositionProposal` |
| `Runtime/Integration/OwnershipSchedulePipeline.cs` | `PipelineDescriptorOutcome`, `PipelineDescriptorReport`, `OwnershipSchedulePipeline`: manifest declarations → GC-007 partition generation, GC-009 compile + adapt, GC-007 ownership and slot-policy validation → GC-008's `OwnershipStageDescriptor` |
| `Runtime/Integration/DerivedAssemblyPipeline.cs` | `DerivedAssemblyOutcome`, `DerivedAssemblyReport`, `DerivedAssemblyPipeline`: freeze → derive → propose → adopt → plan → publish, and the spawn that carries its composition publication |
| `Runtime/Integration/StagedResourceGate.cs` | `StagedResourceLease`, `StagedResourceGate`: a bounded in-memory `IPlanResourceGate` |

Gate fixture (`Packages/com.gamecore.unity.runtime/Fixtures/Runtime/`, assembly `GameCore.Unity.Fixtures`):

| File | Contents |
|---|---|
| `W2GateKeys.cs` | Stable identities (reusing the GC-006 narrative recipes/rule names through `FixtureIds`), the gate's stages/systems/buffers/domains/slots/fields, the components `W2GateTrait`/`W2GateTrail` and the generated `W2GatePayloadReader` |
| `W2GateDeclarations.cs` | `W2GateQuestMigration`, `W2GateSlotMigrations`, `W2GateRecipeApplier`, the two provider manifests (rules, capability contracts, stages, buffer, slots) |
| `W2GateWorld.cs` | `W2GateSettleJob`, `W2GateModule` (compiled schedule, adapter result, native dependency table, root entity), the five systems, `W2GateRecipes`, `W2GateRegistration` (message plane, readers, registration) |
| `W2GateScenario.cs` | `W2GateStep`, `W2GateFacts`, `W2GateScenarioResult`, `W2GateScenario` (11 observations per run) |

Unity qualification project:

- `Assets/GameCore.Validation/Tests/W2Gate/GameCore.W2Gate.Tests.asmdef`
- `Assets/GameCore.Validation/Tests/W2Gate/W2GateIntegrationTests.cs` (11 cases, both catalogs)
- `Assets/GameCore.Validation/Runtime/W2GateScenarioHost.cs`
- `Assets/GameCore.Validation/Runtime/ProbeW2Gate.cs`

Tooling and evidence: `tools/run_w2_gate.sh`, `tools/unity/run_w2_gate_probe.sh`, `tools/unity/probe_runs.sh`,
`artifacts/w2-gate/HANDOFF.md` (this file), `artifacts/w2-gate/static-checks.log`.

## 3. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef` | `+ "GameCore.Derivation"` | **the one deliberate deviation from 04 §2's allowed-references row** — see §7.1. The W2 brief puts the reusable glue in this assembly, and the derivation→plan→publish join cannot be expressed without `DerivationSnapshot`, `DerivationEngine`, `DerivationTarget`, `IDerivationValueSource` |
| `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/GameCore.Unity.Fixtures.asmdef` | `+ GameCore.Derivation`, `GameCore.Derivation.Fixtures`, `GameCore.Planning` | the gate fixture reuses the GC-006 narrative descriptors (`FixtureIds`, `FixtureValueSource`, `NarrativeComposition`) and the planning types |
| `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCorePlayerLoopInstaller.cs` | `+ using UnityEngine`, `InstallQuitHook()`, `IsQuitHookInstalled`, `QuitHookInstallCount`/`QuitHookFireCount`/`QuitRemovalCount`, `OnApplicationQuitting`, counters reset in `ResetCounters()` (the subscription flag deliberately survives) | §6: the PlayerLoop node must not pump a world while the engine tears its storage down. No existing member changed |
| `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs` | `+ GameCorePlayerLoopInstaller.InstallQuitHook();` after `EnsureInstalled()` | one quit hook beside the one pump node |
| `Packages/com.gamecore.unity.runtime/Runtime/Messages/NativeMessageLanes.cs` | `NativeMessageLane.HasOutstandingPayloadWriter`, `PayloadWriterCompletionFailureCount`, writer completion before `Dispose` frees `rows`/`payloadArena`; the same counter and pre-dispose loop on `NativeMessageLanes` | §6: freeing an arena a scheduled producer still targets is a use-after-free. `TrackPayloadWriter`/`CompletePayloadWriter`/`EndStep`/`MergeInto`/`PayloadOf`/`Consume` semantics unchanged |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs` | `-probeW2Gate` | new probe mode; no existing argument changed |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeRunner.cs` | W2 gate branch + task id `W2-GATE` | GC-001/GC-005/W1 paths untouched |
| `tools/unity/run_probe.sh`, `run_world_probe.sh`, `run_w1_gate_probe.sh` | source `probe_runs.sh`, single invocation replaced by `probe_run_n`, required-step loops folded into `probe_require_steps` | §6; every existing assertion, message and exit code kept |
| `tools/check_game_core_csharp.py` | `+ Packages/com.gamecore.planning`, `+ Packages/com.gamecore.unity.runtime` in `TARGETS` (not in `engine_free`) | the host-side balance/forbidden-construct check now covers the W2 modules and this glue (196 files) |
| `dotnet/README.md` | `GameCore.Planning` project rows + the W2 gate command | the two W2-prep projects had no row (GC-009 left this to the integrator, and it lists the gate command) |

## 4. Exact commands for the Linux build host

Everything runs from the repository root. Nothing here has been run.

### 4.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet tools/run_w2_gate.sh
```

It runs `dotnet build dotnet/GameCore.sln -c Release`; `dotnet test dotnet/GameCore.sln -c Release --logger trx
--results-directory artifacts/w2-gate/trx`; a Unity package resolve; the **whole** Unity EditMode suite (every
`testables` package plus `GameCore.W1Gate.Tests` and `GameCore.W2Gate.Tests`); the whole PlayMode suite;
`tools/unity/build_probe.sh` (catalog codegen + StandaloneLinux64 IL2CPP with High stripping); `run_probe.sh both`
(GC-001), `run_world_probe.sh` (GC-005), `run_w1_gate_probe.sh` (W1-GATE) and `run_w2_gate_probe.sh` (W2-GATE), each
repeated `PROBE_RUNS` times (default 5, `PROBE_RUNS=N` to override); and the documentation validator. `UNITY` is
required — the gate is never claimed from the dotnet half alone.

### 4.2 The pieces

```sh
# dotnet half
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w2-gate/trx

# the W2 gate EditMode assembly alone
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.W2Gate.Tests \
  -testResults artifacts/w2-gate/unity/w2gate-editmode.xml -logFile artifacts/w2-gate/unity/w2gate-editmode.log

# what the W1 gate must still pass unchanged on the same revision
UNITY="$UNITY" tools/run_w1_gate.sh

# player build and the probes, five times each
UNITY="$UNITY" tools/unity/build_probe.sh
PROBE_RUNS=5 tools/unity/run_probe.sh both
PROBE_RUNS=5 tools/unity/run_world_probe.sh
PROBE_RUNS=5 tools/unity/run_w1_gate_probe.sh
PROBE_RUNS=5 tools/unity/run_w2_gate_probe.sh
```

Do not add `-quit` to a test-run command (04 §10). The W2 scenario runs twice per probe invocation (generated and
fixture catalogs), so one `-probeW2Gate` process performs two full gate runs.

### 4.3 Host-side checks that did run here

```sh
python3 tools/check_game_core_csharp.py                      # 196 files, ok
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
bash -n tools/run_w2_gate.sh tools/unity/run_w2_gate_probe.sh tools/unity/probe_runs.sh \
  tools/unity/run_probe.sh tools/unity/run_world_probe.sh tools/unity/run_w1_gate_probe.sh
```

### 4.4 What the W2 gate probe asserts

`run_w2_gate_probe.sh` requires `"task": "W2-GATE"`, `"mode": "W2Gate"`, `"result": "Pass"`, no `"status": "Fail"`,
all eleven scenario observations twice (once plain, once `fixture:`-prefixed), the two facts digests, and that the
generated run's facts name the committed generated catalog's fingerprint literal.

## 5. Shared-file reconciliation (task 1 of the brief)

Every shared file that more than one W2 task touched was compared against each task branch (`git diff main...origin/gc-00X`)
and against the merged tree. **No semantic conflict existed; the merges only collided on Unity `.meta` GUIDs of
files two branches both created** (the merge commits record keeping GC-007's skeletons). Concretely:

| Shared file | Branches that touched it | Reconciliation |
|---|---|---|
| `Runtime/WorldHost.cs` | gc-007 (`IWorldExecutionContext.Messages`, plane creation, `ICommandIngress.Submit`, committed-event staging in `TryCommitStep`, plane disposal in `Stop`, `UnityWorldRegistry.TryGetByEntityWorld`), gc-008 (`assemblySlot`, `CurrentEpoch` reading the published view, `IsPumping`, `AttachAssemblySlot`, `TryPublishAssembly`, `PublishedCompositionRevision`, `TryAdoptPublishedComposition`, `EnterFaulted`) | Both kept, in one file: GC-007's members are additive and orthogonal to GC-008's; `TryCommitStep` contains both the plane staging and the epoch-bound token. Verified mechanically that every symbol of both diffs is present (`AttachAssemblySlot` 2, `TryPublishAssembly` 2, `IsPumping` 1, `assemblySlot` 5, `PublishedCompositionRevision` 1, `TryAdoptPublishedComposition` 2, `EnterFaulted` 4, `Messages` 6, `messagePlane` 2, `TryCommitStep` 2, `Stop(` 2, `TryGetByEntityWorld` 1) |
| `Runtime/Execution/GuardedDispatch.cs` | gc-007 only (`GuardedSystemGroup : IStepDispatchFacts`) | single-author; no conflict |
| `Runtime/Execution/UnityExecutionDriver.cs` | gc-007 only (seal input, validate message buffers, `EndStep`) | single-author; the driver also owns `messages?.SealStep`/`ValidateCommit`/`EndStep`, so no glue duplicates them |
| `Runtime/UnityWorldRegistration.cs` | gc-007 only (two trailing optional message parameters) | single-author; `W2GateRegistration` passes both |
| `Runtime/Integration/WorldCompositionBridge.cs` | gc-008 only (publisher parameter, seed equality check, `JoinPublishedComposition`, join counters) | single-author; the W2 gate *reuses* this bridge for the lane/world join and its `SyncStepFromWorld`, and does not duplicate it |
| `Fixtures/Runtime/W1GateScenario.cs` | gc-008 (lane seeds, epoch expectations) | single-author here; the W2 gate adds its own scenario rather than editing the W1 one |
| `Packages/com.gamecore.composition/Runtime/Operations/{CompositionHost,CompositionState}.cs` | gc-008 only (`CompositionLaneSeed`) | single-author |
| `Packages/com.gamecore.composition/package.json` | gc-006 (dependency `file:../com.gamecore.contracts` → `0.1.0`, in its "resolve local Unity packages" commit) — *not* by this gate | left exactly as the merge produced it; see §7.3 |
| `dotnet/GameCore.sln`, `unity/.../Packages/manifest.json` | gc-006 only (derivation projects/testable) | single-author; the merged `manifest.json` already lists all six GameCore packages and five testables |
| `tools/check_game_core_csharp.py` | gc-006 only (derivation target) | extended here by two targets, no removal |
| `.meta` files of files two branches created | gc-006/gc-007/gc-008/gc-009 | the merge kept one branch's GUID per path; a repository-wide scan finds 371 GUIDs with **zero duplicates**, so no two assets share a GUID |

`Packages/com.gamecore.planning/{Runtime,Tests}/**` is touched by gc-007 (`Ownership/`), gc-008 (`Plans/`) and
gc-009 (`Scheduling/`) but in disjoint directories plus the shared `.gitkeep` deletions; the merged tree has all
three directories and no `.gitkeep` left. The three packages' sources compile into one assembly, and
`OwnershipSchedulePipeline` is the first code to consume all three at once.

## 6. The flaky player crash (task 4 of the brief)

**Observed symptom (not reproduced here — no toolchain).** `artifacts/gc-007/BUILD_REPORT.md` line 17: the GC-001
positive probe printed its Pass JSON and then the process segfaulted with exit 139; a retry exited 0. The crash is
therefore *after* `File.WriteAllText(result)` and after `Application.Quit(0)` was requested, i.e. during native
engine teardown.

**Detection (the primary fix).** Every player probe now runs `PROBE_RUNS` times (default 5, `PROBE_RUNS=N` env
override) through `tools/unity/probe_runs.sh`, and the run fails if **any** run is dirty. A run is dirty when its
exit code is ≥ 128 (139 SIGSEGV, 134 SIGABRT, 135 SIGBUS, 136 SIGFPE — reported as `exit <rc> (signal: crash)`), the
result file is missing or not valid JSON, the expected result is absent, the exit code differs from the expected one,
or any step reports `Fail`. Run 1 keeps the canonical `result_file`/`log_file` (the archived evidence); runs 2..N
write `<file>.run<N>`, so a later clean run never overwrites and never repairs an earlier dirty one.

**Cause 1 (fixed): the pump node outlives the engine's teardown.** `Application.Quit` is deferred to the end of the
frame, and `GameCorePlayerLoopInstaller` installs a node whose trampoline pumps every registered owned world on every
`Update`. A pump that runs while the engine is releasing its worlds and native containers steps a world whose storage
is being freed. `GameCorePlayerLoopInstaller.InstallQuitHook()` now subscribes once to `Application.quitting`,
disables `GameCoreApplicationPump.IsEnabled` and removes exactly its own node; `GameCoreApplicationBootstrap.Initialize`
installs it beside `EnsureInstalled()`. The hook is idempotent, removal is node-scoped (`RemoveNodes` matches only
`typeof(GameCorePumpLoop)`), and `ResetCounters()` resets the new counters while deliberately keeping the subscription
flag (the subscription survives a domain-reload-disabled session).

**Cause 2 (fixed): freeing a lane payload arena a scheduled writer still targets.** `NativeMessageLane.Dispose()`
disposed `rows`/`payloadArena` without completing the tracked `payloadWriter` `JobHandle`; a producer job still
outstanding at that moment writes into freed `Allocator.Persistent` memory — a use-after-free whose symptom is
exactly "the process did its work, then died at an arbitrary later point". `NativeMessageLane.Dispose()` now
completes its own writer inside `try`/`catch` (counting a failure instead of throwing from `Dispose`) and zeroes the
handle; `NativeMessageLanes.Dispose()` completes every lane's writer before releasing any lane's storage.

**Honest limits.** Both fixes are reasoned from the code, not from a reproduction: this host cannot run the player,
so whether the observed 139 was cause 1, cause 2 or something else is unproven. The N-run detection is what makes
the gate fail on a recurrence, and it is unconditional. If the crash reappears on the build host, the raw `.run<N>`
logs are the evidence to compare.

## 7. Decisions, assumptions and doc ambiguities

Recorded because 00 wins over 05, which wins over 09.

### 7.1 The runtime assembly now references `GameCore.Derivation` (deviation from 04 §2)

04 §2's allowed-references row for `GameCore.Unity.Runtime` is "Contracts, Composition, Planning, Unity
Entities/Collections/Jobs/Burst/Mathematics" — no Derivation. The W2 brief (09) directs the reusable glue into
`Packages/com.gamecore.unity.runtime/Runtime/Integration/`, and the derivation→plan→publish join needs
`DerivationSnapshot`, `DerivationEngine`, `DerivationTarget`, `IDerivationValueSource` and their results.
Alternatives considered and rejected:

* move the glue into `GameCore.Planning` (which 04 §2 does allow to reference Derivation): impossible for the whole
  chain, because `DerivedAssemblyPipeline` needs `UnityWorldHost`, `CompositionHost`, `AssemblyPublisher`,
  `LiveTargetIndex`; a partial split would leave the runtime half still naming Derivation types;
* wrap GC-006's result shapes in Planning-owned mirrors so the runtime half never names them: this is a second,
  divergent interface for `TargetAssembly`/`EffectiveSlot`/`CapabilityContribution`, which 09 §"shared-file rules"
  forbids ("it does not introduce a private divergent interface").

The edge is acyclic (`GameCore.Derivation` → `GameCore.Contracts` only) and `GameCore.Unity.Runtime` is Unity-only
(never compiled by the plain-dotnet projects, so no second contract copy appears). **Requested follow-up: update 04
§2's row to include `GameCore.Derivation`.** I did not edit the normative doc myself.

### 7.2 Publication 3: a composition publication whose derivation has no target change

P-006 has one publication series, so every lane publication needs the world's assembly for the same number, and
`AssemblyPlanner`/`AssemblyPublisher` refuse to advance an epoch for a `NoChange` plan. A spawn therefore cannot be
"the assembly" of a publication that also changed some target's rows. The gate makes this explicit: provider 2
("forward") declares a rule whose selector recipe has **no live target**, so mounting it publishes a real
composition revision whose derivation produces no effective change, and the *world's* assembly for that publication
is the spawn of the future villager. The window in which the lane is one publication ahead of the world is exactly
the same window that exists between the mount's composition publication and its assembly publication, so no special
case is introduced; the gate asserts `CountersJoined` after every assembly boundary. The scenario also asserts the
"no target change" fact, which is what proves the assembly for that epoch is *complete* rather than partial.

### 7.3 The slot value transfer is one canonical big-endian int32

A derived output slot must become a binding row's `Value` (a single `int`). 05 §6 fixes the wire convention as
"big-endian integer scalars", and `GameCore.Derivation.Fixtures.FixturePayload.Int32` writes exactly those four
bytes, so `IntegrationSlotValues` reads/writes the same encoding and the two cannot disagree. Any other payload
length is refused (`UnsupportedSlotValue`) rather than truncated — a 0-byte or 16-byte reference payload would
otherwise be turned into a value nobody derived. GC-006's own suite keeps full coverage of the multi-contribution
policies; the gate records the seam limit explicitly: an effective slot with more than one supporter has no
representation in a row set keyed by (target, capability, output slot) and is refused with a detail naming it.

### 7.4 The gate admits the command through the host plane and drives frames through GC-009's temporal driver

`WorldTimeDriver.FeedDemand` turns both the cutoff's sealed command batch *and* due plugin wakes into demand, and
`UnityWorldHost.Submit` already turns an admitted command into one unit of demand. Feeding the same command through
both paths would double-count demand and run a second (idle) step, so the gate admits the command with
`host.Submit` (the only path that creates a bounded lane row, P-042) and wraps the frame with
`WorldTimeDriver.PumpFrame` (input cutoff, plugin clocks, adopted native resource table). The driver's cutoff is
therefore empty for that step and feeds nothing. The registered-wake step proves the other side: a zero-delay wake
on a registered `DomainExplicit` clock is demand for exactly one further step, which commits no request.

### 7.5 Other decisions

1. **One authoritative store per domain.** The gate's quest state lives in the `TargetSlotState` slot storage, not in
   a separate gameplay component, so the migrated value (P-029) and the committed command value (P-042) are the same
   value and the publication's migration is visible in the same place the owner writes.
2. **Live state is seeded at schema version 1 while the descriptor declares version 2**, so the end-to-end chain
   really stages a registered migration on bounded scratch; the gate asserts the migrated value, the schema version
   and that the handler ran at least once per migrated slot.
3. **The command's target is resolved by a fixture-owned target→entity map** (`W2GateModule.MapTarget`), populated
   when the scenario seeds a target. The owner stage therefore resolves a message's stable `TargetId` without
   consulting a second authority (P-004), and an unknown target is an observable `StaleHandle` rejection.
4. **The gate's two providers mount through the same precompiled factory key with distinct plugin types.** 04 §8
   makes registration data, and `CatalogManifestSource` admits one declaration per plugin type; two plugin types
   resolving one factory is the smallest honest way to mount two instances of one precompiled plugin.
5. **Scoring: the trait domain's two per-partition writers and the trail domain's two whole-schema writers** are the
   gate's GC-007 evidence — the first pair must be provably disjoint by generated partition, the second ordered by
   the compiled stage order (P-034, P-040).
6. **The settle stage publishes its job handle only through `NativeDependencyTable`** (not through
   `SystemBase.Dependency`), so the later project stage's wait is load-bearing; the project stage records whether the
   combined fence was non-default, which is the observable proof (P-041).
7. **The gate reuses the GC-006 narrative fixture's *identities and descriptor shapes*, not its chapter rules.**
   The live targets are the 07 §3 narrative targets (`npc-mara`, `gate-east`, `crowd-prop`, `encounter-oak`) with the
   narrative recipes as their schemas, and the two rules the gate mounts are named with the narrative fixture's own
   rule-name helpers (`NarrativeComposition.DialogueRule("chapter-one")`, `.GateRule("chapter-one")`), so nothing in
   the gate can drift from the descriptors GC-010 will build on. The gate declares its *own* capability contracts
   and rules on top of them, because the chapter's rules publish opaque tag payloads whose domain wiring (dialogue
   graphs, gate conditions, choice surfaces, encounter hooks) is GC-010's work, and a binding row can only carry
   the one int32 value this gate transfers (§7.3).

## 8. Requirement and test coverage mapping

| Gate clause / requirement | Where proven | Evidence |
|---|---|---|
| the whole chain runs on real modules | `W2GateScenario`, `DerivedAssemblyPipeline` | 11 observations `gate2-*` per catalog, `EveryGateCheckPassesOverTheGeneratedCatalog`, `…FixtureCatalog` |
| P-004/P-005 identity and handles | `LiveTargetIndex`/`LiveTargetSeeder` refuse a default or duplicate identity; the owner resolves `TargetId` | `gate2-world-and-live-targets`, `gate2-one-bounded-command-committed` |
| P-006 one publication series | `DerivedAssemblyPipeline` adopts the lane's own pair; `CountersJoined` asserted after every assembly boundary | `gate2-provider-mounted-and-published`, `gate2-forward-provider-and-spawned-target` |
| P-008 canonical order | GC-009's compiler over the declared graph; GC-007's canonical partition ids | `gate2-ownership-and-schedule-compiled` (`scheduleHash`) |
| P-009 catalog/manifest miss | `CatalogManifestSource` refuses an unregistered declaration | `gate2-catalog-and-declarations` |
| P-013 Automatic grant, P-015 eligibility | GC-006's mode gate and selector matching over the committed composition; an ineligible recipe is untouched | `gate2-derived-layout-in-entities` (Mara/GateEast rows, CrowdProp/EncounterOak empty) |
| P-017 contribution identity | contributions grouped per (target, capability, slot) and transferred as one proposal capability each | `gate2-provider-mounted-and-published` (2 capabilities, 1 mount) |
| P-019/P-022 composition and budgets | derivation accepts; the plan stays inside its hard budgets | same observation ([GC-006/GC-008 suites](artifacts/gc-006/HANDOFF.md) hold the policy cases) |
| P-024 spawn | `AssemblyPublisher.Spawn` with the composition publication's own numbers | `gate2-forward-provider-and-spawned-target` |
| P-027/P-028 plan states and recheck | `AssemblyPlanner` rechecks the world's published pair; the descriptor validates | `gate2-provider-mounted-and-published` |
| P-029/P-032 migration on scratch, one authoritative store | 4 live slots migrated, value = seeded + delta, version = descriptor's | `gate2-derived-layout-in-entities`, `gate2-one-bounded-command-committed` |
| P-030 one visible epoch | the captured observer holds the complete old image while the switch replaces the reference; the stamp names the published epoch | `gate2-provider-mounted-and-published` (`observerOneCompleteImage`), `gate2-derived-layout-in-entities` (stamp == published epoch) |
| P-034 one owner per domain, generated partitions | GC-007's `OwnershipReport` | `gate2-ownership-and-schedule-compiled` (disjoint trait writers, ordered trail writers) |
| P-035/P-036/P-038 lifecycle, temporal models, clocks | `WorldTimeDriver` frames; a registered wake advances exactly one step; the idle world advances none | `gate2-registered-wake-advances-one-step`, `gate2-idle-world-performs-zero-steps` |
| P-039/P-040 stage declarations and the compiled DAG | GC-009's compiler, the adapter's table, the plan's rebind at every epoch | `gate2-ownership-and-schedule-compiled`, `gate2-ordered-dispatch-and-dependent-read` |
| P-041 jobs and structural playback | the settle job's handle travels only through the native dependency table; the project stage waits | `gate2-ordered-dispatch-and-dependent-read` |
| P-042/P-044 bounded command, commit | the message plane's lane, ledger and committed event; live storage compared with the event payload | `gate2-one-bounded-command-committed` |
| P-043 buffers and backpressure | declared lane and declared step buffer; commit-time drain validation passes | `gate2-one-bounded-command-committed` (`drain`), GC-007 suite for overflow |
| P-045 observation | committed event page read at the publication boundary | `gate2-one-bounded-command-committed` |
| P-047/P-048 teardown | retained jobs settle, no resource retained, registry restored | `gate2-teardown-settles-and-disposes` |
| P-058 V1 profile (IL2CPP, no reflection) | the same scenario runs under High stripping via `-probeW2Gate` | `run_w2_gate_probe.sh` |
| TEST-004 | the mount derives both existing eligible targets and the future spawned one | `gate2-derived-layout-in-entities`, `gate2-forward-provider-and-spawned-target` |
| TEST-009 | one epoch, one visible assembly, lane == world | `gate2-provider-mounted-and-published`, `gate2-forward-provider-and-spawned-target` |
| TEST-010 | the migrated live value survives the publication | `gate2-derived-layout-in-entities` |
| TEST-011 | idle world, registered wake, no double advance | `gate2-idle-world-performs-zero-steps`, `gate2-registered-wake-advances-one-step` |
| TEST-012 | the compiled DAG with a buffer edge drives the real dispatch table | `gate2-ownership-and-schedule-compiled`, `gate2-ordered-dispatch-and-dependent-read` |
| TEST-013 | ordered dispatch executed, ordered command commit, dependent read waits | `gate2-ordered-dispatch-and-dependent-read` |
| TEST-014 | committed events exposed only at publication; the ledger distinguishes acceptance from commitment | `gate2-one-bounded-command-committed` |
| TEST-016 | reserved for W5's fault boundaries; this gate has no injected fault (GC-008's own suite covers it) | — |
| TEST-020 | generated-style recipes and precompiled appliers in a real world | `gate2-world-and-live-targets`, `gate2-forward-provider-and-spawned-target` |

## 9. Known gaps and risks

* **Nothing here has been compiled, imported or executed.** The most likely first failures, in order:
  1. an asmdef reference error (`GameCore.Unity.Runtime` → `GameCore.Derivation`, and the fixtures assembly's three
     new references) — the W2 gate is the first consumer of that edge;
  2. a nullable-reference or member-name slip in the new files (Unity does not treat warnings as errors, but a wrong
     member name is a hard error). Highest-risk spots: `W2GateScenario`'s fact wiring, `OwnershipSchedulePipeline`'s
     descriptor assembly, and `DerivedAssemblyPipeline`'s outcome mapping;
  3. an expectations mismatch in the scenario's exact numbers (I asserted literals: 4 stages, 5 systems, 5 dispatched
     entries, 2 derived targets, 4 migrated slots, 2 installed rows, epochs 1→2→3, 3 published binding rows);
  4. `unity/GameCore.Validation/Packages/packages-lock.json` is stale relative to three packages (`composition`,
     `derivation`, `planning` now declare `"com.gamecore.contracts": "0.1.0"` while the committed lock still records
     the `file:` form) — the gate's resolve step regenerates it and the regenerated lock must be committed with the
     result (04 §1 forbids synthesising it here);
  5. `.meta` GUIDs were authored here with deterministic GUIDs and verified unique against 371 existing ones, but
     only an import can confirm Unity keeps them; commit any rewrite.
* **`GameCore.Execution` is compiled twice** (Unity assembly `GameCore.Unity.Runtime` and the plain-dotnet
  `dotnet/src/GameCore.Execution`). The new integration glue is Unity-only (it uses `Entity`/`EntityManager`), so the
  gate's dotnet half proves the Wave 2 modules' pure halves as before and the Unity suites prove the join.
* **No seam fixture was deleted.** Each W2 task's private fixture remains as that task's own unit evidence; what the
  gate proves is that the *real* modules substitute for them end to end. Deleting them is a W3+ decision.
* **The gate does not exercise incremental invalidation, lifecycle transitions, checkpoints or fault injection** —
  those are GC-013 to GC-018, and their absence is why this gate claims only the W2 exit conditions.
* **TEST-016 is deliberately not claimed here** (GC-008's own suite holds the prewrite/postwrite fault cases); the
  W2 gate runs no injected fault.
* **`WorldCompositionBridge.SubmitAndExecute` is not used by this gate.** It implements W1's "one admitted operation
  is one command step" model; W2's operation is a *publication*, so the gate drives `CompositionHost` directly and
  uses the bridge only for the join, its construction-time seed check and `SyncStepFromWorld`.
* **`ProbeRobustness`'s scripts were verified by `bash -n` and against a stub player, not against a real player.**
* **`artifacts/w2-gate/{trx,unity,toolchain}` are produced by the gate; nothing is pre-populated here.**
