# W-GAME-05: Full flow menu→save→load→ending→restart in the player

Verdict: **PASS**. Same current IL2CPP binary completes the unchanged menu/save/UI-Quit and second-process load/Ending-C/Play-Again route. Corrected PID-owned-window capture visibly proves both menus, Save UI, ending and restart; 321.2s concatenated recording retains both actual process IDs and segments. Initial desktop-only movie is explicitly rejected, not counted as graphical proof.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/lifecycle-p42i.py --player .evidence/P42iPlayer/player/Hollowmere.x86_64 --output .evidence/P42iLifecycleOwned
```

## Current-run evidence

- [W-GAME-05/p42i-owned-window-lifecycle/driver-summary.json](../W-GAME-05/p42i-owned-window-lifecycle/driver-summary.json)
- [W-GAME-05/p42i-owned-window-lifecycle/save-quit-report.json](../W-GAME-05/p42i-owned-window-lifecycle/save-quit-report.json)
- [W-GAME-05/p42i-owned-window-lifecycle/load-ending-restart-report.json](../W-GAME-05/p42i-owned-window-lifecycle/load-ending-restart-report.json)
- [W-GAME-05/p42i-owned-window-lifecycle/visual-review.json](../W-GAME-05/p42i-owned-window-lifecycle/visual-review.json)
- [W-GAME-05/p42i-owned-window-lifecycle/external-artifacts.json](../W-GAME-05/p42i-owned-window-lifecycle/external-artifacts.json)
- [W-GAME-05/p42i-player-lifecycle/visual-review.json](../W-GAME-05/p42i-player-lifecycle/visual-review.json)
