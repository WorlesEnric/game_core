# W-UI-04: Pump count exactly one per frame with the viewport open; Play Mode enter/exit 10×

Verdict: **PASS**. Ten current graphical Play/Edit cycles pass one-pump checks. Combined native/managed memory growth is 1.8041%, below the unchanged 15% limit. Full cycle-1/10 snapshots are losslessly retained.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh memory
```

## Current-run evidence

- [W-GAME-08/native-memory-and-pumps-20261007T182723.901674Z/result.json](../W-GAME-08/native-memory-and-pumps-20261007T182723.901674Z/result.json)
- [W-GAME-08/native-memory-and-pumps-20261007T182723.901674Z/memory-and-pumps.json](../W-GAME-08/native-memory-and-pumps-20261007T182723.901674Z/memory-and-pumps.json)
