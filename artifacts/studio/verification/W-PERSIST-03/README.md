# W-PERSIST-03: Schema version bump with migration: restored; without: actionable refusal

Verdict: **PASS**. Production SaveService restores a V1 checkpoint through the registered V2 migration; missing migration refuses while leaving the running world unchanged.

Report timestamp: 2026-10-06T15:29:44.421665+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh v1
```

## Retained evidence

- [W-GAME-06/v1-gate-retry-20261005T190359.447287Z/gate/unity/editmode-results.xml](../W-GAME-06/v1-gate-retry-20261005T190359.447287Z/gate/unity/editmode-results.xml)

Exact acceptance/component cases: `AV1SaveIsMigratedToV2OnRestore`, `AMissingMigrationRefusesAndLeavesTheRunningWorldUntouched`. Component cases do not close any missing external workflow.
