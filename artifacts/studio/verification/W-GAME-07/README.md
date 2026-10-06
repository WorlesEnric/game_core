# W-GAME-07: Player runs with etosd stopped and no network

Verdict: **BLOCKED**. etosd stop/kill is expressly forbidden. The non-destructive network-namespace prerequisite fails with Operation not permitted; companion supervisor restart cannot prove stopped-node player operation.

Report timestamp: 2026-10-06T04:56:25.182035+00:00 UTC.

Acceptance baseline: merged main `1752ca8a`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh node-equivalence
```

## Retained evidence

- [W-GAME-07/p42b-network-namespace-prerequisite-20261006T044240.056723Z/README.md](../W-GAME-07/p42b-network-namespace-prerequisite-20261006T044240.056723Z/README.md)
