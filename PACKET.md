# P3.2 ai-workflows: packet note

Owner: Claude Opus 5.5. Branch `worktree-agent-ab08fa8f6a1843576`. Runs on the pre-P1.7b base were made from branch
`p32-pre17b`; all of their evidence is merged into this branch. Date: 2026-10-05.
Evidence index, timings, revisions and costs: [artifacts/studio/workflows/P3.2/README.md](artifacts/studio/workflows/P3.2/README.md).

## What ran

Each workflow drove the real Studio in an interactive Unity Editor on host display :1 against the live etos node and
the `gamecore-studio` companion (gc-designer / gc-mechanic). Every run was screen-recorded and keyframed, its usage
collected from `etos budget --task`, and it was scanned for credentials. Runs made before the companion reinstall are
kept, and the reruns on main 4635746 (R2-B2) sit alongside them.

| Brief item | Driver | Runs (attempt 1 / retry) | Outcome |
|---|---|---|---|
| W-AI-01 green robe (texture → material, undo) | `workflow-p3.2-robe.sh` | robe-055032Z / robe2-070105Z (+ robe2-064834Z blocked by the old companion) | fail: the texture is generated and undone, but the material is never updated (D4, D5, D9) |
| W-AI-02 ferryman at a point-at location | `workflow-p3.2-text.sh` | text-052459Z / text2-061054Z (+ 053552Z on a stale index; 060332Z aborted) | partial: 6-op NPC applied and undone at edit time; Play not exercised |
| W-AI-03 Odd line after the shrine is lit | `workflow-p3.2-narrative.sh` | narrative-072920Z | blocked: worker `needs_clarification` (catalog gap, D10) |
| W-AI-04 HUD label rebound to the stage name | same | narrative-072920Z | blocked: worker `needs_clarification` (no binding source, D10) |
| W-AI-05 lantern quest needs two oil flasks | same | narrative-072920Z | blocked: `candidate_invalid` ScopeNotAllowed (D5) |
| W-AI-06 undo/redo, close/reopen, consistency | `workflow-p3.2-narrative.sh --only persist/reopen` | persist-080347Z (attempt 1 hung, D18) / reopen-083218Z | partial: consistent for the journaled media change sets; no applied agent edit existed to carry over |
| Text → typed edit; clarification round trip | `workflow-p3.2-text.sh` | text2-061054Z | clarification round trip recorded (`needs_clarification` → tray answer → follow-up); typed move/patrol `candidate_invalid` (worker targeted npc.behaviour) |
| Asset generation → import → assign, plus TTS | robe / persist / honesty | robe2-070105Z, persist-080347Z, honesty-095503Z | generate.image, import, bind as Sprite, TTS line and `history.undo` all work (bind needs `spriteImportMode`, D8) |
| Inspect / explain | `workflow-p3.2-honesty.sh` | honesty-095503Z (rerun on 4635746) | pass: worker read-only candidate (interaction.explain + query.impact), plus local interaction.explain / logic.whyNot / logic.explain / quest.inspectRuntime |
| Failure honesty: cancel mid-run | same | honesty-095503Z | pass: `cancelled`, ack 224 ms, no candidate |
| Failure honesty: image with max_cost_usd 0.001 | same | honesty-095503Z | fail: generated anyway; the ceiling does not bind without prices (D14) |
| Batch / multi-target with conflict | `workflow-p3.2-batch.sh` | batch-100136Z / batch2-104639Z | partial: correct ring candidate; AllOrNothing → Conflict{expected, actual}; Rebase + BestEffort still rejected (D20) |
| Voice push-to-talk; W-VOICE-01 | `workflow-p3.2-voice.sh` | voice-105318Z / voice2-112002Z | W-VOICE-01 partial (nothing sent, final transcript shown, no partial text); voice → edit blocked (D19, D22) |
| Mechanism propose → stage → admit (fallback sample) | `workflow-p3.2-mechanism.sh` | MECH_RUNS | MECH_OUTCOME |
| Headless EditMode variants | `workflow-p3.2-harness.sh` | HARNESS_RUN | HARNESS_OUTCOME |

## How to re-run

From the worktree on the Mac (nothing builds on the Mac): `studio/tools/workflow-p3.2-<name>.sh [--attempt 1|2]`.
Each driver syncs to the host (`sync-to-host.sh p3.2`), takes one Unity slot under the shared lock, and removes the
Studio index cache (D1) unless `WORKFLOW_KEEP_INDEX_CACHE=1` is set. It then opens an interactive Editor on :1, runs
the step machine `Hollowmere.P3_2.Workflows`, records with ffmpeg x11grab, collects usage, scans for
`etk_|ett_|sk-|Bearer`, and rsyncs the run to `artifacts/studio/workflows/P3.2/runs/<workflow>-<stamp>/`.
`studio/tools/workflow-p3.2-summary.sh` rebuilds `summary.json`. Before you run anything:

- The host needs main ≥ 4635746 **and** a companion installed from de2d959 or later (D3, D3b).
- The key file comes from `GAMECORE_ETOS_KEY_FILE`; it is never read or printed by these tools.

## Counts

- Workflow runs: 18 interactive plus 1 headless harness (see README).
- Agent tasks: TASKS_COUNT. Media ops: IMAGES images, TTS_COUNT TTS lines, VOICE_COUNT realtime sessions, 1 describe.
- Applied-then-undone change sets with both journal entries kept: ferryman (6 ops); manual crate move; robe texture;
  icon import plus Sprite bind; TTS lines; budget-probe image; persist/reopen undo → redo → undo.

## B-AGENT-UX

- **State visible ≤ 1 s:** over budget. Across all runs n = 87 transitions, p95 4886 ms, max 10130 ms.
  requested/running transitions are ≤ ~230 ms. done/candidate transitions arrive 1.2–10 s after `updatedAt` under host
  load average ~18 (D13). The period-D runs alone (4635746, lighter load) stay at p95 150–207 ms, except a
  clarification round trip at 4.9 s.
- **Cancel acknowledged ≤ 2 s:** 224 ms, within budget.
- **Reconnect ≤ 5 s:** not exercised.

## Cost

The node reports tokens only (`micro_usd` 0, D14). The estimate uses input $2.5/M, output $15/M, image $0.04,
TTS $0.002, voice session $0.01 and describe $0.02: **TOTAL_LINE**. That is under the 20 USD cap at the mid
estimate. Per-run figures are in `summary.json`.

## Defects for the integrator (no Studio or gameplay code was changed by P3.2)

| # | Defect | Repro | Location |
|---|---|---|---|
| D1 | A stale semantic index cache is trusted wholesale at start-up: new definitions (npc.definition) are missing from slices, so the worker asks for them or the validator reports MissingPrerequisite | Run text2 with `WORKFLOW_KEEP_INDEX_CACHE=1` after definitions changed on disk: `runs/text2-20261005T053552Z` | `Packages/com.gamecore.studio.core/Editor/Index/SemanticIndexService.cs:682` (LoadCache, called from `StudioRuntime.cs:79`); surfaces at `ChangeSetValidator.cs:540/568` |
| D2 | `'authoringId'` unknown value type | fixed on main in 10e5ef6 | `AuthoringIdentity.cs` |
| D3 | A companion installed before P1.7b rejects the P1.7b tool catalog (`structural`): every agent request 400 | `runs/robe2-20261005T064834Z` | resolved by the reinstall from de2d959 (08:47–08:51Z, `artifacts/studio/workflows/P3.2/companion-install-de2d959.log`) |
| D3b | The new companion requires `X-GameCore-Project`; Unity clones older than 4635746 (P3.1's included at the time) get 400 on every call, media included | `runs/honesty-20261005T093618Z` | companion/client version skew; `EtosClientOptions.ProjectId` exists only from R2-B2 |
| D4 | `entity.applyOverride` tint is not validated against the instance contract before apply: `#5C9964FF` passes validation (`#rrggbb(aa)`), then fails at apply with GP-ENT-004 "tint is #rrggbb" and rolls back | `runs/robe-20261005T055032Z/robe` | `ChangeSetValidator.cs:704` / `ValueCodec.cs:806` accept 8 digits; `Packages/com.gamecore.gameplay.entities/Runtime/EntityAuthoring.cs` (tint #rrggbb) |
| D5 | A target with no `scope` is ScopeNotAllowed, even when the tool and type allow exactly one scope; the validator could infer it, and the companion's re-ask did not repair it | `runs/robe2-20261005T070105Z/robe2`, `runs/narrative-20261005T072920Z/quest` | `Packages/com.gamecore.studio.core/Runtime/Model/ChangeSetValidator.cs:440-451` |
| D7 | `dialogue.generateVoice` returns NotConfigured, "no media generation gateway is configured", although the etos client is live | robe2 probe | `Packages/com.gamecore.gameplay.contracts/Runtime/Narrative/NarrativeSeams.cs:303` (the gateway is never registered) |
| D8 | `bind` with importer `{textureType: Sprite}` alone does not produce a Sprite; `spriteImportMode: Single` is needed, and the import policy allowlist does not list `spriteImportMode` | `runs/robe2-20261005T070105Z` ("not a Sprite") vs `runs/persist-20261005T080347Z` (Applied with spriteImportMode) | `Packages/com.gamecore.studio.core/Editor/Tools/BuiltIn/MediaImportPolicy.cs:50`, `ConfigureTools.cs:445` |
| D9 | No catalog tool assigns a texture to a material / renderer, so W-AI-01's "material updated" cannot be expressed | robe runs: the worker falls back to a tint | tool catalog |
| D10 | Catalog gaps reported by the worker: (a) a dialogue condition cannot reference a fact created in the same change set; (b) no binding source exposes the quest stage title to HUD bindings (only `vm:hud.ObjectiveText`) | `runs/narrative-20261005T072920Z/odd-line`, `/hud` (`outcome.json` holds the exact text) | tool catalog / HUD view-model |
| D12 | `OpenStudio` window rects are ignored by the window manager on :1 (Relayout re-applies them) | any run's `relayout` step | Studio layout |
| D13 | Candidate/done transitions become visible 1.2–10 s after the companion's `updatedAt` under host load | `summary.json` lag figures | gateway polling/streaming path |
| D14 | No prices configured: `max_cost_usd` does not bind; a 0.001 USD image is generated | `runs/honesty-20261005T095503Z/budget` | node `models.toml`/`ops.toml` |
| D15 | The installed companion binary is a symlink into `~/wkspace/gc-studio/main`: a checkout there changes the live agent | `ls -l` of the installed agent | `studio/etos/install.sh` |
| D16 | The prompt bar has no worker switch for gc-mechanic (the drivers pass the worker programmatically) | — | `Packages/com.gamecore.studio.ui/Editor/Prompt/PromptBar.cs` |
| D17 | `unity-compile.sh` on the Mac's bash 3 fails (`required_tests[@]: unbound variable`), and a compile FAIL prints no CS errors (they are only in `Library/Bee/tundra.log.json`) | run it on the Mac | `studio/tools/unity-compile.sh:89` |
| D18 | Interactive Editor hang under host load (no log output for 900 s; killed) | `runs/persist-20261005T080347Z` attempt 1 (`editor-a1.log`) | — |
| D19 | A second push-to-talk on the same prompt bar never connects. `EtosVoiceSession._channel` is cleared only on a connect failure, so after the first take `StartAsync` returns at once; the bar shows "listening…" and no transcript arrives | `runs/voice-20261005T105318Z` (second take), `runs/voice2-20261005T112002Z` (second take); one "capturing" log line per Editor | `Packages/com.gamecore.studio.etos/Editor/EtosVoiceSession.cs:73-76` (early return) with `:97` (only reset); `PromptBar.cs:277` reuses the session |
| D20 | Rebase does not refresh the change set's `baseVersions`, so after Rebase every op is still refused under BestEffort: Apply rejects all ops on the first stale base version before per-op policy runs | `runs/batch2-20261005T104639Z/ring` (`rebased.json`, `apply-best-effort-report.json`) | `Packages/com.gamecore.studio.core/Editor/Engine/ChangeSetEngine.cs:147` (Rebase) and `:548-560` (base-version refusal of all ops) |
| D21 | Request context carries only the selected objects; a referenced landmark (the Village Well) is not in scene-context.json or the slice, so the worker asks for its position | `runs/batch-20261005T100136Z/ring` | Studio request context builder |
| D22 | A realtime voice take on a fresh session returned no user transcript for the TTS line "Move the well one metre to the east." (24 kHz mono PCM, played into the virtual mic); the session closed "client closed" on release. The destructive line did transcribe in attempt 1. Cause not isolated | `runs/voice2-20261005T112002Z` (`voice/move-transcript.json`, `editor-excerpt.log`) | etos realtime / `EtosVoiceSession` |
MECH_DEFECT_ROWS
Worker errors (not Studio defects), recorded as-is: npc.setPatrol aimed at an npc.behaviour instead of the
npc.definition (text2-061054Z); 8-digit tint (robe-055032Z).

## Left open

- W-AI-02 in Play, W-AI-03 and W-AI-05 in Play: blocked by D5 and D10; not attempted with hand-edited candidates.
- W-AI-01's material update needs D9 (plus D4/D5).
- The voice → edit path needs D19 and D22.
- B-AGENT-UX reconnect ≤ 5 s was not exercised. The visible-lag budget is over at p95 under host load (D13).
- 07 rows updated: W-AI-01..06 and W-VOICE-01 (status/evidence columns only). B-AGENT-UX has no status column, so it
  is reported in the README.
