# W-AI-06: Undo/redo the above, close and reopen the project, verify consistency

Verdict: **PASS**. Current-run Undo/redo the above, close and reopen the project, verify consistency is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42k.py reopen --row W-AI-06 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow reopen --offline
```

## Current-run evidence

- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/reopen/after-reopen.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/reopen/after-reopen.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/reopen/final.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/reopen/final.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/odd-line/undo-result.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/odd-line/undo-result.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/odd-line/redo-result.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/odd-line/redo-result.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/hud/undo-result.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/hud/undo-result.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/hud/redo-result.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/hud/redo-result.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/quest/undo-result.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/quest/undo-result.json)
- [W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/quest/redo-result.json](../W-AI-06/p42k-reopen-20261007T191123.755008Z/workflow/quest/redo-result.json)
