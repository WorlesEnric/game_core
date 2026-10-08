# W-ETOS-05: Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task

Verdict: **PASS**. Current-run Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42l.py
```

## Current-run evidence

- [W-ETOS-05/p42l-cancel-20261008T000330.148810Z/workflow/result.json](../W-ETOS-05/p42l-cancel-20261008T000330.148810Z/workflow/result.json)
