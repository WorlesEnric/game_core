# W-GAME-06: Build log + sha256 + V1 gate transcript on the same revision

Verdict: **PASS**. Current-source release Hollowmere Linux IL2CPP build passes with executable/full-file hashes. Complete unchanged V1 gate passes 1318 EditMode and 84 PlayMode XML cases, codegen byte identity, qualification/release builds, probes and docs gates. Separate B-FRAME failure is not hidden by build success.

P4.2j product revision: `389cf038a7386dbcc5b2b52ad31744d8747e76f4`; installed release: `0.1.0-fba3604e99ceadd1`. Reported: 2026-10-07T16:04:44.851949+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42j.py
```

## Current-run evidence

- [W-GAME-06/v1-gate-retry-20261007T145440.615549Z/result.json](../W-GAME-06/v1-gate-retry-20261007T145440.615549Z/result.json)
- [W-GAME-01/p42j-player/player/build-report.json](../W-GAME-01/p42j-player/player/build-report.json)
