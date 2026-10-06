# W-ETOS-07: Generated texture arrives with matching sha256; tampered file refused

Verdict: **FAIL**. Legacy Unity media calls fail 404 for an unregistered local request ID. Direct TTS generation/download/import/undo and tamper refusal pass, but a new texture/import is not achieved.

Report timestamp: 2026-10-06T04:56:25.168689+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh media
```

## Retained evidence

- [W-ETOS-07/installed-media-20261005T192218.561813Z/README.md](../W-ETOS-07/installed-media-20261005T192218.561813Z/README.md)
- [W-ETOS-07/direct-tts-import-20261005T194103.515551Z/README.md](../W-ETOS-07/direct-tts-import-20261005T194103.515551Z/README.md)
- [W-ETOS-07/direct-tts-import-20261005T194103.515551Z/tts-import.json](../W-ETOS-07/direct-tts-import-20261005T194103.515551Z/tts-import.json)
- [W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/live/dotnet-f-i-refusals.json](../W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/live/dotnet-f-i-refusals.json)
- [W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md](../W-ETOS-07/direct-image-price-refusal-20261005T200701.405209Z/README.md)
