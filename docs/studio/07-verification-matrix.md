# GameCore Studio: verification matrix and budgets

**Status:** budgets fixed before implementation (2026-10-04); rows filled by the verification owner (P4.2) on the
integrated revision. A row is **exercised** only with linked evidence produced on the delivered revision; historical
reports never count. Deterministic doubles are allowed for isolated tests and never as the only evidence for an
external integration. Repeated measurements are capped at two runs (owner rule).

## 1. Evidence conventions

`artifacts/studio/<area>/<row-id>/` holds: `README.md` (what was run, revision sha, host, date, result),
transcripts, screenshots or recordings (`.png`/`.mp4` under 20 MB, otherwise an external path with sha256), and
machine-readable results where they exist (`result.json`). `tools/studio/check_studio_evidence.py` checks that
every row marked exercised has a folder with a README naming the revision.

## 2. Budgets (local latency separated from model/network latency)

| ID | Budget | How measured | Pass rule |
|---|---|---|---|
| B-FRAME | Graphical Linux player, 1080p, RTX 4060 Ti: p95 frame time ≤ 16.7 ms over a 10-minute playthrough; no frame > 100 ms outside the first second after a region transition; transition hitch ≤ 250 ms | in-game frame logger (`-frameLog` flag) | both conditions, 1 of 2 runs may be re-run once |
| B-SELECT | Hover/click resolution ≤ 16 ms; marquee over 500 candidates ≤ 50 ms | `Stopwatch` in the picking service, Editor log | p95 over 100 picks |
| B-APPLY | Single-target change set apply ≤ 200 ms; region-wide (all NPCs in Marsh) ≤ 1 s; measured without model time | edit engine timers in the journal (`timings`) | p95 over 20 applies |
| B-COMPOSE | Kernel prepare for one composition edit in the reference world (≤ 1.5k simulated targets) ≤ 300 ms in the Editor | bridge telemetry | p95 over 20 edits; the 10k-target issue stays open and is reported, not re-budgeted |
| B-REGION | Region load ≤ 2 s, unload ≤ 1 s; after unload: zero views, zero audio clips, textures of that region released (Profiler memory delta ≤ 5 % of peak) | `RegionStreamer` timings + Memory Profiler snapshots | both |
| B-MEMORY | After 10 Play/Edit cycles in the Editor, managed + native memory within +15 % of cycle 1 | Memory Profiler snapshots cycle 1 and 10 | once |
| B-AGENT-UX | Task state change visible in the tray ≤ 1 s; cancel acknowledged ≤ 2 s; WebSocket reconnect ≤ 5 s | companion event timestamps vs Unity receipt | p95 over 20 |
| B-MODEL | Reported separately: task wall time, provider latency, usage | ledger | informational, no pass rule |
| B-STAGE | Stage job (compile + tests) ≤ 6 min on the host; admit → play resumed ≤ 90 s | stage verdict `durationMs`, admit timer | both, 2 runs |

## 3. Rows

Status: `planned` → `exercised` / `blocked(prereq)` / `failed`. Only the verification owner changes a status.

### Editor interaction (W-UI)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-UI-01 | Open Hollowmere in Studio; Play; walk; Select; click NPC; card shows definition | SR-1.1 | planned | |
| W-UI-02 | Box-select three NPCs behind a fence; overlap list; choose NPCs | SR-1.2 | planned | |
| W-UI-03 | Subpart vs logical vs prefab vs scope choice on a lantern | SR-1.3 | planned | |
| W-UI-04 | Pump count exactly one per frame with the viewport open; Play Mode enter/exit 10× | SR-8.2 | planned | |
| W-UI-05 | Selection/hover and marquee timings | B-SELECT | planned | |

### Edit execution (W-EDIT, W-MODEL, W-TOOL)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-MODEL-02 | Delete an item asset; index lists every referencing dialogue line and objective | SR-2.2 | planned | |
| W-TOOL-01 | Tool catalog of the clean project lists only installed plugins' tools | SR-2.3 | planned | |
| W-TOOL-02 | `check_package_metadata.py` passes with all new packages | SR-11.1 | planned | |
| W-EDIT-01 | Change set in History shows etos task id and GameCore operation ids | SR-3.1 | planned | |
| W-EDIT-02 | 5-op change set with one stale op: AllOrNothing rollback and BestEffort partial report | SR-3.2 | planned | |
| W-EDIT-03 | Generate portrait → apply → undo → redo: no second generation (usage unchanged) | SR-3.3 | planned | |
| W-EDIT-04 | Edit a running NPC, exit Play, apply; delete it, apply: `StaleTarget` | SR-1.4 | planned | |
| W-EDIT-05 | Gizmo drag and typed value produce identical History entries | SR-1.6, SR-3.4 | planned | |
| W-EDIT-06 | Runtime-only move → "Apply to authored" → persists after exiting Play | SR-1.8 | planned | |
| W-EDIT-07 | Concurrent manual rename vs agent candidate: per-op `Conflict`, rebase works | SR-3.5 | planned | |
| W-EDIT-08 | Apply timings (single and region-wide) | B-APPLY, B-COMPOSE | planned | |

### ETOS and AI (W-ETOS, W-AI, W-VOICE)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-HOST-01 | `install.sh` fresh, then no-op; `verify.sh` with real image/describe/tts/realtime calls | SR-11.2 | planned | |
| W-ETOS-01 | Pair; grep repo + Library for `etk_`: zero hits; logs redacted | SR-4.1 | planned | |
| W-ETOS-02 | Unity key against another agent: `forbidden`; shown in Studio log | SR-4.2 | planned | |
| W-ETOS-04 | Worker `etos query` returns the selected NPC's dialogue nodes | SR-2.4, SR-4.3 | planned | |
| W-ETOS-05 | Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task | SR-4.4 | planned | |
| W-ETOS-06 | Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly | SR-4.5, SR-9.2 | planned | |
| W-ETOS-07 | Generated texture arrives with matching sha256; tampered file refused | SR-4.6 | planned | |
| W-ETOS-08 | Remove image provider: `not_configured` surfaced; nothing applied | SR-4.9 | planned | |
| W-ETOS-09 | Domain reload during a task: tray shows the same task afterwards | SR-8.3 | planned | |
| W-VOICE-01 | Speak a destructive command without sending: nothing happens; final transcript appears; partial revisions visible | SR-4.8 | planned | |
| W-AI-01 | Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores | mandate §10 | planned | |
| W-AI-02 | Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav | mandate §10 | planned | |
| W-AI-03 | "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play | mandate §10 | planned | |
| W-AI-04 | Edit the HUD objective label and rebind it to the quest stage name | mandate §10 | planned | |
| W-AI-05 | Change the lantern quest to require two oil flasks; verify consequences in Play | mandate §10 | planned | |
| W-AI-06 | Undo/redo the above, close and reopen the project, verify consistency | mandate §10 | planned | |
| W-AI-07 | 3D generation request: `not_configured`/blocked surfaced honestly | SADR-020 | planned | |

### Plugin library (W-PLUG)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-PLUG-01 | Three-region loop, moved NPC stays, memory baseline, timings | SR-6.1, B-REGION | planned | |
| W-PLUG-02 | Despawn/respawn keeps override; Animator bound | SR-6.2 | planned | |
| W-PLUG-03 | Walk, jump a ledge, focus prompt, interact dispatch | SR-6.3 | planned | |
| W-PLUG-04 | Patrol index across unload/reload and save/load | SR-6.4 | planned | |
| W-PLUG-05 | Locked door with key; explain refusal | SR-6.5 | planned | |
| W-PLUG-06 | Conditional choice and persisted fact | SR-6.6 | planned | |
| W-PLUG-07 | Quest branches; failure closes dependents | SR-6.7 | planned | |
| W-PLUG-08 | Take spam + reload: exactly one lantern | SR-6.8 | planned | |
| W-PLUG-09 | "Why didn't the gate open" trace | SR-6.9 | planned | |
| W-PLUG-10 | Definition validator parity (inspector, validator, agent) | SR-5.2 | planned | |
| W-PLUG-11 | Ambience crossfade, voice line, clip release | SR-6.11 | planned | |
| W-PLUG-12 | Bake byte-identity; one line change → one revision change | SR-5.3 | planned | |

### Persistence and recovery (W-PERSIST, W-REC, W-MECH, W-KERNEL)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-KERNEL-01 | Corrupted catalog → named failure, not an empty world | SR-7.1 | planned | |
| W-PERSIST-01 | Save in the Ruin with items; load: all restored; region re-entered | SR-6.12 | planned | |
| W-PERSIST-02 | Rename prefab, re-run: NPC keeps state in a save | SR-2.1 | planned | |
| W-PERSIST-03 | Schema version bump with migration: restored; without: actionable refusal | SR-6.12 | planned | |
| W-REC-01 | Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback | SR-3.3 | planned | |
| W-REC-03 | Cancel region load mid-way; cancel staging job | SR-9.1 | planned | |
| W-MECH-01 | Pressure-plate mechanism: staged, admitted, world resumed from checkpoint | SR-3.6, SR-9.3, B-STAGE | planned | |

### Game, build, clean project, docs (W-GAME, W-CLEAN, W-DOC, W-E2E)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-GAME-01 | 10-minute graphical playthrough recording with frame log | SR-8.1, B-FRAME | planned | |
| W-GAME-05 | Full flow menu→save→load→ending→restart in the player | SR-6.10 | planned | |
| W-GAME-06 | Build log + sha256 + V1 gate transcript on the same revision | SR-11.3 | planned | |
| W-GAME-07 | Player runs with etosd stopped and no network | SR-4.10 | planned | |
| W-GAME-08 | Memory after 10 Play/Edit cycles | B-MEMORY | planned | |
| W-CLEAN-01 | Clean project: install, author, run, build | SR-7.2 | planned | |
| W-CLEAN-02 | Kernel diff empty after the clean exercise | SR-7.3 | planned | |
| W-DOC-01 | New user adds an NPC with dialogue from the creator guide | SR-12.1 | planned | |
| W-DOC-02 | Developer adds a lever interactable from the plugin guide | SR-12.2 | planned | |
| W-E2E-01 | All rows resolved on one revision; completion report | SR-12.3 | planned | |

## 4. Supported platform/toolchain matrix (to be confirmed by evidence)

| Component | Pinned | Evidence row |
|---|---|---|
| Unity Editor | 6000.0.75f1, Linux x86_64, interactive on X11 (OpenGL Core) | W-UI-01 |
| Player | StandaloneLinux64 IL2CPP, URP, graphical (RTX 4060 Ti, driver 595.84) | W-GAME-01 |
| Convenience build | StandaloneOSX Mono (unqualified) | reported only |
| etos | `6c2c3f4` + SADR-005 patch, Rust 1.97.1, Docker 29 | W-HOST-01 |
| Providers | Echo (chat, images), DashScope (realtime, TTS) | W-HOST-01 |
| .NET | 8.0.425 | W-TOOL-02 |
