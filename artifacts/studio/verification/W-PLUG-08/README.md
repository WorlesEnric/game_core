# W-PLUG-08: Take spam + reload: exactly one lantern

Verdict: **PASS**.

## R7-C evidence

- [Final PlayMode XML](r7-pickup/results.xml): **10 passed, 0 failed/skipped/inconclusive**.
- Named case: `Hollowmere.P3_1.PlayMode.Tests.FullQuestHeadless.R7C_WPLUG08_RepeatedBarnLanternPickupAndReload_LeavesExactlyOneLantern` **Passed**.
- [Editor log](r7-pickup/r7-pickup-20261007T050233-4009528-a1.log).

The actual barn lantern receives 32 `interact.use` submissions in four bursts. Each attempt must reach a committed success/refusal; refusals must identify cooldown/exhaustion/already-used, not range or broken wiring. After cooldown, retries still leave exactly one inventory lantern. Production SaveService captures and restores with the same canonical slot hash into a different gameplay world. Two further bursts after restore leave exactly one lantern; delivery drops and pump violations remain zero. The test deletes its isolated save slot.

Observed interaction outcomes: `1 success / 7 refused`, `1 / 7`, `0 / 8`, `0 / 8`. A successful interaction is not synonymous with an inventory grant: inventory count remains exactly one throughout. The first driver incorrectly required all post-cooldown interactions to refuse; [retained initial XML](../W-PLUG-03/r7-play/results.xml) exposes that incidental assertion. The corrected test preserves the row's exact one-lantern invariant and validates every committed interaction outcome; no product behavior was weakened.

All three endings, failure ending, mid-quest restore, async save and interrupted-save cases in FullQuestHeadless also pass in the final ten-case XML, along with the deterministic ledge case.

## Reproduce

Graphics-enabled batch `studio/tools/unity-batch.sh` through the R7-C adapter, `-runTests -testPlatform PlayMode -testFilter 'FullQuestHeadless|LedgeJumpLanding' -force-glcore`. Xvfb/llvmpipe, not a real-GPU performance claim. No paid operations or service changes.
