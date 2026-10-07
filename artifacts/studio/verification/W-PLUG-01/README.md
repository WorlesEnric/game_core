# W-PLUG-01: Three-region loop, moved NPC stays, memory baseline, timings

Verdict: **PASS**. Two real graphical three-region loop runs preserve moved NPC state and release region views/media. Native residuals 0.08825–0.15878% of peak; all load/unload timings meet 2s/1s. Twelve full snapshots retained losslessly with raw/compressed hashes.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py native
```

## Current-run evidence

- [W-PLUG-01/p42i-native-1-20261007T045942.121995Z/results.xml](../W-PLUG-01/p42i-native-1-20261007T045942.121995Z/results.xml)
- [W-PLUG-01/p42i-native-1-20261007T045942.121995Z/native/native-media.csv](../W-PLUG-01/p42i-native-1-20261007T045942.121995Z/native/native-media.csv)
- [W-PLUG-01/p42i-native-1-20261007T045942.121995Z/native/snapshot-manifest.json](../W-PLUG-01/p42i-native-1-20261007T045942.121995Z/native/snapshot-manifest.json)
- [W-PLUG-01/p42i-native-1-20261007T045942.121995Z/native/retention.json](../W-PLUG-01/p42i-native-1-20261007T045942.121995Z/native/retention.json)
- [W-PLUG-01/p42i-native-2-20261007T050214.386449Z/results.xml](../W-PLUG-01/p42i-native-2-20261007T050214.386449Z/results.xml)
- [W-PLUG-01/p42i-native-2-20261007T050214.386449Z/native/native-media.csv](../W-PLUG-01/p42i-native-2-20261007T050214.386449Z/native/native-media.csv)
- [W-PLUG-01/p42i-native-2-20261007T050214.386449Z/native/snapshot-manifest.json](../W-PLUG-01/p42i-native-2-20261007T050214.386449Z/native/snapshot-manifest.json)
- [W-PLUG-01/p42i-native-2-20261007T050214.386449Z/native/retention.json](../W-PLUG-01/p42i-native-2-20261007T050214.386449Z/native/retention.json)
