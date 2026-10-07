# W-REC-03: Cancel region load mid-way; cancel staging job

Verdict: **PASS**. Actual unfinished native region load cancels and settles all regions Unloaded. Real installed Docker stage is cancelled through the attached Stage-panel button after pausing only its owned dotnet container to serialize Editors. Durable cancelled, no container, released slot, verdict 404 and Admit disabled are verified. Authenticated reread after the permitted companion restart still reports cancelled and verdict 404.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42i.py cancel
```

## Current-run evidence

- [W-REC-03/p42i-installed-cancellation/editor-result.json](../W-REC-03/p42i-installed-cancellation/editor-result.json)
- [W-REC-03/p42i-installed-cancellation/region-cancel-before.json](../W-REC-03/p42i-installed-cancellation/region-cancel-before.json)
- [W-REC-03/p42i-installed-cancellation/region-cancel-after.json](../W-REC-03/p42i-installed-cancellation/region-cancel-after.json)
- [W-REC-03/p42i-installed-cancellation/container-running.json](../W-REC-03/p42i-installed-cancellation/container-running.json)
- [W-REC-03/p42i-installed-cancellation/stage-ui-before.json](../W-REC-03/p42i-installed-cancellation/stage-ui-before.json)
- [W-REC-03/p42i-installed-cancellation/stage-ui-after.json](../W-REC-03/p42i-installed-cancellation/stage-ui-after.json)
- [W-REC-03/p42i-installed-cancellation/cancel-editor-result.json](../W-REC-03/p42i-installed-cancellation/cancel-editor-result.json)
- [W-REC-03/p42i-installed-cancellation/stage-resources.json](../W-REC-03/p42i-installed-cancellation/stage-resources.json)
- [W-REC-03/p42i-cancelled-stage-20261007T062735.109356Z/cancelled-after-restart.json](../W-REC-03/p42i-cancelled-stage-20261007T062735.109356Z/cancelled-after-restart.json)
