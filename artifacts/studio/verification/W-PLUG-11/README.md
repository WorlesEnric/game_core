# W-PLUG-11: Ambience crossfade, voice line, clip release

Verdict: **PASS**. Current native crossfade/release and actual Maren voice cases pass. Current lifecycle movie passes the unchanged normalized audio-identity metric using marker-derived ten-second windows, authored voice/Village/Marsh samples and wrong-region controls. No audio-search-selected windows or human-listening claim.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/audio-p42k.py .evidence/P42kLifecycleOwned <fresh-output>
```

## Current-run evidence

- [W-PLUG-11/p42k-audio-identity-20261007T205516.225809Z/result.json](../W-PLUG-11/p42k-audio-identity-20261007T205516.225809Z/result.json)
- [W-PLUG-11/p42k-audio-identity-20261007T205516.225809Z/audio-identity.json](../W-PLUG-11/p42k-audio-identity-20261007T205516.225809Z/audio-identity.json)
- [W-PLUG-11/p42k-audio-identity-20261007T205516.225809Z/alignment.json](../W-PLUG-11/p42k-audio-identity-20261007T205516.225809Z/alignment.json)
