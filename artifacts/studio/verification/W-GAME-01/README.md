# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **BLOCKED**. The P3.1 recording is reused, as requested. It predates P3.1b and failed B-FRAME; P3.1b’s later 640×480 llvmpipe measurements cannot qualify final-tree RTX/1080p performance.

Report timestamp: 2026-10-06T12:40:21.185720+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
Read the linked retained P3.1/P3.1b recording and frame manifests; do not make another ten-minute recording.
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Historical references: [P3.1 recording](../../evidence/P3.1/recording/playthrough.mp4), [recording hash](../../evidence/P3.1/recording/playthrough.sha256), [P3.1b measurement qualification limits](../../evidence/P3.1b/README.md). The video predates the P3.1b source changes; no new ten-minute recording was made.
