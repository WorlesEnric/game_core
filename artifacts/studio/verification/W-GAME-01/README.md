# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **FAIL**. Current 1080p RTX4060Ti VSync OFF run 1 fails B-FRAME: outside-transition frames are 374.077, 174.926 and 1352.808 ms. OFF p95 is 3.547/3.612 ms over 612.244/612.467s; run 2 has zero outside-transition >100ms frames. No failing probe is repeated. ON p95 17.380/17.266 ms is informational only.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42j.py
```

## Current-run evidence

- [W-GAME-01/p42j-player/result.json](../W-GAME-01/p42j-player/result.json)
- [W-GAME-01/p42j-player/measurement/vsync0/run1/frame-stats.json](../W-GAME-01/p42j-player/measurement/vsync0/run1/frame-stats.json)
- [W-GAME-01/p42j-player/visual-review.json](../W-GAME-01/p42j-player/visual-review.json)
