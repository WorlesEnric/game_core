# W-E2E-01: All rows resolved on one revision; completion report

Verdict: **FAIL**. All 68 rows are freshly judged at one product revision and installed companion release with strict host exclusivity; no historical PASS carries forward. End-to-end acceptance is FAIL because current W-AI-02 refuses the unchanged Ferryman candidate with GP-LOG-002 duplicate enrollment. Six exact owner-forbidden scenarios remain BLOCKED.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/report-p42k.py && python3 artifacts/studio/verification/TOOLS/check-p42k.py
```

## Current-run evidence

- [W-E2E-01/p42k-accounting/accounting.json](../W-E2E-01/p42k-accounting/accounting.json)
