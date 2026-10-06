# W-PLUG-09: "Why didn't the gate open" trace

Verdict: **PASS**. The real Hollowmere logic why-not trace names the failed condition and the change that would make it true.

Report timestamp: 2026-10-06T04:56:25.175692+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)

Exact acceptance/component cases: `WPlug09_LogicWhyNot_NamesTheFailedConditionAndWhatWouldChangeIt`. Component cases do not close any missing external workflow.
