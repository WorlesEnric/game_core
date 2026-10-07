# W-PLUG-10: Definition validator parity (inspector, validator, agent)

Verdict: **PASS**. Current actual inspector, validator console and gateway-with-fake-transport report identical canonical GP-ENT-006 diagnostic; candidate refuses without writes.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py native
```

## Current-run evidence

- [W-PLUG-10/p42i-validator-parity-20261007T050539.192872Z/results.xml](../W-PLUG-10/p42i-validator-parity-20261007T050539.192872Z/results.xml)
- [W-PLUG-10/p42i-validator-parity-20261007T050539.192872Z/result.json](../W-PLUG-10/p42i-validator-parity-20261007T050539.192872Z/result.json)
