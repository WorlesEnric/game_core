# GC-026 — GameCore performance benchmark (08's reproducible method)

Status: **NotRun (pending orchestrator build host)**. Nothing in this directory has been measured: this host has no
Unity, no IL2CPP toolchain, no .NET SDK and no comparable machine, so no player build and no benchmark run has
executed. What *has* been executed here is the tooling's own falsification:
`python3 tools/summarize_benchmarks.py --self-test` (a pure-Python check of the summarizer, no Unity) plus
`bash -n tools/run_benchmarks.sh`. Neither is a measurement, and neither is a substitute for one. Every command below
is labelled `NotRun (pending orchestrator build host)`.

This directory is wave 7's answer to
[08 section 3](../../docs/game-core/08-validation-and-performance.md) — "Instrumentation and reproducible performance
method" and "Provisional budgets and failure decisions". The fixture and the runner live in
`tests/GameCore.Benchmarks/Runtime/` and in the validation project's `-probeBenchmark` mode; this directory holds the
harness that runs the player, the summarizer that turns raw samples into a verdict, and the human record of any
accepted target revision.

## Files

| File | What it is |
|---|---|
| `README.md` | this method document: the workloads, the commands, the cost, and what is *not* claimed |
| `BUDGET_DECISIONS.md` | the human record of the ten budget rows; created once and never overwritten by the tool |
| `summarize_benchmarks.py` | `tools/summarize_benchmarks.py` — pools the raw samples and writes `summary.md` |
| `run_benchmarks.sh` | `tools/run_benchmarks.sh` — runs the player `BENCH_RUNS` times, validates each run, then summarises |
| `raw/run<N>/` | where the harness writes each run (created by the harness; absent here because nothing has run) |
| `raw/run<N>/<workload>.samples.json` | the player's raw per-sample document, one per workload per run |
| `raw/run<N>/<workload>.samples.csv` | the same samples as a wide matrix, one column per schema counter |
| `raw/run<N>/probe-benchmark.json` | the structured probe result: task, mode, result, exit code, build/config facts, steps |
| `raw/run<N>/player-benchmark.log` | the player log for that run |
| `raw/run<N>/invocation.txt` | the exact invocation and the resolved environment of that run |
| `environment.txt` | host facts written by the harness: `uname -a`, `nproc`, CPU model, date, player sha256 |
| `summary.md` | written by the summarizer: hardware, config, per-phase distributions, gates, memory, budget verdicts |
| `summarize.json` | the same verdicts machine-readably (budget rows, gate verdicts, evidence list) |

The two tool paths are `tools/run_benchmarks.sh` and `tools/summarize_benchmarks.py` in the repository; they are listed
here because this directory is where their artifacts land.

## The workloads, and which part of 08 each one serves

The catalogue is `tests/GameCore.Benchmarks/Runtime/BenchmarkWorkload.cs`; the raw document, not this table, is the
source of truth for what actually ran. A **steady** workload is measured for a wall-clock window (declared default
30 s warmup + 120 s measurement); a **change** workload is measured over a repetition count. 08 asks for both, and
conflating them would hide a miss.

| # | workload id | kind | declared default | which part of 08 it serves |
|---|---|---|---|---|
| 1 | `idle-command-world` | Steady | 120 s window | "For idle command-driven cases report zero steps and control-plane wake counts rather than dividing by a fabricated tick count." The budget rows are `steps-advanced == 0` and `stage-samples == 0`. |
| 2 | `steady-unchanged-10000-steps` | Steady | 120 s window | "Add a regression test that leaves the composition unchanged for 10,000 simulation steps." Zero `control-nodes-visited` and zero `service-string-lookups` are the claim. |
| 3 | `steady-execution-10000-targets` | Steady | 120 s window | The 1,000-scope / 10,000-target fixture "with 1,000 active commands per fixed step", and the provisional row *Core execution p95 at most 4 ms* plus *0 managed bytes per logical step*. |
| 4 | `update-size-1` | Change | 1000 reps | 08's first update size: "1 target". |
| 5 | `update-size-100` | Change | 1000 reps | 08's second update size: "100 targets", and the provisional row *Apply pause p95 at most 2 ms*. |
| 6 | `update-size-10000` | Change | 200 reps | 08's third update size: "10,000 targets", and the provisional row *Preparation p95 at most 100 ms* for whole-world derivation. |
| 7 | `whole-world-mode-switch` | Change | 200 reps | 08's fourth update size: "a whole-world mode switch" (P-013/P-014). |
| 8 | `spawn-1000` | Change | 200 reps | "Add 1,000-target spawns under an already-active provider" (P-024). Report-only baseline. |
| 9 | `reparent-100` | Change | 1000 reps | "reparent a 100-target subtree between two providers" (P-025). |
| 10 | `inactive-target-comparison` | Change | 1000 reps | "Compare 1,000 and 10,000 inactive eligible targets while executing the same small active workload" (TEST-023), reported side by side rather than as one average. |
| 11 | `lifecycle-cycles-1000` | Change | 1000 reps | "Run lifecycle churn until bounded registries/caches reach a plateau"; the provisional row is *active counts return to baseline; bounded cache/native/managed growth plateaus*. Report-only. |

The fixture itself is the declared 1,000 scopes / 10,000 targets with three schema families, isolated and excluded
branches, and shared and unique contribution sets — "diagnostic loads, not assumptions about the eventual game".

## Commands

`NotRun (pending orchestrator build host)` — both commands below run on the build host, not here.

```sh
# 1. Build the qualification player. The benchmark mode is compiled into the same IL2CPP binary as every other probe
#    mode. UNITY must be the pinned 6000.0.75f1 Editor; the build refuses a different revision.
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  UNITY_PROJECT="$PWD/unity/GameCore.Validation" \
  ARTIFACTS=artifacts/toolchain \
  tools/unity/build_probe.sh

# 2. Run the benchmark and summarise it. Five independent runs of the whole catalogue is 08's declared shape.
PROBE_PLAYER="$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" \
  ARTIFACTS=artifacts/performance \
  BENCH_RUNS=5 \
  tools/run_benchmarks.sh
```

Re-summarising the same raw data without re-running the player (identical inputs give a byte-identical `summary.md`):

```sh
# NotRun (pending orchestrator build host)
python3 tools/summarize_benchmarks.py \
  --raw artifacts/performance/raw \
  --out artifacts/performance/summary.md \
  --decisions artifacts/performance/BUDGET_DECISIONS.md \
  --machine "$(hostname)" \
  --json artifacts/performance/summarize.json
```

A single workload, for triage (the selector is resolved against the catalogue, duplicates dropped, and the run order
is catalogue order whatever the request order):

```sh
# NotRun (pending orchestrator build host)
BENCH_WORKLOADS=steady-execution-10000-targets BENCH_RUNS=1 tools/run_benchmarks.sh
```

The player command line one run issues, as recorded verbatim in `raw/run<N>/invocation.txt`:

```
"${PROBE_PLAYER}" -batchmode -nographics -logFile raw/run<N>/player-benchmark.log \
  -probeBenchmark \
  -probeBenchmarkWorkload=<all|ids> \
  -probeBenchmarkScopes=1000 -probeBenchmarkTargets=10000 \
  -probeBenchmarkLiveScopes=1000 -probeBenchmarkLiveTargets=10000 \
  -probeBenchmarkWarmup=30 -probeBenchmarkDuration=120 -probeBenchmarkRepetitions=0 \
  -probeBenchmarkRunOrdinal=<N> -probeBenchmarkSeed=20260926 \
  -probeResult raw/run<N>/probe-benchmark.json
```

`-quit` is never passed: the probe exits itself through `Application.Quit` with the code that encodes its result
(0 all-pass, 1 any Fail, 3 the negative mode). Exit `>= 128` is a crash (SIGSEGV 139, SIGABRT 134, ...) and fails the
harness rather than counting as a verdict. The harness requires `"task": "GC-026"`, `"mode": "Benchmark"`,
`"result": "Pass"`, exit 0, no `"status": "Fail"`, at least one `"status": "Pass"`, one `benchmark-<workload>` step for
every selected workload, the fixed steps (`benchmark-config`, `benchmark-hardware`, `benchmark-fixture`,
`benchmark-correctness-gates`, `benchmark-digest`, `benchmark-artifacts-json`, `benchmark-artifacts-csv`), and a
non-empty `.samples.json` **and** `.samples.csv` per selected workload. Any run or validation failure stops the
harness before the summarizer runs.

Harness environment knobs and their defaults: `PROBE_PLAYER`, `UNITY_PROJECT`, `ARTIFACTS` (`artifacts/performance`),
`BENCH_RUNS` (5), `BENCH_WARMUP` (30), `BENCH_DURATION` (120), `BENCH_REPETITIONS` (0 = each workload's declared
default), `BENCH_WORKLOADS` (`all`), `BENCH_SCOPES` (1000), `BENCH_TARGETS` (10000), `BENCH_LIVE_SCOPES` /
`BENCH_LIVE_TARGETS` (default to the two above), `BENCH_SEED` (20260926), `BENCH_TIMEOUT` (3600 s per run), `MACHINE`
(`$(hostname)`).

## Wall-clock cost of the declared defaults

The catalogue is 11 workloads: three steady and eight change.

| Part | Cost at the declared defaults |
|---|---|
| Steady windows | 3 workloads x 5 runs x (30 s warmup + 120 s duration) = **2250 s = 37.5 min** |
| Change warmups | 8 workloads x 5 runs x 30 s = **1200 s = 20 min** (if the runner warms each workload) |
| Change repetitions | 5 runs x 5600 declared repetitions = **28,000 repetitions** |
| Fixed floor | **~57.5 min** of warmup/window time, plus the cost of 28,000 change repetitions |

The 5,600 declared repetitions per run are 1,000 each for `update-size-1`, `update-size-100`, `reparent-100`,
`inactive-target-comparison` and `lifecycle-cycles-1000`, and 200 each for `update-size-10000`,
`whole-world-mode-switch` and `spawn-1000`. Their wall clock is the repetition cost, which this document does not
estimate because no run has measured one; a whole-world repetition is a whole-world publication, which is why those
three declare 200 rather than 1,000. `BENCH_WORKLOADS` narrows the cost linearly when triaging.

## What is measured per phase, and why they are separate

08: "Separate preparation CPU work, wall-clock latency, safety-boundary wait, and apply pause" and "Time end-to-end
changes separately from apply time so off-thread preparation cannot hide an excessive user-visible delay." The raw
document therefore carries one entry per phase, and a budget row names its phase explicitly:

| Phase | What it is | Why it is its own number |
|---|---|---|
| `Warmup` | initialization/compilation/load work before the measured window | 08: "Warm up for 30 seconds or until initialization/compilation/load work is complete, whichever is later." Never a budget row. |
| `Prepare` | off-thread or caller-side derivation, closure and plan build for one change | The 100 ms whole-world row and the spawn baseline are preparation numbers. Preparing off-thread must not be able to hide a user-visible delay, so it is never folded into apply. |
| `Wait` | the mandatory safety-boundary wait: fencing, drain and dependency waits | 08 requires fencing time to be reported in "a separate mandatory wait metric"; folding it into apply would hide the fence. |
| `Apply` | the fenced apply pause, the interval the world is not simulating | The 2 ms pause row. This is the number a player feels as a hitch. |
| `EndToEnd` | the whole change from proposal to published assembly, measured by the caller | 08's "wall-clock latency": prepare + wait + apply may each be fine while the sum is not. |
| `Step` | one simulation step or one idle pump of a steady workload | The core-execution p95 row, and the idle/unchanged counter rows. |
| `Change` | one whole repetition of a repeated change, excluding fixture construction | The per-repetition cost of a change workload. |

Durations are microseconds. The summarizer pools every run's samples per phase and re-derives the distribution; it
never averages per-run percentiles.

## The percentile definition

Identical to `BenchmarkStatistics.PercentileOfSorted`, and stated in `summary.md` beside the numbers:

- sort the phase's pooled samples ascending;
- the p-th percentile is the element at index `ceil(p/100 * N) - 1`, clamped into `[0, N-1]`;
- `p <= 0` returns the minimum, `p >= 100` returns the maximum.

Nearest-rank, one definition, because interpolating percentiles differ between tools and a budget comparison that
changes its definition between runs is not evidence. `p50`, `p95`, `p99` and `max` all come from this one function, and
the summarizer re-derives them from the per-sample arrays rather than trusting the document's own folded statistics —
then audits the two against each other (see *Raw document self-consistency* in `summary.md`).

## Memory categories

08: "Memory reports distinguish managed heap, native containers, retained catalogs/caches, asset leases, events and
quarantined work", and "Avoid inferring a leak solely from an engine cache that has not yet reached its documented
bound." The summarizer reports these **separately**, never as one aggregate process total:

| Category | Source | Note |
|---|---|---|
| managed heap start / end | the runtime | a process total, reported as an observation only |
| managed allocated | the covered threads, with a thread-completeness flag | 08: "a main-thread-only reading cannot prove worker allocation is zero" |
| native containers | the world's own resource ledger | the bytes the world's containers hold |
| lease bytes (`live-leases`) | staged/asset leases | the "leases" half of the split |
| retained events (`retained-event-bytes`/`-count`) | committed events still retained | reported separately from quarantine |
| cache (`cache-bytes`/`-entries`) | retained derived caches | a bounded cache is not a leak |
| quarantine (`quarantine-bytes`/`-entries`) | work unfinished callbacks still reach | P-048 |
| outstanding callbacks | live activations plus tracked jobs | |

The `budget.execution-managed-bytes` row is the one place a number can be *structurally* unable to prove its claim: a
zero reading from a counter that did not cover every declared thread is reported as `NotMeasured`, never as a pass.

## The correctness gates, and the positive control

`summary.md` lists every gate of every workload with its pass/fail and its `k=v` detail, and **any failed gate fails
the whole summary** (exit 1). The gates the probe asserts are:

- zero stable control-tree scans over an unchanged composition (`control-nodes-visited == 0`);
- zero string service lookups while nothing changes (`service-string-lookups == 0`);
- an idle command-driven world advances zero simulation steps and zero simulation-stage updates;
- no duplicated authoritative state;
- the instrumentation is **live**: a positive control, a counter that moved somewhere in the run.

The positive control is what makes the zero claims mean something. Every hot-path count is a call to a
`[Conditional("GAMECORE_TELEMETRY")]` helper, so a build without the symbol removes the call site *and its argument
evaluation* — no branch, no store, no evaluation. Such a build reports `control-nodes-visited = 0` for a structural
reason, not because the control plane did no work. A gate that only asserted "the number is zero" could therefore pass
on a build where counting does not exist. Pairing it with a positive control — at least one counter that demonstrably
moved during the run — distinguishes "the control plane did no work" from "the counter was compiled out", and a zero
reading that cannot be distinguished is not evidence.

## Instrumented diagnostic build vs release-like measurement build

08: "Record the instrumented diagnostic build and release-like measurement build separately."

The counters need `GAMECORE_TELEMETRY`. That symbol reaches a compilation only through the asmdef `versionDefines`
entry on the qualification marker package `com.gamecore.telemetry-qualification`, which only the validation project's
manifest references. A shipping project therefore compiles zero counting call sites.

The consequence is a recorded fact of every measurement in this directory: **the presence of the qualification marker
is part of what was measured**, because without it every counter reads zero. A measurement whose build/config block
does not show the marker is either a release-like build (where counters are expected to be absent, and no counter row
means anything) or a misconfigured build — and the live-instrumentation gate above is what tells the two apart.
Counter-based numbers are only evidence in the instrumented shape; duration samples are evidence in both, and the
`isIl2Cpp`, `managedStrippingLevel` and `burstCompilerEnabled` facts in the build/config block say which shape a run's
numbers describe.

## What is NOT claimed

- **No throughput claim for `spawn-1000`.** 08 asks for a baseline, not a target: "establish a baseline" and "No
  throughput claim until the actual archetype/state footprint is measured." The row is report-only, and a report-only
  row is never turned into a pass or a miss.
- **No universal shipping requirement.** 08: the numbers are "initial engineering targets, not measured results or
  universal shipping requirements". They apply on the recorded baseline machine, which is why `summary.md` and
  `environment.txt` name the hardware.
- **No cross-platform claim.** Every measurement is one machine, one architecture, one scripting backend and one
  managed-stripping level. Nothing here generalises to another platform.
- **No average-FPS number.** 08: "Collect per-sample p50/p95/p99/max and total counts, not only averages or FPS." An
  average FPS figure would hide exactly the tail a frame budget is about.
- **No leak claim.** Native-leak attribution is GC-022's; this directory reports bounded cache/native/managed growth
  against a plateau, and never infers a leak from a cache that has not reached its documented bound.
- **No revision of a protocol quota.** P-022's limits are correctness bounds; see `BUDGET_DECISIONS.md`.

## Verdicts, exit codes and the decisions record

`summary.md` gives each of the ten budget rows exactly one verdict: `WithinTarget`, `MissedTarget`, `NotMeasured`
(the run produced no number — never a pass) or `ReportOnly` (a baseline 08 asks to be established).

| Tool | Exit | Meaning |
|---|---|---|
| `tools/run_benchmarks.sh` | 0 | every run clean and the summarizer satisfied |
| | 1 | a run was not clean (crash, wrong exit code, failed step, missing artifact), or the summarizer reported a failure |
| | 2 | missing prerequisite: no player at `PROBE_PLAYER`, or a bad knob |
| `tools/summarize_benchmarks.py` | 0 | every raw document parsed, every gate passed, no `MissedTarget` |
| | 1 | any gate failed, or any `MissedTarget` |
| | 2 | no raw input found, or a raw document is malformed JSON |

`BUDGET_DECISIONS.md` is created from a template only when it does not already exist, and never overwritten: a human's
record of an accepted revision must survive every later summary. Measured values go into `summary.md`; the human moves
one into the decisions record together with the cause and the decision. `--expect-miss` exists solely for the case
where a budget revision has been explicitly accepted, and it never suppresses a failed gate.

## Falsifiability

```sh
# NotRun (pending orchestrator build host) — pure Python, no Unity and no raw data needed.
python3 tools/summarize_benchmarks.py --self-test
bash -n tools/run_benchmarks.sh
```

The self-test builds synthetic raw documents in a temporary directory and asserts the summarizer's own logic: pooled
nearest-rank percentiles (never an average of per-run percentiles), `NotMeasured` for a missing phase, a missed row and
a failed gate each producing a non-zero exit, `--expect-miss` clearing a miss but never a gate, no raw input and
malformed JSON exiting 2, an existing decisions file never being overwritten, and two summaries of the same raw data
being byte-identical. It also pins the counters-section shape: a document whose `"counters"` is a real JSON object —
what `BenchmarkDocumentWriter` emits — must parse with no repair path taken and be recorded as unrepaired, while the
legacy array rendering is the only shape the fallback claims and is named in the summary when it applies. It is a
check of the tool, not of the engine.
