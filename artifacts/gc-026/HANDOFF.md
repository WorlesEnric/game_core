# GC-026 HANDOFF — measure and tune propagation, execution and memory budgets (Wave 7)

Branch `gc-026` (worktree `/Users/yangcao/wkspace/gc-wt/gc-026`), based on `main` (`700c3a9`, the Wave 6 integration
gate).

**Status of every build/test command in this document: `NotRun (pending orchestrator build host)`.** This host is
macOS with no .NET SDK, no C# compiler and no Unity, so nothing has been compiled, imported, executed or built. What
did run here is interpreter-level only, and each item is named in section 6.

## 1. Summary

GC-026's objective is "Validate architecture costs on named hardware using explicit provisional targets." The change
set has four parts:

1. **A Unity-free fixture package** (`tests/GameCore.Benchmarks`, assembly `GameCore.Benchmarks`) carrying the
   deterministic generator of 08's declared fixture (1,000 scopes / 10,000 targets, three schema families, isolated
   and excluded branches, shared and unique contribution sets), the workload catalogue, the nearest-rank statistics,
   the raw per-sample record with its JSON and CSV writers, and 08's provisional budget table as comparable rows.
2. **A benchmark runner in the qualification player** (`-probeBenchmark`), which runs the fixture through the real
   engines and two real owned worlds, asserts the correctness gates, and writes one raw sample document per workload
   beside the probe result.
3. **A harness and a summarizer** (`tools/run_benchmarks.sh`, `tools/summarize_benchmarks.py`) producing
   `artifacts/performance/summary.md` with the hardware and build named, plus the budget decision record.
4. **One kernel-adjacent hot-path fix** that code inspection justified before any measurement (section 5).

## 2. Commits on this branch

| Commit | Contents |
|---|---|
| `5a89664` | The fixture package, its EditMode suite, the two dotnet shadow projects, the budget rows, and the `LiveTargetIndex` insertion fix. |
| `ee497d5` | The player mode, the scenario, the live world, the harness, the summarizer, the release-clone wiring and the artifacts. |
| `HEAD` | The nested-enum helper and the three missing `using` directives the gate source checker found. |

Nothing was pushed; no branch was switched; no history was rewritten.

## 3. Requirement → implementation → observation mapping

| Requirement / test | Where implemented | Where it is observed |
|---|---|---|
| P-007 references and leases | `BenchmarkLiveWorld` reports lease bytes and outstanding callbacks from the world's own owners; the spawn and apply workloads carry them per phase. | `benchmark-spawn-1000` gates, `benchmark-idle-command-world` memory detail |
| P-008 stable ordering | The fixture derives identities arithmetically per role; the catalogue selector re-orders a request into catalogue order; the digest is computed from the observation names. | `benchmark-digest`, `BenchmarkWorkloadTests.Select` |
| P-022 budgets | `BenchmarkFixture.SuggestedBudget` derives the fixture's limits from its own shape; the run records the reference guardrails beside the configured ones. | `benchmark-config`, `benchmark-fixture` |
| P-023 incrementality | The incremental engine is driven for every change workload, and `ControlNodesVisited` / `CandidatesMatched` deltas are reported. | `benchmark-steady-unchanged-10000-steps` gates, per-workload counter detail |
| P-026 explanation | `Explain` records are retained through the derivation the run drives (the fixture keeps `CollectExplanations` on). | `benchmark-fixture` detail |
| P-034 state authority | The mutation fixture writes an authoritative slot through the world's own owner path, reads it back, and asserts the derived binding rows did not move; the loaded-assembly kernel audit runs in the same step. | `benchmark-correctness-gates` (`authorityMirrorFixture=`) |
| P-043 bounded work | The spawn workload publishes under the already-active provider and reports the installed row count. | `benchmark-spawn-1000` |
| P-048 teardown/quarantine | Quarantine bytes and entries are reported as their own memory category, separate from leases, events and caches. | every live workload's `memory=` detail |
| P-052 diagnostics | Every workload is a named observation whose detail carries its numbers; a refusal carries the engine's own rejection. | the whole result document |
| P-060 evidence | Hardware, build, configuration, seed and shape are recorded in every raw document and in the summary header. | `benchmark-hardware`, `benchmark-config`, `artifacts/performance/summary.md` |
| TEST-008 incremental indexes, subtree movement | The reparent workload moves the declared 100-target subtree between two providers and back through the incremental engine. | `benchmark-reparent-100` |
| TEST-013 authority, direct writes, requests | The authority mutation fixture above. | `benchmark-correctness-gates` |
| TEST-023 performance, bounded memory, architecture regressions | The generated 1,000/10,000 fixture, the four update sizes, the mode switch, the spawn, the inactivity comparison, the 1,000 lifecycle cycles, the two live windows and the memory split. | every `benchmark-*` observation |

**08's budget rows**, each asserted against the measured value by the summarizer (verdicts, not prose):
`budget.execution-p95`, `budget.execution-managed-bytes`, `budget.unchanged-control-nodes`,
`budget.unchanged-service-lookups`, `budget.apply-pause-p95`, `budget.whole-world-preparation-p95`,
`budget.spawn-baseline` (report-only), `budget.lifecycle-plateau` (report-only), `budget.idle-steps`,
`budget.idle-stage-updates`.

**The correctness gates 08 calls out**, all asserted rather than measured: zero stable control-tree scans
(`benchmark-steady-unchanged-10000-steps` → `zero-stable-control-tree-scans`), zero string service lookups
(`zero-string-service-lookups`), an idle command-driven world advancing zero steps
(`idle-command-world-advances-zero-steps`) and zero simulation-stage updates
(`idle-command-world-does-zero-control-work`), and no duplicated authoritative state
(`benchmark-correctness-gates`). Each zero is paired with a positive control — `instrumentedKernelCountersMoved`
on `benchmark-config` and `positiveControlInstrumentedKernelCountersMoved` on `benchmark-correctness-gates` — so a
build whose counters were compiled out cannot report a perfect score.

## 4. Files created

### The fixture package (`tests/GameCore.Benchmarks`, assembly `GameCore.Benchmarks`, Unity-free)

| File | Contents |
|---|---|
| `package.json`, `Runtime/GameCore.Benchmarks.asmdef` | The local Unity package and its engine-free runtime assembly. |
| `Runtime/BenchmarkWorkload.cs` | `BenchmarkWorkloadKind`, `BenchmarkWorkload`, `BenchmarkWorkloads` (the eleven declared ids, their defaults, and `Select`). |
| `Runtime/PerformanceBudgets.cs` | `PerformanceBudget`, `BenchmarkBudgetUnit`, `BenchmarkBudgetVerdict`, `BenchmarkMetrics`, `PerformanceBudgets` (08's ten rows with their reference sentences). |
| `Runtime/BenchmarkSamples.cs` | `BenchmarkPhase`, `BenchmarkThreadCoverage`, `BenchmarkSample`, `BenchmarkAllocationTracker`, `BenchmarkMemoryCategories`, `BenchmarkGateResult`, `BenchmarkRunDocument`. |
| `Runtime/BenchmarkStatistics.cs` | `BenchmarkDistribution`, `BenchmarkStatistics` (the one nearest-rank definition). |
| `Runtime/BenchmarkFixture.cs` | `BenchmarkScale`, `BenchmarkIds`, `BenchmarkNames`, `BenchmarkFixture`, `BenchmarkFixtureGenerator`, and the shape-derived `SuggestedBudget`. |
| `Runtime/BenchmarkSnapshotBuilder.cs` | `BenchmarkSnapshotBuilder`, `BenchmarkFixtureVariants` (update installs, spawned targets, live-state identities). |
| `Runtime/BenchmarkDocumentWriter.cs` | `JsonFormat`/`CsvFormat`/`CounterColumns`/`WriteJson`/`WriteCsv`/`MetricsOf`/`PhaseOrder`/`SummaryMetrics`. |
| `Tests/*.cs`, `Tests/GameCore.Benchmarks.Tests.asmdef` | Seven EditMode suites: fixture shape, real derivations over the fixture, statistics, samples/allocation/memory, documents, budgets, catalogue. |
| `README.md` | The package's own documentation. |

### The player half

| File | Contents |
|---|---|
| `unity/.../Runtime/ProbeBenchmark.cs` | The `-probeBenchmark` mode: its own `-name=value` flag parser (14 flags, each validated) and the raw-artifact writer. |
| `unity/.../Runtime/BenchmarkScenario.cs` | `BenchmarkOptions`, `BenchmarkStep`, `BenchmarkScenarioResult`, `BenchmarkScenario` (the pure half, the live half, the gates, and 08's warmup rule). |
| `unity/.../Runtime/BenchmarkLiveWorld.cs` | `LiveWorldFailure`, `BenchmarkLiveWorld`, `BenchmarkLiveFamily`: the real narrative world at the recorded live scale. |

### Tooling and artifacts

| File | Contents |
|---|---|
| `tools/run_benchmarks.sh` | The player harness: `BENCH_RUNS` runs into per-run directories, strict validation, crash detection, `invocation.txt` and `environment.txt`, then the summarizer. |
| `tools/summarize_benchmarks.py` | The summarizer: pooled nearest-rank percentiles, the counter/memory/gate/budget tables, the decisions template (never overwritten), `--json`, `--expect-miss`, `--self-test` (57 checks). |
| `artifacts/performance/BUDGET_DECISIONS.md` | The decision record template: one section per budget row, every unmeasured field marked `NotRun`, plus the P-022 correctness bounds that a performance revision may never move. |
| `artifacts/performance/README.md` | The method document: each workload and the part of 08 it serves, the commands, the wall-clock cost of the defaults, the percentile definition, the memory categories, the gates and the positive control, and what is **not** claimed. |

### Modified (shared surfaces — recorded as the brief requires)

| File | Change | Why it is safe |
|---|---|---|
| `Packages/com.gamecore.unity.runtime/Runtime/Integration/LiveTargetIndex.cs` | Register by binary-search insertion instead of append-and-sort; retire by binary search too. | See section 5. The produced canonical order is identical. |
| `tools/check_game_core_csharp.py` | `tests/GameCore.Benchmarks` and `dotnet/src/GameCore.Benchmarks` added to `TARGETS` and to `engine_free`. | Additive path lists; the checker now covers the new C# and enforces its engine-freedom. |
| `tools/check_release_clone.py` | The benchmark's types joined `REMOVED_TYPES`, its mode joined `REMOVED_MODES`, and `GameCore.Benchmarks` is checked for reachability beside `GameCore.Replay`. | Additive; keeps the clone's invariants asserted on the clone. |
| `tools/unity/prepare_gc017_release_project.py` | The benchmark's three runtime files joined the strip list, `com.gamecore.benchmarks` joined the manifest removal list, its mode wiring joined `ARG_NEEDLES` and the mode table, and `GameCore.Benchmarks` leaves the probe-host asmdef. | Follows the established per-mode pattern; the clone check passes (section 6). |
| `unity/GameCore.Validation/Packages/manifest.json` | `com.gamecore.benchmarks` added as a dependency and a testable. | Additive entry in the established local-package form. |
| `unity/.../Runtime/GameCore.Validation.ProbeHost.asmdef` | `GameCore.Benchmarks` added to `references`. | One entry; the scenario needs the fixture assembly. |
| `unity/.../Runtime/ProbeArguments.cs`, `ProbeRunner.cs` | The `-probeBenchmark` flag: one const, parameter, assignment, local, parse branch, property, invocation term, identity branch and dispatch arm. | One addition per list, following the documented pattern. |

## 5. The optimization this change makes (and why it is justified from inspection)

`LiveTargetIndex.TryRegister` appended to `ordered` and then called `ordered.Sort(...)` on **every** registration.
That is O(n² log n) over registrations: invisible at the few-target scales the earlier gates used and dominant at the
10,000-target scale TEST-023 asks for. It is a scaling defect in a registered index, not a style preference, and the
fix is a binary-search insert that produces exactly the same order — the list is sorted before the insert and the
index is the first element that is not strictly smaller. `TryRetire` had the same shape (a linear scan) and now uses
the same search.

**Which tests guard it.** `LiveTargetIndex` has no dedicated suite, so the guard is every consumer that reads the
canonical target order: GC-013's reparent and opt-in cases (`Gc013Scenario`), GC-018's checkpoint identity
comparison, GC-019's live target view, and the benchmark's own live world, which asserts the binding rows it
publishes and the exact eligible count. **This is a semantic-adjacent kernel change: the W2/W3/W4/W5/W6 gates that
exercise a live target set should be rerun**, and I am flagging it for the orchestrator rather than assuming it.

No other optimization was applied. Nothing else was changed on the basis of "it looked slow", and no number in this
change is claimed as measured.

## 6. Exact commands for the Linux build host

All `NotRun (pending orchestrator build host)`.

### 6.1 The pure half (plain dotnet)

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --filter "FullyQualifiedName~GameCore.Benchmarks.Tests"
```

The suite drives the real derivation engine over the declared 10,000-target fixture, so it takes seconds rather than
milliseconds; it measures no wall clock, so it is deterministic on a loaded host.

### 6.2 The benchmark (player, the expensive one)

```sh
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/unity/build_probe.sh
BENCH_RUNS=5 tools/run_benchmarks.sh
```

`tools/run_benchmarks.sh` defaults to `ARTIFACTS=artifacts/performance`, `BENCH_RUNS=5`, `BENCH_WARMUP=30`,
`BENCH_DURATION=120`, and writes `artifacts/performance/raw/run<N>/`. **Wall-clock cost of the declared defaults:**
five runs of the whole catalogue, where the three steady workloads alone are 3 × 5 × (30 s + 120 s) = 37.5 min, plus
each change workload's warmup and its declared repetitions (28,000 repetitions across the catalogue per five runs).
Budget an hour or more. A single-workload smoke run is much cheaper:

```sh
BENCH_RUNS=1 BENCH_WARMUP=1 BENCH_DURATION=2 BENCH_REPETITIONS=5 \
  BENCH_WORKLOADS=idle-command-world,update-size-1 tools/run_benchmarks.sh
```

### 6.3 Reloading the raw evidence without re-running

```sh
python3 tools/summarize_benchmarks.py --raw artifacts/performance/raw \
  --out artifacts/performance/summary.md \
  --decisions artifacts/performance/BUDGET_DECISIONS.md \
  --machine myubuntu --json artifacts/performance/summarize.json
python3 tools/summarize_benchmarks.py --self-test
```

## 7. Decisions where the documents were ambiguous

1. **"Provisional provisional": which of 08's numbers are targets and which are quotas.** 08's budget table is
   explicitly "initial engineering targets", while P-022's candidate/contribution/target/byte counts are protocol
   quotas. I encoded 08's table as `PerformanceBudgets` (comparable rows with a verdict) and left P-022's quotas to
   `PropagationBudget`, which the derivation engine enforces itself; `artifacts/performance/BUDGET_DECISIONS.md`
   states that a performance revision may never move the latter. The fixture's configured budget is derived from its
   own shape and the reference guardrails are recorded beside it, so no reference quota is silently reinterpreted.
2. **What "apply pause" measures.** 08 asks for "Apply pause p95 at most 2 ms" and separately for the fencing time.
   I report the publisher's own fenced apply window (`apply-us`/`apply-samples`) as the apply phase, the execution
   driver's job-wait fence (`job-wait-us`) as the wait phase, and `endToEnd - apply - wait` as prepare — so the
   mandatory safety-boundary wait is a separate reported metric rather than hidden inside the apply number.
3. **Update sizes at any scale.** See the header of `BenchmarkFixture.cs`: the three update sizes are selected by
   declared tag so the *affected* count is exact at any scale, while the candidate domain is NOT narrowed and is
   reported (`candidates-matched`, `control-nodes-visited`) rather than implied to equal the affected count.
4. **A missing measurement is never a pass.** `BenchmarkBudgetVerdict` has `NotMeasured` as a distinct value and
   `Verdict(double.NaN)` returns it, so a row the run did not produce cannot read as `WithinTarget`.
5. **The inactivity comparison's cost.** It repeats the workload's own declared repetition count at EACH scale, so
   its cost is twice the declaration. I removed a cap I had first written: a cap would have executed fewer
   repetitions than the document recorded, which is the silent truncation 08 forbids.
6. **08's warmup rule.** Every workload warms up for the declared wall clock. A repeated change's warmup is sampled
   cycle by cycle (tens of samples); a steady window's is not (a 30-second warmup at one sample per frame would add
   hundreds of thousands of entries to an artifact whose purpose is re-analysis), so it is reported as
   `warmupMicroseconds` plus a cycle count, and the measured window's baseline is taken after it.
7. **The thread-complete allocation reading.** `BenchmarkAllocationTracker` cannot read another thread's allocation,
   so it records which threads actually reported and downgrades its own coverage claim when fewer did. The
   steady-execution workload declares one thread and has complete coverage; the live worlds report
   `managedAllocationIsThreadComplete=false` explicitly rather than claiming a zero they did not observe.

## 8. Known gaps, assumptions and risks

- **Nothing was run.** Every number this change will produce is unmeasured, and the four provisional misses 08
  expects may well be real misses. `BUDGET_DECISIONS.md` is the record to fill in from the build host's summary.
- **The derivation budget may be too tight or too loose for the fixture.** `BenchmarkFixture.SuggestedBudget` is
  derived from the fixture's shape (installs × targets with headroom); if the engine reports `BudgetExceeded` on the
  build host, the property is what to fix, and the scenario's `benchmark-fixture` observation carries the engine's
  own rejection text and counter description to make that diagnosis one run.
- **The live world's scale is a declared configuration, not a measurement.** `BenchmarkScale.LiveScopes`/
  `LiveTargets` default to the pure scale (1,000/10,000), so the live half is intended to be the same diagnostic
  load; whether a 10,000-entity world builds and publishes in reasonable time on the build host is unknown until it
  runs.
- **`DerivationModeSwitchValidator` is wired into the live world** exactly as the GC-013 scenario wires it, so a mode
  switch in the live half is validated before it is staged. No live workload currently drives a mode switch (the
  mode-switch workload is pure), so that path is exercised by construction rather than by a workload.
- **`BenchmarkFixtureVariants.SpawnTargets` reuses the live-state identity band** (`2000000 +`). The pure spawn
  workload and the live spawn workload therefore use the same id range, but never in the same world or the same
  snapshot, so no collision is reachable. It is a deliberate economy, and it is the kind of thing that would break
  if a future workload combined them — noted here rather than left as a trap.
- **A checker false positive.** `tools/check_gate_sources.py` reports `BenchmarkIds.Role.Scope` as unresolved member
  access on the test file `BenchmarkFixtureTests.cs`, which deliberately exercises the nested `Role` enum to assert
  that roles never collide. The checker indexes top-level members only, so a nested type is outside its model. The
  three runtime files report `unresolved: none`, `unreachable: none`, `ambiguous: none` after the fixes in the third
  commit.
- **The release clone strips the benchmark.** `com.gamecore.benchmarks` leaves the manifest, the three runtime files
  and the mode wiring are removed, and `GameCore.Benchmarks` leaves the probe-host asmdef. `check_release_clone.py`
  reports `clone is clean` with `GameCore.Benchmarks reachable: False` (verified here at the interpreter level; not a
  build).

## 9. Inventory: proposals only

Per the brief, no row is promoted here. Proposed promotions for the build host to make after running on named
hardware: `TEST-023`, `P-022`, `P-023`, `P-060` for GC-026, each from an archived passing `-probeBenchmark` run plus
`artifacts/performance/summary.md`.

## 10. What ran on this host

Interpreter-level only, and none of it is a build or a test result:

| Command | Result |
|---|---|
| `python3 tools/check_game_core_csharp.py` | `checked 559 C# file(s)` / `ok` |
| `python3 tools/validate_game_core_docs.py --self-test` | 9 isolated fixtures passed |
| `python3 tools/validate_game_core_docs.py` | 14 documents validated |
| `python3 tools/check_gate_sources.py --file <each of the three runtime files>` | `unresolved: none`, `unreachable: none`, `ambiguous: none` |
| `python3 tools/check_release_telemetry_free.py --no-build` | `Pass`, no problems |
| `python3 tools/unity/prepare_gc017_release_project.py` then `check_release_clone.py` | `VERDICT: clone is clean` |
| `bash -n tools/run_benchmarks.sh` | exit 0 |
| `python3 -m py_compile` over the summarizer and the three edited tools | exit 0 |
| `python3 tools/summarize_benchmarks.py --self-test` | `self-test: 57/57 checks passed` |
| JSON validation of the manifest, the asmdefs and the package manifests | valid |
