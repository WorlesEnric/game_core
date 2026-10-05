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

Host results so far: boundary regressions 2/2, metadata self-tests 31/31, stage-slot
self-tests 29/29, stage Python 16/16; dotnet Gameplay 307/307 and Model 104/104
(TRX counters); Rust fmt/clippy clean, ordinary cargo suite 114 passed / 0 failed /
7 ignored external fixtures. The initial parallel dotnet restore collision is retained;
the sequential rerun passed.

Unity tests use `bash studio/tools/unity-batch.sh`, one Editor at a time,
with result XML retained under `studio/agent/evidence/adapt-split/`.
The STAGE-INT acceptance harness is copied to a temporary crate by `prepare-harness.py`;
only repository/binary, scratch-root and evidence paths change. Its authentication fixture,
passing-verdict assertions and production default Docker confinement are unchanged.
No installed companion/node or sibling clone is modified.

## Requests to other packets

- P3.1/integrator: the named `games/hollowmere/Assets/Hollowmere/Authoring/Editor/`
  directory, `HollowmereStudioAdmission.cs` and `P31AdmissionInPlayMode` do not exist
  at base `5eefd3d4`. When integrating that bootstrap, add
  `GameCore.Studio.Gameplay.Editor` to its Editor asmdef references. Existing public
  `GameCore.Gameplay.World.Editor.StudioAdmissionServices` and `WorldLiveOpTranslator`
  signatures are preserved. Run `P31AdmissionInPlayMode` on the combined revision.

## Left open

- P31 admission acceptance cannot be claimed on this baseline: its source/test is absent.
  The exact requested standalone PlayMode filter selects zero cases (NotRun); R2_G
  lives in its EditMode assembly, including a test that enters a real Play world.
  All 18 R2_G cases passed in the initial broad EditMode run. That run had 316 passed,
  one Views metadata failure due to cleanproof's not-yet-resolved lock, and six
  expected live-node/graphics skips; a final rerun follows cleanproof resolution.
- The companion manifest is already Rust 2024 / 1.97.1; this packet does not own it.
  No companion production Rust source or edition is changed.
