# W-CLEAN-01: Clean project: install, author, run, build

Verdict: **PASS**. Current clean project AuthorAll, 11 EditMode+3 PlayMode cases and both rechecks pass. Fresh Linux IL2CPP build and standalone 600-frame autoplay pass real quest/save/restore/ending assertions with zero pump violations. Disk-reserve interruption was resolved by deleting only completed owned compiler caches; no workload bypassed the reserve.

P4.2i product revision: `cb5e2aa20263209df2dea4ee17aa23c50daec0e0`; installed release: `0.1.0-debdab3072dbe1f8`. Reported: 2026-10-07T09:37:56.595149+00:00.
Only this run's evidence determines this disposition. Earlier attempts remain on disk as history, not current PASS.

## Reproduce

```sh
studio/tools/verify-all.sh bake; studio/tools/verify-all.sh unity; python3 artifacts/studio/verification/TOOLS/builds-p42i.py --resume-completed-builds
```

## Current-run evidence

- [W-CLEAN-01/p42i-clean-build-20261007T073040.943841Z/result.json](../W-CLEAN-01/p42i-clean-build-20261007T073040.943841Z/result.json)
- [W-CLEAN-01/p42i-clean-player-20261007T075638.272854Z/result.json](../W-CLEAN-01/p42i-clean-player-20261007T075638.272854Z/result.json)
- [W-CLEAN-01/p42i-clean-player-20261007T075638.272854Z/player.log](../W-CLEAN-01/p42i-clean-player-20261007T075638.272854Z/player.log)
- [UNITY-CLEANPROOF/clean-recheck-editmode-20261007T044422.332841Z/results.xml](../UNITY-CLEANPROOF/clean-recheck-editmode-20261007T044422.332841Z/results.xml)
- [UNITY-CLEANPROOF/editmode-20261007T043529.273476Z/results.xml](../UNITY-CLEANPROOF/editmode-20261007T043529.273476Z/results.xml)
- [UNITY-CLEANPROOF/clean-recheck-playmode-20261007T044507.142970Z/results.xml](../UNITY-CLEANPROOF/clean-recheck-playmode-20261007T044507.142970Z/results.xml)
- [UNITY-CLEANPROOF/playmode-20261007T043623.698282Z/results.xml](../UNITY-CLEANPROOF/playmode-20261007T043623.698282Z/results.xml)
