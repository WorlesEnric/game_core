# 01. ETOS as the Studio sees it

ETOS is the single runtime owner of agents, tasks, providers and budgets; the Studio adds no second agent runtime and reaches everything through the node's `/api/v1` API.

| ETOS concept | What it is | How the Studio uses it |
| --- | --- | --- |
| Node (`etosd`) | One daemon per host owning a state root, here `~/.local/share/etos-studio` (gateway 127.0.0.1:7400, SDK API 127.0.0.1:7410). | Everything below runs under it; the Studio never starts a competing runtime over the same root. |
| App | A paired client identity with an app key and scoped routes. | `gamecore-unity` is the app; its key lives in `~/.config/gamecore-studio/app-key.json`, never in Unity assets. Routes: `proxy`, `query`, `changes`, `entrances`. |
| Agent | An installed program with grants, supervised by the node, reachable through the proxy. | `gamecore-studio`, the companion, in service form (SADR-053). |
| Worker | A named, persistent, text-only model worker bound to a model alias and an instruction file, run per task in its own container. | `gc-designer` and `gc-mechanic`, both on `echo/gpt-6-sol`, network `offline`. |
| Task | One worker run with inputs, a topic for its records, a status and a budget. | Every creator request becomes exactly one task; its id is the change-set id, so a resend is idempotent. |
| Topic | A durable record stream, long-polled by cursor. | The companion follows `#agent/gamecore-studio/cs-<ulid>` for `working`, `waiting`, `failed`, `done` records. |
| Files | Content-addressed uploads referenced by tasks. | Request text, selection, index slice, tool catalog and attachments go up as files; worker outputs come back as refs. |
| Ops | Provider-backed modality operations (`generate.image`, `tts`, `describe`, `generate.3d`) with priced tariffs and a per-call cost ceiling. | All media generation; 3D answers `not_configured` because no provider is configured (SADR-020). |
| Realtime | A provider-backed bidirectional audio session. | Voice prompting through `studio-voice` (DashScope `qwen3-omni-flash-realtime`). |
| Query and bindings | The node's resource graph: an agent binds record kinds and posts traces; apps query them. | The Editor's semantic index is published as `gc_*` records so a worker can run `etos query` over the project. |
| Proxy | The node forwards `ANY /agents/<agent>/http/<path>` to the agent after authenticating the app and stamping a per-connection proxy token. | The only path between Unity and the companion; the companion verifies the token against its current welcome. |

The authority model follows from this: the app key can only reach the proxy, query, changes and entrances routes; agent-only resources (tasks, files, ops, realtime) are reachable through the companion alone, which holds the agent key the node injected into its process environment and never logs or forwards it.

Sources: `studio/etos/app/app.toml`, `studio/etos/agent/agent.toml`, `studio/etos/etos.toml.tmpl`, `docs/studio/04-etos-integration.md` §1, `../etos/docs/design/design-agent.md` §9 and §11.
