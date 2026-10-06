# W-HOST-01: `install.sh` fresh, then no-op; `verify.sh` with real image/describe/tts/realtime calls

Verdict: **BLOCKED**. Immutable reinstall, no-op and authenticated hello pass. Full fresh install would touch provider credentials/restart etosd; node still reports TTS cost 0 after on-disk price updates, and image/describe/realtime acceptance is incomplete.

Report timestamp: 2026-10-06T04:56:25.165304+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
bash artifacts/studio/verification/TOOLS/install-companion.sh
```

## Retained evidence

- [W-HOST-01/immutable-install-20261005T185516.633891Z/README.md](../W-HOST-01/immutable-install-20261005T185516.633891Z/README.md)
- [W-HOST-01/installed-hello-authority-20261005T185555.694549Z/README.md](../W-HOST-01/installed-hello-authority-20261005T185555.694549Z/README.md)
- [W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/fixtures/generate-tts.json](../W-AI-07/installed-3d-refusal-tts-tamper-20261005T192945.989942Z/fixtures/generate-tts.json)
