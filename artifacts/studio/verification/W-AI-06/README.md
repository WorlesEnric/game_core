# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **FAIL for the full row; R9-A Odd portion PASS**. The retained Odd edit now applies, survives distinct-Editor reopen with its Applied journal, and completes Undo → Redo → final Undo with complete-byte restoration. [Reopen result](../W-AI-03/r9-a-odd-20261007T104156.540366Z/reopen-result.json), [reopened journal](../W-AI-03/r9-a-odd-20261007T104156.540366Z/reopened.json). Product `6a67e287`. HUD/quest workflows were not rerun by this packet; partial evidence is not promoted to complete-row PASS.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
The P4.2i evidence below remains historical. Its missing-Odd-journal failure is fixed by the fresh R9-A replay, but its unrelated HUD/quest evidence is not relabeled current-revision acceptance.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py reopen --row W-AI-06 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow reopen --offline
```

## Historical P4.2i evidence

- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/after-reopen.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json](../W-AI-06/p42i-reopen-20261007T052926.823205Z/workflow/reopen/final.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/narrative/saved.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/narrative/saved.json)
- [W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json](../W-AI-03/p42i-narrative-20261007T052144.251187Z/workflow/odd-line/staged.json)
