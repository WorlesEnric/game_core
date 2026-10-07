# 09. Observability and evidence

Every agent interaction leaves a durable record on one of three sides, and each path has an acceptance row that exercises it live, so a tuning change can be judged on the same evidence the completion report uses.

| Side | Record | Where |
| --- | --- | --- |
| Companion | SQLite ledger `ledger.db`: `requests`, `attempts` (with `parent_task` for re-asks), `candidates`, `artifacts`, `events`, `media_charges` (by idempotency key), `voice_sessions`, `stage_jobs`, `tool_catalogs`, `resource_owners` | `<node root>/agents/gamecore-studio/state/`, `studio/agent/src/ledger.rs:48-147` |
| Companion | Resource-graph traces of request and candidate states (`gc_*` kinds) through the agent binding | `studio/agent/src/index.rs:1-16,565` |
| Node | Task status, topic records, per-task usage (`etos budget --task <id> --json`, `micro_usd`), provider charges, worker container logs | `etos` CLI and the gateway UI on 127.0.0.1:7400 |
| Editor | Journaled change sets with inverses under `<project>/Studio/History/<yyyy>/<mm>/`, candidate import diagnostics, event cursor under `Library/GameCoreStudio/` | change-set journal, `EtosStudioSession` |
| Verification | 68-row matrix with per-row evidence directories (XML/TRX, captures, ledger deltas, worker traces) | `artifacts/studio/verification/<row>/`, `ROWS.json`, `docs/studio/07-verification-matrix.md` |

Rows that exercise the agent paths, useful as regression probes after a tuning change:

- W-AI-01 to W-AI-07: text workflows (tint, NPC creation, conditional dialogue, HUD binding, quest, reopen consistency, 3D refusal); their drivers live in `games/hollowmere/Assets/Hollowmere/Tests/P3_2/Editor/Workflows.cs` with in-Play effect checks in `WorkflowPlayChecks.cs`.
- W-ETOS-04/05/06/07/09: worker-side query, tray cancel, agent restart with cursor replay, media tariffs and describe, reload mid-task.
- W-VOICE-01: real microphone takes with the destructive-never-submits assertion.
- W-MECH-01 and W-DOC-02: staging, signed verdict, creator admission, undo.
- W-E2E-01: all rows judged at one revision and one installed release (`studio/tools/verify-all.sh`).

Paid usage is bounded per run by the brief and recorded in each packet note; the cumulative live spend across all acceptance runs since P4.2c is under USD 3.

Sources: `studio/agent/src/ledger.rs`, `studio/tools/workflow-p3.2-lib.sh:288-307`, `artifacts/studio/verification/SUMMARY.md`, `docs/studio/12-completion-report.md` §Addendum.
