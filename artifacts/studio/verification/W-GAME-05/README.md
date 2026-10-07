# W-GAME-05: Full flow menu→save→load→ending→restart in the player

Verdict: **PASS**. Current release IL2CPP player completes menu/save/UI Quit, then separate-process load/Ending C/Play Again. Actual owned-window keyframes prove both menus, Save UI, ending and restart; the 321.6s movie explicitly concatenates the two process segments.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/lifecycle-p42j.py --player .evidence/P42jPlayer/player/Hollowmere.x86_64 --output .evidence/P42jLifecycleOwned
```

## Current-run evidence

- [W-GAME-05/p42j-owned-window-lifecycle/driver-summary.json](../W-GAME-05/p42j-owned-window-lifecycle/driver-summary.json)
- [W-GAME-05/p42j-owned-window-lifecycle/load-ending-restart-report.json](../W-GAME-05/p42j-owned-window-lifecycle/load-ending-restart-report.json)
- [W-GAME-05/p42j-owned-window-lifecycle/visual-review.json](../W-GAME-05/p42j-owned-window-lifecycle/visual-review.json)
