# P0.2 projects-tooling (GameCore Studio, Wave 0)

Owner: Opus 5.5 agent. Branch: `worktree-agent-af9add08a3462aaf6` (fast-forwarded to `main` `7a9c409` first,
so `docs/studio/` is present). Host copy: `myubuntu:~/wkspace/gc-studio/p0.2-projects-tooling/`.

## What was built

### 1. `games/hollowmere/` (Unity 6000.0.75f1 project skeleton, SADR-016)

Created **on the host** with `Unity -batchmode -nographics -quit -createProject`, configured by a one-shot
`-executeMethod` Editor script (`Assets/Editor/HollowmereSetup.cs`, run twice, then deleted and not committed),
pulled back without `Library/ Temp/ Logs/ UserSettings/` (`games/.gitignore` ignores those for every game).

- `Packages/manifest.json`: the 15 shipping `com.gamecore.*` packages by `file:../../../Packages/<name>`
  (kernel, adapters, runtime, content.compiler, all five existing gameplay packages). **Not** included: the two
  marker packages (`fault-qualification`, `telemetry-qualification`: their presence compiles qualification code
  in) and the `tests/` fixture packages. Engine: `com.unity.burst` 1.8.28, `collections` 2.6.6, `entities` 1.4.6,
  `mathematics` 1.3.2 (kernel pins), `render-pipelines.universal` 17.0.4 (the editor's built-in), `inputsystem`
  1.19.0 (the version the 6000.0.75f1 editor manifest names; 1.11.x was not used), `ai.navigation` 2.0.12,
  `nuget.newtonsoft-json` 3.2.1, `test-framework` 1.6.0, `toolchain.linux-x86_64` 2.0.11 (added by
  `-createProject`, needed for the Linux IL2CPP player lane of SADR-017); modules ai, animation, audio,
  imageconversion, imgui, jsonserialize, physics, screencapture, ui, uielements, unitywebrequest, video.
- `Packages/packages-lock.json`: resolved by Unity on the host, committed.
- ProjectSettings: Linear color space (`m_ActiveColorSpace: 1`), `activeInputHandler: 1` (Input System only),
  audio enabled (`m_DisableAudio: 0`), Standalone scripting backend IL2CPP (`scriptingBackend: Standalone: 1`;
  the Editor is always Mono), Enter Play Mode options = Unity 6 default (`m_EnterPlayModeOptions: 0`, i.e. domain
  and scene reload both on; same as the validation project), fixed timestep 0.02 s, company `GameCore`, product
  `Hollowmere`, `Assets/Hollowmere/Scenes/Boot.unity` (empty scene) as the only build scene.
- URP: `Assets/Settings/Hollowmere_URP.asset` (default pipeline and every quality level),
  `Hollowmere_UniversalRenderer.asset` (its `postProcessData` was null when created from script; set to URP's
  own `PostProcessData` asset, guid `41439944...`, as the URP menu does), plus `UniversalRenderPipelineGlobalSettings.asset`
  and `DefaultVolumeProfile.asset` which URP generated at the Assets root and the setup moved (GUIDs kept).
- `Assets/Hollowmere/Input/Player.inputactions`: map `Player` with Move, Look, Jump, Interact, Pause, Journal,
  Inventory; Keyboard&Mouse and Gamepad schemes; registered as the project-wide actions
  (`EditorBuildSettings` config object `com.unity.input.settings.actions`).

### 2. Checker changes (SADR-014)

- `tools/check_package_metadata.py`: `ENGINE_ALLOWLIST` (Unity.InputSystem, Unity.AI.Navigation,
  Unity.RenderPipelines.Universal.Runtime, Unity.RenderPipelines.Core.Runtime, Unity.TextMeshPro / UnityEngine.UI
  -> com.unity.ugui, Unity.Transforms / Unity.Entities.Hybrid -> com.unity.entities at the qualification pin),
  versions pinned by `games/*/Packages/manifest.json` (falling back to that game's lock for a transitive package
  such as render-pipelines.core); `BUILTIN_MODULES` need no dependency; `precompiledReferences` beyond
  `nunit.framework.dll` only for `com.gamecore.studio.*` / `com.gamecore.gameplay.*` and only
  `Newtonsoft.Json.dll` (-> `com.unity.nuget.newtonsoft-json`); a games manifest pinning a kernel engine package
  differently from the qualification manifest is a problem; lock rule = "locked in at least one of the
  qualification lock and `games/*/Packages/packages-lock.json`, and every lock that holds it agrees",
  `--sync-lock` repairs each lock; kernel packages may also not depend on `com.gamecore.studio.*` or
  `com.gamecore.rules.gameplay`; games' `Assets/` assemblies count as embedding-project assemblies.
  `--self-test`: 12 -> 30 cases. The audit of the existing tree is identical in outcome: the `--json` report's
  `packages`, `assemblies`, `engine_pins` and `problems` were compared with the `HEAD` version of the tool and are
  equal; output gains one line (`allowlisted engine pins from games/*: 6; lock sources: 2`) and the JSON two keys.
- `tools/check_game_core_csharp.py`: TARGETS gain `Packages/com.gamecore.studio.*`,
  `Packages/com.gamecore.gameplay.*` (globs; files an earlier entry covers are not double-counted, plain entries
  behave exactly as before, so the existing count stays 627), `com.gamecore.unity.app`,
  `com.gamecore.rules.gameplay`, `games/*/Assets` (exempt from "UnityEngine outside the Unity project" like
  `unity/`). Engine-free: `com.gamecore.rules.gameplay`, `com.gamecore.studio.core/Runtime/Model`;
  `ENGINE_TYPES` now also matches `UnityEditor` (no existing engine-free file names it). New rule for the Studio,
  gameplay, app and rules.gameplay packages: `UnityEditor` only under an `Editor/` folder, in an Editor-only
  asmdef (`includePlatforms: ["Editor"]`), or inside `#if UNITY_EDITOR`. New `--self-test` (9 cases).
- `tools/make_unity_metas.py`: suffixes `.inputactions .mat .prefab .unity .shadergraph .png .wav .mp3 .ogg .fbx
  .anim .controller` added (`.uxml .uss .asset .shader` were already there); default roots gain `games/*/Assets`.
  A run on this tree creates 0 metas.
- `docs/operator/packages.md`: new section 9 (the rules above); the self-test count in section 6 updated to 30.

P0.3's shape (`com.gamecore.studio.core` Model asmdef with `overrideReferences: true`,
`precompiledReferences: ["Newtonsoft.Json.dll"]`, dependency `com.unity.nuget.newtonsoft-json` 3.2.1) and P0.4's
`com.gamecore.unity.app` are legal under these rules (self-test cases "a Studio package's allowlisted precompiled
DLL resolves to its package", "an allowlisted engine assembly resolves to its games-pinned package").

### 3. Host scripts (`studio/tools/`, bash, `set -euo pipefail`, ASCII, usage in the header)

- `sync-to-host.sh <packet>`: rsync of the worktree to `myubuntu:~/wkspace/gc-studio/<packet>/` (`--delete`;
  excludes `.git .claude .DS_Store .unity-logs Library Temp Logs UserSettings obj bin target __pycache__`;
  excluded paths survive on the host so Library caches stay warm). A packet's **first** sync seeds the host
  directory with a host-local copy of `~/wkspace/game_core` (override/disable with `GC_STUDIO_SEED`), because the
  Mac->host link moved only ~0.1 MB/s for the 228 MB `artifacts/` tree; the rsync that follows makes the copy
  identical to the worktree.
- `unity-compile.sh <packet> <project> [--tests EditMode|PlayMode] [--filter <regex>]`: runs on the host (re-runs
  itself there over ssh when started on the Mac). At most `GC_STUDIO_UNITY_SLOTS` (3) batchmode Editors
  host-wide: flock slots in `~/wkspace/gc-studio/.unity-slots/` plus a count of all running batchmode Editors
  (import workers excluded); waits otherwise. `timeout --kill-after=60 1500` plus a log-silence watchdog
  (`UNITY_SILENCE_TIMEOUT`, 600 s), retry exactly once on a timeout/silence kill, never on a real error. Prints
  error lines, the test summary and failed tests; exit 1 on compile errors, failed tests, missing results or zero
  selected tests. Logs/XML in `~/wkspace/gc-studio/<packet>/.unity-logs/`.
- `dotnet-test.sh <packet> [project] [-- args]`: host `~/.dotnet/dotnet test` (default `dotnet/GameCore.sln`),
  `GAMECORE_OFFLINE` 0/1 exactly as `docs/operator/build-and-run.md` §2.1 (1 adds `-p:NuGetAudit=false` to this
  invocation only; any other value is refused).

## Commands run and results

Mac (all pass on the final tree):

| Command | Result |
| --- | --- |
| `python3 tools/check_package_metadata.py --self-test` | passed (30 cases) |
| `python3 tools/check_package_metadata.py` | 21 packages, 46 assemblies, 4 engine pins, 6 allowlisted pins, 2 lock sources; agree |
| `python3 tools/check_game_core_csharp.py --self-test` | passed (9 cases) |
| `python3 tools/check_game_core_csharp.py` | checked 627 C# files; ok (HEAD version: 627; ok) |
| `python3 tools/validate_game_core_docs.py` | passed (14 documents) |
| `python3 tools/emit_failure_codes.py --check` | table up to date |
| `python3 tools/check_operator_docs.py` | 11 pages, 67 links resolved |
| `python3 tools/make_unity_metas.py` | created 0 meta files |

Host (`myubuntu`, 20 cores; note: load average was 180-290 during the later runs because other packets were
building, which explains the spread in durations):

| Step | Command | Result | Duration |
| --- | --- | --- | --- |
| first sync (seeded) | `studio/tools/sync-to-host.sh p0.2-projects-tooling` | ok | 51 s (later syncs 10-74 s) |
| create project | `Unity -batchmode -nographics -quit -createProject .../games/hollowmere` | exit 0 | 13 s |
| configure | `Unity ... -executeMethod HollowmereSetup.Run` (run 1, run 2) | exit 0, "HollowmereSetup: done" | 91 s, 58 s |
| clean open, Library deleted | `studio/tools/unity-compile.sh p0.2-projects-tooling games/hollowmere` | PASS, exit 0, 0 compile errors; afterwards `rsync -c` shows no content change in any committed file | 159 s |
| validation compile | `studio/tools/unity-compile.sh p0.2-projects-tooling unity/GameCore.Validation` | PASS, exit 0; no committed file changed | 77 s (704 s on a later run under load) |
| EditMode subset | `... unity/GameCore.Validation --tests EditMode --filter 'GameCore\.Composition\.Tests\..*'` | PASS, 189/189 | 348 s |
| dotnet | `studio/tools/dotnet-test.sh p0.2-projects-tooling dotnet/tests/GameCore.Contracts.Tests` | PASS, 163/163 | 19 s |
| dotnet offline | `GAMECORE_OFFLINE=1 ... -- --no-restore` | PASS, `-p:NuGetAudit=false` shown | 1 s |
| slot limiter | two compiles with `GC_STUDIO_UNITY_SLOTS=1` | second waited until the first released its slot | - |
| hollowmere after renderer fix | `studio/tools/unity-compile.sh p0.2-projects-tooling games/hollowmere` | PASS, exit 0, 0 compile errors, no committed file changed (log shows transient "IL Post Processor runner" connection retries under the host load) | 792 s under load |

Observed once: a hollowmere compile (the slot-limiter test) logged "Batchmode quit successfully invoked" and then
hung in shutdown for 10+ minutes. `sudo -n gdb` showed the main thread in `GarbageCollectSharedAssets` <-
`UnloadUnusedAssetsOperation::IntegrateMainThread` <- `PreloadManager::Stop` - the same signature as the known
hang in `docs/operator/editor-hang.md`, here at shutdown. I killed it by hand (that run is recorded as FAIL,
exit 143). This is why `unity-compile.sh` now has the log-silence watchdog, which turns such a hang into a
timeout + one retry instead of a 25-minute wait.

## What remains / notes for the integrator

- When P0.3 (`com.gamecore.studio.core`) and P0.4 (`com.gamecore.unity.app`) merge, add them to
  `games/hollowmere/Packages/manifest.json` (`file:../../../Packages/<name>`), resolve once on the host
  (`studio/tools/unity-compile.sh <packet> games/hollowmere` rewrites the lock) and commit the lock: the lock rule
  requires every package to be locked in at least one project, and neither package exists on this branch.
- PlayMode was not exercised by this packet (nothing to play yet); the EditMode path of `unity-compile.sh` was.
- The interactive display `:1` was not needed; everything here ran `-nographics`.
- `com.unity.inputsystem` is 1.19.0 (what the 6000.0.75f1 editor recommends), not 1.11.x.
- No kernel package and nothing under `unity/GameCore.Validation` was changed; `tools/check_gate_sources.py` was
  only read.
