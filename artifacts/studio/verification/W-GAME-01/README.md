# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **PASS**. Current 1080p RTX4060Ti VSync OFF recordings both pass: p95 3.505/3.459ms over 613.087/609.749s, zero outside-transition frames over 100ms and maximum transition 144.944ms. Neither run is eligible for a rerun. VSync ON is informational; all four actual game-window movies retained.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42l.py
```

## Current-run evidence

- [W-GAME-01/p42l-player/result.json](../W-GAME-01/p42l-player/result.json)
- [W-GAME-01/p42l-player/visual-review.json](../W-GAME-01/p42l-player/visual-review.json)
- [W-GAME-01/p42l-player/retry-decision.json](../W-GAME-01/p42l-player/retry-decision.json)
- [W-GAME-01/p42l-player/external-artifacts.json](../W-GAME-01/p42l-player/external-artifacts.json)
