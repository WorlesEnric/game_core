# W-EDIT-04: Edit a running NPC, exit Play, apply; delete it, apply: `StaleTarget`

Verdict: **PASS**. Current real Bram edit in Play, exit/apply, engine delete and second apply produce journaled StaleTarget without resurrection; normal History undo restores original scene and costume.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py lifecycle
```

## Current-run evidence

- [W-EDIT-04/p42i-edit-lifecycle-20261007T045529.763068Z/workflow/edit-result.json](../W-EDIT-04/p42i-edit-lifecycle-20261007T045529.763068Z/workflow/edit-result.json)
