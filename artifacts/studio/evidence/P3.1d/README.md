# P3.1d — Hollowmere real-GPU frame performance

Final implementation/build revision: `f67a6de3`; intermediate revisions: `703e5509` and `a5dc112d` (branch `codex/p3.1d`, base `1752ca8a`).
Host myubuntu, Xorg `:1`, NVIDIA RTX 4060 Ti, OpenGLCore, 1920×1080 borderless.
No capture during qualification, no other Editor/player, no service operations or paid ETOS calls.

## Final result

**PASS in the default uncapped profile; FAIL on p95 with VSync enabled.**

| Metric | VSync off 1 | VSync off 2 | VSync on 1 | VSync on 2 |
|---|---:|---:|---:|---:|
| Duration (s) | 607.359 | 609.984 | 608.382 | 608.196 |
| p95 (ms; ≤16.7) | 2.778 | 2.856 | 18.062 | 18.089 |
| >100 ms outside transitions | 0 | 0 | 0 | 0 |
| Worst transition (ms; ≤250) | 87.853 | 91.395 | 93.913 | 85.264 |
| Marsh→belfry (ms) | 10.03 | 8.522 | 19.982 | 19.666 |
| Manual-save worst frame (ms) | 46.235 | 47.326 | 48.542 | 45.811 |
| Manual-save capture diagnostic (ms) | 42.433 | 43.383 | 41.9 | 41.434 |
| First ready frame (frame 3; ms) | 1.388 | 3.424 | 2.151 | 1.830 |
| B-FRAME | PASS | PASS | FAIL | FAIL |

All four final players exit 0. Full final EditMode: 468 passed, zero failed, 20 unchanged skips.
PlayMode: 22/22. Release build and 1875-frame smoke pass. See [PACKET.md](PACKET.md) for baseline
failures, profiler attribution, exact test dispositions and the retained post-run launcher error.

## Method

07 does not specify VSync. Both states use the unchanged `Autoplay/playthrough.txt` and
unchanged `Tools/frame_stats.py`, two full runs each. The default game is now uncapped.
The 16.7/100/250 ms limits and first-ready/transition rules remain unchanged.

```
PROBE_RUNS=2 games/hollowmere/Tools/measure_frames_p31d.sh
python3 games/hollowmere/Tools/qualify_p31d.py artifacts/studio/evidence/P3.1d/measurement
```

The launcher reserves one host Unity slot and holds the allocator mutex from the zero-Editor
inventory through all four player exits. Each run has its exact command, before/after inventory,
player log, frame CSV, statistics, isolated save files and exit code.

## Profile and fix

The baseline development player is `352b0499`. Short village profiles precede the performance
changes. `profile-baseline/measurement/profile-summary.json` has the counters and native sample
summaries. VSync p95 is 18.118/17.945 ms, while uncapped is 2.857/2.852 ms. Median VSync main-thread
work is ~2.4 ms and presentation wait ~14.2 ms. Native `Gfx.PresentFrame`/`WaitForTargetFPS`
samples agree: this is pacing, not a demonstrated need to lower authored rendering quality.
GPU-duration counters are unavailable on this OpenGL profile (zero is not a measured GPU cost).

`profile-startup/measurement/` retains separate 120-frame captures because Unity's long-capture
history view retains only the tail. Startup samples show CPU initialization, menu layout/font/
binding work and first-render setup. The game now presents a loading curtain with disabled menu
controls while the real menu renders and warms. It reveals/enables the menu after those rendering
steps and restores focus to `menu-new`. Region IO starts after the first presented menu frames.
The readiness marker follows that actual presentation state, not merely attaching the game.

All startup rows remain in the CSV. `boot-warmup` contains no `load` substring, so it cannot be
classified as a region/load transition by the existing algorithm. The logger runs after the
session/game LateUpdate methods, retaining the first visible menu frame in its ready row.

The original native captures are retained on this host at the paths and SHA-256 values in
[capture-manifest.json](capture-manifest.json). Compressed exported sample tables are committed.
These development profiles, including their shorter duration and profiler IO stalls, are diagnostic
runs only. The release qualification has no native profiling or timing-recorder overhead.

The first intermediate release exposed intermittent synchronous frame-log IO stalls: two complete uncapped
runs had p95 2.873/2.855 ms and >100 ms counts 7/0. They remain in `measurement-before-logger/`.
The existing native capture, re-exported with a 2000-frame history window, attributes 704.595 ms of a
706.662 ms frame to `FrameLogRecorder.LateUpdate` (see `profile-baseline/flush-stall.json`). Automatic
flushes now retain immutable blocks in memory; explicit flush/shutdown coalesces and writes them and surfaces failures. The slow
storage regression fails with the blocking call and passes only when automatic flushes stop waiting.
The intermediate sequence was stopped after its second uncapped player exited, before any VSync run.
A worker-writing intermediate (`a5dc112d`) completed one uncapped route with p95 2.877 ms and five stalls;
it is retained in `measurement-worker/` and its sequence was also stopped before further runs. Its residual
stall mechanism is not claimed as diagnosed. The final recorder performs no filesystem activity/task scheduling
from automatic recording. Final qualification is a fresh two-run-per-state series on `f67a6de3`, not a retry
of an unchanged failing binary. All three intermediate full runs remain visible in the evidence.

## Verification and results

Final results are recorded in [PACKET.md](PACKET.md) and the measurement qualification report.
Full EditMode and PlayMode XML is authoritative; explicit/environment/graphics skips are reported
separately. The baseline readiness regression failed for the expected premature `ready` marker.
The first combined profiler-export/test attempt produced no XML and is retained as failed;
the initial game-focused regression run exposed a test-fixture SendMessage assertion, subsequently
fixed by directly invoking the same recorder methods.

No shared package, authored content, bake or authoring journal change is part of this packet.

The opt-in frame log buffers memory proportional to recording duration. Abrupt process death can lose buffered
samples; explicit Flush and normal shutdown publish them. This is a documented diagnostic tradeoff for measuring
completed playthroughs without storage activity inside the measured gameplay window. Normal play without
`-frameLog` has no recorder or recording buffer.

## Retention and reproduction

Frame CSVs are losslessly compressed as `frame-log.csv.gz`. `frame-log-integrity.json` records original/compressed
sizes and SHA-256 values; every final log has consecutive frames 1 through its last frame, with no omissions or
duplicates. `qualify_p31d.py` reads compressed logs directly. To rerun the unchanged statistics implementation,
decompress the relevant `frame-log.csv.gz` beside its report, then run `frame_stats.py <run-directory>`.
The two uncapped CSVs are 13,335,949 / 13,205,771 bytes (roughly twice those character sizes in managed recording
strings, plus container overhead); VSync CSVs are 1,425,095 / 1,424,731 bytes. This is a storage-size witness,
not a peak-memory-profiler measurement.

`measurement/wrapper-result.json` retains the trailing launcher error after all four completed runs. The
launch-time script/provenance is retained unchanged; the wording fix was committed while it was running.
Offline qualification succeeded from all four complete logs, and no additional player was launched.
