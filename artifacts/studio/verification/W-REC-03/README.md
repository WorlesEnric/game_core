# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **PASS**. Actual unfinished native region load and owned running Docker stage cancel. The attached Stage Cancel button leaves durable cancelled state, no execution container, a released slot, verdict 404 and disabled Admit. Authenticated reread after the permitted companion restart still refuses verdict authority.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42j.py cancel
```

## Current-run evidence

- [W-REC-03/p42j-installed-cancellation/editor-result.json](../W-REC-03/p42j-installed-cancellation/editor-result.json)
- [W-REC-03/p42j-installed-cancellation/cancel-editor-result.json](../W-REC-03/p42j-installed-cancellation/cancel-editor-result.json)
- [W-REC-03/p42j-installed-cancellation/stage-resources.json](../W-REC-03/p42j-installed-cancellation/stage-resources.json)
- [W-REC-03/p42j-cancelled-stage-20261007T134245.938620Z/cancelled-after-restart.json](../W-REC-03/p42j-cancelled-stage-20261007T134245.938620Z/cancelled-after-restart.json)
