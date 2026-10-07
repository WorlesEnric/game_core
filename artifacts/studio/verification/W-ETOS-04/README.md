# W-ETOS-04: Worker `etos query` returns the selected NPC's dialogue nodes

Verdict: **PASS**. Actual installed Docker worker resolves selected Bram through owner-scoped gc_definition and executes etos query: all eight returned node indices/kinds/texts match the selected graph. Initial P42h observer wrongly compared authoringId to the R8 owner:asset-GUID graph key; corrected offline verification passes the unchanged current query and independent tool trace, with no repeated provider call.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42i.py; python3 artifacts/studio/verification/TOOLS/query-p42i.py --workflow <current query workflow>
```

## Current-run evidence

- [W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/current-query-verification.json](../W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/current-query-verification.json)
- [W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/worker-query-receipt.json](../W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/worker-query-receipt.json)
- [W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/worker-tool-trace.json](../W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/worker-tool-trace.json)
- [W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/request.json](../W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/request.json)
- [W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/result.json](../W-ETOS-04/p42i-query-20261007T054157.439022Z/workflow/result.json)
