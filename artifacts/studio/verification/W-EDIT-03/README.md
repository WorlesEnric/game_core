# W-EDIT-03: Generate portrait → apply → undo → redo: no second generation (usage unchanged)

Verdict: **PASS**. One current image generated/imported through the production media service; normal History undo/redo restored identical retained bytes with unchanged companion charge ledger; cleanup used normal History undo. Visual review: Generated, applied and redone images visibly show the same adult healer portrait. History screenshots are obscured by overlapping windows; normal History receipts, retained hashes and unchanged charge checkpoints establish replay, not readable History pixels.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
export GC_STUDIO_DISK_RESERVE_GIB=28
python3 artifacts/studio/verification/TOOLS/P42hMedia/ledger.py --out artifacts/studio/verification/W-EDIT-03/p42l-portrait/workflow &
observer=$!
python3 artifacts/studio/verification/TOOLS/live-p42l.py portrait --row W-EDIT-03 --method P42h.Media.Driver.RunPortrait --workflow-out artifacts/studio/verification/W-EDIT-03/p42l-portrait/workflow
wait "$observer"
# One-use reservation: do not replay an already attempted run. P4.2l packet retains the robe pre-generation failure and exact baseline-restored continuation.
```

## Current-run evidence

- [W-EDIT-03/p42l-portrait/workflow/result.json](../W-EDIT-03/p42l-portrait/workflow/result.json)
- [W-EDIT-03/p42l-portrait/workflow/ledger-final.json](../W-EDIT-03/p42l-portrait/workflow/ledger-final.json)
- [W-EDIT-03/p42l-portrait/workflow/result.json](../W-EDIT-03/p42l-portrait/workflow/result.json)
