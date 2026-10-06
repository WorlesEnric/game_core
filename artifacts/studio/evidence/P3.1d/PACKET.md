# P3.1d — Hollowmere real-GPU B-FRAME

Branch `codex/p3.1d`, base `1752ca8a`. Final runtime/build revision **`f67a6de368263c21df86a1ce81d15e8eeeea0a23`**.
Host myubuntu, 2026-10-06 (+08:00). **Default uncapped: PASS in both final runs. VSync enabled: FAIL on p95 only.**

## R2 fixes

These are packet-specific IDs for the B-FRAME subset of R2-38.

| Finding | Profiled cause and final fix | Regression / proof |
|---|---|---|
| P31d-PACING | VSync presentation wait dominates the ~18 ms p95; GameBoot now defaults to uncapped rendering, with `-frameVsync 0\|1` for explicit comparison. No quality downgrade. | `P31d_PACING_DefaultIsUncappedAndExplicitVsyncIsHonored`; unchanged budget check fails on P3.1c and passes on both final default runs. |
| P31d-BOOT | `Attach` emitted ready before presentation. First-render layout, font, binding and render setup are CPU-heavy. Render the real menu under an opaque loading curtain with controls disabled, then expose/enable it and focus `menu-new`. Region IO waits until after the first menu presentation frames. | `P31d_BOOT_AttachDoesNotClaimAnUnreadyMenu` fails on baseline XML, passes after; `P31d_BOOT_RegionWaitsForPresentedMenu`; all four final ready rows match real presentation frame 3, input enabled and focus `menu-new`. |
| P31d-LOG | Native frame 10680 attributes **704.595 ms** of a **706.662 ms** frame to `FrameLogRecorder.LateUpdate`, with **705.763 ms** render-thread wait and only **0.088 ms GC.Alloc**. Final recording retains immutable 120-frame blocks in memory; explicit Flush/shutdown coalesces/writes them and surfaces errors. Automatic recording performs no IO or task scheduling. | `P31d_LOG_AutomaticFlushDoesNotWaitForStorage` fails with the blocking automatic flush and passes after; `P31d_LOG_WritesStayOrderedAndFailuresSurfaceAtDrain`; all four final runs have zero post-ready stalls. |

The recorder runs after game/session LateUpdate. `boot-warmup` deliberately has no `load` substring and cannot
manufacture a transition exemption. **The statistics algorithm, clocks, sample values and budgets are unchanged.**
All startup rows remain retained; the menu is physically covered and disabled during warmup. No logger-only ready
delay, dt clamping, dropped rows or new statistical exclusion was added. The first visible, responsive menu frame
is included in the ready window.

## Profile first

Baseline development player **`352b0499`**, :1 / RTX 4060 Ti / OpenGLCore / 1920×1080 borderless; no deep profiling.
Short village profiles precede performance changes. Their steady diagnostic windows (after 12 s only for
attribution) have uncapped p95 **2.857 / 2.852 ms**, versus VSync **18.118 / 17.945 ms**. Median VSync main work is
**2.390 / 2.417 ms**, present wait **14.227 / 14.188 ms**, render-thread p95 **0.780 / 0.782 ms**. Native
`Gfx.PresentFrame` / `WaitForTargetFPS` agrees. No shared plugin or visual quality reduction was justified.

Separate 120-frame startup captures retain initialization. A representative first gameplay frame has
GameBoot.Start **51.918 ms**, HollowmereGame.Start **9.958 ms**, UI layout **20.943 ms**, UI repaint **91.764 ms**,
and finish-frame rendering **61.805 ms**. These are inclusive/nested samples, not additive independent costs.
The first logged delta also spans preceding engine/splash startup.

Long captures initially exported a 300-frame tail; the later 2000-frame export of the **same capture** includes
the logger stall. See `profile-baseline/flush-stall.json`. All eight original native captures remain at the host
paths and SHA-256 values in `capture-manifest.json`; compressed native sample exports are committed.
`profile-baseline/measurement/profile-summary.json` and `profile-startup/measurement/profile-summary.json`
retain the counter/sample summaries. GPU-duration counters return zero on this OpenGL player and are unavailable,
not zero-cost GPU evidence.

## Final real-GPU measurements

Exactly `PROBE_RUNS=2` per VSync state on the final player, unchanged `Autoplay/playthrough.txt`, no capture,
no profiler instrumentation, no other Editor/player. One allocator reservation plus its mutex covers all four
launches. Exact commands and before/after inventories are in each run directory. Authoritative report:
[measurement/qualification.json](measurement/qualification.json).

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

All four players exit **0** and reach menu → new game → three regions → save → Ending C → restart → restore →
final walk. Every manual-save window has zero >100 ms frames. Its window remains two frames before `ui save.1`
through three frames after `saved`, inclusive. All profile, route, readiness, focus, save and inventory checks pass.

**VSync ambiguity:** 07 does not define VSync. Its rule is unchanged and both states are reported. The game's
new default (off/uncapped) passes; the explicitly enabled state fails the p95 limit. There is no unqualified
claim that the VSync-enabled profile passes. The original P3.1 video is unchanged; no new recording was made.

## Intermediate failures retained

- `measurement-before-logger/`, **703e5509**: two complete uncapped routes, p95 **2.873 / 2.855 ms**, >100 ms
  **7 / 0**. All seven stalls follow synchronous 120-frame flushes. This superseded sequence stopped after the
  second player exited, before VSync runs.
- `measurement-worker/`, **a5dc112d**: one complete uncapped route, p95 **2.877 ms**, >100 ms **5**. Background
  filesystem writing did not suffice; that sequence stopped after this completed player, before further runs.
- These three intermediate full runs are visible, not relabeled as passes or erased. The final four runs use
  changed code; no unchanged failing binary was repeatedly sampled to obtain a lucky pass.
- The first profiler exporter failed on an unnamed render sample; null-name handling/thread enumeration fixed
  it. The combined export/test invocation produced no XML and is not test evidence. The separate baseline
  readiness test failed for the expected premature marker. A preliminary scoped suite exposed a SendMessage
  fixture assertion after Play Mode; direct invocation fixed that fixture.
- After all **four final player exits, host-after inventories and statistics files**, the launcher returned 127
  (`line 111: 3: command not found`) following an in-place provenance-text edit. The launch-time script is retained
  as `measurement/launcher-at-start.sh`; `wrapper-result.json` records the error. The report was run offline and
  all non-budget checks pass. Current scripts pass `bash -n`. No player was rerun for this trailing shell error.

## Verification

| Check on final runtime | Result | Evidence |
|---|---|---|
| Full EditMode | **468 passed, 0 failed, 20 skipped** (488) | `tests/editmode-buffered.xml` |
| Full PlayMode | **22/22 passed** | `tests/playmode-buffered.xml` |
| Five P31d regressions + BakeVerifies | **Passed** | full EditMode XML; baseline failures in `baseline-test-only.xml`, `logger-before.xml` |
| Metadata / C# | **PASS**, 42 packages / 1207 C# files | `metadata-check.txt`, `csharp-check.txt` |
| Linux IL2CPP release build | **PASS**, 0 errors, 5 warnings; 83 s wrapper / 43.413 s build | `build/release-buffered-*.log`, `build/build-report.json` |
| Player smoke | **exit 0**, 1875 frames | `smoke/result.json` |
| Final GPU routes | **4/4 complete**, per-state budgets above | `measurement/` |

XML is authoritative. The 20 unchanged skips are explicit/live ETOS/voice/workflow or graphical Editor fixtures;
they are listed by name/reason in `tests/results.json` and are not counted as passes. No paid gate was enabled.
No dotnet/Rust source or shared package changed. No authored content, bake or Studio authoring journal change is
committed. Build-generated settings were preserved beside the build then restored. Test-created fixture journals
were removed with hashes in `tests/fixture-cleanup.json`; incidental historical memory output was retained here
and its original file restored. Shipped player file hashes are in `build/shipped-files.json`.

## Requests to other packets

None. All implementation changes are game-owned; no shared runtime seam was required.

## Left open

- 07's VSync policy is unspecified. Default/off passes; on fails p95 **18.062 / 18.089 ms**. The row preserves both
  measured dispositions without relaxing any budget.
- Direct GPU-duration counters are unavailable on this OpenGL profile. Attribution uses CPU/present/render
  timings, native samples and controlled VSync comparison; no GPU-millisecond value is claimed.
- The exact mechanism of the worker-version residual stalls was not isolated with a native capture of that
  intermediate revision. Do not attribute them to GC/GPU. The original 704.595 ms synchronous logger stall is
  directly profiled; final acceptance is based on the completed memory-buffered runs.
- The opt-in frame recorder uses memory proportional to run length and can lose unflushed samples on abrupt
  process death. Explicit Flush/normal shutdown publishes all rows. Normal play without `-frameLog` has no recorder.
- The 20 skipped Editor cases and unrelated verification rows remain outside this performance qualification.

Profiler API references: Unity's [command-line profiling](https://docs.unity.com/en-us/engine/6000.5/manual/analysis/profiler/command-line-arguments),
[frame timing explanation](https://unity.com/blog/engine-platform/detecting-performance-bottlenecks-with-unity-frame-timing-manager),
and [6000.0 native frame-data API](https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Modules/ProfilerEditor/Public/RawFrameDataView.bindings.cs).
