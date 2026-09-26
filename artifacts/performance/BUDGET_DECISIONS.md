# GC-026 budget decisions

Status: **TEST-023 timing qualification Deferred by project-owner decision (2026-09-26); not measured at full duration**. Functional correctness gates passed in a final single short diagnostic: 1 s warmup, 2 s steady windows, five change repetitions, 11 workloads and 19/19 gates. A default full-catalogue attempt was stopped at update-size-1 repetition 629/1000 after 916.7 s because the remaining broad candidate fan-out still costs ~0.87 s/change. No full-duration p95/p99 claim follows from the diagnostic.

This file is the human record for the ten rows of
[08's provisional budget table](../../docs/game-core/08-validation-and-performance.md) as encoded in
`tests/GameCore.Benchmarks/Runtime/PerformanceBudgets.cs`. Its own preface is part of the contract:

> The following numbers are initial engineering targets, not measured results or universal shipping requirements.
> Apply them on the recorded baseline machine; retain the measurements if later product evidence justifies revising a
> target. A quota in the protocol is a correctness bound and remains mandatory even when a performance target changes.

So the numbers remain **provisional engineering targets**. No revision is accepted in this record. The observed whole-world miss remains an open performance issue, not an accepted or proposed target revision; the short diagnostic cannot establish a replacement numerical target. Protocol quotas remain mandatory.

A protocol quota is a **correctness bound** and is never revised here; see
[Correctness bounds (not revisable here)](#correctness-bounds-not-revisable-here).

How a row is filled: `tools/summarize_benchmarks.py` writes the measured value and its verdict into `summary.md` and
never edits this file. A human moves the measured number here and records the cause and the decision. A `NotMeasured`
verdict in `summary.md` is never a pass and never becomes a `Measured` field here.

Two of the ten rows are **report-only**: 08 asks for a baseline to be established, not for a number to be met. They
have no target to miss, and inventing one here would be exactly the unsupported claim 08 forbids.

## budget.execution-p95

- workload / phase / metric: `steady-execution-10000-targets` / `Step` / `p95`
- Target (08): at most 4000 us — 10,000 integer-rule targets, 1,000 active commands per fixed step: Core execution p95 at most 4 ms.
- Included / separately reported: Scheduling, queries, request arbitration, commit; report native physics/render/audio separately.
- Measured (diagnostic only): 1514 us, within 4000 us
- Cause: single short run; full gate unavailable
- Decision: retain provisional target; no revision
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.execution-managed-bytes

- workload / phase / metric: `steady-execution-10000-targets` / `Step` / `managed-bytes-per-step`
- Target (08): at most 0 bytes — Stable execution after warmup: 0 managed bytes per logical step in the kernel hot path.
- Included / separately reported: Include worker work; report explicitly enabled diagnostics or plugin allocations separately.
- Measured (diagnostic only): 336.49 bytes/step in old diagnostic run; 0 bytes/step after measuring only kernel work
- Cause: sample serialization (~336 bytes/step) was included in original counter; kernel work measured zero after correction
- Decision: fix: exclude diagnostic sample creation from kernel allocation interval; full replay pending
- Evidence: `artifacts/gc-026/allocation-smoke/raw/run1/`, `artifacts/performance/summary.md`
- Note: a zero is only a measurement when the allocation counter covered every declared thread. A main-thread-only
  zero cannot prove worker allocation is zero, and the summarizer reports the row as NotMeasured in that case.

## budget.unchanged-control-nodes

- workload / phase / metric: `steady-unchanged-10000-steps` / `Step` / `control-nodes-visited`
- Target (08): exactly 0 — No composition changes over 10,000 steps: 0 control-tree visits.
- Included / separately reported: Counters count control work even if a traversal is cached or returns no matches.
- Measured (diagnostic only): 0, within 0
- Cause: zero stable control work; positive control recorded separately
- Decision: retain correctness bound; full replay pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.unchanged-service-lookups

- workload / phase / metric: `steady-unchanged-10000-steps` / `Step` / `service-string-lookups`
- Target (08): exactly 0 — No composition changes over 10,000 steps: 0 string service resolutions.
- Included / separately reported: Counters count control work even if a traversal is cached or returns no matches.
- Measured (diagnostic only): 0, within 0
- Cause: zero string lookups; positive control recorded separately
- Decision: retain correctness bound; full replay pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.apply-pause-p95

- workload / phase / metric: `update-size-100` / `Apply` / `p95`
- Target (08): at most 2000 us — Valid plan affecting 100 existing targets: Apply pause p95 at most 2 ms.
- Included / separately reported: Include fencing time in a separate mandatory wait metric; report preparation and end-to-end latency.
- Measured (diagnostic only): 624 us, within 2000 us
- Cause: one real fenced apply only; no five-run distribution
- Decision: retain provisional target; full replay pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.whole-world-preparation-p95

- workload / phase / metric: `update-size-10000` / `Prepare` / `p95`
- Target (08): at most 100000 us — Whole-world derivation for 10,000 targets: Preparation p95 at most 100 ms.
- Included / separately reported: Full closure/index work; no partial publication to meet the number.
- Measured (diagnostic only): 1123930 us, exceeds 100000 us (round 1: 15425585 us)
- Cause: round-two explanation-input indexing reduced ~12 s/update to ~0.87 s for size 1; the root rule still selects all 10000 family-schema targets and rejects 9999 with a tag predicate. P-015/P-026 require reconstructable rejection provenance. Pure phase sample: candidate strata ~453 ms, global sort/finalization ~254 ms, assembly ~66 ms, delta ~30 ms; see artifacts/gc-026/profile/round2-phases.txt
- Decision: retain 100 ms provisional target unchanged; timing qualification deferred by owner, open performance issue
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.spawn-baseline

- workload / phase / metric: `spawn-1000` / `Prepare` / `p95` (report-only)
- Target (08): report-only: establish a baseline (no target to compare against) — 1,000-target spawn under active capabilities: report prepare/apply p95, native/managed bytes and recipe reuse; establish a baseline.
- Included / separately reported: No throughput claim until the actual archetype/state footprint is measured.
- Measured (diagnostic only): 3059771 us (report-only)
- Cause: pure 1000-target spawn plus one live publication; diagnostic load
- Decision: retain report-only; full baseline pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.lifecycle-plateau

- workload / phase / metric: `lifecycle-cycles-1000` / `Change` / `p99` (report-only)
- Target (08): report-only: establish a baseline (no target to compare against) — 1,000 lifecycle cycles with fixed retained data: active counts return to baseline; bounded cache/native/managed growth plateaus.
- Included / separately reported: Report asset policy, retained events and quarantine separately.
- Measured (diagnostic only): 12337752 us p99 (report-only)
- Cause: five cycles only; no 1000-cycle plateau qualification
- Decision: retain report-only; full plateau pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.idle-steps

- workload / phase / metric: `idle-command-world` / `Step` / `steps-advanced`
- Target (08): exactly 0 — Idle command-driven world: 0 simulation steps.
- Included / separately reported: Host presentation and pending control-plane operations may still run.
- Measured (diagnostic only): 0, within 0
- Cause: idle command world advanced no steps
- Decision: retain correctness bound; full replay pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## budget.idle-stage-updates

- workload / phase / metric: `idle-command-world` / `Step` / `stage-samples`
- Target (08): exactly 0 — Idle command-driven world: 0 simulation-stage updates.
- Included / separately reported: Host presentation and pending control-plane operations may still run.
- Measured (diagnostic only): 0, within 0
- Cause: idle command world ran no stages
- Decision: retain correctness bound; full replay pending
- Evidence: `artifacts/gc-026/catalogue-smoke/raw/run1/`, `artifacts/performance/summary.md`

## Correctness bounds (not revisable here)

A quota in the protocol is a correctness bound and remains mandatory even when a performance target changes. P-022
(`docs/game-core/00-core-protocols.md`) gives the default reference limits of `PropagationBudget`:

| Quota (P-022) | Reference limit |
| --- | --- |
| Examined candidates | 1,000,000 |
| Emitted contributions | 250,000 |
| Affected targets | 100,000 |
| Temporary storage | 128 MiB |
| Wall-clock preparation deadline | 2 s |
| Safe-point apply cost estimate | 8 ms |

A benchmark that exceeds one of these is a **correctness finding reported with the fixture configuration, not a budget
miss**: the fan-out that reached the limit is the finding, and no performance revision here may move the limit. These
are provisional guardrails, not product capacity claims either — but they are enforced by the derivation engine, so a
run that crosses one has already failed a protocol rule rather than merely missed an engineering target.

## Round 2 final diagnostic (2026-09-26)

Evidence: `artifacts/gc-026/correctness-final-round2/summary.md` and `artifacts/gc-026/profile/round2-phases.txt`. Full catalogue timing qualification is **Deferred by project-owner decision (2026-09-26); not measured at full duration**. No target or P-022 correctness quota was revised. One short player run measured 1514 us execution p95 (4000-us target), zero thread-complete kernel bytes/step, zero stable control visits/string lookups, 624 us apply pause from one sample (2000-us target), and 1123930 us whole-world prepare p95 from five samples (100000-us target, open miss). Report-only short spawn prepare p95 181927 us and lifecycle p99 966923 us do not establish a full baseline/plateau. Idle advanced zero steps and stages; the owner-state mutation read 11->12 without changing derived bindings. The update-size-1 prepare p95 was 898936 us, versus ~12 s in round 1; this is diagnostic, not a qualifying p95.
