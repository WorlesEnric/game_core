# GC-020 Linux build and test report

## Host and configuration

- Linux x86-64, kernel `7.0.0-31-generic`, Intel i7-12700KF; .NET SDK `8.0.425`; Unity Editor `6000.0.75f1`; GCC `13.3.0`, Clang `18.1.3`.
- C# 9 and .NET Standard 2.1 assembly contracts retained. Qualification and marker-free players: StandaloneLinux64, IL2CPP, High managed stripping. The project's `AudioManager.asset` has `m_DisableAudio: 1`; audio output was tested with `RecordingAudioSink`, not a device (crash-139).
- Started with `git fetch origin && git checkout gc-020 && git reset --hard origin/gc-020` **only in this worktree**. No Unity or player invocation was run without `timeout` (the Unity scripts use 1800 seconds and the probe helper 600 seconds).

## Final verified results

| Work | Status | Passed / failed / skipped or not run | Evidence |
|---|---|---:|---|
| Entire `dotnet/GameCore.sln` Release build | Pass | 27 projects, 0 warnings, 0 errors | build command output; final test TRX files below |
| Entire .NET test solution | Pass | 1,013 / 0 / 0, 13 test assemblies | `trx/*.trx` |
| Unity package resolution | Pass | import/compilation completed, no CS errors | `unity/resolve.log`, committed `packages-lock.json` |
| All Unity EditMode testables | Pass | 928 / 0 / 0 | `unity/editmode-results.xml`, `unity/editmode-copyback.log` |
| All Unity PlayMode testables | Pass | 26 / 0 / 0 (GC-020: 16 / 0 / 0) | `unity/playmode-results.xml`, `unity/playmode-copyback.log` |
| Qualification IL2CPP build | Pass | 1 / 0 / 0 | `toolchain/build.log`, `toolchain/environment.txt` |
| Qualification player probes | Pass | 70 / 0 / 0 process runs: 14 modes × 5 | `toolchain/probe-*.json` and `.run2`–`.run5`; corresponding `player-*.log` |
| Marker-free IL2CPP build and surface scan | Pass | 1 / 0 / 0; 78 assemblies, 371 generated source files scanned, 0 latch findings | `release/build.log`, `release-player-surface.json` |
| Marker-free family probes | Pass | 30 / 0 / 0 process runs: traversal, world, cards, narrative, GC-018, GC-019 × 5 | `release/probe-*.json` and repetitions |
| Fault-free source/assembly surface | Pass | Release and Qualification compilations checked | `release-surface.json` |
| Host C# checker, metadata, documentation validator | Pass | 494 C# files; 0 new metas; 9 validator fixtures and 14 docs | commands below |

Every retained probe JSON and all four repetitions reported `Pass`, no failed observation; each player process exited with the expected code. The GC-020 traversal probe has 14 named observations plus its digest observation. Its final evidence records the configured 20 ms fixed step, 50 admitted steps and identical 3000 milli-unit velocity at 30/60/144 Hz, `1.00 → 1.04 → 1.02` m/s around reparent, five local physics simulations for five admitted steps, duplicate-step refusal, a stamped physical observation before the downstream sensor reads it, and `offenders=<none>` for the cards/narrative action-phase audit. The audio observation records two committed crossings played through a recording sink once each; disabled sink records zero plays. Pure integer replay matches exactly, while a one-unit perturbed observation mismatches at tolerance zero and matches at the declared tolerance. `toolchain/probe-traversal.json` records the actual counts and details.

All fourteen qualification modes and all six marker-free modes were rerun 5/5 against their respective final rebuilt binaries after the physical observation change. The retained JSON/repetition evidence is from those final-binary sweeps.

## Commands actually run

Environment for dotnet commands: `DOTNET_ROOT=$HOME/.dotnet PATH=$HOME/.dotnet:$PATH DOTNET_CLI_TELEMETRY_OPTOUT=1`. From repository root:

```sh
$HOME/.dotnet/dotnet build dotnet/GameCore.sln -c Release
$HOME/.dotnet/dotnet test dotnet/GameCore.sln -c Release --no-build --logger trx --results-directory artifacts/gc-020/trx

UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -quit -projectPath "$PWD/unity/GameCore.Validation" -logFile "$PWD/artifacts/gc-020/unity/resolve.log"
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform EditMode -testResults "$PWD/artifacts/gc-020/unity/editmode-results.xml" -logFile "$PWD/artifacts/gc-020/unity/editmode-copyback.log"
timeout --signal=TERM --kill-after=60 1800 "$UNITY" -batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform PlayMode -testResults "$PWD/artifacts/gc-020/unity/playmode-results.xml" -logFile "$PWD/artifacts/gc-020/unity/playmode-copyback.log"
UNITY="$UNITY" UNITY_PROJECT="$PWD/unity/GameCore.Validation" ARTIFACTS="$PWD/artifacts/gc-020/toolchain" tools/unity/build_probe.sh
PROBE_RUNS=5 UNITY_PROJECT="$PWD/unity/GameCore.Validation" ARTIFACTS="$PWD/artifacts/gc-020/toolchain" PROBE_PLAYER="$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" tools/unity/run_traversal_probe.sh
```

The same `PROBE_RUNS=5`, `UNITY_PROJECT`, `ARTIFACTS`, and `PROBE_PLAYER` environment ran each of `run_narrative_probe.sh`, `run_cards_probe.sh`, `run_world_probe.sh`, `run_w1_gate_probe.sh`, `run_w2_gate_probe.sh`, `run_w3_gate_probe.sh`, `run_w4_profile_probe.sh`, `run_gc013_probe.sh`, `run_w4_gate_probe.sh`, `run_gc017_faults_probe.sh`, `run_gc018_probe.sh`, `run_gc019_probe.sh`, and `run_w5_gate_probe.sh` from `tools/unity/`.

```sh
python3 tools/unity/prepare_gc017_release_project.py
UNITY="$UNITY" UNITY_PROJECT="$PWD/unity/GameCore.ReleaseCheck" ARTIFACTS="$PWD/artifacts/gc-020/release" tools/unity/build_probe.sh
python3 tools/check_release_fault_free.py --dotnet "$HOME/.dotnet/dotnet" --json artifacts/gc-020/release-surface.json
python3 tools/check_player_fault_free.py --player unity/GameCore.ReleaseCheck/Builds/Linux64 --json artifacts/gc-020/release-player-surface.json
# PROBE_RUNS=5 with release UNITY_PROJECT/ARTIFACTS/PROBE_PLAYER:
# run_traversal_probe.sh, run_world_probe.sh, run_narrative_probe.sh,
# run_cards_probe.sh, run_gc018_probe.sh, run_gc019_probe.sh
python3 tools/check_game_core_csharp.py
python3 tools/make_unity_metas.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

`build_probe.sh` wraps each Unity invocation with `timeout --signal=TERM --kill-after=60 1800`; `probe_runs.sh` wraps each player invocation with `timeout --signal=TERM --kill-after=10 600`. The release clone is disposable and ignored; it was regenerated after the final source change. API snapshots and all three checked generated catalogs had no worktree diff.

## Defects found and repairs

1. **Editor-only physics scene creation:** The first full EditMode run was 930 passed / 14 failed. `SceneManager.CreateScene(..., LocalPhysicsMode.Physics3D)` rejected EditMode; the actual local-physics fixture must run in PlayMode. Made the GC-020 test assembly platform-neutral, without ignoring or deleting a test. Final EditMode 928/928 and PlayMode 26/26.
2. **Seeded targets lacked gameplay storage:** `LiveTargetSeeder.TrySeed` installs generic target rows; unlike `AssemblyPublisher.Spawn`, it does not invoke a recipe applier. The traversal family now resolves the selected precompiled recipe and installs its base layout for seeded course/runners/volumes. This repaired the input refusal and zero-integration cascade; future spawned targets still use the publisher's existing applier path.
3. **Fixture ancestry and state assertions:** Only already-live descendants are required bound at initial creation. The future target now spawns under the live valley-runner scope (the published rule matches recipe **and exact scope**); the reparent assertion checks scope ancestry, not equality of a target's direct scope with its parent's destination. The existing retained-progress check is now part of its pass condition. The mode fixture restores the moved subtree under the valley before asserting that tailwind is regained. No numeric expectation was changed.
4. **Committed audio cursor:** The real committed-event store advances the cursor, so a second read returns zero new events and zero suppressed duplicates. The fixture now asserts zero replayed cues and zero suppressions; this is the store's observable delivery contract, not a weakened dedup test. Existing .NET tests separately exercise at-least-once redelivery with a repeating reader.
5. **External physical authority missing copy-back:** The initial gate separately called physics after gameplay, which did not produce an ECS observation before downstream sensing. Added `TraversalPhysicsObservation` with sampled logical step/epoch, position and velocity; bound the physics gate for external mode and simulate at the traversal sensing boundary after input/integration and before the sensor reads positions. It refuses missing/stale observations. The external-mode observation now asserts copied values and stamp against the backend and that ECS pose was not integrated. The kernel gained no traversal reference. Contact data is **not** represented by this new observation; the current reference has no contact consumer, so a general contact observation remains a design gap for any future collider-driven gameplay.
6. **Marker-free release cloning:** Its exact text substitutions predated `-probeTraversal`, so preparation failed on the new argument list. Updated the clone script to preserve the traversal mode while removing only the two fault-qualified modes. The rebuilt marker-free player passed surface scanning and traversal/family probes.

No test was skipped, ignored, deleted, or changed to alter documented numeric expectations. One first PlayMode invocation stopped logging before dispatch for ten minutes; attempted `gdb -p 3649562 -batch -ex 'thread apply all bt'` first, but the host denied ptrace. The watchdog/cancel terminated that attempt, and the retry produced actionable fixture failures. A later focused run also timed out at 600 seconds with no dispatch progress; its retry ran and exposed the remaining failures. Both stalls are unresolved intermittent Editor pre-dispatch hangs, not test passes.

## Scope of claims and remaining gaps

- GC-020 has one fixture catalog, not a generated traversal catalog (`GeneratedCatalogPresent=false`); the 14 traversal observations ran in Editor PlayMode and both IL2CPP players, not over two traversal catalogs. Generated traversal catalog coverage remains GC-025.
- This run did not implement or verify the full TEST-018 twenty-session Play Mode reload matrix or TEST-021 cross-template combination; those protocols require additional distinct fixtures beyond the GC-020 gate. The acceptance demonstrated here is the specified action reference, the no-action-phase card/narrative observation, and the existing family probes.
- Physical intent submission before simulation and copied step/epoch/pose/velocity before sensing were observed. The optional rigidbody mode is a dedicated qualification scene, not a production rigidbody game. No native physics bitwise repeatability is claimed; engine observation comparison uses a declared tolerance.
- The audio device was deliberately disabled. The committed-output logic was verified through its recording sink, not speakers.
- The running-world target binder remains gate-owned rather than a generic kernel lifecycle hook, as noted in `HANDOFF.md`; no kernel traversal dependency was introduced.
