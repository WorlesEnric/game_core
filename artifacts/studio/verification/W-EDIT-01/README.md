# W-EDIT-01: Change set in History shows etos task id and GameCore operation ids

Verdict: **PASS**. Current-run Change set in History shows etos task id and GameCore operation ids is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42l.py
```

## Current-run evidence

- [W-EDIT-01/p42l-move-20261008T000024.102305Z/workflow/result.json](../W-EDIT-01/p42l-move-20261008T000024.102305Z/workflow/result.json)
