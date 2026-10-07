# W-ETOS-05: Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task

Verdict: **PASS**. Current-run Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task is verified by the linked named XML cases and/or exact JSON assertions; no prior-run result is used.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42j.py
```

## Current-run evidence

- [W-ETOS-05/p42j-cancel-20261007T130611.687321Z/workflow/result.json](../W-ETOS-05/p42j-cancel-20261007T130611.687321Z/workflow/result.json)
