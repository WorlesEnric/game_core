# W-GAME-06: Build log + sha256 + V1 gate transcript on the same revision

Verdict: **PASS**. Current-source release Hollowmere Linux IL2CPP build passes with executable/full-file hashes. Complete unchanged V1 gate passes 1318 EditMode and 84 PlayMode XML cases, codegen byte identity, both build profiles, prescribed probes and docs checks. An owned detached worktree at the exact product revision holds new build caches on /tmp; no compiler caches reclaimed from /home.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42l.py
```

## Current-run evidence

- [W-GAME-06/v1-gate-retry-20261008T014132.752757Z/result.json](../W-GAME-06/v1-gate-retry-20261008T014132.752757Z/result.json)
- [W-GAME-01/p42l-player/player/build-report.json](../W-GAME-01/p42l-player/player/build-report.json)
- [W-GAME-06/v1-gate-retry-20261008T014132.752757Z/gate/unity/playmode-results.xml](../W-GAME-06/v1-gate-retry-20261008T014132.752757Z/gate/unity/playmode-results.xml)
- [W-GAME-06/v1-gate-retry-20261008T014132.752757Z/gate/unity/editmode-results.xml](../W-GAME-06/v1-gate-retry-20261008T014132.752757Z/gate/unity/editmode-results.xml)
- [W-GAME-06/v1-gate-retry-20261008T014132.752757Z/external-artifacts.json](../W-GAME-06/v1-gate-retry-20261008T014132.752757Z/external-artifacts.json)
