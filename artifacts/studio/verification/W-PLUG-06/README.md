# W-PLUG-06: Conditional choice and persisted fact

Verdict: **PASS**. Conditional dialogue differs by fact; fresh-boot restoration preserves heard_rumour/gate_open and reattaches conversations.

Report timestamp: 2026-10-06T15:29:44.412768+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `Dialogue_PreviewDiffersByFact`, `FreshBootRestore_ReattachesTheNarrativeLayer_AndDeliversInFlightWorkExactlyOnce`. Component cases do not close any missing external workflow.
