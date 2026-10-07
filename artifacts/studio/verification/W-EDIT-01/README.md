# W-EDIT-01: Change set in History shows etos task id and GameCore operation ids

Verdict: **PASS**. Current-run Change set in History shows etos task id and GameCore operation ids is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42k.py
```

## Current-run evidence

- [W-EDIT-01/p42k-move-20261007T191754.881437Z/workflow/result.json](../W-EDIT-01/p42k-move-20261007T191754.881437Z/workflow/result.json)
