# W-ETOS-09: Domain reload during a task: tray shows the same task afterwards

Verdict: **PASS**. Actual changed-source compilation/domain reload preserves the same running task and one attached tray row. Cursor continuation 819→821 matches independent authenticated replay; explicit Cancel then settles the original task without candidate.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42i.py
```

## Current-run evidence

- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/result.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/result.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/request.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/request.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/latest-request.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/latest-request.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/started.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/started.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/submission.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/submission.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/source-compilation-request.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/source-compilation-request.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/cursor-replay.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/cursor-replay.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/cancelled.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/cancelled.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/reload-recovered.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/reload-recovered.json)
- [W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/hello.json](../W-ETOS-09/p42i-reload-20261007T054455.135309Z/workflow/hello.json)
