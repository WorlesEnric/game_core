# W-E2E-01: All rows resolved on one revision; completion report

Verdict: **FAIL**. All 68 rows are freshly judged at one product revision and installed companion release with strict host exclusivity; no historical PASS carries forward. End-to-end acceptance is FAIL because W-MECH-01, W-DOC-02 and W-GAME-01 fail now; six exact owner-forbidden scenarios remain BLOCKED.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/report-p42j.py; python3 artifacts/studio/verification/TOOLS/check-p42j.py
```

## Current-run evidence

- [W-E2E-01/p42j-accounting/accounting.json](../W-E2E-01/p42j-accounting/accounting.json)
