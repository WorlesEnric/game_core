# W-VOICE-01: Speak a destructive command without sending: nothing happens; final transcript appears; partial revisions visible

Verdict: **BLOCKED**. The unchanged R6-B driver now passes both readiness-gated real takes: move and destructive final transcripts arrive after proper Stop/drain. Destructive tray/request/journal counts remain exactly 8/11/122, and the field holds the destructive text without Send. Both provider traces contain only revision 1 with final=true; no partial speech revision is observed. Full-row partial-revision visibility remains unqualified despite the passing driver.

Report timestamp: 2026-10-06T15:29:44.395031+00:00 UTC.

Acceptance baseline: merged main `d140f748`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42e.py voice2
```

## Retained evidence

- [W-VOICE-01/p42e-voice2-20261006T152310.669342Z/result.json](../W-VOICE-01/p42e-voice2-20261006T152310.669342Z/result.json)
- [W-VOICE-01/p42e-voice2-20261006T152310.669342Z/workflow/voice/move-transcript.json](../W-VOICE-01/p42e-voice2-20261006T152310.669342Z/workflow/voice/move-transcript.json)
- [W-VOICE-01/p42e-voice2-20261006T152310.669342Z/workflow/voice/destructive-transcript.json](../W-VOICE-01/p42e-voice2-20261006T152310.669342Z/workflow/voice/destructive-transcript.json)
- [W-VOICE-01/p42e-voice2-20261006T152310.669342Z/workflow/voice/destructive-check.json](../W-VOICE-01/p42e-voice2-20261006T152310.669342Z/workflow/voice/destructive-check.json)
- [../workflows/P4.2e/companion-voice.log](../../workflows/P4.2e/companion-voice.log)

Exact acceptance/component cases: `R6_Request3_ActualVoiceTakeDoesNotPlayOnElapsedTimerAndDrains`, `test_R2_38_P42e_failed_move_does_not_starve_distinct_destructive_take`. Component cases do not close any missing external workflow.

Historical references: P4.2e installed release 0.1.0-cac2f82c59be070b on main d140f748. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
