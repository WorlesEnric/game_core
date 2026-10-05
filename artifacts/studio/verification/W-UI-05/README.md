# W-UI-05: Selection/hover and marquee timings

Verdict: **BLOCKED**. Recorded small-sample click p95 exceeds 16 ms, but the mandatory 100-pick p95 and 500-candidate marquee dataset is absent. Neither a qualified pass nor a full-workload timing verdict is inferred.

Report timestamp: 2026-10-05T21:03:25.245336+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh ui
```

## Retained evidence

- [W-UI-01/ui-capture-20261005T192323.970290Z/README.md](../W-UI-01/ui-capture-20261005T192323.970290Z/README.md)
