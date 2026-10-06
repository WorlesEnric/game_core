# W-VOICE-01: Speak a destructive command without sending: nothing happens; final transcript appears; partial revisions visible

Verdict: **FAIL**. R5-B ready-gated self-test passes both real speech fixtures with no submission. The later unchanged driver fails both takes after a provider session.updated acknowledgement error: playback falls outside capture and two late sessions deliver 70/144 all-zero frames, with zero transcripts. Destructive tray/request/journal counts remain unchanged. No transcript loss occurs on the companion→client hop in the passing self-test.

Report timestamp: 2026-10-06T12:40:21.169411+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
bash artifacts/studio/verification/TOOLS/voice-self-p42d.sh
studio/tools/verify-all.sh p42d voice2
```

## Retained evidence

- [W-VOICE-01/p42d-voice-self-compile-retry-20261006T112540.255603Z/results.xml](../W-VOICE-01/p42d-voice-self-compile-retry-20261006T112540.255603Z/results.xml)
- [W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/move-transcript.json](../W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/move-transcript.json)
- [W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/destructive-transcript.json](../W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/destructive-transcript.json)
- [W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/destructive-check.json](../W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/destructive-check.json)

Exact acceptance/component cases: `Hollowmere.R5_B.VirtualVoiceSelfTest.Request6_VirtualMicrophoneBothRetainedLinesTranscribe`. Component cases do not close any missing external workflow.

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
