# W-VOICE-01: Speak a destructive command without sending: nothing happens; final transcript appears; partial revisions visible

Verdict: **FAIL**. Real PipeWire speech reached the microphone (44 frames, peak 0.199); the final transcript was “To lead every N P C in the village” with no partial revisions. Requests/journal/tray stayed unchanged.

Report timestamp: 2026-10-05T21:03:25.254136+00:00 UTC.

Acceptance baseline: merged main `e94f27aa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh voice
```

## Retained evidence

- [W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z/README.md](../W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z/README.md)
- [W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z/voice-result.json](../W-VOICE-01/capture-ready-recorded-wav-20261005T194304.382344Z/voice-result.json)
