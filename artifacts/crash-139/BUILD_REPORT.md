# crash-139 Linux IL2CPP build and investigation

## Result

**Reproduced and isolated.** A player reporting `Pass` then dying with SIGSEGV at address `0x20` was executing Unity's **FMOD/PulseAudio mixer thread**, not an IL2CPP-managed world, GameCore job, PlayerLoop callback, or message lane. This validation player does not use audio. Disabling the Unity audio device in `unity/GameCore.Validation/ProjectSettings/AudioManager.asset` (`m_DisableAudio: 1`) eliminates the offending background mixer in this project. Post-change, **2,400/2,400** four-way concurrent probe launches exited with their expected codes and JSON verdicts, zero crashes; this is evidence for the fix, not proof of a general Unity engine patch. Do not propagate this profile setting to an audio-enabled product without diagnosing its audio backend.

Source before fix: `b697ff6357580df8b9ff00fabb95bddee431bdf5`. Fix commit: `67f6a9e`. No HANDOFF exists for this investigation, as specified. `docs/game-core/09-implementation-guide.md` GC-001/GC-013 and `08-validation-and-performance.md` TEST-001/TEST-020 were read; this report claims the run scenarios, not complete later-wave TEST-020 conformance.

## Host and build

Linux x86_64, kernel `7.0.0-31-generic` (Ubuntu 24.04); Unity `6000.0.75f1` with Linux IL2CPP module; .NET SDK `8.0.425`; host GCC `13.3.0`, host clang `18.1.3`, binutils `2.42`, GDB `15.0.50`. Unity bundled Linux clang `9.0.1`; Entities `1.4.6`, Burst `1.8.28`, Collections `2.6.6`. IL2CPP `StandaloneLinux64`, High managed stripping, Release compiler configuration, Burst enabled; pure source constraints remain C# 9/.NET Standard 2.1 with no UnityEngine references in pure assemblies. Package lock SHA-256 `9a243cfbdb35d1d901102cd68219448f8189ba9d9dc8c2ba86f32ada22dc7597`, generated catalog SHA-256 `2f0e85d0d7c96b0b05404a0b5c7cc1639e625fe44a13e5d83e5f3c2d2406c005`. Rebuilt player `GameAssembly.so` SHA-256 `549e747b6d93f6d91afd6db4abad6db7d775d309a0a3bea65757f6b2eed5c8d4`.

Commands, run from this worktree (with `DOTNET_ROOT=$HOME/.dotnet`, `$HOME/.dotnet` prepended to `PATH`, and `DOTNET_CLI_TELEMETRY_OPTOUT=1`):

```sh
git fetch origin && git checkout crash-139 && git reset --hard origin/crash-139
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity ARTIFACTS=$PWD/artifacts/crash-139/build tools/unity/build_probe.sh
python3 tools/unity/stress_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/baseline --runs 125 --workers 4
python3 tools/unity/stress_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/serial --runs 300 --workers 1 --modes world
python3 tools/unity/stress_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/world-parallel --runs 400 --workers 4 --modes world
python3 tools/unity/stress_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/mapped --runs 300 --workers 4 --modes world
python3 tools/unity/stress_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/mapped2 --runs 200 --workers 4 --modes world
python3 tools/unity/capture_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/gdb-corrected --mode world --runs 300
# Same debugger script also ran 300 each in parallel: world, narrative, positive.
addr2line -f -C -e unity/GameCore.Validation/Builds/Linux64/GameCoreProbe_BackUpThisFolder_ButDontShipItWithYourGame/UnityPlayer_s.debug 0x1a55601 0x1a4fbac 0x192408b 0x832824 0x7c5799 0x858d65 0x8218b4
# Set m_DisableAudio: 1 in AudioManager.asset, then:
UNITY=$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity ARTIFACTS=$PWD/artifacts/crash-139/audio-disabled-build tools/unity/build_probe.sh
python3 tools/unity/stress_crash_139.py --player unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 --output artifacts/crash-139/audio-disabled-stress --runs 300 --workers 4
ARTIFACTS=$PWD/artifacts/crash-139/canonical PROBE_RUNS=5 tools/unity/run_probe.sh both
ARTIFACTS=$PWD/artifacts/crash-139/world PROBE_RUNS=5 tools/unity/run_world_probe.sh
ARTIFACTS=$PWD/artifacts/crash-139/narrative PROBE_RUNS=5 tools/unity/run_narrative_probe.sh
$HOME/.dotnet/dotnet test dotnet/GameCore.sln --verbosity quiet
$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform EditMode -testResults "$PWD/artifacts/crash-139/editmode.xml" -logFile "$PWD/artifacts/crash-139/editmode.log"
$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity -batchmode -nographics -projectPath "$PWD/unity/GameCore.Validation" -runTests -testPlatform PlayMode -testResults "$PWD/artifacts/crash-139/playmode.xml" -logFile "$PWD/artifacts/crash-139/playmode.log"
```

Builds before and after setting change **Succeeded** (baseline 0 errors, 10 warnings). The originally attempted `dotnet test --no-restore` could not find `obj/project.assets.json` (NETSDK1004); re-running with restore passed. An initial Unity test invocation combining `-quit -runTests` exited successfully without producing test XML; rerunning without `-quit` produced actual suite results. Neither false-success invocation is counted as a test pass.

## Pre-fix reproduction and native evidence

| Batch | Modes | Runs | Clean | SIGSEGV after Pass JSON |
| --- | --- | ---: | ---: | ---: |
| `baseline/summary.json`, four concurrent | all eight, 125 each | 1,000 | 997 | 3, all world |
| `serial/summary.json`, one at a time | world | 300 | 300 | 0 |
| `world-parallel/summary.json`, four concurrent | world | 400 | 396 | 4 |
| `mapped/summary.json`, four concurrent | world | 300 | 297 | 3 |
| `mapped2/summary.json`, four concurrent | world | 200 | 198 | 2 |

Total pre-fix ordinary player runs: **2,200**, 2,188 clean and **12 SIGSEGV**. Every recorded failure still wrote a `Pass` JSON; process exit was signal `-11` (shell exit 139). Serial world runs were crash-free but concurrent launches increased frequency. GDB runs did not reproduce with its timing (300 world serial and three parallel sets of 300 world/narrative/positive; 1,200 corrected debugger runs without recorded SIGSEGV). GDB initially stopped on unrelated Unity `SIGPWR`/`SIGXCPU` internal signals; corrected capture ignores those signals. Core pattern is `|/usr/share/apport/apport ...`; `ulimit -c unlimited` was available, but no new apport crash artifact was generated in `/var/crash`. No kernel core was claimed. Instead, `stress_crash_139.py` samples `/proc/<pid>/maps` and retains Unity's native signal stack and mappings for failures.

`mapped2/failures/world-0173/player.log` contains signal `SIGSEGV`, code 1, address `0x20`, and native PC `0x739551655601`. `mapped2/failures/world-0173/maps.txt` maps that PC to `UnityPlayer.so` at file offset `0x1a55601` (`r-xp` base `0x739550300000` with file offset `0x700000`). Symbolication against the build's matching `UnityPlayer_s.debug`:

```text
AudioOutputHookManager::FlushAddRemoveQueue()
DSPGraphModule::DefaultOutputBeginMix(int)
AudioManager::systemCallback(FMOD_SYSTEM*, FMOD_SYSTEM_CALLBACKTYPE, void*, void*)
FMOD::DSPSoundCard::read(...)
FMOD::Output::mix(...)
FMOD::OutputPulseAudio::mixThreadCallback(void*)
FMOD::Thread::callback(void*)
```

`mapped2/failures/world-0200/player.log` has the same offset chain and null-near fault. The crashing thread is the audio mixer, not `GameAssembly.so`; both pre-existing GameCore quit-path patches therefore cannot address this specific fault. Audio was enabled in the validation project's `AudioManager.asset` despite no audio test or fixture. This is a concrete validation-player configuration defect: it leaves an unnecessary FMOD background thread active through process shutdown. Disabling the engine audio output in this audio-free probe is the smallest owning-profile change; no tests or expected values changed.

## Post-fix results

| Run | Pass | Fail | NotRun | Blocked |
| --- | ---: | ---: | ---: | ---: |
| IL2CPP positive | 300/300 | 0 | 0 | 0 |
| IL2CPP omitted-registration negative (expected exit 3) | 300/300 | 0 | 0 | 0 |
| IL2CPP world dispatch | 300/300 | 0 | 0 | 0 |
| IL2CPP W1 gate | 300/300 | 0 | 0 | 0 |
| IL2CPP W2 gate | 300/300 | 0 | 0 | 0 |
| IL2CPP narrative | 300/300 | 0 | 0 | 0 |
| IL2CPP cards | 300/300 | 0 | 0 | 0 |
| IL2CPP W3 gate | 300/300 | 0 | 0 | 0 |
| Existing canonical positive + negative scripts | 10/10 | 0 | 0 | 0 |
| Existing world script | 5/5 | 0 | 0 | 0 |
| Existing narrative script | 5/5 | 0 | 0 | 0 |
| Unity EditMode | 602/602 | 0 | 0 | 0 |
| Unity PlayMode | 6/6 | 0 | 0 | 0 |
| .NET solution, 11 test assemblies | 655/655 | 0 | 0 | 0 |

Each stress pass requires the expected process return code, parseable JSON with the expected result, at least one passing probe and zero failed probe steps. `audio-disabled-stress/summary.json` records counts; `editmode.xml` and `playmode.xml` record Unity's real counts. Test assemblies' .NET counts: Cards 26, ProtocolFixtures 10, Execution 58, ReferenceSeams 21, ProtocolFixtures.Production 10, Narrative 118, Composition 106, Planning 129, Contracts 45, Content.Compiler 44, Derivation 88. No test was weakened, skipped or deleted. Product platforms other than Linux IL2CPP remain **NotRun**; audio-enabled product scenarios remain **NotRun**, as this player deliberately contains no audio workload.

## Files and evidence

`tools/unity/stress_crash_139.py` retains only failed run logs, results and sampled memory maps, plus aggregate JSON, so success does not create thousands of redundant logs. `tools/unity/capture_crash_139.py` retains a native GDB stack if it catches a signal. The pre-fix failed logs/maps, batch summaries, successful post-fix summary, canonical probe JSON, Unity XML, and build logs are under this directory. No >2 MB file is committed. Existing `packages-lock.json` and generated catalog did not change during either build and remain committed on the branch.
