# W-PLUG-08: Take spam + reload: exactly one lantern

Verdict: **BLOCKED**. Exactly-once outbox restoration and grant IDs pass, but the specific repeated lantern pickup plus reload yielding exactly one lantern is not asserted by the retained tests.

Report timestamp: 2026-10-06T12:40:21.178424+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `TwoHundredGrants_GetDistinctRequestIds_AndGrantsAfterAReloadStillApply`. Component cases do not close any missing external workflow.
