# W-GAME-05: Full flow menu→save→load→ending→restart in the player

Verdict: **PASS**. Current release IL2CPP player completes menu/save/UI Quit, then separate-process load/Ending C/Play Again. Reviewed owned-window keyframes show both menus, Save UI and ending. The 320.3s movie explicitly concatenates two actual process segments.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/lifecycle-p42k.py --player .evidence/P42kPlayer/player/Hollowmere.x86_64 --output .evidence/P42kLifecycleOwned
```

## Current-run evidence

- [W-GAME-05/p42k-owned-window-lifecycle/driver-summary.json](../W-GAME-05/p42k-owned-window-lifecycle/driver-summary.json)
- [W-GAME-05/p42k-owned-window-lifecycle/load-ending-restart-report.json](../W-GAME-05/p42k-owned-window-lifecycle/load-ending-restart-report.json)
- [W-GAME-05/p42k-owned-window-lifecycle/visual-review.json](../W-GAME-05/p42k-owned-window-lifecycle/visual-review.json)
- [W-GAME-05/p42k-owned-window-lifecycle/external-artifacts.json](../W-GAME-05/p42k-owned-window-lifecycle/external-artifacts.json)
