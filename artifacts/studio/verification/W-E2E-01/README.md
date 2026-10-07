# W-E2E-01: All rows resolved on one revision; completion report

Verdict: **FAIL**. All 68 rows are now judged at one product revision and one installed release, with no inherited PASS. End-to-end acceptance is FAIL because W-VIEW-02, W-AI-02, W-AI-03 and the complete W-AI-06 sequence fail; six exact owner-forbidden scenarios remain BLOCKED. Same-revision accounting is complete, not a claim that the product is accepted.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/report-p42i.py; python3 artifacts/studio/verification/TOOLS/check-p42i.py
```

## Current-run evidence

- [W-E2E-01/p42i-accounting/accounting.json](../W-E2E-01/p42i-accounting/accounting.json)
