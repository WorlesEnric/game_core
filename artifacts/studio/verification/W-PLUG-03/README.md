# W-PLUG-03: Walk, jump a ledge, focus prompt, interact dispatch

Verdict: **PASS**. Actual walking/focus/interaction/travel and paired 110-frame solid-ledge traces pass; Jump clears the lip, descends and lands grounded.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py native
```

## Current-run evidence

- [W-PLUG-03/p42i-player-interactions-20261007T050443.011713Z/results.xml](../W-PLUG-03/p42i-player-interactions-20261007T050443.011713Z/results.xml)
- [W-PLUG-03/p42i-player-interactions-20261007T050443.011713Z/result.json](../W-PLUG-03/p42i-player-interactions-20261007T050443.011713Z/result.json)
