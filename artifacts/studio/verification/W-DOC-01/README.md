# W-DOC-01: New user adds an NPC with dialogue from the creator guide

Verdict: **PASS**. Current-run New user adds an NPC with dialogue from the creator guide is verified by the linked named XML cases and/or exact JSON assertions; no prior-run result is used.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42j.py guide
```

## Current-run evidence

- [W-DOC-01/p42j-creator-guide-20261007T123414.950608Z/results.xml](../W-DOC-01/p42j-creator-guide-20261007T123414.950608Z/results.xml)
- [W-DOC-01/p42j-creator-guide-20261007T123414.950608Z/guide.json](../W-DOC-01/p42j-creator-guide-20261007T123414.950608Z/guide.json)
