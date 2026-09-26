# GC-029 HANDOFF — package a reproducible implementation and operator contract (Wave 8)

Branch `gc-029`, based on `68dc8f7` (the merged Wave 7 revision, `main`). Three commits:

| Commit | Contents |
|---|---|
| `c522c22` | Packaging metadata: 1.0.0 versions, asmdef-derived dependencies, regenerated lock, and `tools/check_package_metadata.py`. |
| `f85ec1b` | Documentation drift reconciliation (05 / 04 / 09 / contracts README) plus three doc comments in `GameCore.Contracts`. |
| `cfe228c` | `docs/operator/**`, the root `README.md`, `tools/reproduce.sh`, `tools/emit_failure_codes.py`, `tools/check_operator_docs.py`. |

**Status of every build/test/player statement in this document and in every artifact this task
produced: `NotRun (pending orchestrator build host)`.** This host has no Unity, no .NET SDK, no mono and no
C# compiler, so nothing here has been compiled, imported, executed or built. What did run is
interpreter-level only and is recorded verbatim in `artifacts/reproducibility/static-checks.log` (§5).

## 1. Deliverable

GC-029's objective: "Make the implemented runtime buildable, diagnosable and accurately scoped by another
engineer."

1. **Package metadata** — every `Packages/com.gamecore.*/package.json` (and the four `tests/` fixture
   packages) now carries version `1.0.0` with an accurate `displayName`, `description` and `unity` field, and
   a `dependencies` map that matches the assemblies its own `asmdef` files actually reference. The lock was
   regenerated from the manifests.
2. **One clean-checkout entry script** — `tools/reproduce.sh` takes a fresh clone to a reproduced profile in
   one command, with a timeout and a named failure per step, a full transcript, and a machine-readable step
   ledger.
3. **Operator docs** — `docs/operator/`, ten pages plus an index: profile, build/run, packages, headless
   control surface, catalog generation, failure codes (**generated from source**), checkpoint/recovery,
   unload/leaks, the Editor-hang runbook, and deferred scope.
4. **Drift reconciliation** — the API/assembly/path names that 04, 05, 09 and the contracts README used but
   that do not exist were corrected to the shipped names. Docs were fixed, not shipped code renamed.

## 2. Files created

### Tooling

| File | Lines | Contents |
|---|---:|---|
| `tools/reproduce.sh` | 540 | The clean-checkout entry point (§3). |
| `tools/check_package_metadata.py` | 453 | Derives each package's expected dependency set from its `asmdef` references and audits the manifests against it. `--self-test` (12 cases), `--sync-lock`, `--json`, `--root`. |
| `tools/emit_failure_codes.py` | 514 | Generates `docs/operator/failure-codes.md` from the source enums. `--check`, `--self-test` (8 cases), `--json`. |
| `tools/check_operator_docs.py` | 283 | Checks operator links/anchors, the documented player flags against `ProbeArguments.cs`, orphan pages, and the generated banner. `--self-test` (8 cases). |

### Documentation

`docs/operator/`: `README.md` (index), `profile.md`, `build-and-run.md`, `packages.md`, `headless.md`,
`catalog-generation.md`, `failure-codes.md` (generated), `checkpoint-and-recovery.md`,
`unload-and-leaks.md`, `editor-hang.md`, `deferred-scope.md`. Plus the new root `README.md`.

### Evidence

`artifacts/reproducibility/static-checks.log`, `artifacts/reproducibility/host/*.json`, this file.

## 3. Exact commands for the Linux build host

Everything runs from the repository root. **Nothing below has been run.** The host-side checks this task *did*
run are in `artifacts/reproducibility/static-checks.log`.

### 3.1 The whole reproduction (one command)

```sh
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
DOTNET=$HOME/.dotnet/dotnet \
  tools/reproduce.sh
```

Run it from a **genuinely fresh clone**. It executes, in order: the toolchain record; the package-metadata
audit and its self-test; the generated-document checks (failure codes, operator docs); `dotnet restore`,
build and test over `dotnet/GameCore.sln`; the host-side checks; the Unity resolve plus a re-audit of the
regenerated lock; EditMode and PlayMode unfiltered; the five codegen/bake invocations followed by
`git diff --exit-code` over the four generated trees; the qualification player build; the three family probes
×2; the marker-free release player (prepare, check, build, surface inspections) and the same three probes ×2
in it; and the documentation validator. Evidence lands in `artifacts/reproducibility/`.

Optional knobs: `ARTIFACTS`, `PROBE_RUNS` (1 or 2 only), `UNITY_TIMEOUT`, `STEP_TIMEOUT`, `PLAYER_TIMEOUT`,
`RELEASE` (0 skips the release player and says so), `RELEASE_PROJECT`, `DOCS`. To record the transcript
somewhere the orchestrator owns:

```sh
UNITY=… DOTNET=… ARTIFACTS=artifacts/reproducibility tools/reproduce.sh
```

### 3.2 The pieces GC-029 owns, if a step needs re-running

```sh
# packaging metadata (no SDK, no Editor)
python3 tools/check_package_metadata.py --self-test
python3 tools/check_package_metadata.py --json artifacts/reproducibility/host/package-metadata.json
python3 tools/check_package_metadata.py --sync-lock        # must be a no-op on this revision

# generated documents (no SDK, no Editor)
python3 tools/emit_failure_codes.py --self-test
python3 tools/emit_failure_codes.py --check
python3 tools/check_operator_docs.py --self-test
python3 tools/check_operator_docs.py

# the reproduction script's own syntax
bash -n tools/reproduce.sh
```

### 3.3 The suites the wave gate owns (unchanged by this task)

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test  dotnet/GameCore.sln -c Release --no-build
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity DOTNET=$HOME/.dotnet/dotnet PROBE_RUNS=2 \
  tools/run_w7_gate.sh
```

## 4. Requirement and test coverage

GC-029's registered requirement and test set is `traceability.json`'s GC-029 entry: requirements P-001,
P-009, P-051, P-055, P-056, P-057, P-058, P-059, P-060; tests TEST-001, TEST-021, TEST-024. That registration
is **unchanged** by this task, and the documentation validator passes with it.

| Requirement | What this task delivers | Where |
|---|---|---|
| **P-001** genre independence | The operator guide states the single qualified profile and that gameplay packages depend on the kernel, never the reverse; the packaging audit enforces the direction. | `docs/operator/profile.md`, `docs/operator/packages.md`, `tools/check_package_metadata.py` |
| **P-009** catalog roots and reachability | Catalog generation procedure, the four committed catalogs, and how byte-identity is proven. | `docs/operator/catalog-generation.md` |
| **P-051** operation discipline | Every protocol operation code has an operator action and recovery; the player's full command-line surface is documented and checked against source. | `docs/operator/failure-codes.md`, `docs/operator/headless.md` |
| **P-055** protocol evolution | Version `1.0.0` on every package, the `UnsupportedVersion` action, and the "no silent fallback" rule. | `docs/operator/packages.md`, `docs/operator/failure-codes.md` |
| **P-056** extension points | The operator guide points at the extension seams and states what is deferred without empty mandatory interfaces. | `docs/operator/deferred-scope.md` |
| **P-057** conformance | The kernel/gameplay dependency direction is audited mechanically; the docs do not claim untested platforms. | `tools/check_package_metadata.py`, `docs/operator/profile.md` |
| **P-058** V1 implementation profile | The exact IL2CPP/High-stripping/Burst/headless profile with the pins that define it, and the release-vs-qualification build shapes. | `docs/operator/profile.md`, `docs/operator/headless.md` |
| **P-059** genre validation | The three family probes are part of `tools/reproduce.sh`, in both build shapes, ×2. | `tools/reproduce.sh` |
| **P-060** evidence and release status | The reproducibility transcript, the step ledger, the environment record, and the explicit deferred-scope page. | `tools/reproduce.sh`, `docs/operator/deferred-scope.md` |
| **TEST-001** toolchain / registration / IL2CPP | `tools/reproduce.sh` checks the toolchain versions, asserts the pinned Editor revision, and runs the IL2CPP player build plus the catalog coverage probe modes. | `tools/reproduce.sh` |
| **TEST-021** genre neutrality | The three family probes run in both players; the shipping-shaped release player is asserted marker-free. | `tools/reproduce.sh` |
| **TEST-024** documentation and traceability | The validator runs as the last step; `docs/operator/` is additionally checked by `tools/check_operator_docs.py`, because the game-core validator does not read that directory. | `tools/reproduce.sh`, `tools/check_operator_docs.py` |

**TEST-023 timing is NOT claimed.** See §6.

## 5. What actually ran here (interpreter-level only)

Verbatim in `artifacts/reproducibility/static-checks.log`:

- `tools/check_package_metadata.py --self-test` — **12/12 cases pass**; the audit reports 21 packages, 46
  package-defined assemblies, and agreement between manifests, asmdefs and the lock. `--sync-lock` is a
  **no-op** on this revision, which is the proof that the committed lock matches the manifests.
- `tools/emit_failure_codes.py --self-test` — **8/8 cases pass**, including agreement with the real sources;
  `--check` passes (22 operation codes, 17 catalog codes, 11 narrative refusals).
- `tools/check_operator_docs.py --self-test` — **8/8 cases pass**; the real run resolves 49 local links across
  11 pages and matches **26/26** documented probe modes against `ProbeArguments.cs`.
- `tools/check_game_core_csharp.py` (623 files), `check_contract_surface_parity.py`,
  `check_budget_record.py`, `check_link_xml.py`, `check_release_clone.py --self-test`,
  `check_release_fault_free.py --no-build`, `check_release_telemetry_free.py --no-build`, the four
  `verify_generated_catalog.py` runs, the three emitter mirrors, `compare_registration_fingerprints.py
  --self-test` — **all exit 0**.
- `tools/validate_game_core_docs.py --self-test` (9 fixtures) and the full validator (14 documents) — **pass**.
- `bash -n` over `tools/reproduce.sh` and the five harnesses it calls; `py_compile` over the three new tools.
- **A smoke run of `tools/reproduce.sh`'s own control flow**, which proves the prerequisite checks, the
  sequential step numbering, the per-step `timeout` wrapping, the named failure message and the step ledger.
  It necessarily stops at `dotnet restore` (exit 127) because this host has no .NET SDK. The ledger it wrote
  is in the log.

None of that is a build or a test result for the runtime.

## 6. Known gaps, assumptions and decisions

1. **TEST-023 timing is `Deferred (owner decision)`, never Pass.** `docs/operator/deferred-scope.md` lists the
   ten budget rows with the measured diagnostic numbers and names the **open whole-world prepare-cost issue**
   (~0.9–1.1 s per whole-world change at 10,000 targets against a 100 ms target, ≈9–11× over), the
   `MissedTarget` verdict, and the fact that no target was revised. `tools/reproduce.sh` does not run the
   long catalogue and says so in its transcript. This is the one place a reader could mistake deferral for
   success, so it is stated in three places.
2. **The player bundle version is `0.1.0` and was deliberately left alone.** It is `PlayerSettings.bundleVersion`
   (`ProjectSettings.asset`, also set in `BuildProbe.cs:102`), which is the *player's* version string, not a
   package version; nothing asserts it and it is not referenced by any check or gate. Changing it is outside
   "package metadata" and would edit a shared ProjectSettings file for no functional gain. Documented in
   `docs/operator/profile.md` §5 so it is not hidden.
3. **Most `00 s9` citations in source comments were left unchanged, on purpose.** The failure-code list is
   P-052 in 00 §7, while 00 §9 is "Visibility and common operation rules". A repository-wide grep finds ~32
   `00 s9` citations in C# comments, and **most of them are correct**: the control-lane/visibility rules
   ("no live writes before publication", "reports as `Rejected`", "must never be mistaken for world
   observation") genuinely are §9. Three citations were wrong because they attributed the *code list* to §9,
   and those are in the two types `docs/operator/failure-codes.md` cites, so they are fixed in `f85ec1b`
   (`Diagnostic`'s summary, `DiagnosticCode`'s summary, and `DiagnosticCodeText.Values`' doc comment). A
   blanket replace of the remaining citations would have introduced new errors; each needs reading, and they
   live in packages other tasks own. Recorded here rather than silently left.
4. **`--sync-lock` exists because the lock is regenerated by Unity.** A local package's `version` field in
   `packages-lock.json` is a `file:` path, not a semver, so the manifests are the single source of truth for
   versions. `tools/reproduce.sh` re-audits the lock immediately after the resolve, which is the moment a
   version or dependency edit would surface as a resolution problem.
5. **`docs/operator/` is deliberately outside `docs/game-core/`.** The game-core validator's `REQUIRED_DOCS`
   and its ID registry govern the normative design set; adding an `11-*.md` there would put an implementation
   contract inside the protocol authority chain and would make its `P-*`/`GC-*` references subject to the
   registry. `tools/check_operator_docs.py` is the checker for the operator directory instead, and
   `tools/reproduce.sh` runs both.
6. **`PROBE_RUNS` is capped at 2 in `tools/reproduce.sh`,** matching the project-owner decision. The script
   *rejects* any other value rather than clamping it, so a run cannot silently do something different from
   what was asked.
7. **Assumption: the orchestrator runs `tools/reproduce.sh` from a genuinely fresh clone.** The release step
   refuses to proceed if `unity/GameCore.ReleaseCheck` already exists, because that clone is disposable and its
   presence means a previous run left state behind. The clone path is in `.gitignore`.
8. **Doc ambiguity resolved.** 09's GC-029 "Expected files" line names no literal path, and 09's GC-001 line
   named a repository-root `Packages/manifest.json`, which does not exist. Both are reconciled: the GC-001
   paths now name the Unity project's own `Packages/`, and GC-029's outputs are the paths this task created.
   The reading follows 00's authority order (00 > 05 > 09): packaging metadata belongs to 09's concrete-work
   list, and nothing in 00 or 05 constrains where a package manifest lives.
9. **No inventory row was promoted.** The brief allows promotions to be *proposed* in HANDOFF only; no row in
   `artifacts/gates/w4-generic-profile/inventory.{md,json}` was touched, and no production behaviour changed,
   so **no existing suite evidence is invalidated** by this task. The only source changes are three doc
   comments.
10. **The `Packages/com.gamecore.fault-qualification.meta` stray file was left as-is.** It is a tracked
    folder `.meta` with no sibling directory (the real package directory exists beside it). Removing it is not
    packaging metadata, no tool references it, and `tools/make_unity_metas.py` does not create it. Flagged
    here for a future cleanup task rather than changed in a packaging commit.

## 7. Suggested review order

1. `git show c522c22 --stat` then `python3 tools/check_package_metadata.py` — the packaging claim is
   mechanically checkable, so check it first.
2. `docs/operator/profile.md` and `docs/operator/deferred-scope.md` — the two pages that bound what may be
   claimed about this revision.
3. `tools/reproduce.sh`'s header, then `docs/operator/build-and-run.md` §3 — the entry point and its
   documented phases; confirm they agree.
4. `git show f85ec1b` — the drift fixes, each of which is a doc naming something that does not exist.
5. `python3 tools/emit_failure_codes.py --check` — proves the failure-code table is not hand-maintained.
