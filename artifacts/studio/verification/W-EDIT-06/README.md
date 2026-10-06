# W-EDIT-06: Runtime-only move → "Apply to authored" → persists after exiting Play

Verdict: **PASS**.

The real batch Editor moves Hollowmere's authored Village Well from `(0,0,-1.9)` to `(1,0,-1.9)` through the production `GameplayCommands.Place` command. The driver observes the committed request and canonical position/yaw slots, while the authored proxy remains unchanged. Changes / Journal exposes **Apply to authored**; its command handler creates a separate durable Candidate. After actual Play exit/domain reload, a reconstructed Studio runtime applies that candidate through the ordinary `move` tool and saves its scene. Reopening retains the destination. Normal History undo, save and reopen restore the original pose. The runtime action remains non-undoable; duplicate promotion requests apply once.

## Evidence

- [Final acceptance XML](r7-b-final/results.xml): **8 passed / 0 failed / 0 skipped**: six promotion boundary cases, `Hollowmere.R7_B.Promotion.RuntimeMoveAcceptance.W_EDIT_06_RealRuntimeMoveCreatorPromotionExitPlayAndInverse`, and exact item impact. Earlier standalone real-Play acceptance also [passes 1/1](r7-b-tests/attempt-4/results.xml).
- [promotion.json](r7-b/promotion.json): distinct runtime/authored IDs and observed poses.
- [runtime.json](r7-b/runtime.json): runtime action, operation ID, no authored inverse.
- [authored-undone.json](r7-b/authored-undone.json): separate authored History entry and inverse.
- [Broad regression XML](r7-b-tests/attempt-1/results.xml): all five `RuntimeMovePromotionTests` pass, including changed/deleted target refusals, unavailable promotion and atomic mixed-batch refusal. Shared suite: **137 passed / 2 failed / 0 skipped**; CORE-PICK's required idle-host check saw three Editors, and the first promotion driver selected a non-authored decoration.
- [Model TRX](r7-b-dotnet/r7b-model.trx): **113 passed / 0 failed / 0 skipped**.
- [Pre-fix conflict XML](conflict-before/results.xml): the new world-revision refusal omitted expected/actual witnesses; `W_EDIT_06_StaleRuntimeWorldRefusesWithRevisionWitnesses` fails before correction and passes in final XML.

Source: product `7061c5cd`, corrected driver `0909e4a2`, final revision-witness fix `de18d3b9`. Initial promotion attempts remain in `r7-b-tests/attempt-{1,2,3}`: incorrect fixture selection, then iterator locals/closure lost across actual domain reload. The final driver separates reload phases and retains witnesses in SessionState; no product rule or assertion was weakened. Current promotion JSON/History receipts are from the final 8/8 run.

## Contract

`ChangeSetEngine.RuntimeMoves.TryApplyToAuthored(runtimeChangeSetId, out authored, out problem)` requires a committed move in the same live world and a unique, saved, pre-Play authored target. Dirty/unsaved scenes, ambiguous or missing mappings, other runtime actions and uncommitted/wrong-world moves refuse. The creator's queued decision survives runtime reconstruction. Authored changes or deletion after the baseline produce ordinary `Conflict`/`StaleTarget`; promotion never rebases silently.

The trusted core captures open, saved authored scenes before Play. Views binds the production placement adapter when its context opens. `runtime.move` is explicitly RuntimeOnly/Live; `Apply to authored` is a separate creator command. No candidate code or admission authority is added.

## Reproduce

```sh
GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-EDIT-06/rerun/logs" \
  --label r7b-promotion --results "$PWD/artifacts/studio/verification/W-EDIT-06/rerun/results.xml" -- \
  -runTests -testPlatform EditMode -testFilter 'Hollowmere\.R7_B\.Promotion|GameCore\.Studio\.Edit\.Tests\.RuntimeMovePromotionTests'
```

No graphical capture is claimed: the test constructs the real Changes view, asserts its Journal command exists, invokes that command's public handler, and proves the actual world/authored/History transitions. No paid operations or installed-service changes.
