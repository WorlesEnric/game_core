# W-MODEL-02: Delete an item asset; index lists every referencing dialogue line and objective

Verdict: **PASS**.

R7-B runs the exact missing deletion scenario with real Unity item, dialogue, condition and quest assets. Core impact and the actual delete-stage preview identify the dialogue choice line `nodes[0].options[0].condition` at depth 2 (through `conditions[0].item`) and collect objective `objectives[0].item` at depth 1. The engine deletes the item asset; ordinary History undo restores it. Fixture assets are removed after the run.

## Evidence

- [impact.json](r7-b/impact.json): retained delete preview, dialogue text, objective text, Applied change set and inverse.
- [Unity XML](../W-EDIT-06/r7-b-tests/attempt-1/results.xml): `Hollowmere.R7_B.Model.ItemDeletionImpactTests.W_MODEL_02_DeleteItemListsReferencingDialogueLineAndObjective` **Passed**.
- [Editor log](../W-EDIT-06/r7-b-tests/attempt-1/r7b-editmode-20261007T045425-3972164-a1.log).
- Final source `de18d3b9`: [impact.json](r7-b-final/impact.json) and [final XML](../W-EDIT-06/r7-b-final/results.xml), **8/8 passed**, including this exact item-deletion case.

Source: `7061c5cd` (the working-tree bytes were committed while the run was active; the commit did not change tested bytes). Linux batch Editor, no paid operations or service changes. The shared run was **137 passed / 2 failed / 0 skipped**: CORE-PICK's idle-host precondition found three Editors, and the independent promotion driver initially selected an un-authored decoration. Those failures are retained; neither is counted as this row's proof.

## Reproduce

```sh
GAMECORE_ETOS_AUTOSTART=0 GAMECORE_ETOS_LIVE=0 \
GAMECORE_R7B_MODEL_OUT="$PWD/artifacts/studio/verification/W-MODEL-02/rerun" \
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir "$PWD/artifacts/studio/verification/W-MODEL-02/rerun/logs" \
  --label r7b-impact --results "$PWD/artifacts/studio/verification/W-MODEL-02/rerun/results.xml" -- \
  -runTests -testPlatform EditMode -testFilter 'Hollowmere\.R7_B\.Model'
```

The fixture uses the production typed dialogue-option availability condition, not a textual item-name guess or synthetic index. It adds the previously missing exact acceptance driver; no index implementation fix was needed for this case.
