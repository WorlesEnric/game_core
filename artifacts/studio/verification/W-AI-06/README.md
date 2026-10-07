# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **FAIL**. Separate-Editor replay succeeds for the two applied HUD/quest candidates and final complete asset bytes equal baseline, but the fresh Odd edit never applied and reopens with journal missing. Full undo/redo of all preceding narrative edits is therefore not established; partial replay is not promoted to a complete-row PASS.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py reopen --row W-AI-06 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow reopen --offline
```

## Current-run evidence

- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/after-reopen.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/narrative/saved.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/narrative/saved.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json)
