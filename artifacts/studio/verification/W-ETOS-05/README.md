# W-ETOS-05: Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task

Verdict: **PASS**. Current-run Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42k.py
```

## Current-run evidence

- [W-ETOS-05/p42k-cancel-20261007T192114.982259Z/workflow/result.json](../W-ETOS-05/p42k-cancel-20261007T192114.982259Z/workflow/result.json)
