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
