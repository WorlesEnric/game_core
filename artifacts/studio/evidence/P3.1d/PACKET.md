# P3.1d — real-GPU frame performance

Branch `codex/p3.1d`, base `1752ca8a`. Only owned game, evidence, B-FRAME cell and appended P3.1b note paths.

## Profile first

07 does not specify VSync or an uncapped rate. Preserve the row rule and measure both VSync states on Xorg :1, RTX 4060 Ti, 1920×1080 borderless, without capture or another Editor. Development diagnostics precede performance changes. No deep profiling. Opt-in frame timing/ProfilerRecorder samples and native profiler captures are retained separately from qualification.

## R2 fixes

Pending profiler attribution (B-FRAME subset of R2-38; packet-specific IDs below).

## Requests to other packets

None identified.

## Left open

- VSync qualification policy is unspecified in 07; both states will be reported without changing its budget.

Profiler API references (Unity primary sources):
- https://docs.unity.com/en-us/engine/6000.5/manual/analysis/profiler/command-line-arguments
- https://unity.com/blog/engine-platform/detecting-performance-bottlenecks-with-unity-frame-timing-manager
- https://github.com/Unity-Technologies/UnityCsReference/blob/6000.0/Modules/ProfilerEditor/Public/RawFrameDataView.bindings.cs

`profile_stats.py` excludes the first 12 seconds only for *diagnostic attribution* of steady village work. `frame_stats.py` is unchanged and qualification still includes every row from the first ready marker, with the original transition rule.

## Baseline attribution

Development player `352b0499`, zero build errors; native binary profiling (no deep profiling), plus `FrameTimingManager` and `ProfilerRecorder`. Four short village profiles and four separately capped 120-frame startup profiles, all on :1/NVIDIA/1080p under the allocator mutex with zero Editors. These are diagnostic samples, not additional acceptance attempts.

Steady village (after 12 s, diagnostics only): uncapped p95 **2.857 / 2.852 ms**; VSync p95 **18.118 / 17.945 ms**. VSync main-thread median **2.390 / 2.417 ms**, present wait median **14.227 / 14.188 ms**, render-thread p95 **0.780 / 0.782 ms**. Native samples independently show `Gfx.PresentFrame` / `WaitForTargetFPS` dominating; kernel pump is about 1 ms in the captured VSync tail. Thus the ~18 ms steady excess is presentation pacing, not a demonstrated need to reduce visual quality or change shared plugins.

The startup capture shows real first-render initialization on the CPU: representative frame 1 has GameBoot.Start 51.918 ms, HollowmereGame.Start 9.958 ms, UI panel layout 20.943 ms (26 ms UI update), UI repaint 91.764 ms (font/layout/binding/render-chain work), finish-frame rendering 61.805 ms. These are inclusive/nested samples, not additive independent timings. The first logged delta also spans preceding engine/splash startup. Full startup rows remain retained.

Native long captures are larger than the Editor history window; their exported CSV covers the retained 300-frame tail. Short startup captures preserve frame 0 onward. The first exporter failed on an unnamed render-thread sample; null-name handling and explicit thread counts fix the exporter. The failed combined export/test invocation has no XML and is NOT test evidence. The isolated baseline regression XML records the expected failure: `Ready == false` while Attach emitted `ready`.

## Implementation under validation

- **P31d-PACING:** GameBoot defaults to uncapped rendering; `-frameVsync 0|1` explicitly selects both measured states. No URP quality downgrade.
- **P31d-BOOT:** Warm the real menu layout, glyph atlas, bindings and first render under an opaque loading surface, with menu controls disabled. After two completed loading renders, expose/enable the menu. Only actual readiness emits `ready`. Region IO additionally waits until after the menu presentation frames. This is a product loading state, not a logger-only delay; all pre-ready rows carry `boot-warmup` and remain in the CSV. The statistics algorithm is unchanged.
- Regression: `P31d_BOOT_AttachDoesNotClaimAnUnreadyMenu` fails on baseline (`tests/baseline-test-only.xml`); additional policy coverage: `P31d_PACING_DefaultIsUncappedAndExplicitVsyncIsHonored`, `P31d_BOOT_RegionWaitsForPresentedMenu`.
- No shared package, authored content, bake, or Studio authoring journal change is intended.

GPU duration counters return zero on this OpenGL player and are unavailable, not zero-cost GPU evidence. Attribution relies on the measured CPU/present/render counters, native samples and the controlled VSync comparison.

The recorder runs after gameplay/session LateUpdate, so the first visible menu frame carries readiness in that same row. `boot-warmup` deliberately contains no `load` substring: it cannot manufacture a transition exemption in the existing statistics rule.

## Test verification on `703e5509`

| Check | Disposition |
|---|---|
| Full final EditMode | 466 passed, 0 failed, 20 skipped, 486 total; `tests/editmode-final.xml` |
| Full PlayMode | 22 passed, 0 failed, 0 skipped; `tests/playmode-full.xml` |
| All three P31d regressions | Passed in full EditMode; premature-ready regression has retained baseline failure |
| `P31AuthoringTests.BakeVerifies` | Passed in full EditMode; bake/content unchanged |
| Metadata and C# checkers | Pass; transcripts retained |

The 20 skips are retained by name/reason in `tests/results.json`: explicitly gated live ETOS/voice/workflow cases,
graphical Editor cases, and explicit acceptance/memory fixtures. They are not counted as passes. No paid gate was enabled.
The wrapper therefore calls EditMode partial/skipped despite zero failures; the packet reports the XML counts directly.
The game-focused preliminary filter selected a memory fixture which wrote its historical default evidence path;
that incidental file was copied into this packet and the original restored. New test fixture journal entries were
removed (hashes in `tests/fixture-cleanup.json`); no authoring-journal/content change is committed.

## P31d-LOG — newly reproduced synchronous logger stalls

The first release (`703e5509`) completed two uncapped full routes: p95 **2.873 / 2.855 ms**, post-ready >100 ms
**7 / 0**. All seven run-1 stalls occurred at `frame % 120 == 1`, immediately after synchronous logger flushes.
Those runs remain in `measurement-before-logger/`; the first player's binaries/manifests are retained separately.
The superseded sequence was stopped only after run 2 exited, before starting VSync qualification. This is an
intermediate-code failure, not an unchanged-binary rerun to obtain a lucky pass.

Expanding the **existing** baseline capture to Unity's 2000-frame history limit proves the cause:
`profile-baseline/flush-stall.json` records native frame 10680, **704.595 ms in FrameLogRecorder.LateUpdate**,
with **705.763 ms render-thread wait** and only **0.088 ms GC.Alloc**. The following logged delta is 706.662 ms.
Thus this stall is logger/main-thread IO, not shader/GPU work or a large GC sample.

Automatic 120-row flushes now enqueue immutable text to a serial worker. Explicit Flush/quit/destroy drain the
queue, preserve row order and surface write failures. No row, timestamp or budget is changed. The deterministic
`P31d_LOG_AutomaticFlushDoesNotWaitForStorage` failed with the blocking automatic flush (`tests/logger-before.xml`);
`logger-before.patch` retains that test setup. `P31d_LOG_WritesStayOrderedAndFailuresSurfaceAtDrain` covers ordered
publication and IO failure propagation. Final qualification must use the rebuilt logger-fixed revision, two full
runs per VSync state; the intermediate pair remains reported separately.
