# W-PLUG-11: Ambience crossfade, voice line, clip release

Verdict: **PASS**. Current graphical native crossfade/release and actual Maren voice cases pass. Marker-derived ten-second audio windows pass unchanged 2000Hz correlation metric at threshold 0.2: voice 0.5783, Village 0.4657, Marsh 0.4985; wrong-region controls 0.0509/0.0668. Clock anchoring is estimated; no audio-search-selected windows or human-listening claim.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/audio-p42l.py .evidence/P42lLifecycleOwned <fresh-output>
```

## Current-run evidence

- [W-PLUG-11/p42l-audio-identity-20261008T012753.430802Z/result.json](../W-PLUG-11/p42l-audio-identity-20261008T012753.430802Z/result.json)
- [W-PLUG-11/p42l-audio-identity-20261008T012753.430802Z/audio-identity.json](../W-PLUG-11/p42l-audio-identity-20261008T012753.430802Z/audio-identity.json)
- [W-PLUG-11/p42l-audio-identity-20261008T012753.430802Z/alignment.json](../W-PLUG-11/p42l-audio-identity-20261008T012753.430802Z/alignment.json)
