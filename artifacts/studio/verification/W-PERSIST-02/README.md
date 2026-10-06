# W-PERSIST-02: Rename prefab, re-run: NPC keeps state in a save

Verdict: **PASS**.

Two real batch Editor processes execute `ScenariosR7Lifecycle.PersistPrepare` and `PersistReopen`. The first changes Bram's committed mood and cosmetic visibility through production commands, captures the game's SaveService checkpoint, literally renames `NpcCapsule.prefab` to `R7B_RenamedNpc.prefab`, and exits. The second reopens with the old filename absent, loads the renamed prefab through the same GUID, boots the game and restores the checkpoint into a new world. Saved canonical slot hash, all eight observed NPC fields and the existing costume overrides match. The driver then restores the original prefab path and byte-identical prefab/meta files.

## Evidence

Source `0909e4a2`, Linux batch Editor. No paid operations or installed-service changes.

- [prepare-result.json](r7-b/attempt-1/prepare-result.json): `prepared`, **not** a passing full-row verdict; Editor PID 4020708.
- [handoff.json](r7-b/attempt-1/handoff.json): durable preimage metadata, literal paths and save witnesses.
- [reopen-result.json](r7-b/attempt-1/reopen-result.json): `pass`, distinct Editor PID 4024953, only after restore and exact prefab cleanup.
- [prepare-events.json](r7-b/attempt-1/prepare-events.json), [reopen-events.json](r7-b/attempt-1/reopen-events.json).
- [Saved checkpoint directory](r7-b/attempt-1/saves/), [prefab preimage](r7-b/attempt-1/prefab-before.bytes), [meta preimage](r7-b/attempt-1/prefab-before.meta).
- [Prepare log](r7-b/attempt-1/logs/r7b-persist-prepare-20261007T050619-4020638-a1.log), [reopen log](r7-b/attempt-1/logs/r7b-persist-reopen-20261007T050754-4024899-a1.log).

Restored canonical slot hash: `0416bb9f77aa8224bad1b87a842156a05afdc6f72498992e3576c7805db2d5eb`.

Observed NPC state before save and immediately after restore: alive 1, visible 0, variant 0, scaleMilli 1000, mood 1, patrolIndex 0, posX 11000, posZ -13000. Save bytes are hash-checked across process restart. This is actual literal rename/reopen/restore evidence, not an inference from stable GUID design.

## Reproduce

Run these two commands sequentially, using the same fresh output/save directory:

```sh
GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-PERSIST-02/rerun/logs" \
  --label r7b-prepare --attempts 1 -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Lifecycle.PersistPrepare \
  -r7Evidence "$PWD/artifacts/studio/verification/W-PERSIST-02/rerun" \
  -saveDir "$PWD/artifacts/studio/verification/W-PERSIST-02/rerun/saves"

GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-PERSIST-02/rerun/logs" \
  --label r7b-reopen --attempts 1 -- \
  -executeMethod Hollowmere.P3_2.Workflows.ScenariosR7Lifecycle.PersistReopen \
  -r7Evidence "$PWD/artifacts/studio/verification/W-PERSIST-02/rerun" \
  -saveDir "$PWD/artifacts/studio/verification/W-PERSIST-02/rerun/saves"
```

No `-quit`. If interrupted between rename and cleanup, run `ScenariosR7Lifecycle.RecoverPrefab` through the same wrapper with `-r7Evidence` pointing to that run; recovery never emits a passing acceptance receipt. ExecuteMethod assertions/JSON, not stdout or a claimed NUnit result, establish this row.
