# W-GAME-08: Memory after 10 Play/Edit cycles

Verdict: **PASS**. Ten current graphical Play/Edit cycles pass one-pump checks. Combined native/managed memory growth is 1.5436%, below unchanged 15% limit. Full cycle-1/10 snapshots are losslessly retained.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh memory
```

## Current-run evidence

- [W-GAME-08/native-memory-and-pumps-20261007T230751.084405Z/result.json](../W-GAME-08/native-memory-and-pumps-20261007T230751.084405Z/result.json)
- [W-GAME-08/native-memory-and-pumps-20261007T230751.084405Z/memory-and-pumps.json](../W-GAME-08/native-memory-and-pumps-20261007T230751.084405Z/memory-and-pumps.json)
