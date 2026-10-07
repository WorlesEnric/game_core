# W-UI-04: Pump count exactly one per frame with the viewport open; Play Mode enter/exit 10×

Verdict: **PASS**. Ten current graphical Play/Edit cycles pass one-pump checks. Combined native/managed growth is 1.7193%, below 15%; full cycle-1/10 snapshots are losslessly retained.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh memory
```

## Current-run evidence

- [W-GAME-08/native-memory-and-pumps-20261007T115919.520196Z/result.json](../W-GAME-08/native-memory-and-pumps-20261007T115919.520196Z/result.json)
- [W-GAME-08/native-memory-and-pumps-20261007T115919.520196Z/memory-and-pumps.json](../W-GAME-08/native-memory-and-pumps-20261007T115919.520196Z/memory-and-pumps.json)
