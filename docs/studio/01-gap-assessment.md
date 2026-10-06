# GameCore Studio: source-based gap assessment and requirement traceability

**Status:** assessment of 2026-10-04 against game_core `2a325b9` (identical content to `0523bea`) and etos `6c2c3f4`.
Every "current implementation" cell was read in source in this session or by a read-only audit agent whose
citations were spot-checked. Nothing here is a plan; the plan is [06-implementation-plan.md](06-implementation-plan.md).
The requirement IDs (`SR-…`) are stable and are the keys used by the verification matrix
([07-verification-matrix.md](07-verification-matrix.md)) and the completion report
([12-completion-report.md](12-completion-report.md)).

Status vocabulary (one per row): **absent** · **partial** · **implemented-unverified** · **exercised**.
"Exercised" means the delivered revision's workflow was run and its evidence is linked in column *Evidence*.
At assessment time, no row of this product is exercised; the column is filled by the verification owner.

The five distinctions the mandate asks for are applied in every row:
specification ≠ implementation; runtime kernel ≠ editor; reference fixture ≠ reusable system;
generated content ≠ integrated content; configured provider ≠ live provider.

## 0. Findings that shape everything below

| # | Finding | Evidence |
|---|---|---|
| F1 | GameCore V1 is a runtime **protocol** (composition, propagation, publication, checkpoints) with zero editor surface. The only Editor code in the packages is one catalog-generation menu item and a Play Mode exit hook. "Workbench/editor/AI UX" is explicitly deferred with no seams. | `Packages/com.gamecore.content.compiler/Editor/CatalogGeneratorMenu.cs:27-47`; `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationReset.cs:66-99`; `docs/game-core/09-implementation-guide.md:1177`; `docs/operator/deferred-scope.md:24-30` |
| F2 | The three gameplay "families" (cards, narrative, traversal) are **qualification fixtures**, not reusable systems: two hard-coded chapters, two binary facts, two dialogue choices by constant, one runner on a plane, no camera, no UI, no NPC navigation, no items. All narrative ECS systems live in a `Fixtures` assembly. | `Packages/com.gamecore.rules.narrative/Runtime/NarrativeChapters.cs:85,125-136`; `NarrativeDialogueRules.cs:129-142`; `NarrativeFacts.cs:34-50,112`; `Packages/com.gamecore.gameplay.narrative/Fixtures/Runtime/NarrativeWorld.cs:374-888`; `docs/game-core/07-reference-compositions.md:3,17` |
| F3 | No application root exists outside the validation project: lane + publisher + pipeline + bridge are composed only in fixtures and probe scenarios; the bootstrap passes `ContentHash.Empty` and silently falls back to an infrastructure-only world. | `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs:41-56,81-87`; `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/ProbeWorldDispatch.cs:29-33` |
| F4 | Persistence captures only int32 `TargetSlotState` rows plus composition, clocks, RNG, cursors and outbox; the only restore builders are in the validation project and refuse migrations; a restored world restarts at step 0 with a new `WorldId`. There is no player-facing save/load. | `Packages/com.gamecore.unity.runtime/Runtime/Persistence/UnityCommittedBoundaryReader.cs:235-243`; `unity/GameCore.Validation/Assets/GameCore.Validation/Runtime/Gc027RestoreBuilder.cs:258`; `Packages/com.gamecore.unity.runtime/Runtime/WorldHost.cs:184,512` |
| F5 | The composition bridge always uses the committed revision as the expected revision, drains every pending edit, and charges one logical step per edit; a world-side refusal after lane publication leaves the lane and world divergent. There is no multi-edit atomic publication. | `Packages/com.gamecore.unity.runtime/Runtime/Integration/WorldCompositionBridge.cs:266,331,369-395`; `Packages/com.gamecore.composition/Runtime/Operations/CompositionHost.cs:501-594` |
| F6 | There is no Scene, Prefab, ScriptableObject or Baker pipeline; the only "bake" turns JSON into `.g.cs`. The qualification project has one empty scene, no graphical asset, audio disabled, built-in pipeline, legacy input. | `unity/GameCore.Validation/Assets/Scenes/GameCoreProbe.unity`; `ProjectSettings/AudioManager.asset:18`; `ProjectSettings/ProjectSettings.asset:682`; `artifacts/release-readiness/outstanding-defects.md:52` |
| F7 | `tools/check_package_metadata.py` forbids every engine reference except Burst/Collections/Entities/Mathematics for any `com.gamecore.*` package and requires each such package to be locked in the validation project. New packages that use Input System, UI Toolkit, AI Navigation, Transforms or URP cannot pass it unchanged. | `tools/check_package_metadata.py:64-75,110-121,161-186,250-273` |
| F8 | etos v2 (`6c2c3f4`) exposes an authenticated HTTP API under `/api/v1` with **two key kinds**. App keys reach logger, sources, entrances, query, changes, services and proxied agent HTTP. Media ops, tasks (open/get/cancel), files (upload/download with digest), topics, actors and the realtime voice WebSocket require an **agent key** held by an installed agent. There is no OpenAPI document, only a `$defs` JSON Schema. | `crates/etapi/src/http.rs:39-117,167-178,503-704`; `crates/etapi/src/schema.rs:18-65`; `crates/etagents/src/manifest.rs:17-49` |
| F9 | etos entrance conversations deliver text-only progress (structured `data` is dropped), long-poll only, with no app-side cancel and no artifact download for app keys; `Reference` carries no sha256. | `crates/etapi/src/entrance.rs:539-599`; `crates/etnode/src/adapters.rs:718-730`; `crates/etagents/src/files.rs:1-5` |
| F10 | Live providers available to this product (verified by calls on 2026-10-04): Echo (`api.echo-coding.com`, OpenAI-compatible) serves chat models including `claude-opus-5-5`, `gpt-6-sol`, `deepseek-v4-*`, and `images/generations` with `gpt-image-2` (verified, 22 s, b64). Echo has no `/audio/speech` or `/audio/transcriptions` (404). DashScope serves chat, native TTS (`qwen3-tts-flash`, verified) and realtime (`qwen3-omni-flash-realtime`, `qwen3-asr-flash-realtime`); its OpenAI-compatible images/speech/transcriptions routes are 404. etos ships `openai-images`, `openai-speech`, `openai-transcriptions`, `predictions`, `chat` and `bailian-omni` (realtime) provider kinds; there is **no 3D-generation provider credential** available, and no file-based DashScope TTS/ASR provider kind. | session probe transcript (`artifacts/studio/environment/provider-probe-2026-10-04.md`, written in W0); `crates/etops/src/config.rs:178-202`; `crates/etops/src/bailian.rs` |
| F11 | The Linux host has Unity 6000.0.75f1 with a validated Personal entitlement, an RTX 4060 Ti on X display `:1`, OpenGL 4.6, a microphone device, Docker, Rust 1.97.1 (etos's pin). The desktop session was in GNOME's failure screen until the shell was restarted in place; interactive Unity now opens. | host checks 2026-10-04 (recorded in `artifacts/studio/environment/`) |
| F12 | Measured kernel cost: whole-world derivation at 10,000 targets takes 0.87–1.12 s per composition edit against a 100 ms target; at ~1k targets 0.1–0.2 s. One spawn consumes exactly one composition revision. This bounds how many *simulated* targets a region may hold and rules out streaming scenery through GameCore spawns. | `artifacts/performance/BUDGET_DECISIONS.md:77-84`; `Packages/com.gamecore.unity.runtime/Runtime/Assembly/AssemblyPublisher.cs:732-752` |

## 1. Unity editor interaction and authoring

| ID | User-observable behavior | Current implementation (source) | Status | Missing mechanisms / links | Depends on / owner | Acceptance scenario | Evidence |
|---|---|---|---|---|---|---|---|
| SR-1.1 | A Studio viewport shows the running game (Play) or the authored world (Edit) and is the primary place to author: play, select, prompt, review. | None. No `EditorWindow` in any package (F1). | absent | Studio viewport window rendering the game camera; mode switch play/select; input routing that never drives the world twice (F3: one pump). | `com.gamecore.studio.ui`; owner W2-UI | Open the reference game, press Play in the Studio viewport, walk 10 m, press Select, click an NPC, see its card. | W-UI-01 |
| SR-1.2 | Click, hover, box-select and multi-select scene objects, runtime entities, UI elements and spatial regions, with reliable screen→object mapping from engine data. | None. No picking code; views are empty GameObjects (`GameObjectViewBinder.cs:66`). | absent | Picking service: physics raycast + renderer bounds + UI Toolkit panel pick + ground-plane point pick; marquee frustum; candidate list for occlusion/overlap. | `com.gamecore.studio.core` picking; owner W2-UI | Box-select three NPCs behind a fence; the overlap popover lists the fence and the NPCs; choose NPCs only. | W-UI-02 |
| SR-1.3 | Select logical object vs visual sub-part; choose instance vs Prefab/template vs broader scope before editing. | None. | absent | Selection model with `SelectionTarget{authoringRef, scope: Instance/Prefab/Definition/Scope}`; UI affordance. | studio.core + studio.ui | Select a lantern mesh child; the card offers "this lantern", "Lantern prefab", "all lanterns in Marsh". | W-UI-03 |
| SR-1.4 | Runtime selections map back to authored definitions; stale/destroyed/unloaded/changed targets are detected before application. | TargetId↔Entity map exists (`TargetRegistry.cs:32-172`) but no GameObject→TargetId authoring component and Unity instance IDs are session-local (`04-unity-integration.md:144`). | partial (kernel) | `AuthoringRef` (GlobalObjectId + authoring GUID + content stamp); TargetId derived from authoring GUID; precondition checks. | studio.core + `com.gamecore.gameplay.entities`; owner W1-MODEL | Select a running NPC, exit Play, apply the pending edit; it lands on the prefab/instance. Delete the NPC; the edit is refused with `StaleTarget`. | W-EDIT-04 |
| SR-1.5 | Frame/camera context (view matrix, screenshot) is captured with each selection for the agent. | None. | absent | Capture service (RenderTexture readback, camera pose, viewport rect). | studio.ui | Prompt "make this redder" with a selection; the request carries the frame and the agent's answer references the selected id, not the pixel. | W-AI-02 |
| SR-1.6 | Direct manipulation and precise parameter controls exist beside prompting and produce the same change sets. | Standard Unity gizmos/inspectors only. | partial (Unity stock) | Contextual control panel generated from authoring metadata; gizmo drag recorded as a Move operation on release. | studio.ui + studio.core | Drag an NPC 2 m; History shows one "Move" change set with undo; typing 2.0 in the field does the same. | W-EDIT-05 |
| SR-1.7 | Nonvisual views: behavior/interaction relationships, dialogue and quest structure, world/region flow, data tables, changes/dependencies/diagnostics/history, all over the same content. | None. | absent | Views package reading the semantic index and change journal; no second model. | `com.gamecore.studio.views`; W3-VIEWS | Edit a dialogue choice in the graph view; the viewport NPC offers the new line; the data table shows the same node. | W-VIEW-01..05 |
| SR-1.8 | Play/edit/preview semantics are explicit: persistent vs temporary, apply-back, reconstruction needed, compile/build needed. | 04 §9: compile ends the session; Play Mode reset hook exists. | partial (rules only) | Change-set `ApplyRequirement` enum and UI badges; runtime-only edits journal with "apply to authored" action. | studio.core; W1-MODEL | During Play, move a chest (runtime-only), then "Apply to authored"; exit Play; chest is moved in the scene. | W-EDIT-06 |

## 2. Semantic selection and project context

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-2.1 | Persistent authoring identities independent of runtime handles and instance IDs. | Stable ids exist in the kernel (`StableNameKeyDerivation.cs:15-55`); nothing binds them to Unity objects. | partial | `AuthoringId` component + asset GUID mapping; TargetId = derive(authoringId). | gameplay.entities; W1-MODEL | Reopen the project, rename the prefab, re-run; the NPC keeps its quest state in a save. | W-PERSIST-02 |
| SR-2.2 | A semantic index of objects, definitions, references and capabilities that is a traceable projection of Unity assets + GameCore identities. | None. | absent | Index builder (scenes, prefabs, definitions, authoring metadata via reflection), incremental rebuild on asset changes, JSON export with provenance (asset GUID, file, line). | studio.core; W1-MODEL | Delete an item asset; the index reports every dialogue line and quest objective that referenced it. | W-MODEL-02 |
| SR-2.3 | Project capability manifest: which plugin operations/fields/constraints exist, so agents only propose what the project can do. | None. | absent | `[Authorable]` metadata attributes + tool registry export. | studio.core; W1-MODEL | The agent's catalog for the clean project lists only installed plugins' tools. | W-TOOL-01 |
| SR-2.4 | Context delivered to etos: index slices, selection, frame, tool catalog, within bounds; registered in the Resource Graph. | etos logger/query exist for app keys (`crates/etapi/src/http.rs:43-51,89`). Nothing on the game side. | partial (etos side) | Companion agent publishes `gc_*` states via the SDK logger; bounded context packer. | `studio/agent` (Rust); W2-ETOS | A worker's `etos query` returns the selected NPC's dialogue nodes. | W-ETOS-04 |

## 3. Structured edit execution and history

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-3.1 | Typed change sets: identity, intent, selection+scope, base versions, typed ops with dependencies, artifacts, validation scenarios, apply requirement, outcomes, recovery info. | Kernel has `OperationId` (session-scoped, `Handles.cs:44-80`) and composition proposals; no authoring change set. | absent | `ChangeSet` schema (Unity-free C#, JSON), id relationships to GameCore OperationIds and etos task ids. | studio.core; W1-MODEL | Inspect a change set in History; it shows its etos task id and the GameCore operation ids it produced. | W-EDIT-01 |
| SR-3.2 | Preconditions, staging, validation, application, partial-failure reporting. | Composition lane has admission/staging/publication (`CompositionHost.cs:1-17`) for *world* edits only. | partial (kernel world edits) | Edit engine over Unity assets + scene + GameCore; per-op validators; partial failure report with per-op status. | studio.core; W1-MODEL | A 5-op change set with one op targeting a deleted object: 4 applied or none (per policy), report names the failed op and why. | W-EDIT-02 |
| SR-3.3 | Undo/redo that reuses retained artifacts; recovery after editor crash/domain reload. | Unity Undo only. | partial (Unity stock) | Change journal on disk (`<project>/Studio/History`), retained artifact store, inverse ops, Undo group integration, reload-safe state. | studio.core; W1-MODEL | Generate a portrait, apply, undo, redo: no second generation call (etos usage unchanged). Kill the editor mid-apply; reopen; journal shows `Interrupted` and offers resume/rollback. | W-EDIT-03, W-REC-01 |
| SR-3.4 | Manual and agent edits share one edit semantics. | n/a | absent | All inspectors/gizmos/panels route through tools. | studio.ui | Same History entry shape for a typed value and an agent-proposed value. | W-EDIT-05 |
| SR-3.5 | Concurrency: explicit conflict detection, serialized mutation, no global lock during model work. | n/a | absent | Base-version stamps per op; a single-writer apply queue; proposals carry read dependencies. | studio.core | Start an agent task on NPC A, manually rename A's dialogue node, apply the proposal: `Conflict` on the stale op, others apply. | W-EDIT-07 |
| SR-3.6 | Generated code is staged and validated in an isolated project before affecting the editor; no unrestricted script execution. | None (04 §9 forbids hot code). | absent | Staging lane: isolated Unity project copy + dotnet tests + batchmode compile; admission merges package into the project; domain reload with checkpoint continuity. | `studio/stage` + studio.core; W4-MECH | Ask for a new "pressure plate" mechanism; the branch compiles and tests in staging; Admit; the plate works in Play; the world resumes from the pre-reload checkpoint. | W-MECH-01 |

## 4. ETOS integration and AI workflows

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-4.1 | Pairing and authentication through etos's app pairing; no credentials in assets, source, screenshots or logs. | `etos app pair --approve --out` (`crates/etapi/src/apps.rs:483-598`). | implemented-unverified (etos) | Unity reads the key file path from a user-level setting outside the project; redaction in logs. | W2-ETOS | Pair, open the project, grep the repo and `Library` for `etk_`: zero hits. | W-ETOS-01 |
| SR-4.2 | Project-scoped authority: the Unity app key reaches only its own app and the Studio agent. | `AppAuth::allow` (`state.rs:136-165`), `uses` for agents. | implemented-unverified (etos) | Pairing with `routes` and `uses`; refusal surfaced in UI. | W2-ETOS | Call another agent's service with the Unity key: `wrong_app`/`forbidden` shown in the Studio log. | W-ETOS-02 |
| SR-4.3 | Context registration, query and bounded materialization. | Logger/query/`/rg` view exist (`crates/etrg/src/view/mod.rs:40-61`). | implemented-unverified (etos) | gc_* bindings and the publication cadence. | W2-ETOS | see SR-2.4 | W-ETOS-04 |
| SR-4.4 | Task admission, status, events, result retrieval and cancellation with etos's outcome vocabulary preserved (queued/starting/running/waiting/done/failed/cancelled; refusal codes). | `POST /tasks`, `GET /tasks/{id}`, `POST /tasks/{id}/cancel` for agent keys (`crates/etagents/src/tasks.rs`). | implemented-unverified (etos) | Companion agent task desk; Unity task tray; mapping table (no invented states). | W2-ETOS | Cancel a running narrative task from the tray: task shows `cancelled`, no change set is produced, no duplicate task. | W-ETOS-05 |
| SR-4.5 | Reconnection and recovery after lost responses: a transport disconnect never duplicates a submission or fabricates success. | Idempotent `id` on `POST /tasks` (`sdk/rust/README.md` Tasks row); entrance request ids. | implemented-unverified (etos) | Request ledger in the agent (durable), Unity reconnect with `after` cursors. | W2-ETOS | Kill the companion mid-task; restart; the tray resumes the same task id; outcome arrives once. | W-ETOS-06 |
| SR-4.6 | Artifact retrieval and verified import (sha256). | `GET /files/{id}` agent-only; `FileInfo.digest` on uploads; worker `/outputs` become references at close. | partial (etos) | Agent fetches, hashes, stores content-addressed; Unity imports via a verified import pipeline. | W2-ETOS | A generated texture arrives; its sha256 matches the ledger; a tampered file is refused. | W-ETOS-07 |
| SR-4.7 | Generation workflows: assets (image), narrative, gameplay composition, media (TTS voice lines). | Providers exist in etops (F10); none configured; no Studio workers. | partial (configured ≠ live) | `models.toml`/`ops.toml` on Echo + DashScope; worker definitions + instructions; **etos extension**: a file-based DashScope TTS provider kind. | W2-ETOS, W0-HOST | Live calls recorded with task ids and usage. 3D mesh generation: **blocked** (no provider credential); documented. | W-AI-01..06 |
| SR-4.8 | Real microphone voice input via etos realtime; visible transcription; partial results never commit edits. | `bailian-omni` adapter + `/realtime/connect` (agent-only); Rust client. | implemented-unverified (etos) | Unity mic capture → PCM16 → companion WS → etos realtime; transcript revisions shown; only final text becomes a prompt; explicit Apply. | W2-ETOS, W2-UI | Speak "delete the ferryman" without confirming: nothing is deleted; the final transcript appears in the prompt box awaiting Send. | W-VOICE-01 |
| SR-4.9 | Provider failures, unavailable capabilities and usage limits are surfaced, never faked. | etops refusals `not_configured`, `outcome_unknown`, `request_rejected` (`crates/etops/src/error.rs:94-95`). | implemented-unverified (etos) | Pass-through of codes to the tray; no fallback generation. | W2-ETOS | Remove the image provider from `ops.toml`; "generate portrait" shows `not_configured` and the change set stays unapplied. | W-ETOS-08 |
| SR-4.10 | Built games run without the editor or etos. | GameCore player has no network dependency. | implemented-unverified | Keep runtime packages free of Studio/etos references (asmdef check). | W5-GAME | Run the Linux player with etosd stopped and no network: full playthrough. | W-GAME-07 |

## 5. Assets, scenes, Prefabs, UI and content compilation

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-5.1 | Authoring with real Unity scenes and Prefabs; regions are scenes; entities are Prefab instances carrying authoring identity and definition refs. | None (F6). | absent | `AuthoredEntity`, `AuthoredRegion`, `RegionPortal` components; region manifest bake on save; world builder at Play. | gameplay.entities, gameplay.world; W1-PLUGINS | Place a prefab in the Marsh scene, save, Play: it is a GameCore target with the right definition. | W-PLUG-01 |
| SR-5.2 | Definitions (items, NPC archetypes, dialogues, quests, rules, UI) are inspectable assets with metadata and validation. | Kernel `DefinitionRef` only. | absent | ScriptableObject-backed definition assets with `[Authorable]` fields; validators; content hash → `DefinitionRef` revision. | gameplay.*; W1-PLUGINS | Edit an item's weight out of range: inspector + validator diagnostic + agent refusal share the same message. | W-PLUG-08 |
| SR-5.3 | Content compilation: authored assets → GameCore catalog/manifests/spawn plan, deterministic, verifiable. | Catalog compiler from JSON (`CC/README.md:18-114`), definition-level bake. | partial | Authoring→description-document emitter; generated catalog for the game; byte-identity check reused. | gameplay.compile; W1-PLUGINS | Bake twice: byte-identical outputs; change one dialogue line: only the definition revision changes. | W-PLUG-09 |
| SR-5.4 | Generated assets are imported, assigned and bound (textures, sprites, audio) through the same change sets. | None. | absent | Verified import pipeline + `AssignAsset` ops. | studio.core; W2-ETOS | "give the healer a green robe": new texture imported, material updated, undo restores the old material. | W-AI-01 |
| SR-5.5 | Runtime UI is data-bound to committed state and commands (not visuals only). | No UI at all. | absent | UI Toolkit runtime documents + binding layer over the committed image and command ingress. | gameplay.ui; W1-PLUGINS | Change the HUD health label's binding to stamina via the agent; it shows stamina in Play. | W-AI-04 |

## 6. Required gameplay plugin library

| ID | Capability group | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-6.1 | World and regions: connected regions, real load/unload, spawn/transition points, ownership, persistent identity/state across transitions, residency and cancellation. | Scopes are names (`TraversalVocabulary.cs:42-57`); no SceneManager use. | absent | `com.gamecore.gameplay.world`: region scopes, residency capability, portals, additive scene streaming with cancellation, logical entities persist while views unload (F12 design). | W1-PLUGINS | Walk Village→Marsh→Ruin→Village: scenes unload/load; an NPC moved before leaving is where it was; memory returns to baseline ±10 %. | W-PLUG-01 |
| SR-6.2 | Entities and presentation: definitions + Prefab binding, spawn/despawn, instance overrides, visual/animation/audio/interaction bindings. | Empty-GameObject views (`GameObjectViewBinder.cs:66`); Transform-only animation sink. | partial (adapter seam) | `com.gamecore.gameplay.entities`: entity definitions, prefab binder, override layer, Animator/AudioSource binders. | W1-PLUGINS | Despawn and respawn an entity by command; its override (scale 1.2) is kept; the Animator plays the bound idle. | W-PLUG-02 |
| SR-6.3 | Player exploration: 3D locomotion, camera, collision, input; interaction targeting, focus, prompts, dispatch. | Integer kinematics on a plane (`TraversalMotionRules.cs:291-349`); `Input.GetKey`. | fixture-only | `com.gamecore.gameplay.player`: CharacterController locomotion, third-person camera, Input System actions, interaction focus ray, prompt, command dispatch to GameCore. | W1-PLUGINS | Walk, jump over a 0.4 m ledge, look at a door: prompt "Open"; press E: door command admitted. | W-PLUG-03 |
| SR-6.4 | NPCs: placement, identity, config, navigation, idle/patrol/interaction, state-dependent behavior, correct through unload/reload. | Names only (`NarrativeCompositionNames.cs:55-66`). | absent | `com.gamecore.gameplay.npc`: NavMeshAgent binding, behavior definitions (idle, patrol, approach, converse), logical state in slots, view suspension on unload. | W1-PLUGINS | A patrolling NPC keeps its patrol index across region unload/reload and after save/load. | W-PLUG-04 |
| SR-6.5 | Interaction system: click/use/proximity, conditions, typed actions, reusable triggers, feedback; doors, gates, examinables, interaction points. | Boolean gate (`NarrativeGateRules.cs:19-27`). | fixture-only | `com.gamecore.gameplay.interaction`: interactable definitions with condition/action graphs, trigger volumes, feedback bindings. | W1-PLUGINS | Locked door opens only with the key item; the trace explains the refusal when it does not. | W-PLUG-05 |
| SR-6.6 | Dialogue and narrative: branching, conditions, choices, consequences; speaker/content/voice/subtitle bindings; interruption; persistent facts. | Two constant choices (`NarrativeDialogueRules.cs:129-142`). | fixture-only | `com.gamecore.gameplay.dialogue`: dialogue graph definition, runtime conversation state in slots, condition/consequence ops, voice clip + subtitle bindings, interruption. | W1-PLUGINS | A choice is shown only when quest stage ≥ 2; picking it sets a fact; reload: the fact persists and the line is gone. | W-PLUG-06 |
| SR-6.7 | Quests: objectives, stages, dependencies, branches, fail/complete, rewards, persistent consequences, authoring and inspection. | Two binary facts; one hard-coded reward (`RewardRule.cs:183-194`). | fixture-only | `com.gamecore.gameplay.quest`: quest definitions, stage machine, objective counters in slots, reward grants via outbox (reusing the exactly-once pattern of `gameplay.integration`), journal queries. | W1-PLUGINS | Quest branch A vs B leads to different world state; failing an objective marks the quest failed and closes dependents. | W-PLUG-07 |
| SR-6.8 | Items and economy: definitions, inventory, grants, consumption, interaction bindings; no duplication through repeated commands, reload or recovery. | Card buffers; no grant command (`RewardRule.cs:37-41`). | partial (outbox idempotency reusable) | `com.gamecore.gameplay.inventory`: item definitions, inventory slots, grant/consume commands with request ids, trade/reward paths. | W1-PLUGINS | Spam the "take" command 20× and reload mid-way: exactly one lantern. | W-PLUG-08 |
| SR-6.9 | Reusable rules: typed event/condition/action composition, explicit execution and ownership, debuggable explanations. | Derivation rules are capability rules, not gameplay logic; refusal codes per family. | partial | `com.gamecore.gameplay.logic`: rule assets (trigger, conditions, actions), deterministic evaluation stage, trace with "why/why not". | W1-PLUGINS | Ask "why didn't the gate open?": the trace lists the failing condition and its inputs. | W-PLUG-09 |
| SR-6.10 | UI and player flow: HUD, prompts, dialogue, journal, inventory; main menu, pause, settings, save/load, ending/restart; real bindings and commands. | None. | absent | `com.gamecore.gameplay.ui`: UI Toolkit documents + binding layer + command dispatch; flow controller. | W1-PLUGINS | Full flow: menu → new game → pause → save → quit → load → ending → restart, with no editor. | W-GAME-05 |
| SR-6.11 | Audio and atmosphere: ambient, music/SFX, voice playback, animation integration; real asset import/binding and lifecycle. | AudioSource sink exists but is never installed, audio disabled in the project. | partial | `com.gamecore.gameplay.audio`: ambient zones per region, music states, SFX events, voice-line playback bound to dialogue; audio enabled in the game project. | W1-PLUGINS | Entering the Marsh crossfades ambience; a dialogue line plays its generated voice clip; leaving the region releases the clips. | W-PLUG-11 |
| SR-6.12 | Persistence and recovery: user-facing save/load; world, quest, inventory, region-state restoration; version/schema compatibility with actionable failures. | Checkpoint capture exists; restore builders only in validation; no migrations; no save UI (F4). | partial (kernel) | Production restore builder in `com.gamecore.unity.runtime`; forward slot migrations; save slots; actionable refusal in the load UI; player position/region in slots. | W1-KERNEL + gameplay.save | Save in the Ruin with 3 items; change a definition schema version with a registered migration; load: everything restored; without a migration: a clear "save needs migration X" refusal. | W-PERSIST-01..03 |

## 7. Application bootstrap and independent game projects

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-7.1 | A production application root composes the GameCore world for a game (lane, publisher, pipeline, bridge, provenance, restore) with a real catalog hash and no silent fallback. | Fixtures only (F3). | absent | `com.gamecore.unity.app`: `GameApplicationRoot`, one pump path, fallback is a hard failure with diagnostics. | W1-KERNEL | Start the player with a corrupted catalog: it exits with a named failure code, not an empty world. | W-KERNEL-01 |
| SR-7.2 | A new Unity project installs the packages and runs new content without copying hidden setup. | n/a | absent | Project template/installer (`Studio > Project Setup`), required settings applied by code (Input System, URP, audio on, packages). | W5-CLEAN | Clean project: install, create 2 regions, 1 quest, 2 NPCs, run and build. | W-CLEAN-01 |
| SR-7.3 | Ordinary new content needs no kernel source change. | n/a | absent | All content through definitions, prefabs, rule assets, UI documents. | W5-CLEAN | `git diff` of kernel packages is empty after the clean-project exercise. | W-CLEAN-02 |

## 8. Graphical runtime behavior

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-8.1 | A graphical Linux player (URP, Input System, audio on) runs the reference game at the accepted frame budget. | Headless IL2CPP only; audio disabled. | absent | Game project settings; graphical build lane; frame capture evidence. | W5-GAME | 10-minute playthrough recording on the RTX 4060 Ti with frame times logged. | W-GAME-01 |
| SR-8.2 | Editor Play Mode and the Studio viewport run the same game without double-stepping. | Pump node + reset hook. | partial | Viewport uses the game camera; pump ownership asserted. | W2-UI | Step counter advances exactly once per frame with the viewport open. | W-UI-04 |
| SR-8.3 | Domain reload and Play Mode lifecycle keep Studio state (tasks, history) intact. | Reset hook only. | partial | Studio state serialized in `ScriptableSingleton`/disk; companion holds task state. | W2-UI, W2-ETOS | Recompile a script while a task runs: after reload the tray shows the same task. | W-REC-02 |

## 9. Persistence, lifecycle, cancellation, recovery

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-9.1 | Every long operation (generation, staging, build, region load) is cancellable with a defined outcome. | etos cancel for agent-opened tasks; none for Unity. | partial | Cancellation tokens in edit engine, region streaming, staging lane; outcomes recorded. | W1-MODEL, W2-ETOS | Cancel a region load mid-way: no half-loaded views remain; cancel a staging job: slot is freed. | W-REC-03 |
| SR-9.2 | Delayed completion after disconnect/restart is attributed to the right request. | Idempotent task ids. | partial | Durable request ledger keyed by change-set id ↔ task id. | W2-ETOS | see SR-4.5 | W-ETOS-06 |
| SR-9.3 | Checkpoint continuity across code admission (compile → reload → restore). | 04 §9: session terminates. | absent | Capture before reload; restore after; explicit "play since checkpoint is lost" messaging where it applies. | W4-MECH, W1-KERNEL | see SR-3.6 | W-MECH-01 |

## 10. Performance and resource budgets

Budgets are defined before optimization in [07-verification-matrix.md §2](07-verification-matrix.md#2-budgets); they separate model/network latency from local latency. Summary:

| ID | Budget | Current measurement | Status |
|---|---|---|---|
| SR-10.1 | Graphical player: p95 frame ≤ 16.7 ms at 1080p on the RTX 4060 Ti; no frame > 100 ms during region transitions. | none | absent |
| SR-10.2 | Editor selection/hover ≤ 16 ms; box-select of 500 candidates ≤ 50 ms. | none | absent |
| SR-10.3 | Local edit application (no model): ≤ 200 ms for a single-target change set; large-scope (all NPCs in a region) ≤ 1 s. | kernel prepare 0.1–0.2 s at ~1k targets | partial |
| SR-10.4 | Region load ≤ 2 s, unload ≤ 1 s, residency: unloaded regions hold no views, textures or audio clips. | none | absent |
| SR-10.5 | Memory after 10 Play/Edit cycles within +15 % of the first cycle. | none | absent |
| SR-10.6 | Agent progress visible ≤ 1 s after a state change; cancel acknowledged ≤ 2 s; model latency reported separately. | none | absent |
| SR-10.7 | Existing kernel issue: ~1 s prepare per edit at 10k targets stays an open item; the reference game is designed under ~1.5k simulated targets and the measurement is re-taken with product workloads. | `BUDGET_DECISIONS.md:77-84` | partial |

## 11. Packaging, installation, build, release

| ID | Behavior | Current implementation | Status | Missing | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|---|---|
| SR-11.1 | Installable package set (UPM, `file:` or tarball) with versions and dependencies that pass the repo checkers. | 16 packages, strict checker (F7). | partial | New packages + extended checker allowlist with pinned Unity package versions; lock in the game project. | W0-TOOLS | `python3 tools/check_package_metadata.py` passes with the new packages. | W-TOOL-02 |
| SR-11.2 | One-command host setup for etos + companion + workers + providers, idempotent, no secrets in the repo. | none | absent | `studio/etos/` scripts, templates, lock file. | W0-HOST | Fresh run on the Linux host completes; second run is a no-op. | W-HOST-01 |
| SR-11.3 | Reproducible player builds (Linux IL2CPP graphical) and the existing V1 gates still pass. | `tools/reproduce.sh` for V1. | partial | `tools/build_game_player.sh`; V1 gates re-run on the integrated revision. | W5-GAME, W6-VERIFY | Build log, binary sha256, and a V1 gate transcript on the same revision. | W-GAME-06 |

## 12. Documentation and end-to-end acceptance

| ID | Behavior | Status | Owner | Acceptance | Evidence |
|---|---|---|---|---|---|
| SR-12.1 | Creator documentation (how to author, prompt, review, save, build). | absent | W6-DOCS | A new user follows it to add an NPC with a dialogue in the clean project. | W-DOC-01 |
| SR-12.2 | Plugin-developer documentation (authoring metadata, tools, definitions, validators, tests). | absent | W6-DOCS | A developer adds a "lever" interactable plugin following only the guide. | W-DOC-02 |
| SR-12.3 | End-to-end acceptance: the six mandated AI workflows + clean-project proof + standalone playthrough, on one revision. | absent | W6-VERIFY | [07-verification-matrix.md](07-verification-matrix.md) rows all exercised or marked blocked with the exact prerequisite. | W-E2E-01 |

## 13. Known issues investigated (not repeated blindly)

| Claim in the mandate | What the source says | Consequence |
|---|---|---|
| "V1 completion covers a bounded runtime protocol, not a creator-facing editor." | Confirmed (F1). | Everything in §1–§3 is new work. |
| "Content-generation and build workflows are oriented toward qualification projects." | Confirmed: generators are `-executeMethod` entry points in the validation project; probe modes only (`docs/operator/headless.md:94-119`). | New game build lane and compile step. |
| "Definition-level baking ≠ scene/Prefab pipeline." | Confirmed (F6). | §5 pipeline is new; the catalog compiler is reused as its last stage. |
| "View and gameplay references are intentionally small." | Confirmed (F2). Reusable pieces: stage pipeline shape, typed command/event codecs, exactly-once outbox, slot policies. | The plugin library is new code that reuses kernel contracts, not the fixtures. |
| "Qualification is Linux IL2CPP headless." | Confirmed; nothing in the runtime checks batchmode, so graphical use is unblocked but unmeasured. | Graphical qualification is part of W5/W6. |
| "Expensive composition preparation at 10k targets; timing deferred." | Confirmed (F12). | Architecture keeps scenery out of GameCore and bounds simulated targets; budgets re-measured on product workloads, never lowered silently. |

## 14. External prerequisites found

| Prerequisite | State | Affected rows |
|---|---|---|
| Model/image/TTS/realtime providers | Available (Echo, DashScope) with keys on the host | none blocked |
| 3D mesh generation provider (Replicate/Tripo/Meshy style `predictions`) | **No credential available.** | SR-4.7 (3D part only): implemented against the `predictions` family, marked **blocked** until a key is supplied. Not replaced by a fake. |
| Interactive Unity on the Linux display | Available after restarting the GNOME shell; the licence validates. | none |
| Owner approval for host changes (service user for etos, systemd units) | Not yet given. Default: run etosd under the existing user in a user systemd unit, state under `~/.local/share/etos`, no new OS users. | SR-11.2 uses the default; hardening with separate users is documented as optional. |

## 15. Status at completion

As of P4.2d (2026-10-06): the original assessment above is historical and unchanged except for its report link. Current platform baseline is **etos main ≥ e4067fd (contains 278ef9c)**; the original 6c2c3f4 references describe the assessment, not the installation requirement. ([P4.3-final §Baseline](packets/P4.3-final-docs.md#baseline))

Closing status is evidence coverage: **done** means all mapped acceptance rows PASS; **partial** means a FAIL or mixed PASS/BLOCKED remains; **blocked** means the mapped acceptance evidence is BLOCKED or absent. This does not claim a same-revision rerun. The sources are [SUMMARY](../../artifacts/studio/verification/SUMMARY.md) and [ROWS](../../artifacts/studio/verification/ROWS.json). Historical aliases W-REC-02 and W-PLUG-08/09 map to W-ETOS-09 and W-PLUG-10/12 respectively; views include all six rows.

| SR row | Status at completion | Acceptance evidence path and verdict |
|---|---|---|
| SR-1.1 | partial | [W-UI-01](../../artifacts/studio/verification/W-UI-01/README.md): FAIL |
| SR-1.2 | blocked | [W-UI-02](../../artifacts/studio/verification/W-UI-02/README.md): BLOCKED |
| SR-1.3 | blocked | [W-UI-03](../../artifacts/studio/verification/W-UI-03/README.md): BLOCKED |
| SR-1.4 | blocked | [W-EDIT-04](../../artifacts/studio/verification/W-EDIT-04/README.md): BLOCKED |
| SR-1.5 | partial | [W-AI-02](../../artifacts/studio/verification/W-AI-02/README.md): FAIL |
| SR-1.6 | done | [W-EDIT-05](../../artifacts/studio/verification/W-EDIT-05/README.md): PASS |
| SR-1.7 | done | [W-VIEW-01](../../artifacts/studio/verification/W-VIEW-01/README.md): PASS; [W-VIEW-02](../../artifacts/studio/verification/W-VIEW-02/README.md): PASS; [W-VIEW-03](../../artifacts/studio/verification/W-VIEW-03/README.md): PASS; [W-VIEW-04](../../artifacts/studio/verification/W-VIEW-04/README.md): PASS; [W-VIEW-05](../../artifacts/studio/verification/W-VIEW-05/README.md): PASS; [W-VIEW-06](../../artifacts/studio/verification/W-VIEW-06/README.md): PASS |
| SR-1.8 | blocked | [W-EDIT-06](../../artifacts/studio/verification/W-EDIT-06/README.md): BLOCKED |
| SR-2.1 | blocked | [W-PERSIST-02](../../artifacts/studio/verification/W-PERSIST-02/README.md): BLOCKED |
| SR-2.2 | blocked | [W-MODEL-02](../../artifacts/studio/verification/W-MODEL-02/README.md): BLOCKED |
| SR-2.3 | done | [W-TOOL-01](../../artifacts/studio/verification/W-TOOL-01/README.md): PASS |
| SR-2.4 | blocked | [W-ETOS-04](../../artifacts/studio/verification/W-ETOS-04/README.md): BLOCKED |
| SR-3.1 | blocked | [W-EDIT-01](../../artifacts/studio/verification/W-EDIT-01/README.md): BLOCKED |
| SR-3.2 | done | [W-EDIT-02](../../artifacts/studio/verification/W-EDIT-02/README.md): PASS |
| SR-3.3 | partial | [W-EDIT-03](../../artifacts/studio/verification/W-EDIT-03/README.md): BLOCKED; [W-REC-01](../../artifacts/studio/verification/W-REC-01/README.md): PASS |
| SR-3.4 | done | [W-EDIT-05](../../artifacts/studio/verification/W-EDIT-05/README.md): PASS |
| SR-3.5 | done | [W-EDIT-07](../../artifacts/studio/verification/W-EDIT-07/README.md): PASS |
| SR-3.6 | partial | [W-MECH-01](../../artifacts/studio/verification/W-MECH-01/README.md): FAIL |
| SR-4.1 | blocked | [W-ETOS-01](../../artifacts/studio/verification/W-ETOS-01/README.md): BLOCKED |
| SR-4.2 | blocked | [W-ETOS-02](../../artifacts/studio/verification/W-ETOS-02/README.md): BLOCKED |
| SR-4.3 | blocked | [W-ETOS-04](../../artifacts/studio/verification/W-ETOS-04/README.md): BLOCKED |
| SR-4.4 | blocked | [W-ETOS-05](../../artifacts/studio/verification/W-ETOS-05/README.md): BLOCKED |
| SR-4.5 | blocked | [W-ETOS-06](../../artifacts/studio/verification/W-ETOS-06/README.md): BLOCKED |
| SR-4.6 | blocked | [W-ETOS-07](../../artifacts/studio/verification/W-ETOS-07/README.md): BLOCKED |
| SR-4.7 | partial | [W-AI-01](../../artifacts/studio/verification/W-AI-01/README.md): BLOCKED; [W-AI-02](../../artifacts/studio/verification/W-AI-02/README.md): FAIL; [W-AI-03](../../artifacts/studio/verification/W-AI-03/README.md): PASS; [W-AI-04](../../artifacts/studio/verification/W-AI-04/README.md): PASS; [W-AI-05](../../artifacts/studio/verification/W-AI-05/README.md): PASS; [W-AI-06](../../artifacts/studio/verification/W-AI-06/README.md): FAIL |
| SR-4.8 | partial | [W-VOICE-01](../../artifacts/studio/verification/W-VOICE-01/README.md): FAIL |
| SR-4.9 | blocked | [W-ETOS-08](../../artifacts/studio/verification/W-ETOS-08/README.md): BLOCKED |
| SR-4.10 | blocked | [W-GAME-07](../../artifacts/studio/verification/W-GAME-07/README.md): BLOCKED |
| SR-5.1 | done | [W-PLUG-12](../../artifacts/studio/verification/W-PLUG-12/README.md): PASS |
| SR-5.2 | blocked | [W-PLUG-10](../../artifacts/studio/verification/W-PLUG-10/README.md): BLOCKED |
| SR-5.3 | done | [W-PLUG-12](../../artifacts/studio/verification/W-PLUG-12/README.md): PASS |
| SR-5.4 | blocked | [W-AI-01](../../artifacts/studio/verification/W-AI-01/README.md): BLOCKED |
| SR-5.5 | done | [W-AI-04](../../artifacts/studio/verification/W-AI-04/README.md): PASS |
| SR-6.1 | blocked | [W-PLUG-01](../../artifacts/studio/verification/W-PLUG-01/README.md): BLOCKED |
| SR-6.2 | blocked | [W-PLUG-02](../../artifacts/studio/verification/W-PLUG-02/README.md): BLOCKED |
| SR-6.3 | blocked | [W-PLUG-03](../../artifacts/studio/verification/W-PLUG-03/README.md): BLOCKED |
| SR-6.4 | done | [W-PLUG-04](../../artifacts/studio/verification/W-PLUG-04/README.md): PASS |
| SR-6.5 | done | [W-PLUG-05](../../artifacts/studio/verification/W-PLUG-05/README.md): PASS |
| SR-6.6 | done | [W-PLUG-06](../../artifacts/studio/verification/W-PLUG-06/README.md): PASS |
| SR-6.7 | done | [W-PLUG-07](../../artifacts/studio/verification/W-PLUG-07/README.md): PASS |
| SR-6.8 | blocked | [W-PLUG-08](../../artifacts/studio/verification/W-PLUG-08/README.md): BLOCKED |
| SR-6.9 | done | [W-PLUG-09](../../artifacts/studio/verification/W-PLUG-09/README.md): PASS |
| SR-6.10 | blocked | [W-GAME-05](../../artifacts/studio/verification/W-GAME-05/README.md): BLOCKED |
| SR-6.11 | blocked | [W-PLUG-11](../../artifacts/studio/verification/W-PLUG-11/README.md): BLOCKED |
| SR-6.12 | partial | [W-PERSIST-01](../../artifacts/studio/verification/W-PERSIST-01/README.md): PASS; [W-PERSIST-02](../../artifacts/studio/verification/W-PERSIST-02/README.md): BLOCKED; [W-PERSIST-03](../../artifacts/studio/verification/W-PERSIST-03/README.md): PASS |
| SR-7.1 | done | [W-KERNEL-01](../../artifacts/studio/verification/W-KERNEL-01/README.md): PASS |
| SR-7.2 | done | [W-CLEAN-01](../../artifacts/studio/verification/W-CLEAN-01/README.md): PASS |
| SR-7.3 | done | [W-CLEAN-02](../../artifacts/studio/verification/W-CLEAN-02/README.md): PASS |
| SR-8.1 | blocked | [W-GAME-01](../../artifacts/studio/verification/W-GAME-01/README.md): BLOCKED |
| SR-8.2 | done | [W-UI-04](../../artifacts/studio/verification/W-UI-04/README.md): PASS |
| SR-8.3 | blocked | [W-ETOS-09](../../artifacts/studio/verification/W-ETOS-09/README.md): BLOCKED |
| SR-9.1 | blocked | [W-REC-03](../../artifacts/studio/verification/W-REC-03/README.md): BLOCKED |
| SR-9.2 | blocked | [W-ETOS-06](../../artifacts/studio/verification/W-ETOS-06/README.md): BLOCKED |
| SR-9.3 | partial | [W-MECH-01](../../artifacts/studio/verification/W-MECH-01/README.md): FAIL |
| SR-10.1 | blocked | [W-GAME-01](../../artifacts/studio/verification/W-GAME-01/README.md): BLOCKED |
| SR-10.2 | done | [W-UI-05](../../artifacts/studio/verification/W-UI-05/README.md): PASS |
| SR-10.3 | done | [W-EDIT-08](../../artifacts/studio/verification/W-EDIT-08/README.md): PASS |
| SR-10.4 | blocked | [W-PLUG-01](../../artifacts/studio/verification/W-PLUG-01/README.md): BLOCKED |
| SR-10.5 | done | [W-GAME-08](../../artifacts/studio/verification/W-GAME-08/README.md): PASS |
| SR-10.6 | blocked | [W-ETOS-05](../../artifacts/studio/verification/W-ETOS-05/README.md): BLOCKED; [W-ETOS-06](../../artifacts/studio/verification/W-ETOS-06/README.md): BLOCKED |
| SR-10.7 | done | [W-EDIT-08](../../artifacts/studio/verification/W-EDIT-08/README.md): PASS |
| SR-11.1 | done | [W-TOOL-02](../../artifacts/studio/verification/W-TOOL-02/README.md): PASS |
| SR-11.2 | blocked | [W-HOST-01](../../artifacts/studio/verification/W-HOST-01/README.md): BLOCKED |
| SR-11.3 | done | [W-GAME-06](../../artifacts/studio/verification/W-GAME-06/README.md): PASS |
| SR-12.1 | done | [W-DOC-01](../../artifacts/studio/verification/W-DOC-01/README.md): PASS |
| SR-12.2 | partial | [W-DOC-02](../../artifacts/studio/verification/W-DOC-02/README.md): FAIL |
| SR-12.3 | blocked | [W-E2E-01](../../artifacts/studio/verification/W-E2E-01/README.md): BLOCKED |
