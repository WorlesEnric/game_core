# W-PLUG-11: Ambience crossfade, voice line, clip release

Verdict: **PASS**. Actual output recording, source-overlap assertions, native data release and waveform identity are combined; non-silence alone was not accepted.

## Final evidence

- [Final Linux IL2CPP recording](../W-GAME-05/r7-lifecycle-final/playthrough.mp4), source `fc3245ea4143220629a1a562081a2d097da39759`, 350.2 s, 1920×1080, H.264/AAC; actual PulseAudio monitor track, Xvfb/llvmpipe. [Driver PASS](../W-GAME-05/r7-lifecycle-final/driver-summary.json), [frame markers](../W-GAME-05/r7-lifecycle-final/save-quit-frames.csv).
- [Combined production crossfade/native release XML](r7-combined/results.xml): **3/3 passed**. The real three-region loop samples both actual AudioSources playing simultaneously with positive gains, then requires outgoing source detachment, `AudioDataLoadState.Unloaded`, incoming native samples Loaded, and ≤5% native residual.
- [Six Memory Profiler snapshot hashes/paths](r7-combined/external-snapshots.json), [measured residuals and timings](r7-combined/snapshots/native-media.csv). Residuals **0.08825%, 0.13234%, 0.15878%** including the reloadable 702-byte clip descriptor. Snapshot capture is not replaced by managed-memory estimates.
- [Post-fix real dialogue XML](r7-voice-fixed/results.xml): **10/10 passed** including all FullQuestHeadless cases. [Explicit native voice selection](r7-voice-final/results.xml): **1/1 passed**, no skips. `R7C_WPLUG11_ActualMarenLinePlaysItsNativeVoiceClip` asserts the real Maren conversation plays `Maren_greet`, native source `isPlaying=true`, volume>0 and Loaded samples.
- [Actual captured clip identity](r7-audio-identity.json), reproduced by `node artifacts/studio/verification/W-PLUG-11/audio-identity.mjs`.
- [Captured-level measurements](r7-audio-final/audio-measurements.json) and inspectable excerpts: [Maren voice](r7-audio-final/maren-voice.wav), [village ambience](r7-audio-final/village-ambience.wav), [crossfade](r7-audio-final/ambience-crossfade.wav), [marsh ambience](r7-audio-final/marsh-ambience.wav).

## Observable audio proof

Normalized correlation of the actual recorded monitor waveform against authored WAV samples (mono 2000 Hz, fixed marker-aligned windows):

| Signal | Correct reference | Mismatched-region control |
|---|---:|---:|
| Full Maren greeting | 0.5905 | Not a regional comparison |
| Village before travel | 0.4642 | Marsh 0.0492 |
| Marsh after travel | 0.4552 | Village 0.0740 |

The reproducible assertion requires >0.2 identity correlation and correct-region correlation greater than the mismatched-region control. Voice starts at approximately **22.5055 s** in the final movie. Mean/peak levels: voice −25.7/−12.0 dBFS; village −27.8/−16.6; transition −27.7/−16.4; marsh −27.7/−15.4. This is measured specific audible content, not a claim of human listening. The actual native-source overlap test separately proves crossfade rather than an abrupt substitution hidden by other audio.

## Fixes and retained failures

1. Finished fades previously stopped but retained `AudioSource.clip`. [Before XML](r7-baseline-tests/results.xml) fails that contract. Sources now detach; ambience unloads unshared native sample payloads and reloads on return. Shared/paused source references retain their lease; the dedicated shared-clip test passes.
2. Scene unload previously retained regional textures. `UnitySceneLoader` now waits for unused-native-asset release; before/intermediate/passing captures are retained under [W-PLUG-01](../W-PLUG-01/README.md).
3. The first movie had ambience but no actual voice: dialogue graph clips were not enrolled in the bank. [Real native regression before](r7-voice-baseline/results.xml) fails with `Voice.clip == null`. All **28 existing authored voice clips** are now enrolled under their canonical `AudioClip.name`; no provider calls were made. Future generated voices need the excluded authoring seam in [R7-C PACKET](../../../../docs/studio/packets/R7-C-plugin-rows.md#requests-to-other-packets).

The initial `r7-audio` measurements are explicitly PARTIAL before-evidence and do not prove voice. The aborted `r7-voice-explicit` Editor launch was cancelled during package registration before Play to avoid contaminating the final recording's monitor track; no XML result is claimed for it. The final explicit test ran after recording ended.

No real-GPU frame-budget claim, installed-service changes or paid operations. Full native snapshots are host-retained with hashes, following the existing external-snapshot convention.
