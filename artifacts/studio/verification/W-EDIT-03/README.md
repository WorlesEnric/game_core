# W-EDIT-03: Generate portrait → apply → undo → redo: no second generation (usage unchanged)

Verdict: **BLOCKED**. No current generated portrait reaches apply/undo/redo: the legacy media caller supplies an unregistered local ID; direct image calls additionally need a verified operator tariff. Old image files are not new generation evidence.

Report timestamp: 2026-10-06T04:56:25.162846+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh media
```

## Retained evidence

- [W-ETOS-07/installed-media-20261005T192218.561813Z/README.md](../W-ETOS-07/installed-media-20261005T192218.561813Z/README.md)
- [W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md](../W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md)
