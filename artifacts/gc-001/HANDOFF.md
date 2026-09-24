# GC-001 handoff — Qualify the exact Unity and IL2CPP toolchain (Wave 0)

Status: **NotRun (pending orchestrator build host)**. No Unity resolve, Editor compile, IL2CPP build, or player
execution happened for this task. The machine that produced this branch has no Unity, no .NET SDK, and no mono,
so nothing here is a claim that a build or test passed.

## 1. Summary

GC-001 delivers the exact-toolchain qualification project, the build/run scripts, and the recorded baseline
replacement from macOS ARM64/Xcode to **Linux x86_64 + StandaloneLinux64 + IL2CPP + High stripping**.

What was built:

1. `unity/GameCore.Validation/` — a Unity project pinned to Editor **6000.0.75f1** with the exact 04 package pins
   plus `com.unity.toolchain.linux-x86_64` `2.0.11`, five probe assemblies with acyclic `.asmdef` references, and
   a `.gitignore`.
2. A **deterministic Editor catalog generator** producing
   `Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs`: one keyed plugin factory registration (stable 128-bit
   key), one closed generic handler instantiation rooted for AOT, and one generic job registration
   (`[assembly: RegisterGenericJobType(typeof(ProbeAggregateJob<ProbeVector3Value>))]`). The committed file is
   byte-reproducible from the same key literals and carries a verifiable prefix hash.
3. A **linked-but-inactive fixture plugin** in its own assembly, referenced only by the generated catalog, never
   instantiated at startup, mounted late by generated key.
4. A **runtime probe** (entry `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]`) that creates an Entities world,
   creates entities and runs an `EntityQuery`, schedules and completes a `[BurstCompile]` generic job, round-trips
   a record through canonical big-endian bytes with length-bound rejection, late-mounts the fixture plugin through
   the generated catalog, executes the closed generic handler, writes a structured JSON result, and exits through
   `Application.Quit(code)` — 0 only when every probe passes.
5. A **negative mode** (`-probeMissingRegistration`) that resolves a key the generator never emits and must report
   `status: "ExpectedNegative"` with exit code **3**, distinct from the positive success code **0** and the failure
   code **1**.
6. An **Editor build script** (`BuildProbe.BuildLinuxIL2CPP`) that sets IL2CPP, High stripping, .NET Standard 2.1
   and the Linux64 target from `PlayerSettings`, refuses to build when the Editor revision or Burst enablement
   disagrees, builds `Builds/Linux64/GameCoreProbe.x86_64`, and exits nonzero on any failure.
7. **Scripts** `tools/unity/build_probe.sh` (codegen then build, logs under `artifacts/toolchain/`) and
   `tools/unity/run_probe.sh` (both player modes, result JSON, exit nonzero on mismatch).
8. **Baseline documentation** updated in `04-unity-integration.md`, `10-decisions-and-open-questions.md`, and one
   added paragraph in `references/unity-evidence.md`.

## 2. Files created or changed

Created:

| Path | Purpose |
| --- | --- |
| `unity/GameCore.Validation/ProjectSettings/ProjectVersion.txt` | Pins the Editor to 6000.0.75f1 |
| `unity/GameCore.Validation/Packages/manifest.json` | Exact 04 pins + `com.unity.toolchain.linux-x86_64` 2.0.11 + module entries |
| `unity/GameCore.Validation/.gitignore` | `Library/ Temp/ Logs/ Builds/ UserSettings/ obj/` and IDE output |
| `unity/GameCore.Validation/Assets/link.xml` | Preserves the fixture plugin assembly under High stripping |
| `.../Abstractions/GameCore.Validation.Probe.asmdef` | Assembly `GameCore.Validation.Probe` |
| `.../Abstractions/ProbeKey.cs` | 128-bit key with documented derivation and canonical big-endian byte order |
| `.../Abstractions/ProbeKeys.cs` | Stable key literals + `DerivationHolds()` used by the generator |
| `.../Abstractions/ProbeTypes.cs` | `ProbeVector3Value`, `ProbeAmount`, `IProbeHandler<,>`, `ProbeScalarHandler<T>`, `ProbeAotRoots` |
| `.../Abstractions/ProbePluginContracts.cs` | `IProbePlugin`, `IProbePluginFactory`, registration records |
| `.../Abstractions/ProbeComponents.cs` | `ProbeCounter`, `ProbeAbsent`, `[BurstCompile] ProbeAggregateJob<TPayload>` |
| `.../Abstractions/ProbeCanonicalCodec.cs` | 25-byte canonical big-endian record codec with length validation |
| `.../Plugin/GameCore.Validation.Fixture.asmdef` | Assembly `GameCore.Validation.Fixture` |
| `.../Plugin/FixturePlugin.cs` | `FixturePlugin` + `FixturePluginFactory` (linked, inactive) |
| `.../Generated/GameCore.Validation.Generated.asmdef` | Assembly `GameCore.Validation.Generated` |
| `.../Generated/ProbeCatalog.g.cs` | Committed generated catalog (keyed registrations, closed generic roots, hash) |
| `.../Runtime/GameCore.Validation.ProbeHost.asmdef` | Assembly `GameCore.Validation.ProbeHost` |
| `.../Runtime/ProbeRunner.cs` | All probe steps, `Application.Quit` exit codes, JSON write |
| `.../Runtime/ProbeReport.cs` | Structured result model and deterministic JSON writer |
| `.../Runtime/ProbeEnvironment.cs` | Unity/backend/stripping/arch/Burst/catalog-hash fields |
| `.../Runtime/ProbeArguments.cs` | `-probeResult` / `-probeMissingRegistration` parsing |
| `.../Editor/GameCore.Validation.Editor.asmdef` | Editor-only assembly `GameCore.Validation.Editor` |
| `.../Editor/ProbeCatalogGenerator.cs` | Deterministic generation, `-executeMethod` entry, key verification |
| `.../Editor/BuildProbe.cs` | `BuildLinuxIL2CPP()`, PlayerSettings enforcement, scene generation |
| `tools/unity/build_probe.sh` | Codegen + IL2CPP build, logs, host environment capture |
| `tools/unity/run_probe.sh` | Runs both player modes and validates exit code + result field |
| `artifacts/toolchain/README.md` | Evidence description, exit-code contract, environment fields |
| `artifacts/gc-001/HANDOFF.md` | This file |

Changed (baseline only):

| Path | Change |
| --- | --- |
| `docs/game-core/04-unity-integration.md` | Toolchain table rows (Editor, host, target, new "Unqualified targets" row), host/Xcode paragraph, manifest fragment, test-layer ARM64 wording |
| `docs/game-core/10-decisions-and-open-questions.md` | P0 toolchain row, P0 shipping-platform row, source-register entry, researched-sources sentence |
| `docs/game-core/references/unity-evidence.md` | One added "Baseline replacement (GC-001)" paragraph; research text untouched |

`traceability.json` was **not** changed: it contains no platform statement (`grep` for macOS/ARM64/Xcode/Linux/
Apple/platform returns no matches), and no task, wave, dependency, requirement or test mapping changed.
`tools/validate_game_core_docs.py` was not modified.

## 3. Exact commands for the Linux build host

Environment: `UNITY` must be the Editor executable of the pinned revision
(`~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`). The host needs the **Linux Build Support (IL2CPP) Hub module**
because the UPM toolchain package alone does not ship the `LinuxStandaloneSupport` playback engine, and batchmode
needs an activated Editor license.

```bash
# 1. resolve packages, generate the catalog, build the IL2CPP player
UNITY=~/Unity/Hub/Editor/6000.0.75f1/Editor/Unity tools/unity/build_probe.sh

# 2. run the player headless in both modes and validate the results
tools/unity/run_probe.sh

# or one mode only
tools/unity/run_probe.sh positive
tools/unity/run_probe.sh negative
```

The two Editor invocations the script performs, for reference:

```bash
# codegen (deterministic; reruns must leave the tree unchanged)
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.ProbeCatalogGenerator.GenerateCatalog \
  -logFile artifacts/toolchain/codegen.log

# player build (fails with a nonzero exit code on any build error)
"$UNITY" -batchmode -nographics -quit -projectPath unity/GameCore.Validation \
  -executeMethod GameCore.Validation.Editor.BuildProbe.BuildLinuxIL2CPP \
  -logFile artifacts/toolchain/build.log
```

Direct player invocations (what `run_probe.sh` does):

```bash
unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 \
  -batchmode -nographics -logFile artifacts/toolchain/player-positive.log \
  -probeResult artifacts/toolchain/probe-result.json            # expect exit 0, "result": "Pass"

unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64 \
  -batchmode -nographics -logFile artifacts/toolchain/player-negative.log \
  -probeMissingRegistration -probeResult artifacts/toolchain/probe-negative.json   # expect exit 3
```

Determinism check of the generated catalog (no Unity needed; the committed file is the expected output):

```bash
# after the codegen step, this must print nothing
git diff --exit-code -- unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs
```

Independent check of the catalog hash recorded inside the generated file:

```bash
python3 - <<'PY'
import hashlib, pathlib
p = pathlib.Path("unity/GameCore.Validation/Assets/GameCore.Validation/Generated/ProbeCatalog.g.cs")
text = p.read_text(encoding="utf-8")
decl = "        public const string CatalogFileHash = "
digest = hashlib.sha256(text[:text.index(decl)].encode("utf-8")).hexdigest()
print("ok" if digest == text.split('CatalogFileHash = "')[1].split('"')[0] else "MISMATCH")
PY
```

Documentation validation (executes no Unity or gameplay code):

```bash
python3 tools/validate_game_core_docs.py --self-test
python3 tools/validate_game_core_docs.py
```

There is no `dotnet/` project for this task: GC-001's evidence is a built player, and `dotnet/` belongs to the
pure-assembly tasks. The scripts are `bash` with `set -euo pipefail` and take the Editor path from `UNITY`.

## 4. Requirement and test coverage mapping

| Requirement / suite | Where it is implemented | Observable acceptance |
| --- | --- | --- |
| P-009 (manifest: stable identities, generated registration keys, no reflection) | `ProbeKeys`, generated `ProbeCatalog`, `TryGetPluginFactory`/`TryGetHandler` | Catalog holds exactly the generated keys; a key absent from the catalog resolves to `false` + `null` and is never constructed by reflection (negative mode) |
| P-054 (serialization: schema/version, canonical byte order, length bounds, no CLR identity) | `ProbeCanonicalCodec`, `canonical-bytes-roundtrip` probe step | 25-byte big-endian layout asserted byte by byte, exact-sized round trip equal, truncated and oversize lengths rejected |
| P-058 (V1 profile: IL2CPP + managed stripping acceptance with generated registration/serializer/generic roots) | `BuildProbe`, generated catalog, JSON `scriptingBackend`/`isIl2Cpp`/`managedStrippingLevel`/`burstCompilerEnabled` | Player is IL2CPP with High stripping, Burst enabled observed in the player; the closed generic job/handler are reachable only through generated code |
| P-060 (evidence: exact versions, resolved dependencies, native compiler, architecture, backend/stripping settings, logs, hashes) | `run_probe.sh` + `build_probe.sh` environment capture, `ProbeEnvironment`, `ProbeReport` | `environment.txt` records Editor, host, native compiler/linker, sysroot, manifest hash, player hash; the JSON records Unity version, backend, stripping (with its source), architecture, Burst, catalog hash and scope |
| TEST-001 | Whole probe project + both scripts | Exact project created; registration, closed generic handler, entity query, Burst job and serialization round trip all execute in the player; linked inactive plugin mounted by ID after startup; omitted registration fails explicitly; source generation reproducible |
| TEST-020 (applicable subset: precompiled plugins and generated roots) | Fixture plugin assembly, generated catalog, `late-mount-linked-inactive-plugin` step | The plugin is present in the player (link.xml + direct factory reference), reports zero instances before the mount, is created through the generated key, executes the generated closed generic handler, and is mounted a second time without rebuilding the player |
| TEST-020 (not covered here, owned by later tasks) | — | Editor baking, runtime recipes, descendant spawn after publication, reparent, provider unload, unknown-plugin and incompatible-recipe rejection |

Baseline replacement requirement: 04 section 1 and the 10 P0 rows record StandaloneLinux64 x86_64 IL2CPP, High
stripping, headless execution on a Linux x86_64 host, and mark macOS ARM64 — including the previously documented
macOS 15.5/Xcode 16.4 profile — as unqualified.

## 5. Known gaps, assumptions, and doc ambiguities

### NotRun / open until the build host runs

1. **Everything build- and runtime-related**: no resolve, no `packages-lock.json`, no Editor compile, no IL2CPP
   build, no player run. `artifacts/toolchain/`, `artifacts/gc-001/`, and this file may not claim otherwise.
2. **`packages-lock.json` is absent by design.** It is produced by the first resolve on the build host and must be
   committed as-is. Do not synthesize it from `manifest.json`.
3. **`.meta` files are absent.** Unity creates them on first open/import. They should be committed after the first
   resolve, together with the lock, so GUID references stay stable. Only
   `Assets/Scenes/GameCoreProbe.unity` (+ its `.meta`) is gitignored.
4. **`ProjectSettings` YAML is deliberately not hand-written.** `ProjectVersion.txt` is the only committed
   ProjectSettings file; backend, stripping, API compatibility, product name and target come from
   `BuildProbe` via `PlayerSettings` APIs, as the task requested.
5. **Bootstrap scene is generated at build time** (`BuildProbe.EnsureBootstrapScene`, via
   `EditorSceneManager`) so it always matches the pinned Editor's scene serialization. It is gitignored; the probe
   needs no scene content because it is entered from `RuntimeInitializeOnLoadMethod`.

### Uncertainty to resolve on the first resolve/build

6. **Toolchain package version**: `com.unity.toolchain.linux-x86_64` `2.0.11` is the version Unity documents as
   "released for Unity Editor 6000.0" (2.0.10 is also listed). If the first resolve reports that 2.0.11 is not
   available for this Editor build, pin 2.0.10 and record the change as a baseline revision.
7. **Module entries**: `manifest.json` lists the modules the probe and Entities need directly. UPM adds the rest of
   the transitive module graph during resolve; the resolved lock is the authoritative record.
8. **Hub module vs UPM package**: building a Linux IL2CPP player needs the `Linux Build Support (IL2CPP)` module
   installed through Unity Hub in addition to the UPM toolchain package. `BuildProbe` fails with an explicit
   message when `BuildPipeline.IsBuildTargetSupported(Standalone, StandaloneLinux64)` is false.
9. **Editor license/activation** is required for `-batchmode`; a licensing failure appears in
   `artifacts/toolchain/codegen.log`, not as a build error in our code.
10. **`BurstCompiler.IsEnabled` inside a build** is only true when Burst AOT compilation actually ran for the
    platform; the probe fails the Burst step when it is false, and `BuildProbe` refuses to build when
    `BurstCompiler.Options.EnableBurstCompilation` is false. Burst AOT settings are on by default for the target,
    so no hand-written `BurstAotSettings_StandaloneLinux64.json` is required; if the build log shows Burst AOT
    disabled, that file (or the Editor UI) must set `EnableBurstCompilation: true` and the reason recorded.
11. **Managed stripping level cannot be read from inside a player.** The JSON therefore reports it as a declared
    value and names its source (`BuildProbe` sets and re-reads it before building). If the orchestrator wants
    runtime proof, it must come from the Editor log, not the player.
12. **Exit codes 0/1/3** are a project convention; the documents do not prescribe codes. `run_probe.sh` encodes the
    same contract, so a mismatch fails the script.

### Deliberate interpretation decisions

13. **"Dynamic mount without rebuilding"** is implemented as *mounting a plugin whose assembly is already compiled
    into the player*, reached only through the generated key. No managed assembly loading, no runtime compilation,
    no reflection-based construction (04 section 8).
14. **Stable keys** are declared as literal `ProbeKey`s in `ProbeKeys.cs` and documented as
    `SHA-256(UTF-8 stable name)` with the first 16 digest bytes read as two big-endian 64-bit words. The generator
    verifies the literals against that derivation before emitting, so the literals cannot silently drift.
15. **Catalog hash scope** is the exact file prefix preceding the `CatalogFileHash` declaration (including the
    newline that ends the previous line). The scope text is embedded in the generated file so the value can be
    reproduced without reading the generator.
16. **`Assets/link.xml` preserves only the fixture assembly.** The generated catalog and probe runtime survive by
    reachability; the fixture is preserved explicitly because "linked but inactive" must not depend on the current
    reachability graph. This is preservation, not generic-specialization creation (04 section 8).
17. **The probe's JSON writer is hand-written** (no `JsonUtility`) so field order is fixed and diffable and no
    serialization package behavior enters the evidence path.

### Adjacent facts the orchestrator should know

18. **Pre-existing documentation-validator failure, not introduced here**:
    `docs/game-core/README.md:37` links to `../Game_Core_Implementation_Design_v1.md`, which does not exist in this
    repository. `python3 tools/validate_game_core_docs.py` fails with exactly that one error on the pristine base
    revision (verified by running the validator in a detached worktree of `HEAD`) and still fails with exactly that
    one error after my changes — my edits add no new error. Fixing it means editing a file this task does not own
    (README) or restoring a historical document; the W0 documentation-check gate stays red until its owner decides.
19. **`references/unity-evidence.md` was edited** even though my brief named 04/10: its section 1 still described
    macOS/Xcode as the chosen qualification profile, which would have contradicted the updated 04. The edit is one
    added paragraph that records the replacement and marks macOS unqualified; the research text is untouched.
20. **macOS remains unqualified.** Any macOS ARM64 claim needs its own build script and player run; the hub-module
    and playbook in 04 section 10 apply unchanged, only the native toolchain differs.

### Follow-ups for later waves

21. `GC-002` and the W1 tasks can now depend on: the Linux IL2CPP profile, the generated-catalog pattern (direct
    keyed registrations + explicit closed generic roots), the stable-key derivation, and the exit-code/JSON
    evidence convention. None of them should reintroduce a private registration mechanism.
22. When the first real catalog covers many plugins, this generator must be generalized, not duplicated: keep one
    generator producing keyed registrations plus closed generic roots, with the same reproducibility guarantee.
