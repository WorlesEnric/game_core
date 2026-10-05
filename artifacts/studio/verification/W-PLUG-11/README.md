# W-PLUG-11: Ambience crossfade, voice line, clip release

Verdict: **BLOCKED**. Boot/UI/audio and direct TTS import pass; audible ambience crossfade and native clip release have no current combined capture/snapshot proof.

Report timestamp: 2026-10-05T21:03:25.259659+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh unity
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)
- [W-ETOS-07/direct-tts-import-20261005T194103.515551Z/README.md](../W-ETOS-07/direct-tts-import-20261005T194103.515551Z/README.md)
