# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **PASS**. Current-run Cancel region load mid-way; cancel staging job is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42k.py cancel
```

## Current-run evidence

- [W-REC-03/p42k-installed-cancellation/editor-result.json](../W-REC-03/p42k-installed-cancellation/editor-result.json)
- [W-REC-03/p42k-installed-cancellation/cancel-editor-result.json](../W-REC-03/p42k-installed-cancellation/cancel-editor-result.json)
- [W-REC-03/p42k-installed-cancellation/stage-resources.json](../W-REC-03/p42k-installed-cancellation/stage-resources.json)
- [W-REC-03/p42k-cancelled-stage-20261007T195656.130261Z/cancelled-after-restart.json](../W-REC-03/p42k-cancelled-stage-20261007T195656.130261Z/cancelled-after-restart.json)
