# W-UI-01: Open Hollowmere in Studio; Play; walk; Select; click NPC; card shows definition

Verdict: **PASS**. Current ordinary Open Studio connects and the walking driver passes; actual Select capture shows Maren and MarenEntity in the readable context card.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42j.py editor
```

## Current-run evidence

- [W-UI-01/p42j-open-play-20261007T120519.948523Z/workflow/result.json](../W-UI-01/p42j-open-play-20261007T120519.948523Z/workflow/result.json)
- [W-UI-01/p42j-open-play-20261007T120519.948523Z/visual-review.json](../W-UI-01/p42j-open-play-20261007T120519.948523Z/visual-review.json)
