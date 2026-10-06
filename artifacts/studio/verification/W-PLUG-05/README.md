# W-PLUG-05: Locked door with key; explain refusal

Verdict: **PASS**. The actual Causeway Gate condition explain names the failed condition; committed locked-gate refusal and condition-controlled travel are exercised.

Report timestamp: 2026-10-06T04:56:25.174334+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md](../UNITY-HOLLOWMERE/editmode-final-gated-20261005T204827.752033Z/README.md)
- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `WPlug05_InteractionExplain_NamesTheFailedConditionOfTheLockedCausewayGate`, `UsingTheWellSucceeds_AndTheLockedGateRefusesWithItsCode`, `APortalCondition_RefusesTravelWithAStableCode_UntilItHolds`. Component cases do not close any missing external workflow.
