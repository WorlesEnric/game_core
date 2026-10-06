# W-PLUG-03: Walk, jump a ledge, focus prompt, interact dispatch

Verdict: **BLOCKED**. Real walking, focus/interact and travel pass; the deterministic script includes Jump but does not assert clearing a ledge and landing, so that required observation stays open.

Report timestamp: 2026-10-06T12:40:21.176666+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `WalksInteractsTalksPastNpcsAndTravels`. Component cases do not close any missing external workflow.
