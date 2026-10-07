# W-EDIT-03: Generate portrait → apply → undo → redo: no second generation (usage unchanged)

Verdict: **PASS**. One current USD0.20 portrait imports, undoes, redoes byte-identically and finally undoes with unchanged charge checkpoints. Actual portrait pixels match. Overlapping History screenshots do not visibly prove states; successful normal-panel receipts and retained journals provide that proof.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42j.py portrait --row W-EDIT-03 --method P42h.Media.Driver.RunPortrait
```

## Current-run evidence

- [W-EDIT-03/p42j-portrait/workflow/result.json](../W-EDIT-03/p42j-portrait/workflow/result.json)
- [W-EDIT-03/p42j-portrait/workflow/ledger-final.json](../W-EDIT-03/p42j-portrait/workflow/ledger-final.json)
