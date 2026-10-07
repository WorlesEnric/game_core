# W-MECH-01: Pressure-plate mechanism: staged, admitted, world resumed from checkpoint

Verdict: **FAIL**. Both current signed Docker stages pass (166.402s cold, 76.786s warm; each 36 EditMode and 2 PlayMode cases), but both creator admissions roll back with catalog_mismatch. Signed world 6c13778e differs from live re-baked d82aed18 after a stale-bake warning. No restored-world smoke or successful admission is claimed; both packages are removed and rollback completes.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/stages-p42j.py mechanism; after verified cold rollback only: python3 artifacts/studio/verification/TOOLS/stages-p42j.py mechanism --mechanism-run warm
```

## Current-run evidence

- [W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/outcome.json](../W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/outcome.json)
- [W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/outcome.json](../W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/outcome.json)
- [W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/admission.json](../W-MECH-01/p42j-stage-review-cold-20261007T131253.042554Z/admission.json)
- [W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/admission.json](../W-MECH-01/p42j-stage-review-warm-20261007T132025.462151Z/admission.json)
