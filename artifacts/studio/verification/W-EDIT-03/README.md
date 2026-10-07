# W-EDIT-03: Generate portrait → apply → undo → redo: no second generation (usage unchanged)

Verdict: **PASS**. One fresh USD0.20 portrait passes production import, normal History undo/redo and final undo; generated/applied/redone SHA256 remains identical and every charge checkpoint stays unchanged. Actual portrait pixels reviewed.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py portrait --row W-EDIT-03 --method P42h.Media.Driver.RunPortrait
```

## Current-run evidence

- [W-EDIT-03/p42i-portrait/workflow/result.json](../W-EDIT-03/p42i-portrait/workflow/result.json)
- [W-EDIT-03/p42i-portrait/workflow/generate.json](../W-EDIT-03/p42i-portrait/workflow/generate.json)
- [W-EDIT-03/p42i-portrait/workflow/ledger-final.json](../W-EDIT-03/p42i-portrait/workflow/ledger-final.json)
- [W-EDIT-03/p42i-portrait/workflow/visual-review.json](../W-EDIT-03/p42i-portrait/workflow/visual-review.json)
