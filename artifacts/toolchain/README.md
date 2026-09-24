# Toolchain qualification evidence (GC-001)

Status: **Pass for the GC-001 Linux qualification scope**. Unity 6000.0.75f1 resolved the committed lock,
built the High-stripping IL2CPP player, and ran both headless modes. Positive: seven Pass outcomes and exit 0.
Negative: two Pass outcomes, one ExpectedNegative outcome and exit 3. See [BUILD_REPORT](../gc-001/BUILD_REPORT.md)
for exact commands, initial failures, fixes, toolchain versions and limits.

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
the structured JSON result. Python 3 rejects malformed JSON before the result/status checks.

## Exit-code contract of the probe player

| Mode | Required result field | Exit code |
| --- | --- | --- |
| positive | `"result": "Pass"` | 0 |
| negative (`-probeMissingRegistration`) | `"result": "ExpectedNegative"` | 3 |
| any failure | `"result": "Fail"` | 1 |

A nonzero-but-wrong code, a missing or malformed JSON result file, any `"status": "Fail"` entry, or a missing
`"status": "Pass"` entry makes `run_probe.sh` exit nonzero. `Pass` in a log is never evidence on its own.

## Environment fields recorded

`environment.txt` is written by the script from the host; the JSON result carries the runtime-observable half.
Both must be archived together, because several fields cannot be read from inside a player.

| Field | Source | Note |
| --- | --- | --- |
| Editor build | `PlayerSettings`/log (`Application.unityVersion` at build time) | Must equal `6000.0.75f1` |
| Host OS / kernel | `uname -a`, `uname -m` | Linux x86_64 on the selected build host |
| Native compiler | `native-build-commands.json`, `unity-native-clang-version.txt`, `unity-native-lld-version.txt` | Actual Unity compiler/linker are 9.0.1; system compiler versions in `environment.txt` are not the IL2CPP compiler |
| Sysroot | `unity-sdk.txt`, `native-build-commands.json` | Actual UPM-extracted cache path; glibc 2.17. The build script's legacy `sysroot_glibc` lookup is empty on this installation |
| Scripting backend | build script re-read of `PlayerSettings.GetScriptingBackend` | Must be `IL2CPP` |
| Managed stripping | build script re-read of `GetManagedStrippingLevel` | Must be `High`; a player cannot query it at runtime, so the result JSON labels it a declared value |
| Architecture | both `environment.txt` (`uname -m`) and `"architecture"` in the JSON | Must agree note: `RuntimeInformation.ProcessArchitecture` in the player |
| API compatibility | build script re-read of `GetApiCompatibilityLevel` | Must be `.NET Standard 2.1` |
| Burst | `"burstCompilerEnabled"` in the JSON (`BurstCompiler.IsEnabled`) | In a build this is only true when Burst AOT compilation ran; a build with Burst silently disabled fails the probe |
| Package pins | `Packages/manifest.json` hash in `environment.txt` plus the committed `packages-lock.json` | The lock is produced by the first resolve and must be committed as-is |
| Catalog | `catalogFileHash` in the JSON and `catalog_sha256` in `environment.txt` | Hash of the generated catalog prefix, with the exact scope recorded beside it |
| Player | `player_sha256`, `player_bytes` in `environment.txt`, plus `player-files-sha256.json` | Launcher and complete shipped player file hashes; the launcher alone does not identify gameplay code |

## Negative control

The negative mode requests a stable key that the generator never emits
(`gamecore.validation.plugin.absent`). A successful run reports `ExpectedNegative` with exit code 3, proving
that an unregistered key is detected as missing rather than satisfied by reflection or a fallback constructor.
Because the two modes use different exit codes, a script-driven run cannot confuse "registration missing works"
with "everything works".

## Scope and additional evidence

- `codegen.log`, `build.log`, `run-probe.log`, both player logs and result JSON are the final successful run.
- `catalog-initial-mismatch.json` records the original missing final LF; `catalog-determinism.json` proves
  exact regeneration after committing the generator's output.
- `probe-player-compiler.rsp` records C# 9 and .NET Standard 2.1 reference assemblies.
- `burst-aot-methods.txt` includes the closed `ProbeAggregateJob<ProbeVector3Value>` compilation entry.
- Initial compile errors, malformed reports and their strict-parser rejection are retained separately;
  they are not final results. Logs are below 2 MB each.
- Bootstrap scene and build binaries remain ignored; Unity-generated asset metadata, project settings and
  the authoritative package lock are committed.
- No EditMode/PlayMode NUnit suite was run. This task's assertions execute in the standalone player.
  TEST-020 coverage is limited to linked inactive plugins/generated roots; later-task scenarios remain NotRun.
  Other target platforms remain unqualified.
