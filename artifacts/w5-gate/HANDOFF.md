# W5-GATE HANDOFF — Wave 5 integration gate (retained observation, faults, checkpoint restore, adapters)

Branch `w5-gate` (worktree `/Users/yangcao/wkspace/gc-wt/w5-gate`), based on `main` (`f28a4af`, the Wave 4 gate) plus
GC-016, with GC-017, GC-018 and GC-019 merged into it here.

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host has no
.NET SDK, no C# compiler, no Mono and no Unity, so nothing in this change set has been compiled, imported, executed or
built here. What did run is interpreter-level only and is recorded verbatim in
`artifacts/w5-gate/static-checks.log`: the host-side C# checker, the W0 contract-surface parity check, the
documentation validator (self-test + full), the release-surface check in `--no-build` mode, `bash -n` over the two new
shell scripts, a JSON validation of the inventory, a whole-worktree `.meta` GUID uniqueness scan and `.cs`↔`.meta`
coverage scan, an independent recomputation of both digest literals, the observation-table agreement check across the
scenario, the probe harness and the EditMode suite, the catalog verifier over all three committed generated catalogs,
the meta generator (idempotent: 0 creations) and a falsifiability self-test of the release checker's balance rule.
None of those is a build or a test result.

## 1. Summary

The Wave 5 exit gate is: *"Join retained observation, deterministic faults, checkpoint restore and common adapters in
one actual world. Show prewrite rejection, postwrite fail-stop, new-session restore, read-only snapshots and stale
asset callback rejection."* Four tasks finished the four modules (GC-016 observation, GC-017 faults, GC-018
checkpoints, GC-019 adapters); this task is the join, and it adds no kernel behaviour of its own.

One real family world is built and driven end to end:

1. **Read-only pinned snapshots (GC-016).** A committed boundary is leased through the world's own
   `WorldObservation`, verified to be its own token's complete image, held across a real publication that moves the
   world's revision and epoch, and byte-identical afterwards. A reflection scan over `ICommittedBoundaryLease`'s own
   members proves no writable type is reachable through it, and mutating the caller's copy of the leased bytes leaves
   both the lease and the store's image unchanged.
2. **Prewrite rejection (GC-017).** `FaultBoundary.Validation` is armed, a real mode edit is admitted, drained and
   derived for, and the publication is refused before any live write: the old assembly keeps its rows, revision,
   epoch, image count and running lifecycle, the plan is terminal `Rejected` without having crossed the live-write
   boundary, and the latch's own trace carries the boundary, the operation and the plan hash.
3. **Postwrite fail-stop (GC-017), on the same world.** The composition publication the refusal left adopted and
   pending is retried with `FaultBoundary.FirstLiveWrite` armed, so the publisher really applies its structural
   writes and then fails inside the fence: `Faulted`/`ApplyFault`, no epoch, no image, no step, the last good image
   still leasable and byte-identical, and no later frame or command resumes the world.
4. **Checkpoint + new-session restore (GC-018).** The checkpoint is captured *before* either fault, at the committed
   boundary and through GC-016's lease (the observation's own lease counter proves the path), with the world's
   declared queue disposition. The document restores into a new unexposed world that is exposed only after
   validation; the restored slots are compared row by row against the captured active **and** dormant rows, and every
   handle minted in the faulted session is refused.
5. **Common adapters (GC-019) on the restored world.** A stamped typed command commits a step through the restored
   world's own command port, a bounded asset lease completes under a live token of the new incarnation, presentation
   reads only the restored world's committed image, destroying every view leaves its gameplay intact, and the faulted
   world's token, input stamp and image are all refused.
6. **Stale asset callback rejection.** The source world's adapter frame is retired after its world stopped; the
   completion that arrives afterwards reaches a retired table, installs nothing, releases nothing another caller owns
   and is counted as what it is. The retired world's last committed image stays readable through a lease.

`IGc019Family` and `IGc018Family` are satisfied by the *same* family objects the earlier gates use (new `partial`
parts), so the gate runs the narrative world and the card market the previous waves run rather than a third
implementation of either genre.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `Merge GC-017`, `Merge GC-018`, `Merge GC-019` | The three task branches, with the probe host's conflicted modes resolved before each commit (see §5). |
| `shared: read the committed boundary through GC-016's frozen lease interface` | `CommittedBoundary.cs`, `CheckpointCapture.cs`, `UnityCommittedBoundaryReader.cs`, new `WorldBoundaryFacts.cs`, three new cases in `CheckpointCaptureTests.cs`. |
| `shared: share the checkpoint codec binding table instead of copying it` | New `Gc018CheckpointCodecs.cs` (moved verbatim out of `Gc018Scenario`), a thin delegating wrapper in `Gc018Scenario.cs`. |
| `shared: prepare the release clone for the merged probe host, and check balance literally` | `tools/unity/prepare_gc017_release_project.py`, `tools/check_release_fault_free.py`. |
| `W5-GATE: join the four Wave 5 modules in one actual world per family` | `W5GateScenario.cs`, `W5GateFamily.cs`, `W5GateNarrativeHost.cs`, `W5GateCardsHost.cs`, `ProbeW5Gate.cs`, `Tests/W5Gate/**`, all `.meta`s. |
| `W5-GATE: the gate script, the player probe and the inventory proposals` | `tools/run_w5_gate.sh`, `tools/unity/run_w5_gate_probe.sh`, `artifacts/gates/w4-generic-profile/inventory.{md,json}`. |
| `W5-GATE: the handoff and the host-side static checks` | `artifacts/w5-gate/HANDOFF.md`, `artifacts/w5-gate/static-checks.log`. |

## 3. Files created

### The gate (all `.cs` files carry a Unity `.meta` with a unique GUID)

| File | Contents |
|---|---|
| `unity/.../Runtime/W5GateFamily.cs` | `W5GateStep`, `W5GateScenarioResult` (digest over `name=pass` lines via `NarrativeDigest.OfLines`) and `IW5GateFamily : IGc018Family, IGc019Family` — the two frozen contracts one genre must satisfy at once, with no member of its own. |
| `unity/.../Runtime/W5GateScenario.cs` | The runner: `ObservationNames` (8, frozen), `QualifiedNames`, `Run(IW5GateFamily)` and the `Executor` that builds the one world, registers the adapters, drives the two faults, captures/restores the checkpoint and verifies the restored state. |
| `unity/.../Runtime/W5GateNarrativeHost.cs` | The narrative family's `IW5GateFamily` partial part plus `RunGeneratedCatalogW5Gate`, `RunFixtureCatalogW5Gate`, `RunBothW5Gate`. |
| `unity/.../Runtime/W5GateCardsHost.cs` | The card family's equivalent. |
| `unity/.../Runtime/ProbeW5Gate.cs` | The `-probeW5Gate` player mode and both digest literals. |
| `unity/.../Tests/W5Gate/GameCore.W5Gate.Tests.asmdef` | Editor-only EditMode assembly (`UNITY_INCLUDE_TESTS`), referencing the two family fixtures, the rules assemblies, the composition, derivation, planning, adapter, runtime and generated-catalog assemblies. |
| `unity/.../Tests/W5Gate/W5GateIntegrationTests.cs` | One `[Test]` per named observation, the two digest tests, and a table test that recomputes both digests from `W5GateScenario.QualifiedNames` so a renamed or dropped observation fails the suite instead of shrinking it. |

### Production and shared

| File | Contents |
|---|---|
| `Packages/.../Runtime/Persistence/WorldBoundaryFacts.cs` | The production `ICommittedBoundaryFactsSource`: the world's own external pending commands and staged lane operations (P-053). |
| `unity/.../Runtime/Gc018CheckpointCodecs.cs` | GC-018's twelve generated-serializer bindings and their twenty-four field-exact conversions, extracted so GC-018's round trip and this gate's capture use one binding table (P-054). |
| `tools/unity/run_w5_gate_probe.sh` | The player-probe harness: `PROBE_RUNS` runs, strict JSON, all 32 observation names (8 × 2 catalogs × 2 families), both digest steps, both literals and 23 clause fragments read out of the step details. |
| `tools/run_w5_gate.sh` | The gate script (§6). |
| `artifacts/w5-gate/HANDOFF.md`, `artifacts/w5-gate/static-checks.log` | This document and the verbatim host-side checks. |

### Modified (shared surfaces)

| File | Change | Why it is safe |
|---|---|---|
| `Packages/.../Runtime/Pure/Persistence/CommittedBoundary.cs` | Four new constructor parameters and four new properties on `CommittedBoundarySnapshot`; a header note recording the reconciliation. | Additive; both construction sites (the Unity reader and the pure test fixture) were updated, and the parameters are required so no reader can forget to report what it leased. |
| `Packages/.../Runtime/Pure/Persistence/CheckpointCapture.cs` | One new refusal: a world whose declared queue facts contradict the copied records. | Only fires when the world declares facts (`Unspecified` is untouched), which is why the existing pure cases still capture. |
| `Packages/.../Runtime/Persistence/UnityCommittedBoundaryReader.cs` | The read now leases GC-016's boundary for its whole duration, takes the step/epoch and queue facts from the lease, and refuses when the world moved under it. | Same ECS copy, one pinned image; every existing refusal value is unchanged. |
| `dotnet/tests/GameCore.Execution.Tests/CheckpointCaptureTests.cs` | The fixture's `Boundary(...)` helper carries the lease facts; three new cases cover a contradicting declaration, a consistent one and an unspecified one. | Additive; the helper's new parameters have defaults. |
| `unity/.../Runtime/Gc018Scenario.cs` | `TryBuildCodecs` delegates to `Gc018CheckpointCodecs`; the now-unused generated-checkpoint `using` was removed. | The observations, headers and digests are unchanged: the same bindings, the same order, the same evidence string. |
| `tools/unity/prepare_gc017_release_project.py` | Removes the Wave 5 gate's scenario/hosts/probe from the marker-free clone and strips both latch-arming modes' flags and branches from the new probe-host shape. | The clone is disposable and gitignored; verified by running the script and checking the patched files. |
| `tools/check_release_fault_free.py` | The balance rule now runs over a literal-blind view of the compiled text; the gate's EditMode assembly joins the asmdef list. | Balance is code structure; the marker scans keep the literal-aware view, and a falsifiability self-test proves real splits still fail. |
| `artifacts/gates/w4-generic-profile/inventory.{md,json}` | A `w5GateRevisions` / "W5-GATE revision notes" section: **proposals only**, no row promoted. | A row may only be promoted from an archived passing probe on the build host. |

## 4. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| P-002 participants and authority | one world per family; the latch, observation store, command port and ledger are the world's own; adapters are callers, never a second authority | every observation; `w5gate-adapters-bind-to-the-restored-world`, `w5gate-retired-world-callbacks-are-rejected` |
| P-004 stable identities, fresh `WorldId` | restore into a new session; old handles/tokens/stamps rejected | `w5gate-restore-into-a-new-session`, `w5gate-retired-world-callbacks-are-rejected` |
| P-005 runtime handles are never identities | the gate compares captured rows to restored rows and refuses old handles | `w5gate-restore-into-a-new-session` |
| P-007 references and leases | GC-016 lease held across a publication; asset leases bounded and validated at completion | `w5gate-pinned-snapshots-are-read-only`, `w5gate-retired-world-callbacks-are-rejected` |
| P-024 spawn/despawn and views | views created from the committed image; destroying them leaves gameplay intact | `w5gate-adapters-bind-to-the-restored-world` |
| P-028 validation, stale plans | the validation boundary reached by a real edit in a live world | `w5gate-prewrite-fault-keeps-the-old-assembly` |
| P-029 prewrite failure releases staged work and keeps the old assembly | `ReachPrewriteFault` → `PrewriteRefusal` in the real publisher | `w5gate-prewrite-fault-keeps-the-old-assembly` |
| P-030 one serialized commit | the pending publication is published exactly once, and the failed one publishes nothing | `w5gate-prewrite-fault-keeps-the-old-assembly`, `w5gate-postwrite-fault-fail-stops-the-world` |
| P-031 postwrite failure faults the world | `FaultAfterLiveWrite` → `EnterFaulted`; the gate then proves no resumption | `w5gate-postwrite-fault-fail-stops-the-world` |
| P-032 dormant state | dormant row seeded in the source world, carried by the capture, verified in the restored world | `w5gate-one-world-with-retained-observation`, `w5gate-restore-into-a-new-session` |
| P-034 one owner per authoritative domain | presentation reads committed images only | `w5gate-adapters-bind-to-the-restored-world` |
| P-045 immutable observation, stale images refused | pinned lease, no writable escape, foreign image refused, old world's last image still readable | `w5gate-pinned-snapshots-are-read-only`, `w5gate-adapters-bind-to-the-restored-world`, `w5gate-retired-world-callbacks-are-rejected` |
| P-047 in-flight lifetime | the retired table refuses the late completion and the old world's token/stamp | `w5gate-retired-world-callbacks-are-rejected`, `w5gate-adapters-bind-to-the-restored-world` |
| P-048 teardown and quarantine | `AdapterTeardownReport`, retired table, no outstanding load, no reserved bytes | `w5gate-retired-world-callbacks-are-rejected` |
| P-049 recovery limits | faulted world never resumes; a new session is restored from the pre-fault checkpoint | `w5gate-postwrite-fault-fail-stops-the-world`, `w5gate-restore-into-a-new-session` |
| P-051 pure queries | the capture mutates nothing (step, epoch, image count, rows and lifecycle all unchanged) | `w5gate-checkpoint-from-the-committed-boundary` |
| P-053 committed boundary, explicit command disposition | the capture leases the boundary through GC-016 and carries the world's declared queue facts; a contradicting declaration refuses | `w5gate-checkpoint-from-the-committed-boundary` |
| P-054 generated serializers | the document is written and read with the committed generated checkpoint catalog's own serializers | `w5gate-checkpoint-from-the-committed-boundary`, `w5gate-restore-into-a-new-session` |
| TEST-002 identity/epoch/stale references | leases are token-bound; restored world refuses old handles | `w5gate-pinned-snapshots-are-read-only`, `w5gate-restore-into-a-new-session` |
| TEST-009 prepared plans visible on both sides of a boundary | the refused plan is terminal and unpublished while the old assembly still serves | `w5gate-prewrite-fault-keeps-the-old-assembly` |
| TEST-010 state preservation and migration | restored slots/mode/scopes compared against the captured set | `w5gate-restore-into-a-new-session` |
| TEST-014 committed events, read-only inspection | lease-only observation, payload verification, mutation isolation, reflection scan | `w5gate-pinned-snapshots-are-read-only` |
| TEST-015 lifecycle and managed resource teardown | adapter leases in the world ledger; teardown report; no retained lease | `w5gate-retired-world-callbacks-are-rejected` |
| TEST-016 fault boundaries (rows 1 and 5) | `Validation` and `FirstLiveWrite` armed in the integrated world | `w5gate-prewrite-fault-keeps-the-old-assembly`, `w5gate-postwrite-fault-fail-stops-the-world` |
| TEST-017 checkpoints and schema evolution | capture/restore through the generated catalog | `w5gate-checkpoint-from-the-committed-boundary`, `w5gate-restore-into-a-new-session` |
| TEST-018 Unity worlds, bootstrap and Play Mode | the whole gate runs in a real player mode and a real EditMode assembly | `-probeW5Gate`, `GameCore.W5Gate.Tests` |
| TEST-019 engine adapters and single state authority | adapters bound to the restored world; foreign work refused | `w5gate-adapters-bind-to-the-restored-world` |
| TEST-022 ordering and determinism | canonical digest over the observation table; the capture is byte-deterministic through the generated serializers | both digest steps |

## 5. Merge reconciliations (the brief's list, each recorded)

1. **Probe host (`ProbeArguments.cs`, `ProbeRunner.cs`).** Both conflict sites were resolved by keeping **every**
   probe mode: one flag, one property, one `Parse` branch, one `IsProbeInvocation` term and one ctor argument per
   mode. The nested report-identity ternary chain (which conflicted on every merge) was replaced by one
   `CreateReport` helper with one independent `if` per mode, so adding a mode is a block rather than a reshuffle;
   `Named(mode, task)` supplies the identity. The resulting shape is what the release-preparation script strips.
2. **`dotnet/GameCore.sln`** merged cleanly with unique GUIDs (checked: 25 projects, 25 unique project GUIDs, no
   duplicate).
3. **`unity/GameCore.Validation/Packages/manifest.json` and `packages-lock.json`** merged cleanly; the qualification
   marker package the fault latch needs is present, the `testables` list covers every testable package, and nothing
   else references the marker.
4. **`inventory.{md,json}`** merged without conflict and now holds the union of the evidence-backed promotions:
   GC-018's four promoted rows (applied on the build host) and GC-019's `gc019` deltas. GC-017 deliberately made no
   promotion, so there was nothing to union from it. This task adds proposals only (§7).
5. **Shared kernel files** touched by several tasks were reconciled as follows: the world host, registry
   (`TryExpose`/`TryCreateUnexposed`), committed-boundary reader, fault latches in dispatch/publisher and the
   adapters-vs-lifecycle surfaces all merged without conflict, and the four edits above are the ones that needed
   hand reconciliation:
   * GC-018's boundary read → GC-016's lease (§1, commit 2);
   * GC-018's codec binding table → shared with the gate (commit 3);
   * the release clone's preparation → the merged probe host plus the gate's own files (commit 4);
   * the release checker's balance rule → literal-blind, because a GC-016 file landed after the rule was frozen.
6. **GC-017's latches stay compiled out of release.** `tools/check_release_fault_free.py --no-build` passes on this
   revision (it is in `static-checks.log`); the gate script also runs it with the compiled-assembly half, and builds
   and inspects a real marker-free release player.

## 6. Exact commands for the Linux build host

Everything runs from the repository root. Nothing below has been run.

### 6.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=5 tools/run_w5_gate.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` (trx into `artifacts/w5-gate/trx`); the Unity
resolve; EditMode (every testable package plus every gate assembly, including `GameCore.W5Gate.Tests`); PlayMode; the
card- and checkpoint-catalog code generation; `tools/unity/build_probe.sh` (which regenerates the probe catalog and
builds the StandaloneLinux64 IL2CPP qualification player with High stripping); the byte-identity check of all three
committed catalogs; every player probe `PROBE_RUNS` times; the release-surface check; the marker-free release player
(clone, build, inspect, family probes); the documentation validator. Every Unity invocation is wrapped in `timeout`
with one logged retry on a timeout; the delegating build keeps its own wrappers.

### 6.2 This task's suites alone

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics \
  -projectPath unity/GameCore.Validation -runTests -testPlatform EditMode \
  -testFilter GameCore.W5Gate.Tests \
  -testResults artifacts/w5-gate/unity/w5gate-editmode.xml \
  -logFile artifacts/w5-gate/unity/w5gate-editmode.log
```

Do not add `-quit` to a `-runTests` command (04 §10). `-testResults` with a relative path resolves against the Unity
**project** path, not the shell cwd (unlike `-logFile`), so pass an absolute path or copy the file out afterwards.

```sh
PROBE_RUNS=5 UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/w5-gate/toolchain \
  tools/unity/run_w5_gate_probe.sh
```

### 6.3 The pure half (this task's reconciliation)

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test  dotnet/tests/GameCore.Execution.Tests/GameCore.Execution.Tests.csproj -c Release \
  --filter "FullyQualifiedName~CheckpointCaptureTests" --logger trx \
  --results-directory artifacts/w5-gate/trx
```

The three new cases are `DeclaredQueueFactsThatContradictTheCopiedQueueRefuseTheCapture`,
`AConsistentDeclarationCapturesAndCarriesTheWorldsOwnQueueFacts` and
`AWorldThatDeclaresNoFactsSourceIsNotReadAsAnEmptyQueue`. `UnityCommittedBoundaryReader` and `WorldBoundaryFacts`
are Unity-only (they read real ECS storage and the world's plane), so no plain-dotnet test can construct them; the
gate's EditMode observation 3 is their proof.

## 7. Inventory: proposals only

`artifacts/gates/w4-generic-profile/inventory.{md,json}` gains a W5-GATE section that **promotes nothing**. The one
row this gate's probe would close is `P-049` (the integrated pre/post-mutation evidence GC-018 explicitly deferred to
the wave gate, plus GC-017's initial-definition half); `P-007`, `P-045`, `P-032`, `P-028`, `P-002` and `P-034` are
proposed unchanged with a delta that records what the gate adds; `P-053` keeps its promoted status and its new lease
path is recorded. Every proposal names the observation that would carry it and the artifacts that must first exist.

## 8. Known gaps, assumptions and doc ambiguities

1. **Nothing has been compiled or executed here.** The highest-risk items, in the order a compiler would find them:
   (a) `W5GateScenario.cs` is ~2 100 lines of hand-written code across four modules and two family contracts, audited
   in full by three independent read-only passes against every declaration (§9) — eleven defects were found and
   fixed, but only a compiler settles it; (b) the fault steps depend on `GAMECORE_FAULT_INJECTION` reaching the probe
   host assembly, which it does only because the runtime asmdef's `versionDefines` entry is applied to the validation
   project's compilation; (c) the restored world's provider installation must be live in its callback gate before the
   gate's asset lease can complete, which the restore's own replay establishes (asserted, not assumed: the step
   reports `tokenState` and fails with it if the installation is not live).
2. **The gate's fault pair deliberately shares one world and one publication series.** A prewrite refusal leaves the
   composition publication adopted-and-pending (P-006 forbids publishing one number twice), so `PublishDerived` cannot
   answer it. The postwrite step therefore builds its plan from the *module's own proposal* captured from the refused
   run (`DerivedAssemblyPipeline.Derive` + `AssemblyPlanner.Build` + `AssemblyPublisher.Publish`, exactly as
   `FaultScenario` and `W4GateScenario` do for publications the runner owns) rather than re-deriving. Nothing is
   re-implemented and no second interpretation of the composition is introduced; recorded because it differs from
   the most obvious reading of "retry the publication".
3. **The adapter frame is driven through `AdapterFrameRegistry`, not through a PlayerLoop installation.** The pump
   (GC-005/GC-019) is the only driver and installs the two calls; the gate calls the registry's two points directly
   so the EditMode suite can run it without a scene. The PlayMode half of the pump is GC-019's own suite.
4. **The restored world's adapters are constructed by the gate, not by the restore.** `Gc018FamilyRestoreBuilder`
   owns the world, its lane, its publisher and its stage runtime; the adapter frame is presentation/input/asset
   surface and is the gate's to bind. A production application would bind them the same way after exposing a restored
   world.
5. **`PostRetireCompletionCount` counts the arrival, not an installation.** A completion that reaches a retired table
   over an already-terminal lease reports `AlreadyTerminal`; the gate accepts either terminal outcome and asserts
   the facts that matter (nothing installed, nothing readable, the counter moved by exactly one, no outstanding load,
   no reserved bytes).
6. **The reflection scan is a property of the lease interface, not of every observation type.** It enumerates
   `ICommittedBoundaryLease`'s own members for `Entity`/`EntityManager`/`EntityQuery`/`World`/`DynamicBuffer<>`/
   `NativeArray<>`/`NativeSlice<>`/`NativeList<>`/`SystemHandle`/`JobHandle`/`GameObject`/`Transform`, including
   generic arguments. `WorldObservation` itself exposes `Snapshots` (which can publish) because it *is* the world's
   observation storage; a reader cannot reach that through a lease, which is what the requirement constrains.
7. **No row was promoted and no test was run**, per the brief: the inventory section is proposals, and every evidence
   path in it names an artifact the build host must first produce.
8. **The release clone is disposable.** `unity/GameCore.ReleaseCheck` is created by
   `tools/unity/prepare_gc017_release_project.py`, is gitignored, and the gate refuses to start if a previous clone is
   still present (remove it first) so a stale clone cannot be mistaken for a fresh one.
9. **Doc ambiguity: whether the release player's family probes belong to this gate.** The brief says "marker-free
   release build + release-surface inspection + family probes on it (reuse GC-017's script)"; this gate builds the
   player and runs the family probes that survive in a clone without the marker (`world`, `narrative`, `cards`,
   `gc018`, `gc019`), and deliberately does *not* run `-probeFaults`/`-probeW5Gate` there, because those two modes do
   not exist in that build. `RELEASE_BUILD=0` skips the build and says so.
10. **`O-22 RecoverWorld` is still not composed** — GC-027 owns it, over the restore path this gate proves.
11. **`P-007`'s long-run retention budget (TEST-023) and `P-045`'s cursor/dedup scale claims remain open**: the gate
    exercises them at one-world scale, not under memory pressure; GC-026/GC-021 own those rows.

## 9. Read-only audits and the defects they found

Three independent read-only passes (one per third of `W5GateScenario.cs`) checked every external call against its
declaration: exact name, namespace reachability through the file's `using` directives, arity and parameter order,
property existence, return type and nullable compatibility, plus C# 9 violations and ambiguous names. Findings, all
fixed before the final commit:

1. **Missing `using GameCore.Derivation;`** — `IDerivationValueSource` and `DerivationTarget` are declared only
   there, so both would have been `CS0246`. (Found by two of the three audits independently.)
2. **`new DerivationModeSwitchValidator(valueSource, TargetView())` passed the call's result** where the constructor
   takes a `Func<IReadOnlyList<DerivationTarget>>` (it re-reads the target view lazily on every check) — `CS1503`.
   Now a method group, as the sibling scenarios pass it.
3. **`CS0165` on three `out` variables declared inside a short-circuit `&&`** in the retired-callback region
   (`leaseId`, `requestCode`, `requestDetail`): the request call is now unconditional and its outcome is asserted,
   which is the shape the neighbouring region already used.
4. Four further self-found defects before the audit: a duplicated catalog-fingerprint parse, a nonsense
   mutation-isolation expression with a placeholder helper, a double snapshot acquisition that leaked a lease, and a
   `Bindings.Equals(null)` placeholder assertion. All removed.

The audits also confirmed the fault-latch resolution chain (`GAMECORE_FAULT_INJECTION` from the marker package via
the runtime asmdef's `versionDefines`, present in the validation manifest) rather than assuming it.
