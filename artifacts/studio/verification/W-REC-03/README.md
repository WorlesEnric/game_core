# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **BLOCKED**. Region cancellation components exist; no installed running-stage cancellation is possible through the missing app-origin staging path, and discard is not proof of cancellation.

Report timestamp: 2026-10-06T09:08:50.184541+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh stage
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md](../W-MECH-01/pressure-plate-explicit-paired-ui-20261005T193418.507302Z/README.md)
