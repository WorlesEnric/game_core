# W-GAME-05: Full flow menu→save→load→ending→restart in the player

Verdict: **PASS**.

## Final Linux IL2CPP capture

- [Continuous 350.2-second 1920×1080 H.264/AAC recording](r7-lifecycle-final/playthrough.mp4), 30 fps capture on isolated Xvfb/llvmpipe. Actual standalone player, not an Editor viewport or headless simulation.
- [Driver result](r7-lifecycle-final/driver-summary.json): **PASS**, both distinct player processes exit 0 through production UI Quit; no service changes.
- [Ordered lifecycle assertions](r7-lifecycle-final/saves/lifecycle-report.json), [first-process receipt](r7-lifecycle-final/save-quit-report.json), [second-process receipt](r7-lifecycle-final/load-ending-restart-report.json).
- [Build summary](r7-build-final/build-summary.json), [36-file build manifest](r7-build-final/data-manifest.txt), [build log](r7-build-final/build.log), [recording metadata](r7-lifecycle-final/recording-metadata.json), [capture hashes](r7-lifecycle-final/SHA256SUMS).

Product revision: `fc3245ea4143220629a1a562081a2d097da39759`. StandaloneLinux64, **IL2CPP**, Low stripping, non-development build; 0 errors and 4 warnings. Incremental build took 163 seconds including launcher overhead; Unity build report 95.083 seconds. Existing build-script smoke exits 0 after 2,507 frame-log rows. Executable SHA-256 `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`; data/IL2CPP payload hashes are in the full manifest, not inferred from the small executable launcher hash alone.

## Observed sequence

Approximate positions in the final movie, corroborated by runtime UTC receipts and frame logs:

| Position | Observation |
|---|---|
| 5–13 s | Initial production Menu, empty save slot, fresh state |
| 14 s | New Game/HUD; actual locomotion, voiced dialogue, purchases and quest interactions follow |
| 191 s | Pause in Blackmere with active quest, lantern, clapper and lit shrine |
| 199–205 s | Save UI confirms slot-1 and completed asynchronous save |
| 210–215 s | Actual application Quit, then a distinct fresh Linux player process |
| 216–224 s | Relaunched Menu: fresh world, existing saved slot |
| 228 s | Production Load restores region/pose, quest/stage, lantern, clapper, coin, rumour, gate and shrine |
| 318–331 s | Actual Ending C: The Freed Echo; completed quest, branch/outcome 3, bell and ending facts |
| 334–341 s | Play Again creates a new attachment; fresh starting region/quest/items/facts, old save still exists |

Saved canonical slot hash: `9d2bf9e75eed5c79f1bdc3483e15b9f2b2a05f81e26c9bdc9effd466d80906c4`. The audit compares the restored state against the actual saved state, not against an unrelated fresh world.

The final movie's real game, save UI, dialogue and ending were visually inspected. The [first full recording](r7-lifecycle/playthrough.mp4), revision `7da2148a`, also passed all nine stages; its menu/save/ending/restart frames were individually inspected at 23/226/352/370 seconds. It is retained as before-evidence for the subsequent voice-bank fix, not promoted as voice playback evidence. The final movie's actual voice and ambience identities are measured under [W-PLUG-11](../W-PLUG-11/README.md).

Some scene materials render magenta on llvmpipe; their origin was not determined. This row qualifies the lifecycle, not rendering quality, real-GPU B-FRAME or W-GAME-01. Build-generated URP serialization changes are retained as a [diff](r7-build-final/generated-source-changes.diff) and restored to source HEAD after the build; they are not unrelated product edits.

## Driver boundary and reproduction

The driver uses production UI dispatch and collision-aware autoplay controls; its audit only reads committed state. No direct authoritative slot writes, teleport shortcuts, fake menus, paid calls or installed-service changes. Isolated save files are retained with the movie.

1. `bash studio/tools/build_game_player.sh r7-c` (batch Editor through the host allocator).
2. Start an isolated 1920×1080 X display.
3. `python3 games/hollowmere/Assets/Hollowmere/Tests/R7_C/PlayerFlow/run_lifecycle.py --output <fresh absolute directory> --display :97 --pulse-source alsa_output.pci-0000_00_1f.3.iec958-stereo.monitor --record`.

The recorder stays alive across the process boundary and captures actual monitor audio. No `record_playthrough.sh` service-stop behavior is invoked. The wrapper refuses existing output, missing stages, failed UI Quit, non-Linux/non-IL2CPP receipts, revision mismatch or missing audio/video tracks.
