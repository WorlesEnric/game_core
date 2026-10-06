# W-PLUG-01: Three-region loop, moved NPC stays, memory baseline, timings

Verdict: **PASS** — R7-C fixes two real native-resource lifetime defects and retains Memory Profiler captures, exact resource populations and assertions.

Source `7da2148a`, Linux Unity 6000.0.75f1, graphics-enabled **batch** Editor on isolated Xvfb/llvmpipe. No real-GPU frame-budget claim.

## Evidence

- [Complete repeat XML](r7-region-repeat/results.xml): **5/5 passed**, including `W_PLUG_01_TravelReleasesRegionNativeMediaWithinFivePercentPeak`, both audio ownership regressions, Animator binding and lifecycle state regression.
- [Complete repeat measurements](r7-region-repeat/snapshots/native-media.csv), [log](r7-region-repeat/r7-region-repeat-20261007T051425-4055723-a1.log), [six full native/managed snapshot hashes and retained host paths](r7-region-repeat/external-snapshots.json).
- [First passing native loop XML](r7-native-final/results.xml): **3/3 passed**; [measurements](r7-native-final/snapshots/native-media.csv), [six snapshot hashes](r7-native-final/external-snapshots.json).
- [Existing real scene/pump loop](../W-PLUG-03/r7-play/results.xml): `TravelsVillageMarshBelfryVillage` **Passed**.

| Unloaded region | Peak native media bytes | Retained bytes | Retained / peak | Repeat load / unload ms | First passing load / unload ms |
|---|---:|---:|---:|---:|---:|
| Thornwick Village | 795497 | 702 | 0.08825% | 39 / 31 | 101 / 79 |
| Blackmere Marsh | 530465 | 702 | 0.13234% | 26 / 26 | 33 / 35 |
| Drowned Belfry | 442121 | 702 | 0.15878% | 25 / 27 | 24 / 26 |

Unchanged assertions: load ≤2000 ms, unload ≤1000 ms, retained native media ≤5% peak. The complete repeat moves the actual idle NPC Odd by 1.5 m through `world.place` and checks his committed pose after every travel and the complete loop. Each departure asserts Unloaded residency, **zero region views**, **zero AudioSource references to the outgoing clip**, and `AudioDataLoadState.Unloaded`; destination ambience follows the new region.

Population: four village-exclusive textures, one marsh-exclusive texture, and each region's actual ambience WAV from the production audio bank. Belfry has no unique texture; its ambience is still measured. Shared textures are not falsely counted as region-exclusive. Native Unity clip metadata remains 702 bytes; the native sample payload is unloaded and that descriptor is included in—not subtracted from—the residual percentage. Bank identity remains available for loading on return.

## Product fixes and before evidence

`UnitySceneLoader` now waits for `Resources.UnloadUnusedAssets` after scene unload. `LoopCrossfader` clears its finished outgoing source reference; region ambience unloads native sample data only if no other AudioSource retains the clip, including paused sources. Incoming unloaded clips reload through the production path.

- [Original texture probe XML](r7-native/results.xml): 100% region-specific texture retention. Its scene-only audio lookup was incomplete; the next probe correctly classifies ambience through the bank.
- [After texture cleanup, before audio-data release](r7-release/results.xml): measured 55.58%, 83.35%, 100% residuals, all due to 442121-byte ambience payloads. [Measurements](r7-release/snapshots/native-media.csv).
- The shared-clip regression proves another source prevents unloading and the production fade path reloads an unshared clip on return. No global stop or destructive `UnloadAsset` is used.

Full snapshots are host-retained, not committed multi-gigabyte blobs, following W-GAME-08's external-snapshot manifest convention. Raw/compressed baseline hashes, intermediate snapshots and passing snapshots remain available at the exact manifest paths.

## Reproduce

`studio/tools/unity-batch.sh --project <abs project> --log-dir <dir> --label native --results <xml> -- -runTests -testPlatform EditMode -testFilter NativeMediaAcceptance -force-glcore`, with `DISPLAY`, `UNITY=<abs Tests/R7_C/batch-graphics.py>` and `GAMECORE_R7_MEDIA_OUTPUT=<abs snapshot directory>`. The test enters/exits Play itself. The wrapper retains allocation/redaction/deadlines; no second packet Editor is launched.
