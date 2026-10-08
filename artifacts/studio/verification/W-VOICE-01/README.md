# W-VOICE-01: Speak a destructive command without sending: nothing happens; final transcript appears; partial results never commit; final text becomes a prompt only on explicit Send (restated per SR-4.8 by owner decision 2026-10-07)

Verdict: **PASS**. Final transcript visibly remains unsent; tray/request/journal counts stay 1/1/122. Earlier move is explicitly sent and undone. Provider transcription of the destructive fixture is imperfect (To, to lead to every NPC in the village.); no partial-revision visibility requirement is imposed.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/live-p42l.py voice --row W-VOICE-01 --method Hollowmere.P4_2.EvidenceEntry.RunStage --workflow voice2
```

## Current-run evidence

- [W-VOICE-01/p42l-voice-20261007T233657.311356Z/workflow/voice/destructive-check.json](../W-VOICE-01/p42l-voice-20261007T233657.311356Z/workflow/voice/destructive-check.json)
- [W-VOICE-01/p42l-voice-20261007T233657.311356Z/workflow/voice/destructive-transcript.json](../W-VOICE-01/p42l-voice-20261007T233657.311356Z/workflow/voice/destructive-transcript.json)
- [W-VOICE-01/p42l-voice-20261007T233657.311356Z/visual-review.json](../W-VOICE-01/p42l-voice-20261007T233657.311356Z/visual-review.json)
