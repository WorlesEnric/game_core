# W-PERSIST-01: Save in the Ruin with items; load: all restored; region re-entered

Verdict: **PASS**. Belfry save/fresh-boot restore preserves canonical slots, items/facts, resident region and in-flight work exactly once; real game mid-quest restore continues to ending C.

Report timestamp: 2026-10-06T09:08:50.182978+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `FreshBootRestore_ReattachesTheNarrativeLayer_AndDeliversInFlightWorkExactlyOnce`, `SaveRestoreMidQuest`. Component cases do not close any missing external workflow.
