# W-UI-05: Selection/hover and marquee timings

Verdict: **PASS**. Two current 500-candidate datasets contain 100 picks and 100 marquees each. Pick p95 is 0.9203/0.9819ms and marquee p95 0.2771/0.2545ms; unchanged limits are 16/50ms.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42j.py timing
```

## Current-run evidence

- [W-EDIT-08/p42j-timing-1-20261007T121340.343896Z/selection.json](../W-EDIT-08/p42j-timing-1-20261007T121340.343896Z/selection.json)
- [W-EDIT-08/p42j-timing-2-20261007T121510.067979Z/selection.json](../W-EDIT-08/p42j-timing-2-20261007T121510.067979Z/selection.json)
