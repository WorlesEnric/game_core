# ADAPT-SPLIT — Studio gameplay adapters

Branch `codex/adapt-split`, base `5eefd3d4`, Linux build host myubuntu.

## R2 fixes

| Finding / blocker | Fix | Regression |
|---|---|---|
| R2-11 / STAGE-INT dependency closure | Gameplay World no longer requires Studio core or Newtonsoft. New Editor-only `com.gamecore.studio.gameplay` owns the two Studio adapters. The stage's existing `com.gamecore.studio.*` exclusion applies unchanged. | `test_R2_11_ADAPT_SPLIT_gameplay_closure_excludes_studio` |
| R2-06 / R2-14 adapter placement | `git mv` preserves both source files and their metas byte-for-byte, namespaces, overloads, first-party InitializeOnLoad registration and smoke polling. World Editor retains gameplay authoring tools; the R2_G test assembly references the new assembly. | `test_R2_06_ADAPT_SPLIT_world_editor_has_no_studio_reference`; existing R2_G live/admission tests |

Both new regressions fail on the baseline and pass after the move; transcripts are in
`studio/agent/evidence/adapt-split/boundary-{before,after}.txt`.
The new assembly needs only composition, Studio core/model, Unity App and Newtonsoft;
no unused gameplay/dialogue/audio references or new contracts are introduced.
Both games install the new package. Unity resolves their locks during host validation.

## Validation

Linux host, implementation and resolved locks at `0a2619cf` (adapter move `52624475`).
Both games compile. All counts below come from XML/TRX where applicable.

| Check | Result |
|---|---|
| New boundary regressions | 2/2; both fail before and pass after |
| Metadata / self-tests | 42 packages, 91 assemblies, exact dependencies; 31/31 self-tests |
| C# policy | 1,137 files pass |
| Stage-slot checker / stage Python | 29/29 and 16/16 |
| Rust fmt / clippy / ordinary cargo suite | clean; 114 passed, 0 failed, 7 ignored external fixtures |
| dotnet Gameplay / Model | 307/307 and 104/104 |
| Hollowmere requested EditMode selection | 323 total: **317 passed, 0 failed, 6 skipped** |
| Hollowmere requested standalone PlayMode selection | **0 cases, NotRun** (baseline lacks P3.1 and R2_G is an Editor assembly) |
| Supplemental Hollowmere P1_1 PlayMode | **1/1 passed** |
| Saltmarsh EditMode / PlayMode | **3/3 and 3/3 passed**, no skips |
| Default Docker pressure-plate stage | **BLOCKED**: mandatory Unity probe fails before the pipeline; no signed verdict (authenticated fetch 404) |

Final EditMode groups: R2_G 18, R2_B 71, P1_7b 28, P2_4 8, P1_1 16,
GameCore.Studio 176 passed + 6 skipped. Four skips are opt-in live ETOS cases;
two require graphics. The wrapper correctly reports partial acceptance, never an
all-pass result. No live/paid test was enabled to eliminate those skips.
The exact requested PlayMode filter was executed unchanged and selected zero cases;
the supplemental world test does not substitute for missing P31 admission coverage.

The first broad EditMode run retained 316 passed / 1 failed / 6 skipped: the Views
metadata test observed cleanproof's not-yet-resolved lock. After Unity resolved both
locks, the full rerun passed that test with no test changes. The initial parallel
dotnet restore collision is also retained; the sequential rerun passed all 307 tests.
Cleanproof's Library was seeded from this clone's completed Hollowmere Library;
no sibling clone was accessed. Unity used the shared allocator, one Editor per packet.
Generated normalization of 28 pre-existing Hollowmere assets was restored after tests.

Evidence and exact stage reproduction:
[PACKET.md](../../../studio/agent/evidence/adapt-split/PACKET.md),
[test-summary.json](../../../studio/agent/evidence/adapt-split/test-summary.json).
`prepare-harness.py` copies STAGE-INT's acceptance to a temporary crate, changing only
repo/binary, scratch-root and evidence paths and retaining Cargo pins. Its authenticated
fetch/verify assertions and production default Docker confinement remain unchanged.
No installed companion/node, STAGE-INT evidence/state or sibling clone is modified.

## Docker stage outcome

The copied STAGE-INT acceptance was explicitly executed once against `0a2619cf`.
It failed at the mandatory default-Docker Unity startup probe, **before scan, Rules,
candidate EditMode/PlayMode or the seven-step verdict pipeline**. The stable refusal
is `stage_failed`, reason `sandbox_unavailable`. Unity licensed successfully, then
Bee threw `System.ArgumentOutOfRangeException`: its Unix-domain socket pathname was
**203 characters**, above the **108-character** platform maximum. The probe Editor
exited 1 after 4 seconds; no candidate code was executed.

`studio/agent/src/stage/sandbox.rs:163` sets `TMPDIR` to the full host slot path;
line 187 already mounts that same job-private directory at `/tmp` inside Docker.
This launcher path is outside ADAPT-SPLIT's exclusive edit set. No host fallback,
probe bypass, shortened-owner namespace or candidate exception was introduced.

The authenticated verdict endpoint returned **404**, retained in `issuance.json`.
`service-job.json`, `service-test.txt`, `stage-log-tails.txt` and
`probe-failure-summary.json` retain the redacted refusal. **No StageVerdict JSON or
candidate EditMode/PlayMode XML exists**, so no signed pass, Rules pass, candidate
Unity pass or warm B-STAGE measurement is claimed for this attempt. `stage-timing.json`
records the service elapsed time separately; verdict `durationMs`, `budgetMs` and
`coldCache` are null because the pipeline never issued a verdict. Configured B-STAGE
remains 360,000 ms. No second performance run was useful with the same probe blocker.

A separate non-executing `make-slot.py` preparation plus `check_stage_slot.py` passes
for the real pressure-plate candidate (52 files, nine inputs). Its actual manifest
contains 31 GameCore packages, zero Studio packages and zero missing GameCore
transitive dependencies (`closure-manifest.json`, `closure-result.json`). This,
together with the before/after closure regressions, establishes the dependency split;
it is explicitly not a substitute for confined Unity execution or signed admission.

## Requests to other packets

- STAGE-INT / R2-F launcher owner: in
  `studio/agent/src/stage/sandbox.rs`, preserve
  `Sandbox::command(&self, executable: &Path, private_home: &Path) -> Result<Command, String>`
  and set the Docker child's `TMPDIR=/tmp`, using the existing job-private `/tmp`
  bind mount. Do not expose host `/tmp` or alter network/readonly/capability limits.
  Add a regression with a full app/project-owned slot path exceeding 108 characters,
  then rerun `r2_11_stage_int_docker_pressure_signed_verdict` and warm B-STAGE.

- P3.1/integrator: the named `games/hollowmere/Assets/Hollowmere/Authoring/Editor/`
  directory, `HollowmereStudioAdmission.cs` and `P31AdmissionInPlayMode` do not exist
  at base `5eefd3d4`. When integrating that bootstrap, add
  `GameCore.Studio.Gameplay.Editor` to its Editor asmdef references. Existing public
  `GameCore.Gameplay.World.Editor.StudioAdmissionServices` and `WorldLiveOpTranslator`
  signatures are preserved. Run `P31AdmissionInPlayMode` on the combined revision.

## Left open

- Confined stage execution and signed passing authority are blocked by the launcher
  socket-path defect above. Candidate XML and warm/coldCache verdict evidence do not
  exist for this refused run; the retained nulls are unavailable values, not passes.

- P31 admission acceptance cannot be claimed on this baseline: its source/test is absent.
  The exact requested standalone PlayMode filter selects zero cases (NotRun); R2_G
  lives in its EditMode assembly, including a test that enters a real Play world.
  All 18 R2_G cases passed in both broad EditMode runs. The six live/graphics
  skips require separately authorized live-node or graphical qualification.
- The companion manifest is already Rust 2024 / 1.97.1; this packet does not own it.
  No companion production Rust source or edition is changed.
