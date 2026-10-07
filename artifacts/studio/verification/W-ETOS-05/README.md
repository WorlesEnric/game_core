# W-ETOS-05: Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task

Verdict: **PASS**. Actual enabled attached tray Cancel is activated while original request/task/tray report running. The same task becomes cancelled after 429ms, no candidate or duplicate row after a three-second observation.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42i.py
```

## Current-run evidence

- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/result.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/result.json)
- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/request.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/request.json)
- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/latest-request.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/latest-request.json)
- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/started.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/started.json)
- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/submission.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/submission.json)
- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/cancelled.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/cancelled.json)
- [W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/hello.json](../W-ETOS-05/p42i-cancel-20261007T054357.507172Z/workflow/hello.json)
