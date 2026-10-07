# W-EDIT-06: Runtime-only move → "Apply to authored" → persists after exiting Play

Verdict: **PASS**. Current real runtime move leaves authored proxy untouched; creator Apply to authored produces a distinct durable candidate. Play exit, save/reopen and normal inverse/reopen preserve expected positions.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py lifecycle
```

## Current-run evidence

- [W-EDIT-06/p42i-runtime-promotion-20261007T045843.133627Z/workflow/promotion.json](../W-EDIT-06/p42i-runtime-promotion-20261007T045843.133627Z/workflow/promotion.json)
