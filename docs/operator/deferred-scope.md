# Scope: required V1, and deferred work

This page is the scope contract. It separates what V1 **must** do from what is deliberately deferred, and it
records the one performance qualification the project owner deferred. Nothing here may be read as a passing
test result.

## 1. Required V1

Verbatim from [09 "Required V1 and deferred work"](../game-core/09-implementation-guide.md):

> Required V1 includes both propagation modes, automatic existing/future descendants, all composition
> policies, finite derivation and indexes, state ownership, both temporal models, plugin stages, all
> lifecycle transitions, stale-work rejection, safe unload/fail-stop, checkpoint/schema migration, consistent
> observation, the three reference families and their combination, generated precompiled mounting,
> IL2CPP/stripping, and measured bounded operation. None is a discretionary follow-up to the early slice.

Deferred items are **never** reclassified as required capabilities, and required capabilities are **never**
relabelled as deferred conveniences.

## 2. Deferred, because each adds a large independent mechanism outside these requirements

Verbatim from 09:

> another engine/ECS backend, universal query abstraction, arbitrary executable-code download or hot
> replacement, untrusted plugin sandboxing, cross-world/distributed transactions, generic speculative
> execution/undo, historical rollback/netcode lockstep, exact cross-engine physics/animation parity, and
> workbench/editor/AI UX.

09 adds the consequence that matters for an operator: **no empty mandatory interfaces are created for them.**
There are no placeholder seams to discover, and their absence is not a gap.

Explicitly still required, and therefore *not* deferred: ordinary adapters, gameplay extension points,
checkpoint recovery, and safe dynamic mounting.

## 3. The deferred performance qualification

**This is the one item that would otherwise look like a required test with a missing result. It is not
missing — it was deferred by the project owner on 2026-09-26.**

| Item | Status |
| --- | --- |
| TEST-023 **timing** qualification (full-duration p95/p99 catalogues, 30 s warmups + five independent 120 s runs) | **Deferred (owner decision).** Not measured at full duration. **Never to be reported as Pass.** |
| TEST-023 **correctness** gates (zero stable control-tree scans, zero string service lookups, an idle world advancing zero steps, no duplicated authoritative state) | **Required, and passed** in a short diagnostic. |

The decision is recorded in [`artifacts/performance/BUDGET_DECISIONS.md`](../../artifacts/performance/BUDGET_DECISIONS.md),
and `tools/check_budget_record.py` mechanically asserts that the record still says so — including refusing a
section that claims a full-duration measurement while the deferral stands.

### What the short diagnostic did measure

One short player run (1 s warmup, 2 s steady windows, five change repetitions, 11 workloads, 19/19
correctness gates):

| Budget row | Target (08) | Measured (diagnostic only) | Verdict |
| --- | --- | --- | --- |
| `budget.execution-p95` | ≤ 4000 µs | 1514 µs | Within target |
| `budget.execution-managed-bytes` | 0 bytes/step | 0 (thread-complete) | Within target |
| `budget.unchanged-control-nodes` | exactly 0 | 0 | Within target |
| `budget.unchanged-service-lookups` | exactly 0 | 0 | Within target |
| `budget.apply-pause-p95` | ≤ 2000 µs | 624 µs (one sample) | Within target |
| `budget.whole-world-preparation-p95` | ≤ 100000 µs | **1123930 µs** | **Missed — open issue** |
| `budget.spawn-baseline` | report-only | 181927 µs | Report-only |
| `budget.lifecycle-plateau` | report-only | 966923 µs p99 | Report-only |
| `budget.idle-steps` | exactly 0 | 0 | Within target |
| `budget.idle-stage-updates` | exactly 0 | 0 | Within target |

The two `report-only` rows are baselines 08 asks to be *established*, not numbers to be met; no target was
invented for them.

### The open prepare-cost issue (stated plainly)

Whole-world derivation for 10,000 targets takes **~0.87–1.12 s** against a **100 ms** target: roughly
**9–11× over**. This is an **open performance issue**, not an accepted or revised target.

- Round-two explanation-input indexing reduced the cost from ~12 s/update to ~0.87 s for update size 1.
- The root rule still selects all 10,000 family-schema targets and rejects 9,999 with a tag predicate, which
  is the dominant cost. P-015/P-026 require reconstructable rejection provenance, so the fan-out cannot
  simply be skipped.
- Measured phase split (`artifacts/gc-026/profile/round2-phases.txt`): candidate strata ~453 ms, global
  sort/finalization ~254 ms, assembly ~66 ms, delta ~30 ms.

The 100 ms target was **retained unchanged**. No target and no P-022 correctness quota was revised, and the
short diagnostic cannot establish a replacement numerical target.

### P-022 correctness quotas are not performance targets

These are enforced by the derivation engine and are **never** revised by a performance decision. A run that
crosses one has failed a protocol rule, not missed an engineering target:

| Quota (P-022) | Reference limit |
| --- | ---: |
| Examined candidates | 1,000,000 |
| Emitted contributions | 250,000 |
| Affected targets | 100,000 |
| Temporary storage | 128 MiB |
| Wall-clock preparation deadline | 2 s |
| Safe-point apply cost estimate | 8 ms |

## 4. Unqualified platforms

Beyond the single qualified profile, every platform is unqualified. The full list with reasons is in
[profile.md §3](profile.md) and
[`artifacts/baseline/unsupported-targets.md`](../../artifacts/baseline/unsupported-targets.md).
Product platform and load targets beyond the exact qualification profile remain **explicit unknowns** in
[10-decisions-and-open-questions.md](../game-core/10-decisions-and-open-questions.md); they do not justify
claiming support without a built and executed player.

## 5. What this means when you report status

- Do **not** report the timing qualification as Pass, and do not omit it either — report it as
  **Deferred (owner decision)** with the diagnostic numbers and the open prepare-cost issue.
- Do **not** report "Linux evidence" as portability.
- Do **not** treat a deferred mechanism as a pending backlog item that someone forgot; it is a deliberate
  scope decision, recorded.
- A performance number in 08 is a **provisional engineering target** on a named machine, not a shipping
  requirement — but a protocol quota is a correctness bound and stays mandatory.
