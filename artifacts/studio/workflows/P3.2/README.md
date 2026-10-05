# P3.2 ai-workflows: live AI-assisted creation workflows

Owner: P3.2 (Claude Opus 5.5). Host `myubuntu`, display `:1`, Unity 6000.0.75f1, etos node `127.0.0.1:7410`,
companion agent `gamecore-studio` (workers gc-designer and gc-mechanic). Date: 2026-10-05 (UTC).
Every run below drove the real Studio in an interactive Editor through the real node and companion. Nothing is mocked.
Candidates were applied only where acceptance requires it; every applied change was undone with `history.undo`, and
both journal entries are kept in the run folder.

## Revisions and the companion reinstall (the resolution of defect D3)

Each run folder has a `run.json`. Its `revision` field is the P3.2 branch commit that was synced to the host. The
"main base" column below gives the main commit that branch commit had merged (`git merge-base <rev> main`).

| Period | Main base | Companion | Runs |
|---|---|---|---|
| A: before P1.7b | 30844cf / ddd22fe (via `ff43da5`, branch `p32-pre17b`) | installed before P1.7b | text, text2, robe, robe2-070105Z, narrative, persist, reopen |
| B: P1.7b, old companion | 4434e6a (`5a65183`) | installed before P1.7b | robe2-064834Z: every agent request **400** |
| C: R2-E, new companion | fd8ae9d (`a212892`) | reinstalled from de2d959 | honesty-093618Z: every call **400** |
| D: R2-B2 (current) | **4635746** (`f01f65f`, `fc1e0b0`, `53df3e5`) | reinstalled from de2d959 | honesty-095503Z, batch, batch2, voice, voice2, mech-*, harness |

**Companion reinstall (resolves D3).** The coordinator asked for the reinstall. Before running it I checked
`pgrep -f 'live-etos-tests|gc-studio/p2|gc-studio/p3.1'`; nothing was stopping or restarting the node. The command was
`studio/etos/install.sh --skip-images`, run from `~/wkspace/gc-studio/main` at de2d959, from **08:47:06Z to 08:51:30Z**,
exit 0. The transcript is [companion-install-de2d959.log](companion-install-de2d959.log), scanned clean.

- **Symptom before the reinstall.** On main ≥ P1.7b, the installed companion was older than the P1.7b tool catalog
  schema. It answered every agent request with HTTP 400 (the catalog's `structural` field was rejected); see
  `runs/robe2-20261005T064834Z`.
- **Symptom after the reinstall, on an old Unity client.** The new companion scopes everything by (app, project). Any
  Unity clone older than R2-B2 (46357460) does not send `X-GameCore-Project`, so every call, media included, returned
  `400 X-GameCore-Project must be this project's stable SHA-256 identity`; see `runs/honesty-20261005T093618Z`.
- **After the reinstall, on 46357460+.** Everything works; see period D.
- **Risk for the integrator (D15).** The installed agent binary is a symlink into `~/wkspace/gc-studio/main`.

Recordings made before the reinstall are kept. The reruns on 4635746+ sit alongside them; honesty-095503Z is the rerun
of an agent workflow on current main.

## Index

Result keys: **pass**, **partial** (part of the acceptance is met; the gap is named), **fail** (the worker's output
was wrong or the Studio rejected it), **blocked** (a Studio or catalog defect prevents the row). Cost is estimated: the
node reports tokens but no prices (D14), so cost uses the assumed rates below.

| Workflow / row | Run (folder under `runs/`) | Main base | Result | Task ids | Est. USD | Key timings | Defects |
|---|---|---|---|---|---|---|---|
| Text → typed edit, clarification (W-AI-02 prep) | text-20261005T052459Z | 30844cf | fail: both requests `needs_clarification` (the stale index had no npc.definition); point-at missed | t06b09f05…, t525d2057…, t1d062d1a…, tbe66ce5f… | 1.18 | accepted 359–862 ms | D1, D12 |
| Text, retry, stale index | text2-20261005T053552Z | 30844cf | fail: clarify2 and ferryman2 `needs_clarification`; move-patrol2 `candidate_invalid` (MissingPrerequisite) | td6670495…, t0e47053e…, td6d27505…, tef89e4b1…, t6fc7ab0f… | 1.65 | | D1 |
| Text, retry, aborted after the index probe | text2-20261005T060332Z | ddd22fe | aborted by me (index probe only); t69738f23… cancelled with `etos task cancel` | t9a55f92f…, t69738f23… | 0.48 | | |
| **W-AI-02 ferryman** + clarification round trip + move/patrol | text2-20261005T061054Z | ddd22fe | **partial, ferryman**: 6-op candidate (entity, patrol behaviour, dialogue, NPC, roster, instance on the NavMesh at (4.0, 0.083, 1.1)), applied, then undone. Play was not exercised. clarify2 is a `needs_clarification` round trip (the worker asked for the well's coordinates). move-patrol2 is `candidate_invalid`: the worker targeted npc.behaviour | t049950b2…, t10cfcf00… (ferryman); t2ffc0841…, t35d405ba… (clarify); tb1fea55c… (move) | 2.02 | submit→candidate 611.6 s; preview 125 ms; apply 675.5 ms (6 ops); undo 519.2 ms | worker error |
| **W-AI-01 green robe**, attempt 1 | robe-20261005T055032Z | 30844cf | fail: candidate `entity.applyOverride` with tint `#5C9964FF` failed at apply (GP-ENT-004, tint is #rrggbb) and was rolled back. Robe texture generated. Icon assign refused (Texture2D, not a Sprite). TTS ok | t0d55d526…, t8a544b1e… | 0.70 | submit→candidate 135 s; preview 121 ms | D4, D8, D9 |
| W-AI-01, retry on P1.7b | robe2-20261005T064834Z | 4434e6a | blocked: agent request 400 (old companion schema). Media ops ok | none | 0.08 | | D3 |
| **W-AI-01, retry** | robe2-20261005T070105Z | ddd22fe | fail: `candidate_invalid`. Tint #228B22 was right but the target had no scope (ScopeNotAllowed). Robe texture generated and undone; bind refused (not a Sprite); TTS undone | t73ad0f6f… | 0.28 | submit→candidate 106 s | D5, D8, D9 |
| **W-AI-03 Odd line / W-AI-04 HUD / W-AI-05 lantern** | narrative-20261005T072920Z | ddd22fe | blocked. odd-line `needs_clarification`: "The available operations cannot reference a newly created shrine_lit fact from a dialogue condition in the same change set…". hud `needs_clarification`: no binding source for the stage title (only vm:hud.ObjectiveText). quest `candidate_invalid`: create OilFlask plus quest.addObjective Collect 2, ScopeNotAllowed on op2 | te4674ea1…, tb2c186da…, t28c03fc3…, t3d3b31b2…, td662950c…, tdf99497e… | 2.65 | accepted 1.6–17.9 s (host load ~18) | D5, D10 |
| W-AI-06 persist (asset gen → import → assign, TTS, save) | persist-20261005T080347Z | ddd22fe | pass on attempt 2 (attempt 1: the Editor hung and was killed after 900 s of silence). Icon generated, **bind Applied** (Sprite), TTS applied, saved | none (media ops) | 0.04 | | D18 |
| **W-AI-06 close/reopen + undo/redo** | reopen-20261005T083218Z | ddd22fe | **pass for the journaled media change sets**. After the restart the journal lists all 3 as Applied with matching hashes; undo → redo → undo all ok; back to the pre-edit hashes | none | 0.00 | | |
| Inspect/explain, cancel, budget ceiling (old client) | honesty-20261005T093618Z | fd8ae9d | blocked: every call 400 (`X-GameCore-Project`); the local explain tools still answered | none | 0.00 | | D3b |
| **Inspect/explain + failure honesty (rerun on 4635746+)** | honesty-20261005T095503Z | **4635746** | **pass**. Explain: candidate with 2 read-only ops (interaction.explain, query.impact), rejected after preview, plus local answers (interaction.explain, logic.whyNot, logic.explain, quest.inspectRuntime, query.impact/references). Cancel mid-run: `cancelled`, no candidate, ack in **224 ms**. Image with max_cost_usd 0.001: **generated anyway** (not binding: D14), described, undone | td5bc9866…, tbedaa9b8… | 0.38 | explain submit→candidate 69.5 s; cancel ack 224 ms; image undo 2563 ms | D14 |
| Batch / multi-target + conflict, attempt 1 | batch-20261005T100136Z | **4635746** | fail: `needs_clarification` twice (the worker asked for the Village Well's position; scene-context.json carries only selected objects) | t3db1a0cc…, t0f61eea3… | 0.76 | | D21 |
| **Batch / multi-target + conflict, retry** | batch2-20261005T104639Z | **4635746** | **partial**. The well was Ctrl-added to the selection, giving a correct 3-op ring candidate (radius 3 about the well). Market Crate was moved by hand, then AllOrNothing apply was Rejected with Conflict{expected, actual} as expected. **Rebase + BestEffort was still Rejected**: Rebase does not refresh the change set's baseVersions. Manual move undone | t5260bdef… | 0.37 | submit→candidate 91.8 s; preview 121.9 ms; reject 4.3 / 5.4 ms; manual undo 35.8 ms | D20 |
| **W-VOICE-01 + voice edit**, attempt 1 | voice-20261005T105318Z | **4635746** | **W-VOICE-01 partial**. "Delete every NPC in the village." spoken with push-to-talk; the final transcript "To lead every N P C in the village." landed in the field; nothing was sent (tray/requests/journal unchanged). No partial-text revision was shown (only "listening…" then the final). Voice edit blocked: the second push-to-talk never connected | none (voice + tts) | 0.02 | | D19 |
| Voice, retry (move take first) | voice2-20261005T112002Z | **4635746** | blocked. The move take on a fresh session produced no transcript (session closed "client closed"); the second take never connected. Nothing sent | none | 0.02 | | D19, D22 |
| **Mechanism via the agent** (pressure plate opens the Causeway Gate) | mech-a-20261005T113219Z | **4635746** | fail: gc-mechanic built the package and proposal (Rules tests 9/9, then 12/12 after the companion re-ask), but the change set was `candidate_invalid` on both attempts. The final diagnostics are `schema_violation` at `/intent` (`description` unexpected; `origin` and `text` required). Stage / admit / Play / undo were not reached. No retry: the stage seam cannot stage it (D23, D24) | t16cb27e2…, t60d38464… | 3.18 | submit→accepted 4.7 s; settled after 1709 s | worker error, D23 |
| Mechanism fallback: P2.4 pressure-plate sample, run 1 | mech-b-20261005T120918Z | **4635746** | blocked (infrastructure): Unity start-up hit `Can't find file /tmp/ilpp.sock-…` (IL post-processor), so no etos gateway was registered; Stage → `stage_service_unavailable` | none | 0.00 | | D18 |
| **Mechanism fallback: sample, rerun** | mech-b-20261005T124205Z | **4635746** | **blocked**: the candidate was retained in the panel and Stage was pressed. The companion answered `not_found: no request cs_01K6RW0MECH00000000000000A [HTTP 404]`: it stages only change sets that are its own ledger requests, and main has no verdict-file path any more, so a sample candidate cannot be staged or admitted | none | 0.00 | stage answer 1.3 s | D23 |
| Headless EditMode variants (H1 typed edit, H2 media gen/bind/TTS/0.001 budget, H3 cancel) | HARNESS_ROW | **4635746** | HARNESS_RESULT | HARNESS_TASKS | HARNESS_USD | | HARNESS_DEFECTS |

`…` abbreviates a 25-character task id. The full ids are in each request folder's `task-ids.txt` and in the run's
`usage.json`.

### Assumed rates (estimates; the node reports `micro_usd: 0`)

Input $2.5/M tokens, output $15/M tokens; image $0.04, TTS line $0.002, realtime voice session $0.01, describe $0.02.
A high estimate uses input $5/M and output $20/M. The totals are in [summary.json](summary.json), regenerated with
`studio/tools/workflow-p3.2-summary.sh`. **Total so far: TOTAL_LINE**

## Rows in 07 (status and evidence only)

| Row | Result | Evidence |
|---|---|---|
| W-AI-01 | fail | The texture is generated and imported and undo restores it, but no candidate updated the material. Attempt 1's tint format failed at apply (D4); the retry was ScopeNotAllowed (D5); the catalog has no texture→material tool (D9). `runs/robe-20261005T055032Z`, `runs/robe2-20261005T070105Z` |
| W-AI-02 | partial | The ferryman (entity, dialogue, patrol, nav-placed instance) was applied and undone at edit time; Play was not exercised. `runs/text2-20261005T061054Z/ferryman2` |
| W-AI-03 | blocked | Catalog gap D10: the worker's `needs_clarification` gives the exact text. `runs/narrative-20261005T072920Z/odd-line` |
| W-AI-04 | blocked | Catalog gap D10: no stage-title binding source. `runs/narrative-20261005T072920Z/hud` |
| W-AI-05 | blocked | ScopeNotAllowed on quest.addObjective (D5); Play not reached. `runs/narrative-20261005T072920Z/quest` |
| W-AI-06 | partial | Undo/redo plus close/reopen are consistent for the journaled media change sets (icon import, Sprite bind, TTS line). W-AI-01..05 produced no applied agent edit to carry over. `runs/persist-20261005T080347Z`, `runs/reopen-20261005T083218Z` |
| W-VOICE-01 | partial | Nothing happens and the final transcript appears. Partial revisions were not visible (no partial text shown). `runs/voice-20261005T105318Z` |

## B-AGENT-UX (no status column in 07 §2; reported here)

The source is the companion event `updatedAt` against Unity's receipt of the RequestView (same host clock), counting
state/taskStatus transitions only. Per-request values are in each folder's `timings.json` and `summary.json`.

| Budget | Measured | Verdict |
|---|---|---|
| Task state change visible in the tray ≤ 1 s | Period D: honesty explain p95 150 ms, cancel 207 ms, batch2 158 ms; batch with a clarification 4886 ms. All runs: n = 87 transitions, **p95 4886 ms, max 10130 ms**. The slow transitions are the done/candidate ones, 1.2–10 s after `updatedAt`, under host load average ~18 (D13). requested/running transitions are ≤ ~230 ms | **over budget** at p95 |
| Cancel acknowledged ≤ 2 s | **224 ms** (honesty-095503Z; `cancelled` 1.5 s after submit) | within |
| WebSocket reconnect ≤ 5 s | not exercised by P3.2 | — |
| (informational) submit → accepted / first event | 629–1541 ms / 140–329 ms in period D; up to 17.9 s / 2.3 s under peak host load | |
| (informational) preview / apply / undo | preview 121–631 ms; ferryman 6-op apply 675.5 ms, undo 519.2 ms; media undo 2.6 s | |

## Re-running

Run everything from the worktree on the Mac. Unity runs on the host under the shared lock.
`studio/tools/workflow-p3.2-lib.sh` syncs the worktree to the host, removes the index cache, starts an interactive
Editor on :1, records it, collects `etos` usage, scans for credentials and copies the run back.

```
studio/tools/workflow-p3.2-text.sh [--attempt 1|2]            # clarification, move+patrol, ferryman (point-at)
studio/tools/workflow-p3.2-robe.sh [--attempt 1|2]            # green robe + asset gen/import/assign + TTS
studio/tools/workflow-p3.2-narrative.sh [--only narrative|persist|reopen] [--persist]
studio/tools/workflow-p3.2-honesty.sh                         # explain, cancel, 0.001 budget image
studio/tools/workflow-p3.2-batch.sh [--attempt 1|2]           # marquee ring + conflict
studio/tools/workflow-p3.2-voice.sh [--attempt 1|2]           # PipeWire virtual mic, push-to-talk
studio/tools/workflow-p3.2-mechanism.sh [--sample] [--skip-agent]
studio/tools/workflow-p3.2-harness.sh                         # headless EditMode H1/H2/H3 (GAMECORE_ETOS_LIVE=1)
studio/tools/workflow-p3.2-summary.sh                         # summary.json + digest
```

The driver code is in `games/hollowmere/Assets/Hollowmere/Tests/P3_2/Editor/` (`Workflows.cs`, `WorkflowSteps.cs`,
`WorkflowRunner.cs`) and the headless variants are in `.../P3_2/EditMode/`. Every run folder holds `run.json`,
`run-log.jsonl` (each step with a caption and a `shot` keyframe), `timeline.jsonl` (companion events), `keyframes/`,
`recording.mp4` (x11grab, all under 1 MB), `usage.json`, `editor-excerpt.log`, and one folder per request
(`request.json`, `prompt.txt`, `candidate.json`, `outcome.json`, `timings.json`, journals, `task-ids.txt`).

Defects (with repro and file:line) are listed in `PACKET.md` at the worktree root.
