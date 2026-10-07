# W-GAME-06: Build log + sha256 + V1 gate transcript on the same revision

Verdict: **PASS**. Current-source release Hollowmere Linux IL2CPP build succeeds with retained full-file hashes. The complete unchanged V1 gate passes all phases, including 1318 EditMode and 84 PlayMode XML cases, qualification/release player probes, codegen byte identity and docs gate; no failed probe was repeated or budget relaxed.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42i.py --resume-completed-builds
```

## Current-run evidence

- [W-GAME-06/v1-gate-retry-20261007T075649.868432Z/result.json](../W-GAME-06/v1-gate-retry-20261007T075649.868432Z/result.json)
- [W-GAME-06/v1-gate-retry-20261007T075649.868432Z/command.log](../W-GAME-06/v1-gate-retry-20261007T075649.868432Z/command.log)
- [W-GAME-06/v1-gate-retry-20261007T075649.868432Z/gate/unity/editmode-results.xml](../W-GAME-06/v1-gate-retry-20261007T075649.868432Z/gate/unity/editmode-results.xml)
- [W-GAME-06/v1-gate-retry-20261007T075649.868432Z/gate/unity/playmode-results.xml](../W-GAME-06/v1-gate-retry-20261007T075649.868432Z/gate/unity/playmode-results.xml)
- [W-GAME-01/p42i-player/player/build-report.json](../W-GAME-01/p42i-player/player/build-report.json)
- [W-GAME-01/p42i-player/external-artifacts.json](../W-GAME-01/p42i-player/external-artifacts.json)
