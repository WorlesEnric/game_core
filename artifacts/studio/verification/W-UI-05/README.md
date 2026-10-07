# W-UI-05: Selection/hover and marquee timings

Verdict: **PASS**. Two current datasets contain 100 picks and 100 marquees over 500 candidates. Pick p95 is 1.499/0.881ms; marquee p95 0.4756/0.2362ms. Unchanged limits are 16/50ms.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42k.py timing
```

## Current-run evidence

- [W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/selection.json](../W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/selection.json)
- [W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/selection.json](../W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/selection.json)
