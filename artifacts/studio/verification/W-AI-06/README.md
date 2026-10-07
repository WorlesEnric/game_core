# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **PASS**. Separate-Editor reopen finds Odd/HUD/quest journals Applied and exact saved bytes. All three normal History undo/redo paths succeed; final undo plus production bake restores the complete baseline asset bytes without normalization.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42j.py reopen --row W-AI-06 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow reopen --offline
```

## Current-run evidence

- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/after-reopen.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/final.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/reopen/final.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/odd-line/undo-result.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/odd-line/undo-result.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/odd-line/redo-result.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/odd-line/redo-result.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/hud/undo-result.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/hud/undo-result.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/hud/redo-result.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/hud/redo-result.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/quest/undo-result.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/quest/undo-result.json)
- [W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/quest/redo-result.json](../W-AI-06/p42j-reopen-20261007T125251.086788Z/workflow/quest/redo-result.json)
