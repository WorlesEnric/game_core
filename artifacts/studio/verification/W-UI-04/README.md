# W-UI-04: Pump count exactly one per frame with the viewport open; Play Mode enter/exit 10×

Verdict: **PASS**. Ten current graphical Play/Edit cycles each record 60 frames and 60 sanctioned pumps. Combined native/managed growth is 1.6563%, below 15%; full cycle-1 and cycle-10 snapshots retained with hashes.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh memory
```

## Current-run evidence

- [W-GAME-08/native-memory-and-pumps-20261007T044042.839291Z/memory-and-pumps.json](../W-GAME-08/native-memory-and-pumps-20261007T044042.839291Z/memory-and-pumps.json)
- [W-GAME-08/native-memory-and-pumps-20261007T044042.839291Z/result.json](../W-GAME-08/native-memory-and-pumps-20261007T044042.839291Z/result.json)
- [W-GAME-08/native-memory-and-pumps-20261007T044042.839291Z/snapshot-manifest.json](../W-GAME-08/native-memory-and-pumps-20261007T044042.839291Z/snapshot-manifest.json)
