# W-UI-04: Pump count exactly one per frame with the viewport open; Play Mode enter/exit 10×

Verdict: **PASS**. Ten graphical Play/Edit cycles: every measured frame has one pump and zero violations; combined engine/managed growth is +0.715% against the unchanged +15% limit. Full cycle-1/cycle-10 snapshots are retained with hashes.

Report timestamp: 2026-10-05T21:03:25.244642+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh memory
```

## Retained evidence

- [W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/README.md](../W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/README.md)
- [W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/memory-and-pumps.json](../W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/memory-and-pumps.json)
- [W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/snapshot-manifest.json](../W-GAME-08/final-ten-cycle-snapshots-20261005T201735.176679Z/snapshot-manifest.json)
