# W-UI-03: Subpart vs logical vs prefab vs scope choice on a lantern

Verdict: **BLOCKED**. Logical-object selection is captured; the lantern subpart/prefab/scope chooser sequence is not demonstrated by this driver.

Report timestamp: 2026-10-05T21:03:25.244382+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh ui
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
