# P4.2h installed live qualification

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`, main after R7 integration. The packet uses its own root `games/hollowmere` project, not sibling clones. Never inspect credential contents, restart etosd, or submit paid operations directly to the node.

## Prerequisites and authority

Read the packet note and contracts first. Merge origin/main, establish no other Unity Editor or sibling project-hub OMP run, then start `monitor-p42h.py` through the process supervisor. Unrelated owner sessions outside the project hub are not blockers. Build the exact source companion with fmt, all-target clippy, tests and release build. `activate-worker-p42h.py` reuses the R6-F immutable installer and preserves the existing USD0.50 designer policy. It verifies the executable/worker digests and checksum no-op, and registers this root project.

The activation adapter intentionally requires the product-baseline checkout before qualification commits. To reproduce after those commits, retain/copy the packet's TOOLS directory into a fresh checkout at the stated baseline; do not weaken the source-revision guard or erase an existing activation receipt.

Run `live-p42h.py baseline` exactly once before paid work, then `live-p42h.py hello`. The baseline and reservations must not be reset. The packet ceiling is two images, two TTS, one describe, no paid 3D and USD1.50 total. This execution used only two images, no TTS/describe, plus one unconfigured 3D refusal. Text requests use the installed worker ceiling. `retain-ledger-p42h.py` collects only this project's tasks; empty task usage is explicitly listed rather than called free execution.

## Install acceptance drivers

After every owned Editor exits:

```sh
python3 artifacts/studio/verification/TOOLS/P42hTasks/launch.py install
```

Copy `P42hMedia/Editor` into `games/hollowmere/Assets/Hollowmere/Tests/P42hMedia/Editor`. Canonical drivers remain under TOOLS; installed copies are removable qualification fixtures. The first compile exposed missing `GameCore.Gameplay.Logic` references in both new harness assemblies; the corrected asmdefs are retained. No product package changes are needed.

## Worker and tray lanes

Each is a separate graphical Editor through the host allocator, display `:1`, no automatic paid replay:

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py move --row W-EDIT-01 --method P42h.Tasks.TaskDriver.JoinedMove
python3 artifacts/studio/verification/TOOLS/live-p42h.py query --row W-ETOS-04 --method P42h.Tasks.TaskDriver.SelectedNpcQuery
python3 artifacts/studio/verification/TOOLS/live-p42h.py cancel --row W-ETOS-05 --method P42h.Tasks.TaskDriver.TrayCancel
python3 artifacts/studio/verification/TOOLS/live-p42h.py reload --row W-ETOS-09 --method P42h.Tasks.TaskDriver.SourceReload
```

Inspect actual captures. `collect-p42h.py` retains exact owned node `tool_call` records using read-only SQLite, never request credentials. Worker prose is not query execution proof. The query really executed but returned zero dialogue rows; the missing production projection is documented, not filled with harness data. After reload exits, restore only the installed owned `CompilePulse.cs` from TOOLS; the original/changed source digests and loaded token are retained.

W-REC-03 has no running-stage cancel endpoint/client seam. Its source-backed prerequisite supersedes the historical absent-app-origin explanation. Do not substitute discard, cancelling a transport task, or killing an installed service.

## Portrait and healer robe

Use separate output directories and start the read-only `P42hMedia/ledger.py --out DIRECTORY` observer through the process supervisor before each Editor. It prints `LEDGER_OBSERVER_READY`. Each original lane may issue exactly one real image capped at USD0.20:

```sh
python3 artifacts/studio/verification/TOOLS/live-p42h.py portrait --row W-EDIT-03 --method P42h.Media.Driver.RunPortrait --workflow-out artifacts/studio/verification/W-EDIT-03/p42h-portrait/workflow
python3 artifacts/studio/verification/TOOLS/live-p42h.py robe --row W-AI-01 --method P42h.Media.Driver.RunRobe --workflow-out artifacts/studio/verification/W-AI-01/p42h-robe/workflow
```

Both use current production `EtosMediaGenerator`, omit request changeSetId, import verified artifacts, and invoke normal History. Robe assignment uses engine `entity.setMaterialTexture`, followed by actual GameBoot/New Game before/applied/undone observations. No tint fallback, direct binding call, repaired candidate or forced undo.

The first portrait observation incorrectly required non-null producer jobId. Real synchronous output has a valid effect key/etosRef and null jobId. The original failure is preserved. `R2_38_P42h_ImageWithoutOptionalJobIdRetainsAuthorityAcrossHistory` fails before and passes after correcting the observer, while refusing a second charge. The original successful import was continued after reopening with `Driver.ResumePortrait`; this method reads the retained import and **cannot generate**. Before continuing, preserve the initial result and failed ledger checkpoint under distinct filenames. No second portrait call occurred.

Successful runtime receipts remain BLOCKED until actual visual review. Inspect the named images, then invoke `P42hMedia/review.py --out DIRECTORY --decision PASS --reviewer Main --observation 'actual visible details'`. Never infer visual success from file existence. The current healer is capsule-shaped; observed cloth texture/material change is not a claim of improved character geometry.

## Standalone frame profiles

```sh
python3 artifacts/studio/verification/TOOLS/P42hPlayer/player.py build --output "$PWD/.evidence/P42hPlayer"
python3 artifacts/studio/verification/TOOLS/P42hPlayer/player.py run --output "$PWD/.evidence/P42hPlayer"
python3 artifacts/studio/verification/TOOLS/P42hPlayer/retain.py --source "$PWD/.evidence/P42hPlayer"
```

The production release Linux IL2CPP build is bound to its source revision. Evidence-only later commits are permitted only after a zero diff for Packages/games/companion/stage between build and harness revisions; both revisions are retained. Four full routes run serially, twice each VSync off/on, real NVIDIA GPU at 1920×1080 on `:1`. The unchanged frame rule and owner definition remain separate. Recordings include capture overhead. Final capture waits for actual frame-pacing readiness and targets only the launched PID's substantial X11 game window; it moves/maps/raises that window without changing input focus. Every run retains an early actual-pixel proof. Desktop-only, auxiliary-window, legacy-title and premature-focus setup failures remain separate, never full-run passes. Large movies stay outside Git with exact path/size/SHA256 manifests.

## Reporting and cleanup

`row-dispositions.json` requires exact receipt assertions before `report_p42h.py` updates only the nine commissioned rows, ROWS/SUMMARY and matrix cells. The completion report gets an append-only dated Addendum. All-row accounting distinguishes fresh current-source passes from inherited revision-specific passes.

Run `retain-ledger-p42h.py`, stop the owned monitor after all workloads, and run `collect-p42h.py`. Keep original failed attempts. Check source/report preservation and evidence hashes, restore only owned generated fixture changes, remove installed throwaway driver copies, and leave the immutable installed companion ready. Never delete or reset creator history to make a check pass.
