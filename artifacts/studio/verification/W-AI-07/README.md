# W-AI-07: 3D generation request: `not_configured`/blocked surfaced honestly

Verdict: **BLOCKED**. P4.2b final-main rerun BLOCKED: active P3.1d live-run guard prevents required R4 immutable companion activation; installed release 0.1.0-74096dc59ab3fd21 has no media_charges ledger. No request sent. Intended coverage: R4-C provider-before-budget refusal precedence.

Report timestamp: 2026-10-06T04:56:25.172533+00:00 UTC.

Acceptance baseline: merged main `1752ca8a`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh live-preflight
studio/tools/verify-all.sh final-live
```

## Retained evidence

- [W-AI-07/p42b-live-prerequisite-20261006T044157.606085Z/README.md](../W-AI-07/p42b-live-prerequisite-20261006T044157.606085Z/README.md)

Historical references: P4.2 disposition retained in git history: BLOCKED. Old component evidence does not establish a final-main live workflow.
