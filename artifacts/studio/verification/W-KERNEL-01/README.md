# W-KERNEL-01: Corrupted catalog → named failure, not an empty world

Verdict: **PASS**. Production GameApplication boot with a corrupted catalog returns CatalogFingerprintMismatch and creates no world.

Report timestamp: 2026-10-06T09:08:50.182474+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh v1
```

## Retained evidence

- [W-GAME-06/v1-gate-retry-20261005T190359.447287Z/gate/unity/editmode-results.xml](../W-GAME-06/v1-gate-retry-20261005T190359.447287Z/gate/unity/editmode-results.xml)

Exact acceptance/component cases: `Boot_WithACorruptedCatalog_FailsWithANamedCode_AndCreatesNoWorld`. Component cases do not close any missing external workflow.
