# W-EDIT-03: Generate portrait → apply → undo → redo: no second generation (usage unchanged)

Verdict: **PASS**. One current image generated/imported through the production media service; normal History undo/redo restored identical retained bytes with unchanged companion charge ledger; cleanup used normal History undo. Visual review: Generated, applied and redone pixels show the same brown-haired healer portrait with herbs and green-brown cloth. History screenshots are clipped by overlapping windows and do not visibly prove each entry state; normal panel receipts, retained journals, exact image hashes and unchanged ledger checkpoints establish replay. No readable-History-screenshot claim. Initial attempts stopped before generation because the ledger observer was absent; both are retained, and the same unused reservations were subsequently exercised with observers.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42k.py portrait --row W-EDIT-03 --method P42h.Media.Driver.RunPortrait
```

## Current-run evidence

- [W-EDIT-03/p42k-portrait/workflow/result.json](../W-EDIT-03/p42k-portrait/workflow/result.json)
- [W-EDIT-03/p42k-portrait/workflow/ledger-final.json](../W-EDIT-03/p42k-portrait/workflow/ledger-final.json)
- [W-EDIT-03/p42k-portrait-20261007T185846.087649Z/workflow/result.json](../W-EDIT-03/p42k-portrait-20261007T185846.087649Z/workflow/result.json)
