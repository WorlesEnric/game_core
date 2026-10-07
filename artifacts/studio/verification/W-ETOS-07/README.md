# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **PASS**. Current-run Generated texture arrives with matching sha256; tampered file refused is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/receipts-p42k.py tamper --artifact dc9e49e43cc72e5ba8b8d12877e1eb1eb7e555c4f5fc6dfb01f813aaa471cb52; python3 artifacts/studio/verification/TOOLS/receipts-p42k.py describe --artifact dc9e49e43cc72e5ba8b8d12877e1eb1eb7e555c4f5fc6dfb01f813aaa471cb52
```

## Current-run evidence

- [W-ETOS-07/p42k-tamper-20261007T192804.029366Z/tamper.json](../W-ETOS-07/p42k-tamper-20261007T192804.029366Z/tamper.json)
- [W-ETOS-07/p42k-describe-20261007T192814.175198Z/result.json](../W-ETOS-07/p42k-describe-20261007T192814.175198Z/result.json)
