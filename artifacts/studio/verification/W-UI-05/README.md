# W-UI-05: Selection/hover and marquee timings

Verdict: **FAIL**. picks: p95 [3.245, 3.7098] ms, median 3.4774 ms, budget 16.0 ms; marquee: p95 [330.90360000000004, 621.8599] ms, median 476.3818 ms, budget 50.0 ms

Report timestamp: 2026-10-06T09:08:50.152336+00:00 UTC.

Acceptance baseline: merged main `1752ca8a`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh final-timing
studio/tools/verify-all.sh final-selection
```

## Retained evidence

- [B-SELECT/p42b-dataset-summary/README.md](../B-SELECT/p42b-dataset-summary/README.md)
- [B-SELECT/p42b-dataset-summary/dataset.json](../B-SELECT/p42b-dataset-summary/dataset.json)
