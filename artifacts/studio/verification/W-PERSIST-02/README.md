# W-PERSIST-02: Rename prefab, re-run: NPC keeps state in a save

Verdict: **BLOCKED**. Cosmetic-content save compatibility passes, but the literal prefab rename/reopen case is not exercised; no claim is inferred from stable GUID design alone.

Report timestamp: 2026-10-06T12:40:21.181785+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `CosmeticEditRestores_StructuralEditRefusesWithRecipeRevisionMismatch`. Component cases do not close any missing external workflow.
