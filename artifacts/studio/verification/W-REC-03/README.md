# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **PASS**. Current-run Cancel region load mid-way; cancel staging job is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42l.py cancel
```

## Current-run evidence

- [W-REC-03/p42l-installed-cancellation/editor-result.json](../W-REC-03/p42l-installed-cancellation/editor-result.json)
- [W-REC-03/p42l-installed-cancellation/cancel-editor-result.json](../W-REC-03/p42l-installed-cancellation/cancel-editor-result.json)
- [W-REC-03/p42l-installed-cancellation/stage-resources.json](../W-REC-03/p42l-installed-cancellation/stage-resources.json)
- [W-REC-03/p42l-cancelled-stage-20261008T002811.211630Z/cancelled-after-restart.json](../W-REC-03/p42l-cancelled-stage-20261008T002811.211630Z/cancelled-after-restart.json)
