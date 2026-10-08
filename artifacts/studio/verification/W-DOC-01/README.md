# W-DOC-01: New user adds an NPC with dialogue from the creator guide

Verdict: **PASS**. Current-run New user adds an NPC with dialogue from the creator guide is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42l.py guide
```

## Current-run evidence

- [W-DOC-01/p42l-creator-guide-20261007T233546.090584Z/results.xml](../W-DOC-01/p42l-creator-guide-20261007T233546.090584Z/results.xml)
- [W-DOC-01/p42l-creator-guide-20261007T233546.090584Z/guide.json](../W-DOC-01/p42l-creator-guide-20261007T233546.090584Z/guide.json)
