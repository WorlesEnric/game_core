# W-CLEAN-01: Clean project: install, author, run, build

Verdict: **PASS**. Current clean-project author/bake, 11 EditMode and three PlayMode cases, both rechecks, Linux IL2CPP build and standalone 600-frame quest/save/restore/ending autoplay pass with zero pump violations.

P4.2k product revision: `7a7ff0c0e5ec2332f360f521ff0467390d063491`; installed release: `0.1.0-3475150b9571123a`. Reported: 2026-10-07T22:04:41.754041+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
python3 artifacts/studio/verification/TOOLS/builds-p42k.py
```

## Current-run evidence

- [W-CLEAN-01/p42k-clean-build-20261007T205517.239438Z/result.json](../W-CLEAN-01/p42k-clean-build-20261007T205517.239438Z/result.json)
- [W-CLEAN-01/p42k-clean-player-20261007T210722.243877Z/result.json](../W-CLEAN-01/p42k-clean-player-20261007T210722.243877Z/result.json)
