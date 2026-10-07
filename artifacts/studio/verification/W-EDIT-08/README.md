# W-EDIT-08: Apply timings (single and region-wide)

Verdict: **PASS**. Two current 20-sample datasets pass. Single/Marsh-all-NPC/kernel-prepare p95 ms: [[22.3321, 31.6321, 9.5897], [25.0379, 34.204100000000004, 10.7461]]; limits 200/1000/300 ms.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py timing
```

## Current-run evidence

- [W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/apply.json](../W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/apply.json)
- [W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/region-apply.json](../W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/region-apply.json)
- [W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/compose.json](../W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/compose.json)
- [W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/results.xml](../W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/results.xml)
- [W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/apply.json](../W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/apply.json)
- [W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/region-apply.json](../W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/region-apply.json)
- [W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/compose.json](../W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/compose.json)
- [W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/results.xml](../W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/results.xml)
