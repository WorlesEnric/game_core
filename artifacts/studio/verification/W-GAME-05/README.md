# W-GAME-05: Full flow menu→save→load→ending→restart in the player

Verdict: **PASS**. Current release IL2CPP player completes menu/save/UI Quit, then separate-process load/Ending C/Play Again. Reviewed PID-owned-window keyframes show both menus, Saved to slot-1, ending and restarted village. Movie explicitly concatenates two actual processes.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/lifecycle-p42l.py --player .evidence/P42lPlayer/player/Hollowmere.x86_64 --output .evidence/P42lLifecycleOwned
```

## Current-run evidence

- [W-GAME-05/p42l-owned-window-lifecycle/driver-summary.json](../W-GAME-05/p42l-owned-window-lifecycle/driver-summary.json)
- [W-GAME-05/p42l-owned-window-lifecycle/load-ending-restart-report.json](../W-GAME-05/p42l-owned-window-lifecycle/load-ending-restart-report.json)
- [W-GAME-05/p42l-owned-window-lifecycle/visual-review.json](../W-GAME-05/p42l-owned-window-lifecycle/visual-review.json)
- [W-GAME-05/p42l-owned-window-lifecycle/external-artifacts.json](../W-GAME-05/p42l-owned-window-lifecycle/external-artifacts.json)
