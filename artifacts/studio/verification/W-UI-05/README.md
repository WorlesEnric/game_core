# W-UI-05: Selection/hover and marquee timings

Verdict: **PASS**. Two fresh 500-candidate datasets each contain 100 picks and 100 marquees. Pick/marquee p95 ms: [[1.0265, 0.27540000000000003], [0.9298000000000001, 0.23820000000000002]]; unchanged limits 16/50 ms.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py timing
```

## Current-run evidence

- [W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/selection.json](../W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/selection.json)
- [W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/selection.json](../W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/selection.json)
- [W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/results.xml](../W-EDIT-08/p42i-timing-1-20261007T045345.335538Z/results.xml)
- [W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/results.xml](../W-EDIT-08/p42i-timing-2-20261007T045434.953869Z/results.xml)
