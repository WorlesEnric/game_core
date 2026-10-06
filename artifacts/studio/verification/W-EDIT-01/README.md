# W-EDIT-01: Change set in History shows etos task id and GameCore operation ids

Verdict: **BLOCKED**. Real task/candidate IDs are recorded, but the worker move is rejected for missing scope, so no applied joined task/GameCore-operation History entry exists.

Report timestamp: 2026-10-06T04:56:25.162266+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh ui
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
