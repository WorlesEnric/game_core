# W-CLEAN-01: Clean project: install, author, run, build

Verdict: **PASS**. Current clean-project author/bake, 11 EditMode and three PlayMode cases, both rechecks, same-revision Linux IL2CPP build and standalone 600-frame quest/save/restore/ending autoplay pass with zero pump violations. Build uses owned external same-revision worktree to preserve /home reserve.

P4.2l product revision: `6e8e73c42427e4f65ffae6f5028373a0566ba1d0`; installed release: `0.1.0-b50cd34dddae2cc4`. Reported: 2026-10-08T02:38:22.131501+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42l.py
```

## Current-run evidence

- [W-CLEAN-01/p42l-clean-build-20261008T012754.477935Z/result.json](../W-CLEAN-01/p42l-clean-build-20261008T012754.477935Z/result.json)
- [W-CLEAN-01/p42l-clean-player-20261008T014119.503790Z/result.json](../W-CLEAN-01/p42l-clean-player-20261008T014119.503790Z/result.json)
