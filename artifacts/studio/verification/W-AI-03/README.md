# W-AI-03: "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play

Verdict: **FAIL**. Fresh installed-worker Odd dialogue candidate is Invalid: GP-DLG-003 edges 3→8 and 8→4 leave the projected graph, dangling next→8, plus GP-DLG-005 unreachable node 4. Candidate never applies; actual Play driver reports candidate was not applied. Historical PASS is not retained.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative
```

## Current-run evidence

- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/play-effect.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/play-effect.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/outcome.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/outcome.json)
