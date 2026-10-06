# W-EDIT-04: Edit a running NPC, exit Play, apply; delete it, apply: `StaleTarget`

Verdict: **PASS**.

`ScenariosR7Lifecycle.EditLifecycle` executes the complete ordered scenario in one real batch Editor: boot Hollowmere; change Bram's committed mood through the production NPC command; stage a costume edit against his live authored identity; exit Play; apply that exact serialized candidate; stage a second edit; delete Bram through the engine; apply the pending edit. The final outcome is precisely `Refused/StaleTarget`, retained in History, with no resurrection. Ordinary History undo restores the deletion and original costume; scene bytes remain unchanged.

## Evidence

Source `0909e4a2`, Linux batch Editor, no paid operations or service changes.

- [edit-result.json](r7-b/attempt-1/edit-result.json): `status: pass` only after all assertions and cleanup.
- [edit-events.json](r7-b/attempt-1/edit-events.json): ordered real-Play → exit → Applied → delete Applied → Rejected → normal-undo sequence.
- [planned-in-play.json](r7-b/attempt-1/planned-in-play.json) and [after-exit-apply.json](r7-b/attempt-1/after-exit-apply.json).
- [delete.json](r7-b/attempt-1/delete.json) and [after-delete-apply.json](r7-b/attempt-1/after-delete-apply.json): exactly one `npc-tint` outcome, `Refused`, `StaleTarget`, “The target no longer exists.”
- [delete undo](r7-b/attempt-1/deleteId-undone.json) and [edit undo](r7-b/attempt-1/editId-undone.json).
- [Editor log](r7-b/attempt-1/logs/r7b-lifecycle-20261007T050445-4016320-a1.log): process exit 0. The JSON assertions, not the runner's stdout summary, establish this executeMethod acceptance; no NUnit XML is claimed for it.

## Reproduce

```sh
GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-EDIT-04/rerun/logs" \
  --label r7b-lifecycle --attempts 1 -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Lifecycle.EditLifecycle \
  -r7Evidence "$PWD/artifacts/studio/verification/W-EDIT-04/rerun"
```

Use a fresh output directory; do not pass `-quit`. This is the combined scenario previously missing from component stale-target evidence.
