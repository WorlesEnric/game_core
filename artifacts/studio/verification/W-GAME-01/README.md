# W-GAME-01: 10-minute graphical playthrough recording with frame log

Verdict: **BLOCKED**. P4.2h current-source release Linux IL2CPP, real RTX4060Ti on :1 at 1080p: four actual >600-second recordings. VSync OFF literal B-FRAME PASS twice: p95 3.461/3.552 ms, zero frames >100 ms outside transition windows. VSync ON FAIL twice: p95 17.375/17.314 ms; outside-transition >100 ms counts 2/0. All full routes, saves and recordings pass; transition hitches <=122.590 ms. Owner VSync definition remains open; no blended or relaxed verdict. Failed recorder setup attempts remain retained.

Product baseline: `a77cb38ba4a2265007fa40c38983e01a17bb0914`. Historical receipts retain their original revision.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/P42hPlayer/player.py run --output "$PWD/.evidence/P42hPlayer-qualified"
```

## Retained evidence

- [UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md](../UNITY-HOLLOWMERE/playmode-final-harness-20261005T204457.641118Z/README.md)
- [W-GAME-01/p42h-player/result.json](../W-GAME-01/p42h-player/result.json)
- [W-GAME-01/p42h-player/visual-review.json](../W-GAME-01/p42h-player/visual-review.json)
- [W-GAME-01/p42h-player/external-movies.json](../W-GAME-01/p42h-player/external-movies.json)
- [W-GAME-01/p42h-player/player/build-report.json](../W-GAME-01/p42h-player/player/build-report.json)
