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
