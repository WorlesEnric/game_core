# Outstanding defects and deferred scope

Everything below is known, recorded, and **outside** the V1 completion claim — except where marked as a
defect still open on the accepted revision. Nothing here is a pending item someone forgot; each is a
decision, a diagnosed limitation, or a defect with its evidence. The V1 status itself is in
[`status.json`](status.json).

## 1. Deferred by explicit scope decision (design, not a gap)

From [09 "Required V1 and deferred work"](../../docs/game-core/09-implementation-guide.md#required-v1-and-deferred-work),
restated in [`docs/operator/deferred-scope.md`](../../docs/operator/deferred-scope.md) §2: another
engine/ECS backend, a universal query abstraction, arbitrary executable-code download or hot
replacement, untrusted plugin sandboxing, cross-world or distributed transactions, generic speculative
execution/undo, historical rollback/netcode lockstep, exact cross-engine physics/animation parity, and
workbench/editor/AI UX. No empty mandatory interfaces were created for them.

Explicitly **not** deferred and therefore in the completion claim: ordinary adapters, gameplay
extension points, checkpoint recovery and safe dynamic mounting.

## 2. Deferred by project-owner decision (2026-09-26)

| Item | Status |
| --- | --- |
| TEST-023 full-duration **timing** qualification (five independent 120 s runs, p95/p99 catalogues) | **Deferred by project-owner decision; never reported as Pass.** Timing rows: P-060 / `budget.*` timing rows. |
| TEST-023 **correctness** gates (zero stable control-tree scans, zero string service lookups, an idle world advancing zero steps, no duplicated authoritative state) | Required and **Passed** in the short diagnostic. |
| Repeat budget for player/probe runs | Capped at **two** (`PROBE_RUNS=2`); `tools/reproduce.sh` rejects any other value rather than clamping it. |

## 3. Open performance issue (post-V1 work)

Whole-world derivation for 10,000 targets costs **~0.9–1.17 s** against a **100 ms** provisional target
(~9–12× over); local change preparation at 10,000 targets is ~0.87–1.15 s. The root rule selects all
10,000 family-schema targets and rejects 9,999 with a tag predicate, which P-015/P-026 provenance
requirements prevent from being skipped. No target was revised; the 100 ms target stands as the
provisional engineering target. Measured phases and the recorded decision:
[`artifacts/performance/BUDGET_DECISIONS.md`](../../artifacts/performance/BUDGET_DECISIONS.md),
[`artifacts/gc-026/profile/round2-phases.txt`](../../artifacts/gc-026/).

## 4. Diagnosed, not source-fixed

| Item | State | Evidence / consequence |
| --- | --- | --- |
| Intermittent Unity Editor pre-dispatch hang | **Diagnosed, not fixed in source.** The Editor occasionally hangs before dispatching (no log progress for ten minutes); every Editor invocation is wrapped in `timeout --signal=TERM --kill-after=60 1800`, the matrix runner allows one logged retry, and no accepted run counted a timeout as a pass. | `docs/operator/editor-hang.md`; `artifacts/w8-gate/BUILD_REPORT.md` (host and profile) |
| NuGet advisory audit on a host with no advisory feed (`NU1900`) | **Handled, not suppressed.** The audit is on by default; `GAMECORE_OFFLINE=1` opts a single run out for `restore/build/test` and is recorded in the transcript and `environment.txt`; an audit-off run's audit result is `NotRun`, never `Pass`. GC-029's host-side runs recorded `NotRun`; the Wave 8 matrix and the final clean clone both ran with the audit **on** and zero warnings/errors. | `README.md`, `docs/operator/build-and-run.md` §2.1, `artifacts/w8-gate/BUILD_REPORT.md` |
| Release gate-surface scan reports `missingSeams: ["durable-delivery-core (DeliveryKey)"]` while its checker verdict is Pass | **Reportable scan limitation, not waived.** `DurableOutbox` is present and the GC-021 qualification probe plus the release recovery smoke ran; the advisory is recorded rather than turned into a Pass. | `artifacts/w8-gate/matrix/release-gate-surface.json`, `artifacts/w8-gate/BUILD_REPORT.md` |
| `artifacts/w8-gate/matrix/benchmark/BUDGET_DECISIONS.md` is a pre-decision template snapshot | **Recorded hazard, not corrected by editing archived evidence.** The tool that created that tree wrote its default template because the file did not exist there. The authoritative decision record is `artifacts/performance/BUDGET_DECISIONS.md`; the consistency tool fails if the snapshot ever starts carrying the deferral or if the authoritative record loses it. | §5 of `supported-profile.md`; `check_revision_consistency.py` group F |

## 5. Design gaps carried under the mandatory interface

| Item | State |
| --- | --- |
| Contact observations (GC-020) | The reference compositions declare no action-phase observation for cards/narrative; physics/animation ownership is an optional adapter, not part of gameplay authority. Recorded in [the decisions document](../../docs/game-core/10-decisions-and-open-questions.md) §2 (P1 rows) rather than implemented speculatively. |
| Definition-level bake rather than prefab bake (GC-025) | The catalogs are generated from definition assets through committed generated code, not from prefab baking. Design decision recorded in the GC-025 handoff and [04](../../docs/game-core/04-unity-integration.md); it changes the authoring surface, not the runtime contract. |
| O-26 pause-while-a-job-is-executing | Dotnet half proves the job pin and the debt arithmetic; no archived run pauses a world with a tracked job in flight. Listed as the O-26 `gap` in `artifacts/gates/w4-generic-profile/inventory.json`; the clause is `Pass` with that note. |
| P-007 binding-resolution counter | No direct resolution counter exists; the clause is evidenced by the string-lookup counter (zero service string resolutions across a 10,000-step replay) and the lease-identity assertions. |
| P-034 ownership transfer | The writer-domain validator has no transfer input, so a cross-owner writer overlap always rejects; transfer is expressed only through the state-slot `OwnerTransferValidator` (P-025/P-032). `Pass` with that note. |
| Inventory rows | `artifacts/gates/w4-generic-profile/inventory.{md,json}` is GC-012's artifact and was not modified by GC-028, GC-029 or GC-030. GC-028 proposed promotions in its handoff; no row was promoted. |

## 6. Not claimed here

No publishing, no registry push, no platform claim beyond §1 of
[`supported-profile.md`](supported-profile.md), and no commercial or schedule claim.
