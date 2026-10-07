# W-PLUG-11: Ambience crossfade, voice line, clip release

Verdict: **PASS**. Current graphics-enabled native crossfade/release and actual Maren voice tests pass. Recorded player audio matches Maren/village/marsh WAVs with correlations 0.6108/0.4972/0.4242, exceeding 0.2 and wrong-region controls. Its initial desktop-only video remains rejected; this row uses that recording’s real audio track, with no human-listening claim. A separate corrected owned-window recording closes W-GAME-05.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42i.py native; node artifacts/studio/verification/W-PLUG-11/audio-identity.mjs <current lifecycle movie> <output>
```

## Current-run evidence

- [W-PLUG-11/p42i-audio-identity-20261007T075637.391454Z/audio-identity.json](../W-PLUG-11/p42i-audio-identity-20261007T075637.391454Z/audio-identity.json)
- [W-PLUG-11/p42i-audio-identity-20261007T075637.391454Z/result.json](../W-PLUG-11/p42i-audio-identity-20261007T075637.391454Z/result.json)
- [W-PLUG-01/p42i-native-1-20261007T045942.121995Z/results.xml](../W-PLUG-01/p42i-native-1-20261007T045942.121995Z/results.xml)
- [W-PLUG-01/p42i-native-2-20261007T050214.386449Z/results.xml](../W-PLUG-01/p42i-native-2-20261007T050214.386449Z/results.xml)
- [W-PLUG-03/p42i-player-interactions-20261007T050443.011713Z/results.xml](../W-PLUG-03/p42i-player-interactions-20261007T050443.011713Z/results.xml)
