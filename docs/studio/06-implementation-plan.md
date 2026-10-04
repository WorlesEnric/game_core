# GameCore Studio: implementation ownership and dependency plan

**Status:** live plan (2026-10-04). Fable (orchestrator) owns requirement interpretation, contracts, decomposition,
integration, review and acceptance. Opus 5.5 agents own packets. One owner per shared mutation boundary; a packet
never edits outside its listed paths. Shared files (`games/*/Packages/manifest.json`, `tools/check_*.py` lists,
`docs/studio/*`) are owned by the packet named here or by Fable at integration.

## 1. Working method

- Each packet runs in its own git worktree on the Mac checkout (branch `studio/<packet>`), commits there, and
  reports. Fable merges packets into `main` in dependency order and runs the integration checks on the Linux host.
- **No builds on the Mac:** nothing is compiled, built or tested on the Mac; code reaches the host only through
  git (`studio/tools/sync-to-host.sh` pushes the branch to the host hub `~/wkspace/gc-studio/hub.git`), never rsync.
- Compile and test feedback during a packet: the worker syncs its branch to its own directory on the Linux host
  (`ssh myubuntu`, `~/wkspace/gc-studio/<packet>/`, a clone of the hub checked out at the packet's branch,
  created by `studio/tools/sync-to-host.sh <packet>`), runs
  `dotnet build/test` for Unity-free code and `studio/tools/unity-compile.sh <project>` (batchmode compile +
  optional EditMode filter) for Unity code. At most three concurrent Unity batchmode instances host-wide; each
  packet uses only its own project copy. Workers never run `tools/reproduce.sh` or project-wide gates; the
  integration owner does.
- Every packet delivers: code, tests, a `PACKET.md` note in its branch root (what was built, what was verified,
  how, what is left), and no secrets.
- "Done" is accepted only after Fable inspects the diff and exercises the claimed behaviour (or has the
  verification owner do it) on the integrated revision.

## 2. Packets

### Wave 0: foundations (parallel)

| Packet | Owner model | Paths (exclusive) | Depends on | Delivers | Acceptance |
|---|---|---|---|---|---|
| **P0.1 host-etos** | Opus 5.5 | `studio/etos/**`, `studio/images/**`, `studio/tools/host-*.sh`, `artifacts/studio/environment/**`; etos repo: `crates/etops/src/speech_bailian.rs` (new), `crates/etops/src/config.rs` (kind), `crates/etops/README.md`, `sdk/rust/src/data.rs` (generate op fix), tests | none | etos built at the pin + SADR-005 patch; node running as a user service with Docker runtime; `models.toml`/`ops.toml`; images `gc-designer`, `gc-mechanic`; workers created; verification transcript with real image/describe/tts/realtime calls; `etos.lock` | `studio/etos/verify.sh` passes on the host; second `install.sh` run is a no-op; transcript in `artifacts/studio/environment/` |
| **P0.2 projects-tooling** | Opus 5.5 | `games/hollowmere/**` (skeleton), `tools/check_package_metadata.py`, `tools/check_game_core_csharp.py`, `tools/studio/**`, `studio/tools/sync-to-host.sh`, `studio/tools/unity-compile.sh`, `.gitignore` | none | Unity project skeleton per SADR-016 with all kernel packages, URP, Input System, UI Toolkit, AI Navigation, audio on; checker changes per SADR-014 with self-tests; host sync/compile scripts | project opens in batchmode on the host with zero errors; all existing `tools/check_*.py` and `validate_game_core_docs.py` pass unchanged on `unity/GameCore.Validation` |
| **P0.3 studio-model** | Opus 5.5 | `Packages/com.gamecore.studio.core/Runtime/Model/**`, `Packages/com.gamecore.studio.core/package.json`, `dotnet/src/GameCore.Studio.Model/**`, `dotnet/tests/GameCore.Studio.Model.Tests/**`, `docs/studio/schemas/**`, `tools/studio/emit_studio_schemas.py` | none | Unity-free shapes of 03 §1–§6, §9: AuthoringRef, SelectionSnapshot, SemanticIndex, ToolCatalog + `[Authorable]`/`[AuthorField]`/`[AuthorRef]`/`[AuthorOperation]` attributes, ChangeSet, Diagnostic, JSON (Newtonsoft) round trip, schema emission, validation of change sets against a catalog | dotnet tests green; schemas committed and byte-identical on regeneration |
| **P0.4 kernel-app** | Opus 5.5 | `Packages/com.gamecore.unity.app/**` (new), `Packages/com.gamecore.unity.runtime/Runtime/Integration/WorldCompositionBridge.cs`, `.../Integration/CompositionEditValidation*.cs` (new), `Packages/com.gamecore.derivation/**` or `.../unity.runtime/Runtime/Integration/InstallConfig*.cs` (SADR-013, worker's choice, documented), `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs` (fallback → failure), related tests in `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/**` and `dotnet/tests/**`, `docs/game-core/04-unity-integration.md` §9 note, `docs/game-core/10-decisions-and-open-questions.md` (SADR cross-reference row) | none | SADR-010, 011, 013: `GameApplicationRoot` (lane+publisher+pipeline+bridge+provenance+catalog hash+one pump+hard failure), explicit expected revision, no step charge for composition edits, validate-before-commit, install config reaching systems | existing EditMode/PlayMode suites still green on the host; new tests for each SADR; a smoke scene in `games/hollowmere` boots a world through the root with `FallbackCount == 0` |
| **P0.5 companion** | Opus 5.5 | `studio/agent/**`, `studio/etos/agent/agent.toml`, `studio/etos/agent/workers/*.md`, `studio/etos/app/app.toml` | none (uses etos `sdk/rust` by pinned path; runs its own local etosd for tests) | Rust companion per 04 §2–§7: endpoint + proxy-token check, HTTP/WS API, durable ledger (SQLite), task desk (open/follow/cancel/recover), files + content store + digests, ops (`generate.image`, `tts`, `describe`, `generate.3d` → blocked), realtime voice bridge, index logger, stage job runner shell (calls `studio/stage/stage.sh`), structured logs with redaction | `cargo test` incl. a fake-node suite; an integration run against a real local etosd (documented) for: request→task→candidate, cancel, restart-recovery, voice session open/close |

### Wave 1a: entity foundation and persistence (parallel)

| Packet | Paths | Depends on | Delivers | Acceptance |
|---|---|---|---|---|
| **P1.1 entities-world-compile** | `Packages/com.gamecore.gameplay.entities/**`, `.../gameplay.world/**`, `.../gameplay.compile/**`, `Packages/com.gamecore.rules.gameplay/{Entities,World}/**`, `games/hollowmere/Assets/Hollowmere/Regions/*` (three empty region scenes + manifest), tests | P0.2, P0.3, P0.4 | catalog rows 1–2 and the compile pipeline: `AuthoredEntity`, definitions, prefab binder, overrides, binders, region scopes, residency, portals, streaming, world builder at Play, authored → catalog description → generated catalog + spawn plan, deterministic bake, Studio tools for groups 1–2 | three-region loop in Play with state kept; bake byte-identity; EditMode tests |
| **P1.2 save-restore** | `Packages/com.gamecore.gameplay.save/**`, `Packages/com.gamecore.unity.runtime/Runtime/Persistence/Production*.cs` (new restore builder), `.../Pure/Persistence/SlotMigration*.cs`, `WorldHost.cs` (restored temporal origin, SADR-012), `Packages/com.gamecore.rules.gameplay/Save/**`, tests | P0.4 | SADR-012 + catalog row 12: production restore builder, forward slot migrations, step/time continuity, batched restore publication, save slots, refusals | round trip on a synthetic world; migration test; refusal test; V1 recovery suites still green |

### Wave 1b: plugin library and Studio core (parallel, after P1.1 merge)

| Packet | Paths | Depends on | Delivers | Acceptance |
|---|---|---|---|---|
| **P1.3 player-npc-interaction** | `gameplay.player/**`, `gameplay.npc/**`, `gameplay.interaction/**`, `rules.gameplay/{Player,Npc,Interaction}/**` | P1.1 | catalog rows 3–5 | W-PLUG-03/04/05 scenes in `games/hollowmere/Assets/Hollowmere/Tests` |
| **P1.4 dialogue-quest-logic-inventory** | `gameplay.dialogue/**`, `gameplay.quest/**`, `gameplay.logic/**`, `gameplay.inventory/**`, `rules.gameplay/{Dialogue,Quest,Logic,Inventory}/**` | P1.1 | catalog rows 6–9 | W-PLUG-06/07/08/09 |
| **P1.5 ui-audio** | `gameplay.ui/**`, `gameplay.audio/**`, `rules.gameplay/{Ui,Audio}/**` | P1.1 (and reads P1.4 definitions through interfaces declared in P1.1's `GameCore.Gameplay.Contracts`) | catalog rows 10–11 | HUD/dialogue/journal/inventory/menus bound to committed state; ambience/music/voice |
| **P1.6 studio-core-unity** | `Packages/com.gamecore.studio.core/{Runtime/Authoring,Editor}/**` (index builder, tool registry, edit engine, journal, undo, picking service, conflict detection, staging of previews, apply queue) | P0.3, P1.1 | 03 §2–§3, §5–§7, §9 on the Unity side; generic tools (set/assign/create/delete/duplicate/move/place/layout); inspector generator; EditMode tests | tool round trips on test assets; journal survives domain reload; undo/redo with retained artifacts |

### Wave 2: Studio surfaces and integration (parallel)

| Packet | Paths | Depends on | Delivers | Acceptance |
|---|---|---|---|---|
| **P2.1 studio-ui** | `Packages/com.gamecore.studio.ui/**` | P1.6 | viewport window (game camera render, Play/Select modes, input routing, pump assertion), picking UX (click/hover/marquee/overlap list/point-at), prompt bar, voice button + transcript, task tray, candidate strip (preview/compare/apply/reject), contextual control panel, history panel, badges for apply requirements | W-UI-01..04, W-EDIT-05/06 |
| **P2.2 studio-etos-client** | `Packages/com.gamecore.studio.etos/**` | P0.5, P1.6 | C# client of 04 §2 (HTTP + WS through etos, tickets, reconnect with cursors), microphone capture → PCM16 24 kHz, request builder (context slice packing), candidate import (verified artifacts → assets), provider status, error pass-through, settings UI (key file path), redaction | W-ETOS-01..08, W-VOICE-01 against the live node |
| **P2.3 studio-views** | `Packages/com.gamecore.studio.views/**` | P1.6, P1.4 | relationships view, dialogue/quest graph view (editable through tools), world/region flow view, data tables with inline edit, changes/dependencies/diagnostics/history view | W-VIEW-01..05 |
| **P2.4 staging-lane** | `studio/stage/**`, `Packages/com.gamecore.studio.core/Editor/Stage/**`, `studio/agent/src/stage.rs` | P0.5, P1.6, P0.2 | slot creation, stage runner (dotnet + batchmode Unity + watchdog + forbidden-content scan), verdicts, admit flow with checkpoint/restore (uses P1.2) | W-MECH-01 with a sample "pressure plate" package |

### Wave 3: reference game and AI workflows

| Packet | Paths | Depends on | Delivers | Acceptance |
|---|---|---|---|---|
| **P3.1 hollowmere-content** | `games/hollowmere/Assets/Hollowmere/**`, `artifacts/studio/workflows/**` | W1, W2 | the complete game of 05 §"Reference game", authored through Studio tools with journal evidence, generated textures/portraits/voice lines, Linux graphical build via `studio/tools/build_game_player.sh`, playthrough recording | W-GAME-01..07 |
| **P3.2 ai-workflows** | `artifacts/studio/workflows/**` | P3.1 (overlaps) | the six mandated workflows exercised live with recordings, journal entries, task ids and usage | W-AI-01..06 |

### Wave 4: proof, verification, docs

| Packet | Paths | Depends on | Delivers | Acceptance |
|---|---|---|---|---|
| **P4.1 clean-proof** | `games/cleanproof/**`, `studio/tools/new-project.sh`, `artifacts/studio/cleanproof/**` | W3 | a fresh project with different content and quest sequence; run and build; `git diff` of kernel = empty | W-CLEAN-01/02 |
| **P4.2 verification** | `artifacts/studio/**`, `docs/studio/07-verification-matrix.md` (fill), `tools/studio/check_studio_evidence.py` | W3 | every matrix row exercised or marked blocked with prerequisite; budgets measured (≤ 2 runs); V1 `tools/reproduce.sh` on the integrated revision | matrix complete |
| **P4.3 docs** | `docs/studio/08-creator-guide.md`, `09-plugin-developer-guide.md`, `10-install-build-run.md`, `11-completion-report.md`, `README.md` | W3 | creator and developer documentation validated by P4.1's exercise; completion report with the supported profile | W-DOC-01/02 |

## 3. Integration checks run by Fable per merge

1. `python3 tools/check_package_metadata.py`, `check_game_core_csharp.py`, `validate_game_core_docs.py`,
   `emit_failure_codes.py --check` on the Mac.
2. On the host: `dotnet test dotnet/GameCore.sln`; Unity batchmode compile of `games/hollowmere` and
   `unity/GameCore.Validation`; EditMode suites touched by the merged packets.
3. After Wave 1b and Wave 2: PlayMode suites and the Play smoke of `games/hollowmere`.
4. Before completion: `tools/reproduce.sh` (V1) and the Studio verification matrix.

## 4. Non-goals of every packet

No browser editor, no multiplayer, no cross-engine abstraction, no general visual programming language, no
hot code replacement claims, no mock providers in production paths, no credentials in files under version control,
no changes to `docs/game-core/00-core-protocols.md` semantics (additive notes only).
