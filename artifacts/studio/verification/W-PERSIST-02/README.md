# W-PERSIST-02: Rename prefab, re-run: NPC keeps state in a save

Verdict: **PASS**. Literal NPC prefab rename after checkpoint capture; separate Editor PIDs restore identical canonical slot hash/eight NPC fields and cosmetics. Original prefab/meta paths and bytes restore exactly.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py lifecycle
```

## Current-run evidence

- [W-PERSIST-02/p42i-prefab-prepare-20261007T045650.504079Z/workflow/reopen-result.json](../W-PERSIST-02/p42i-prefab-prepare-20261007T045650.504079Z/workflow/reopen-result.json)
