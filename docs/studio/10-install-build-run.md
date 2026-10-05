# GameCore Studio install, build and run

This runbook covers the shipped host tooling through P2.4. Commands below are operator procedures, not executions or fresh verification by P4.3-draft. The recorded packet evidence is linked in the [draft status report](12-completion-status-draft.md); player and clean-project acceptance remain pending. ([Plan waves 3–4](06-implementation-plan.md), [verification rules](07-verification-matrix.md))

## Profile and prerequisites

| Component | Pin / requirement | Source |
|---|---|---|
| Build host | Linux x86_64, `myubuntu`; interactive X display `:1`, RTX 4060 Ti, validated Unity entitlement. | [Assessment F11](01-gap-assessment.md#0-findings-that-shape-everything-below), [P2.1 evidence](packets/P2.1-studio-ui.md#evidence-artifactsstudioevidencep21readmemd) |
| Unity | 6000.0.75f1 with Linux IL2CPP support; default executable `$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity`. | [unity-compile header](../../studio/tools/unity-compile.sh), [operator profile](../operator/profile.md) |
| Dotnet | .NET 8; host-tested SDK 8.0.425, default `$HOME/.dotnet/dotnet`. | [P0.2](packets/P0.2-projects-tooling.md), [dotnet-test header](../../studio/tools/dotnet-test.sh) |
| ETOS | Patched commit `278ef9cf421f5e64e83a402960def0c51e3833c3`, Rust 1.97.1, Docker, C compiler. The committed lock records binary digests and layer version. | [etos.lock](../../studio/etos/etos.lock), [host-build-etos.sh](../../studio/tools/host-build-etos.sh), [P0.1](packets/P0.1-host-etos.md) |
| Host utilities | git, Python 3, GNU timeout; host-wide Unity runners use flock. | [operator prerequisites](../operator/build-and-run.md#1-prerequisites), [unity-batch.sh](../../studio/tools/unity-batch.sh) |
| Providers | Echo image/chat and DashScope TTS/realtime. Credentials stay on the host. 3D is blocked; prices are not configured. | [P0.1 host table and §4](packets/P0.1-host-etos.md) |

The **qualified V1 profile** is Linux x86_64 IL2CPP Release, High stripping, Burst, headless with audio disabled. **SADR-017's Studio target** is Linux IL2CPP graphical, URP and audio on, with player evidence still pending P3.1/P4.2. Editor tests and screenshots do not qualify a stripped player. macOS Mono is an unqualified convenience target; Windows is out of scope. ([Operator profile](../operator/profile.md), [SADR-016/017](02-architecture.md#8-decision-records-sadr--studio-adr-numbering-continues-after-the-kernels-adr-017), [matrix §4](07-verification-matrix.md#4-supported-platformtoolchain-matrix-to-be-confirmed-by-evidence))

## Bring up the host node

The deployed node root is **`~/.local/share/etos-studio`**, not the earlier design's `~/.local/share/etos`, which belongs to another installation. The existing host user owns the user systemd service; no separate service user is required by this deployment. ([P0.1 §1 Host](packets/P0.1-host-etos.md#host-myubuntu-user-worlesenric), [install header](../../studio/etos/install.sh))

| Component | Actual location / endpoint | Source |
|---|---|---|
| Source clone | `~/wkspace/etos-studio`, from `~/wkspace/gc-studio/etos-hub.git`; unrelated `~/wkspace/etos` is untouched. | [host-sync-etos.sh](../../studio/tools/host-sync-etos.sh) |
| Host binaries | `~/.local/opt/etos/bin/{etosd,etos,etos-musl}`; static musl CLI also becomes `<root>/bin/etos`. | [host-build-etos.sh](../../studio/tools/host-build-etos.sh), [install.sh](../../studio/etos/install.sh) |
| Node config | `<root>/etos.toml`, `models.toml`, `ops.toml` rendered from repository templates. `ETOS_STUDIO_ROOT` overrides the root. | [install.sh](../../studio/etos/install.sh) |
| Service | `~/.config/systemd/user/etosd.service`, existing user, linger enabled. | [P0.1 §1 Host](packets/P0.1-host-etos.md#host-myubuntu-user-worlesenric) |
| API / broker / web UI | `127.0.0.1:7410`; broker listen and authority `172.17.0.1:7411`; web UI `127.0.0.1:7400`. | Same source |
| Provider credentials | `~/.config/gamecore-studio/providers.env`, mode 0600; generated from named exports by host-providers-env.sh, never printed or committed. | [host-providers-env.sh header](../../studio/tools/host-providers-env.sh) |
| Unity app credential | `~/.config/gamecore-studio/app-key.json`, mode 0600; only its path belongs in ignored UserSettings. | [P0.1 Host](packets/P0.1-host-etos.md#host-myubuntu-user-worlesenric), [P2.2 decision 2](packets/P2.2-studio-etos-client.md#decisions-and-deviations) |
| Images / workers | `localhost/etos-default:latest`, `localhost/gc-designer:current`, `localhost/gc-mechanic:current`; workers are offline containers. Their installed manifest selects `echo/gpt-6-sol`; default alias remains configured separately. | [P0.1 Host](packets/P0.1-host-etos.md#host-myubuntu-user-worlesenric), [agent.toml](../../studio/etos/agent/agent.toml) |

The source transfer scripts are Mac-side git orchestration; builds run on Linux. After the pinned source is present, the host bring-up sequence is `host-build-etos.sh` then `install.sh`. Install generates provider environment/config, builds images unless skipped, builds/upgrades the companion, installs workers/app and pairs the app. A second unchanged run reports “nothing changed”. Building etos records the source HEAD in the lock; verify the checkout revision before using that build to update a pin. ([P0.1 §5](packets/P0.1-host-etos.md#5-operating-the-node), [host-build-etos.sh](../../studio/tools/host-build-etos.sh), [install.sh](../../studio/etos/install.sh))

```sh
studio/tools/host-sync-etos.sh
studio/tools/host-sync-studio.sh push
```

On the Linux host, use the build/install/verify sequence recorded by P0.1. ([P0.1 §5](packets/P0.1-host-etos.md#5-operating-the-node))

```sh
studio/tools/host-build-etos.sh
studio/etos/install.sh
studio/etos/verify.sh
```

`install.sh [--skip-images]` is the actual install syntax. `host-build-images.sh` can build the images separately. `host-sync-studio.sh pull` commits/fetches the lock and environment evidence back to the Mac branch. These are mutating provisioning operations; a documentation audit only reads their headers. ([Install header](../../studio/etos/install.sh), [image build header](../../studio/tools/host-build-images.sh), [sync header](../../studio/tools/host-sync-studio.sh))

`verify.sh` checks service/node status, creates a throwaway verification agent, performs real image/describe/TTS/realtime calls, checks the expected 3D refusal, removes that agent and runs the companion's real-node tests through the paired app. It spends provider usage and writes redacted evidence under `artifacts/studio/environment/etos-verify-<UTC date>/`; exit 0 requires every check and both designer requests ending candidate. The historical passing run is [etos-verify-2026-10-04](../../artifacts/studio/environment/etos-verify-2026-10-04/README.md). ([verify.sh header and result checks](../../studio/etos/verify.sh), [P0.1 §2](packets/P0.1-host-etos.md#2-verified-how))

### Companion and app pairing

The companion uses the vendored SDK and builds with `cargo build --release --manifest-path studio/agent/Cargo.toml`. The installer creates `studio/etos/agent/bin/gamecore-studio` pointing to `../../../agent/target/release/gamecore-studio`, then installs the manifest with `--link`. The process command must be `bin/gamecore-studio`; etos rejects `..` in a manifest command. Re-vendor with `studio/agent/vendor-etos-sdk.sh <etos-checkout>` when the pin changes, and commit the matching vendor tree and ETOS_PIN. ([P0.5 §1, §3 and §5](packets/P0.5-companion.md))

For explicit installation/pairing after the binary link exists, the documented commands are below. Set ETOS_ROOT to the Studio node so these do not address the unrelated installation. Do not display the output key file. ([P0.1 §5](packets/P0.1-host-etos.md#5-operating-the-node), [P0.5 §5](packets/P0.5-companion.md#5-expectations-for-p01-install))

```sh
export ETOS_ROOT="$HOME/.local/share/etos-studio"
export PATH="$HOME/.local/opt/etos/bin:$PATH"
etos agent install --link studio/etos/agent
etos app install studio/etos/app
etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json
```

Unity reaches `/api/v1/agents/gamecore-studio/http` through the node, using an app key. The agent key remains with the companion. In **Project Settings → GameCore Studio → ETOS**, set **App key file**, **Node URL** as needed, **Save and restart session**, then **Test connection**. `GAMECORE_ETOS_KEY_FILE` overrides the file path. Settings live in `UserSettings/GameCoreStudio.json`; the session does not auto-start in batchmode unless `GAMECORE_ETOS_AUTOSTART=1`. ([ETOS §1–§2](04-etos-integration.md), [P2.2 decisions 2 and 10](packets/P2.2-studio-etos-client.md#decisions-and-deviations), [settings controls](../../Packages/com.gamecore.studio.etos/Editor/EtosSettingsProvider.cs))

## Unity project setup

Hollowmere's manifest and lock are the current package inventory. Local packages use `file:../../../Packages/<name>`. Keep Studio Model/Authoring/UI/Views/ETOS assemblies Editor-only. Runtime gameplay packages use mirror metadata in gameplay.contracts. Resolve changes with Unity on the host and commit the lock; every lock containing a local package must match its dependency map. The checker requires a lock in at least one qualifying project, while 05's two-game lock target still awaits cleanproof. ([Hollowmere manifest](../../games/hollowmere/Packages/manifest.json), [lock](../../games/hollowmere/Packages/packages-lock.json), [package rules §9](../operator/packages.md#9-studio-gameplay-and-games-projects-sadr-014), [P1.1 mirror attributes](packets/P1.1-entities-world-compile.md))

| Setting | Hollowmere setup | Source |
|---|---|---|
| Rendering | URP 17.0.4, Linear color space, assigned renderer/pipeline assets. | [P0.2 §1](packets/P0.2-projects-tooling.md#1-gameshollowmere-unity-6000075f1-project-skeleton-sadr-016) |
| Input / navigation | Input System 1.19.0, activeInputHandler 1; AI Navigation 2.0.12. | Same source |
| Kernel engine pins | Entities 1.4.6, Burst 1.8.28, Collections 2.6.6, Mathematics 1.3.2. | Same source |
| Audio / backend | Audio on; Standalone IL2CPP; Editor Mono. | Same source |
| Play lifecycle | Domain and scene reload on; fixed timestep 0.02 s. | Same source |
| Startup/build scenes | `Assets/Hollowmere/Boot/Boot.unity`, then region scenes. The older empty `Scenes/Boot.unity` is not the current gameplay boot. | [P1.3 Hollowmere content](packets/P1.3-player-npc-interaction.md#hollowmere-content-made-by-hollowmeregameplayauthoringauthorandbake-idempotent), [P1.5 GameBoot](packets/P1.5-ui-audio.md#gameboot-the-real-boot) |

For a new game, use the same manifest-relative `file:` convention when it lives under `games/<name>`, the pinned settings above, game-owned definitions/scenes and explicit boot composition. Omit qualification marker/fixture packages from shipping projects. Add gameplay contributors and bake through `GameCore.Gameplay.Compile.Entry.Bake`; verify with `Entry.Verify`. The automated installer `studio/tools/new-project.sh`, cleanproof and a validated from-scratch user sequence are **pending P4.1**. No `Studio > Project Setup` menu is established by the shipped packets. ([P0.2 setup](packets/P0.2-projects-tooling.md), [P1.1 Compile](packets/P1.1-entities-world-compile.md#compile), [P4.1 plan](06-implementation-plan.md#wave-4-proof-verification-docs), [initial SR-7.2 requirement](01-gap-assessment.md#7-application-bootstrap-and-independent-game-projects))

## Interactive Editor and headless runs

The documented graphical evidence harness uses an interactive Editor on `DISPLAY=:1`, after taking a host slot and checking the project's Unity lockfile. Its underlying launch is `Unity -projectPath <absolute project> ... -logFile <log>`, without `-batchmode -nographics`. For manual use, open the same project on display `:1`, then choose **GameCore/Studio/Open Studio** and use Unity's Play control before the viewport's Play interaction mode. Coordinate the host instance budget and never open the same project in two Editors. ([evidence-p2.1.sh](../../studio/tools/evidence-p2.1.sh), [StudioViewportWindow.SetMode](../../Packages/com.gamecore.studio.ui/Editor/Viewport/StudioViewportWindow.cs))

A manual host launch using those documented flags is below; reserve capacity and close any Editor already using this project first. ([Interactive launch source](../../studio/tools/evidence-p2.1.sh))

```sh
DISPLAY=:1 "$HOME/Unity/Hub/Editor/6000.0.75f1/Editor/Unity" \
  -projectPath "$PWD/games/hollowmere" -logFile /tmp/hollowmere-editor.log
```

The graphical verification entry points are `studio/tools/evidence-p2.1.sh <packet> games/hollowmere` and `studio/tools/evidence-p2.3.sh <packet> games/hollowmere`. They automate capture and exit; they are not passive Editor launchers. P2.3's archived capture used a host-only metadata patch. ([Script headers](../../studio/tools/evidence-p2.1.sh), [views capture script](../../studio/tools/evidence-p2.3.sh), [P2.3 Verified](packets/P2.3-studio-views.md#verified-host-myubuntu-unity-6000075f1))

### Exact runner interfaces

Replace placeholders with an existing dedicated host clone/project. The headers are authoritative; do not run sync/reset orchestration against an active clone with work to preserve. ([sync-to-host.sh](../../studio/tools/sync-to-host.sh), [codex-packet.sh](../../studio/tools/codex-packet.sh))

| Script | Usage and behavior | Source |
|---|---|---|
| `sync-to-host.sh` | `[--wip] <packet-name>`; clean committed branch by default, optional private-index WIP snapshot. Pushes through git, resets/cleans the destination clone while retaining ignored caches. It force-pushes its branch. | [Header](../../studio/tools/sync-to-host.sh) |
| `unity-compile.sh` | `<packet-name> <project-rel-path> [--tests EditMode\|PlayMode] [--filter <regex>]`; without tests resolves/imports/compiles then quits. With tests uses `-runTests` and no `-quit`. | [Header](../../studio/tools/unity-compile.sh) |
| `unity-batch.sh` | `--project <abs-project-dir> --log-dir <dir> --label <name> [--results <xml>] [--timeout <seconds>] [--attempts 1\|2] -- <extra Unity args...>`; always adds batchmode/nographics. Add `-quit` for plain compile, not a self-exiting test/entry. | [Header](../../studio/tools/unity-batch.sh) |
| `dotnet-test.sh` | `<packet-name> [project-or-solution] [-- extra dotnet test args...]`; default dotnet/GameCore.sln, host SDK, 1800 s default timeout. | [Header](../../studio/tools/dotnet-test.sh) |
| `codex-packet.sh` | `start <packet-name> <branch> <prompt-file> [--base <ref>]`; `status <packet-name>`; `log <packet-name> [lines]`; `result <packet-name>`; `stop <packet-name>`. Creates/resets a host clone and runs the packet there; outputs under `.codex/`. The brief must require commit/push to the branch. | [Header](../../studio/tools/codex-packet.sh) |

Examples using the documented runners, to execute during an implementation packet with compile/test scope. The corrected stage entry namespace is `GameCore.Studio.Edit.StageCommandLine`, not the older `GameCore.Studio.Stage` example in unity-batch's header. ([P2.4 batch entries](packets/P2.4-staging-lane.md), [StageCommandLine.cs](../../Packages/com.gamecore.studio.core/Editor/Stage/StageCommandLine.cs))

```sh
studio/tools/unity-compile.sh <packet> games/hollowmere
studio/tools/unity-compile.sh <packet> games/hollowmere --tests EditMode --filter 'GameCore\.Studio\..*'
studio/tools/unity-compile.sh <packet> games/hollowmere --tests PlayMode --filter 'Hollowmere\..*'
studio/tools/dotnet-test.sh <packet> dotnet/tests/GameCore.Rules.Gameplay.Tests
studio/tools/unity-batch.sh --project "$PWD/games/hollowmere" \
  --log-dir /tmp/studio-compile --label compile -- -quit
```

Both batch runners share flock slots under `~/wkspace/gc-studio/.unity-slots`, default maximum three batch Editors host-wide, excluding import workers. Each invocation runs one Editor. Compile defaults to 1500 seconds and log-silence detection to 600 seconds; timeout/silence kill gets one retry, a real compile/test failure gets none. Logs/XML go into the packet's `.unity-logs` or the explicit log directory. Read result XML: P2.3 reports Inconclusive blockers and P2.2 live tests are env-gated; a wrapper failure from skipped/inconclusive results is not proof those cases passed. ([Runner headers](../../studio/tools/unity-compile.sh), [unity-batch.sh](../../studio/tools/unity-batch.sh), [P2.3 Verified](packets/P2.3-studio-views.md#verified-host-myubuntu-unity-6000075f1), [P2.2 Verified](packets/P2.2-studio-etos-client.md))

`studio/tools/live-etos-tests.sh <packet>` exercises real providers and the microphone path and writes P2.2 evidence. Keep it separate from ordinary unit verification. Full V1 `tools/reproduce.sh` and integrated gates belong to the integrator, not every packet. ([P2.2 Running it](packets/P2.2-studio-etos-client.md#running-it), [Plan §1 and §3](06-implementation-plan.md))

## Linux player build status

The existing player build lane is the **V1 probe** lane: `tools/unity/build_probe.sh`, driven by `UNITY`, `UNITY_PROJECT`, `ARTIFACTS`, and the clean reproduction procedure. Its default executable is `unity/GameCore.Validation/Builds/Linux64/GameCoreProbe.x86_64`; headless probes require their documented `-probeResult` and mode flags. They are not Hollowmere launch flags. ([Operator build §6](../operator/build-and-run.md#6-re-running-pieces), [headless §3–§4](../operator/headless.md))

The requested `studio/tools/build_game_player.sh`, graphical player launch/capture lane and final Hollowmere build evidence are **pending P3.1** at this draft's baseline. The plan names that Studio path, while SR-11.3 names `tools/build_game_player.sh`; the integrator must settle the final path. Build settings and boot scenes alone do not establish a player build. Generated catalog reflection also needs stripping preservation verified in that lane. ([P3.1 plan](06-implementation-plan.md#wave-3-reference-game-and-ai-workflows), [SR-11.3](01-gap-assessment.md#11-packaging-installation-build-release), [P1.1 Open](packets/P1.1-entities-world-compile.md#open), [tracked tool inventory](11-ownership-plan.md#tool-and-script-inventory))

## Troubleshooting

| Symptom | Cause / action | Source |
|---|---|---|
| Editor log stops for 10 minutes | Known unresolved pre-dispatch/package-resolve or shutdown hang. Keep watchdog logs, retry once only; missing XML is NotRun. See the backtrace procedure before diagnosing it as managed-code failure. | [editor-hang.md](../operator/editor-hang.md), [P0.2 observed shutdown hang](packets/P0.2-projects-tooling.md) |
| Project already open / UnityLockfile | Check for a live Editor using the exact project. Remove `Temp/UnityLockfile` only after proving none owns it; the evidence script does this check. | [evidence-p2.1.sh](../../studio/tools/evidence-p2.1.sh) |
| Waiting for a Unity slot | Other host Editors consume the budget. Keep runners on the shared flock protocol and use distinct project copies. Do not increase the host limit to bypass contention. | [Plan §1](06-implementation-plan.md#1-working-method), [unity-batch.sh](../../studio/tools/unity-batch.sh) |
| Outbound GitHub/NuGet/container access fails | P0.1 records host proxy dependence. Image builds detect `127.0.0.1:7897`; detached packet runs import only proxy exports. Do not dump a login environment to discover settings. etosd deliberately unsets desktop proxies for direct provider access. | [P0.1 §1](packets/P0.1-host-etos.md), [host-build-images.sh](../../studio/tools/host-build-images.sh), [codex-packet.sh lines 71–73](../../studio/tools/codex-packet.sh#L71) |
| `NU1900` | Establish that the advisory feed is unreachable before `GAMECORE_OFFLINE=1`; it adds NuGetAudit=false to that invocation only. Report audit NotRun. | [operator build §2.1](../operator/build-and-run.md#21-gamecore_offline1-the-one-documented-way-to-run-without-the-nuget-audit) |
| Companion fails to start from manifest | Manifest paths cannot contain `..`; use installer-created bin link. Confirm the Studio node root, worker image/model and linked binary. | [P0.5 §3–§5](packets/P0.5-companion.md), [P0.1 Host](packets/P0.1-host-etos.md#host-myubuntu-user-worlesenric) |
| Task stuck / default model refusal | Historical dropped-open bug was fixed at pinned etos 278ef9c; workers use gpt-6-sol because default's upstream model was refused. Preserve request ids and diagnose against the deployed pin. | [P0.1 §1 and §3](packets/P0.1-host-etos.md) |
| Describe error mentions max_cost_usd | P2.2 evidence is blocked at an older companion. Later P0.5 records the fix and a real proxy 200; the full integrated rerun remains open. | [P2.2 known issues](packets/P2.2-studio-etos-client.md#blocked--known-issues), [ops.rs](../../studio/agent/src/ops.rs), [P0.5 host follow-up](packets/P0.5-companion.md#host-run-after-re-vendoring-etos-278ef9c) |
| Stage timeout after upgrades | Warm Library is reused until removed; P2.4 instructs invalidating `<stage root>/_warm` after Unity/kernel upgrades. Cold seeding took about 12 minutes, beyond the warm stage budget. | [P2.4 open item 6](packets/P2.4-staging-lane.md#open-items) |
| Save UI unavailable | Production game codecs/UseSaves/restore wiring are missing; this is not fixed by pairing etos. | [P1.5 Open](packets/P1.5-ui-audio.md#open) |
| Hollowmere player exits 139 at start (SIGSEGV in `PlayerMain`, `__strcasecmp_l_avx2`; log line "The selected window backend is (null)") | The Unity 6000.0 Linux player picks a window backend even under `-batchmode -nographics` and crashes when no X display is reachable, which is the case in a non-interactive ssh session (`DISPLAY` unset; a dummy `DISPLAY=:99` crashes too). Run headless players under a private virtual display, `xvfb-run -a <player> -batchmode -nographics ...` (what `build_game_player.sh`'s smoke does), and graphical runs on a real display (`DISPLAY=:1`, `record_playthrough.sh`). Never borrow `:1` for headless smokes. | [P3.1 PACKET](../../PACKET.md), [build_game_player.sh](../../studio/tools/build_game_player.sh) |
| Runtime bootstrap fallback differs from operator prose | Legacy validation defaults to InfrastructureFallback. A game registered through GameApplication uses HardFailure. Keep those profiles distinct. | [P0.4 switch](packets/P0.4-kernel-app.md#1-changes-by-sadr), [operator headless §1](../operator/headless.md#1-production-entry-points) |
