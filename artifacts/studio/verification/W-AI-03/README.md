# W-AI-03: "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play

Verdict: **PASS**. Fresh unchanged Odd dialogue candidate applies. Actual unlit dialogue excludes the new shrine-light line and lit dialogue includes it; both line sets are retained.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42j.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative
```

## Current-run evidence

- [W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/odd-line/apply-report.json](../W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/odd-line/apply-report.json)
- [W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/odd-line/play-effect.json](../W-AI-03/p42j-narrative-20261007T124515.649672Z/workflow/odd-line/play-effect.json)
