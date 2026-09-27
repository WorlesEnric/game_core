# GC-030 HANDOFF — the required V1 completion gate (Wave 9)

**Status of every build/test/player statement in this document: `NotRun (pending orchestrator build host)`.**
This host has no Unity, no .NET SDK, no mono and no C# compiler, so nothing in this change set has been
compiled, imported, executed or built here. What DID run is interpreter-level and is listed in §8.
No C# file was changed by this task at all.

## 1. Deliverable

GC-030, verbatim objective: *"Produce one auditable V1 completion decision with the actual supported
build profile."* Its concrete work: check both prior tasks refer to the same source/catalog/package-lock
revision; reconcile outstanding defects and deferred features; archive final evidence; set
implementation status only after all required gates pass; record the exact profile and the provisional
budgets accepted by measurement.

The project-owner decision of 2026-09-26 is executed as directed: **V1 is complete with one explicit
owner-approved exception** (TEST-023 full-duration timing, P-060 timing rows → `Deferred by
project-owner decision`), and the deferral is recorded traceably — who, when, what, why — in
`docs/game-core/10-decisions-and-open-questions.md` §5 (new decision `ADR-017`) and in the
release-readiness record. It is never reclassified as `Pass`.

| Deliverable | What it is |
| --- | --- |
| `tools/release_readiness/check_revision_consistency.py` | The required consistency tool: reads the recorded revisions, catalog digests, package digests and player digests out of the evidence and fails on any mismatch with each other or with the working tree. |
| `tools/release_readiness/readiness_data.py` | The data those tools read: evidence records, their roles, the declared supersession/rejection/digest tables and the per-gate pointer registry. Pointers and reasons only — **no status is stated in it**. |
| `tools/release_readiness/build_release_readiness.py` | Evaluates the completion condition mechanically and generates the manifest and status; **refuses** to emit the completion status when the evidence does not support it, and fails if any document claims it early. |
| `artifacts/release-readiness/supported-profile.md` | The exact profile, the explicitly unqualified targets, the accepted provisional budgets per measurement, the owner exception and the conditions the status is conditioned on. |
| `artifacts/release-readiness/evidence-manifest.json` / `.md` | Generated: every required gate (W1..W9, GC-001..GC-030) with its report, its result files, its recorded revision, the verdict sentence its own report contains, and its status derived from the accepted requirement index. |
| `artifacts/release-readiness/status.json` | Generated: the decision, the owner exception, the revision-consistency summary and the gate-condition result. |
| `artifacts/release-readiness/outstanding-defects.md` | Outstanding defects / deferred features: 09's deferred scope, the owner exception, the diagnosed Editor hang, the contact-observation and definition-level-bake design gaps, `NU1900` handling, the advisory `missingSeams` scan limitation, the pre-decision budget snapshot, and the carried inventory gaps. |
| Docs status updates | `docs/game-core/10-decisions-and-open-questions.md` (§5 new), `docs/game-core/{README,00-core-protocols,07-reference-compositions,09-implementation-guide}.md`, `README.md`, `docs/operator/deferred-scope.md`. |

## 2. The revision question, answered with evidence

The Wave 8 gate's accepted revision is `3895d0c632c867ba87d783cd1a1654643dedfeb1`
(`artifacts/w8-gate/reproduction/environment.txt`, and the compatibility reports). Seven records declare
it and all agree; the tool proves the accepted revision is an ancestor of `HEAD`.

Two earlier records legitimately declare **different** revisions, and the tool proves rather than
assumes why that is acceptable:

| Record | Revision | Proof the tool performs |
| --- | --- | --- |
| `artifacts/reproducibility/final-clone/environment.txt`, `artifacts/gc-029/BUILD_REPORT.md` | `7d1e305` (GC-029's branch) | `git diff --name-only 7d1e305 3895d0c -- <packages + 4 catalog dirs>` is empty: catalogs, manifest and lock are byte-identical. The Wave 8 gate then re-ran the same `tools/reproduce.sh` at the accepted revision (54/54 steps) — that run is GC-029's evidence of record for the gate. |
| `artifacts/conformance/results/compatibility.json` | `119ae11` (GC-028's branch) | The four catalogs are byte-identical and the lock's resolved `(source, version)` graph is identical; the **lock text** differs only in the local packages' nested dependency version strings (`0.1.0` → `1.0.0`), which GC-029's packaging change caused. The Wave 8 gate re-ran the whole matrix at the accepted revision. |

Two more are declared **historical** with their reason: GC-029's *first* clone (`62539e2`), whose own run
rewrote the committed lock and which no accepted record cites (the gate still records its rejection —
the tool fails if that sentence disappears), and the GC-025 baseline record (`aad9038`), taken before the
packaging change.

Catalog identity is proven per record, not only per report: both compatibility reports, the three
committed catalog ledgers, the nine globs of player documents (28+ probe documents) and every
environment record's `catalog_sha256` must all reproduce the `CatalogFileHash` / `CatalogFingerprint`
literals inside the committed catalog files. Package identity is re-derived from the working tree's
manifest and lock (pins, resolved registry/builtin/local versions, testables) and compared with every
compatibility report's copy. The player launcher digest is **recorded-only** — eleven records agree on
`aeaf13e2…`, and the tool says so rather than pretending to recompute a build product.

## 3. The completion condition the tool enforces

The status string is emitted only when, at the accepted revision:

* **both** accepted trees — `artifacts/w8-gate/matrix` (the gate's own run) and
  `artifacts/conformance/results` (GC-028's canonical run) — report 60 requirements and 26 operations,
  no `Fail`/`Blocked`/`NotRun`, exactly one non-pass row (`P-060`, `Deferred`), zero unresolved
  references, and **identical row-by-row statuses**;
* both report 24/24 required TEST suites `Pass`, no failing suite;
* all three families are `Pass` with a clean assembly audit in both;
* the diagnostic is the accepted one: 11 workloads, **1 run**, 19 correctness gates, exactly one
  `MissedTarget` (`budget.whole-world-preparation-p95`), none unmeasured (a longer or differently
  shaped run is refused);
* `artifacts/performance/BUDGET_DECISIONS.md` still carries `Deferred by project-owner decision`,
  the date and the deferred row, and the **pre-decision template snapshot** in the gate tree does not
  (if it ever starts carrying the deferral, or the authoritative record loses it, the tool fails);
* every revision / catalog / package / player digest is consistent with the working tree;
* every gate keeps the verdict sentence and revision declaration its report recorded, and every gate's
  derived status is `Pass` or `Deferred (owner decision)`;
* every document that states the status states exactly this status — and **while the evidence is
  incomplete, a document that still claims completion is itself a failure**.

One corpus problem is declared rather than silently tolerated: GC-028's canonical tree indexes two
replay `.trace.json` sidecars as unreadable probe results, because it was built before the sidecar
exclusion that the Wave 8 gate records; both are listed in `KNOWN_CORPUS_PROBLEMS` with that reason, the
accepted gate tree reports zero corpus problems, and an **undeclared** problem fails the condition.

## 4. Requirement and test coverage

`traceability.json`'s GC-030 entry (requirements P-001, P-055, P-056, P-057, P-058, P-059, P-060; tests
TEST-001, TEST-021, TEST-023, TEST-024) is unchanged, and the documentation validator passes with it.
09's acceptance sentence — *"No unresolved required P requirement or operation case remains; all
selected-target build/runtime evidence is current. Deferred items are outside V1 by the stated scope,
never reclassified required capabilities"* — is what §3 enforces.

| Requirement | What this task delivers | Where |
| --- | --- | --- |
| **P-001** genre independence | Nothing new is required of the kernel; the manifest derives W9's coverage from the waves and records the three families `Pass` in both accepted trees. | `evidence-manifest.json`, `supported-profile.md` §3 |
| **P-055** protocol evolution | The accepted revision carries protocol `1.0` and package version `1.0.0` everywhere; the profile and manifest record the protocol version they claim. | `supported-profile.md` §1 |
| **P-056** extension points | The outstanding-defects record states the deferred scope and that no empty mandatory interfaces were created for it. | `outstanding-defects.md` §1 |
| **P-057** conformance | The completion status is conditioned on both accepted trees agreeing row-by-row, and on the suite matrix and family audit; no unsupported platform is claimed. | `build_release_readiness.py`, `supported-profile.md` §2 |
| **P-058** implementation profile | The exact Editor/Entities/Burst/Collections/IL2CPP/High-stripping/headless/audio profile is recorded with its pins and the unqualified list. | `supported-profile.md` §1–§2 |
| **P-059** genre validation | The three families' verdicts are read from the accepted trees and required `Pass`; the release and qualification players are in the gate record. | `build_release_readiness.py` |
| **P-060** evidence and release status | This task *is* the P-060 record: the exact profile, the accepted provisional budgets per measurement, the open prepare-cost issue and the owner-deferred timing rows (never `Pass`). | `supported-profile.md`, `status.json`, `10-decisions…` §5 |
| **TEST-001** toolchain/registration/IL2CPP | The profile records the toolchain the accepted evidence came from; the revision tool re-checks the recorded digests. | `supported-profile.md` §1, `check_revision_consistency.py` |
| **TEST-021** genre neutrality | All three families must be `Pass` in both accepted trees before the status is emitted. | `build_release_readiness.py` |
| **TEST-023** performance | Its timing rows are recorded `Deferred (owner decision)` with the diagnostic numbers; its correctness gates are recorded `Pass`; the deferred rows are surfaced by the tool, never as `Pass`. | `supported-profile.md` §3–§4, `status.json` |
| **TEST-024** documentation/traceability | The documentation validator is unchanged and passes; the status in every document is checked against the computed status. | `check_docs_status` in `build_release_readiness.py` |

## 5. Files created

### Tools

| File | Lines | Contents |
| --- | ---: | --- |
| `tools/release_readiness/readiness_data.py` | 745 | Evidence records with roles, supersession/rejection declarations, declared-superseded and unverifiable digest tables, the catalog/package identity sources, the per-gate pointer registry, the accepted evidence trees and the status/data literals. |
| `tools/release_readiness/check_revision_consistency.py` | 899 | Groups A–F: accepted-revision agreement (with an ancestor check), catalog identity, package identity, player-digest agreement, supersession/rejection proofs (via `git diff`/`git cat-file`), and stale-claim refusal. `--self-test`, `--json`, `--quiet`. |
| `tools/release_readiness/build_release_readiness.py` | 855 | The completion condition, the manifest/status generator, the documentation-status check, `--build`, `--check`, `--self-test`. |

### Artifacts

| File | Contents |
| --- | --- |
| `artifacts/release-readiness/supported-profile.md` | Written record: profile, unqualified targets, budgets, exception, conditions. |
| `artifacts/release-readiness/outstanding-defects.md` | Written record: defects, deferrals and carried gaps. |
| `artifacts/release-readiness/evidence-manifest.json` / `.md` | **Generated**; do not edit. |
| `artifacts/release-readiness/status.json` | **Generated**; the decision and its inputs. |
| `artifacts/release-readiness/HANDOFF.md` | This document. |

### Documentation modified (status only, plus the new decision entry)

`docs/game-core/10-decisions-and-open-questions.md` (§5, new `ADR-017` with who/when/what/why),
`docs/game-core/README.md` (line 5), `docs/game-core/00-core-protocols.md` (line 3 and the §11
paper-validation sentence), `docs/game-core/07-reference-compositions.md` (line 3 and the freeze
sentence), `docs/game-core/09-implementation-guide.md` (line 3), `README.md` (status bullet, developer
checks, scope pointer), `docs/operator/deferred-scope.md` (§3 table plus pointers).

Nothing in `Packages/`, `dotnet/`, `unity/`, `tests/` or `tools/conformance/` was touched: **no
production code changed, so no earlier wave's suite is invalidated by this task.**

## 6. Exact commands for the Linux build host

Nothing below has been run against a build host. Every command needs Python 3 and `git`; the tools are
standard-library only and work on the build host's Python 3.12.3.

### 6.1 Falsify the tools themselves (no SDK, no Editor)

```sh
python3 tools/release_readiness/check_revision_consistency.py --self-test
python3 tools/release_readiness/build_release_readiness.py --self-test
```

### 6.2 The consistency check and the completion condition (no SDK, no Editor)

```sh
python3 tools/release_readiness/check_revision_consistency.py
python3 tools/release_readiness/build_release_readiness.py            # prints the manifest; exit 0 only when complete
python3 tools/release_readiness/build_release_readiness.py --check    # committed records vs re-derived records
```

`--check` fails if `evidence-manifest.json`, `evidence-manifest.md` or `status.json` is stale, if the
completion condition no longer holds, or if a document's stated status disagrees with the computed one.
Re-generating after an intentional change:

```sh
python3 tools/release_readiness/build_release_readiness.py --build
```

### 6.3 The documentation gate (unchanged by this task)

```sh
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
python3 tools/check_operator_docs.py
```

### 6.4 What was NOT run and is not required here

No `dotnet build`, no `dotnet test`, no Unity invocation and no player run: this task adds no C#, no
Unity asset and no script that the build host must compile. The runtime evidence it reads was produced
by the Wave 8 gate; re-running `tools/reproduce.sh` or `tools/conformance/run_test_matrix.sh` is *not*
part of GC-030, and no new timing run is required or permitted (the owner capped runs at two and
deferred the timing qualification).

## 7. Known gaps, assumptions and doc ambiguities

1. **The accepted revision is a declared value, not a constant in the tool.** It is read from
   `artifacts/w8-gate/reproduction/environment.txt` (the gate's own record) and required to agree with
   six other records. If those records are ever edited to disagree, the tool fails rather than picking
   one.
2. **`artifacts/reproducibility/first-clone/` is retained but is not gate evidence.** The tool asserts
   that the accepted gate report still says the earlier clone was not accepted, and that no accepted
   record cites it. It is listed as a historical record with that reason rather than deleted (evidence
   is archived, not removed).
3. **Both accepted trees are read, and their *derived facts* must agree** — statuses, suite verdicts,
   family verdicts. Their JSON cannot be compared byte-wise (different roots, revisions and embedded
   paths), so the agreement is asserted on the derived values.
4. **The player digest is recorded-only.** The launcher (`GameCoreProbe.x86_64`, 14,784 bytes) is a
   build product and is not committed, so a rebuild on the host may produce a different digest. The
   digest is used for cross-record agreement, never as a recomputed value. The baseline record's
   `GameAssembly.so` digest is the closest thing to a payload identity and is cited in
   `supported-profile.md` §1.
5. **The release project's manifest digest is unverifiable here.** It belongs to the disposable
   `unity/GameCore.ReleaseCheck` clone that `tools/reproduce.sh` creates and deletes. Three records
   hash it; each is declared in `UNVERIFIABLE_DIGESTS` with its exact recorded value, so a change to one
   of those records is still caught.
6. **One historical digest differs on purpose.** The GC-025 baseline records a pre-packaging manifest
   and lock; both exact values are declared in `SUPERSEDED_DIGESTS` with the change that explains them.
   An undeclared differing value fails.
7. **`corpus.problems` in GC-028's canonical index.** Two replay `.trace.json` sidecar paths are
   reported there; declared in `KNOWN_CORPUS_PROBLEMS` with the reason (the index predates the sidecar
   exclusion the Wave 8 gate records). The tool also fails if a declared problem *disappears*, so the
   declaration cannot outlive its cause silently.
8. **Doc ambiguity resolved (00 > 05 > 09).** 09's GC-030 "Expected files" names only
   `artifacts/release-readiness/`; nothing in 00 or 05 constrains its contents, so the files above are
   that task's own record. The status string itself is not in 00: 00 owns requirements, not release
   status, so the exact wording is owned by the decision entry and the generated `status.json`, with 00
   pointing at both.
9. **The status sentence had to be introduced, not merely updated.** Four documents (00, 07, README,
   09) stated that implementation was pending; the wording now names the exact status and the exception
   so a reader cannot read "complete" without the deferral. `docs/operator/deferred-scope.md`'s timing
   row was reworded to carry the owner's exact phrase (`Deferred by project-owner decision`) because
   that is what the decision record says and what the tool checks.
10. **The W7-derived suite matrix is retained** (`artifacts/conformance/suite-status-w7-gate.json`) and
    labelled as derived from the Wave 7 corpus; the tool fails if it is deleted and does not read it as
    accepted-revision evidence.
11. **No inventory row was promoted.** `artifacts/gates/w4-generic-profile/inventory.{md,json}` was not
    modified. GC-028's proposals are recorded in its handoff and restated in `outstanding-defects.md` §5.
12. **No publishing happened.** No registry push, no package publication, no platform claim beyond
    `supported-profile.md` §1.

## 8. What ran on this host (interpreter-level only)

```
python3 tools/release_readiness/check_revision_consistency.py --self-test   # 16 mutations detected
python3 tools/release_readiness/check_revision_consistency.py               # 139 ok, 4 declared skips, 0 fail
python3 tools/release_readiness/build_release_readiness.py --self-test      # complete corpus records the status; 9 mutations refused
python3 tools/release_readiness/build_release_readiness.py                  # gate condition satisfied; manifest printed
python3 tools/release_readiness/build_release_readiness.py --build
python3 tools/release_readiness/build_release_readiness.py --check
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
python3 tools/check_operator_docs.py
python3 -m py_compile tools/release_readiness/{readiness_data,check_revision_consistency,build_release_readiness}.py
```

Neither self-test result is a product result: the first proves the consistency rules reject 16
individually broken corpora, and the second proves the completion condition refuses nine differently
broken ones. The build-host run of `--check` is what certifies the committed records.
