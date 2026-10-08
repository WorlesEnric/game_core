# W-ETOS-04: Worker `etos query` returns the selected NPC's dialogue nodes

Verdict: **PASS**. Actual installed worker executes an owner-scoped query and returns all eight current Bram nodes with exact indices/kinds/texts. Initial observer rejected structured total_rows output; unchanged independent trace passes corrected observer without another worker call.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/query-p42l.py --workflow artifacts/studio/verification/W-ETOS-04/p42l-query-20261008T000152.746669Z/workflow
```

## Current-run evidence

- [W-ETOS-04/p42l-query-20261008T000152.746669Z/workflow/current-query-verification.json](../W-ETOS-04/p42l-query-20261008T000152.746669Z/workflow/current-query-verification.json)
