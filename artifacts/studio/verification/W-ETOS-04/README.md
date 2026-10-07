# W-ETOS-04: Worker `etos query` returns the selected NPC's dialogue nodes

Verdict: **PASS**. Actual installed worker executes an owner-scoped query and returns all eight current Bram nodes with matching indices/kinds/texts. Initial observer rejected local variable cmd and colon-delimited output; unchanged independent trace and receipt pass the corrected observer without another worker call.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/tasks-p42k.py; python3 artifacts/studio/verification/TOOLS/query-p42k.py --workflow <current query workflow>
```

## Current-run evidence

- [W-ETOS-04/p42k-query-20261007T191948.047722Z/workflow/current-query-verification.json](../W-ETOS-04/p42k-query-20261007T191948.047722Z/workflow/current-query-verification.json)
