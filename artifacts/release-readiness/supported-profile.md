# Supported profile and accepted budgets

**V1 status: V1 complete (with owner-approved exception: TEST-023 timing deferred).**

This is the release-readiness record of what may be claimed about this revision. It is a record, not a
test result: every value below is read from a committed evidence file, and
`tools/release_readiness/build_release_readiness.py --check` re-derives the status from that evidence
and refuses to state it when the evidence has moved. The machine-readable form is
[`evidence-manifest.json`](evidence-manifest.json) and [`status.json`](status.json); the operator-facing
form is [`docs/operator/profile.md`](../../docs/operator/profile.md).

## 1. The exact profile

| Item | Value | Where the value comes from |
| --- | --- | --- |
| Protocol version | `1.0` | `artifacts/conformance/compatibility.json` |
| Accepted source revision | `3895d0c632c867ba87d783cd1a1654643dedfeb1` | `artifacts/w8-gate/reproduction/environment.txt`, `artifacts/conformance/compatibility.json` |
| Unity Editor | `6000.0.75f1` (Linux x86_64, Linux IL2CPP module) | `artifacts/w8-gate/matrix/toolchain/environment.txt` |
| Unity Entities | `1.4.6` | `unity/GameCore.Validation/Packages/packages-lock.json` |
| Unity Burst | `1.8.28` | same lock |
| Unity Collections | `2.6.6` | same lock |
| Unity Mathematics | `1.3.2` | same lock |
| Linux IL2CPP toolchain | `com.unity.toolchain.linux-x86_64` `2.0.11` | same lock |
| Native compiler | clang `18.1.3`, GCC `13.3.0`, GNU ld `2.42` (host); Unity sysroot glibc `2.17` | `artifacts/baseline/ENVIRONMENT.md` |
| Build target | `StandaloneLinux64` x86_64, IL2CPP, Release, High managed stripping, Burst enabled | `artifacts/conformance/compatibility.json`, player probe results |
| Execution | headless (`-batchmode -nographics`); audio disabled in the headless player | `unity/GameCore.Validation/ProjectSettings/AudioManager.asset` |
| Managed language / API | C# 9, .NET Standard 2.1 for the pure assemblies | `dotnet/GameCore.sln`, `tools/check_game_core_csharp.py` |
| Host | Ubuntu 24.04.4 LTS, kernel `7.0.0-31-generic`, Intel i7-12700KF, 20 logical CPUs | `artifacts/performance/environment.txt` |
| Package lock digest (accepted revision) | `350f75841d38cf3db58cddcb9b6761ecc9cd87bc199f74e281f11d9e80ba0d2b` | recomputed from `unity/GameCore.Validation/Packages/packages-lock.json` by the consistency tool |
| Package manifest digest | `19c09c9cc670b3ffa866bd600814b39766880a32571aeaa9afc23a7dba840ec8` | `artifacts/w8-gate/matrix/toolchain/environment.txt` |
| Qualification player digest (launcher) | `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70` | eleven evidence records agree; **recorded only** — the player is a build product and is not committed |
| Committed catalogs | 4 (`ProbeCatalog`, `CardCatalog`, `CheckpointCatalog`, `TraversalCatalog`) | `artifacts/conformance/compatibility.json` |

The launcher digest is the same across the Wave 7, Wave 8 and baseline records because the IL2CPP
launcher is a fixed shim; the AOT payload is `GameAssembly.so`, whose digest the baseline environment
record carries separately. Nothing here claims the committed tree can be rebuilt bit-identically.

## 2. Explicitly unqualified

Everything not in §1 is unqualified. Do not read Linux evidence as portability, and do not round
"unqualified" up to "supported".

| Target | Status |
| --- | --- |
| macOS ARM64 / x86_64 | **Unqualified.** The earlier macOS profile was replaced when the build host became Linux x86_64; no Apple SDK or macOS IL2CPP toolchain is installed. |
| Windows x86_64 (IL2CPP or Mono) | **Unqualified.** No host, no toolchain, no player. |
| Linux ARM64 | **Unqualified.** The Linux IL2CPP support is pinned to the x86_64 toolchain. |
| Android, iOS, other mobile | **Unqualified.** Different stripping and AOT surface; no module, device or player. |
| Consoles | **Unqualified and out of V1 scope.** |
| WebGL / WebAssembly | **Unqualified.** IL2CPP-over-WASM has a different AOT and threading surface. |
| Mono / desktop scripting backend | **Unqualified.** P-058 makes IL2CPP the V1 profile; a Mono player does not exercise the AOT/stripping surface this catalog work exists for. |
| Unity Editor execution | **Not evidence at all.** The Editor runs the same managed code under the JIT without managed stripping, so an EditMode pass says nothing about a stripped player. |

Reasons and the conditions that would qualify each target:
[`artifacts/baseline/unsupported-targets.md`](../../artifacts/baseline/unsupported-targets.md).

## 3. Accepted provisional budgets, per measurement

Source: `artifacts/w8-gate/matrix/benchmark/summarize.json` — the short diagnostic run at the accepted
revision (1 s warmup, 2 s steady windows, five change repetitions, 11 workloads, **19/19 correctness
gates passed**, one budget row `MissedTarget`). A second, earlier diagnostic at the GC-026 revision is
recorded in `artifacts/performance/summary.md`; its numbers differ slightly and are not the accepted
ones.

| Budget row | 08's provisional target | Measured (this diagnostic) | Verdict |
| --- | ---: | ---: | --- |
| `budget.execution-p95` | ≤ 4000 µs | 1639 µs | Within target |
| `budget.execution-managed-bytes` | 0 bytes/step | 0 (thread-complete) | Within target |
| `budget.unchanged-control-nodes` | exactly 0 | 0 | Within target |
| `budget.unchanged-service-lookups` | exactly 0 | 0 | Within target |
| `budget.apply-pause-p95` | ≤ 2000 µs | 632 µs (one sample) | Within target |
| `budget.whole-world-preparation-p95` | ≤ 100000 µs | **1165868 µs** | **Missed — open issue** |
| `budget.spawn-baseline` | report-only | 164991 µs | Report-only (a baseline, not a target) |
| `budget.lifecycle-plateau` | report-only | 992397 µs | Report-only (a baseline, not a target) |
| `budget.idle-steps` | exactly 0 | 0 | Within target |
| `budget.idle-stage-updates` | exactly 0 | 0 | Within target |

The targets in that column remain **provisional engineering targets on the named machine**; no target
was revised, and no full-duration p95/p99 qualification is claimed. The protocol quotas of P-022 are
correctness bounds, not performance targets, and are unaffected by anything here. The row-by-row human
record with causes and decisions is
[`artifacts/performance/BUDGET_DECISIONS.md`](../../artifacts/performance/BUDGET_DECISIONS.md); the
operator summary is [`docs/operator/deferred-scope.md`](../../docs/operator/deferred-scope.md).

### The open whole-world preparation cost

Whole-world derivation for 10,000 targets takes ~0.9–1.17 s against a 100 ms target: roughly **9–12×
over**, unchanged from the GC-026 diagnostic. The dominant cost is the root rule that selects all
10,000 family-schema targets and rejects 9,999 with a tag predicate, which P-015/P-026 provenance
requirements prevent from being skipped. This is an **open performance issue for post-V1 work**, not an
accepted or revised target.

## 4. The owner-approved exception

TEST-023 full-duration timing qualification (P-060 timing rows) is Deferred by project-owner decision
(2026-09-26). Its correctness gates passed; the measured diagnostic numbers and the open
whole-world/local prepare-cost miss at 10,000 targets are recorded as a known open performance issue
for post-V1 work. Repeated player/probe runs were capped at two (PROBE_RUNS=2) by the same decision.
The decision is recorded in
[`docs/game-core/10-decisions-and-open-questions.md`](../../docs/game-core/10-decisions-and-open-questions.md)
and the deferral itself in `artifacts/performance/BUDGET_DECISIONS.md`.

**The deferred timing rows are never reported as Pass.** `artifacts/release-readiness/status.json`
carries the same wording, and the tool that writes it fails if the accepted evidence ever reports the
deferred row as anything other than `Deferred`.

## 5. What the status is conditioned on

`tools/release_readiness/build_release_readiness.py` emits the status string at the top of this page
only when, at the accepted revision:

* both accepted evidence trees (`artifacts/w8-gate/matrix`, `artifacts/conformance/results`) report
  60 requirements and 26 operations with no `Fail`, `Blocked` or `NotRun`, exactly one non-pass row
  (`P-060`, `Deferred`), zero unresolved references, and identical row-by-row statuses;
* both trees report 24/24 required TEST suites `Pass` with no failing suite;
* the three reference families are `Pass` and the assembly audit is clean in both trees;
* the diagnostic is the accepted one (11 workloads, 1 run, 19 correctness gates, exactly one missed
  provisional target, none unmeasured);
* the budget decision record still carries the deferral, the decision date and the deferred row, and
  the pre-decision template snapshot in the gate tree does not claim it;
* every recorded revision, catalog digest, package digest and player digest is consistent with the
  working tree (`tools/release_readiness/check_revision_consistency.py`);
* every gate keeps the verdict sentence and revision declaration its report recorded, and every gate's
  derived status is `Pass` or `Deferred (owner decision)`;
* every document that states the implementation status states exactly this status.

Otherwise the tool writes `incomplete`, prints the reasons and exits non-zero, and the documentation
check fails if a page still claims completion.
