# Toolchain qualification evidence (GC-001)

Status: **NotRun (pending orchestrator build host)**. Nothing in this directory has been produced by an actual
Unity resolve, build, or player execution. The files here describe exactly what the two scripts produce and
which environment fields the qualification report must record.

Normative context: [P-058 and P-060](../../docs/game-core/00-core-protocols.md), [Unity integration section 10](../../docs/game-core/04-unity-integration.md),
[TEST-001](../../docs/game-core/08-validation-and-performance.md).

## Scripts

| Command | Produces |
| --- | --- |
| `UNITY=<editor>/Unity tools/unity/build_probe.sh` | `codegen.log`, `build.log`, `environment.txt`, and the player at `unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64` |
| `tools/unity/run_probe.sh` | `probe-result.json`, `probe-negative.json`, `player-positive.log`, `player-negative.log` |

`UNITY` must point at the Editor executable of the pinned **6000.0.75f1** installation. `BuildProbe` refuses to
build when `Application.unityVersion` differs, so an accidentally installed other Editor cannot produce
qualification evidence. Overridable environment: `UNITY_PROJECT`, `ARTIFACTS`, `PROBE_PLAYER`.

`build_probe.sh` runs two batchmode Editor invocations: first the deterministic catalog generator, then the
player build. `run_probe.sh` launches the built player twice, headless, and compares the process exit code and
the structured JSON result.

## Exit-code contract of the probe player

| Mode | Required result field | Exit code |
| --- | --- | --- |
| positive | `"result": "Pass"` | 0 |
| negative (`-probeMissingRegistration`) | `"result": "ExpectedNegative"` | 3 |
| any failure | `"result": "Fail"` | 1 |

A nonzero-but-wrong code, a missing result file, any `"status": "Fail"` entry, or a missing `"status": "Pass"`
entry makes `run_probe.sh` exit nonzero. `Pass` in a log is never evidence on its own; the JSON artifacts are.

## Environment fields recorded

`environment.txt` is written by the script from the host; the JSON result carries the runtime-observable half.
Both must be archived together, because several fields cannot be read from inside a player.

| Field | Source | Note |
| --- | --- | --- |
| Editor build | `PlayerSettings`/log (`Application.unityVersion` at build time) | Must equal `6000.0.75f1` |
| Host OS / kernel | `uname -a`, `uname -m` | Linux x86_64 on the selected build host |
| Native compiler | `gcc --version`, `clang --version`, `ld --version` | IL2CPP C++ toolchain actually used |
| Sysroot | Linux IL2CPP sysroot path under the Editor's `PlaybackEngines` | Supplied by `com.unity.toolchain.linux-x86_64` |
| Scripting backend | build script re-read of `PlayerSettings.GetScriptingBackend` | Must be `IL2CPP` |
| Managed stripping | build script re-read of `GetManagedStrippingLevel` | Must be `High`; a player cannot query it at runtime, so the result JSON labels it a declared value |
| Architecture | both `environment.txt` (`uname -m`) and `"architecture"` in the JSON | Must agree note: `RuntimeInformation.ProcessArchitecture` in the player |
| API compatibility | build script re-read of `GetApiCompatibilityLevel` | Must be `.NET Standard 2.1` |
| Burst | `"burstCompilerEnabled"` in the JSON (`BurstCompiler.IsEnabled`) | In a build this is only true when Burst AOT compilation ran; a build with Burst silently disabled fails the probe |
| Package pins | `Packages/manifest.json` hash in `environment.txt` plus the committed `packages-lock.json` | The lock is produced by the first resolve and must be committed as-is |
| Catalog | `catalogFileHash` in the JSON and `catalog_sha256` in `environment.txt` | Hash of the generated catalog prefix, with the exact scope recorded beside it |
| Player | `player_sha256`, `player_bytes` in `environment.txt` | Identifies the executed binary |

## Negative control

The negative mode requests a stable key that the generator never emits
(`gamecore.validation.plugin.absent`). A successful run reports `ExpectedNegative` with exit code 3, proving
that an unregistered key is detected as missing rather than satisfied by reflection or a fallback constructor.
Because the two modes use different exit codes, a script-driven run cannot confuse "registration missing works"
with "everything works".

## NotRun

- No Unity resolve, `packages-lock.json`, Editor compile, IL2CPP build, or player execution has happened.
- `unity/GameCore.Validation/Assets/Scenes/GameCoreProbe.unity` does not exist yet: `BuildProbe` generates it
  through the Editor API on the first build so the scene always matches the pinned Editor's serialization.
- `unity/GameCore.Validation/Packages/packages-lock.json` must not be hand-written; it is an output of the first
  resolve on the build host.
