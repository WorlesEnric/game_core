# W-AI-04: Edit the HUD objective label and rebind it to the quest stage name

Verdict: **PASS**. Fresh installed-worker ui.bind candidate rebinds objective-line.text to vm:hud.QuestStageTitle. It applies unchanged, saves, reopens with matching complete bytes, then normal undo/redo/final undo restores the original HUD bytes.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative; then reopen workflow
```

## Current-run evidence

- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/hud/apply-report.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/hud/apply-report.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/hud/candidate.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/hud/candidate.json)
- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/after-reopen.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json)
