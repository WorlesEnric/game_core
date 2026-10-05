# W-EDIT-08: Apply timings (single and region-wide)

Verdict: **BLOCKED**. Existing timings are isolated operations; there is no completed 20-apply single-target/Marsh p95 and 1.5k-target compose dataset. The fixed budgets are not inferred from smaller probes.

Report timestamp: 2026-10-05T21:03:25.249694+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
