# W-EDIT-02: 5-op change set with one stale op: AllOrNothing rollback and BestEffort partial report

Verdict: **PASS**. Five-op stale-member cases prove AllOrNothing applies nothing and BestEffort records the other outcomes.

Report timestamp: 2026-10-06T04:56:25.162556+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `WEdit02_AllOrNothing_RefusesTheStaleOpAndAppliesNothing`, `WEdit02_BestEffort_AppliesTheOthersAndRecordsPerOpOutcomes`. Component cases do not close any missing external workflow.
