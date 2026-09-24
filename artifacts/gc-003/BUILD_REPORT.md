# GC-003 Linux build report

## Result

**Pass for the requested GC-003 Linux build/test gates.** The Release solution builds with zero errors and zero warnings; all **126 dotnet tests pass**, with zero failures or skips. The StandaloneLinux64 IL2CPP player passes all **eight positive probes**, including `production-contract-catalog`. Negative mode returns **exit 3**, `ExpectedNegative`, with an explicit production-catalog lookup miss.

The real content compiler ran through Unity, repeatedly. Its 22,579-byte output is byte-identical across runs and to the existing committed `ProbeCatalog.g.cs`. There is consequently no generated-source diff to commit; the committed source already is exactly the real compiler output. Reproducibility logs and hashes are committed instead. All three `CommittedProbeCatalogTests` passed again after the final Unity build.

This qualifies the task-specific subset described in HANDOFF §4, not every future runtime behavior in the broader TEST-001/002/003/009/012/013/017/020 specifications or the full W1 integration gate.

## Host and toolchain

- Host: Ubuntu 24.04, Linux `7.0.0-31-generic`, x86_64; Intel Core i7-12700KF, 20 logical CPUs.
- .NET SDK **8.0.425**; runtime **8.0.31**; MSBuild **17.11.48+02bf66295**; VSTest **17.11.1**.
- Pure production assemblies compile as **.NET Standard 2.1**, **C# 9.0**, nullable enabled, warnings as errors. These constraints were not relaxed.
- Unity **6000.0.75f1**, revision **26349cd2a5c8**, licensed Unity Personal.
- Player: **StandaloneLinux64 / IL2CPP / High managed stripping / Burst enabled / .NET Standard 2.1**. Unity's enum spelling logged for that API profile is `NET_Standard_2_0`; this was not changed.
- Resolved core packages: Entities **1.4.6**, Collections **2.6.6**, Burst **1.8.28**, Mathematics **1.3.2**; Linux toolchain **2.0.11**, sysroot **2.0.10**, Linux sysroot **2.0.9**.
- Actual native compiler: Unity sysroot **Clang 9.0.1**, target `x86_64-unknown-linux-gnu`, from `~/.local/share/unity3d/cache/sysroots/linux-x86/llvm-9.0.1-1/bin/clang++`.
- Actual native sysroot: `~/.local/share/unity3d/cache/sysroots/linux-x86/9.1.0-2.17-v0_608efc24a3b402ec57809211b16a6d32d519f891d4038e1fc8509fe300c395b2-1`. The build script's older `sysroot_glibc` discovery field is blank; `toolchain/native-command.txt` records the real compiler command and sysroot instead.
- Host tools, distinct from the actual Unity compiler: GCC **13.3.0**, Clang **18.1.3**, GNU ld **2.42**.

Full inventories: `host-dotnet.log`, `host-kernel.log`, `host-cpu.log`, `toolchain/environment.txt`, `toolchain/native-compiler.log`, and the committed Unity package lock.

## Exact commands and execution sequence

Commands ran in the requested `gc-003` worktree; the main worktree was not used. `commands.json` records top-level verification argv, environment overrides, start timestamps, exit codes and log destinations, including failed attempts. Python subprocess capture redirected stdout/stderr to the named logs without masking exit codes. The following shell syntax expresses the same environment and invocations.

Initial synchronization, as requested:

```sh
git fetch origin && git checkout gc-003 && git reset --hard origin/gc-003
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$HOME/.dotnet:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet --info
```

The starting revision was `7434d99`. After reading HANDOFF, GC-003 in 09, and the named tests in 08:

```sh
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-003/trx-initial
dotnet test dotnet/GameCore.sln -c Release --logger trx --results-directory artifacts/gc-003/trx-second
dotnet run --project dotnet/tools/GameCore.ApiSnapshot -c Release -- \
  --assembly dotnet/src/GameCore.Contracts/bin/Release/netstandard2.1/GameCore.Contracts.dll \
  --output artifacts/gc-003/GameCore.Contracts.production.api.txt
DOTNET="$HOME/.dotnet/dotnet" PYTHON=/usr/bin/python3 bash tools/run_gc003_checks.sh
```

The build command ran five times during repair: four failures, then success. The aggregate script subsequently built successfully again. The first test invocation had 112 passes and 14 compiler-suite failures; the second had 126 passes. The aggregate script first failed because the static scanner included SDK-generated `obj` sources; its second invocation passed all static/doc/build/test gates, with final full-suite TRX in `trx/`.

The successful aggregate script executed these child commands, with `UNITY` unset because Unity was run separately:

```sh
python3 tools/check_game_core_csharp.py
python3 tools/verify_generated_catalog.py
python3 tools/check_contract_surface_parity.py
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
dotnet build dotnet/GameCore.sln -c Release
dotnet test dotnet/GameCore.sln -c Release --logger trx \
  --results-directory /home/worlesenric/wkspace/gc-wt/gc-003/artifacts/gc-003/trx
```

Unity build and player qualification, run twice (before and after the nullable probe-argument correction), both successful:

```sh
UNITY="$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" \
ARTIFACTS="$PWD/artifacts/gc-003/toolchain" \
DOTNET="$HOME/.dotnet/dotnet" bash tools/unity/build_probe.sh
UNITY="$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" \
ARTIFACTS="$PWD/artifacts/gc-003/toolchain" \
DOTNET="$HOME/.dotnet/dotnet" bash tools/unity/run_probe.sh both
```

`build_probe.sh` invoked the pinned editor with `-batchmode -nographics -quit -projectPath "$PWD/unity/GameCore.Validation"`, first with `-executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog -logFile "$PWD/artifacts/gc-003/toolchain/codegen.log"`, then with `-executeMethod GameCore.Validation.Editor.BuildProbe.BuildLinuxIL2CPP -logFile "$PWD/artifacts/gc-003/toolchain/build.log"`. Initial logs were preserved as `codegen-initial.log`, `build-initial.log`, and `environment-initial.txt` before the final build.

`run_probe.sh both` invoked `unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64` with `-batchmode -nographics -logFile <player log> -probeResult <result JSON>` in positive mode, and additionally `-probeMissingRegistration` in negative mode. It observed actual process exits 0 and 3 respectively; JSON was not the sole exit-code evidence.

Between those builds, explicit repeated generation:

```sh
"$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" \
  -batchmode -nographics -quit -projectPath "$PWD/unity/GameCore.Validation" \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile "$PWD/artifacts/gc-003/toolchain/codegen-repeat.log"
```

After the final build and player runs:

```sh
dotnet test dotnet/tests/GameCore.Content.Compiler.Tests/GameCore.Content.Compiler.Tests.csproj \
  -c Release --filter 'FullyQualifiedName~CommittedProbeCatalogTests' \
  --logger trx --results-directory artifacts/gc-003/trx-regenerated
```

Additional toolchain commands recorded in `commands.json`: `dotnet --info`, `uname -a`, `lscpu`, and the actual Unity Clang path with `--version`. In-process Python checks compared complete catalog byte arrays, parsed TRX counters and probe JSON, verified all eight original artifact hashes, and hashed the native player libraries. No hand-mirror generator was used.

## Final suite results

| Suite / gate | Pass | Fail | NotRun / skipped | Blocked | Evidence |
|---|---:|---:|---:|---:|---|
| GameCore.ReferenceSeams.Tests | 21 | 0 | 0 | 0 | `trx/`, `dotnet-test.log` |
| GameCore.ProtocolFixtures.Tests | 10 | 0 | 0 | 0 | `trx/`, `dotnet-test.log` |
| GameCore.Contracts.Tests | 45 | 0 | 0 | 0 | `trx/`, `dotnet-test.log` |
| GameCore.Content.Compiler.Tests | 40 | 0 | 0 | 0 | `trx/`, `dotnet-test.log` |
| GameCore.ProtocolFixtures.Production.Tests | 10 | 0 | 0 | 0 | `trx/`, `dotnet-test.log` |
| **Full solution total** | **126** | **0** | **0** | **0** | `suite-results.json` |
| Post-codegen CommittedProbeCatalogTests rerun | 3 | 0 | 0 | 0 | `trx-regenerated/`, `committed-catalog-tests.log` |
| Seam-based protocol fixture cases | 58 | 0 | 0 | 0 | `../protocol-fixtures/results.json` |
| Production-contract protocol fixture cases | 58 | 0 | 0 | 0 | `../protocol-fixtures/results-production-contracts.json` |
| Documentation validator self-test fixtures | 9 | 0 | 0 | 0 | `validator-self-test.log` |

The 58-case fixture documents are case-level evidence within their respective ten-test suites, not additional NUnit tests. The three post-codegen tests are reruns, not additions to the 126 total. Each individual test's name and outcome is retained in TRX; each protocol case is retained in the result JSON.

Additional passing gates: static C# scan of 63 source files; generated catalog hash/fingerprint validation; source enum parity; documentation validation of 14 documents; real Unity package resolve and native player build. The final Unity build report records `Succeeded`, **0 errors, 1 warning**; this is not a warning-free Unity claim.

### Production API compatibility

`ProductionSurfaceIsAStrictSupersetOfTheFrozenSeamSnapshot` passed against the compiled production assembly and the unchanged frozen snapshot:

- Frozen types: **221**; production types: **233**.
- Removed or changed lines: **0**.
- Added listing lines: **66**, comprising 12 type headers, their members, and four additions to existing types.
- Existing-type additions are exactly `FactoryKind.Handler = 10`, `EnvelopeError.MissingRequiredField = 16`, `EnvelopeError.DuplicateField = 17`, and `EnvelopeReader.TrySeekTo(int)`.
- Added types are the documented catalog/validation/generated-serialization family: `BoundRegistration<TImplementation>`, `CatalogBuildResult`, `CatalogFingerprint`, `GeneratedEnvelopeReader`, `GeneratedFieldBuffer`, `GeneratedFieldSlot`, `GeneratedSerializerBase`, `ISchemaSerializer`, `ImmutableCatalog`, `ManifestValidationReport`, `ManifestValidator`, and `StableNameKeyDerivation`. `ManifestValidationReport` is the result type of the documented validator; `CatalogOrdering` is internal and adds no public type.
- The test now rejects added types or existing-type members outside that explicit list, rather than accepting arbitrary supersets. The engine-free and deterministic listing tests also pass.

Evidence: `compiled-api-compatibility.log`, `GameCore.Contracts.production.api.txt`, and the contract suite TRX. Nothing in `tests/GameCore.ReferenceSeams/**` was modified. The frozen snapshot still has SHA-256 `e4def11460b63ca11094a0afb7a4aed485a79b52be69f84946c8489fc25f4966`.

### Real compiler reproducibility

Both captured generation outputs have:

- Length: **22,579 bytes**.
- Whole-file SHA-256: `2f0e85d0d7c96b0b05404a0b5c7cc1639e625fe44a13e5d83e5f3c2d2406c005`.
- Embedded prefix hash: `f35a69b3380b0f9470f5dcdd3147698ec2ca49304bf00dc6df11d95f938ace23`.
- Catalog fingerprint: `a4ea6f9190c40054b6733e6bcdfe060f478e77f0f3f8a0020c5a341381d353e7`.

The comparison used full byte-array equality, not hashes alone. `catalog-reproducibility.json` points to both real Unity compiler logs. Unity's generator correctly reports `unchanged` because the existing file already equals its generated output. Final generation and the committed-catalog tests agree with those bytes.

### IL2CPP player results

Positive mode: **8 Pass, 0 Fail**, actual process exit **0**, result `Pass`:

1. `generated-aot-roots`
2. `entities-world-entity-query`
3. `burst-generic-job` — observed 97, expected 97.
4. `canonical-bytes-roundtrip`
5. `generated-closed-generic-handler` — observed 42, expected 42.
6. `late-mount-linked-inactive-plugin` — not instantiated before mount; both mounts returned 36.
7. `production-contract-catalog` — fingerprint, keyed lookups, serializer binding, feature support and round trip pass; tampering and truncation reject; unknown schema reports a miss.
8. `generated-catalog-integrity`

Negative mode: **2 Pass, 1 ExpectedNegative, 0 Fail**, actual process exit **3**, result `ExpectedNegative`. The integrity and missing-registration detection assertions pass; the missing-registration report names `FactoryKey(4771366f6c5f1ea9cdd9bc836d9c05ae, 1)` and the production catalog returns `MissingDependency`, with no instance created.

Evidence: `unity-probes-final.log`, `toolchain/probe-result.json`, `toolchain/probe-negative.json`, and both player logs. `toolchain/player-binary-hashes.json` hashes `GameAssembly.so`, `UnityPlayer.so`, the Burst library, launcher and companion native library. Build binaries remain in the ignored build directory rather than being committed.

## Fixes and rationale

| File / area | Defect and minimal correction |
|---|---|
| `dotnet/tools/GameCore.ApiSnapshot/GameCore.ApiSnapshot.csproj` | An XML comment contained `--assembly`, which is illegal inside an XML comment and caused MSB4025. Reworded only the comment. |
| `dotnet/tools/GameCore.ApiSnapshot/ApiSurfaceComparer.cs` | `TypeSurface` assigned and read `Header` but never declared it. Added the internal getter initialized by its existing constructor. |
| `Packages/com.gamecore.contracts/Runtime/Serialization/GeneratedSerializerBase.cs` | Constructor assigned the sorted feature IDs back to its parameter, leaving the readonly field null. Assigned `this.knownFeatureIds`; this fixes both compilation and the serializer feature gate. |
| `Packages/com.gamecore.contracts/Runtime/Validation/ManifestValidator.cs` | Service contract IDs are already `Id128`, not wrappers with `.Value`; recipe schema version is `Recipe.Schema.Version`, not nonexistent `Recipe.SchemaVersion`. Corrected both comparisons without changing the frozen DTOs. |
| `Packages/com.gamecore.content.compiler/Runtime/Json/JsonReader.cs` | JSON object/array parser could not call a private `JsonValue(JsonKind)` constructor. Made it internal, like the other parser-only constructor; no public API addition. |
| `Packages/com.gamecore.content.compiler/Runtime/Description/CatalogDescriptionReader.cs` | Optional/invalid code-section paths passed null to non-null list parameters. Supply empty arrays while retaining the invalid-input diagnostic. |
| Same description reader | Namespace validation used the single-identifier predicate, rejecting valid dotted namespaces and causing all 14 initial compiler-test failures. Reused the existing dotted-identifier validator only for namespace input; other identifier fields remain strict. Existing tests reproduce and verify this defect. |
| `dotnet/tests/GameCore.Contracts.Tests/ContractTests.cs` | Test helper accepted a list but passed it to an array parameter; corrected the helper type. Canonical-order expectation incorrectly supplied raw `Id128` values to a `FactoryKey` formatter; constructed version-1 keys matching the registrations and frozen contract. No identity, ordering or numeric expectation changed. |
| `dotnet/tests/GameCore.Contracts.Tests/ManifestValidationTests.cs` | Cycle test passed null to non-null stage-edge list parameters. Used empty stage arrays; the same cycle rejection remains required. |
| Contract API compatibility test | Original test allowed any additive API drift. Added the explicit documented additions allowlist and logged the full compiled comparison, satisfying the requested stricter compatibility gate. No existing assertion was removed. |
| `tools/check_game_core_csharp.py` | Recursive source discovery included SDK-generated `dotnet/**/obj` files, making the gate fail after its first real build. Excluded dotnet `bin`/`obj` directories, consistent with repository build-output rules; authored and committed generated C# remains checked. |
| `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeArguments.cs` | A private constructor declared non-null `resultPath` even though absence is legal, the parser initializes it to null, and the property is nullable. Corrected the parameter annotation to `string?`; no suppression or runtime behavior change. Rebuilt and reran both player modes afterward. |

No tests were weakened, skipped, deleted, ignored, or assigned different semantic expected values. The canonical-order test correction is a type correction to the existing versioned FactoryKey contract (05 and the frozen production-compatible DTO); it does not change the expected IDs, order or version. No design-gap workaround or protocol/seam change was required.

## Committed artifacts and cleanup

- `681bf33`: Unity-resolved package lock and **61 generated package `.meta` files**.
- `8c3a7ca`: dotnet/compiler/test-harness repairs and explicit API additions gate.
- `da82e4f`: nullable probe argument contract correction.
- `85f8b63`: real build/test/API/reproducibility/player evidence and both protocol-fixture result documents.
- This report and the updated evidence index are committed separately.

Every archived log is below 2 MB; no trimming was required. The performance-test package generated temporary `Assets/Resources/PerformanceTestRunInfo.json` and `PerformanceTestRunSettings.json` during player building; those files and their new metadata were removed after verification because they are transient run inputs, not authored project assets. No throwaway source script or project remains.

The original authoring-host HANDOFF, reviews, host-tools log and artifact-hashes document remain historical evidence. Their `NotRun` labels describe that earlier host and are superseded by this report for Linux execution. All eight hashes in the original artifact manifest still match, including the frozen snapshot and generated catalog.

## Remaining warnings and unqualified scope

- **No requested GC-003 gate remains failing or blocked.**
- Unity's final build summary reports **one warning**. The final log has no `warning CS` diagnostic after the nullable correction, but the summary warning is not individually attributed by the existing build wrapper. Initial full build output also records upstream Entities/Burst `BC1371` messages and an unresolved `Unity.Properties.Internals.asmref` package reference. These logs are retained; no vendor package, pin, warning policy or probe was changed to hide them.
- Headless Editor logs contain GTK/debugger/accelerator messages and an access-token refresh error, followed by successful license entitlement/update and successful editor exits. Licensing did not block the run.
- Unity EditMode/PlayMode test-runner suites: **NotRun**. HANDOFF §6.5 adds no such test assemblies for GC-003; its engine-free tests ran under dotnet, and its Unity acceptance path ran in the actual IL2CPP player. No EditMode/PlayMode pass is claimed.
- Other player targets, full W1 multi-module integration, broad V1 conformance and performance benchmarks: **NotRun / not qualified by this task**. The successful test subsets do not claim those later capabilities.
