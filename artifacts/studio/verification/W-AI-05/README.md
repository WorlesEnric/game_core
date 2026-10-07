# W-AI-05: Change the lantern quest to require two oil flasks; verify consequences in Play

Verdict: **PASS**. Fresh unchanged worker quest candidate requires indexed OilFlask twice: real Play remains stage 1 after one and advances to stage 2 after two. Normal saved History replay restores original bytes.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42j.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative
```

## Current-run evidence

- [W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/quest/play-effect.json](../W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/quest/play-effect.json)
