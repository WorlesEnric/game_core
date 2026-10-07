# W-ETOS-04: Worker `etos query` returns the selected NPC's dialogue nodes

Verdict: **PASS**. Actual installed worker executes an owner-scoped query and returns all eight current Bram nodes with matching indices/kinds/texts. The old observer rejected a concatenated graph identity in the worker script; offline verification of the unchanged request, result and independent trace passes without a new query.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42j.py; python3 artifacts/studio/verification/TOOLS/query-p42j.py --workflow <current query workflow>
```

## Current-run evidence

- [W-ETOS-04/p42j-query-20261007T130430.466361Z/workflow/current-query-verification.json](../W-ETOS-04/p42j-query-20261007T130430.466361Z/workflow/current-query-verification.json)
