# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **PASS**. The current generated portrait digest imports correctly; tampering one byte refuses artifact_digest_mismatch and clean reread matches without regeneration. One current describe uses that same digest under the USD0.01 bound tariff.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/receipts-p42j.py tamper --artifact dc9e49e43cc72e5ba8b8d12877e1eb1eb7e555c4f5fc6dfb01f813aaa471cb52; python3 artifacts/studio/verification/TOOLS/receipts-p42j.py describe --artifact dc9e49e43cc72e5ba8b8d12877e1eb1eb7e555c4f5fc6dfb01f813aaa471cb52
```

## Current-run evidence

- [W-ETOS-07/p42j-tamper-20261007T124429.514414Z/tamper.json](../W-ETOS-07/p42j-tamper-20261007T124429.514414Z/tamper.json)
- [W-ETOS-07/p42j-describe-20261007T124438.536458Z/result.json](../W-ETOS-07/p42j-describe-20261007T124438.536458Z/result.json)
