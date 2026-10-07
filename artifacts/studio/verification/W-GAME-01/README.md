# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **PASS**. Current 1080p RTX4060Ti VSync OFF runs both pass: p95 3.502/3.565ms over 607.410/611.724s, zero outside-transition frames over 100ms and maximum transition 124.909ms. Neither run is eligible for a rerun. VSync ON p95 17.331/17.315ms is informational only. All four actual game-window movies are retained.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42k.py
```

## Current-run evidence

- [W-GAME-01/p42k-player/result.json](../W-GAME-01/p42k-player/result.json)
- [W-GAME-01/p42k-player/visual-review.json](../W-GAME-01/p42k-player/visual-review.json)
- [W-GAME-01/p42k-player/retry-decision.json](../W-GAME-01/p42k-player/retry-decision.json)
- [W-GAME-01/p42k-player/external-artifacts.json](../W-GAME-01/p42k-player/external-artifacts.json)
