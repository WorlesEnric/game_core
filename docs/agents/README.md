# GameCore Studio agents: design and ETOS integration

This set describes the AI agents that give GameCore Studio its authoring intelligence and how they are integrated with ETOS, so the agents can be tuned without re-deriving the architecture from the code. It covers the installed ETOS agent `gamecore-studio` (the companion), its two workers, the Unity client that talks to it, the modality operations (image, speech, describe, 3D), real-time voice, the semantic index published to ETOS query, the staging lane that admits generated code, and the authority model that bounds all of it.

Everything stated here is taken from this repository at main `5110bf20` (2026-10-08, after R11-A and P4.2l) and from ETOS main `e4067fd`; each document ends with the files that implement it. Out of scope: the GameCore kernel itself, the plugin library's gameplay semantics, and the Hollowmere game content, except where a worker contract depends on them.

| Document | Content |
| --- | --- |
| [01-etos-primer.md](01-etos-primer.md) | ETOS as the Studio sees it: node, app, agent, worker, task, topic, files, ops, realtime, query, proxy; the authority model |
| [02-gamecore-studio-agent.md](02-gamecore-studio-agent.md) | The companion: service form, manifest, release lifecycle, runtime topology |
| [03-workers.md](03-workers.md) | gc-designer and gc-mechanic: roles, model binding, inputs, output contract, validation, rules added by the fix waves |
| [04-request-lifecycle.md](04-request-lifecycle.md) | Prompt to task to candidate to apply/undo; clarification, re-ask, cancel, cursor replay, reload |
| [05-modalities-and-voice.md](05-modalities-and-voice.md) | Image, TTS, describe, 3D with tariffs and budgets; the voice path |
| [06-query-and-index.md](06-query-and-index.md) | Index publication from the Editor and what workers can query |
| [07-staging-lane.md](07-staging-lane.md) | App-origin staging, signed verdicts, creator admission, cancellation |
| [08-security-and-authority.md](08-security-and-authority.md) | Keys, proxy token, scopes, input hygiene, D1/D2, cost, redaction |
| [09-observability-and-evidence.md](09-observability-and-evidence.md) | Ledger, journals, traces, and the verification rows that exercise each path |
| [10-optimization-opportunities.md](10-optimization-opportunities.md) | Ranked levers for agent quality, latency and cost, with the files involved |
| [11-source-map.md](11-source-map.md) | File-level index of every component named above |

Related design documents: `docs/studio/02-architecture.md` (SADR-020, 053, 054, 055), `docs/studio/03-authoring-contracts.md`, `docs/studio/04-etos-integration.md`, `docs/studio/07-verification-matrix.md`, `docs/studio/12-completion-report.md`.
