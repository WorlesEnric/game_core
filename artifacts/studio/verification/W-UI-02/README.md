# W-UI-02: Box-select three NPCs behind a fence; overlap list; choose NPCs

Verdict: **PASS**. Current real marquee driver selects exactly Odd/Maren/Pip and excludes the fence entries; actual chooser controls and three NPCs behind the fence are visible.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42j.py editor
```

## Current-run evidence

- [W-UI-02/r7-d-20261007T120722657Z/receipt.json](../W-UI-02/r7-d-20261007T120722657Z/receipt.json)
- [W-UI-02/r7-d-20261007T120722657Z/visual-review.json](../W-UI-02/r7-d-20261007T120722657Z/visual-review.json)
