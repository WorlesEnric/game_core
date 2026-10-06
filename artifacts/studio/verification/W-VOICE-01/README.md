# W-VOICE-01: Speak a destructive command without sending: nothing happens; final transcript appears; partial revisions visible

Verdict: **FAIL**. R4-A microphone path delivers the move final and its explicit Send/apply/undo. Destructive speech yields no transcript in the reused session and a fresh ready-gated session (560 chunks, peak 0.15248). Request/tray/journal counts stay unchanged. Dedicated XML: 0 passed, 1 failed; listening/final status labels are not partial speech revisions.

Report timestamp: 2026-10-06T09:08:50.170407+00:00 UTC.

Acceptance baseline: merged main `f787829289ea7402c08917a78553ff6c3838bda8`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
studio/tools/verify-all.sh p42c voice2
studio/tools/verify-all.sh p42c voice-proof
```

## Retained evidence

- [W-VOICE-01/p42c-voice2-20261006T074412.994071Z/workflow/voice/destructive-check.json](../W-VOICE-01/p42c-voice2-20261006T074412.994071Z/workflow/voice/destructive-check.json)
- [W-VOICE-01/p42c-fresh-destructive-20261006T074915.219505Z/results.xml](../W-VOICE-01/p42c-fresh-destructive-20261006T074915.219505Z/results.xml)
- [W-VOICE-01/p42c-fresh-destructive-20261006T074915.219505Z/voice-result.json](../W-VOICE-01/p42c-fresh-destructive-20261006T074915.219505Z/voice-result.json)

Exact acceptance/component cases: `Hollowmere.P4_2.VoiceAcceptanceTests.R2_38_W_VOICE_01_DestructiveSpeechNeverSubmits`. Component cases do not close any missing external workflow.

Historical references: Earlier P4.2/P4.2b attempts remain retained; this disposition uses the installed P4.2c release and final-main product source.
