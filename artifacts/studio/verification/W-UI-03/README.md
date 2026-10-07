# W-UI-03: Subpart vs logical vs prefab vs scope choice on a lantern

Verdict: **PASS**. Real 1280x720 lantern chooser exposes and exercises Logical, Mesh:Body, Prefab and Instance-scope buttons; exact refs/scopes and parts reset pass.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py editor
```

## Current-run evidence

- [W-UI-03/r7-d-20261007T044913085Z/receipt.json](../W-UI-03/r7-d-20261007T044913085Z/receipt.json)
- [W-UI-03/r7-d-20261007T044913085Z/observations.json](../W-UI-03/r7-d-20261007T044913085Z/observations.json)
- [W-UI-03/r7-d-20261007T044913085Z/visual-review.json](../W-UI-03/r7-d-20261007T044913085Z/visual-review.json)
