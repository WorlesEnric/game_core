# W-PLUG-11: Ambience crossfade, voice line, clip release

Verdict: **PASS**. Current native crossfade/release and actual Maren voice cases pass. Same current movie correlates with authored voice/village/marsh at 0.5696/0.4869/0.5445, above 0.2 and wrong-region controls. The old fixed 100-110s window crossed this movie's transition and failed; current marker-derived ten-second windows preserve thresholds, samples and the initial failure. No new recording or human-listening claim.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/rows-p42j.py native; python3 artifacts/studio/verification/TOOLS/audio-p42j.py .evidence/P42jLifecycleOwned artifacts/studio/verification/W-PLUG-11/p42j-marker-aligned-audio
```

## Current-run evidence

- [W-PLUG-11/p42j-marker-aligned-audio/audio-identity.json](../W-PLUG-11/p42j-marker-aligned-audio/audio-identity.json)
- [W-PLUG-01/p42j-native-1-20261007T122027.825900Z/result.json](../W-PLUG-01/p42j-native-1-20261007T122027.825900Z/result.json)
- [W-PLUG-01/p42j-native-2-20261007T122240.031825Z/result.json](../W-PLUG-01/p42j-native-2-20261007T122240.031825Z/result.json)
- [W-PLUG-11/p42j-marker-aligned-audio/alignment.json](../W-PLUG-11/p42j-marker-aligned-audio/alignment.json)
- [W-PLUG-11/p42j-audio-identity-20261007T144149.852637Z/result.json](../W-PLUG-11/p42j-audio-identity-20261007T144149.852637Z/result.json)
