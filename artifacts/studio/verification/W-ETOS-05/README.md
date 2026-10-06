# W-ETOS-05: Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task

Verdict: **BLOCKED**. Live client cancellation completes in 556 ms with one task/no candidate, but the creator tray-button portion is not exercised.

Report timestamp: 2026-10-06T15:29:44.390244+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh reconnect
```

## Retained evidence

- [W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md](../W-ETOS-06/installed-stream-resume-cancel-20261005T185854.961200Z/README.md)
