# W-MODEL-02: Delete an item asset; index lists every referencing dialogue line and objective

Verdict: **BLOCKED**. Current index/impact tests cover typed references and lantern reward/stock; the exact deleted-item dialogue-line/objective witness is not exercised.

Report timestamp: 2026-10-06T15:29:44.376151+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `IndexEdges_HollowmereContent_TypedReferencesBecomeEdges_AndImpactOfTheLanternListsRewardAndStock`. Component cases do not close any missing external workflow.
