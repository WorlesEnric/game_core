# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **PASS**. Current-run Generated texture arrives with matching sha256; tampered file refused is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/receipts-p42l.py tamper --artifact dc9e49e43cc72e5ba8b8d12877e1eb1eb7e555c4f5fc6dfb01f813aaa471cb52; python3 artifacts/studio/verification/TOOLS/receipts-p42l.py describe --artifact dc9e49e43cc72e5ba8b8d12877e1eb1eb7e555c4f5fc6dfb01f813aaa471cb52
```

## Current-run evidence

- [W-ETOS-07/p42l-tamper-20261007T234608.137312Z/tamper.json](../W-ETOS-07/p42l-tamper-20261007T234608.137312Z/tamper.json)
- [W-ETOS-07/p42l-describe-20261007T234617.273213Z/result.json](../W-ETOS-07/p42l-describe-20261007T234617.273213Z/result.json)
