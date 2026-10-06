# W-GAME-07: Player runs with etosd stopped and no network

Verdict: **BLOCKED**. The owner forbids etosd stop/restart, so the requested stopped-node/no-network player scenario is not run. The companion-only supervisor restart does not establish this condition; the prior namespace prerequisite refusal remains historical evidence.

Report timestamp: 2026-10-06T12:40:21.187448+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh node-equivalence
```

## Retained evidence

- [W-GAME-07/p42b-network-namespace-prerequisite-20261006T044240.056723Z/README.md](../W-GAME-07/p42b-network-namespace-prerequisite-20261006T044240.056723Z/README.md)

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
