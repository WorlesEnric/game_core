# W-EDIT-08: Apply timings (single and region-wide)

Verdict: **PASS**. single: p95 [55.8581, 63.073600000000006] ms, median 59.4659 ms, budget 200.0 ms; region: p95 [157.6064, 79.8135] ms, median 118.7100 ms, budget 1000.0 ms; prepare: p95 [4.3048, 10.177200000000001] ms, median 7.2410 ms, budget 300.0 ms

Report timestamp: 2026-10-06T12:40:21.163962+00:00 UTC.

Acceptance baseline: merged main `1752ca8a`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh final-timing
studio/tools/verify-all.sh final-selection
```

## Retained evidence

- [B-EDIT/p42b-dataset-summary/README.md](../B-EDIT/p42b-dataset-summary/README.md)
- [B-EDIT/p42b-dataset-summary/dataset.json](../B-EDIT/p42b-dataset-summary/dataset.json)
