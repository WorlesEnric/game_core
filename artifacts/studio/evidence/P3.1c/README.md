# P3.1c — B-FRAME: FAIL on the real NVIDIA GPU

Measured on **2026-10-06 (+08:00)**, Linux build host **myubuntu** (hostname
`worlesenric`), branch `codex/p3.1c`. Both complete graphical playthroughs fail
the unchanged B-FRAME budget. The save and belfry transition remain below their
applicable 100 ms / 250 ms limits, but p95 and the first two logged frames fail.

Source/player revision: **`c79a38321f2b56592ce4dd9e62ec734e3be38cf9`**, the merged
P4.2 main revision and a descendant of `7051e95`. `git fetch origin && git merge
origin/main` reported already up to date. The later packet commits contain only
the launcher, evidence and documentation; [source-diff.txt](source-diff.txt)
confirms unchanged runtime/package/project sources after generated settings were restored.

## Two runs, one profile

| Measurement | Run 1 | Run 2 |
|---|---:|---:|
| Player exit / all route markers | 0 / complete | 0 / complete |
| Logged frames | 36,493 | 36,548 |
| Window from first `ready` through quit | 608.406 s | 609.220 s |
| **p95, limit 16.7 ms** | **18.122 ms — FAIL** | **17.912 ms — FAIL** |
| **Frames >100 ms outside transition windows, limit 0** | **2 — FAIL** | **2 — FAIL** |
| Marsh → belfry maximum in its 1 s window, limit 250 ms | 18.695 ms — PASS | 18.518 ms — PASS |
| Largest of all transition-window maxima | 65.530 ms — PASS | 62.463 ms — PASS |
| Manual-save worst frame | 80.953 ms (frame 17,196) | 31.579 ms (frame 17,250) |
| Frames >100 ms in manual-save window | 0 | 0 |
| Manual-save main-thread capture diagnostic | 66.642 ms | 28.590 ms |
| **B-FRAME** | **FAIL** | **FAIL** |

Both player logs name **`NVIDIA GeForce RTX 4060 Ti/PCIe/SSE2`**, OpenGLCore,
driver 595.84. Both CSV headers report **1920×1080**. The player launched on the
real Xorg display `:1` with the existing gdm Xauthority, borderless fullscreen,
without batchmode, Xvfb, or screen/video capture. No ETOS operation or service
stop/restart was performed. The existing x11vnc listener was left alone; the
retained connection snapshot has no established viewer connection.

Both runs use the unchanged `games/hollowmere/Autoplay/playthrough.txt`: village
dialogue/items → marsh shrine/clapper → manual save → ferry → belfry → Ending C
“The Freed Echo” → restart → restore slot 1 → final causeway walk → quit 0.
No route marker is missing. **Exactly two measured launches** were made, with
`PROBE_RUNS=2`; no third attempt or profile adjustment followed the failures.

## Window and failure witnesses

The unchanged `games/hollowmere/Tools/frame_stats.py` reproduces the **entire
original P3.1 JSON exactly** ([parity check](stats-parity.txt)). The p95 uses all
rows from the first `ready` marker, including that row, with linear interpolation
and the original three-decimal reporting. Only frames in the inclusive interval
`0 ≤ frame.time − transition.time ≤ 1 second` are exempt from the >100 ms count.
Every inherited load/restore exclusion coincides with a real `region:` marker;
there are no additional non-region exclusion windows. Both runs also exceed
the literal ten-minute duration, not just the legacy statistics script's 594 s check.

The complete list of >100 ms frames outside those windows is:

| Run | Frame | Logger time | Frame time | Marker |
|---|---:|---:|---:|---|
| 1 | 1 | 2.7294 s | 2675.260 ms | `ready\|menu` |
| 1 | 2 | 2.8600 s | 183.973 ms | none |
| 2 | 1 | 2.6477 s | 2606.899 ms | `ready\|menu` |
| 2 | 2 | 2.7532 s | 144.199 ms | none |

These are startup frames, and **07 provides no startup exemption**. They remain
counted. No alternate warm-up-trimmed result is used for acceptance.

The manual-save window follows P3.1b: two frames before the logged `ui save.1`
command through three frames after `saved`, inclusive. This includes the following
frame's delta carrying capture cost. Run 1 is frames 17,193–17,201; run 2 is
17,247–17,255. The raw window rows are retained in each `manual-save.json`.

## Host isolation and wait

The pre-build inventory was empty after 0.056 s; initial 1/5/15-minute load
averages were **1.14 / 1.39 / 1.93**. The build used one shared allocator slot.
Other packets started Editors during the build; no measured player ran then.

The post-build idle wait began **05:34:48**, with load **18.41 / 22.93 / 19.72**
and two Editors. The launcher polled under the allocator protocol for **1086 s
(18 min 6 s)**, within the 60-minute limit. It acquired one reservation and the
allocator mutex, confirmed no Editor/player, and held both through the two runs.
Persistent Unity licensing clients were listed separately: they are neither
Editors nor players and were not stopped.

| Checkpoint | Local time | 1 / 5 / 15-minute load | Other Editors/players |
|---|---|---|---|
| Before run 1 | 05:52:54 | 6.18 / 9.82 / 13.62 | none |
| Before run 2 | 06:03:15 | 2.37 / 3.36 / 8.19 | none |
| After run 2 | 06:13:33 | 2.63 / 2.47 / 5.29 | none |

The reservation/mutex were released after run 2. No other packet's clone or
process was modified. Raw inventories, timestamps, exact commands, route/statistics
hashes, display inspection and OpenGL inspection are under [measurement/](measurement/).

## Build and checks

No qualifying player was available in this clone's P3.1 evidence, so it was rebuilt
from the merged tree through `unity-batch.sh`. The wrapper passed in **1181 s**,
one attempt; the build report says **Succeeded, 0 errors, 7 warnings**, Linux
IL2CPP, non-development. Shipped output: **176,338,014 bytes / 36 files**.
The player stays on this host at
`artifacts/studio/evidence/P3.1c/player/` (git-ignored); the manifest covers the
actual `GameAssembly.so` and data, not just the unchanged Unity executable stub.
Build-generated URP settings changes and generated assets are retained under
`build/`; source settings were restored afterward.

| Check | Result | Evidence |
|---|---|---|
| Linux IL2CPP build | PASS | [wrapper](build/runner.txt), [report](build/build-report.json), [manifest](build/data-manifest.txt) |
| Package metadata | PASS, 42 packages / 91 assemblies | [output](metadata-check.txt) |
| C# checker | PASS, 1198 files | [output](csharp-check.txt) |
| Original statistics parity / original graphical failure witness | PASS | [parity](stats-parity.txt), [regression](analysis-regression.txt) |
| Independent metric recomputation, launcher/report syntax, unchanged budgets and evidence links | PASS | [verification](verification.txt) |
| `P31c_GPU_RequiredProfile` | PASS: old P3.1b llvmpipe/640×480 is unqualified; both new runs meet the profile | [checks](qualification-checks.json) |
| `P31c_GPU_TwoGraphicalPlaythroughs` | PASS: both routes complete, >600 s, exit 0 | [checks](qualification-checks.json) |
| `P31c_BFRAME_UnchangedBudget` | **FAIL in both runs** | [result](result.json) |

No C#/Rust implementation changed, and no additional EditMode, PlayMode, dotnet
or cargo suite was run for this measurement-only packet. No synthetic pass is
substituted for the real-player FAIL.

Commands, from the clone root:

```sh
GC_STUDIO_UNITY_SLOTS=1 studio/tools/unity-batch.sh \
  --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/evidence/P3.1c/build" \
  --label build --timeout 2400 -- \
  -executeMethod Hollowmere.Build.BuildLinuxPlayer \
  -buildOutput "$PWD/artifacts/studio/evidence/P3.1c/player" \
  -buildRevision c79a38321f2b56592ce4dd9e62ec734e3be38cf9
PROBE_RUNS=2 games/hollowmere/Tools/measure_frames_nvidia.sh
python3 artifacts/studio/evidence/P3.1c/analyze.py
```

The launcher refuses an existing measurement directory. Recompute reports from
the retained CSVs with `analyze.py`; do not launch more qualification attempts.
Each run retains its raw CSV, player log, exit code, exact command, save checkpoint,
statistics, route/profile result and manual-save window. `sha256-manifest.txt`
covers the evidence. Raw tool output retains its emitted whitespace; narrowly
scoped `.gitattributes` exemptions keep Git whitespace checks from altering those
bytes. Authored reports, scripts, CSV and JSON remain checked normally.
The [original P3.1 video](../P3.1/recording/playthrough.mp4)
remains the recording evidence; it predates P3.1b and is not relabeled as this run.

## Left open

B-FRAME remains **FAIL**, now measured on the required graphical profile. The
causes of the startup stalls and p95 pacing excess were not isolated: game-code
changes and additional performance probes are outside this packet's exclusive
paths and two-run cap. The save/transition results do not waive the other limits.
