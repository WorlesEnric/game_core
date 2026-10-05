# W-GAME-07: Player runs with etosd stopped and no network

Verdict: **BLOCKED**. Stopping etosd is forbidden by the packet; network-namespace isolation is unavailable on this host. Historical no-socket observation does not prove this exact final-tree stopped-node scenario.

Report timestamp: 2026-10-05T21:03:25.264969+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
No service-stop command is authorized for this packet.
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Historical references: [P3.1 recording](../../evidence/P3.1/recording/playthrough.mp4), [recording hash](../../evidence/P3.1/recording/playthrough.sha256), [P3.1b measurement qualification limits](../../evidence/P3.1b/README.md). The video predates the P3.1b source changes; no new ten-minute recording was made.
