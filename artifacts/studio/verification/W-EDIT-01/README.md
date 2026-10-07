# W-EDIT-01: Change set in History shows etos task id and GameCore operation ids

Verdict: **PASS**. Fresh unchanged installed-worker runtime.move candidate applies in real Play. History joins the task to the exact committed GameCore operation and Bram pose; authored scene bytes remain unchanged.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42i.py
```

## Current-run evidence

- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/result.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/result.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/request.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/request.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/joined-history.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/joined-history.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/latest-request.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/latest-request.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/candidate-before-apply.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/candidate-before-apply.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/started.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/started.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/submission.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/submission.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/candidate-envelope.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/candidate-envelope.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/hello.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/hello.json)
- [W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/apply-report.json](../W-EDIT-01/p42i-move-20261007T054012.009656Z/workflow/apply-report.json)
