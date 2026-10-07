# W-AI-04: Edit the HUD objective label and rebind it to the quest stage name

Verdict: **PASS**. Fresh unchanged worker HUD candidate applies and saves; a separate Editor reopens the matching complete bytes, and normal undo/redo/final undo restores the original HUD.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42j.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative; then reopen workflow
```

## Current-run evidence

- [W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/hud/apply-report.json](../W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/hud/apply-report.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/after-reopen.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/final.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/final.json)
