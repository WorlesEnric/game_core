# W-KERNEL-01: Corrupted catalog → named failure, not an empty world

Verdict: **PASS**. Current production corrupted-catalog boot returns named CatalogFingerprintMismatch and creates no world.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py validation
```

## Current-run evidence

- [W-KERNEL-01/p42i-kernel-save-20261007T051059.070176Z/results.xml](../W-KERNEL-01/p42i-kernel-save-20261007T051059.070176Z/results.xml)
- [W-KERNEL-01/p42i-kernel-save-20261007T051059.070176Z/result.json](../W-KERNEL-01/p42i-kernel-save-20261007T051059.070176Z/result.json)
