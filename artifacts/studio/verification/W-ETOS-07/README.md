# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **PASS**. Current generated PNG imports with verified matching SHA256. Downloading that same owned artifact with one byte tampered refuses artifact_digest_mismatch; a clean reread equals the original bytes, with no regeneration. One priced describe call consumes the same digest for USD0.01.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/receipts-p42i.py tamper --artifact 0d00c9991e76dede94bbf4d3cbc51b8dcb0dfa700733608ea3a1debf484612e7
```

## Current-run evidence

- [W-EDIT-03/p42i-portrait/workflow/generate.json](../W-EDIT-03/p42i-portrait/workflow/generate.json)
- [W-ETOS-07/p42i-tamper-20261007T052130.875133Z/tamper.json](../W-ETOS-07/p42i-tamper-20261007T052130.875133Z/tamper.json)
- [W-ETOS-07/p42i-tamper-20261007T052130.875133Z/result.json](../W-ETOS-07/p42i-tamper-20261007T052130.875133Z/result.json)
- [W-ETOS-07/p42i-describe-20261007T052135.363495Z/describe.json](../W-ETOS-07/p42i-describe-20261007T052135.363495Z/describe.json)
- [W-ETOS-07/p42i-describe-20261007T052135.363495Z/result.json](../W-ETOS-07/p42i-describe-20261007T052135.363495Z/result.json)
