# GC-001 Linux build and test report

## Result

**Pass for GC-001's Linux IL2CPP qualification scope.** The actual standalone player was built with High managed stripping and run headless in both modes. This is not an Editor-only or compile-only result.

- Positive process: **exit 0**, valid JSON, **7 Pass / 0 Fail** outcomes.
- Negative process: **exit 3**, valid JSON, **2 Pass / 0 Fail / 1 ExpectedNegative** outcomes.
- Generated catalog: **byte-identical on repeated Unity generation after correcting the committed file's missing final LF**.
- Final Unity build: **Succeeded, 0 errors, 2 warnings** as reported by `BuildReport.summary`.
- No tests were deleted, ignored, skipped to obtain green, or given different expected values.

Started from `origin/gc-001` at `94194f3`. Implementation fixes are committed as `3d6881a`, `a01c73b`, and `432ddc9`; Unity-generated project settings and GUID metadata are committed as `15b6813`. The final player contains these source fixes. The later evidence/report commits do not change runtime code.

Execution date: 2026-09-25 local time (UTC logs span 2026-09-24). Machine: `worlesenric`.

## Host and actual toolchain

| Item | Observed value / evidence |
| --- | --- |
| Host OS | Ubuntu **24.04.4 LTS**, Linux **7.0.0-31-generic**, x86_64; `os-release.txt`, `host.txt` |
| CPU | Intel Core i7-12700KF; player JSON |
| Unity | **6000.0.75f1**, revision **26349cd2a5c8**; Linux Editor with Linux IL2CPP playback variations installed |
| External .NET SDK | **8.0.425**, MSBuild **17.11.48**, runtime **8.0.31**, linux-x64; `dotnet-info.txt`. Not used as a replacement for Unity's compiler/runtime |
| Managed compilation | Editor-bundled Roslyn; actual player response file contains **`-langversion:9.0`** and **`NetStandard/ref/2.1.0/netstandard.dll`**; `probe-player-compiler.rsp` |
| Target | **StandaloneLinux64**, x86_64; actual player reports `LinuxPlayer`, `X64`, `is64BitProcess: true` |
| Backend / build | **IL2CPP**, native configuration **Release**, `BuildOptions.None` |
| Stripping | **High**, set and re-read by `BuildProbe`; UnityLinker command recorded in `native-build-commands.json`. Player JSON correctly labels this as a build-time declaration, not a runtime query |
| Actual native compiler | Unity **Clang 9.0.1**, LLVM revision `c1a0a213378a458fbea1a5c77b315c7dce08fd05`, target `x86_64-unknown-linux-gnu`; `unity-native-clang-version.txt` |
| Actual native linker | Unity **LLD 9.0.1**, same LLVM revision; `unity-native-lld-version.txt` |
| Actual Linux SDK/sysroot | UPM-extracted `9.1.0-2.17-v0_608efc24a3b402ec57809211b16a6d32d519f891d4038e1fc8509fe300c395b2-1`, **glibc 2.17**, Linux header `LINUX_VERSION_CODE 199276`; `unity-sdk.txt` |
| Host compilers, not IL2CPP compilers | GCC **13.3.0**, Clang **18.1.3**, GNU ld **2.42**, LLD **18.1.3**; host environment/version files |
| Burst | **1.8.28**, enabled in Editor and player; completed generic job returned **97**. `burst-aot-methods.txt` includes the closed `ProbeAggregateJob<ProbeVector3Value>` compilation entry; the shipped `lib_burst_generated.so` is hashed |

Actual native executable path:

```text
/home/worlesenric/.local/share/unity3d/cache/sysroots/linux-x86/llvm-9.0.1-1/bin/clang++
```

The original build script's `sysroot_glibc` field is empty because it searches inside the Editor playback directories, while this installation extracts the SDK under the user cache. Do not interpret the system compiler versions in `environment.txt` as the compiler used by IL2CPP. `native-build-commands.json` records actual Bee compile/link/UnityLinker/IL2CPP actions and the exact sysroot path.

Unity prints the API enum alias `NET_Standard_2_0` in its settings log. The actual compiler's **2.1.0 reference assemblies** establish the selected .NET Standard profile; the log's enum spelling alone is not used as evidence.

## Package resolution

Unity generated the committed `unity/GameCore.Validation/Packages/packages-lock.json`; it was not synthesized or edited by hand. The lock contains **25 packages**.

| Package | Resolved version | Notes |
| --- | --- | --- |
| Entities | 1.4.6 | Original direct pin retained |
| Collections | 2.6.6 | Original direct pin retained |
| Burst | 1.8.28 | Original direct pin retained |
| Mathematics | 1.3.2 | Original direct pin retained; higher than Burst's 1.2.1 declaration |
| Test Framework | **1.6.0** | Editor-bundled selection, replacing requested 1.4.6; manifest and baseline docs now agree with Unity |
| Performance Testing | 3.0.3 | Original direct pin retained |
| Linux toolchain | 2.0.11 | Original pin resolved successfully; no downgrade |
| Sysroot package / Linux sysroot | 2.0.10 / 2.0.9 | Resolved transitives |
| Serialization | 3.1.5 | Unity-selected override of Entities' 3.1.3 declaration |
| Scriptable Build Pipeline | 2.6.1 | Unity-selected override of Entities' 1.23.1 declaration |
| NUnit extension | 2.0.5 | Built-in dependency of resolved Test Framework |
| Profiling Core / Mono Cecil | 1.0.3 / 1.11.6 | Resolved transitives |

The initial package resolve stalled while downloading the 483,732,751-byte Burst archive through the inherited proxy. Its temporary download advanced only about 0.3 MB over several minutes after reaching about 254.5 MB. Direct and proxied 1 MiB HTTP Range diagnostics both succeeded; the stalled Editor invocation was cancelled and its logs retained. A fresh resolve with Unity registry/download hosts in `NO_PROXY` completed. No registry contents, package bytes, or lock entries were fabricated.

Licensing **succeeded** with the existing Unity Personal entitlement. Logs contain the nonfatal message `[Licensing::Module] Error: Access token is unavailable; failed to update`, followed by successful entitlement/license resolution. This was not an activation failure and required no licensing workaround.

## Commands actually run

The working directory was `/home/worlesenric/wkspace/gc-wt/gc-001`. The exact main argument lists, environment overrides, observed exit codes and attempt order are also archived in [`commands.json`](../toolchain/commands.json).

Initial synchronization:

```bash
git fetch origin && git checkout gc-001 && git reset --hard origin/gc-001
```

Environment used for Unity builds (shown as exports for reproducibility; tools supplied the same values as process environment overrides):

```bash
export DOTNET_ROOT=/home/worlesenric/.dotnet
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH=/home/worlesenric/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export UNITY=/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity
export DOTNET=/home/worlesenric/.dotnet/dotnet
# Added for the second resolve and retained for builds:
export NO_PROXY=localhost,127.0.0.1,::1,packages.unity.com,download.packages.unity.com
export no_proxy="$NO_PROXY"
```

Resolution command, run twice (first with inherited proxy exclusions, then the exclusions above):

```bash
/home/worlesenric/Unity/Hub/Editor/6000.0.75f1/Editor/Unity \
  -batchmode -nographics -quit \
  -projectPath /home/worlesenric/wkspace/gc-wt/gc-001/unity/GameCore.Validation \
  -logFile /home/worlesenric/wkspace/gc-wt/gc-001/artifacts/toolchain/resolve.log
```

First invocation: cancelled stalled download. Second: exit **1**, package resolve succeeded but application compilation exposed the missing references described below. Logs were archived as `resolve-proxy-stalled.log` and `resolve-initial-compile-errors.log`; the duplicate working `resolve.log` was removed.

Build and player commands:

```bash
tools/unity/build_probe.sh
tools/unity/run_probe.sh
```

| Attempt | Actual command | Observed result |
| --- | --- | --- |
| First build, after assembly-reference fixes | `tools/unity/build_probe.sh` | Exit 0; generator ran, High-stripping IL2CPP build succeeded |
| First player run | `tools/unity/run_probe.sh` | Player exits 0/3 and all expected outcomes, but original grep-only runner accepted invalid JSON; this was **not accepted as final evidence** |
| Reproduction after strict-parser runner fix, before rebuilding player | `tools/unity/run_probe.sh` | Exit **1**, both real-player reports rejected as invalid JSON; `run-invalid-json-rejected.log` |
| Second build, after JSON writer fix | `tools/unity/build_probe.sh` | Exit **0**, final `codegen.log` and `build.log` |
| Final player run | `tools/unity/run_probe.sh` | Exit **0**, individual player exits **0/3**, valid JSON and all required outcomes; `run-probe.log` |

Each build script ran these Editor entry points, with absolute project/log paths as recorded in the logs:

```bash
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD/unity/GameCore.Validation" \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile "$PWD/artifacts/toolchain/codegen.log"
"$UNITY" -batchmode -nographics -quit -projectPath "$PWD/unity/GameCore.Validation" \
  -executeMethod GameCore.Validation.Editor.BuildProbe.BuildLinuxIL2CPP \
  -logFile "$PWD/artifacts/toolchain/build.log"
```

The runner invoked the actual player as follows (no `-quit`; the probe calls `Application.Quit`):

```bash
"$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" \
  -batchmode -nographics -logFile "$PWD/artifacts/toolchain/player-positive.log" \
  -probeResult "$PWD/artifacts/toolchain/probe-result.json"
"$PWD/unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64" \
  -batchmode -nographics -logFile "$PWD/artifacts/toolchain/player-negative.log" \
  -probeMissingRegistration -probeResult "$PWD/artifacts/toolchain/probe-negative.json"
```

The corrected runner executes `python3 -m json.tool <result-file>` before its existing result/status checks. An independent Python `json.loads` pass verified final status counts, exitCode fields, IL2CPP/Burst fields and exact generated-file bytes. Before the fix, `json.loads` failed at the first outcome's trailing comma in both reports. This regression was exercised against the real old and rebuilt players, not mocks.

Supporting host commands: `stat` on the supplied Unity/dotnet paths; `/home/worlesenric/.dotnet/dotnet --info`; `uname -a`; `clang --version`; `ld.lld --version`; the actual Unity `clang++ --version` and adjacent `ld.lld --version`. The build script additionally captured `gcc --version`, `ld --version`, `uname -m`, UTC date, file size and SHA-256 hashes. `git diff -- <generated-catalog-path>` diagnosed the original single final-newline mismatch. Python SHA-256 checks compared original/generated bytes and independently verified the embedded prefix hash. Tool reads inspected package/compiler/build records; no alternate .NET build was substituted for Unity.

## Final per-probe and suite results

| Mode / probe | Status | Observed behavior |
| --- | --- | --- |
| Positive / generated-aot-roots | Pass | Closed generic handler key and registered generic job root present |
| Positive / entities-world-entity-query | Pass | Real world; 3 matching entities; sum 31; absent-component query empty |
| Positive / burst-generic-job | Pass | Burst enabled; job completed; observed 97, expected 97 |
| Positive / canonical-bytes-roundtrip | Pass | 25-byte canonical big-endian record; fields and round trip equal; truncated and oversized records rejected |
| Positive / generated-closed-generic-handler | Pass | Generated-key resolution; observed 42, expected 42 |
| Positive / late-mount-linked-inactive-plugin | Pass | Instance count 0 before mount, 1 after first mount, 2 after second; both generated-handler results 36 |
| Positive / generated-catalog-integrity | Pass | One plugin key, one handler key, unique keys, omitted key absent |
| Negative / generated-catalog-integrity | Pass | Catalog unchanged and omitted key absent |
| Negative / missing-registration-detected | Pass | Stable key `0x4771366F6C5F1EA9-0xCDD9BC836D9C05AE` resolves neither factory nor handler; no instance created |
| Negative / missing-registration-report | ExpectedNegative | Explicit missing stable ID; process exit 3; no reflection/fallback |

| Suite / gate | Status | Counts / scope |
| --- | --- | --- |
| TEST-001, GC-001 standalone qualification | **Pass** | 7 positive Pass + 2 negative Pass + 1 ExpectedNegative; 0 Fail; catalog regeneration also passed |
| TEST-020, GC-001 precompiled inactive-plugin/generated-root subset | **Pass** | Late mount and repeat mount passed in the same positive player process; these are shared outcomes, not extra tests |
| Full TEST-020 later-task baking/recipes/descendants/reparent/unload cases | **NotRun** | Not implemented or claimed by GC-001 |
| EditMode NUnit | **NotRun** | 0 executed; no NUnit XML claimed |
| PlayMode NUnit | **NotRun** | 0 executed; qualification uses the standalone probe |
| dotnet test | **NotRun** | GC-001 has no pure .NET test project; external SDK version inspection only |
| Other platforms, including macOS ARM64 | **NotRun / unqualified** | No inference from Linux results |
| Documentation validator | **NotRun** | Known README link failure intentionally left to GC-002, per instruction |

Final GC-001 build/player gates: **none failing or blocked**. This is not full GameCore V1 conformance, broader TEST-020 completion, performance qualification or a leak-lifecycle qualification.

## Fixes and why

1. **Missing Editor assembly reference**: `GameCore.Validation.Editor.asmdef` now directly references `Unity.Burst`. Initial `CS0103` errors showed that importing the namespace without referencing its assembly did not make `BurstCompiler` available.
2. **Missing generated assembly reference**: `GameCore.Validation.Generated.asmdef` now directly references `Unity.Collections`, the resolved assembly containing `Unity.Jobs.RegisterGenericJobTypeAttribute`. This removes the initial `CS0246` errors; no generic registration was removed.
3. **Committed generated-file mismatch**: the original catalog lacked the final LF emitted by the generator. Retained actual generator output, a one-byte correction from **6398 to 6399 bytes**, and committed it. Subsequent generator calls were byte-identical. No keys, roots, expected values or embedded prefix hash changed.
4. **Incorrect package pin relative to the actual Editor resolver**: changed Test Framework manifest pin from **1.4.6 to 1.6.0**, the bundled version Unity actually selected. Updated 04 and the evidence ledger, preserving historical registry research. All other original direct pins remain intact; the authoritative transitive lock is committed.
5. **Invalid structured result JSON**: `ProbeReport.AppendString` now permits omission of the trailing comma; the final `detail` field in each outcome uses that form. The original player ran correctly but produced invalid JSON. `run_probe.sh` now requires Python 3 strict JSON parsing before declaring success. Reproduced rejection against the old actual player, rebuilt IL2CPP, and confirmed both final reports parse and retain all original outcomes.
6. **Reproducible Unity import/build metadata**: committed Unity-generated asset `.meta` files and project settings, including exact Editor revision and build settings. Bootstrap scene/build output remain generated and ignored as designed. No runtime source semantics were altered by these files.

No design gap required changing runtime acceptance semantics. No pure assembly gained a UnityEngine reference. No test assertion or expected numerical value was weakened.

## Remaining diagnostics and limits

- Final build reports **2 warnings**, not a warning-free build. `ProbeArguments.cs:53` emits `CS8604`: its private constructor parameter is declared nonnullable although the parser and nullable property allow an absent result path. The actual required invocations provide a path; this annotation warning was left unchanged rather than suppressing it.
- Unity/Entities also logs `Cannot add extra type Unity.Entities.FastEquality+ManagedGetHashCodeImpl\`1[Unity.Entities.Editor.EntitySelectionProxy]. Skipping.` No project test was skipped; this is an upstream IL2CPP diagnostic. Other Editor startup/shutdown diagnostics (GTK, assembly-reference import message, debugger-agent listen message) remain in the logs. They did not prevent compilation or the measured player outcomes.
- Stripping is proven by build settings/UnityLinker evidence, not by trusting a runtime hard-coded string alone.
- Host/system compiler versions and actual Unity toolchain versions are explicitly separated above.
- No fresh second-machine reproduction was performed. The real first import generated the lock, and subsequent Editor runs reused it successfully on this host.

## Committed evidence

All evidence is under [`artifacts/toolchain/`](../toolchain/README.md). No individual log exceeds 2 MB; none required trimming.

- Final `codegen.log`, `build.log`, `run-probe.log`, `player-positive.log`, `player-negative.log`.
- Final `probe-result.json`, `probe-negative.json`, `qualification-summary.json`.
- Initial compiler errors, stalled resolver/UPM logs, initial build/codegen logs, malformed reports renamed `*.invalid-json.txt`, strict-parser rejection log.
- `catalog-before.json`, `catalog-initial-mismatch.json`, `catalog-determinism.json`.
- Exact invocation/environment ledger `commands.json`; host/.NET/native compiler/SDK files; `native-build-commands.json`; player C# response file; Burst compilation method list.
- `player-files-sha256.json`: hashes for **23 shipped files**, including `GameAssembly.so` and Burst native library. The small launcher hash alone is not treated as gameplay-code identity.

Corrected catalog full-file SHA-256:

```text
a754defcbcd44d8f01a22a5fdd5232c7efa99a3d9729b9a7b5017daf0d57988a
```

Unchanged embedded prefix SHA-256:

```text
33ff04034018b48cdeb7d479c9217cc3ad72945d1b0fe47feb0b7fcc7734768c
```

Executed final `GameAssembly.so` SHA-256:

```text
5c968fc6d1a7a324ea5d403d4227f34b93c824262fabe4db59751ed1d46d681d
```

No throwaway source/test project was added. One-off verification ran in memory; only requested evidence remains. Binaries and Unity caches are not committed. The handoff is marked historical, and the evidence README now points to these actual results.
