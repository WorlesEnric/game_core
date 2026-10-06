# W-GAME-05: Full flow menu→save→load→ending→restart in the player

Verdict: **BLOCKED**. Editor full-quest endings and save/load pass; a current standalone menu→save→load→ending→restart playthrough is not re-recorded under the explicit recording-reuse instruction.

Report timestamp: 2026-10-06T09:08:50.186969+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)

Exact acceptance/component cases: `EndingA_Silence`, `EndingB_Toll`, `EndingC_FreedEcho`, `SaveRestoreMidQuest`. Component cases do not close any missing external workflow.

Historical references: [P3.1 recording](../../evidence/P3.1/recording/playthrough.mp4), [recording hash](../../evidence/P3.1/recording/playthrough.sha256), [P3.1b measurement qualification limits](../../evidence/P3.1b/README.md). The video predates the P3.1b source changes; no new ten-minute recording was made.
