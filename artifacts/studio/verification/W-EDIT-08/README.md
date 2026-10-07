# W-EDIT-08: Apply timings (single and region-wide)

Verdict: **PASS**. Two current 20-sample datasets pass: single-target p95 25.926/24.6934ms; Marsh-all-NPC p95 43.5722/35.9275ms; kernel prepare p95 9.8924/7.0972ms. Limits remain 200/1000/300ms.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42k.py timing
```

## Current-run evidence

- [W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/apply.json](../W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/apply.json)
- [W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/region-apply.json](../W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/region-apply.json)
- [W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/compose.json](../W-EDIT-08/p42k-timing-1-20261007T183741.128628Z/compose.json)
- [W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/apply.json](../W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/apply.json)
- [W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/region-apply.json](../W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/region-apply.json)
- [W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/compose.json](../W-EDIT-08/p42k-timing-2-20261007T183828.714513Z/compose.json)
