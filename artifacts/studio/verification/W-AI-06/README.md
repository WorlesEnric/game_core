# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **PASS**. Current-run Undo/redo the above, close and reopen the project, verify consistency is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42l.py reopen --row W-AI-06 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow reopen --offline
```

## Current-run evidence

- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/reopen/after-reopen.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/reopen/final.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/reopen/final.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/odd-line/undo-result.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/odd-line/undo-result.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/odd-line/redo-result.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/odd-line/redo-result.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/hud/undo-result.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/hud/undo-result.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/hud/redo-result.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/hud/redo-result.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/quest/undo-result.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/quest/undo-result.json)
- [W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/quest/redo-result.json](../W-AI-06/p42l-reopen-20261007T235325.590280Z/workflow/quest/redo-result.json)
