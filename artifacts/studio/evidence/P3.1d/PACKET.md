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
