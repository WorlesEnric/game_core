# GameCore Studio: implementation ownership and dependency plan

**Status:** delivery ledger as of P4.2d (2026-10-06); original packet scope rows retained. Fable (orchestrator) owns requirement interpretation, contracts, decomposition,
integration, review and acceptance. Initial packets used Claude Opus; follow-up Codex packets used gpt-6-astra; Fable-architect integrates. One owner per shared mutation boundary; a packet
never edits outside its listed paths. Shared files (`games/*/Packages/manifest.json`, `tools/check_*.py` lists,
`docs/studio/*`) are owned by the packet named here or by Fable at integration.

## 1. Working method

- Current Codex packets run in dedicated Linux host clones on their assigned branches. Initial Claude Opus packets used Mac worktrees for source orchestration; all builds/tests run on myubuntu. Fable merges and reviews in dependency order. ([merge ledger](#5-merged-packet-provenance))
- **No builds on the Mac:** nothing is compiled, built or tested on the Mac; code reaches the host only through
  git (`studio/tools/sync-to-host.sh` pushes the branch to the host hub `~/wkspace/gc-studio/hub.git`), never rsync.
- Compile and test feedback during a packet: the worker syncs its branch to its own directory on the Linux host
  (`ssh myubuntu`, `~/wkspace/gc-studio/<packet>/`, a clone of the hub checked out at the packet's branch,
  created by `studio/tools/sync-to-host.sh <packet>`), runs
  `dotnet build/test` for Unity-free code and `studio/tools/unity-compile.sh <packet> <project>` (batchmode compile +
  optional EditMode filter) for Unity code. At most three concurrent Unity batchmode instances host-wide; each
  packet uses only its own project copy. Workers never run `tools/reproduce.sh` or project-wide gates; the
  integration owner does.
- Every packet delivers: code, tests, a `PACKET.md` note in its branch root (what was built, what was verified,
  how, what is left), and no secrets.
- "Done" is accepted only after Fable inspects the diff and exercises the claimed behaviour (or has the
  verification owner do it) on the integrated revision.

## 2. Packets

### Wave 0: foundations (parallel)

| Packet | Owner model | Paths (exclusive) | Depends on | Delivers | Acceptance | Merged delivery (SHA; branch; implementer; date) |
|---|---|---|---|---|---|---|
| **P0.1 host-etos** | Opus 5.5 | `studio/etos/**`, `studio/images/**`, `studio/tools/host-*.sh`, `artifacts/studio/environment/**`; etos repo: `crates/etops/src/speech_bailian.rs` (new), `crates/etops/src/config.rs` (kind), `crates/etops/README.md`, `sdk/rust/src/data.rs` (generate op fix), tests | none | etos built at the pin + SADR-005 patch; node running as a user service with Docker runtime; `models.toml`/`ops.toml`; images `gc-designer`, `gc-mechanic`; workers created; verification transcript with real image/describe/tts/realtime calls; `etos.lock` | `studio/etos/verify.sh` passes on the host; second `install.sh` run is a no-op; transcript in `artifacts/studio/environment/` | `7b5bca20`; `worktree-agent-a397841371142d705`; Claude Opus; 2026-10-04 |
| **P0.2 projects-tooling** | Opus 5.5 | `games/hollowmere/**` (skeleton), `tools/check_package_metadata.py`, `tools/check_game_core_csharp.py`, `tools/studio/**`, `studio/tools/sync-to-host.sh`, `studio/tools/unity-compile.sh`, `.gitignore` | none | Unity project skeleton per SADR-016 with all kernel packages, URP, Input System, UI Toolkit, AI Navigation, audio on; checker changes per SADR-014 with self-tests; host sync/compile scripts | project opens in batchmode on the host with zero errors; all existing `tools/check_*.py` and `validate_game_core_docs.py` pass unchanged on `unity/GameCore.Validation` | `80f7e01d`; `worktree-agent-af9add08a3462aaf6`; Claude Opus; 2026-10-04 |
| **P0.3 studio-model** | Opus 5.5 | `Packages/com.gamecore.studio.core/Runtime/Model/**`, `Packages/com.gamecore.studio.core/package.json`, `dotnet/src/GameCore.Studio.Model/**`, `dotnet/tests/GameCore.Studio.Model.Tests/**`, `docs/studio/schemas/**`, `tools/studio/emit_studio_schemas.py` | none | Unity-free shapes of 03 §1–§6, §9: AuthoringRef, SelectionSnapshot, SemanticIndex, ToolCatalog + `[Authorable]`/`[AuthorField]`/`[AuthorRef]`/`[AuthorOperation]` attributes, ChangeSet, Diagnostic, JSON (Newtonsoft) round trip, schema emission, validation of change sets against a catalog | dotnet tests green; schemas committed and byte-identical on regeneration | `037b3fe6`; not retained; second parent `7b22c474`; Claude Opus; 2026-10-04 |
| **P0.4 kernel-app** | Opus 5.5 | `Packages/com.gamecore.unity.app/**` (new), `Packages/com.gamecore.unity.runtime/Runtime/Integration/WorldCompositionBridge.cs`, `.../Integration/CompositionEditValidation*.cs` (new), `Packages/com.gamecore.derivation/**` or `.../unity.runtime/Runtime/Integration/InstallConfig*.cs` (SADR-013, worker's choice, documented), `Packages/com.gamecore.unity.adapters/Runtime/PlayerLoop/GameCoreApplicationBootstrap.cs` (fallback → failure), related tests in `unity/GameCore.Validation/Assets/GameCore.Validation/Tests/**` and `dotnet/tests/**`, `docs/game-core/04-unity-integration.md` §9 note, `docs/game-core/10-decisions-and-open-questions.md` (SADR cross-reference row) | none | SADR-010, 011, 013: `GameApplicationRoot` (lane+publisher+pipeline+bridge+provenance+catalog hash+one pump+hard failure), explicit expected revision, no step charge for composition edits, validate-before-commit, install config reaching systems | existing EditMode/PlayMode suites still green on the host; new tests for each SADR; a smoke scene in `games/hollowmere` boots a world through the root with `FallbackCount == 0` | `04d04f24`; `worktree-agent-a0524a3d15ab6a066`; Claude Opus; 2026-10-04 |
| **P0.5 companion** | Opus 5.5 | `studio/agent/**`, `studio/etos/agent/agent.toml`, `studio/etos/agent/workers/*.md`, `studio/etos/app/app.toml` | none (uses etos `sdk/rust` by pinned path; runs its own local etosd for tests) | Rust companion per 04 §2–§7: endpoint + proxy-token check, HTTP/WS API, durable ledger (SQLite), task desk (open/follow/cancel/recover), files + content store + digests, ops (`generate.image`, `tts`, `describe`, `generate.3d` → blocked), realtime voice bridge, index logger, stage job runner shell (calls `studio/stage/stage.sh`), structured logs with redaction | `cargo test` incl. a fake-node suite; an integration run against a real local etosd (documented) for: request→task→candidate, cancel, restart-recovery, voice session open/close | `c226fa72`; `worktree-agent-a904de072adf44bb1`; Claude Opus; 2026-10-04 |

### Wave 1a: entity foundation and persistence (parallel)

| Packet | Paths | Depends on | Delivers | Acceptance | Merged delivery (SHA; branch; implementer; date) |
|---|---|---|---|---|---|
| **P1.1 entities-world-compile** | `Packages/com.gamecore.gameplay.entities/**`, `.../gameplay.world/**`, `.../gameplay.compile/**`, `Packages/com.gamecore.rules.gameplay/{Entities,World}/**`, `games/hollowmere/Assets/Hollowmere/Regions/*` (three empty region scenes + manifest), tests | P0.2, P0.3, P0.4 | catalog rows 1–2 and the compile pipeline: `AuthoredEntity`, definitions, prefab binder, overrides, binders, region scopes, residency, portals, streaming, world builder at Play, authored → catalog description → generated catalog + spawn plan, deterministic bake, Studio tools for groups 1–2 | three-region loop in Play with state kept; bake byte-identity; EditMode tests | `10eb327c`; `worktree-agent-ad61857ccbcbba79a`; Claude Opus; 2026-10-04 |
| **P1.2 save-restore** | `Packages/com.gamecore.gameplay.save/**`, `Packages/com.gamecore.unity.runtime/Runtime/Persistence/Production*.cs` (new restore builder), `.../Pure/Persistence/SlotMigration*.cs`, `WorldHost.cs` (restored temporal origin, SADR-012), `Packages/com.gamecore.rules.gameplay/Save/**`, tests | P0.4 | SADR-012 + catalog row 12: production restore builder, forward slot migrations, step/time continuity, batched restore publication, save slots, refusals | round trip on a synthetic world; migration test; refusal test; V1 recovery suites still green | `f0e26b7a`; `worktree-agent-af7f4821f2e82a7c3`; Claude Opus; 2026-10-04 |

### Wave 1b: plugin library and Studio core (parallel, after P1.1 merge)

| Packet | Paths | Depends on | Delivers | Acceptance | Merged delivery (SHA; branch; implementer; date) |
|---|---|---|---|---|---|
| **P1.3 player-npc-interaction** | `gameplay.player/**`, `gameplay.npc/**`, `gameplay.interaction/**`, `rules.gameplay/{Player,Npc,Interaction}/**` | P1.1 | catalog rows 3–5 | W-PLUG-03/04/05 scenes in `games/hollowmere/Assets/Hollowmere/Tests` | `f313a6f5`; `worktree-agent-a1885e5fa6b8a204c`; Claude Opus; 2026-10-04 |
| **P1.4 dialogue-quest-logic-inventory** | `gameplay.dialogue/**`, `gameplay.quest/**`, `gameplay.logic/**`, `gameplay.inventory/**`, `rules.gameplay/{Dialogue,Quest,Logic,Inventory}/**` | P1.1 | catalog rows 6–9 | W-PLUG-06/07/08/09 | `1c4a363a`; `worktree-agent-a833890c84121beac`; Claude Opus; 2026-10-04 |
| **P1.5 ui-audio** | `gameplay.ui/**`, `gameplay.audio/**`, `rules.gameplay/{Ui,Audio}/**` | P1.1 (and reads P1.4 definitions through interfaces declared in P1.1's `GameCore.Gameplay.Contracts`) | catalog rows 10–11 | HUD/dialogue/journal/inventory/menus bound to committed state; ambience/music/voice | `01c213ed`; `worktree-agent-ab2fb374b37e8085c`; Claude Opus; 2026-10-04 |
| **P1.6 studio-core-unity** | `Packages/com.gamecore.studio.core/{Runtime/Authoring,Editor}/**` (index builder, tool registry, edit engine, journal, undo, picking service, conflict detection, staging of previews, apply queue) | P0.3, P1.1 | 03 §2–§3, §5–§7, §9 on the Unity side; generic tools (set/assign/create/delete/duplicate/move/place/layout); inspector generator; EditMode tests | tool round trips on test assets; journal survives domain reload; undo/redo with retained artifacts | `b3762de2`; `worktree-agent-a004eae06ca97de37`; Claude Opus; 2026-10-04 |

### Wave 2: Studio surfaces and integration (parallel)

| Packet | Paths | Depends on | Delivers | Acceptance | Merged delivery (SHA; branch; implementer; date) |
|---|---|---|---|---|---|
| **P2.1 studio-ui** | `Packages/com.gamecore.studio.ui/**` | P1.6 | viewport window (game camera render, Play/Select modes, input routing, pump assertion), picking UX (click/hover/marquee/overlap list/point-at), prompt bar, voice button + transcript, task tray, candidate strip (preview/compare/apply/reject), contextual control panel, history panel, badges for apply requirements | W-UI-01..04, W-EDIT-05/06 | `61b9fab4`; `worktree-agent-a595188d74c6b5b39`; Claude Opus; 2026-10-04 |
| **P2.2 studio-etos-client** | `Packages/com.gamecore.studio.etos/**` | P0.5, P1.6 | C# client of 04 §2 (HTTP + WS through etos, tickets, reconnect with cursors), microphone capture → PCM16 24 kHz, request builder (context slice packing), candidate import (verified artifacts → assets), provider status, error pass-through, settings UI (key file path), redaction | W-ETOS-01..08, W-VOICE-01 against the live node | `4d7ac6cc`; `worktree-agent-ade08c7cbb82eacb1`; Claude Opus; 2026-10-04 |
| **P2.3 studio-views** | `Packages/com.gamecore.studio.views/**` | P1.6, P1.4 | relationships view, dialogue/quest graph view (editable through tools), world/region flow view, data tables with inline edit, changes/dependencies/diagnostics/history view | W-VIEW-01..06 | `31c020b1`; `worktree-agent-a61c8c439837536f1`; Claude Opus; 2026-10-04 |
| **P2.4 staging-lane** | `studio/stage/**`, `Packages/com.gamecore.studio.core/Editor/Stage/**`, `studio/agent/src/stage.rs` | P0.5, P1.6, P0.2 | slot creation, stage runner (dotnet + batchmode Unity + watchdog + forbidden-content scan), verdicts, admit flow with checkpoint/restore (uses P1.2) | W-MECH-01 with a sample "pressure plate" package | `907ab3bd`; `codex/stage-tmp`; Claude Opus; 2026-10-04 |

### Wave 3: reference game and AI workflows

| Packet | Paths | Depends on | Delivers | Acceptance | Merged delivery (SHA; branch; implementer; date) |
|---|---|---|---|---|---|
| **P3.1 hollowmere-content** | `games/hollowmere/Assets/Hollowmere/**`, `artifacts/studio/workflows/**` | W1, W2 | the complete game of 05 §"Reference game", authored through Studio tools with journal evidence, generated textures/portraits/voice lines, Linux graphical build via `studio/tools/build_game_player.sh`, playthrough recording | W-GAME-01..07 | `c2f82451`; `worktree-agent-a86a49b2ba589f359`; Claude Opus; 2026-10-05 |
| **P3.2 ai-workflows** | `artifacts/studio/workflows/**` | P3.1 (overlaps) | the six mandated workflows exercised live with recordings, journal entries, task ids and usage | W-AI-01..06 | `6e8b8385`; `worktree-agent-ab08fa8f6a1843576`; Claude Opus; 2026-10-05 |

### Wave 4: proof, verification, docs

| Packet | Paths | Depends on | Delivers | Acceptance | Merged delivery (SHA; branch; implementer; date) |
|---|---|---|---|---|---|
| **P4.1 clean-proof** | `games/cleanproof/**`, `studio/tools/new-project.sh`, `artifacts/studio/cleanproof/**` | W3 | a fresh project with different content and quest sequence; run and build; `git diff` of kernel = empty | W-CLEAN-01/02 | `eb1c7bba`; `codex/p4.1`; Codex gpt-6-astra; 2026-10-05 |
| **P4.2 verification** | `artifacts/studio/**`, `docs/studio/07-verification-matrix.md` (fill), `tools/studio/check_studio_evidence.py` | W3 | every matrix row exercised or marked blocked with prerequisite; budgets measured (≤ 2 runs); V1 `tools/reproduce.sh` on the integrated revision | matrix complete | `c79a3832`; `codex/p4.2`; Codex gpt-6-astra; 2026-10-05 |
| **P4.3 docs** | `docs/studio/08-creator-guide.md`, `09-plugin-developer-guide.md`, `10-install-build-run.md`, `11-ownership-plan.md`, `12-completion-report.md`, `README.md` | W3 | creator and developer documentation validated by P4.1's exercise; completion report with the supported profile | W-DOC-01/02 | `e6deeb1d`; `codex/p4.3-docs`; Codex gpt-6-astra; 2026-10-04 (draft); final merge pending |


## 3. Integration checks run by Fable per merge

1. `python3 tools/check_package_metadata.py`, `check_game_core_csharp.py`, `validate_game_core_docs.py`,
   `emit_failure_codes.py --check` on the Linux host.
2. On the host: `dotnet test dotnet/GameCore.sln`; Unity batchmode compile of `games/hollowmere` and
   `unity/GameCore.Validation`; EditMode suites touched by the merged packets.
3. After Wave 1b and Wave 2: PlayMode suites and the Play smoke of `games/hollowmere`.
4. Before completion: `tools/reproduce.sh` (V1) and the Studio verification matrix.

## 4. Non-goals of every packet

No browser editor, no multiplayer, no cross-engine abstraction, no general visual programming language, no
hot code replacement claims, no mock providers in production paths, no credentials in files under version control,
no changes to `docs/game-core/00-core-protocols.md` semantics (additive notes only).

## 5. Merged packet provenance

Source command: `git log --merges --first-parent origin/main 0523bea9..d5080459`; this clone has `origin/main` at the packet's main baseline, not a local main branch. Merge dates below are git author dates. Branch is the exact second-parent ref where retained, otherwise a branch named in the packet note. Fable-architect is the integration role, not the implementation author of Codex/Opus work. The merge SHA also identifies the immutable source in local git. ([packet inventory](11-ownership-plan.md#document-inventory))

| Packet / merge subject | Merge SHA | Branch | Implementer | Date | Integrator |
|---|---|---|---|---|---|
| P0.2 projects-tooling | `80f7e01d` | `worktree-agent-af9add08a3462aaf6` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.3 studio-model | `037b3fe6` | not retained; second parent `7b22c474` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.5 companion | `c226fa72` | `worktree-agent-a904de072adf44bb1` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.2 follow-up | `0d99f505` | `worktree-agent-af9add08a3462aaf6` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.4 kernel-app | `04d04f24` | `worktree-agent-a0524a3d15ab6a066` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.3 review fixes | `f86b11dc` | `worktree-agent-a904de072adf44bb1` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.5 review fixes | `45beb941` | `worktree-agent-a904de072adf44bb1` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.2 save-restore | `f0e26b7a` | `worktree-agent-af7f4821f2e82a7c3` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.1 entities-world-compile | `10eb327c` | `worktree-agent-ad61857ccbcbba79a` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.1 host-etos | `7b5bca20` | `worktree-agent-a397841371142d705` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.6 studio-core-unity | `b3762de2` | `worktree-agent-a004eae06ca97de37` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.5 follow-ups | `2a34e3d9` | `worktree-agent-a904de072adf44bb1` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.3 player-npc-interaction | `f313a6f5` | `worktree-agent-a1885e5fa6b8a204c` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.1 fix round | `4d157308` | `worktree-agent-a397841371142d705` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.4 dialogue-quest-logic-inventory | `1c4a363a` | `worktree-agent-a833890c84121beac` | Claude Opus | 2026-10-04 | Fable-architect |
| P2.2 studio-etos-client | `4d7ac6cc` | `worktree-agent-ade08c7cbb82eacb1` | Claude Opus | 2026-10-04 | Fable-architect |
| P0.5 round | `0ba1de5f` | `worktree-agent-a904de072adf44bb1` | Claude Opus | 2026-10-04 | Fable-architect |
| branch 'worktree-agent-a87161a756b3b10dc' | `7263944a` | `worktree-agent-a87161a756b3b10dc` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.5 ui-audio | `01c213ed` | `worktree-agent-ab2fb374b37e8085c` | Claude Opus | 2026-10-04 | Fable-architect |
| P2.4 staging-lane | `907ab3bd` | `codex/stage-tmp` | Claude Opus | 2026-10-04 | Fable-architect |
| P2.1 studio-ui | `61b9fab4` | `worktree-agent-a595188d74c6b5b39` | Claude Opus | 2026-10-04 | Fable-architect |
| P2.3 studio-views | `31c020b1` | `worktree-agent-a61c8c439837536f1` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.6 follow-up | `10e5ef6a` | `worktree-agent-a004eae06ca97de37` | Claude Opus | 2026-10-04 | Fable-architect |
| P1.7a gameplay hardening | `0f0ad0e7` | `worktree-agent-a4c0e77e640321d4a` | Claude Opus | 2026-10-04 | Fable-architect |
| P4.3-draft (Codex) | `e6deeb1d` | `codex/p4.3-docs` | Codex gpt-6-astra | 2026-10-04 | Fable-architect |
| R2 (Codex) | `1640ffec` | `codex/r2-studio-review` | Codex gpt-6-astra | 2026-10-04 | Fable-architect |
| P1.7b gameplay hardening (metadata) | `809f70e2` | `worktree-agent-a05edc7a6e52c5648` | Claude Opus | 2026-10-04 | Fable-architect |
| R2-B (Codex) | `80966687` | `codex/r2-b` | Codex gpt-6-astra | 2026-10-04 | Fable-architect |
| R2-F (Codex) | `d0f08805` | `codex/r2-f` | Codex gpt-6-astra | 2026-10-04 | Fable-architect |
| R2-G-host (Codex) | `bae9369f` | `codex/r2-g-host` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P1.7c (Codex) | `c10ea81f` | `codex/p1.7c` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-A + R2-int1 (Codex) | `862f08d8` | `codex/r2-a` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P4.1 clean-proof (Codex) | `eb1c7bba` | `codex/p4.1` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-E (Codex) | `27b497ae` | `codex/r2-e` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-C (Codex) | `e27d4b42` | `codex/r2-c` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-D (Codex) | `3b06d950` | `codex/r2-d` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-G (Codex) | `323898fc` | `codex/r2-g` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-B2 (Codex) | `46357460` | `codex/r2-b2` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-G2 (Codex) | `f9c7dfcf` | `codex/r2-g2` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| APP-1 (Codex) | `e4ffd40f` | `codex/app-1` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-F2 (Codex) | `2ed48b96` | `codex/r2-f2` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| VIEWS-RENAME (Codex) | `436406d3` | `codex/views-rename` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R2-D2 (Codex) | `e8648d8c` | `codex/r2-d2` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| STAGE-INT (Codex) | `22dbd459` | `codex/stage-int` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| CORE-RENAME (Codex) | `5eefd3d4` | `codex/core-rename` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| ADAPT-SPLIT (Codex) | `fd038534` | `codex/adapt-split` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P3.2 (Claude Opus) | `6e8b8385` | `worktree-agent-ab08fa8f6a1843576` | Claude Opus | 2026-10-05 | Fable-architect |
| R3-E (Codex) | `5e5a088e` | `codex/r3-e` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R3-C (Codex) | `63daee97` | `codex/r3-c` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R3-D (Codex) | `c6645212` | `codex/r3-d` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R3-B (Codex) | `3b2fa276` | `codex/r3-b` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R3-A (Codex) | `fcdd4d6d` | `codex/r3-a` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| STAGE-TMP (Codex) | `880d26cf` | `codex/stage-tmp` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| STAGE-TMP2 (Codex) | `1ff39355` | `codex/stage-tmp2` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R3-F (Codex) | `399fc742` | `codex/r3-f` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P3.1 (Claude Opus) | `c2f82451` | `worktree-agent-a86a49b2ba589f359` | Claude Opus | 2026-10-05 | Fable-architect |
| P3.1b (Codex) | `7051e953` | `codex/p3.1b` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P4.2 (Codex) | `c79a3832` | `codex/p4.2` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R4-C (Codex) | `199e7f13` | `codex/r4-c` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R4-A (Codex) | `5930ab0f` | `codex/r4-a` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| R4-B (Codex) | `969024a7` | `codex/r4-b` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P3.1c (Codex) | `1752ca8a` | `codex/p3.1c` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P4.2b (Codex) | `c31d846b` | `codex/p4.2b` | Codex gpt-6-astra | 2026-10-05 | Fable-architect |
| P3.1d (Codex) | `f7878292` | `codex/p3.1d` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |
| P4.2c (Codex) | `c9866292` | `codex/p4.2c` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |
| R5-B (Codex) | `2b48aa0d` | `codex/r5-b` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |
| CORE-PICK (Codex) | `b18245ec` | `codex/core-pick` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |
| R5-C (Codex) | `0f042d87` | `codex/r5-c` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |
| R5-A (Codex) | `40fb91fa` | `codex/r5-a` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |
| P4.2d (Codex) | `d5080459` | `codex/p4.2d` | Codex gpt-6-astra | 2026-10-06 | Fable-architect |

P4.3-final runs on `codex/p4.3-final`, implementer Codex gpt-6-astra, 2026-10-06; merge SHA is pending integration and cannot be invented. Its commits are recorded in [P4.3-final](packets/P4.3-final-docs.md). R6 is outside this as-of snapshot.
