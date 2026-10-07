# W-E2E-01: All rows resolved on one revision; completion report

Verdict: **BLOCKED**. P4.2h fresh complete-row PASS at product a77cb38b: W-AI-01, W-EDIT-01, W-EDIT-03, W-ETOS-05, W-ETOS-09. W-ETOS-04 lacks real dialogue-node publication; W-REC-03 lacks running-stage cancellation; W-GAME-01 has separate OFF PASS/ON FAIL measurements with the owner rule open. The other 59 rows were not rerun as complete scenarios at this revision; inherited PASS is not current acceptance. Current retained-dialogue regressions are 35/37, with two projection failures, and eight transient empty-argv children leave strict host exclusivity unproven. All 68 exact dispositions are listed.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/check_p42h.py
```

## Retained evidence

- [../workflows/P4.2e/outcomes.json](../../workflows/P4.2e/outcomes.json)
- [../workflows/P4.2e/paid-ledger.json](../../workflows/P4.2e/paid-ledger.json)
- [../workflows/P4.2e/editor-exclusivity-open.json](../../workflows/P4.2e/editor-exclusivity-open.json)
- [W-E2E-01/p42h-accounting/result.json](../W-E2E-01/p42h-accounting/result.json)
- [W-E2E-01/p42h-accounting/README.md](../W-E2E-01/p42h-accounting/README.md)
