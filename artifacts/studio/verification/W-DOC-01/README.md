# W-DOC-01: New user adds an NPC with dialogue from the creator guide

Verdict: **PASS**. Current creator-guide existing-Maren-definition flow places an NPC, appends the bell dialogue line, saves, and ordinary History undo restores roster 20 and graph 13. Named XML passes. Auxiliary viewport screenshot is blank; no additional visual-layout claim.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py guide
```

## Current-run evidence

- [W-DOC-01/p42i-creator-guide-20261007T051239.640968Z/results.xml](../W-DOC-01/p42i-creator-guide-20261007T051239.640968Z/results.xml)
- [W-DOC-01/p42i-creator-guide-20261007T051239.640968Z/guide.json](../W-DOC-01/p42i-creator-guide-20261007T051239.640968Z/guide.json)
- [W-DOC-01/p42i-creator-guide-20261007T051239.640968Z/guide-applied.png](../W-DOC-01/p42i-creator-guide-20261007T051239.640968Z/guide-applied.png)
