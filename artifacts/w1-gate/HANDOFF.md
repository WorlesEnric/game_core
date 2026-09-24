# W1-GATE HANDOFF — Wave 1 integration gate

Branch: `w1-gate` (worktree `/Users/yangcao/wkspace/gc-wt/w1-gate`); it merges `gc-003`, `gc-004` and `gc-005`.

**Status of every executable check in this handoff: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported or
executed here. Only interpreter-level host checks ran; they are recorded in `artifacts/w1-gate/static-checks.log`
and are explicitly **not** a build result.

Gate sentence implemented (verbatim, `docs/game-core/09-implementation-guide.md`, Wave 1 — Shared seams and one
owned world):

> Integrate catalog/DTOs, control host and actual Unity world driver. Create two worlds, admit one operation and
> execute a guarded fixture stage; prove a thrown postwrite exception stops the next stage and publication.

## 1. Summary

Three things were done.

1. **Every production consumer now compiles against the production contracts.** `GameCore.Composition`,
   `GameCore.Execution` and their test projects reference `dotnet/src/GameCore.Contracts` instead of
   `dotnet/src/GameCore.ReferenceSeams`; no project in the solution references both contract assemblies. The W0
   seam survives only where it belongs (its own project, the seam-based fixture oracle and the API-snapshot
   comparison). The temporary Unity packaging of the seam (`tests/GameCore.ReferenceSeams/package.json` +
   `GameCore.Contracts.asmdef`) is deleted, so `Packages/com.gamecore.contracts` is the only Unity provider of the
   `GameCore.Contracts` assembly. `dotnet/GameCore.sln` and `dotnet/README.md` were repaired after the merge lost
   the GC-004/GC-005 project entries.
2. **The integration itself** — real modules only, no seam stub — lives in three integration points:
   `GameCore.Unity.Runtime.Integration.WorldCompositionBridge` (control lane ↔ owned world),
   `GameCore.Unity.Runtime.Integration.CatalogManifestSource` (generated catalog ↔ mount resolution) and
   `GameCore.Unity.Fixtures.W1GateScenario` (the scenario that drives both). It is executed by a new Unity
   EditMode assembly `GameCore.W1Gate.Tests` and by a new player probe mode `-probeW1Gate`; both run the identical
   scenario over the committed generated catalog *and* over a hand-written generated-style catalog.
3. **Two gate scripts**: `tools/run_w1_gate.sh` (whole gate) and `tools/unity/run_w1_gate_probe.sh` (the player
   probe alone).

## 2. What was substituted (production modules in place of the frozen seam)

| Consumer | Before (W1 peers) | After (this gate) |
|---|---|---|
| `dotnet/src/GameCore.Composition` | `ProjectReference` → `GameCore.ReferenceSeams` | `ProjectReference` → `GameCore.Contracts` |
| `dotnet/tests/GameCore.Composition.Tests` | `GameCore.Composition` + `GameCore.ReferenceSeams` | `GameCore.Composition` + `GameCore.Contracts` |
| `dotnet/src/GameCore.Execution` | `ProjectReference` → `GameCore.ReferenceSeams` | `ProjectReference` → `GameCore.Contracts` |
| `dotnet/tests/GameCore.Execution.Tests` | `GameCore.Execution` + `GameCore.ReferenceSeams` | `GameCore.Execution` + `GameCore.Contracts` |
| `unity/GameCore.Validation` manifest | `com.gamecore.reference-seams` (GC-005 shim) | production `com.gamecore.contracts` only (swap already merged; the shim package is now deleted) |

Kept on the seam on purpose: `dotnet/src/GameCore.ReferenceSeams`, `dotnet/src/GameCore.ProtocolFixtures` (the
seam-compiled oracle), `dotnet/tests/GameCore.ReferenceSeams.Tests`, `dotnet/tests/GameCore.ProtocolFixtures.Tests`.
`GameCore.ApiSnapshot` compares the frozen surface as a committed text snapshot, so no assembly ever sees two
`GameCore.Contracts` copies. Verified mechanically over every `.csproj` in this change:

```sh
for f in $(find dotnet -name '*.csproj'); do grep -o 'ProjectReference Include="[^"]*"' "$f"; done
```

**No namespace `GameCore.TestFixtures` stub is referenced from any production or test project.** The only
occurrences are inside the seam package itself (`tests/GameCore.ReferenceSeams/Stubs/**`) and one assertion in
`dotnet/tests/GameCore.ReferenceSeams.Tests/ApiSnapshotTests.cs` that the stub namespace never appears in the
snapshot. `Packages/com.gamecore.unity.runtime`, `…unity.adapters`, `…composition` and the validation project
never referenced a seam stub, so no Unity-side fix was needed beyond deleting the shim package.

Deleted (GC-005 additions, no longer needed and now a duplicate-assembly hazard):

- `tests/GameCore.ReferenceSeams/package.json` (+ `.meta`)
- `tests/GameCore.ReferenceSeams/GameCore.Contracts.asmdef` (+ `.meta`)
- `tests/GameCore.ReferenceSeams/README.md` — "Unity packaging" section rewritten to record the removal.

## 3. The integration and where the glue lives

| Piece | Path | Why it exists |
|---|---|---|
| Control lane ↔ owned world | `Packages/com.gamecore.unity.runtime/Runtime/Integration/WorldCompositionBridge.cs` (assembly `GameCore.Unity.Runtime`) | Admission, publication and the demand handoff are one API: a world that cannot execute is refused **before** admission, an admitted+published operation becomes exactly one unit of command demand, and a post-write fault is reported beside the operation without restating its published result. |
| Generated catalog ↔ mount resolution | `Packages/com.gamecore.unity.runtime/Runtime/Integration/CatalogManifestSource.cs` (assembly `GameCore.Unity.Runtime`) | Implements `GameCore.Composition.IPluginManifestSource` over an `ICatalog` + generated-style declarations. A declaration whose precompiled factory key, factory kind or configuration schema the catalog does not register is refused at construction and reported as a miss at resolution (P-009). |
| The scenario | `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/W1GateScenario.cs` (+ `W1GateCatalog.cs`, `W1GateFixture.cs`, assembly `GameCore.Unity.Fixtures`) | Runs the gate end to end and returns named observations plus the facts they were computed from. |
| EditMode half | `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/W1Gate/` (assembly `GameCore.W1Gate.Tests`, Editor-only, `UNITY_INCLUDE_TESTS`) | 8 NUnit cases asserting the scenario's facts per requirement. |
| Player half | `…/Runtime/ProbeW1Gate.cs` + `…/Runtime/W1GateScenarioHost.cs` + `-probeW1Gate` in `ProbeArguments`/`ProbeRunner` | The same scenario inside the IL2CPP player, reported into the existing JSON result shape. |

Both integration adapters are genuinely reusable (they are the plain two-module join, not test scaffolding), so
they live in the runtime assembly rather than in the test assembly. The scenario and its data live in the
qualification fixture assembly, next to `FixtureKeys`/`FixtureRegistration`.

`WorldCompositionBridge` deliberately owns no durable state and does not pretend to be live publication: mapping a
`CompositionEditPlan` to component/binding diffs and advancing the **world's** `AssemblyEpoch` is GC-008's work
(see "known gaps").

## 4. Files created

Runtime package `Packages/com.gamecore.unity.runtime`:

- `Runtime/Integration/CatalogManifestSource.cs` — `CatalogManifestSource`, `CatalogPluginDeclaration`,
  `CatalogDeclarationRejection`
- `Runtime/Integration/WorldCompositionBridge.cs` — `WorldCompositionBridge`, `BridgeOutcome`,
  `WorldAdmissionReport`, `WorldExecutionReport`
- `Fixtures/Runtime/W1GateCatalog.cs` — `W1GateRecord`, `W1GateRecordSerializer`, `W1GateCatalog` (generated-style
  registration table + `DerivationHolds`)
- `Fixtures/Runtime/W1GateFixture.cs` — `W1GateKeys`, `W1GateManifests`, `W1GatePayloads`
- `Fixtures/Runtime/W1GateScenario.cs` — `W1GateStep`, `W1GateFacts`, `W1GateScenarioResult`, `W1GateScenario`
- `.meta` for every new file and folder (deterministic GUIDs, checked unique against the 170 GUIDs already in the
  repository)

Unity qualification project:

- `Assets/GameCore.Validation/Tests/W1Gate/GameCore.W1Gate.Tests.asmdef`
- `Assets/GameCore.Validation/Tests/W1Gate/W1GateIntegrationTests.cs`
- `Assets/GameCore.Validation/Runtime/W1GateScenarioHost.cs`
- `Assets/GameCore.Validation/Runtime/ProbeW1Gate.cs`
- `.meta` for the new files and folders

Tooling and evidence:

- `tools/run_w1_gate.sh`, `tools/unity/run_w1_gate_probe.sh`
- `artifacts/w1-gate/HANDOFF.md` (this file), `artifacts/w1-gate/static-checks.log`

## 5. Files modified (each minimal and explained)

| Path | Change | Why |
|---|---|---|
| `dotnet/GameCore.sln` | GC-004 projects (`GameCore.Composition`, `GameCore.Composition.Tests`) and GC-005 projects (`GameCore.Execution`, `GameCore.Execution.Tests`) re-added with fresh project GUIDs `{7E4C1A20-4B31-4D6E-9C58-2A1F0B7D1101..1104}` and matching Debug/Release rows | the merge kept only GC-003's list; the branches had collided on `{1A2B3C4D-0006…}`/`{…0007…}` |
| `dotnet/README.md` | project table extended with the four projects; substitution paragraph added; W1 gate command added | the table is the documented index of `dotnet/` |
| `dotnet/src/GameCore.Composition/GameCore.Composition.csproj` | reference → `GameCore.Contracts`; comment updated | substitution |
| `dotnet/src/GameCore.Execution/GameCore.Execution.csproj` | reference → `GameCore.Contracts`; comment updated | substitution |
| `dotnet/tests/GameCore.Composition.Tests/*.csproj`, `dotnet/tests/GameCore.Execution.Tests/*.csproj` | reference → `GameCore.Contracts`; comment updated | substitution |
| `tests/GameCore.ReferenceSeams/README.md` | "what this is" and "Unity packaging" sections updated | the shim package was deleted by this task |
| `Packages/com.gamecore.unity.runtime/Runtime/GameCore.Unity.Runtime.asmdef` | `+ GameCore.Composition` | 04 §2 allows `GameCore.Unity.Runtime` → Composition; the integration adapter needs `CompositionHost` |
| `Packages/com.gamecore.unity.runtime/Fixtures/Runtime/GameCore.Unity.Fixtures.asmdef` | `+ GameCore.Composition` | the gate fixture builds edit payloads and lanes |
| `unity/.../Runtime/GameCore.Validation.ProbeHost.asmdef` | `+ GameCore.Composition`; duplicate `GameCore.Contracts` entry removed | the probe host now runs the gate scenario |
| `unity/.../Runtime/ProbeArguments.cs` | `-probeW1Gate` added | new probe mode; no existing argument changed |
| `unity/.../Runtime/ProbeRunner.cs` | W1 gate branch + task id `W1-GATE` | GC-001 and GC-005 paths untouched |

No file under `tests/GameCore.ReferenceSeams/**/*.cs` or `.../api/` was touched, so the frozen W0 surface and its
API snapshot are unchanged.

## 6. Exact commands for the Linux build host

Everything below runs from the repository root.

### 6.1 One command (the whole gate)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=/usr/bin/dotnet \
  tools/run_w1_gate.sh
```

It runs: `dotnet build dotnet/GameCore.sln -c Release`; `dotnet test dotnet/GameCore.sln -c Release --logger trx
--results-directory artifacts/w1-gate/trx`; a Unity package resolve; the **whole** Unity EditMode suite (every
`testables` package plus `GameCore.W1Gate.Tests`); the whole PlayMode suite; `tools/unity/build_probe.sh` (catalog
codegen + StandaloneLinux64 IL2CPP with High stripping); `run_probe.sh both` (GC-001), `run_world_probe.sh`
(GC-005), `run_w1_gate_probe.sh` (W1-GATE); and the documentation validator. `UNITY` is required — the gate is
never claimed from the dotnet half alone.

### 6.2 The pieces

```sh
# dotnet half
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/w1-gate/trx

# W1 gate EditMode assembly alone
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.W1Gate.Tests \
  -testResults artifacts/w1-gate/unity/w1gate-editmode.xml -logFile artifacts/w1-gate/unity/w1gate-editmode.log

# the other EditMode suites of the testable packages
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Composition.Tests \
  -testResults artifacts/w1-gate/unity/composition-editmode.xml -logFile artifacts/w1-gate/unity/composition.log
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform EditMode -testFilter GameCore.Unity.Runtime.Tests \
  -testResults artifacts/w1-gate/unity/runtime-editmode.xml -logFile artifacts/w1-gate/unity/runtime.log

# PlayMode suite of the adapters package
"$UNITY" -batchmode -nographics -projectPath unity/GameCore.Validation \
  -runTests -testPlatform PlayMode -testFilter GameCore.Unity.Adapters.Tests \
  -testResults artifacts/w1-gate/unity/adapters-playmode.xml -logFile artifacts/w1-gate/unity/adapters.log

# player build, then every probe
UNITY="$UNITY" tools/unity/build_probe.sh
tools/unity/run_probe.sh both
tools/unity/run_world_probe.sh
tools/unity/run_w1_gate_probe.sh
```

Do not add `-quit` to a test-run command (04 §10).

### 6.3 What the gate probe asserts

`run_w1_gate_probe.sh` requires `"task": "W1-GATE"`, `"mode": "W1Gate"`, `"result": "Pass"`, no `"status": "Fail"`,
the thirteen observations (ten scenario steps, each present twice — once for the committed generated catalog and
once prefixed `fixture:` — plus `fixture:gate-key-derivation`, `gate-generated-catalog-facts`,
`gate-fixture-catalog-facts`), and that the generated run's facts digest reports the committed generated catalog's
own fingerprint literal.

### 6.4 Build-host cleanups this gate expects

1. **`unity/GameCore.Validation/Packages/packages-lock.json` is stale**: it still lists only
   `com.gamecore.content.compiler` and `com.gamecore.contracts` as `gamecore` entries, while the manifest names
   five. The gate's resolve step regenerates it; commit the regenerated lock with the result (04 §1 forbids
   synthesising it here).
2. Unity creates `.meta` files for new assets on first import. This task committed them already (deterministic
   GUIDs, no collisions with the existing 170), so a first import should change nothing; if Unity rewrites any of
   them, commit the rewritten files.

## 7. Requirement and test coverage mapping

The gate sentence, clause by clause:

| Clause | Where it is proven | Evidence |
|---|---|---|
| "Integrate catalog/DTOs" | `CatalogManifestSource` over `ICatalog`; `W1GateCatalog` generated-style table; the committed `ProbeCatalog` | step `gate-catalog-and-manifest-source` (both runs) + `fixture:gate-key-derivation`; test `TheGeneratedRunUsesTheCommittedGeneratedCatalog` |
| "control host" | real `CompositionHost` lane per world (`CreateDefault`) | steps `gate-control-lanes-linked`, `gate-one-operation-admitted` |
| "actual Unity world driver" | real `Unity.Entities.World` through `UnityWorldRegistry`/`UnityWorldHost` and the guarded dispatch groups | steps `gate-two-owned-worlds`, `gate-guarded-stage-commits` |
| "Create two worlds" | two hosts, distinct sessions, registry grows by exactly 2 | step `gate-two-owned-worlds`; test `TwoOwnedWorldsAreCreatedAndTheSecondStaysIdle` |
| "admit one operation" | `WorldCompositionBridge.SubmitAndExecute` → `CompositionHost.SubmitEdit` + `Drain` → revision/epoch 1 | step `gate-one-operation-admitted` |
| "execute a guarded fixture stage" | one admitted command = one logical step; accept/settle/fault/project each ran once; counter 111; step image published | step `gate-guarded-stage-commits`; test `TheAdmittedOperationExecutesOneGuardedStage` |
| "prove a thrown postwrite exception stops the next stage" | `FixtureFaultSystem` wrote (counter 222) then threw; `ProjectCount` stays 1 | step `gate-thrown-postwrite-exception-fails-stop`; test `AThrownPostwriteExceptionStopsTheNextStageAndPublication` |
| "…and publication" | step stays 1, image count stays 2, no image for step 2, world `Faulted`, jobs quarantined | same step/test |
| honest status after the fault | published operation result retained; fault reported by code/lifecycle/count; lane revision still 2; retention follows the world's step; a faulted world admits nothing | steps `gate-operation-status-reports-fault-honestly`, `gate-faulted-world-refuses-admission`; test `TheStatusOfTheOperationAndTheWorldFaultAreReportedHonestly` |
| safe teardown | retained jobs settled before storage release, no retained resources, both worlds disposed, registry back to its pre-gate size | step `gate-teardown-settles-and-disposes`; test `TeardownSettlesPendingWorkAndLeavesNoHostRegistered` |
| world B stays idle over real frames | step 0, zero step-group dispatches, one image, ingress/output on every routed frame | step `gate-second-world-stays-idle` |

Requirements (00) and suites (08) this gate re-proves on real modules:

| Requirement | Mechanism exercised here |
|---|---|
| P-002 participants/authority | one lane + one host per world, joined explicitly; no second host or lane is created |
| P-004 identity | two distinct world sessions; installation identity is the mount's stable id |
| P-006 version domains | the lane's revision/epoch advance on publication while the world's logical step does not; the world's step is the only step authority, and the lane merely follows it |
| P-009 catalog/manifest | a declaration whose factory key the catalog does not register is refused and reported as a miss; an unregistered plugin type is never substituted |
| P-027/P-028 plans and fingerprints | the edit is checked against the published revision; the catalog's own fingerprint is compared with the emitted/recomputed value |
| P-031 post-write fail-stop | the throwing stage stops the next stage, faults the world and publishes nothing |
| P-035/P-036/P-037 lifecycle and demand | one admitted command is exactly one logical step; the idle world commits zero steps over many frames |
| P-041 jobs | the failed step's job is quarantined and retained until teardown |
| P-043/P-044 commit | the committed step image is published with the token both modules agree on; a failed step never publishes |
| P-047/P-048 teardown | retained work settles before storage release; no retained resources; both worlds disposed |
| P-050/P-051 control lane | admission returns a handle, the terminal result is readable by operation identity, a refused admission creates no ledger row |
| P-058 V1 profile | the same scenario runs in the IL2CPP player under High stripping (`-probeW1Gate`) |
| TEST-009/011/013/016/018 | step/snapshot publication boundary, idle worlds, ordered dispatch, named fault injection, Unity-world fail-stop — now with the production catalog and the control lane in the loop |

## 8. Decisions, assumptions and doc ambiguities

Recorded because 00 wins over 05, which wins over 09.

1. **What "admitted result becomes execution" means at W1.** The bridge turns an admitted *and published* edit
   into exactly one unit of command demand; the guarded driver consumes it as one logical step and runs the
   fixture stages. The gate does not claim that a plan's component/binding diffs were applied to the world —
   that mapping is live publication (GC-008), which W1 has no module for.
2. **The two epoch counters are separate, on purpose.** The lane's `CompositionRevision`/`AssemblyEpoch` advance
   on the composition publication; the world's `AssemblyEpoch` advances only through *its own* published
   assembly (04 §5). The gate asserts the world epoch stays 1 while the lane reaches 2/2 and records that as an
   explicit gap (§9), rather than inventing a cross-writer epoch advance in the W1 bridge.
3. **A post-write fault does not roll back a publication.** The lane published before the world executed, and
   04 §5 says a theoretically reversible write does not relax the cutoff; the operation's terminal result
   therefore stands (`Published`) and the world reports `Faulted` separately. `WorldExecutionReport` exposes both
   and `FaultedAfterPublication` names the combination, so no caller can read the pair as a false success or a
   false rollback.
4. **A faulted world refuses before admission.** `SubmitAndExecute` checks the lifecycle first, so the lane gains
   no ledger row, retention entry or issuer-sequence consumption for work the world cannot run. The scenario
   asserts the row count is unchanged.
5. **Retransmissions create no demand.** A repeated `(operation id, input)` returns the original row (P-050), so
   the bridge reports `BridgeOutcome.Retransmission` with the original outcome and submits no command;
   `SubmittedCount` counts demand handoffs only.
6. **Hand-written generated-style catalogs follow the emitter's conventions, including key derivation.** The
   fixture's two literal `FactoryKey`s are the documented SHA-256 derivation of their stable names, and
   `W1GateCatalog.DerivationHolds()` re-checks them with the production `StableNameKeyDerivation`, exactly as
   `ProbeKeys.DerivationHolds()` does for GC-001. That is why the gate needs no build step and still cannot drift
   from generated output.
7. **The gate runs the scenario twice, over two catalogs.** Once over the committed generated catalog (real
   compiler output, with the emitted fingerprint literal as the expected value) and once over the fixture's
   hand-written table. The EditMode suite must pass both; the probe requires both name sets.
8. **The scenario is synchronous, so its pump assertions are strict.** Nothing else can pump the gate's worlds
   inside one call, so `PumpFrame` must report the committed step and the faulted step exactly. The only
   pump-order-tolerant assertion is the idle world's, which compares dispatch counts against the world's *own*
   frame counter.
9. **`UnityWorldRegistry` is never reset by the gate.** Teardown stops only the gate's own two worlds and asserts
   the registry returns to its pre-gate size, so the player's application world survives (`ResetAll` would also
   dispose it).
10. **Tests assert facts, not only booleans.** The scenario computes each observation and also records the values
    it was computed from; the EditMode cases assert those values individually so a regression names the value that
    moved.
11. **`GameCore.Unity.Runtime` may reference `GameCore.Composition`** (04 §2 lists Composition among the runtime
    assembly's allowed references), which is what lets the join live in the runtime assembly instead of in the
    test assembly.

## 9. Known gaps

- **Nothing in this change set has been compiled or executed on this host.** The first candidate failures on the
  build host are (a) a Unity asmdef reference error in the new test assembly or the integration adapter,
  (b) nullable-reference warnings in the new files (Unity does not treat warnings as errors; the dotnet projects
  do, but none of the new files is compiled by dotnet), and (c) a name-shape mismatch in the probe step list
  between `W1GateScenario` and `run_w1_gate_probe.sh`.
- **Live publication is out of scope.** The plan→ECS-diff mapping, the world's own `AssemblyEpoch` advance on a
  composition change, and the published view swap belong to GC-008. The gate proves admission → demand →
  guarded step → honest fault reporting, and says so.
- **The lane and the world keep separate epochs** (decision 2). Unifying them requires GC-008's publication
  module; the gate records the observation (`worldAEpoch`) instead of papering over it.
- **`packages-lock.json` is stale and the `.meta` files were authored here.** Both are mechanical host-side
  follow-ups (§6.4).
- **`GameCore.Execution` is compiled twice** (Unity assembly `GameCore.Unity.Runtime` and the plain-dotnet
  project); the gate's dotnet half only sees the engine-free part, so the Unity-only code paths
  (`WorldHost`, `GuardedDispatch`, the bridge) are proven by the Unity suites and the player probe, not by
  `dotnet test`.
- Only the Linux x86_64 IL2CPP profile is targeted; macOS remains unqualified (GC-001/04 §1).

## 10. Production defects found and fixed

None. The substitution was mechanical: no GC-003/GC-004/GC-005 source file needed a semantic change to compile
against the production `GameCore.Contracts` surface, and no defect in those modules was observed while integrating
them (nothing here was executed, so this is a statement about reading, not about a green build). The only
integration-level behaviours that had to be *decided* — refusal before admission, no demand for retransmissions,
no rollback of a published result — are recorded in §8 and are properties of the new bridge, not changes to any
task's module.
