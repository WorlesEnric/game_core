# GameCore Studio: ETOS integration contract and operational setup

**Status:** contract for implementation (2026-10-04) against etos `6c2c3f4`. Every route and limit below was read
in etos source (citations are etos-relative). The companion agent `gamecore-studio` is the only holder of an etos
*agent* key; Unity holds an *app* key. Nothing here bypasses etos's authority model, and nothing in Unity talks
to a model provider.

## 1. Roles and keys

| Principal | Kind | How it is created | What it may do |
|---|---|---|---|
| `gamecore-studio` | installed agent (Rust process supervised by etosd) | `etos agent install studio/etos/agent` (key issued by etosd, rotated on every install/upgrade; `crates/etnode/src/agents/mod.rs:418-439`, `crates/etapi/src/apps.rs:918-943`) | grants: `logger, query, changes, tasks, files, topics, ops, providers, realtime, proxy, services` |
| `gamecore-unity` | installed app + paired key | `etos app install studio/etos/app` (declares `uses = ["gamecore-studio"]`, `routes = ["proxy","query","changes","entrances"]`), then `etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json` (`crates/etapi/src/apps.rs:483-598`; `uses` only via install, `crates/etcli/src/apps.rs:173-188`) | proxied HTTP/WebSocket to the companion, read-only `query` of its own tables |
| Studio workers | etos container workers | `etos worker create gc-designer --image localhost/gc-designer:current --network allowlist:...` before agent install; the agent manifest's `[[worker]]` then only sets model/instructions/tools (`crates/etagents/src/manifest.rs:150-171`; image/network preserved on install, `agents/mod.rs:224-259`) | run tasks opened by the companion |

The agent process inherits etosd's environment (`crates/etnode/src/agents/process.rs:293-296`), including provider
keys; the companion never logs its environment and never forwards it to Unity.

## 2. Unity ↔ companion protocol (through etos)

Base: `http://127.0.0.1:7410/api/v1/agents/gamecore-studio/http` with `Authorization: Bearer <app key>`; etos adds
`X-Etos-App` and `X-Etos-Proxy-Token`, strips `Authorization`, and pipes bodies and upgrades
(`crates/etagents/src/proxy.rs:172-293`). The companion registers its loopback endpoint with
`PUT /agent/endpoint` on every connect (token and endpoint are in memory and reset on etosd restart,
`crates/etagents/src/channel.rs:153-158`) and verifies `X-Etos-Proxy-Token` on every request. WebSocket uses a
ticket: `POST /tickets {path}` → `?etos_ticket=` (single use, 30 s, `crates/etapi/src/tickets.rs:107-139`).

| Method + path (relative to the base) | Purpose | Notes |
|---|---|---|
| `GET /v1/hello` | version, capabilities, provider status (`image`, `tts`, `voice`, `3d` each `live\|not_configured\|blocked`) | polled on Studio open |
| `POST /v1/requests` | submit an `EditRequest {changeSetId, intent, selection, contextSlice, toolCatalogRevision, worker, attachments[]}` | idempotent on `changeSetId`; returns `{requestId, taskId?, state}` |
| `GET /v1/requests/{id}` | state, task status, outcome | mirrors etos `TaskInfo.status` verbatim plus companion states |
| `POST /v1/requests/{id}/cancel` | cancel | calls `POST /tasks/{task}/cancel`; result is the etos status |
| `GET /v1/requests?after=` | list for recovery after reload | durable ledger |
| `GET /v1/candidates/{id}` | the validated change set and artifact manifest | artifacts referenced by sha256 |
| `GET /v1/artifacts/{sha256}` | bytes | served from the content store, digest verified on write |
| `POST /v1/index/delta` | semantic index delta (nodes, edges, removals, revision) | companion logs `gc_*` traces |
| `POST /v1/ops/generate` | direct media op for a tool (`asset.generate`): `{op: image\|tts\|3d, spec, max_cost_usd, changeSetId}` | runs `POST /ops/generate.image` etc.; returns artifact refs |
| `WS /v1/events?after=` | request/task/candidate/voice events, ordered, with cursor | reconnect with `after` |
| `WS /v1/voice` | duplex: client → `{type:"audio", seq, pcm16 base64}` (≤ 24 KiB raw = 32 KiB base64 per frame) / `{type:"stop"}`; server → transcript revisions (`role` always `user`; `final` only on `done`), speech boundaries, errors | one session per Studio instance |
| `POST /v1/stage` / `GET /v1/stage/{job}` | stage a mechanism package; verdict | §6 |

Error bodies are etos-shaped `{code, message, hint}`; etos refusal codes pass through unchanged
(`not_configured`, `outcome_unknown`, `request_rejected`, `budget_exhausted`, `too_large`, `agent_starting`,
`forbidden`, …). The companion adds `candidate_invalid`, `stale_context`, `stage_failed`, `ledger_conflict` and the
transport-level `bad_request`, `not_found`, `internal`, `transport`, `protocol`, `invalid`, `backpressure`
(the last five are the SDK's own error kinds, passed through by name). Request outcome codes shown in the tray are
`candidate`, `task_failed`, `waiting`, `needs_clarification`, `cancelled`, `unresolved`. `/v1/hello` reports providers
`image`, `tts`, `voice`, `3d`, `describe` each as `live | not_configured | blocked | unknown`.

## 3. Task lifecycle mapping

| Step | etos call (agent key) | Citation |
|---|---|---|
| Upload context + attachments | `POST /files?name=&media_type=` (≤ 64 MiB each) → `FileInfo{id,digest}` | `crates/etapi/src/http.rs:109-112,676-695` |
| Open task | `POST /tasks {worker, text, topic, inputs:[fileIds], id: changeSetId}`; the topic is `#agent/gamecore-studio/cs-<ulid>` (lowercase ULID without the `cs_` prefix; etos topic segments are lowercase) | `crates/etagents/src/wire.rs:110-126`; `tasks.rs:103-131` |
| Follow | `GET /topics/agent/gamecore-studio/cs-<id>/records?after=&wait_ms=60000` (≤ 1000 records; progress records, final `done\|failed` record carries `refs`) | `crates/etagents/src/topics.rs:166-217`; `crates/etagent/src/controller.rs:556-581` |
| Status | `GET /tasks/{id}` → `queued\|starting\|running\|waiting\|done\|failed\|cancelled\|unknown` | `wire.rs:129-150` |
| Cancel | `POST /tasks/{id}/cancel` (no-op if ended) | `tasks.rs:249-261`; `crates/etnode/src/node.rs:784-812` |
| Results | worker writes `/outputs/changeset.json` + artifacts; at close ≤ 64 files become pinned references shared into the origin topic; companion `GET /files/{ref}` and verifies sha256 against `changeset.json` | `crates/etnode/src/endpoint.rs:903-958`; `crates/etagents/src/files.rs:1-5,116-126` |
| Lost response | companion ledger row holds `changeSetId → taskId`; on restart it re-reads `GET /tasks/{id}` and the topic from its saved cursor; `POST /tasks` with the same `id` returns the same task, never a second one | `tasks.rs:103-113` |
| Budget stop | etos leaves the task `waiting`; the companion reports `waiting(budget)` from the record text and never retries by itself | `crates/etagent/src/controller.rs:392-398` |

Outcome vocabulary shown to the user is exactly etos's plus the companion's candidate states; "unresolved" is the
UI label for etos `unknown` and `outcome_unknown`, never a success.

## 4. Workers and their contracts

| Worker | Image | Network | Model alias | Used for | Inputs (`/inputs`) | Output |
|---|---|---|---|---|---|---|
| `gc-designer` | `localhost/gc-designer:current` (python3, jq, imagemagick) | `allowlist:` (none needed; ops run on the node) | `default` → `echo/claude-opus-5-5` | configure/compose change sets: entities, dialogue, quests, rules, UI bindings, placement; asset generation via `etos generate image`, `etos tts` | `request.md`, `selection.json`, `index-slice.json`, `tool-catalog.json`, `frame.png` | `/outputs/changeset.json` (+ artifacts) |
| `gc-mechanic` | `localhost/gc-mechanic:current` (.NET 8 SDK, prewarmed NuGet, GameCore rules DLLs) | `allowlist:` | `default` | new mechanism packages (`mechanism.propose`) with `dotnet test` run inside the container before output | same + `package-template/`, `kernel-contracts.md` | `/outputs/package/**`, `/outputs/proposal.json` |

Worker instructions (`studio/etos/agent/workers/*.md`) specify: read the tool catalog first; produce only
operations in the catalog; never invent object ids; cite the `indexRevision`; stop and return `status: needs-clarification`
with at most one question when two interpretations differ materially; never claim an asset you did not write to
`/outputs`. The companion rejects a change set that violates the schema **or the tool catalog** (unknown tool, missing required
args, disallowed target kind, candidate-mode fields) and sends one re-ask as a new task with `parent`.

## 5. Media and voice

| Capability | Provider config (`ops.toml`) | etos op | Live status (2026-10-04) |
|---|---|---|---|
| Image generation | `family="image" kind="openai-images" base_url="https://api.echo-coding.com/v1" credential="env:ECHO_API_KEY" model="gpt-image-2" options={quality="low"}` | `POST /ops/generate.image` (blocks until done or `max_wait`; `crates/etops/src/service.rs:465-483`) | live (verified by direct call) |
| Describe (vision) | `family="describe" kind="chat"` on Echo `gpt-5.6-sol` | `POST /ops/describe {input: ref}` | configured, to verify in W0 |
| TTS (voice lines) | **new kind** `bailian-tts` (`family="tts"`, DashScope `multimodal-generation` with `qwen3-tts-flash`, downloads the returned wav URL) | `POST /ops/tts {text, voice}` | requires the SADR-005 extension; DashScope endpoint verified live |
| Voice input | `[[realtime]] name="studio-voice" kind="bailian-omni" model="qwen3-omni-flash-realtime" credential="env:BAILIAN_API_KEY" turn_detection="semantic_vad"` | `GET /realtime/connect?provider=studio-voice`, `RealtimeConfig{audio_format:"pcm16", sample_rate_hz:24000}`, never `response_create` (transcript only) | adapter present; verify live in W0 |
| 3D generation | `family="3d" kind="predictions"` | `POST /ops/generate.3d` | **blocked**: no provider credential; UI shows `not_configured` |
| File transcription | not used (voice is realtime) | — | — |

SDK: `Ops::generate` used to post `generate` instead of `generate.image`; fixed in the pinned etos `278ef9c`
(`Ops::generate(family, input)` posts `generate.<family>`), and the companion now uses it (P0.5).

Operation timeout: the companion runs each media op on its own client timeout, `ops_timeout_secs` (default 300 s,
companion `config.toml`), because `generate.image` blocks until done and outlives the SDK's 30 s default. It **holds**
`POST /v1/ops/generate` until the node answers. Past the timeout it answers 504 `transport`, with the op's idempotency
`key` in `hint` and in `data.key`. The client recovers by resending the identical request: same spec and changeSetId
give the same key, and etops answers a known key with the finished result instead of generating again. The etos proxy
between Unity and the companion must allow a request this long.

`max_cost_usd` goes only to `generate.image`, `tts` and `generate.3d`. etops' `describe` input has no such field and
refuses unknown fields, so the companion omits it and refuses `describe` itself when the ceiling is 0 (P0.5).

Partial speech: transcript events carry `revision` and `done`; only `done=true` text is placed in the prompt box.
No voice utterance triggers a tool directly.

## 6. Staging (code admission)

`POST /v1/stage {changeSetId, packageRef}`: the companion exports the package into `studio/stage/slots/<n>/Packages/<name>`
(a copy of the game project made by `studio/stage/make-slot.sh`), then runs `dotnet test` on `Rules/` and
`Unity -batchmode -nographics -projectPath <slot> -executeMethod GameCore.Studio.Stage.Entry.Run -quit` with a
10-minute watchdog (the Editor hang documented in `docs/operator/editor-hang.md` is retried once). Verdicts are
`{ok, compile:{errors[]}, tests:{passed,failed,names[]}, forbidden:[...], durationMs}` stored in the ledger and
shown in Unity before *Admit*. Staging never shares `Library/` with the live project.

## 7. Resource Graph publication

Binding `gamecore-studio` (declared by the companion with the SDK logger; digest computed by the SDK):

| Kind | Key | Props | Links |
|---|---|---|---|
| `gc_project` | project | name, unity, kernel_tag, index_revision | |
| `gc_entity` | authoringId | name, type, region, definition, fields json | region, definition |
| `gc_definition` | name@revision | type, fields json, asset path | |
| `gc_dialogue_node` | graph/node | speaker, text, conditions json, consequences json | graph, speaker |
| `gc_quest` | quest id | stages json, status schema | |
| `gc_rule` | rule id | trigger, conditions, actions | |
| `gc_region` | region id | scene, portals json | |
| `gc_changeset` | changeSetId | state, intent, ops count, outcome json | task |
| `gc_task` | etos task id | worker, status, usage | changeset |
| `gc_asset` | sha256 | name, media_type, role, producer json | changeset |

Workers read `/etos/rg` (bounded view: 200 per kind, 7 days, `crates/etrg/src/view/mod.rs:40-61`) and may
`etos query`. Index deltas are logged on every Studio index revision, coalesced to at most one batch per second.

## 8. Operational setup (host)

All files under `studio/etos/`; `install.sh` is idempotent and prints what it changed.

1. **Build etos** at the pinned commit (`etos.lock`: commit `6c2c3f4` + the SADR-005 patch commit, sha256 of the
   three binaries): `cargo build --release -p etnode -p etcli`, the static musl `etos` in an Alpine container
   (`docs/operator.md:19-29`), installed to `~/.local/opt/etos/bin`.
2. **Init** `~/.local/share/etos` with `etosd init --name studio --owner <user>` and write the templated
   `etos.toml` (Docker runtime, `[api] listen="127.0.0.1:7410"`, `[broker] listen="172.17.0.1:7411"`,
   `authority="172.17.0.1:7411"`; Linux needs the explicit authority because `host.docker.internal` is not
   added, `crates/etnode/src/daemon.rs:283`), `models.toml` (Echo endpoint with `env:ECHO_API_KEY`; models
   `echo/claude-opus-5-5` alias `default`, `echo/gpt-6-sol` alias `fast`), `ops.toml` (§5).
3. **Images**: build `localhost/etos-default:latest` from `image/`, then `studio/images/gc-designer` and
   `gc-mechanic` (Debian base, no etos layer; the node adds it).
4. **Workers**: `etos worker create gc-designer --image … --network allowlist:` and `gc-mechanic` likewise;
   `etos grant gc-mechanic credential 172.17.0.1 --source file:… --scheme bearer` only if the staging HTTP
   endpoint is enabled for workers (not in V1 of this product; staging is companion-local).
5. **Agent**: `cargo build --release` in `studio/agent`, `etos agent install studio/etos/agent --link` (manifest
   `[process] command = "../../agent/target/release/gamecore-studio"`), `etos agent list` shows `ready`.
6. **App**: `etos app install studio/etos/app` then `etos app pair gamecore-unity --approve --out ~/.config/gamecore-studio/app-key.json`.
7. **Service**: a user systemd unit `etosd.service` (`ExecStart=… etosd run`, `Environment` from
   `~/.config/gamecore-studio/providers.env`, mode 0600, never in the repo), enabled with `loginctl enable-linger`.
8. **Verify**: `studio/etos/verify.sh` runs: hello through the proxy with the app key; one `generate.image`;
   one `tts`; a 3-second realtime session with a synthetic tone (expects `ready` and `speech_*` or a clean
   close); one designer task that returns a schema-valid empty change set. The transcript is saved to
   `artifacts/studio/environment/etos-verify-<date>.md`.

Secrets: provider keys are read by etosd from its environment file; the Unity app key file is outside the repo;
`UserSettings/GameCoreStudio.json` holds only the file path and the base URL. Logs redact `etk_` and `ett_` tokens.

## 9. What is not done through etos (and why)

- Picking, previews, undo and local application: editor-local, no model (D2).
- Staging compile/tests: run by the companion on the host, because they need Unity and the game project; etos
  workers only produce the package.
- Player builds and launches: Unity build pipeline on the host.
