# P3.1 hollowmere-content: Hollowmere, the complete reference game

Owner: Claude Opus 5.5 (worktree `agent-a86a49b2ba589f359`, branch `worktree-agent-a86a49b2ba589f359`).
Host: myubuntu (Unity 6000.0.75f1, .NET 8). Nothing was compiled, built or tested on the Mac.
Final code revision: **d59a1af5**. It merges main 5e5a088e, which includes:
- R2-G2 and APP-1 (e4ffd40);
- R2-D2 (the media gateway provider);
- the gate-checker renames (5eefd3d);
- ADAPT-SPLIT (fd03853), whose Hollowmere manifest and lock were taken from main;
- R3-E.

Commits after it add evidence and this file only.

## 1. What the game is

| | |
|---|---|
| Regions | Thornwick Village, Blackmere Marsh, the Drowned Belfry (portal-linked; per-region atmosphere, lights, scenery) |
| NPCs | Maren (innkeeper, rumour), Odd (ferryman), Pip (shrine order), Warden Hale (causeway gate), the Belfry Echo (ending speaker), Bram (lantern) |
| Quest | "The Drowned Bell": Rumour, then Lantern (Bram or the inn vendor), then Crossing (Hale's gate, the shrine lanterns west to east, the clapper, Odd's ferry), then Belfry (the Echo and the bell) |
| Endings | A "Silence" (let it sleep); B "The Toll" (ring it; `maren_grateful`); C "The Freed Echo" (ring it after the Echo's ask) |
| Failure | Lantern lost (`Failure_LanternLost`); stuck paths stay at their stage |
| Systems used | dialogue graphs, quest/facts/rules, inventory + vendors + loot, interaction (examinables, pickups, gates), save/load (Pause, then Save/Load, slots `slot-N`), UI/audio, generated portraits and voices |

**Studio authoring.** Everything is authored through Studio tools as journaled change sets, using `HollowmereAuthoring.AuthorAll` and its steps:
- `story.*`: facts, items, item tuning, starting coins, sets, quest, rules, graphs, inn vendor, marsh loot, declared trade, registrations;
- `world.*`: NPC rosters, Bram, Odd's behaviour, interactables, world items, placements, the belfry enclosure;
- `dress.*`: textures, materials, prop/NPC skins, region scenery and lights, atmosphere, NPC animator and its states, boot presentation;
- `media.*`: portraits, voices, the SFX bank;
- `closure.*`: stamina HUD, application registration, run action;
- `migrate.refs`.

The journal under `games/hollowmere/Studio/History` holds:
- **78 P3.1 change sets**: 73 Applied, 5 Rejected (kept as evidence of refused attempts);
- **589 operations** across **23 tools**.

| Tool | Ops |
|---|---|
| `assign` | 255 |
| `create` | 96 |
| `hollowmere.addScenery` | 95 |
| `set` | 31 |
| `asset.import` | 24 |
| `hollowmere.createMaterial` | 19 |
| `interaction.addExaminable` | 13 |
| `hollowmere.skinSceneObject`, `hollowmere.addLight` | 10 each |
| `audio.assignClip`, `hollowmere.skinPrefab`, `entity.applyOverride` | 7 each |
| `hollowmere.setAtmosphere` | 3 |
| `npc.addAt`, `hollowmere.generateNpcAnimator` | 2 each |
| `delete`, `move`, `addComponent`, `hollowmere.configureBootPresentation`, `ui.bind`, `hollowmere.registerApplication`, `inventory.grantStarting`, `authoring.migrateRefs` | 1 each |

**Game-owned Studio tools** (`Assets/Hollowmere/Authoring/Tools/Editor`, journaled like the built-ins): `hollowmere.addLight`, `hollowmere.addScenery`, `hollowmere.configureBootPresentation`, `hollowmere.createMaterial`, `hollowmere.generateNpcAnimator`, `hollowmere.registerApplication`, `hollowmere.setAtmosphere`, `hollowmere.skinPrefab`, `hollowmere.skinSceneObject`.

**Media** (`artifacts/studio/workflows/P3.1/media-manifest.json`, 57 entries with sha256): 34 etos gateway ops, 23 procedural assets.
- The etos ops are six NPC portraits (gpt-image) and the 28 dialogue voice lines (TTS).
- Their ceilings total 5.80 USD of the 15 USD budget. Each op sent `max_cost_usd` ≤ 0.50; the gateway reports no actual cost.
- The procedural assets (SFX, textures, meshes) were generated locally at cost 0.
- No media op ran after the companion started requiring `X-GameCore-Project`. The recording and builds make no node calls.

## 2. Runtime pieces (games/hollowmere)

- `Game/Runtime`:
  - `HollowmereGame`, `HollowmereDirector` (+ definition), `HollowmerePersistentSession`, `HollowmereConditions`, `RegionAtmosphere`;
  - `FrameLogRecorder` (`-frameLog`); `HollowmereCommandLine`;
  - the autoplay driver: `AutoplayScript`, `AutoplayRunner`, `AutoplayIntentSource`.
- Autoplay verbs: `wait`, `waitframes`, `ui`, `walk`, `approach <entity> [within m] [run] [timeout s]`, `face`, `interact`, `advance`, `choose`, `waituntil`, `mark`, `log`, `quit`.
  - Walking re-targets by baked entity name, ends on a portal jump, sidesteps a blocked path, and fails naming the collider ahead.
  - Scripted intents only run with `-autoplay` or on request; P1.5's pause gate stays the input source.
- `Autoplay/smoke.txt` drives the player smoke. `Autoplay/playthrough.txt` drives the recorded playthrough (about 10.5 min; see section 6).
- `Boot/GameBoot.cs` changes are **additive only** (zero removed lines against main):
  - `Saves`, the `SavesInstalled` event (raised in `UseSaves` before its UI-rig early return);
  - `AdmissionReady(SaveService)`.
- New `Boot/AdmissionSmokeFrames.cs`: a per-frame `Frame` event, added only by the Editor integration.
- **Resources asset check.** `Boot/Resources/Hollowmere/Application.asset` holds only five GUID object references (manifest, content, player, npcs, interactions) and its script GUID. There are no secrets, keys, tokens or file paths (grep for `etk_|key|token|/home|/Users|secret|password` finds 0 matches).

## 3. Studio admission in the real game (R2-G request 4, as superseded by R2-G2)

`Authoring/Editor/HollowmereStudioAdmission.cs` is game-owned and Editor-only. It lives in `Hollowmere.Authoring.Editor`, which references `GameCore.Studio.Gameplay.Editor`: since ADAPT-SPLIT, `StudioAdmissionServices` and `WorldLiveOpTranslator` live in the Editor-only package `com.gamecore.studio.gameplay`, with the same namespaces and signatures. The player build references no Editor assembly; `GameBoot` only raises an instance event.

**Binding.**
- It binds on every `GameBoot.SavesInstalled`, i.e. every `UseSaves`, including each restored-world replacement.
- It also binds on EnteredPlayMode and after a domain reload while playing.
- The call is R2-G2's exact line:
  `StudioAdmissionServices.BindAdmission(runtime, () => service, () => boot.AdmissionReady(service), verdict => StudioAdmissionServices.RunSmokeTest(runtime, verdict, (type, method, steps) => smoke.RunAdmittedSmokeEntry(verdict, type, method, steps)))`
  It uses the tri-state poll overload.
- `SmokeTestFrameBudget` is raised to 240 (2 × the 120-step maximum) **before** binding, i.e. before any admission begins.

**Readiness.** `GameBoot.AdmissionReady` is R2-G2's literal lambda: world and narrative exist, and `World.Root` is both `service.ActiveRoot` and `GameApplication.Current`.

**APP-1 finding (SADR-021).**
- P3.1 found that `SaveService.Restore` stopped the replaced root and so cleared `GameApplication.Current`, leaving a restored game that admission could never call ready.
- It was accepted as a unity.app defect and fixed by Codex APP-1. SADR-021: restore transfers application ownership to the restored root before stopping the replaced one.
- P3.1 carried an interim acceptance (null `Current` after a re-attach) until APP-1 reached main (e4ffd40). The interim was then **removed** (6f711a9b), and the admission test passes on the literal lambda.

**Smoke registry (`HollowmereAdmittedSmoke`).**
- It holds trusted entries compiled into the Editor assembly only. Today that is the pressure-plate sample: `Hollowmere.Mechanism.PressurePlate.PressurePlateSmoke.Begin`, package `com.hollowmere.mechanism.pressureplate`.
- The entry signature is `AdmissionSmokeStatus RunAdmittedSmokeEntry(StageVerdict verdict, string type, string method, int steps)`.
- An entry registers once per verdict digest and active root.
- It advances **only** from `AdmissionSmokeFrames.Frame` (LateUpdate). It never advances from the poll, never pumps, and never calls the sandbox `Begin` harness.
- It asserts the live world every step: ready, same root, root Running/Paused. After the last step it asserts once more with an equal save round trip.
- Status:
  - Pending until then;
  - Passed only then;
  - Failed on an assertion failure, root replacement, a missing entry, a package mismatch, or steps outside 1..120.

**The Play-Mode admission test** `Tests/P3_1/EditMode/P31AdmissionInPlayMode.PressurePlateAdmittedIntoTheRunningGame` uses the real sample artifacts `samples/mechanisms/pressure-plate/candidate/artifacts/{package.tgz,proposal.json}`.
1. Admit from Play Mode: capture, then stop. The install happens in Edit Mode.
2. Re-enter Play Mode and re-bind; then restore and run the polled smoke.
3. Result: **"Admitted and verified." after 122 editor frames; 121 polls; 120 game steps; Pending → Passed**; coins restored; round trip equal.

The result is identical on 03e3496f (before ADAPT-SPLIT) and on 905ec23e (after ADAPT-SPLIT).

## 4. Tests

| Suite | Revision | Result | Evidence (`artifacts/studio/evidence/P3.1/tests/`) |
|---|---|---|---|
| Full EditMode | 905ec23e (ADAPT-SPLIT) | **392 passed, 0 failed, 11 skipped** (403) | `editmode-905ec23e.xml` |
| Full EditMode (retained, earlier) | a869ed8b | 385 passed, 1 failed (R2_41), 8 skipped | `editmode-a869ed8b.xml` |
| Full PlayMode | 03e3496f | **19/19 passed** | `playmode-03e3496f.xml` |
| Admission pair | 03e3496f | 2/2 passed (also inside the 905ec23e run) | `admission-03e3496f.xml` |
| TenPlayEditCycles (explicit, W-GAME-08) | 03e3496f | passed | `tenplayeditcycles-03e3496f.xml` and `../memory-cycles.json` |

Beyond 905ec23e, the final revision adds only main's R3-E (Studio runner scripts and Python tests). The V1 gate below re-ran the validation suites on it.

**The 905ec23e run.**
- The runner prints "FAIL ... PARTIAL/NotRun" only because its acceptance rule refuses skipped cases.
- All 11 skips are environment-gated:
  - 4 live etos tests and 3 P3.2 live workflows (need `GAMECORE_ETOS_LIVE=1`);
  - 3 that need a graphics device (`-nographics`);
  - `TenPlayEditCycles` (explicit; run separately).

**R2_41.** In the retained earlier run, `R2EtosTests.R2_41_AudioToolDiscoversGatewayAndImportsVerifiedVoiceThroughEngine` failed, and it is **not P3.1**.
- The log read `[R2_41] media gateway lookup saw 0 provider(s): ; EtosStudioSession implements the provider: False`: no production type implemented `IMediaGenerationGatewayProvider`.
- It was routed to Codex **R2-D2**, which made `EtosStudioSession` the provider.
- On 905ec23e it **passes**, with the log line `saw 1 provider(s): GameCore.Studio.Etos.EtosStudioSession -> GameCore.Studio.Etos.EtosMediaGenerator`.

**P3.1's own tests:**
- EditMode `P31AuthoringTests`: `AuthorAllIsIdempotent`, `BakeVerifies`, `ContentIsBoundToScripts` (script binding).
- EditMode `P31PlayModeHooksTests`: `AdmissionFromPlayModeCaptures`, `TenPlayEditCycles`.
- EditMode `P31AdmissionInPlayMode`: `PressurePlateAdmittedIntoTheRunningGame`.
- PlayMode `FullQuestHeadless`: `EndingA_Silence`, `EndingB_Toll`, `EndingC_FreedEcho`, `Failure_LanternLost`, `SaveRestoreMidQuest`.
- The player smoke is in the build (section 5).

**Old-story tests ported** to P3.1's Drowned Bell:
- P1.4 `NarrativeBakeTests`: counts are facts 25, graphs 6, quest 1, items 6, vendors 2, world items 6, rules 31; endings A/B/C simulated.
- P1.4 PlayMode `DrownedBellHeadless`:
  - ending B; the bell and the coins pickup captured in flight and replayed;
  - asserts no refusal while playing;
  - the replay of the despawned coins is refused rather than re-applied, and this is logged.
- P1.7a `HollowmereSaveRestore` (5 cases): coin counts relative to the starting purse; the portal is gated on `heard_rumour`; Hale opens the gate.
- P1.7b `IndexEdgeTests` and P2.3 `HollowmereEditingViewsTests` / `HollowmereRelationshipsWorldTests`: objectives, three branches, loot through the oil flask.
- P1.7c test asmdefs reference `Hollowmere.Checkpoint`.

**W-GAME-08 memory** (`memory-cycles.json`; 10 Boot.unity Play/Edit cycles; sampled after GC + UnloadUnusedAssetsImmediate):
- allocated at cycle 10 is **−0.38%** vs cycle 1, and reserved is **+0.0%**;
- cycle 2 is a one-off warm-up peak (+40% allocated) that returns by cycle 3.

## 5. Player build and V1 gate

`studio/tools/build_game_player.sh p3.1 --gate` ran on the host clone at **d59a1af5**. The player is at `~/wkspace/gc-studio/p3.1/build/HollowmereLinux/`.

**Build: PASS.**
- Unity 6000.0.75f1, IL2CPP, StandaloneLinux64; 0 errors, 4 warnings.
- 1057 s, including one retry: the first Unity attempt hit the documented ILPP start-up fault (`Can't find file /tmp/ilpp.sock-*`), which R3-E's runner retries.
- `Hollowmere.x86_64` sha256 `aeaf13e291886fbd5a99b7dbd7c113b8ee13ed462a419a4d31a8ecbc3241ac70`. This is the Unity player stub and is identical across builds; the game is in `GameAssembly.so` and `Hollowmere_Data`, listed in `data-manifest.txt`.
- Shipped output is 176,265,359 bytes. That excludes 1,735,270,270 `notShippedBytes` of IL2CPP's `*_ButDontShipItWithYourGame` / `*_DoNotShip` folders, which the manifest and size now leave out.
- Player smoke: `xvfb-run -a … -autoplay smoke.txt`, **exit 0**, 2 s, 3,590 frame-log rows.
- **Bootstrap adoption:** the A11 registration (`GameApplication.Register` at SubsystemRegistration) is adopted by GameBoot in the player. The smoke log shows "registered the application definition at SubsystemRegistration (11 plugins)", then "adopted the application root registered at SubsystemRegistration".

Evidence is in `artifacts/studio/evidence/P3.1/build/`: `build-summary.json`, `build-report.json`, `data-manifest.txt`, `sha256.txt`, `unity-batch.txt`, `smoke-player.log`, `smoke-stdout.txt`. The full build log (348,981 bytes, sha256 `47eeccc8f6de8983f405fcd7cc7f0585caf60df58d62057197996544fc263bf8`) is at host `~/wkspace/gc-studio/evidence/p3.1/build-d59a1af5/build.log`.

**V1 gate (`tools/run_w7_gate.sh`, on the same revision as the build): PASS at d59a1af5.**
- Evidence: `v1-gate-transcript-PASS.txt`; "Wave 7 integration gate PASSED"; exit 0; 4587 s.
- It covers every dotnet suite and the host-side checks (`check_gate_sources`: unresolved none).
- It covers Unity EditMode/PlayMode and every GC/W probe run: narrative, cards, transition, W4, fault boundary, lifecycle stress, W6, catalog coverage, recovery, conformance, W7, world-dispatch, checkpoint round trip, adapters, traversal.
- It covers the release shape (GC-017 release surface and release player), the short benchmark correctness diagnostic, and the documentation validator.

Earlier runs, kept for the record:

**Run 1 at c8497644: FAIL** (`v1-gate-transcript-run1-FAIL.txt`).
- Every dotnet suite passed, as did the derivation-equivalence and 10k-property suites and `check_game_core_csharp`.
- `check_gate_sources.py` member resolution reported `Result.Accepted` / `Result.ResultHash` / `Result.Assemblies` in `W7GateScenario.cs` 417–425 as unresolved, giving the verdict REVIEW NEEDED.
- Cause: main's new nested `public sealed class Result` in `Packages/com.gamecore.studio.views/Tests/Editor/R2ViewsRegressionTests.cs` (75f5c51a) makes the name-based checker read the property `IncrementalDerivationOutcome.Result` as that type.
- This is not P3.1: the branch had zero diff from main in Packages/, unity/, tools/ and dotnet/. It was fixed on main 5eefd3d (`Result` renamed to `CheckerResult`; studio.core's test `World` renamed to `FakeWorld`).

**Run 2 at 636f9293** (after 5eefd3d): stopped by me (`v1-gate-transcript-run2-636f9293-stopped.txt`), because ADAPT-SPLIT (fd03853) required a merge and a rebuild.
- Before I stopped it, the host checks passed. The `check_release_clone` self-test reports its seeded PROBLEM(S) as designed.
- The Unity EditMode stage timed out once at 1800 s under host load, and its retry passed **1318/1318**. PlayMode passed **84/84**.

The gate verdict is tied to the clone revision, so each main merge meant a rebuild before the passing run 3.

## 6. Recorded playthrough on :1

The recording was made with `studio/tools/record_playthrough.sh p3.1 games/hollowmere/Autoplay/playthrough.txt --minutes 11 --label playthrough-d59a1af5`, using the d59a1af5 player (the gated build), fullscreen on the host display `:1` with `-frameLog`.

**Evidence.** In the repo: `artifacts/studio/evidence/P3.1/recording/`. On the host: `~/wkspace/gc-studio/evidence/p3.1/playthrough-d59a1af5/`.

| | |
|---|---|
| Played | **633 s** (10 min 33 s); player **exit 0**; autoplay `quit 0` at frame 36,465 |
| Route | menu → new game → Thornwick (Maren's rumour, the coins, Pip's shrine hint, Bram's lantern) → Hale's gate → Blackmere (the shrine lanterns west to east, the clapper, **manual save to slot 1**, the satchel) → Odd's ferry → the Drowned Belfry (the Echo, **ring** the bell) → **Ending C "The Freed Echo"** → **restart** → **load slot 1 mid-quest** → a walk on the causeway → quit. Every `mark` was reached (`player.log`). |
| etosd | User unit `etosd.service`. **Stopped at 2026-10-05T23:28:05+08:00 and restarted at 23:38:43+08:00** (systemd user journal), with 0 etosd processes during the run. Before launch, the script checks `pgrep -f 'live-etos-tests\|gc-studio/p2'` and found nothing. |
| Network | `unshare -rn` is refused on this host (`kernel.apparmor_restrict_unprivileged_userns=1`; "write failed /proc/self/uid_map: Operation not permitted"). The fallback was `ss -tunap` snapshots every 5 s, filtered to the player PID, with the verdict **"no inet sockets opened by the player"** (`network-verdict.txt`). The full host-wide snapshot file stays on the host; its sha256 is in `playthrough.sha256`. |
| Video | `playthrough.mp4`: 20,223,247 bytes (≤ 25 MB, committed); sha256 `ba2ea75016c0d5555f76eecc2b4872c935100b2d4cd8b53413e9bafe7b9a886d`; x11grab 1280x720 at 30 fps with PulseAudio monitor audio |
| Keyframes | `keyframes/kf-0000s.png` … `kf-0630s.png`, every 30 s (22 files, each ≤ 280 KB) |
| Save headers | `save-headers.json`. `slot-1.gcc` is 260,236 bytes (`GCCK` header) and `slot-1.json` is format 1, gameId hollowmere, catalog `d82aed185b6d…`, region `7f21b99a…` (Blackmere), playTime 289.97 s, logicalStep 17182, `temporalContinuity: true`, documentHash `2fb8c659…`. |
| Frame data | `frame-log.csv` (36,465 rows) and `frame-stats.json` |

**B-FRAME** (16.7 / 100 / 250 ms): **FAIL**.

| Check | Result | Status |
|---|---|---|
| p95 ≤ 16.7 ms | **p95 21.82 ms** (p50 16.67, p99 25.42, mean 16.78; 59.9 fps mean) | FAIL |
| Per-region p95 | village 17.92 ms, marsh 22.27 ms, belfry 21.68 ms | the marsh and belfry scenes are the heavier ones |
| No frame > 100 ms outside the 1 s after a transition | **3 frames** | FAIL |
| Transition hitches ≤ 250 ms | village→marsh 17.7 ms; **marsh→belfry 267.1 ms**; belfry→village (restart) 44.6 ms; load (restore) 61.3 ms | FAIL (the belfry transition only) |
| Steady window ≥ 0.9 × budget | 608.6 s | pass |

The three frames over 100 ms:
- Frames 1 and 2 (2,900 ms and 612 ms) are the boot load. The rule starts the steady window at the first `ready` marker, which is the menu's first frame.
- Frame 17183 (288 ms) is the **manual save**, a synchronous write of the 260 KB checkpoint.

**What this shows:**
- Two real costs in the game: the synchronous save (~290 ms) and the belfry region load (~267–269 ms, also in run 1). Both are follow-ups for the game owner: an asynchronous checkpoint write, and preloading or splitting the belfry region.
- Frame pacing: the mean is exactly 60 fps, but 47% of frames exceed 16.7 ms. That is pacing jitter while x11grab encodes the same display, on a shared host (load average 7–22 during this session from other packets' Unity instances). Run 1 on a quieter stretch measured p95 17.10 ms.
- The verdict is reported as measured; the rule was not changed after seeing results.

**Run 1** (c8497644, kept in `recording-run1/`) also exited 0 but played only 523 s: vsynced walking is faster than the headless rehearsal, so the script was lengthened. Its B-FRAME was also FAIL: p95 17.10 ms, two village stalls of 464 ms and 805 ms, and the belfry transition at 269 ms.

Before both recordings, a headless rehearsal ran the script under `xvfb-run -a`: 632 s, `quit 0`.

## 7. Findings

- **The Unity 6 Linux player needs an X display.** Without one, it SIGSEGVs in PlayerMain (exit 139), even with `-batchmode -nographics`. `build_game_player.sh` therefore runs its smoke under `xvfb-run -a`. A troubleshooting row was added to `docs/studio/10-install-build-run.md` (approved one-off edit).
- **Compile errors can appear only in `Library/Bee/tundra.log.json`.** A build failed with "ExitCode 3" and no visible error; the real error was CS0234 in the P1_7c test asmdefs, found in that file.
- **Stale semantic index.** The persisted index misses assets changed outside the Editor, so `AuthorAll` rebuilds the index first.
- **Asset edit block.**
  - Folder creation inside `AssetDatabase.StartAssetEditing` is not visible to `IsValidFolder`. This produced 20 stray numbered folder metas (`Materials 1..18` and others); they were removed, and `EnsureFolder` now trusts the disk.
  - Animator controller sub-assets created in the block were lost. `dress.npc-animator-states` repairs them through the journal.
- **`unity-compile.sh` line 89** forwards an empty required-tests array under macOS bash 3.2 (`${required_tests[@]+"${required_tests[@]}"}`). This was an approved one-off edit.
- **The host sync wipes untracked files.** `sync-to-host.sh` resets and runs `git clean -fd` on the host clone, which deletes evidence written into it. Evidence is therefore copied out right after each run, and `TenPlayEditCycles` also logs its JSON.
- **R2-B's polled-smoke budget.** It charges one frame per Editor-update poll (default 120), so an entry needing 120 game frames must raise the budget before admission begins; P3.1 sets 240.

## 8. Scope

**Edited outside `games/hollowmere/**`:**
- `studio/tools/build_game_player.sh` and `studio/tools/record_playthrough.sh` (new, owned);
- `artifacts/studio/workflows/P3.1/**` and `artifacts/studio/evidence/P3.1/**`;
- the approved one-offs `studio/tools/unity-compile.sh` (line 89) and `docs/studio/10-install-build-run.md` (one troubleshooting row).

**No package files changed:** zero diff against main under `Packages/`, so there are no new package files to list.

**Secrets:** the etos key was never printed or committed. Committed logs and XMLs contain no `etk_` strings (checked).

**Never run:** `tools/reproduce.sh`, full perf benchmarks, repeats above 2.
