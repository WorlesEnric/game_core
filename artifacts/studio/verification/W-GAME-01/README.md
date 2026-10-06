# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **BLOCKED**. reused P3.1 video predates fixes. P3.1b `406f00c1` offscreen llvmpipe: p95 1.284 ms, 0 >100 ms, belfry 7.4 ms. [P3.1d real GPU](packets/P3.1b-frame-time.md): RTX 4060 Ti 1080p, VSync OFF PASS 2.778/2.856 ms; VSync ON FAIL 18.062/18.089 ms; both 0 >100 ms. New real-GPU measurements exist; final-tree recording and owner VSync rule remain open.

Report timestamp: 2026-10-06T15:29:44.428661+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
Read the linked retained P3.1/P3.1b recording and frame manifests; do not make another ten-minute recording.
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Historical references: [P3.1 recording](../../evidence/P3.1/recording/playthrough.mp4), [recording hash](../../evidence/P3.1/recording/playthrough.sha256), [P3.1b measurement qualification limits](../../evidence/P3.1b/README.md). The video predates the P3.1b source changes; no new ten-minute recording was made.
