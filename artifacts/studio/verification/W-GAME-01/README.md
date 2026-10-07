# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **PASS**. Current release Linux IL2CPP on real RTX4060Ti/:1 at 1920x1080: VSync OFF p95 3.404/3.463ms over 607.729/608.971s, zero >100ms frames outside transitions, maximum transition 123.080ms. Both OFF recordings pass B-FRAME. ON p95 17.230/17.261ms is informational only; all four actual game-window recordings and routes pass their other checks.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42i.py
```

## Current-run evidence

- [W-GAME-01/p42i-player/result.json](../W-GAME-01/p42i-player/result.json)
- [W-GAME-01/p42i-player/player/build-report.json](../W-GAME-01/p42i-player/player/build-report.json)
- [W-GAME-01/p42i-player/visual-review.json](../W-GAME-01/p42i-player/visual-review.json)
- [W-GAME-01/p42i-player/external-artifacts.json](../W-GAME-01/p42i-player/external-artifacts.json)
