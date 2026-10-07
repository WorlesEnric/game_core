# GameCore Studio: verification matrix and budgets

**Status:** P4.2k (2026-10-08; execution receipts 2026-10-07 UTC): **60 PASS / 6 BLOCKED / 2 FAIL, 68 rows**. Every row is freshly judged at product `7a7ff0c0e5ec2332f360f521ff0467390d063491` and immutable installed companion `0.1.0-3475150b9571123a`; no historical PASS is carried forward. B-FRAME uses VSync OFF; VSync ON is informational. W-VOICE-01 uses SR-4.8 and explicit Send, not mandatory partial-revision visibility. See [P4.2k](packets/P4.2k-same-revision.md) for exact non-PASS causes, lane outcomes and paid accounting.

## 1. Evidence conventions

`artifacts/studio/<area>/<row-id>/` holds: `README.md` (what was run, revision sha, host, date, result),
transcripts, screenshots or recordings (`.png`/`.mp4` under 20 MB, otherwise an external path with sha256), and
machine-readable results where they exist (`result.json`). The retained [verification runner](../../artifacts/studio/verification/TOOLS/verify.py) and row receipts preserve source revisions and results; the originally planned `tools/studio/check_studio_evidence.py` was not delivered under that name.

<a id="2-budgets"></a>

## 2. Budgets (local latency separated from model/network latency)

| ID | Budget | How measured | Pass rule |
|---|---|---|---|
| B-FRAME | Graphical Linux player, 1080p, RTX 4060 Ti: p95 frame time ≤ 16.7 ms over a 10-minute playthrough; no frame > 100 ms outside the first second after a region transition; transition hitch ≤ 250 ms | in-game frame logger (`-frameLog`); **current P4.2k PASS, VSync OFF**: [evidence](../../artifacts/studio/verification/W-GAME-01/README.md), product `7a7ff0c0`, p95 **3.502 / 3.565 ms**, outside-transition >100 ms **0 / 0**, maximum transition **124.909 ms**. Neither run qualified for the permitted rerun; none repeated. ON p95 **17.331 / 17.315 ms** is informational only under SADR-054. Capture overhead is included. | both conditions, 1 of 2 runs may be re-run once |
| B-SELECT | Hover/click resolution ≤ 16 ms; marquee over 500 candidates ≤ 50 ms | `Stopwatch` in the picking service, Editor log | p95 over 100 picks and 100 marquee queries, twice; [CORE-PICK evidence](../../Packages/com.gamecore.studio.core/Tests/CORE-PICK/PACKET.md) |
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
| W-UI-01 | Open Hollowmere in Studio; Play; walk; Select; click NPC; card shows definition | SR-1.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-UI-01/README.md): Current-run Open Hollowmere in Studio; Play; walk; Select; click NPC; card shows definition is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-UI-02 | Box-select three NPCs behind a fence; overlap list; choose NPCs | SR-1.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-UI-02/README.md): Current-run Box-select three NPCs behind a fence; overlap list; choose NPCs is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-UI-03 | Subpart vs logical vs prefab vs scope choice on a lantern | SR-1.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-UI-03/README.md): Current-run Subpart vs logical vs prefab vs scope choice on a lantern is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-UI-04 | Pump count exactly one per frame with the viewport open; Play Mode enter/exit 10× | SR-8.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-UI-04/README.md): Ten current graphical Play/Edit cycles pass one-pump checks. Combined native/managed memory growth is 1.8041%, below the unchanged 15% limit. Full cycle-1/10 snapshots are losslessly retained. |
| W-UI-05 | Selection/hover and marquee timings | B-SELECT | exercised | [PASS evidence](../../artifacts/studio/verification/W-UI-05/README.md): Two current datasets contain 100 picks and 100 marquees over 500 candidates. Pick p95 is 1.499/0.881ms; marquee p95 0.4756/0.2362ms. Unchanged limits are 16/50ms. |

### Studio views (W-VIEW)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-VIEW-01 | Relationships: Selection/search neighbourhood, labelled references, impact, navigation and JSON/Mermaid export | P2.3 view definition | exercised | [PASS evidence](../../artifacts/studio/verification/W-VIEW-01/README.md): Current-run Relationships: Selection/search neighbourhood, labelled references, impact, navigation and JSON/Mermaid export is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-VIEW-02 | Dialogue: Graph tools, conditions, preview, journaled edits/undo and real-Play bridge | P2.3 view definition | exercised | [PASS evidence](../../artifacts/studio/verification/W-VIEW-02/README.md): Current-run Dialogue: Graph tools, conditions, preview, journaled edits/undo and real-Play bridge is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-VIEW-03 | Quests: Stage/objective graph, branches, reward/rule links, simulation and live state | P2.3 view definition | exercised | [PASS evidence](../../artifacts/studio/verification/W-VIEW-03/README.md): Current-run Quests: Stage/objective graph, branches, reward/rule links, simulation and live state is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-VIEW-04 | World: Regions, portals, residency, spawn points and journaled world tools | P2.3 view definition | exercised | [PASS evidence](../../artifacts/studio/verification/W-VIEW-04/README.md): Current-run World: Regions, portals, residency, spawn points and journaled world tools is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-VIEW-05 | Tables: Typed inline edits, bulk AllOrNothing edits, filtering and CSV | P2.3 view definition | exercised | [PASS evidence](../../artifacts/studio/verification/W-VIEW-05/README.md): Current-run Tables: Typed inline edits, bulk AllOrNothing edits, filtering and CSV is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-VIEW-06 | Changes: Pending operations, diagnostics, dependencies and journal inspection | P2.3 view definition | exercised | [PASS evidence](../../artifacts/studio/verification/W-VIEW-06/README.md): Current-run Changes: Pending operations, diagnostics, dependencies and journal inspection is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |

### Edit execution (W-EDIT, W-MODEL, W-TOOL)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-MODEL-02 | Delete an item asset; index lists every referencing dialogue line and objective | SR-2.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-MODEL-02/README.md): Current-run Delete an item asset; index lists every referencing dialogue line and objective is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-TOOL-01 | Tool catalog of the clean project lists only installed plugins' tools | SR-2.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-TOOL-01/README.md): Current-run Tool catalog of the clean project lists only installed plugins' tools is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-TOOL-02 | `check_package_metadata.py` passes with all new packages | SR-11.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-TOOL-02/README.md): Current-run `check_package_metadata.py` passes with all new packages is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-01 | Change set in History shows etos task id and GameCore operation ids | SR-3.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-01/README.md): Current-run Change set in History shows etos task id and GameCore operation ids is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-02 | 5-op change set with one stale op: AllOrNothing rollback and BestEffort partial report | SR-3.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-02/README.md): Current-run 5-op change set with one stale op: AllOrNothing rollback and BestEffort partial report is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-03 | Generate portrait → apply → undo → redo: no second generation (usage unchanged) | SR-3.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-03/README.md): One current image generated/imported through the production media service; normal History undo/redo restored identical retained bytes with unchanged companion charge ledger; cleanup used normal History undo. Visual review: Generated, applied and redone pixels show the same brown-haired healer portrait with herbs and green-brown cloth. History screenshots are clipped by overlapping windows and do not visibly prove each entry state; normal panel receipts, retained journals, exact image hashes and unchanged ledger checkpoints establish replay. No readable-History-screenshot claim. Initial attempts stopped before generation because the ledger observer was absent; both are retained, and the same unused reservations were subsequently exercised with observers. |
| W-EDIT-04 | Edit a running NPC, exit Play, apply; delete it, apply: `StaleTarget` | SR-1.4 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-04/README.md): Current-run Edit a running NPC, exit Play, apply; delete it, apply: `StaleTarget` is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-05 | Gizmo drag and typed value produce identical History entries | SR-1.6, SR-3.4 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-05/README.md): Current-run Gizmo drag and typed value produce identical History entries is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-06 | Runtime-only move → "Apply to authored" → persists after exiting Play | SR-1.8 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-06/README.md): Current-run Runtime-only move → "Apply to authored" → persists after exiting Play is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-07 | Concurrent manual rename vs agent candidate: per-op `Conflict`, rebase works | SR-3.5 | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-07/README.md): Current-run Concurrent manual rename vs agent candidate: per-op `Conflict`, rebase works is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-EDIT-08 | Apply timings (single and region-wide) | B-APPLY, B-COMPOSE | exercised | [PASS evidence](../../artifacts/studio/verification/W-EDIT-08/README.md): Two current 20-sample datasets pass: single-target p95 25.926/24.6934ms; Marsh-all-NPC p95 43.5722/35.9275ms; kernel prepare p95 9.8924/7.0972ms. Limits remain 200/1000/300ms. |

### ETOS and AI (W-ETOS, W-AI, W-VOICE)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-HOST-01 | `install.sh` fresh, then no-op; `verify.sh` with real image/describe/tts/realtime calls | SR-11.2 | blocked(prereq) | [BLOCKED evidence](../../artifacts/studio/verification/W-HOST-01/README.md): Owner rule: never stop/restart etosd; fresh node installation prerequisite is forbidden. |
| W-ETOS-01 | Pair; grep repo + Library for `etk_`: zero hits; logs redacted | SR-4.1 | blocked(prereq) | [BLOCKED evidence](../../artifacts/studio/verification/W-ETOS-01/README.md): Owner rule: credential handling is forbidden; pairing and credential-file inspection are not run. |
| W-ETOS-02 | Unity key against another agent: `forbidden`; shown in Studio log | SR-4.2 | blocked(prereq) | [BLOCKED evidence](../../artifacts/studio/verification/W-ETOS-02/README.md): Owner rule: credential handling is forbidden; cross-agent credential probe is not run. |
| W-ETOS-04 | Worker `etos query` returns the selected NPC's dialogue nodes | SR-2.4, SR-4.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-ETOS-04/README.md): Actual installed worker executes an owner-scoped query and returns all eight current Bram nodes with matching indices/kinds/texts. Initial observer rejected local variable cmd and colon-delimited output; unchanged independent trace and receipt pass the corrected observer without another worker call. |
| W-ETOS-05 | Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task | SR-4.4 | exercised | [PASS evidence](../../artifacts/studio/verification/W-ETOS-05/README.md): Current-run Cancel a running task from the tray: `cancelled`, no candidate, no duplicate task is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-ETOS-06 | Kill companion mid-task; restart; same task id resumes; one outcome; kill etosd mid-task: delayed completion attributed correctly | SR-4.5, SR-9.2 | blocked(prereq) | [BLOCKED evidence](../../artifacts/studio/verification/W-ETOS-06/README.md): Companion-only portion PASS: permitted restart preserves the original task and cursor replay with one cancellation outcome. Owner rule: never stop/restart etosd; node-death/reconnect portion is forbidden and not run. |
| W-ETOS-07 | Generated texture arrives with matching sha256; tampered file refused | SR-4.6 | exercised | [PASS evidence](../../artifacts/studio/verification/W-ETOS-07/README.md): Current-run Generated texture arrives with matching sha256; tampered file refused is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-ETOS-08 | Remove image provider: `not_configured` surfaced; nothing applied | SR-4.9 | blocked(prereq) | [BLOCKED evidence](../../artifacts/studio/verification/W-ETOS-08/README.md): Owner rule: shared-provider removal is forbidden; removing the shared image provider and reloading the node are not run. |
| W-ETOS-09 | Domain reload during a task: tray shows the same task afterwards | SR-8.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-ETOS-09/README.md): Current-run Domain reload during a task: tray shows the same task afterwards is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-VOICE-01 | Speak a destructive command without sending: nothing happens; final transcript appears; partial results never commit; final text becomes a prompt only on explicit Send (restated per SR-4.8 by owner decision 2026-10-07) | SR-4.8 | exercised | [PASS evidence](../../artifacts/studio/verification/W-VOICE-01/README.md): Current-run Speak a destructive command without sending: nothing happens; final transcript appears; partial results never commit; final text becomes a prompt only on explicit Send (restated per SR-4.8 by owner decision 2026-10-07) is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-AI-01 | Select the healer; "give her a green robe": texture generated, material updated, behaviour unchanged; undo restores | mandate §10 | exercised | [PASS evidence](../../artifacts/studio/verification/W-AI-01/README.md): One real green cloth image passed through entity.setMaterialTexture; the real Hollowmere view used it in the body material slot, behaviour inputs and runtime profile stayed equal, and normal History undo restored the authored binding and rendered material. No tint substitute or direct binding call. Visual review: Generated pixels show green woven cloth. Actual Maren body is visibly olive before, dark green with coarse weave after application, and olive again after normal Undo. All three viewport captures frame the complete body; Studio captures identify the workflow. Capsule geometry and distant magenta geometry are unchanged, not improved. Initial attempts stopped before generation because the ledger observer was absent; both are retained, and the same unused reservations were subsequently exercised with observers. |
| W-AI-02 | Point at a location; "add a ferryman NPC here who talks about the bell": NPC spawned with dialogue, patrol, nav | mandate §10 | failed | [FAIL evidence](../../artifacts/studio/verification/W-AI-02/README.md): Fresh unchanged installed-worker Ferryman candidate is refused before apply: GP-LOG-002, FerrymanElian is listed twice in Assets/Hollowmere/Rules/HollowmereContent.asset. No NPC Play/nav/dialogue or successful admission is claimed; original roster remains 20. Candidate is not repaired or regenerated. |
| W-AI-03 | "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play | mandate §10 | exercised | [PASS evidence](../../artifacts/studio/verification/W-AI-03/README.md): Current-run "Add a line Odd only says after the shrine is lit": conditional dialogue works in Play is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-AI-04 | Edit the HUD objective label and rebind it to the quest stage name | mandate §10 | exercised | [PASS evidence](../../artifacts/studio/verification/W-AI-04/README.md): Current-run Edit the HUD objective label and rebind it to the quest stage name is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-AI-05 | Change the lantern quest to require two oil flasks; verify consequences in Play | mandate §10 | exercised | [PASS evidence](../../artifacts/studio/verification/W-AI-05/README.md): Current-run Change the lantern quest to require two oil flasks; verify consequences in Play is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-AI-06 | Undo/redo the above, close and reopen the project, verify consistency | mandate §10 | exercised | [PASS evidence](../../artifacts/studio/verification/W-AI-06/README.md): Current-run Undo/redo the above, close and reopen the project, verify consistency is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-AI-07 | 3D generation request: `not_configured`/blocked surfaced honestly | SADR-020 | exercised | [PASS evidence](../../artifacts/studio/verification/W-AI-07/README.md): Current-run 3D generation request: `not_configured`/blocked surfaced honestly is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |

### Plugin library (W-PLUG)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-PLUG-01 | Three-region loop, moved NPC stays, memory baseline, timings | SR-6.1, B-REGION | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-01/README.md): Current-run Three-region loop, moved NPC stays, memory baseline, timings is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-02 | Despawn/respawn keeps override; Animator bound | SR-6.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-02/README.md): Current-run Despawn/respawn keeps override; Animator bound is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-03 | Walk, jump a ledge, focus prompt, interact dispatch | SR-6.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-03/README.md): Current-run Walk, jump a ledge, focus prompt, interact dispatch is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-04 | Patrol index across unload/reload and save/load | SR-6.4 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-04/README.md): Current-run Patrol index across unload/reload and save/load is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-05 | Locked door with key; explain refusal | SR-6.5 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-05/README.md): Current-run Locked door with key; explain refusal is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-06 | Conditional choice and persisted fact | SR-6.6 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-06/README.md): Current-run Conditional choice and persisted fact is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-07 | Quest branches; failure closes dependents | SR-6.7 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-07/README.md): Current-run Quest branches; failure closes dependents is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-08 | Take spam + reload: exactly one lantern | SR-6.8 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-08/README.md): Current-run Take spam + reload: exactly one lantern is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-09 | "Why didn't the gate open" trace | SR-6.9 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-09/README.md): Current-run "Why didn't the gate open" trace is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-10 | Definition validator parity (inspector, validator, agent) | SR-5.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-10/README.md): Current-run Definition validator parity (inspector, validator, agent) is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PLUG-11 | Ambience crossfade, voice line, clip release | SR-6.11 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-11/README.md): Current native crossfade/release and actual Maren voice cases pass. Current lifecycle movie passes the unchanged normalized audio-identity metric using marker-derived ten-second windows, authored voice/Village/Marsh samples and wrong-region controls. No audio-search-selected windows or human-listening claim. |
| W-PLUG-12 | Bake byte-identity; one line change → one revision change | SR-5.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PLUG-12/README.md): Current-run Bake byte-identity; one line change → one revision change is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |

### Persistence and recovery (W-PERSIST, W-REC, W-MECH, W-KERNEL)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-KERNEL-01 | Corrupted catalog → named failure, not an empty world | SR-7.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-KERNEL-01/README.md): Current-run Corrupted catalog → named failure, not an empty world is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PERSIST-01 | Save in the Ruin with items; load: all restored; region re-entered | SR-6.12 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PERSIST-01/README.md): Current-run Save in the Ruin with items; load: all restored; region re-entered is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PERSIST-02 | Rename prefab, re-run: NPC keeps state in a save | SR-2.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PERSIST-02/README.md): Current-run Rename prefab, re-run: NPC keeps state in a save is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-PERSIST-03 | Schema version bump with migration: restored; without: actionable refusal | SR-6.12 | exercised | [PASS evidence](../../artifacts/studio/verification/W-PERSIST-03/README.md): Current-run Schema version bump with migration: restored; without: actionable refusal is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-REC-01 | Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback | SR-3.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-REC-01/README.md): Current-run Kill editor mid-apply; reopen: journal `Interrupted`, resume/rollback is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-REC-03 | Cancel region load mid-way; cancel staging job | SR-9.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-REC-03/README.md): Current-run Cancel region load mid-way; cancel staging job is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-MECH-01 | Pressure-plate mechanism: staged, admitted, world resumed from checkpoint | SR-3.6, SR-9.3, B-STAGE | exercised | [PASS evidence](../../artifacts/studio/verification/W-MECH-01/README.md): Current signed Docker stages pass in 163.483s cold and 81.506s warm. Creator admissions restore Play in 46.292s and 44.906s, preserve nine coins, pass 120-step Pending-to-Passed smoke and verified Undo. Initial cache_invalid prerequisite refusal and stale-harness revision assertion failures are retained; neither authorized admission. |

### Game, build, clean project, docs (W-GAME, W-CLEAN, W-DOC, W-E2E)
| Row | Scenario | Requirement | Status | Evidence |
|---|---|---|---|---|
| W-GAME-01 | 10-minute graphical playthrough recording with frame log | SR-8.1, B-FRAME | exercised | [PASS evidence](../../artifacts/studio/verification/W-GAME-01/README.md): Current 1080p RTX4060Ti VSync OFF runs both pass: p95 3.502/3.565ms over 607.410/611.724s, zero outside-transition frames over 100ms and maximum transition 124.909ms. Neither run is eligible for a rerun. VSync ON p95 17.331/17.315ms is informational only. All four actual game-window movies are retained. |
| W-GAME-05 | Full flow menu→save→load→ending→restart in the player | SR-6.10 | exercised | [PASS evidence](../../artifacts/studio/verification/W-GAME-05/README.md): Current release IL2CPP player completes menu/save/UI Quit, then separate-process load/Ending C/Play Again. Reviewed owned-window keyframes show both menus, Save UI and ending. The 320.3s movie explicitly concatenates two actual process segments. |
| W-GAME-06 | Build log + sha256 + V1 gate transcript on the same revision | SR-11.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-GAME-06/README.md): Current-source release Hollowmere Linux IL2CPP build passes with executable/full-file hashes. Complete unchanged V1 gate passes 1318 EditMode and 84 PlayMode XML cases, codegen byte identity, qualification/release builds, prescribed probes and docs gates. B-FRAME is independently measured and passes under VSync OFF. |
| W-GAME-07 | Player runs with etosd stopped and no network | SR-4.10 | blocked(prereq) | [BLOCKED evidence](../../artifacts/studio/verification/W-GAME-07/README.md): Owner rule: never stop/restart etosd; stopped-node/no-network player scenario is not run. |
| W-GAME-08 | Memory after 10 Play/Edit cycles | B-MEMORY | exercised | [PASS evidence](../../artifacts/studio/verification/W-GAME-08/README.md): Ten current graphical Play/Edit cycles pass one-pump checks. Combined native/managed memory growth is 1.8041%, below the unchanged 15% limit. Full cycle-1/10 snapshots are losslessly retained. |
| W-CLEAN-01 | Clean project: install, author, run, build | SR-7.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-CLEAN-01/README.md): Current clean-project author/bake, 11 EditMode and three PlayMode cases, both rechecks, Linux IL2CPP build and standalone 600-frame quest/save/restore/ending autoplay pass with zero pump violations. |
| W-CLEAN-02 | Kernel diff empty after the clean exercise | SR-7.3 | exercised | [PASS evidence](../../artifacts/studio/verification/W-CLEAN-02/README.md): After all clean-project/build/player exercises and owned-fixture cleanup, literal git diff --exit-code origin/main -- Packages/ exits zero with empty output. No kernel/package source changed. |
| W-DOC-01 | New user adds an NPC with dialogue from the creator guide | SR-12.1 | exercised | [PASS evidence](../../artifacts/studio/verification/W-DOC-01/README.md): Current-run New user adds an NPC with dialogue from the creator guide is verified by the linked exact XML cases and/or current JSON assertions; no historical PASS is used. |
| W-DOC-02 | Developer adds a lever interactable from the plugin guide | SR-12.2 | exercised | [PASS evidence](../../artifacts/studio/verification/W-DOC-02/README.md): Literal current guide export/client-submit and signed Docker stage pass. Creator Admit completes in 32.253s within 90s, restores nine coins and checkpoint round-trip equality, passes 120-frame smoke, and actual Toggle lever control commits 0/1/0 on normal frames. Undo removes the package and restores the original catalog. |
| W-E2E-01 | All rows resolved on one revision; completion report | SR-12.3 | failed | [FAIL evidence](../../artifacts/studio/verification/W-E2E-01/README.md): All 68 rows are freshly judged at one product revision and installed companion release with strict host exclusivity; no historical PASS carries forward. End-to-end acceptance is FAIL because current W-AI-02 refuses the unchanged Ferryman candidate with GP-LOG-002 duplicate enrollment. Six exact owner-forbidden scenarios remain BLOCKED. |

## 4. Supported platform/toolchain matrix (to be confirmed by evidence)

| Component | Pinned | Evidence row |
|---|---|---|
| Unity Editor | 6000.0.75f1, Linux x86_64, interactive on X11 (OpenGL Core) | W-UI-01 |
| Player | StandaloneLinux64 IL2CPP, URP, graphical (RTX 4060 Ti, driver 595.84) | W-GAME-01 |
| Convenience build | StandaloneOSX Mono (unqualified) | reported only |
| etos | etos main ≥ e4067fd (contains 278ef9c), Rust 1.97.1, Docker 29 | W-HOST-01 |
| Providers | Echo (chat, images), DashScope (realtime, TTS) | W-HOST-01 |
| .NET | 8.0.425 | W-TOOL-02 |

## 5. B-FRAME and recording clarification

| Evidence profile | Observation | Disposition |
|---|---|---|
| P3.1 recording | Predates P3.1b fixes; retains its original failing frame evidence. | W-GAME-01 recording remains BLOCKED for final-tree qualification. |
| P3.1b `406f00c1`, offscreen llvmpipe | p95 1.284 ms; 0 frames >100 ms; belfry transition 7.4 ms. | Headless improvement, not real-GPU qualification. |
| P3.1d `f67a6de3`, RTX 4060 Ti 1080p, VSync OFF | Two runs p95 2.778 / 2.856 ms, 0 frames >100 ms. | PASS against literal rule. |
| Same, VSync ON | Two runs p95 18.062 / 18.089 ms (60 Hz lock interval plus jitter), 0 frames >100 ms. | FAIL against literal 16.7 ms rule. |

([P3.1b note including §P3.1d](packets/P3.1b-frame-time.md), [W-GAME-01](../../artifacts/studio/verification/W-GAME-01/README.md))

P3.1d closed the historical missing real-GPU measurement; it did not replace the old recording. **DECIDED 2026-10-07 (owner, SADR-054):** B-FRAME is measured with VSync OFF; VSync ON results are informational. P4.2k supplies the current same-revision W-GAME-01 evidence below; P4.2h/P4.2i/P4.2j measurements retain their original provenance. There is no W-REC recording row: W-REC-01 is crash recovery and W-REC-03 cancellation; recording maps to W-GAME-01.

### Historical P4.2h recording update

The historical missing-recording condition was closed by four actual 1080p NVIDIA recordings from product `a77cb38b`; [recording manifests and metrics](../../artifacts/studio/verification/W-GAME-01/p42h-player/README.md) retain their exact hashes and capture overhead. The owner subsequently selected VSync OFF; these measurements remain historical, not P4.2i acceptance evidence.

| Profile | Steady duration, two runs | p95 frame ms | >100 ms outside transition windows | Literal B-FRAME |
|---|---|---|---|---|
| VSync OFF | 611.395 / 609.205 s | 3.461 / 3.552 | 0 / 0 | PASS / PASS |
| VSync ON | 608.505 / 609.438 s | 17.375 / 17.314 | 2 / 0 | FAIL / FAIL |

All four routes, saves and actual recordings pass their separate checks. Maximum transition hitch is 122.590 ms, below 250 ms. No average across VSync states, revised p95 limit or claim of all-row same-revision acceptance is made.

### P4.2i same-revision recordings

Four fresh actual game-window recordings use product `cb5e2aa20263209df2dea4ee17aa23c50daec0e0` and the one installed companion release `0.1.0-debdab3072dbe1f8`. [Metrics and build record](../../artifacts/studio/verification/W-GAME-01/p42i-player/result.json), [large-file hashes](../../artifacts/studio/verification/W-GAME-01/p42i-player/external-artifacts.json).

| Profile | Steady seconds | p95 ms | Outside-transition >100 ms | Disposition |
|---|---|---|---|---|
| VSync OFF | 607.729 / 608.971 | 3.404 / 3.463 | 0 / 0 | PASS / PASS |
| VSync ON | 608.314 / 608.274 | 17.230 / 17.261 | 0 / 0 | Informational; literal p95 FAIL / FAIL |

Maximum transition hitch is 123.080 ms; all four route/save/recording checks pass. No VSync averaging or budget relaxation is used. W-VOICE-01 was also rerun under SR-4.8: destructive final text stays visible and unsent, with unchanged task/request/journal counts; partial-revision visibility is not required. Original P4.2i 68-row result: **60 PASS / 6 BLOCKED / 2 FAIL**; [packet](packets/P4.2i-same-revision.md). The subsequent R9 mixed-revision 60/6/2 registry was not a P4.2i same-revision result.

### P4.2j same-revision recordings

Current product `389cf038a7386dbcc5b2b52ad31744d8747e76f4`, immutable installed companion `0.1.0-fba3604e99ceadd1`. [Current measurements](../../artifacts/studio/verification/W-GAME-01/p42j-player/result.json), [exact external movie/build hashes](../../artifacts/studio/verification/W-GAME-01/p42j-player/external-artifacts.json).

| Profile | Steady seconds | p95 ms | Outside-transition >100 ms | Disposition |
|---|---|---|---|---|
| VSync OFF | 612.244 / 612.467 | 3.547 / 3.612 | 3 / 0 | FAIL / PASS |
| VSync ON | 608.373 / 609.328 | 17.380 / 17.266 | 1 / 1 | Informational; literal FAIL / FAIL |

OFF run 1 fails on 374.077, 174.926 and 1352.808 ms stalls outside transition windows; maximum transition hitch is only 118.074 ms, which does not excuse them. The row remains FAIL, with no rerun or budget relaxation. All four actual owned-window recordings remain retained. W-VOICE-01 freshly passes SR-4.8: visible destructive final text remains unsent and tray/request/journal counts stay 1/1/120; no partial-revision visibility condition. Final all-row result: **60 PASS / 6 BLOCKED / 2 FAIL**; [P4.2j packet](packets/P4.2j-same-revision.md).

### P4.2k same-revision recordings

Current product `7a7ff0c0e5ec2332f360f521ff0467390d063491`, immutable installed companion `0.1.0-3475150b9571123a`. [Measurements](../../artifacts/studio/verification/W-GAME-01/p42k-player/result.json), [external movie/build hashes](../../artifacts/studio/verification/W-GAME-01/p42k-player/external-artifacts.json).

| Profile | Steady seconds | p95 ms | Outside-transition >100 ms | Disposition |
|---|---|---|---|---|
| VSync OFF | 607.410 / 611.724 | 3.502 / 3.565 | 0 / 0 | PASS / PASS |
| VSync ON | 608.117 / 608.621 | 17.331 / 17.315 | 0 / 0 | Informational; literal p95 FAIL / FAIL |

Maximum OFF transition hitch is 124.909ms. Neither OFF run meets the owner's rerun trigger, so no frame run is repeated. All four actual 1080p game-window recordings and capture overhead are retained. W-VOICE-01 freshly passes SR-4.8: the destructive final transcript remains visible and unsent, with tray/request/journal counts unchanged at 1/1/120; partial-revision visibility is not required. Final all-row result: **60 PASS / 6 BLOCKED / 2 FAIL**. The fresh Ferryman candidate is refused for duplicate enrollment (W-AI-02), so aggregate W-E2E-01 remains FAIL; [P4.2k packet](packets/P4.2k-same-revision.md).
