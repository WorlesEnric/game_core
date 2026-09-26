# W7-GATE HANDOFF — Wave 7 integration gate (merged-revision join of GC-024..GC-027)

Branch `w7-gate` = `main` + GC-025, with `origin/gc-027` and then `origin/gc-026` merged into it here and reconciled
by hand. **GC-024 is NOT on this revision**: its branch is still on the build host, and §7 records exactly what its
merge must change (one appended observation group in `W7GateScenario`, plus its own package in the solution and the
manifest — nothing else).

**Status of every build/test/player command in this document: `NotRun (pending orchestrator build host)`.** This host
has no Unity, no .NET SDK, no Mono and no C# compiler, so nothing in this change set has been compiled, imported,
executed or built here. What did run is interpreter-level only and is recorded verbatim in
`artifacts/w7-gate/static-checks.log`: the host-side C# checker (591 files), the gate-source invariant checker for the
whole repository and for this gate's own change set, the frozen-table/digest/probe-step cross-checks for both the
Wave 6 literals and this gate's own W7 table, the contract-surface parity check, the four committed catalogs and the
reachability/bake mirrors, the budget-record checker, the link.xml checker in both directions, the release-clone
preparation **run for real** followed by the clone's own checker and then deletion, `bash -n` over every shell script
this gate owns, `py_compile` over every Python tool it touches, the `.meta` generator (idempotent: 0 creations on the
second run) and the documentation validator (self-test + full). None of those is a build or a test result.

## 1. Summary

The Wave 7 exit gate, verbatim from `docs/game-core/09-implementation-guide.md`:

> "All reference transition tables and cross-template flow pass; complete IL2CPP/headless catalog coverage runs;
> faulted checkpoint/outbox recovery passes; benchmark data and budget decisions are recorded. Production fixes
> require affected gates rerun on the new revision."

Wave 7 is an **integration** gate: it owns no kernel behaviour and adds no mechanism. Its subject is the JOIN of the
Wave 7 tasks on one merged revision — which is also why the last sentence matters, because each task's evidence was
taken on the W6 revision and merging them changed shared kernel files (GC-026 changed the derivation indexes;
GC-027 changed the checkpoint/restore publication path). So every observation this gate records is a **re-run**,
through the owning task's own runner, of that task's own acceptance sequence.

One runner (`W7GateScenario`) records thirteen observations and one digest step; one player mode (`-probeW7Gate`)
reports them; one EditMode assembly (`GameCore.W7Gate.Tests`) asserts them; one script (`tools/run_w7_gate.sh`) drives
the whole thing. The groups are:

1. **GC-025's own coverage sequence**, re-run over the merged kernel (`CatalogCoverageScenario.Run()`, the same
   sequence `-probeCatalogCoverage` drives), in the qualification player and again in the marker-free release player.
2. **GC-012/TEST-008's incremental-vs-clean equivalence**, re-established at the declared 10,000-target scale over the
   recorded seed series, for the four edit kinds GC-026's index change could have broken (install-only mount —
   the path `BuildIncremental` was added for — subtree reparent, 1,000-target spawn, retraction of those targets).
   Each edited snapshot is derived BOTH incrementally from the previous accepted publication and cleanly from
   scratch, and the two must agree on the canonical result hash and the effective assembly count.
3. **Two merge invariants**, because they are the failure modes a four-way merge really has: every probe mode must
   still parse out of the merged `ProbeArguments` exactly one at a time (a dropped mode removes a gate from the
   release process silently), and the ten recorded budget rows must still be the declared ten.
4. **GC-027's recovery sequence**, re-run for all three genres through GC-027's own runner, with the two fault points
   the gate sentence names recorded as their own observations: **postwrite-apply** and **restart**.
5. **A NEW release-kept mode, `-probeRecoverySmoke`**, which drives the PRODUCTION `WorldRecovery.Recover`/`Restart`
   with a real `FileCheckpointStore` and **no fault latches** in the marker-free release player. This is the backlog
   item the task asked for, and it is the first exercise of O-22 by a shipping-shaped build in this repository.

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `Merge GC-027 into W7 gate` | GC-027, with the probe host's conflicted regions, the release-clone strip lists and the constructor needle reconciled by hand (§4.1, §4.2). |
| `Merge GC-026 into W7 gate` | GC-026, same shape: every mode kept, the solution file unioned with unique GUIDs, the manifest's testables unioned, the clone strip lists unioned (§4.1–§4.4). |
| `W7-GATE: the gate scenario, its probe and the recovery smoke` | `W7GateScenario.cs`, `ProbeW7Gate.cs`, `ProbeRecoverySmoke.cs`, the two new modes' wiring, and the `PostwriteApplyObservation`/`RestartObservation` constants GC-027's table is now built from. |
| `W7-GATE: the EditMode suite, the two harnesses and the gate script` | `Tests/W7Gate/**`, `tools/unity/run_w7_gate_probe.sh`, `tools/unity/run_recovery_smoke_probe.sh`, `tools/run_w7_gate.sh`, `tools/check_budget_record.py`. |
| `W7-GATE: reconcile the release tooling with the merged revision` | `tools/unity/prepare_gc017_release_project.py`, `tools/check_release_clone.py`, `tools/check_release_gate_free.py`, `tools/check_link_xml.py`, `tools/check_gate_sources.py`, and the W6 wrapper's leak-log path fix. |
| `W7-GATE: the handoff, the static checks and the inventory proposals` | `artifacts/w7-gate/**`, `artifacts/gates/w4-generic-profile/inventory.{md,json}`. |

## 3. Files created

### The gate (every `.cs` file carries a Unity `.meta` with a unique GUID, generated by `tools/make_unity_metas.py`)

| File | Contents |
|---|---|
| `unity/.../Runtime/W7GateScenario.cs` | `W7GateStep`, `W7GateScenarioResult`, the frozen table (`ProcessObservationNames` + `FamilyObservationNames` × three families), `ObservationNames()`, `ExpectedDigest()`, `Run()`, the four process observations (`CatalogCoverageStep`, `IncrementalEquivalenceStep`, `ProbeModeDispatchStep`, `BudgetRowsStep`), the per-family recovery group, and the falsifiable digest step. |
| `unity/.../Runtime/ProbeW7Gate.cs` | The `-probeW7Gate` player mode: the quoted digest literal `44742f…`, `ExpectedObservations`, one `ProbeOutcome` per recorded step, and the `w7-frozen-digest-agrees-with-the-table` step that compares the build's own table digest against the quoted literal. |
| `unity/.../Runtime/ProbeRecoverySmoke.cs` | The `-probeRecoverySmoke` release mode: eight observations per family (real-file capture and publication, envelope reload, the two refusals, recover, restart, the recovered world's own state, teardown) plus one digest step. Needs `-probeResult` (it is the directory the checkpoint files are published into). |
| `unity/.../Tests/W7Gate/GameCore.W7Gate.Tests.asmdef` | Editor-only EditMode assembly (byte-identical to the W6Gate asmdef apart from the name/rootNamespace). |
| `unity/.../Tests/W7Gate/W7GateIntegrationTests.cs` | One `[Test]` per frozen observation (each reading its name out of the table, never writing it), the digest test that recomputes the literal from the table alone and compares it with the probe's constant, the "recorded run is the table plus its digest step" test, the probe/suite table-agreement test, and a `[TearDown]` asserting the world registry is back at zero. |
| `tools/unity/run_w7_gate_probe.sh` | The `-probeW7Gate` harness: `PROBE_RUNS` runs, strict JSON, all fifteen steps by name, the claim clauses, the frozen literal, and native leak attribution over every retained run log. |
| `tools/unity/run_recovery_smoke_probe.sh` | The `-probeRecoverySmoke` harness: seventeen steps by name, the frozen smoke literal, the claim clauses its own details must carry, and the assertion that the two real `.checkpoint` files exist on disk. |
| `tools/run_w7_gate.sh` | The gate (§6). |
| `tools/check_budget_record.py` | The document half of "benchmark data and budget decisions are recorded": parses the ten row ids out of `PerformanceBudgets.cs`, requires one `## budget.<id>` section per id with a Decision and Evidence line, requires every Evidence path to exist, requires the project-owner deferral sentence, and refuses a section that claims a full-duration measurement while deferred. |
| `artifacts/w7-gate/static-checks.log`, `artifacts/w7-gate/HANDOFF.md` | The verbatim host-side checks and this document. |

### Modified (shared surfaces)

| File | Change | Why it is safe |
|---|---|---|
| `unity/.../Runtime/ProbeArguments.cs` | Two constants, two constructor parameters, two assignments, two locals, two properties, two `IsProbeInvocation` terms, two parse branches and two constructor arguments. Plus **one repair**: the `/// <summary>` opener above the `Benchmark` property, which the GC-026 conflict resolution had dropped. | The additive block every earlier task added for its own mode; every earlier mode's branch is untouched. |
| `unity/.../Runtime/ProbeRunner.cs` | Two dispatch arms, two report-identity arms, and `ProbeRecoverySmoke.Run(report, arguments.ResultPath)` (the mode needs the result directory). | Additive. |
| `unity/.../Runtime/Gc027Scenario.cs` | The two fault-point observation names GC-027's own definition names are now `const`s (`PostwriteApplyObservation`, `RestartObservation`) and `ObservationNames` is built from them, so this gate addresses them without repeating a literal and a rename moves both together. | The resulting table is byte-identical to the previous one (§4.5). |
| `tools/unity/prepare_gc017_release_project.py` | The W7 gate's two files join the removal list, the W7 mode joins the needles and the strip list, and the constructor needle carries `w7Gate`/`recoverySmoke`. | Verified by running the script for real (§8). |
| `tools/check_release_clone.py` | The W7 types, the `RecoverySmoke` kept-mode entry, a boundary-aware removed-mode test and a `wired` test that now requires the assignment. | Verified in both directions (§8). |
| `tools/check_release_gate_free.py` | A `w7-gate` marker group, and the single kept-mode anchor becomes a tuple of three. | The release-surface check must now prove three kept modes, which is stronger. |
| `tools/check_link_xml.py` | Permits exactly one kernel-assembly type-level preserve (GC-027's `WorldRecovery`) and requires the permitted fixture preserves to carry the attribute. | A GC-025 tool that failed on the merged revision; the exception is one assembly and one exact type list (§4.6). |
| `tools/check_gate_sources.py` | Recomputes and cross-checks this gate's frozen table (scenario, probe, suite, harness) in addition to the Wave 6 literals; runs only when the W7 files exist so a subset invocation stays valid. | Additive; the Wave 6 block is untouched. |
| `tools/unity/run_w6_gate_probe.sh` | Absolutizes `ARTIFACTS` before deriving the player log path. | The fix GC-026 asked for (§5). |
| `artifacts/gates/w4-generic-profile/inventory.{md,json}` | A `GC-024` section and a `W7-GATE` section: **proposals only**, no row promoted. | A row may only be promoted from an archived passing run. |

## 4. Merge reconciliations (each recorded)

1. **The probe host.** Both conflict sites in `ProbeArguments.cs` and one in `ProbeRunner.cs` were resolved by keeping
   **every** mode from all four tasks: one flag, one property, one `Parse` branch, one `IsProbeInvocation` term, one
   dispatch arm, one report-identity arm and one constructor argument per mode, in both branches and in the merged
   dispatch. The merged host now carries twenty-four modes.
2. **The release-clone preparation.** Both conflicts (the `ARG_NEEDLES` block and the constructor-call needle) were
   resolved as a union, and both replacement strings were **re-derived from the merged sources** rather than taken
   from either side, because either side's text no longer matches: the qualification-mode expression becomes
   `|| Gc013 || W4Gate || Gc018 || Gc019 || Traversal` and the constructor call becomes
   `w4Gate, gc018, gc019, traversal, catalogCoverage, recoverySmoke, resultPath)`. **Two modes are deliberately
   KEPT** and stay in both the signature and the call: `-probeCatalogCoverage` (GC-025's release coverage run — the
   release half of the coverage clause) and the new `-probeRecoverySmoke`. Everything else in the list removes only
   qualification-only modes.
3. **`dotnet/GameCore.sln`.** Merged by keeping both sides' project entries with their own GUIDs. The two Benchmark
   projects keep their own GUIDs and GC-026's `{1A2B3C4D-0033-…033}`/`{…0034-…034}` collisions with the recovery
   fixture projects were resolved by moving the Benchmark pair to `…0035`/`…0036` and adding their four configuration
   rows apiece: 34 `Project(` lines, 34 `EndProject`, 35 distinct GUIDs, 136 configuration rows, no duplicate GUID and
   no configuration row without a project (verified programmatically, §8).
4. **`manifest.json` / `packages-lock.json`.** The dependency and `testables` unions hold every package all four tasks
   added; the lock was taken as a union of both sides' entries. `unity/GameCore.Validation` now depends on and tests
   thirteen packages. The clone's lock is deleted by the preparer, as before.
5. **GC-027's observation table.** Turning the two fault-point names into constants changes no recorded name and no
   order: the table is the same twenty-one strings in the same sequence, so GC-027's three frozen digest literals are
   unchanged — which the `-probeRecovery` harness would fail on if it were not.
6. **`link.xml` and its checker.** GC-027's build report records that High managed stripping dropped
   `WorldRecovery` once its only caller (the qualification probe) left the clone, and that `Assets/link.xml` now
   preserves the production type. The checker that GC-025 owns and runs — `tools/check_link_xml.py` — was not updated,
   so on the merged revision it reported three problems and **GC-025's own gate could not pass**. It is reconciled
   here, and the reconciliation is deliberately narrow: one assembly, one exact type list, and the checker now also
   *requires* the permitted preserves to be present and to carry the attribute (it previously accepted an element with
   the attribute removed). All six negative directions were exercised (§8).
7. **The W6 gate probe wrapper's leak-log path** (GC-026's finding) is fixed: `ARTIFACTS` is absolutized before the log
   path is derived. Unity resolves a relative `-logFile` against the *player's* directory, while
   `tools/attribute_native_leaks.py` re-opens the same name against the *caller's*: with a relative root, runs 2..N
   were silently skipped by the `[[ -f … ]]` test and run 1 failed inside the attributor with "log not found".

## 5. The new release-kept mode, and why it is shipping-shaped

`-probeRecoverySmoke` exists because the gate's backlog item asks for a recovery smoke that survives in a marker-free
build. It references **no** `Gc027*`, `W5*`, `W6*`, `W7*`, benchmark, replay, fault-latch or
`GameCore.Unity.Runtime.Faults` type and carries no `#if GAMECORE_FAULT_INJECTION` region — verified by a scan of the
file and by the release-clone checker, which inspects the prepared clone for every removed type and member. It drives
only production seams: `CheckpointPublication.CaptureAndPublish`, `WorldRecovery.Recover`, `WorldRecovery.Restart`,
`FileCheckpointStore`, `UnityCommittedBoundaryReader` and GC-018's own restore builder.

**One documented deviation, and it is a real constraint of the production API.** The mode's "recover" step cannot use
a `TryCreate`-fresh world as its source and report `Created->Disposed`, because `UnityWorldHost.CreateCore` sets the
lifecycle to `Running` before `TryCreate`/`TryCreateUnexposed` returns, and `WorldRecovery.Recover` refuses any source
that is `Running`/`Paused` with `TooLate`. There is no API that returns a registered host to `Created`. So the second
source is created normally and then retired into the terminal non-live state a recovery actually reads through the
production driver seam (`createdHost.Driver.LatchFault(...)`, the same path `WorldHost` itself uses for a post-write
failure), and the step's detail reports `sourceLifecycle=Faulted->Disposed` plus a `sourceNote` explaining it. Every
other claim of that step is what the task asked for and is asserted verbatim.

## 6. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where observed (this change set) |
|---|---|---|
| P-001 genre independence | GC-027's runner re-run for three genres from each genre's own host; the budget/merge checks are genre-free | `narrative|cards|traversal/w7-recovery-*`, `w7/w7-probe-mode-dispatch-keeps-every-mode` |
| P-002 participants and authority | every world built through the genre's own request/registration/lane/publisher; the smoke drives the production composition | every observation; `release/probe-recovery-smoke.json` |
| P-004 / P-005 identities and handles | a source session distinct from every recovered incarnation; the smoke requires the disposed source to be unresolvable | `…/w7-recovery-restart-without-in-process-state`, `recovery-smoke-recovered-world-is-authoritative` (`oldSessionRefused=true`) |
| P-008 deterministic evidence | one digest over the frozen table, recomputed from the table alone by the suite and compared with the probe's quoted literal | `w7/w7-gate-digest`, `w7-frozen-digest-agrees-with-the-table`, `GameCore.W7Gate.Tests` |
| P-009 catalog roots and reachability | the coverage sequence re-run over the merged kernel, in both shapes | `w7/w7-catalog-coverage-repasses-on-the-merged-kernel` |
| P-022 bounded work / P-023 incremental equivalence | the 10,000-target equivalence group over the recorded seed series, four edit kinds, incremental vs clean | `w7/w7-incremental-derivation-matches-a-clean-derivation`, `artifacts/w7-gate/trx/` |
| P-030 / P-031 fail-stop and no partial exposure | GC-027's postwrite-apply observation re-run per genre | `*/w7-recovery-postwrite-apply-fault` |
| P-032 checkpoint round trip | the smoke's envelope reload and the recovered world's own live rows | `recovery-smoke-published-envelope-reloads-from-disk`, `recovery-smoke-recovered-world-is-authoritative` |
| P-035 world lifecycle / P-048 teardown | every world the gate and the smoke build is stopped and disposed, the registry asserted back at its baseline | `recovery-smoke-teardown-is-clean`, the suite's `[TearDown]` |
| P-045 durable delivery / P-049 recovery limits | a live source refused `TooLate`, a retired one `StaleHandle`, a restart from the store alone owing nothing | `recovery-smoke-refuses-a-live-source`, `…-refuses-a-stopped-source`, `…-restart-publishes-a-new-session` |
| P-053 serialization discipline | the published envelope reloads with the identical stored identity and no `.partial` artifact; the restore reports what it really rebuilt | `…published-envelope-reloads-from-disk`, `…recover-publishes-a-new-session` |
| P-058 headless reference | the coverage sequence's headless canonical step runs in the player | `w7/w7-catalog-coverage-repasses-on-the-merged-kernel` |
| P-060 evidence and release status | this gate's evidence set and the release-surface inspection over the union of markers | `artifacts/w7-gate/**`, `release-gate-surface.json` |
| TEST-001 / TEST-020 catalog coverage | GC-025's own sequence in both players | the same observation, plus `probe-catalog-coverage{,-release}.json` |
| TEST-008 / TEST-023 incremental equivalence and budgets | the equivalence group and the ten-row record assertion | `w7/w7-incremental-derivation-matches-a-clean-derivation`, `w7/w7-declared-budget-rows-are-the-recorded-ten`, `host/budget-record.json` |
| TEST-014 / TEST-016 faulted recovery and fault points | GC-027's sequence plus the two named fault points | `*/w7-recovery-sequence-repasses`, `*/w7-recovery-postwrite-apply-fault`, `*/w7-recovery-restart-without-in-process-state` |
| TEST-018 Unity worlds and Play Mode | the EditMode suite, the player mode and the host-side checks | `GameCore.W7Gate.Tests`, `-probeW7Gate` |

## 7. What the GC-024 merge must change (the gate is designed to slot it in)

GC-024 owns the reference-conformance tables and the combined narrative+cards world. When the orchestrator merges
`origin/gc-024`:

1. `W7GateScenario` gains **one** observation group: a `ConformanceObservationNames` array, one
   `AddConformanceSteps(steps)` call in `Run()` in the documented emission position, and the recomputed digest
   literal. `ObservationNames()` is the single frozen table, so the literal changes loudly and
   `tools/check_gate_sources.py` will recompute it from the table — a merge that widens or narrows the table without
   changing the literal fails that checker rather than passing quietly.
2. `ProbeW7Gate.ExpectedDigest` and `ProbeW7Gate.ExpectedObservations` follow the table (the latter is already
   `W7GateScenario.ObservationNames()`, so only the literal needs recomputing).
3. `run_w7_gate_probe.sh`'s `w7_steps` array gains that group's names, in emission order.
4. Nothing else: the gate's dotnet and Unity test invocations are **unfiltered** over every testable assembly, so
   GC-024's own suites participate as soon as its package is in the solution, the manifest's `testables` and the
   package references. `tools/run_w7_gate.sh` needs no edit for that.
5. The inventory's `GC-024 revision notes` section records what its arrival would promote; it promotes nothing today.

## 8. Exact commands for the Linux build host

Everything runs from the repository root. **Nothing below has been run.** The host-side checks this task DID run are in
`artifacts/w7-gate/static-checks.log`; they need no Unity and no SDK.

### 8.1 The whole gate (one command)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet \
  PROBE_RUNS=2 tools/run_w7_gate.sh
```

In order: `dotnet build` + `dotnet test dotnet/GameCore.sln -c Release` over **every** test project (trx into
`artifacts/w7-gate/trx`), then the two named equivalence suites a second time with their own trx; the host-side checks
(C# shape, gate sources with this gate's `--file` list, contract-surface parity, the four committed catalogs, the
catalog-emitter mirror, the reachability manifest, the baked coverage artifact, the fingerprint self-test, the
budget-record checker, `link.xml` for the qualification project, and `bash -n` over five scripts); the Unity resolve;
EditMode and PlayMode **unfiltered** over every testable package (every gate assembly W1..W7 and every task suite);
the five catalog codegens and the coverage bake followed by `git diff --exit-code` over the four generated trees; the
StandaloneLinux64 IL2CPP qualification player via `tools/unity/build_probe.sh`; **every** player probe twice (GC-001
positive and expected-negative, world dispatch, the W1..W6 gates, the three genre probes, GC-013, GC-017 faults,
GC-018/GC-019/GC-021, GC-022 lifecycle stress, GC-023 replay, GC-025 catalog coverage, GC-027 recovery and this
gate's `-probeW7Gate`); the release surface (the fault and telemetry source halves, the prepared clone and its own
checker, the release player build, `link.xml` for the clone, the latch/telemetry/gate-surface player inspections, then
the kept modes twice each — the family probes, `-probeCatalogCoverage` with `PROBE_SHAPE=release`, and
`-probeRecoverySmoke`); ONE short benchmark correctness diagnostic; and the documentation validator. Every Unity
invocation is wrapped in `timeout` with one logged retry on a timeout.

### 8.2 The pieces, if a single step needs re-running

```sh
# the plain-dotnet half
DOTNET=$HOME/.dotnet/dotnet $HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --no-build \
  --logger trx --results-directory artifacts/w7-gate/trx

# the two suites the gate names, by name
$HOME/.dotnet/dotnet test dotnet/tests/GameCore.Derivation.Tests -c Release --no-build \
  --filter "FullyQualifiedName~GameCore.Derivation.Tests.IncrementalAgreementTests|FullyQualifiedName~GameCore.Derivation.Tests.OracleAgreementTests"
$HOME/.dotnet/dotnet test dotnet/tests/GameCore.Benchmarks.Tests -c Release --no-build \
  --filter "FullyQualifiedName~BenchmarkFixtureDerivationTests"

# the host-side checks (no Unity, no SDK)
python3 tools/check_game_core_csharp.py
python3 tools/check_gate_sources.py --json artifacts/w7-gate/host/gate-sources.json
python3 tools/check_contract_surface_parity.py
python3 tools/check_budget_record.py --json artifacts/w7-gate/host/budget-record.json
python3 tools/check_link_xml.py --project unity/GameCore.Validation

# the qualification player and this gate's own mode
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_PROJECT=unity/GameCore.Validation \
  ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/build_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_w7_gate_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_catalog_coverage_probe.sh
PROBE_RUNS=2 ARTIFACTS=artifacts/w7-gate/toolchain tools/unity/run_recovery_probe.sh

# the release shape: prepare, check the clone, build, then the kept modes
python3 tools/unity/prepare_gc017_release_project.py
python3 tools/check_release_clone.py --clone unity/GameCore.ReleaseCheck
python3 tools/check_link_xml.py --project unity/GameCore.ReleaseCheck
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity UNITY_PROJECT=unity/GameCore.ReleaseCheck \
  ARTIFACTS=artifacts/w7-gate/release tools/unity/build_probe.sh
PROBE_PLAYER=unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64 UNITY_PROJECT=unity/GameCore.ReleaseCheck \
  ARTIFACTS=artifacts/w7-gate/release PROBE_RUNS=2 PROBE_SHAPE=release \
  tools/unity/run_catalog_coverage_probe.sh
PROBE_PLAYER=unity/GameCore.ReleaseCheck/Builds/Linux64/GameCoreProbe.x86_64 UNITY_PROJECT=unity/GameCore.ReleaseCheck \
  ARTIFACTS=artifacts/w7-gate/release PROBE_RUNS=2 tools/unity/run_recovery_smoke_probe.sh

# the ONE short benchmark correctness diagnostic (NOT the deferred full-duration catalogue)
BENCH_RUNS=1 BENCH_WARMUP=1 BENCH_DURATION=2 BENCH_REPETITIONS=5 BENCH_TIMEOUT=1800 \
  PROBE_PLAYER=unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 \
  UNITY_PROJECT=unity/GameCore.Validation ARTIFACTS=artifacts/w7-gate/benchmark tools/run_benchmarks.sh
```

### 8.3 The host-side checks that ran **here** (recorded verbatim in `artifacts/w7-gate/static-checks.log`)

`check_game_core_csharp.py` (591 files, ok); `check_gate_sources.py` for the repository and for this gate's own
`--file` list (both `VERDICT: ok`, with the W7 table resolved `harness=True probe=True suite=True`); the
contract-surface parity check; `verify_generated_catalog.py` for all four catalogs; the catalog-emitter mirror's
`--self-check`; the reachability manifest and bake `--check`s; the fingerprint tool's `--self-test`;
`check_budget_record.py` (`VERDICT: pass`, 10/10 rows); `check_link_xml.py` for the qualification project and for the
prepared clone, plus four planted-defect directions; the release-clone preparation **run for real** and then the clone
checker (clean) plus two planted-defect directions; `bash -n` over five scripts; `py_compile` over six tools; the
`.meta` generator (0 creations on the second run); the documentation validator's self-test and full run.

## 9. Known gaps, assumptions and doc ambiguities

1. **Nothing has been built or executed.** Every projected result in this document is a projection. The gate's own
   exit claim is therefore unproven until the build host runs §8.1.
2. **GC-024 is not on this revision.** The gate re-runs GC-025/GC-026/GC-027's sequences and its own merge invariants.
   "All reference transition tables and cross-template flow pass" is therefore **open** on this revision and §7 is the
   contract for closing it. The inventory's GC-024 section records that rather than implying coverage it does not have.
3. **The full-duration TEST-023 qualification is not run**, by project-owner decision. The gate asserts the ten
   recorded budget rows, the decision record's own consistency, and one short correctness diagnostic; it never claims
   a p95/p99 plateau. `tools/check_budget_record.py` is what makes that claim checkable rather than a promise.
4. **The recovery smoke's "second source" deviation** (§5) is a constraint of the production API, not a shortcut: a
   `TryCreate`-fresh world is already `Running`, and `Recover` refuses a live source by design. The step reports
   `sourceLifecycle=Faulted->Disposed` and explains itself in the detail string.
5. **`-probeRecoverySmoke` needs `-probeResult`.** It publishes real checkpoint files beside the structured result;
   without the argument it records one failing step instead of a verdict. The harness always passes it.
6. **Probe repetition and the checkpoint files.** `PROBE_RUNS=2` means each run writes over the same two
   `.checkpoint` paths (a replace, which is the point of the file store's temp+replace publication). The harness
   asserts the files exist after every run, and the mode's own step asserts `publishCount=1` per store instance, so a
   run that failed to publish cannot pass on an earlier run's file.
7. **The `link.xml` rule is now an allow-list of two assemblies** (§4.6). The doc's 04 §8 item 4 permits preservation
   for "entry points reached by Unity callbacks or native code"; the host-invoked `WorldRecovery` is read as that
   category because a marker-free clone has no managed caller for it. If the project owner reads that sentence more
   narrowly, the alternative is to keep a managed caller of `WorldRecovery` alive in the release player (this gate's
   `-probeRecoverySmoke` is exactly such a caller now — so a build host that wants to drop the `link.xml` exception
   can test it by removing the entry and re-running this gate; the smoke will fail if stripping drops the type).
8. **The W6 gate's own leak attribution** now works with a relative `ARTIFACTS`; nothing else about that harness
   changed, and `PROBE_RUNS` there is whatever the caller passes (this gate passes 2).
9. **Two merge defects were found and fixed here**, both of the class a merge introduces and a compiler would report
   late: the dropped `/// <summary>` above `Benchmark` in `ProbeArguments.cs` (§4.5 note in the commit table), and the
   duplicate/non-matching constructor needle the union left behind. Both are recorded because they are behaviour fixes
   on merged code rather than parts of this gate's own files.
10. **Doc ambiguity recorded:** the guide's Wave 7 sentence names "cross-template flow" and "all reference transition
    tables", which GC-024 owns; this gate reads the sentence as *the join runs on the merged revision and reruns the
    affected gates*, and states plainly that GC-024's half is not on this revision rather than substituting a
    narrower sequence for it. The 00-wins-over-05-wins-over-09 rule is not engaged: no normative statement was
    ambiguous, only a scope allocation between two tasks in the same wave.
