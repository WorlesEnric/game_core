# W-E2E-01: All rows resolved on one revision; completion report

Verdict: **BLOCKED**. P4.2d requalifies the requested R5/CORE-PICK rows on main 40fb91fa with the matching installed release. Real dialogue/quest Play effects and targeted NPC/history fixes pass, but the ferryman Play bake, full byte consistency, driver voice, and live admission retain failures. Describe remains unpriced. Untouched rows retain their original revision-specific evidence; this is not all-row product acceptance.

Report timestamp: 2026-10-06T12:40:21.191406+00:00 UTC.

Acceptance baseline: merged main `40fb91fa`; every linked run records its exact source commit and measured UTC timestamps. Evidence-only and P4_2 harness commits do not change the product implementation. Historical evidence is explicitly identified and never promoted to a current workflow pass.

## Reproduce

```sh
See docs/studio/packets/P4.2d-live-rerun.md and artifacts/studio/verification/TOOLS/README-P4.2d.md.
```

## Retained evidence

- [W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/play-effect.json](../W-AI-02/p42d-text2-20261006T113349.342620Z/workflow/ferryman2/play-effect.json)
- [W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/final.json](../W-AI-06/p42d-reopen-20261006T114958.044810Z/workflow/reopen/final.json)
- [W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/outcome.json](../W-MECH-01/p42d-stage-recover-20261006T122131.388873Z/outcome.json)
- [W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/destructive-transcript.json](../W-VOICE-01/p42d-voice2-20261006T112706.963706Z/workflow/voice/destructive-transcript.json)

Historical references: P4.2d installed release 0.1.0-e8a72b2d6eb3aad9 on main 40fb91fa. Earlier attempts remain retained; untouched rows keep their original revision-specific evidence.
