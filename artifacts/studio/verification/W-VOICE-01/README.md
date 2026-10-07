# W-VOICE-01: Speak a destructive command without sending: nothing happens; final transcript appears; partial results never commit; final text becomes a prompt only on explicit Send (restated per SR-4.8 by owner decision 2026-10-07)

Verdict: **PASS**. Both current readiness-gated real voice takes reach final transcript after Stop/drain. Destructive text is visibly unsent; tray/request/journal counts stay exactly 1/1/120. Earlier move becomes a prompt/task only on explicit Send, then ordinary undo succeeds. Judged on SR-4.8; no partial-revision visibility requirement.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42i.py voice --row W-VOICE-01 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow voice2
```

## Current-run evidence

- [W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice/destructive-check.json](../W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice/destructive-check.json)
- [W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice/destructive-transcript.json](../W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice/destructive-transcript.json)
- [W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice/move-transcript.json](../W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice/move-transcript.json)
- [W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice-move/undo-result.json](../W-VOICE-01/p42i-voice-20261007T051405.333523Z/workflow/voice-move/undo-result.json)
- [W-VOICE-01/p42i-voice-20261007T051405.333523Z/visual-review.json](../W-VOICE-01/p42i-voice-20261007T051405.333523Z/visual-review.json)
- [W-VOICE-01/p42i-voice-20261007T051405.333523Z/result.json](../W-VOICE-01/p42i-voice-20261007T051405.333523Z/result.json)
