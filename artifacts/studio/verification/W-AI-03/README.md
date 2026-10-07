# W-AI-03: "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play

Verdict: **PASS (R9-A retained-candidate replay)**. The unchanged Odd candidate passes Stage/Apply and actual Play: its new shrine line is absent when unlit and present when lit. Journal Undo restores complete saved baseline bytes. Product `6a67e287`; [result](r9-a-odd-20261007T104156.540366Z/result.json), [Play effect](r9-a-odd-20261007T104156.540366Z/odd-line/play-effect.json), [source hashes](r9-a-odd-20261007T104156.540366Z/source-sha256.json). No fresh model call or repaired candidate.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
The P4.2i failure evidence below remains historical. R9-A's final-state projection fixes that unchanged candidate; exact retained/current catalog equality is recorded.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py narrative --row W-AI-03 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow narrative
```

## Historical P4.2i evidence

- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/candidate.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/play-effect.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/play-effect.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/outcome.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/outcome.json)

## R9-A reproduction

On fresh project state: `python3 artifacts/studio/verification/W-AI-06/r9-a/run.py --lane odd`, then `--lane odd-reopen --odd-output <successful odd output directory>`. Existing journals are never erased. See [packet](../../../../docs/studio/packets/R9-A.md).
