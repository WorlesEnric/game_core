# W-AI-05: Change the lantern quest to require two oil flasks; verify consequences in Play

Verdict: **PASS**. Fresh unchanged worker quest candidate uses indexed OilFlask. Actual Play stays at quest stage 1 after one flask and advances to stage 2 after two; saved History replay restores original bytes.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative
```

## Current-run evidence

- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/quest/play-effect.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/quest/play-effect.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/quest/candidate.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/quest/candidate.json)
- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json)
