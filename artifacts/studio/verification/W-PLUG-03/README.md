# W-PLUG-03: Walk, jump a ledge, focus prompt, interact dispatch

Verdict: **PASS**.

## R7-C evidence

[Executed PlayMode XML](r7-play/results.xml), Linux Unity 6000.0.75f1, graphics-enabled batch Editor via the host-wide allocator, isolated Xvfb/llvmpipe:

- `Hollowmere.R7_C.Interaction.Tests.LedgeJumpLanding.WPLUG03_DeterministicJumpClearsSolidLedgeAndLands`: **Passed**.
- `Hollowmere.P1_3.PlayMode.Tests.PlayerWalkAndInteract.WalksInteractsTalksPastNpcsAndTravels`: **Passed** (retained real walking, focus/prompt, interaction dispatch and travel).
- [Editor log](r7-play/r7-play-20261007T050003-4000332-a1.log).
- [Gameplay rules TRX](r7-rules/r7-rules.trx): **309 passed, 0 failed/skipped**.

The ledge test boots the real game twice. Production input, kernel motion and CharacterController traverse an isolated solid 0.8 m ledge above the streamed map. Walking alone is blocked by its lip. Jump must commit ascent with grounded=0, clear the top while crossing the lip, descend, land inside the supporting bounds, and remain grounded with zero vertical speed. The actual controller must report contact; off-map fallback is forbidden. Both fresh boots must produce identical 110-frame committed traces. No pose writes occur during traversal.

The initial combined XML has 13 passing cases and one lantern-driver assertion failure (W-PLUG-08); that does not change these two passing cases. No graphical frame-budget claim is made from Xvfb.

## Reproduce

`studio/tools/unity-batch.sh --project <abs project> --log-dir <dir> --label ledge --results <xml> -- -runTests -testPlatform PlayMode -testFilter 'LedgeJumpLanding|WalksInteractsTalksPastNpcsAndTravels' -force-glcore`, with `DISPLAY` and `UNITY=<abs Tests/R7_C/batch-graphics.py>`.
